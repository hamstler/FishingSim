using System;
using System.Collections.Generic;
using Godot;

namespace FishingSim.Graphics;

/// <summary>
/// Textures generated at start-up for the realistic materials: tiling noise (and its normal map) for terrain, water
/// and clouds, normal maps derived from the albedo of the procedural planks/stones, leaf and needle clusters for the
/// tree crowns, and bark. Everything is cached; no image files are needed.
/// </summary>
public static class ProceduralTextures
{
    private static readonly Dictionary<string, Texture2D> Cache = new();

    /// <summary>Seamless fractal noise (generated on a worker thread by Godot).</summary>
    public static NoiseTexture2D Noise(int seed, float frequency, int size = 512, bool asNormalMap = false, float bump = 8f, int octaves = 5)
    {
        string key = $"noise:{seed}:{frequency}:{size}:{asNormalMap}:{bump}:{octaves}";
        if (Cache.TryGetValue(key, out var t))
            return (NoiseTexture2D)t;
        var tex = new NoiseTexture2D
        {
            Width = size,
            Height = size,
            Seamless = true,
            SeamlessBlendSkirt = 0.15f,
            AsNormalMap = asNormalMap,
            BumpStrength = bump,
            GenerateMipmaps = true,
            Noise = new FastNoiseLite
            {
                Seed = seed,
                Frequency = frequency,
                NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
                FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
                FractalOctaves = octaves,
            },
        };
        Cache[key] = tex;
        return tex;
    }

    /// <summary>Cellular noise (cell edges) used for the light caustics on the lake bottom.</summary>
    public static NoiseTexture2D Caustics()
    {
        const string key = "caustics";
        if (Cache.TryGetValue(key, out var t))
            return (NoiseTexture2D)t;
        var tex = new NoiseTexture2D
        {
            Width = 256,
            Height = 256,
            Seamless = true,
            GenerateMipmaps = true,
            Noise = new FastNoiseLite
            {
                Seed = 11,
                Frequency = 0.04f,
                NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular,
                CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Sub,
                FractalType = FastNoiseLite.FractalTypeEnum.None,
            },
        };
        Cache[key] = tex;
        return tex;
    }

    /// <summary>A normal map computed from the brightness of an albedo texture (planks, shingles, stone).</summary>
    public static Texture2D NormalFrom(Texture2D albedo, float strength = 3f)
    {
        string key = $"normal:{albedo.GetRid()}:{strength}";
        if (Cache.TryGetValue(key, out var t))
            return t;
        var img = albedo.GetImage();
        if (img == null)
            return albedo;
        img = (Image)img.Duplicate();
        if (img.IsCompressed())
            img.Decompress();
        img.ClearMipmaps();
        img.BumpMapToNormalMap(strength);
        img.Convert(Image.Format.Rgba8);
        img.GenerateMipmaps();
        var tex = ImageTexture.CreateFromImage(img);
        Cache[key] = tex;
        return tex;
    }

    /// <summary>
    /// A cluster of leaves (or needles) with alpha, for the leaf cards of the tree crowns. Leaves are mid-grey so the
    /// per-tree colour tints them; empty pixels carry the average colour so mipmaps do not get dark halos.
    /// </summary>
    public static Texture2D LeafCluster(bool needles)
    {
        string key = needles ? "needles" : "leaves";
        if (Cache.TryGetValue(key, out var t))
            return t;
        const int size = 256;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        img.Fill(new Color(0.8f, 0.9f, 0.72f, 0f));
        var rng = new RandomNumberGenerator { Seed = needles ? 21UL : 12UL };
        var c = new Vector2(size / 2f, size / 2f);
        if (!needles)
        {
            for (int i = 0; i < 95; i++)
            {
                float r = MathF.Sqrt(rng.Randf()) * size * 0.42f;
                float a = rng.Randf() * Mathf.Tau;
                var p = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                float len = rng.RandfRange(13f, 24f), wid = len * rng.RandfRange(0.38f, 0.5f);
                float ang = a + rng.RandfRange(-0.8f, 0.8f);
                float shade = rng.RandfRange(0.55f, 1f);
                var tint = new Color(0.88f * shade, 1f * shade, 0.78f * shade);
                Ellipse(img, p, len, wid, ang, tint);
            }
        }
        else
        {
            for (int b = 0; b < 9; b++)
            {
                float a = rng.Randf() * Mathf.Tau;
                var start = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rng.RandfRange(0f, 30f);
                var dir = new Vector2(MathF.Cos(a + rng.RandfRange(-0.4f, 0.4f)), MathF.Sin(a + rng.RandfRange(-0.4f, 0.4f)));
                float branchLen = rng.RandfRange(60f, 100f);
                for (float s = 0; s < branchLen; s += 2.2f)
                {
                    var p = start + dir * s;
                    float shade = rng.RandfRange(0.55f, 1f);
                    var tint = new Color(0.82f * shade, 1f * shade, 0.84f * shade);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var n = new Vector2(-dir.Y, dir.X) * side;
                        var tip = p + (n * 0.9f + dir * 0.45f).Normalized() * rng.RandfRange(9f, 15f);
                        Line(img, p, tip, tint);
                    }
                }
            }
        }
        img.GenerateMipmaps();
        PreserveAlphaCoverage(img, 0.5f);
        var tex = ImageTexture.CreateFromImage(img);
        Cache[key] = tex;
        return tex;
    }

    /// <summary>
    /// Rescales the alpha of every mipmap so that the share of pixels passing the alpha-test threshold stays the same
    /// as in the full-size image. Without it, alpha-tested foliage thins out and vanishes in the distance.
    /// </summary>
    public static void PreserveAlphaCoverage(Image img, float threshold)
    {
        if (img.GetFormat() != Image.Format.Rgba8 || !img.HasMipmaps())
            return;
        byte[] data = img.GetData();
        int levels = img.GetMipmapCount();
        float Coverage(int offset, int count, float scale)
        {
            int pass = 0;
            for (int i = 0; i < count; i++)
                if (data[offset + i * 4 + 3] / 255f * scale >= threshold)
                    pass++;
            return pass / (float)Math.Max(count, 1);
        }
        int w0 = img.GetWidth(), h0 = img.GetHeight();
        float target = Coverage(0, w0 * h0, 1f);
        for (int level = 1; level <= levels; level++)
        {
            int offset = (int)img.GetMipmapOffset(level);
            int w = Math.Max(w0 >> level, 1), h = Math.Max(h0 >> level, 1);
            float lo = 0.5f, hi = 8f;
            for (int it = 0; it < 14; it++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Coverage(offset, w * h, mid) < target)
                    lo = mid;
                else
                    hi = mid;
            }
            for (int i = 0; i < w * h; i++)
            {
                int k = offset + i * 4 + 3;
                data[k] = (byte)Math.Min(255f, data[k] * hi);
            }
        }
        img.SetData(w0, h0, true, Image.Format.Rgba8, data);
    }

    /// <summary>Vertical bark furrows (grey-brown, tinted per tree).</summary>
    public static Texture2D Bark()
    {
        const string key = "bark";
        if (Cache.TryGetValue(key, out var t))
            return t;
        var noise = new FastNoiseLite { Seed = 5, Frequency = 0.05f, FractalOctaves = 4 };
        const int w = 128, h = 256;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Stretched noise along the trunk gives furrows; wrap X so the cylinder has no seam.
                float ang = x / (float)w * Mathf.Tau;
                float n = noise.GetNoise3D(MathF.Cos(ang) * 14f, y * 0.15f, MathF.Sin(ang) * 14f);
                float furrow = 0.55f + 0.45f * MathF.Tanh(n * 3f);
                float fine = noise.GetNoise2D(x * 3f, y * 3f) * 0.08f;
                float v = Math.Clamp(furrow + fine, 0f, 1f);
                img.SetPixel(x, y, new Color(0.62f * v, 0.58f * v, 0.52f * v));
            }
        }
        img.GenerateMipmaps();
        var tex = ImageTexture.CreateFromImage(img);
        Cache[key] = tex;
        return tex;
    }

    private static void Ellipse(Image img, Vector2 p, float len, float wid, float ang, Color tint)
    {
        int r = (int)MathF.Ceiling(len);
        var axis = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        var perp = new Vector2(-axis.Y, axis.X);
        for (int y = -r; y <= r; y++)
        {
            for (int x = -r; x <= r; x++)
            {
                int px = (int)p.X + x, py = (int)p.Y + y;
                if (px < 0 || py < 0 || px >= img.GetWidth() || py >= img.GetHeight())
                    continue;
                var d = new Vector2(x, y);
                float u = d.Dot(axis) / (len * 0.5f), v = d.Dot(perp) / (wid * 0.5f);
                // Pointed leaf: narrower towards both ends.
                float edge = u * u + v * v / MathF.Max(1f - u * u * 0.5f, 0.2f);
                if (edge > 1f)
                    continue;
                float vein = MathF.Abs(v) < 0.08f ? 0.85f : 1f;
                float light = 0.85f + 0.15f * u;     // a little lighter towards the tip
                img.SetPixel(px, py, new Color(tint.R * vein * light, tint.G * vein * light, tint.B * vein * light, 1f));
            }
        }
    }

    private static void Line(Image img, Vector2 a, Vector2 b, Color tint)
    {
        int steps = (int)MathF.Ceiling(a.DistanceTo(b));
        for (int i = 0; i <= steps; i++)
        {
            var p = a.Lerp(b, i / (float)Math.Max(steps, 1));
            int px = (int)p.X, py = (int)p.Y;
            if (px < 0 || py < 0 || px >= img.GetWidth() || py >= img.GetHeight())
                continue;
            img.SetPixel(px, py, new Color(tint.R, tint.G, tint.B, 1f));
        }
    }
}
