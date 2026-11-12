using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;

namespace FishingSim.Graphics;

/// <summary>
/// Photographed PBR textures from Poly Haven (CC0: free for any use, commercial too), kept as WebP in
/// assets/textures/&lt;id&gt;/ (diff, nor = OpenGL normal map, optional disp = height; info.json gives the real-world
/// size). Fetched by tools/assets/polyhaven.py. Decoded here, given mipmaps and block compressed by the game itself,
/// and cached like the painted textures (TextureStore).
/// </summary>
public static class PhotoTextures
{
    private const int Version = 1;

    public sealed record Photo(Texture2D Albedo, Texture2D Normal, float Metres);

    private static readonly Dictionary<string, Photo> Cache = new();

    private static string Dir(string id) => $"res://assets/textures/{id}";

    public static bool Has(string id) => FileAccess.FileExists($"{Dir(id)}/diff.webp");

    /// <summary>The texture's real-world width (m): one repeat covers this much.</summary>
    public static float Metres(string id)
    {
        string path = $"{Dir(id)}/info.json";
        if (!FileAccess.FileExists(path) || JsonNode.Parse(FileAccess.GetFileAsString(path)) is not JsonObject info)
            return 2f;
        return (float?)info["metres"]?[0] ?? 2f;
    }

    /// <summary>A picture of the set decoded to RGBA8 (null when missing).</summary>
    public static Image? Decode(string id, string part)
    {
        string path = $"{Dir(id)}/{part}.webp";
        if (!FileAccess.FileExists(path))
            return null;
        var img = new Image();
        if (img.LoadWebpFromBuffer(FileAccess.GetFileAsBytes(path)) != Error.Ok)
            return null;
        img.Convert(Image.Format.Rgba8);
        return img;
    }

    /// <summary>Decodes and compresses every set into the disk cache (no textures are made: safe on a worker thread).</summary>
    public static void Prewarm()
    {
        using var dir = DirAccess.Open("res://assets/textures");
        if (dir == null)
            return;
        foreach (var id in dir.GetDirectories())
            if (Has(id))
            {
                TextureStore.Get($"photo_{id}_a", Version, BlockCompress.Kind.Colour, () => Decode(id, "diff")!);
                TextureStore.Get($"photo_{id}_n", Version, BlockCompress.Kind.TwoChannel, () => Decode(id, "nor")!, normalMap: true);
            }
    }

    /// <summary>Colour and normal map as compressed textures for ordinary materials (OpenGL normal map, as Godot reads it).</summary>
    public static Photo? Get(string id)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(id, out var cached))
                return cached;
            if (!Has(id))
                return null;
            if (Renderer.IsHeadless)
            {
                var plain = ImageTexture.CreateFromImage(Image.CreateEmpty(4, 4, false, Image.Format.Rgba8));
                return Cache[id] = new Photo(plain, plain, Metres(id));
            }
            var (albedo, _) = TextureStore.Get($"photo_{id}_a", Version, BlockCompress.Kind.Colour, () => Decode(id, "diff")!);
            var (normal, _) = TextureStore.Get($"photo_{id}_n", Version, BlockCompress.Kind.TwoChannel, () => Decode(id, "nor")!, normalMap: true);
            var photo = new Photo(ImageTexture.CreateFromImage(albedo), ImageTexture.CreateFromImage(normal), Metres(id));
            Cache[id] = photo;
            return photo;
        }
    }

    /// <summary>
    /// Colour with a height in alpha (the set's displacement map, or its brightness when it has none), resized to
    /// <paramref name="size"/>: what the terrain's layer blending and the bark shader read.
    /// </summary>
    public static Image AlbedoWithHeight(string id, int size)
    {
        var img = Decode(id, "diff") ?? Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        if (img.GetWidth() != size)
            img.Resize(size, size, Image.Interpolation.Cubic);
        var height = Decode(id, "disp");
        if (height != null && height.GetWidth() != size)
            height.Resize(size, size, Image.Interpolation.Cubic);
        var data = img.GetData();
        var h = height?.GetData();
        for (int i = 0; i < size * size; i++)
            data[i * 4 + 3] = h != null ? h[i * 4] : (byte)((data[i * 4] * 77 + data[i * 4 + 1] * 150 + data[i * 4 + 2] * 29) >> 8);
        return Image.CreateFromData(size, size, false, Image.Format.Rgba8, data);
    }

    /// <summary>
    /// The normal map in the ground painter's convention (green points down the picture: it is the tilt along +z
    /// when the picture lies on the ground), resized to <paramref name="size"/>.
    /// </summary>
    public static Image GroundNormal(string id, int size)
    {
        var img = Decode(id, "nor") ?? Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        if (img.GetWidth() != size)
            img.Resize(size, size, Image.Interpolation.Cubic);
        var data = img.GetData();
        for (int i = 0; i < size * size; i++)
            data[i * 4 + 1] = (byte)(255 - data[i * 4 + 1]);
        return Image.CreateFromData(size, size, false, Image.Format.Rgba8, data);
    }
}
