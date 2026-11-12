using System;
using Godot;

namespace FishingSim.Graphics;

/// <summary>
/// The painted world textures, kept block compressed on disk (user://texcache/*.fstx, zstd): the first start paints
/// and compresses them, later starts only read them. Also remembers each picture's average colour (linear), which
/// the materials use to tint a texture to a map's colours.
/// </summary>
public static class TextureStore
{
    private const uint Magic = 0x58545346;     // "FSTX"

    /// <summary>
    /// A block-compressed image with mipmaps: from the cache, or painted by <paramref name="paint"/> (an RGBA8 image
    /// without mipmaps) and stored. <paramref name="prepare"/> may adjust the mipmaps before compression (leaves keep
    /// their alpha coverage); <paramref name="normalMap"/> renormalises them.
    /// </summary>
    public static (Image Image, Color Mean) Get(string name, int version, BlockCompress.Kind kind, Func<Image> paint,
        bool normalMap = false, Action<Image>? prepare = null)
    {
        bool compress = Fishing.FishAsset.CompressTextures;
        string path = $"user://texcache/{name}_v{version}{(compress ? "" : "_raw")}.fstx";
        if (!_tidiedPngs && !OS.HasEnvironment("FISHSIM_NO_CACHE"))
            Tidy("", "");
        if (Read(path) is { } cached)
            return cached;
        var img = paint();
        if (img.GetFormat() != Image.Format.Rgba8)
            img.Convert(Image.Format.Rgba8);
        img.GenerateMipmaps(normalMap);
        prepare?.Invoke(img);
        var mean = MeanOf(img);
        var result = compress ? BlockCompress.Compress(img, kind) : img;
        if (!OS.HasEnvironment("FISHSIM_NO_CACHE"))
        {
            Write(path, result, mean);
            Tidy(name, path);
        }
        return (result, mean);
    }

    private static bool _tidiedPngs;

    /// <summary>Deletes the older versions of a texture, and once the PNG caches of earlier builds.</summary>
    private static void Tidy(string name, string keep)
    {
        lock (Lock)
        {
            using var dir = DirAccess.Open("user://texcache");
            if (dir == null)
                return;
            foreach (var file in dir.GetFiles())
            {
                bool old = (name != "" && file.StartsWith(name + "_v") && file.EndsWith(".fstx") && "user://texcache/" + file != keep)
                    || (!_tidiedPngs && file.EndsWith(".png"));
                if (old)
                    dir.Remove(file);
            }
            _tidiedPngs = true;
        }
    }

    private static readonly object Lock = new();

    /// <summary>The average colour (linear) from a small mipmap level.</summary>
    private static Color MeanOf(Image img)
    {
        int level = 0;
        while (level < img.GetMipmapCount() && Math.Max(img.GetWidth() >> level, img.GetHeight() >> level) > 64)
            level++;
        int w = Math.Max(img.GetWidth() >> level, 1), h = Math.Max(img.GetHeight() >> level, 1);
        int offset = level == 0 ? 0 : (int)img.GetMipmapOffset(level);
        var data = img.GetData();
        double r = 0, g = 0, b = 0;
        for (int i = 0; i < w * h; i++)
        {
            r += Lin(data[offset + i * 4] / 255f);
            g += Lin(data[offset + i * 4 + 1] / 255f);
            b += Lin(data[offset + i * 4 + 2] / 255f);
        }
        return new Color((float)(r / (w * h)), (float)(g / (w * h)), (float)(b / (w * h)));
    }

    public static float Lin(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static (Image, Color)? Read(string path)
    {
        if (!FileAccess.FileExists(path))
            return null;
        using var f = FileAccess.OpenCompressed(path, FileAccess.ModeFlags.Read, FileAccess.CompressionMode.Zstd);
        if (f == null || f.Get32() != Magic)
            return null;
        int w = (int)f.Get32(), h = (int)f.Get32(), format = (int)f.Get32();
        bool mips = f.Get8() != 0;
        var mean = new Color(f.GetFloat(), f.GetFloat(), f.GetFloat());
        long length = (long)f.Get64();
        var data = f.GetBuffer(length);
        if (data.Length != length)
            return null;
        try
        {
            return (Image.CreateFromData(w, h, mips, (Image.Format)format, data), mean);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Write(string path, Image img, Color mean)
    {
        DirAccess.MakeDirRecursiveAbsolute("user://texcache");
        using var f = FileAccess.OpenCompressed(path, FileAccess.ModeFlags.Write, FileAccess.CompressionMode.Zstd);
        if (f == null)
            return;
        var data = img.GetData();
        f.Store32(Magic);
        f.Store32((uint)img.GetWidth());
        f.Store32((uint)img.GetHeight());
        f.Store32((uint)img.GetFormat());
        f.Store8((byte)(img.HasMipmaps() ? 1 : 0));
        f.StoreFloat(mean.R);
        f.StoreFloat(mean.G);
        f.StoreFloat(mean.B);
        f.Store64((ulong)data.Length);
        f.StoreBuffer(data);
    }
}
