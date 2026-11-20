using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// Graphics/ProceduralTextures' leaf pictures, ported: twigs with their leaves (oak, birch, willow, hazel in a 2x2
    /// atlas) or needle sprays, painted with a midrib, side veins, a lighter half, lobed or toothed edges; pale green so
    /// each tree's colour tints them; mipmaps keep the leaves' coverage for the alpha test.
    /// </summary>
    public static class LeafPainter
    {
        public const int LeafOak = 0, LeafBirch = 1, LeafWillow = 2, LeafHazel = 3;
        // ProceduralTextures.LK: designs in 1024-px units, painted at the Textures setting's size (Godot paints a 4096 atlas
        // on worker threads; here 2048 at most: the painting runs on the main thread when a map first loads)
        private static readonly float LK = Settings.Quality >= 3 ? 2f : 1f;
        private static readonly int LeafCell = (int)(512 * LK);
        private static Texture2D? _leaves, _needles;

        public static Texture2D Leaves => _leaves ??= Upload(PaintCluster(false), "Leaves");
        public static Texture2D Needles => _needles ??= Upload(PaintCluster(true), "Needles");

        private sealed class Canvas
        {
            public readonly int Width, Height;
            private readonly float[] _r, _g, _b, _a;

            public Canvas(int w, int h = 0)
            {
                Width = w;
                Height = h > 0 ? h : w;
                _r = new float[Width * Height];
                _g = new float[Width * Height];
                _b = new float[Width * Height];
                _a = new float[Width * Height];
            }

            public void Plot(int x, int y, Color c, float cover)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height || cover <= 0f)
                    return;
                cover = Mathf.Min(cover, 1f);
                int i = y * Width + x;
                _r[i] = c.r * cover + _r[i] * (1f - cover);
                _g[i] = c.g * cover + _g[i] * (1f - cover);
                _b[i] = c.b * cover + _b[i] * (1f - cover);
                _a[i] = cover + _a[i] * (1f - cover);
            }

            /// <summary>Rows top-down (as painted); colours divided by coverage, empty pixels the average leaf.</summary>
            public Color32[] Pixels(Color empty)
            {
                var px = new Color32[Width * Height];
                for (int i = 0; i < px.Length; i++)
                {
                    float a = _a[i];
                    var c = a > 0.01f ? new Color(_r[i] / a, _g[i] / a, _b[i] / a) : empty;
                    px[i] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), (byte)(Mathf.Clamp01(a) * 255f));
                }
                return px;
            }

            public void Line(Vector2 a, Vector2 b, float w0, float w1, Color c0, Color c1)
            {
                float len = Vector2.Distance(a, b);
                int steps = Math.Max(2, (int)(len * 2f));
                var d = (b - a) / Mathf.Max(len, 0.001f);
                var n = new Vector2(-d.y, d.x);
                for (int k = 0; k <= steps; k++)
                {
                    float t = k / (float)steps;
                    var p = Vector2.Lerp(a, b, t);
                    float w = w0 + (w1 - w0) * t;
                    var c = Color.Lerp(c0, c1, t);
                    float half = w * 0.5f + 0.75f;
                    for (float o = -half; o <= half; o += 0.5f)
                    {
                        float cover = Mathf.Clamp01(w * 0.5f + 0.5f - Mathf.Abs(o));
                        float round = 0.8f + 0.25f * (1f - Mathf.Abs(o) / half) - 0.1f * Mathf.Max(o, 0f) / half;
                        var q = p + n * o;
                        Plot(Mathf.RoundToInt(q.x), Mathf.RoundToInt(q.y), c * round, cover);
                    }
                }
            }
        }

        private static float Rf(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        private static float LeafProfile(int kind, float t)
        {
            float body = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t));
            return kind switch
            {
                LeafOak => Mathf.Pow(Mathf.Max(Mathf.Sin(Mathf.PI * Mathf.Pow(Mathf.Max(t, 0f), 0.8f)), 0f), 0.7f) * (0.62f + 0.38f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 4.5f))),
                LeafBirch => (t < 0.3f ? Mathf.Sqrt(Mathf.Max(t, 0f) / 0.3f) : Mathf.Pow(Mathf.Max(1f - t, 0f) / 0.7f, 0.85f)) * (1f - 0.08f * ((t * 22f) % 1f)),
                LeafWillow => Mathf.Pow(Mathf.Max(body, 0f), 0.75f),
                _ => Mathf.Pow(Mathf.Max(body, 0f), 0.55f) * (1f - 0.55f * Mathf.Pow(Mathf.Max(t, 0f), 5f)) * (1f - 0.06f * ((t * 16f) % 1f)),
            };
        }

        private static void PaintLeaf(Canvas c, Vector2 stalk, float angle, float length, float aspect, int kind, Color col)
        {
            var d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var n = new Vector2(-d.y, d.x);
            float halfW = length * aspect * 0.5f;
            int r = Mathf.CeilToInt(length + 2f);
            var dark = col * 0.78f;
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    var p = new Vector2(x, y);
                    float along = Vector2.Dot(p, d), across = Vector2.Dot(p, n);
                    float t = along / length;
                    if (t < -0.02f || t > 1.02f)
                        continue;
                    float hw = halfW * LeafProfile(kind, t);
                    float dist = hw - Mathf.Abs(across);
                    if (dist < -0.7f)
                        continue;
                    float cover = Mathf.Clamp01(dist + 0.5f);
                    float side = across / Mathf.Max(hw, 0.5f);
                    var k = col * (across > 0f ? 1.06f : 0.9f);
                    k = Color.Lerp(k, dark, Mathf.Pow(Mathf.Abs(side), 3f) * 0.35f + (1f - t) * 0.12f);
                    if (Mathf.Abs(across) < 0.9f * LK && t < 0.92f)
                        k = Color.Lerp(k, new Color(0.86f, 0.92f, 0.7f), 0.45f);
                    else
                    {
                        float vein = ((t * 6f - Mathf.Abs(side) * 1.4f) % 1f + 1f) % 1f;
                        if (vein < 0.08f && Mathf.Abs(side) < 0.85f)
                            k = Color.Lerp(k, new Color(0.82f, 0.9f, 0.66f), 0.3f);
                    }
                    c.Plot((int)(stalk.x + x), (int)(stalk.y + y), k, cover);
                }
        }

        private static void PaintLeafSpray(Canvas c, float cx, float cy, int cell, int kind, System.Random rng)
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
            var centre = new Vector2(cx, cy);
            for (int i = 0; i < twigs; i++)
            {
                float a = i * Mathf.PI * 2f / twigs + Rf(rng, -0.25f, 0.25f);
                var start = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rf(rng, 0f, cell * 0.05f);
                float twigLen = reach - len * (kind == LeafWillow ? 0.7f : 0.9f);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var end = start + dir * twigLen;
                c.Line(start, end, 3.2f * LK, 1.2f * LK, twig, twig * 1.1f);
                for (int k = 0; k < perTwig; k++)
                {
                    float s = (k + 0.6f) / perTwig;
                    var at = Vector2.Lerp(start, end, s);
                    float sideA = (k % 2 == 0 ? 1f : -1f) * Rf(rng, 0.55f, 0.95f) * (kind == LeafWillow ? 0.5f : 1f);
                    float la = a + sideA;
                    float l = len * Rf(rng, 0.75f, 1.1f) * (0.75f + 0.25f * s);
                    var tip = at + new Vector2(Mathf.Cos(la), Mathf.Sin(la)) * l;
                    if (Vector2.Distance(tip, centre) > reach)
                        l *= Mathf.Max(0.4f, (reach - Vector2.Distance(at, centre)) / l);
                    order.Add((at, la, l, greens[rng.Next(greens.Length)] * Rf(rng, 0.72f, 1f)));
                }
                order.Add((end, a + Rf(rng, -0.15f, 0.15f), len * 0.95f * Mathf.Min(1f, (reach - Vector2.Distance(end, centre)) / len + 0.3f),
                    greens[rng.Next(greens.Length)] * Rf(rng, 0.85f, 1f)));
            }
            order.Sort((p, q) => (p.C.r + p.C.g).CompareTo(q.C.r + q.C.g));
            foreach (var (p, a, l, col) in order)
                PaintLeaf(c, p, a, l, aspect, kind, col);
        }

        private static void PaintNeedleSpray(Canvas c, float cx, float cy, int cell, System.Random rng)
        {
            float reach = cell * 0.46f;
            var bark = new Color(0.42f, 0.33f, 0.25f);
            var old = new Color(0.64f, 0.8f, 0.68f);
            var young = new Color(0.82f, 0.92f, 0.58f);
            var centre = new Vector2(cx, cy);
            void Twig(Vector2 start, float angle, float length, float width, int depth)
            {
                var d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var end = start + d * length;
                c.Line(start, end, width, width * 0.4f, bark, bark);
                for (float s = 3f * LK; s < length; s += 2.3f * LK)
                {
                    float t = s / length;
                    var p = start + d * s;
                    var col = Color.Lerp(old, young, Mathf.Max(t - 0.7f, 0f) / 0.3f) * Rf(rng, 0.78f, 1f);
                    float nl = Rf(rng, 11f, 17f) * LK * (1f - 0.3f * t);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float na = angle + side * Rf(rng, 0.6f, 1.0f);
                        var tip = p + new Vector2(Mathf.Cos(na), Mathf.Sin(na)) * nl;
                        if (Vector2.Distance(tip, centre) < reach)
                            c.Line(p, tip, 1.8f * LK, 0.8f * LK, col * 0.85f, col);
                    }
                    if (depth > 0 && rng.NextDouble() < 0.07)
                        Twig(p, angle + (rng.NextDouble() < 0.5 ? -1f : 1f) * Rf(rng, 0.5f, 0.8f), (length - s) * Rf(rng, 0.4f, 0.6f), width * 0.6f, depth - 1);
                }
            }
            for (int i = 0; i < 11; i++)
            {
                float a = i * Mathf.PI * 2f / 11f + Rf(rng, -0.2f, 0.2f);
                var start = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rf(rng, 0f, 18f) * LK;
                Twig(start, a, reach - 22f * LK, 3.5f * LK, 1);
            }
        }

        private static (Color32[] Px, int Size) PaintCluster(bool needles)
        {
            int size = needles ? LeafCell : LeafCell * 2;
            var canvas = new Canvas(size);
            var rng = new System.Random(needles ? 21 : 12);
            if (needles)
                PaintNeedleSpray(canvas, LeafCell / 2f, LeafCell / 2f, LeafCell, rng);
            else
                for (int k = 0; k < 4; k++)
                    PaintLeafSpray(canvas, (k % 2) * LeafCell + LeafCell / 2f, (k / 2) * LeafCell + LeafCell / 2f, LeafCell, k, rng);
            return (canvas.Pixels(needles ? new Color(0.6f, 0.78f, 0.66f) : new Color(0.72f, 0.86f, 0.6f)), size);
        }

        /// <summary>
        /// A texture from top-down rows (flipped so Unity's v runs up as in the painted picture's Godot UVs mirrored), its
        /// mipmaps box-filtered with the alpha rescaled to keep the coverage of the alpha test (PreserveAlphaCoverage).
        /// </summary>
        public static Texture2D Upload((Color32[] Px, int Size) painted, string name, int width = 0)
        {
            int w = width > 0 ? width : painted.Size, h = painted.Px.Length / w;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var level = new Color32[w * h];
            for (int y = 0; y < h; y++)
                Array.Copy(painted.Px, y * w, level, (h - 1 - y) * w, w);
            float Coverage(Color32[] px, float scale)
            {
                int pass = 0;
                foreach (var p in px)
                    if (p.a / 255f * scale >= 0.5f)
                        pass++;
                return pass / (float)Math.Max(px.Length, 1);
            }
            float target = Coverage(level, 1f);
            tex.SetPixels32(level, 0);
            int lw = w, lh = h;
            for (int m = 1; m < tex.mipmapCount; m++)
            {
                int nw = Math.Max(lw / 2, 1), nh = Math.Max(lh / 2, 1);
                var next = new Color32[nw * nh];
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                    {
                        int r = 0, g = 0, b = 0, a = 0, n = 0;
                        for (int dy = 0; dy < 2; dy++)
                            for (int dx = 0; dx < 2; dx++)
                            {
                                int sx = Math.Min(x * 2 + dx, lw - 1), sy = Math.Min(y * 2 + dy, lh - 1);
                                var p = level[sy * lw + sx];
                                r += p.r; g += p.g; b += p.b; a += p.a; n++;
                            }
                        next[y * nw + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                    }
                // keep the share of pixels passing the alpha test
                float lo = 0.5f, hi = 8f;
                for (int it = 0; it < 14; it++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (Coverage(next, mid) < target) lo = mid; else hi = mid;
                }
                var scaled = new Color32[next.Length];
                for (int i = 0; i < next.Length; i++)
                    scaled[i] = new Color32(next[i].r, next[i].g, next[i].b, (byte)Mathf.Min(255f, next[i].a * hi));
                tex.SetPixels32(scaled, m);
                level = next;
                lw = nw;
                lh = nh;
            }
            tex.Apply(false, false);
            tex.Compress(true);
            tex.Apply(false, true);
            return tex;
        }
    }
}
