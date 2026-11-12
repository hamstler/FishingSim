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

    /// <summary>Atlas cells of <see cref="LeafCluster"/>: which leaf a crown wears.</summary>
    public const int LeafOak = 0, LeafBirch = 1, LeafWillow = 2, LeafHazel = 3;

    /// <summary>
    /// Leaf sprays with alpha for the crowns' cards: twigs with their leaves, painted with a midrib, side veins, a
    /// lighter half and serrated or lobed edges. Broad leaves come as an atlas of four 512-px cells (oak, birch, willow,
    /// hazel; <see cref="LeafOak"/>...), conifers as one cell of needle sprays with lighter new growth at the tips.
    /// Leaves are a pale green so the per-tree colour tints them; empty pixels carry the average colour so mipmaps do
    /// not get dark halos.
    /// </summary>
    public static Texture2D LeafCluster(bool needles)
    {
        string key = needles ? "needles" : "leaves";
        if (Cache.TryGetValue(key, out var t))
            return t;
        const int cell = 512;
        int size = needles ? cell : cell * 2;
        var canvas = new LeafCanvas(size);
        var rng = new Random(needles ? 21 : 12);
        if (needles)
            PaintNeedleSpray(canvas, cell / 2f, cell / 2f, cell, rng);
        else
            for (int k = 0; k < 4; k++)
                PaintLeafSpray(canvas, (k % 2) * cell + cell / 2f, (k / 2) * cell + cell / 2f, cell, k, rng);
        var img = canvas.Image(needles ? new Color(0.6f, 0.78f, 0.66f) : new Color(0.72f, 0.86f, 0.6f));
        img.GenerateMipmaps();
        PreserveAlphaCoverage(img, 0.5f);
        var tex = ImageTexture.CreateFromImage(img);
        Cache[key] = tex;
        return tex;
    }

    /// <summary>RGBA in floats, painted with "over" blending and anti-aliased coverage.</summary>
    private sealed class LeafCanvas
    {
        public readonly int Width, Height;
        private readonly float[] _r, _g, _b, _a;

        public LeafCanvas(int width, int height = 0)
        {
            Width = width;
            Height = height > 0 ? height : width;
            _r = new float[Width * Height];
            _g = new float[Width * Height];
            _b = new float[Width * Height];
            _a = new float[Width * Height];
        }

        public void Plot(int x, int y, Color c, float cover)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || cover <= 0f)
                return;
            cover = MathF.Min(cover, 1f);
            int i = y * Width + x;
            _r[i] = c.R * cover + _r[i] * (1f - cover);
            _g[i] = c.G * cover + _g[i] * (1f - cover);
            _b[i] = c.B * cover + _b[i] * (1f - cover);
            _a[i] = cover + _a[i] * (1f - cover);
        }

        public Image Image(Color empty)
        {
            var data = new byte[Width * Height * 4];
            for (int i = 0; i < Width * Height; i++)
            {
                float a = _a[i];
                // Colours were blended against black: divide the coverage out; empty pixels get the average leaf.
                var c = a > 0.01f ? new Color(_r[i] / a, _g[i] / a, _b[i] / a) : empty;
                data[i * 4] = (byte)(Math.Clamp(c.R, 0f, 1f) * 255f);
                data[i * 4 + 1] = (byte)(Math.Clamp(c.G, 0f, 1f) * 255f);
                data[i * 4 + 2] = (byte)(Math.Clamp(c.B, 0f, 1f) * 255f);
                data[i * 4 + 3] = (byte)(Math.Clamp(a, 0f, 1f) * 255f);
            }
            return Godot.Image.CreateFromData(Width, Height, false, Godot.Image.Format.Rgba8, data);
        }

        /// <summary>A tapering line (twig, needle) with a rounded cross-section shade.</summary>
        public void Line(Vector2 a, Vector2 b, float w0, float w1, Color c0, Color c1)
        {
            float len = a.DistanceTo(b);
            int steps = Math.Max(2, (int)(len * 2f));
            var d = (b - a) / MathF.Max(len, 0.001f);
            var n = new Vector2(-d.Y, d.X);
            for (int k = 0; k <= steps; k++)
            {
                float t = k / (float)steps;
                var p = a.Lerp(b, t);
                float w = w0 + (w1 - w0) * t;
                var c = c0.Lerp(c1, t);
                float half = w * 0.5f + 0.75f;
                for (float o = -half; o <= half; o += 0.5f)
                {
                    float cover = Math.Clamp(w * 0.5f + 0.5f - MathF.Abs(o), 0f, 1f);
                    float round = 0.8f + 0.25f * (1f - MathF.Abs(o) / half) - 0.1f * MathF.Max(o, 0f) / half;
                    var q = p + n * o;
                    Plot((int)MathF.Round(q.X), (int)MathF.Round(q.Y), c * round, cover);
                }
            }
        }
    }

    /// <summary>Half-width (0..1) of a leaf at <paramref name="t"/> (0 = stalk, 1 = tip) for each leaf kind.</summary>
    private static float LeafProfile(int kind, float t)
    {
        float body = MathF.Sin(MathF.PI * Math.Clamp(t, 0f, 1f));
        return kind switch
        {
            // Oak: widest beyond the middle, deep rounded lobes.
            LeafOak => MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * MathF.Pow(t, 0.8f)), 0f), 0.7f) * (0.62f + 0.38f * MathF.Abs(MathF.Sin(t * MathF.PI * 4.5f))),
            // Birch: a broad base drawn out to a point, finely toothed.
            LeafBirch => (t < 0.3f ? MathF.Sqrt(t / 0.3f) : MathF.Pow((1f - t) / 0.7f, 0.85f)) * (1f - 0.08f * ((t * 22f) % 1f)),
            // Willow: long and narrow.
            LeafWillow => MathF.Pow(MathF.Max(body, 0f), 0.75f),
            // Hazel and bushes: round with a short drawn-out tip, toothed.
            _ => MathF.Pow(MathF.Max(body, 0f), 0.55f) * (1f - 0.55f * MathF.Pow(t, 5f)) * (1f - 0.06f * ((t * 16f) % 1f)),
        };
    }

    private static void PaintLeaf(LeafCanvas c, Vector2 stalk, float angle, float length, float aspect, int kind, Color col)
    {
        var d = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var n = new Vector2(-d.Y, d.X);
        float halfW = length * aspect * 0.5f;
        int r = (int)MathF.Ceiling(length + 2f);
        var dark = col * 0.78f;
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                var p = new Vector2(x, y);
                float along = p.Dot(d), across = p.Dot(n);
                float t = along / length;
                if (t < -0.02f || t > 1.02f)
                    continue;
                float hw = halfW * LeafProfile(kind, t);
                float dist = hw - MathF.Abs(across);
                if (dist < -0.7f)
                    continue;
                float cover = Math.Clamp(dist + 0.5f, 0f, 1f);
                float side = across / MathF.Max(hw, 0.5f);           // -1..1 across the leaf
                var k = col * (across > 0f ? 1.06f : 0.9f);          // folded along the midrib: one half catches more light
                k = k.Lerp(dark, MathF.Pow(MathF.Abs(side), 3f) * 0.35f + (1f - t) * 0.12f);
                if (MathF.Abs(across) < 0.9f && t < 0.92f)
                    k = k.Lerp(new Color(0.86f, 0.92f, 0.7f), 0.45f);  // midrib
                else
                {
                    float vein = ((t * 6f - MathF.Abs(side) * 1.4f) % 1f + 1f) % 1f;
                    if (vein < 0.08f && MathF.Abs(side) < 0.85f)
                        k = k.Lerp(new Color(0.82f, 0.9f, 0.66f), 0.3f);
                }
                c.Plot((int)(stalk.X + x), (int)(stalk.Y + y), k, cover);
            }
    }

    private static float Rf(Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    /// <summary>
    /// A fern frond with alpha (u across, v from the stalk at 0 to the tip at 1): a pale stalk with pinnae on both sides,
    /// longest a third of the way up, each with rounded lobes, lighter towards the tip.
    /// </summary>
    public static Texture2D FernFrond()
    {
        const string key = "fern";
        if (Cache.TryGetValue(key, out var t))
            return t;
        const int w = 256, h = 1024;
        var c = new LeafCanvas(w, h);
        var rng = new Random(33);
        var stalk = new Color(0.7f, 0.78f, 0.48f);
        c.Line(new Vector2(w / 2f, 0f), new Vector2(w / 2f, h - 6f), 5f, 1.5f, stalk * 0.85f, stalk);
        int side = 1;
        for (float y = 70f; y < h - 30f; y += 21f)
        {
            float along = y / h;
            float len = (w * 0.47f) * MathF.Pow(MathF.Sin(MathF.PI * MathF.Min(along * 1.25f + 0.12f, 1f)), 0.7f) * (1f - 0.75f * along * along);
            if (len < 6f)
                continue;
            foreach (int sgn in new[] { side, -side })
            {
                var root = new Vector2(w / 2f + sgn * 2f, y + (sgn == side ? 0f : 10f));
                // Pinnae point outwards and towards the tip.
                float ang = MathF.Atan2(0.55f, sgn * 1f);
                var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                if (sgn < 0)
                    dir = new Vector2(-MathF.Abs(dir.X), dir.Y);
                var col = new Color(0.62f, 0.82f, 0.48f).Lerp(new Color(0.8f, 0.92f, 0.55f), along) * Rf(rng, 0.85f, 1f);
                var n = new Vector2(-dir.Y, dir.X);
                int lobes = Math.Max(3, (int)(len / 9f));
                for (int k = 0; k < lobes; k++)
                {
                    float s = (k + 0.5f) / lobes;
                    var p = root + dir * len * s;
                    float lw = 9f * (1f - 0.75f * s) + 2f;
                    foreach (int ls in new[] { -1, 1 })
                    {
                        var lc = p + n * ls * lw * 0.5f;
                        int r = (int)MathF.Ceiling(lw) + 1;
                        for (int dy = -r; dy <= r; dy++)
                            for (int dx = -r; dx <= r; dx++)
                            {
                                float u = (dx * dir.X + dy * dir.Y) / (lw * 0.62f), v = (dx * n.X + dy * n.Y) / (lw * 0.62f);
                                float dd = u * u + v * v;
                                if (dd > 1.2f)
                                    continue;
                                float cover = Math.Clamp((1f - MathF.Sqrt(dd)) * lw * 0.6f + 0.5f, 0f, 1f);
                                c.Plot((int)(lc.X + dx), (int)(lc.Y + dy), col * (0.86f + 0.14f * (1f - dd) + (ls > 0 ? 0.04f : -0.04f)), cover);
                            }
                    }
                }
                c.Line(root, root + dir * len, 2.2f, 0.8f, stalk * 0.9f, stalk);
            }
            side = -side;
        }
        var img = c.Image(new Color(0.66f, 0.84f, 0.5f));
        img.GenerateMipmaps();
        PreserveAlphaCoverage(img, 0.5f);
        var tex = ImageTexture.CreateFromImage(img);
        Cache[key] = tex;
        return tex;
    }

    private static void PaintLeafSpray(LeafCanvas c, float cx, float cy, int cell, int kind, Random rng)
    {
        float reach = cell * 0.46f;
        var (len, aspect, perTwig) = kind switch
        {
            LeafOak => (cell * 0.16f, 0.55f, 5),
            LeafBirch => (cell * 0.1f, 0.7f, 7),
            LeafWillow => (cell * 0.2f, 0.17f, 8),
            _ => (cell * 0.14f, 0.8f, 5),
        };
        Color[] greens =
        {
            new(0.74f, 0.88f, 0.6f), new(0.8f, 0.9f, 0.55f), new(0.66f, 0.84f, 0.64f), new(0.72f, 0.86f, 0.58f), new(0.84f, 0.9f, 0.6f),
        };
        var twig = new Color(0.45f, 0.38f, 0.3f);
        int twigs = kind == LeafWillow ? 9 : 7;
        var order = new List<(Vector2 P, float A, float L, Color C)>();
        for (int i = 0; i < twigs; i++)
        {
            float a = i * Mathf.Tau / twigs + Rf(rng, -0.25f, 0.25f);
            var start = new Vector2(cx, cy) + new Vector2(MathF.Cos(a), MathF.Sin(a)) * Rf(rng, 0f, cell * 0.05f);
            float twigLen = reach - len * (kind == LeafWillow ? 0.7f : 0.9f);
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            var end = start + dir * twigLen;
            c.Line(start, end, 3.2f, 1.2f, twig, twig * 1.1f);
            for (int k = 0; k < perTwig; k++)
            {
                float s = (k + 0.6f) / perTwig;
                var at = start.Lerp(end, s);
                float sideA = (k % 2 == 0 ? 1f : -1f) * Rf(rng, 0.55f, 0.95f) * (kind == LeafWillow ? 0.5f : 1f);
                float la = a + sideA;
                float l = len * Rf(rng, 0.75f, 1.1f) * (0.75f + 0.25f * s);
                // Keep the leaf inside the cell.
                var tip = at + new Vector2(MathF.Cos(la), MathF.Sin(la)) * l;
                if (tip.DistanceTo(new Vector2(cx, cy)) > reach)
                    l *= MathF.Max(0.4f, (reach - at.DistanceTo(new Vector2(cx, cy))) / l);
                order.Add((at, la, l, greens[rng.Next(greens.Length)] * Rf(rng, 0.72f, 1f)));
            }
            order.Add((end, a + Rf(rng, -0.15f, 0.15f), len * 0.95f * MathF.Min(1f, (reach - end.DistanceTo(new Vector2(cx, cy))) / len + 0.3f),
                greens[rng.Next(greens.Length)] * Rf(rng, 0.85f, 1f)));
        }
        // Darker leaves first (they lie deeper), lighter ones on top.
        order.Sort((p, q) => (p.C.R + p.C.G).CompareTo(q.C.R + q.C.G));
        foreach (var (p, a, l, col) in order)
            PaintLeaf(c, p, a, l, aspect, kind, col);
    }

    private static void PaintNeedleSpray(LeafCanvas c, float cx, float cy, int cell, Random rng)
    {
        float reach = cell * 0.46f;
        var bark = new Color(0.42f, 0.33f, 0.25f);
        var old = new Color(0.64f, 0.8f, 0.68f);
        var young = new Color(0.82f, 0.92f, 0.58f);
        void Twig(Vector2 start, float angle, float length, float width, int depth)
        {
            var d = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var end = start + d * length;
            c.Line(start, end, width, width * 0.4f, bark, bark);
            for (float s = 3f; s < length; s += 2.3f)
            {
                float t = s / length;
                var p = start + d * s;
                var col = old.Lerp(young, MathF.Max(t - 0.7f, 0f) / 0.3f) * Rf(rng, 0.78f, 1f);
                float nl = Rf(rng, 11f, 17f) * (1f - 0.3f * t);
                foreach (float side in new[] { -1f, 1f })
                {
                    float na = angle + side * Rf(rng, 0.6f, 1.0f);
                    var tip = p + new Vector2(MathF.Cos(na), MathF.Sin(na)) * nl;
                    if (tip.DistanceTo(new Vector2(cx, cy)) < reach)
                        c.Line(p, tip, 1.8f, 0.8f, col * 0.85f, col);
                }
                if (depth > 0 && rng.NextDouble() < 0.07)
                    Twig(p, angle + (rng.NextDouble() < 0.5 ? -1f : 1f) * Rf(rng, 0.5f, 0.8f), (length - s) * Rf(rng, 0.4f, 0.6f), width * 0.6f, depth - 1);
            }
        }
        for (int i = 0; i < 11; i++)
        {
            float a = i * Mathf.Tau / 11f + Rf(rng, -0.2f, 0.2f);
            var start = new Vector2(cx, cy) + new Vector2(MathF.Cos(a), MathF.Sin(a)) * Rf(rng, 0f, 18f);
            Twig(start, a, reach - 22f, 3.5f, 1);
        }
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
}
