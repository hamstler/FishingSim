using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Data;

namespace FishingSim.Graphics;

/// <summary>
/// Painted pictures for the shops, the inventory and the fish lists, drawn in code (in a 64-unit space on a canvas
/// four times finer: smooth shading, a soft dark outline and a drop shadow, 128 px), one per item and per species.
/// </summary>
public static class Icons
{
    public const int Size = 64;

    /// <summary>The finished pictures are this many pixels square.</summary>
    public const int TextureSize = 128;
    private static readonly Dictionary<string, ImageTexture> Cache = new();
    private static readonly Color Outline = new(0.13f, 0.08f, 0.05f);

    // ------------------------------------------------------------------ canvas
    /// <summary>
    /// Drawing happens in a 64-unit space on a canvas four times finer, kept in managed memory; the finished picture
    /// gets a soft dark outline and a small drop shadow and is scaled down to 128 px, so it is smooth, not pixelated.
    /// </summary>
    private sealed class Canvas
    {
        public const int Scale = 4;
        public const int Res = Size * Scale;
        public readonly float[] Px4 = new float[Res * Res * 4];

        private void Set(int hx, int hy, Color c)
        {
            if (hx < 0 || hy < 0 || hx >= Res || hy >= Res)
                return;
            int i = (hy * Res + hx) * 4;
            // Paint over what is there (respecting the new colour's alpha).
            float a = c.A, inv = 1f - a;
            Px4[i] = c.R * a + Px4[i] * inv;
            Px4[i + 1] = c.G * a + Px4[i + 1] * inv;
            Px4[i + 2] = c.B * a + Px4[i + 2] * inv;
            Px4[i + 3] = a + Px4[i + 3] * inv;
        }

        /// <summary>A unit pixel of the 64-unit space (a 4×4 block on the fine canvas).</summary>
        public void Px(int x, int y, Color c)
        {
            for (int j = 0; j < Scale; j++)
                for (int i = 0; i < Scale; i++)
                    Set(x * Scale + i, y * Scale + j, c);
        }

        /// <summary>One fine pixel at a fractional position of the 64-unit space.</summary>
        public void Fine(float x, float y, Color c) => Set((int)(x * Scale), (int)(y * Scale), c);

        public void Disc(float cx, float cy, float rx, float ry, Color c, Color? shade = null)
        {
            float sx = cx * Scale, sy = cy * Scale, srx = MathF.Max(rx * Scale, 0.5f), sry = MathF.Max(ry * Scale, 0.5f);
            for (int y = (int)(sy - sry - 2); y <= sy + sry + 2; y++)
                for (int x = (int)(sx - srx - 2); x <= sx + srx + 2; x++)
                {
                    float dx = (x + 0.5f - sx) / srx, dy = (y + 0.5f - sy) / sry;
                    float r = MathF.Sqrt(dx * dx + dy * dy);
                    // Anti-aliased edge: the last fine pixel fades out.
                    float edge = Math.Clamp((1f - r) * MathF.Min(srx, sry) + 0.5f, 0f, 1f);
                    if (edge <= 0f)
                        continue;
                    var col = c;
                    if (shade != null)
                    {
                        // Light from the top-left: a smooth turn into the shade colour, and a soft highlight.
                        float t = Math.Clamp((dx * 0.6f + dy * 0.8f + 0.15f) / 0.95f, 0f, 1f);
                        col = c.Lerp(shade.Value, t * t * (3 - 2 * t));
                        float hl = MathF.Max(0f, 1f - MathF.Sqrt((dx + 0.42f) * (dx + 0.42f) + (dy + 0.42f) * (dy + 0.42f)) / 0.38f);
                        col = col.Lerp(c.Lightened(0.45f), hl * 0.55f);
                    }
                    col.A = c.A * edge;
                    Set(x, y, col);
                }
        }

        public void Rect(int x0, int y0, int x1, int y1, Color c)
        {
            for (int y = y0 * Scale; y < (y1 + 1) * Scale; y++)
                for (int x = x0 * Scale; x < (x1 + 1) * Scale; x++)
                    Set(x, y, c);
        }

        public void Line(float x0, float y0, float x1, float y1, float width, Color c)
        {
            // A capsule: every fine pixel within half the width of the segment.
            float ax = x0 * Scale + 2f, ay = y0 * Scale + 2f, bx = x1 * Scale + 2f, by = y1 * Scale + 2f;
            float hw = MathF.Max(width * Scale * 0.5f, 1.2f);
            int minX = (int)(MathF.Min(ax, bx) - hw - 2), maxX = (int)(MathF.Max(ax, bx) + hw + 2);
            int minY = (int)(MathF.Min(ay, by) - hw - 2), maxY = (int)(MathF.Max(ay, by) + hw + 2);
            float vx = bx - ax, vy = by - ay, len2 = MathF.Max(vx * vx + vy * vy, 1e-4f);
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float t = Math.Clamp(((px - ax) * vx + (py - ay) * vy) / len2, 0f, 1f);
                    float qx = ax + t * vx - px, qy = ay + t * vy - py;
                    float d = MathF.Sqrt(qx * qx + qy * qy);
                    float edge = Math.Clamp(hw - d + 0.5f, 0f, 1f);
                    if (edge <= 0f)
                        continue;
                    var col = c;
                    col.A = c.A * edge;
                    Set(x, y, col);
                }
        }

        public void Ring(float cx, float cy, float r, float thickness, Color c)
        {
            float sx = cx * Scale, sy = cy * Scale, sr = r * Scale, st = thickness * Scale;
            for (int y = (int)(sy - sr - 2); y <= sy + sr + 2; y++)
                for (int x = (int)(sx - sr - 2); x <= sx + sr + 2; x++)
                {
                    float d = MathF.Sqrt((x + 0.5f - sx) * (x + 0.5f - sx) + (y + 0.5f - sy) * (y + 0.5f - sy));
                    float edge = Math.Clamp(MathF.Min(sr - d, d - (sr - st)) + 0.5f, 0f, 1f);
                    if (edge <= 0f)
                        continue;
                    var col = c;
                    col.A = c.A * edge;
                    Set(x, y, col);
                }
        }

        /// <summary>Outline, drop shadow, scale down to 128 px.</summary>
        public ImageTexture Finish()
        {
            const int R = Res;
            var alpha = new float[R * R];
            for (int i = 0; i < R * R; i++)
                alpha[i] = Px4[i * 4 + 3];
            // The outline: a dilation of the shape by ~1.1 units, drawn dark under the picture.
            var grow = Dilate(alpha, R, 4);
            // The shadow: the shape shifted down-right and blurred a little.
            var shadow = Dilate(Shift(alpha, R, 5, 7), R, 3);
            var bytes = new byte[R * R * 4];
            for (int i = 0; i < R * R; i++)
            {
                float a = Px4[i * 4 + 3];
                float r = Px4[i * 4], g = Px4[i * 4 + 1], b = Px4[i * 4 + 2];
                // Un-premultiply the painted colour.
                if (a > 1e-4f)
                {
                    r /= a; g /= a; b /= a;
                }
                float oa = grow[i] * (1f - a);
                float sa = shadow[i] * 0.28f * (1f - a) * (1f - oa);
                float outA = a + oa + sa;
                var col = outA > 1e-4f
                    ? new Color((r * a + Outline.R * oa) / outA, (g * a + Outline.G * oa) / outA, (b * a + Outline.B * oa) / outA)
                    : new Color(0, 0, 0);
                bytes[i * 4] = (byte)(Math.Clamp(col.R, 0f, 1f) * 255f);
                bytes[i * 4 + 1] = (byte)(Math.Clamp(col.G, 0f, 1f) * 255f);
                bytes[i * 4 + 2] = (byte)(Math.Clamp(col.B, 0f, 1f) * 255f);
                bytes[i * 4 + 3] = (byte)(Math.Clamp(outA, 0f, 1f) * 255f);
            }
            var img = Image.CreateFromData(R, R, false, Image.Format.Rgba8, bytes);
            img.Resize(TextureSize, TextureSize, Image.Interpolation.Lanczos);
            img.GenerateMipmaps();
            return ImageTexture.CreateFromImage(img);
        }

        private static float[] Dilate(float[] a, int n, int radius)
        {
            var tmp = new float[n * n];
            var outp = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float m = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int xx = x + k;
                        if (xx >= 0 && xx < n)
                            m = MathF.Max(m, a[y * n + xx]);
                    }
                    tmp[y * n + x] = m;
                }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float m = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int yy = y + k;
                        if (yy >= 0 && yy < n)
                            m = MathF.Max(m, tmp[yy * n + x]);
                    }
                    outp[y * n + x] = m;
                }
            return outp;
        }

        private static float[] Shift(float[] a, int n, int dx, int dy)
        {
            var o = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int sx = x - dx, sy = y - dy;
                    if (sx >= 0 && sy >= 0 && sx < n && sy < n)
                        o[y * n + x] = a[sy * n + sx];
                }
            return o;
        }
    }

    private static Color C(float r, float g, float b) => new(r, g, b);

    private static ImageTexture Cached(string key, Func<Canvas, Canvas> draw)
    {
        if (Cache.TryGetValue(key, out var t))
            return t;
        t = draw(new Canvas()).Finish();
        Cache[key] = t;
        return t;
    }

    // ------------------------------------------------------------------ items
    public static ImageTexture For(TackleItem item) => item switch
    {
        RodData r => Rod(r),
        ReelData r => Reel(r),
        LineData l => Line(l),
        HookData h => Hook(h),
        BaitData b => Bait(b.ItemId),
        LeaderData d => Leader(d),
        KeepnetData k => Keepnet(k),
        LandingNetData n => LandingNet(n),
        AccessoryData a => Accessory(a),
        _ => Cached("unknown", c => { c.Disc(32, 32, 16, 16, C(0.6f, 0.6f, 0.6f)); return c; }),
    };

    private static ImageTexture Rod(RodData r) => Cached("rod:" + r.ItemId, c =>
    {
        // Blank colour from the wood: hazel, ash, oak, willow, yew, hornbeam, reed, horn-tipped oak, laminated woods.
        var wood = r.ItemId switch
        {
            "junior" => C(0.78f, 0.58f, 0.32f), "karpiowka" => C(0.45f, 0.29f, 0.16f), "wierzbowa_splawikowa" => C(0.82f, 0.78f, 0.5f),
            "cisowy_spinning" => C(0.66f, 0.32f, 0.2f), "grabowy_gruntowy" => C(0.72f, 0.68f, 0.6f), "trzcinowa_odleglosciowa" => C(0.9f, 0.8f, 0.48f),
            "jesionowy_spinning" => C(0.8f, 0.66f, 0.4f), "sumowy_kij" => C(0.32f, 0.2f, 0.12f), "wedka_mistrza" => C(0.6f, 0.38f, 0.2f),
            _ => C(0.86f, 0.74f, 0.46f),
        };
        var shade = wood.Darkened(0.3f);
        // Spinning rods are short and stiff, float rods long.
        float tipX = r.Kind == RodKind.Spinning ? 50 : 58, tipY = r.Kind == RodKind.Spinning ? 14 : 6;
        float thick = r.MaxLoad > 10f ? 1.4f : 1f;
        c.Line(10, 56, tipX, tipY, 3.2f * thick, shade);
        c.Line(9, 55, tipX - 1, tipY - 1, 2.2f * thick, wood);
        if (r.ItemId == "trzcinowa_odleglosciowa")           // the reed's nodes
            foreach (float t in new[] { 0.2f, 0.4f, 0.6f, 0.8f })
                c.Disc(10 + (tipX - 10) * t, 56 - (56 - tipY) * t, 2f, 2f, wood.Darkened(0.35f));
        if (r.ItemId == "wedka_mistrza")                     // light and dark woods glued in bands
            foreach (float t in new[] { 0.3f, 0.5f, 0.7f })
                c.Line(10 + (tipX - 10) * t, 56 - (56 - tipY) * t, 10 + (tipX - 10) * (t + 0.08f), 56 - (56 - tipY) * (t + 0.08f), 2.2f, C(0.9f, 0.8f, 0.55f));
        if (r.ItemId == "sumowy_kij")                        // horn tip
            c.Line(10 + (tipX - 10) * 0.85f, 56 - (56 - tipY) * 0.85f, tipX - 1, tipY - 1, 2.6f, C(0.92f, 0.88f, 0.78f));
        c.Line(6, 60, 20, 45, 5f, C(0.36f, 0.22f, 0.13f));                      // leather grip
        c.Line(6, 59, 19, 45, 3f, C(0.5f, 0.32f, 0.18f));
        foreach (float t in new[] { 0.35f, 0.55f, 0.72f, 0.86f })
            c.Disc(10 + (tipX - 10) * t + 1.5f, 56 - (56 - tipY) * t + 1.5f, 1.4f, 1.4f, C(0.75f, 0.6f, 0.3f));   // brass guides
        c.Disc(23, 41, 2.2f, 2.2f, C(0.85f, 0.8f, 0.6f));                                   // twine wrap
        return c;
    });

    private static ImageTexture Reel(ReelData r) => Cached("reel:" + r.ItemId, c =>
    {
        var body = r.ItemId switch
        {
            "mlynek1000" => C(0.55f, 0.36f, 0.2f), "mlynek3000" => C(0.5f, 0.52f, 0.56f), "szpula_lipowa" => C(0.86f, 0.74f, 0.52f),
            "orzechowy_lekki" => C(0.42f, 0.27f, 0.16f), "mosiezny_hamulec" => C(0.8f, 0.62f, 0.28f), "beben_sumowy" => C(0.3f, 0.3f, 0.33f),
            _ => C(0.85f, 0.66f, 0.25f),
        };
        float rr = 14f + 5f * Math.Clamp(r.LineCapacity / 220f, 0f, 1f);
        c.Rect(28, 8, 34, 20, C(0.32f, 0.22f, 0.14f));                                   // foot
        c.Disc(30, 36, rr, rr, body, body.Darkened(0.3f));
        c.Disc(30, 36, rr * 0.6f, rr * 0.6f, C(0.85f, 0.78f, 0.6f), C(0.7f, 0.62f, 0.45f));          // line on the spool
        if (r.ItemId == "mosiezny_hamulec" || r.ItemId == "freerunner")
            c.Ring(30, 36, rr - 1.5f, 1.5f, C(1f, 0.86f, 0.5f));                        // brass rim
        c.Disc(30, 36, 4, 4, body.Darkened(0.4f));
        c.Line(30, 36, 50, 50, 3f, body.Darkened(0.2f));                                // handle arm
        c.Disc(52, 52, 4.5f, 4.5f, C(0.3f, 0.2f, 0.12f), C(0.2f, 0.13f, 0.08f));       // knob
        return c;
    });

    private static ImageTexture Line(LineData l) => Cached("line:" + l.ItemId, c =>
    {
        var thread = l.ItemId switch
        {
            "plecionka030" => C(0.82f, 0.74f, 0.55f), "zylka016" => C(0.93f, 0.9f, 0.84f), "jelito_cienkie" or "jelito_mocne" => C(0.88f, 0.7f, 0.42f),
            "jedwab_skrecany" or "plecionka_jedwabna" => C(0.97f, 0.95f, 0.9f), "plecionka_konopna" => C(0.6f, 0.58f, 0.4f),
            "plecionka_smolowana" => C(0.18f, 0.16f, 0.14f), _ => C(0.86f, 0.82f, 0.72f),
        };
        c.Rect(14, 14, 50, 50, C(0.45f, 0.3f, 0.18f));                                  // spool flanges
        c.Rect(18, 18, 46, 46, thread);
        if (l.Kind == LineKind.Braid)
        {
            for (int k = -28; k < 28; k += 4)                                          // the braid's weave
            {
                c.Line(18 + MathF.Max(k, 0), 18 - MathF.Min(k, 0), 46 - MathF.Max(-k, 0), 46 + MathF.Min(-k, 0) - 0.01f, 1f, thread.Darkened(0.22f));
                c.Line(46 - MathF.Max(k, 0), 18 - MathF.Min(k, 0), 18 + MathF.Max(-k, 0), 46 + MathF.Min(-k, 0) - 0.01f, 1f, thread.Lightened(0.15f));
            }
        }
        else
        {
            for (int y = 20; y < 46; y += 3)
                c.Line(18, y, 46, y + 1, 1f, thread.Darkened(0.18f));                  // windings
        }
        c.Disc(32, 32, 4, 4, C(0.3f, 0.2f, 0.12f));
        c.Line(46, 40, 58, 56, 1f, thread);                                             // loose end
        return c;
    });

    private static ImageTexture Hook(HookData h) => Cached("hook:" + h.ItemId, c =>
    {
        float s = 0.55f + h.Size * 0.05f;
        var iron = C(0.62f, 0.64f, 0.68f);
        float cx = 32, top = 32 - 22 * s, bend = 32 + 14 * s;
        c.Ring(cx, top - 3, 3.5f * s + 1, 2, iron);                                      // eye
        c.Line(cx, top, cx, bend, 3f, iron);                                             // shank
        for (int a = 0; a <= 180; a += 6)
        {
            float rad = Mathf.DegToRad(a);
            c.Disc(cx - 9 * s + MathF.Cos(rad) * 9 * s, bend + MathF.Sin(rad) * 9 * s, 1.5f, 1.5f, iron);
        }
        c.Line(cx - 18 * s, bend, cx - 18 * s, bend - 9 * s, 3f, iron);               // point
        c.Line(cx - 18 * s, bend - 9 * s, cx - 14 * s, bend - 5 * s, 2f, iron);        // barb
        c.Line(cx + 1, top, cx + 1, bend, 1f, C(0.85f, 0.86f, 0.9f));                  // shine
        return c;
    });

    public static ImageTexture Bait(string id) => Cached("bait:" + id, c =>
    {
        switch (id)
        {
            case "worm":
                for (int i = 0; i < 40; i++)
                {
                    float t = i / 39f;
                    c.Disc(12 + t * 40, 32 + MathF.Sin(t * 9f) * 9, 4.2f, 4.2f, C(0.86f, 0.48f, 0.5f), C(0.7f, 0.34f, 0.38f));
                }
                break;
            case "maggot":
                foreach (var (x, y) in new[] { (20, 24), (40, 22), (30, 40), (46, 42) })
                    for (int k = 0; k < 4; k++)
                        c.Disc(x + k * 3, y + k, 3.2f, 3.6f, C(0.96f, 0.92f, 0.8f), C(0.85f, 0.8f, 0.66f));
                break;
            case "insect":
                c.Disc(30, 34, 15, 6, C(0.45f, 0.65f, 0.2f), C(0.32f, 0.5f, 0.15f));
                c.Disc(46, 30, 6, 5, C(0.5f, 0.7f, 0.22f));
                c.Line(24, 36, 14, 50, 2f, C(0.32f, 0.5f, 0.15f));
                c.Line(34, 38, 40, 52, 2f, C(0.32f, 0.5f, 0.15f));
                c.Line(48, 26, 58, 14, 1f, C(0.3f, 0.45f, 0.12f));
                c.Px(48, 28, C(0.05f, 0.05f, 0.05f));
                break;
            case "bread":
                c.Disc(32, 34, 24, 15, C(0.78f, 0.5f, 0.22f), C(0.62f, 0.38f, 0.16f));
                c.Disc(32, 32, 16, 8, C(0.96f, 0.86f, 0.62f));
                break;
            case "corn":
                foreach (var (x, y) in new[] { (22, 22), (36, 20), (46, 30), (28, 34), (40, 42), (20, 44), (32, 50) })
                    c.Disc(x, y, 5, 7, C(0.93f, 0.78f, 0.38f), C(0.8f, 0.62f, 0.25f));
                break;
            case "boilie":
                c.Disc(26, 36, 13, 13, C(0.85f, 0.55f, 0.25f), C(0.65f, 0.38f, 0.16f));
                c.Disc(44, 26, 9, 9, C(0.9f, 0.7f, 0.35f), C(0.7f, 0.5f, 0.2f));
                break;
            case "rosowka":
                for (int i = 0; i < 46; i++)
                {
                    float t = i / 45f;
                    c.Disc(8 + t * 48, 32 + MathF.Sin(t * 7f) * 11, 5.2f, 5.2f, C(0.62f, 0.3f, 0.32f), C(0.48f, 0.2f, 0.24f));
                }
                break;
            case "ochotka":
                foreach (var (x, y, a) in new[] { (18, 24, 0.4f), (36, 20, -0.3f), (28, 40, 0.1f), (46, 40, 0.6f), (16, 46, -0.5f) })
                    for (int k = 0; k < 8; k++)
                        c.Disc(x + k * 1.6f, y + k * a * 1.6f, 1.8f, 1.8f, C(0.82f, 0.12f, 0.12f), C(0.6f, 0.06f, 0.08f));
                break;
            case "chrabaszcz":
                c.Disc(32, 36, 14, 17, C(0.46f, 0.26f, 0.14f), C(0.32f, 0.17f, 0.09f));
                c.Line(32, 20, 32, 52, 1f, C(0.2f, 0.1f, 0.05f));
                c.Disc(32, 17, 7, 5, C(0.15f, 0.12f, 0.1f));
                foreach (int side in new[] { -1, 1 })
                    for (int k = 0; k < 3; k++)
                        c.Line(32 + side * 12, 30 + k * 8, 32 + side * 22, 26 + k * 10, 1.5f, C(0.15f, 0.12f, 0.1f));
                break;
            case "martwa_rybka":
                c.Disc(30, 32, 20, 8, C(0.62f, 0.64f, 0.6f), C(0.45f, 0.47f, 0.44f));
                c.Line(48, 32, 58, 24, 3f, C(0.5f, 0.52f, 0.5f));
                c.Line(48, 32, 58, 40, 3f, C(0.5f, 0.52f, 0.5f));
                c.Line(14, 28, 18, 32, 1f, C(0.05f, 0.05f, 0.05f));
                c.Line(18, 28, 14, 32, 1f, C(0.05f, 0.05f, 0.05f));
                break;
            case "ciasto":
                c.Disc(32, 34, 18, 16, C(0.95f, 0.86f, 0.66f), C(0.82f, 0.72f, 0.5f));
                c.Disc(26, 28, 5, 4, C(1f, 0.95f, 0.8f));
                break;
            case "groch":
                foreach (var (x, y) in new[] { (22, 24), (38, 22), (46, 36), (30, 38), (18, 42), (36, 50) })
                    c.Disc(x, y, 7, 7, C(0.55f, 0.7f, 0.3f), C(0.4f, 0.55f, 0.2f));
                break;
            case "kasza" or "konopie":
            {
                // A small heap of grains, scattered at random in a round pile.
                bool hemp = id == "konopie";
                var rng = new Random(hemp ? 7 : 3);
                for (int k = 0; k < (hemp ? 22 : 34); k++)
                {
                    float a = (float)rng.NextDouble() * MathF.Tau, d = MathF.Sqrt((float)rng.NextDouble()) * 20f;
                    float gx = 32 + MathF.Cos(a) * d * 1.1f, gy = 36 + MathF.Sin(a) * d * 0.75f;
                    if (hemp)
                        c.Disc(gx, gy, 3.6f, 3f, C(0.4f, 0.34f, 0.24f), C(0.26f, 0.21f, 0.15f));
                    else
                        c.Disc(gx, gy, 3f, 2.2f, C(0.92f, 0.86f, 0.68f), C(0.76f, 0.68f, 0.5f));
                }
                break;
            }
            case "kulka_czosnkowa":
                c.Disc(26, 36, 13, 13, C(0.95f, 0.93f, 0.85f), C(0.78f, 0.76f, 0.68f));
                c.Disc(44, 26, 9, 9, C(0.92f, 0.9f, 0.8f), C(0.75f, 0.73f, 0.64f));
                break;
            case "kulka_plywajaca":
                c.Disc(32, 36, 15, 15, C(0.92f, 0.6f, 0.25f), C(0.72f, 0.42f, 0.16f));
                c.Rect(26, 16, 38, 22, C(0.7f, 0.55f, 0.35f));                         // the cork in it
                break;
            case "blystka_obrotowa":
                c.Line(10, 32, 54, 32, 1.5f, C(0.55f, 0.56f, 0.6f));                    // wire shaft
                c.Disc(30, 26, 9, 5, C(0.9f, 0.82f, 0.5f), C(0.7f, 0.6f, 0.3f));          // the spinning blade
                c.Disc(42, 32, 4, 4, C(0.8f, 0.2f, 0.15f));                              // bead body
                c.Line(54, 32, 58, 40, 1.5f, C(0.55f, 0.56f, 0.6f));                     // treble hook
                c.Line(54, 32, 58, 24, 1.5f, C(0.55f, 0.56f, 0.6f));
                c.Ring(8, 32, 3, 1.2f, C(0.55f, 0.56f, 0.6f));
                break;
            case "blystka_mosiezna" or "blystka_srebrna":
                var metal = id == "blystka_srebrna" ? C(0.86f, 0.88f, 0.92f) : C(0.86f, 0.68f, 0.3f);
                for (int i = 0; i < 30; i++)
                {
                    float t = i / 29f;
                    c.Disc(14 + t * 34, 22 + t * 16, 3f + 7f * MathF.Sin(MathF.PI * MathF.Pow(t, 0.7f)), 3f + 6f * MathF.Sin(MathF.PI * MathF.Pow(t, 0.7f)), metal, metal.Darkened(0.3f));
                }
                c.Line(22, 26, 40, 34, 1f, metal.Lightened(0.4f));                      // shine
                c.Ring(11, 20, 3, 1.2f, C(0.55f, 0.56f, 0.6f));
                c.Line(50, 40, 56, 50, 1.5f, C(0.55f, 0.56f, 0.6f));
                break;
            case "rybka_kora" or "rybka_drewniana":
                bool painted = id == "rybka_drewniana";
                c.Disc(32, 32, 20, 8, painted ? C(0.9f, 0.85f, 0.6f) : C(0.55f, 0.38f, 0.24f), painted ? C(0.35f, 0.5f, 0.3f) : C(0.42f, 0.28f, 0.17f));
                if (painted)
                    c.Disc(16, 31, 6, 6, C(0.85f, 0.2f, 0.15f));                         // red head
                c.Line(12, 34, 5, 40, 2.5f, C(0.85f, 0.85f, 0.8f));                     // diving lip
                c.Disc(16, 29, 1.8f, 1.8f, C(0.05f, 0.05f, 0.05f));
                c.Line(28, 40, 26, 48, 1.5f, C(0.55f, 0.56f, 0.6f));                    // belly and tail trebles
                c.Line(52, 32, 58, 38, 1.5f, C(0.55f, 0.56f, 0.6f));
                break;
            case "piorko_olowiane":
                c.Disc(16, 34, 8, 8, C(0.55f, 0.56f, 0.6f), C(0.38f, 0.39f, 0.43f));     // lead head
                c.Disc(13, 32, 1.6f, 1.6f, C(0.9f, 0.85f, 0.2f));
                for (int k = 0; k < 7; k++)
                    c.Line(22, 34, 56, 24 + k * 3, 2f, k % 2 == 0 ? C(0.95f, 0.95f, 0.9f) : C(0.8f, 0.3f, 0.25f));
                break;
            case "skorka_wegorza":
                for (int i = 0; i < 40; i++)
                {
                    float t = i / 39f;
                    float w = t < 0.6f ? 6f : 6f * (1f - (t - 0.6f) / 0.4f) + 1f;
                    c.Disc(12 + t * 36 + MathF.Max(t - 0.6f, 0f) * 10f, 32 + MathF.Sin(MathF.Max(t - 0.5f, 0f) * 12f) * 6f, w, w, C(0.3f, 0.32f, 0.2f), C(0.2f, 0.22f, 0.13f));
                }
                c.Disc(12, 32, 6, 6, C(0.55f, 0.56f, 0.6f));
                break;
            case "mucha_kogut" or "mucha_zajac":
                bool nymph = id == "mucha_zajac";
                c.Line(20, 36, 46, 36, 3f, nymph ? C(0.55f, 0.45f, 0.32f) : C(0.25f, 0.2f, 0.15f));   // body on the hook shank
                if (nymph)
                    for (int k = 0; k < 6; k++)
                        c.Line(22 + k * 4, 36, 20 + k * 4, 42, 1f, C(0.7f, 0.6f, 0.45f));
                else
                {
                    c.Disc(24, 24, 10, 6, C(0.75f, 0.45f, 0.2f), C(0.6f, 0.32f, 0.12f));   // hackle feathers
                    c.Disc(36, 22, 8, 5, C(0.85f, 0.6f, 0.3f));
                }
                c.Line(46, 36, 50, 46, 1.5f, C(0.55f, 0.56f, 0.6f));
                c.Line(50, 46, 44, 48, 1.5f, C(0.55f, 0.56f, 0.6f));
                break;
            default:   // livebait: a small silvery fish
                c.Disc(30, 32, 20, 8, C(0.75f, 0.8f, 0.82f), C(0.55f, 0.6f, 0.62f));
                c.Line(48, 32, 58, 24, 3f, C(0.6f, 0.65f, 0.7f));
                c.Line(48, 32, 58, 40, 3f, C(0.6f, 0.65f, 0.7f));
                c.Disc(16, 30, 2, 2, C(0.05f, 0.05f, 0.05f));
                break;
        }
        return c;
    });

    private static ImageTexture Leader(LeaderData d) => Cached("leader:" + d.ItemId, c =>
    {
        if (d.ItemId == "brak")
        {
            c.Ring(32, 32, 20, 3, C(0.7f, 0.25f, 0.2f));
            c.Line(18, 46, 46, 18, 3f, C(0.7f, 0.25f, 0.2f));
            return c;
        }
        var strand = d.ItemId switch
        {
            "przypon_wlosie" => C(0.92f, 0.9f, 0.84f), "przypon_jelito" => C(0.88f, 0.7f, 0.42f), "przypon_mosiezny" => C(0.88f, 0.7f, 0.3f),
            "przypon_stalowy" => C(0.5f, 0.52f, 0.56f), _ => C(0.95f, 0.93f, 0.86f),
        };
        // A loop at the top, the strand in a gentle S, a swivel, the hook at the bottom.
        c.Ring(32, 10, 5, 1.6f, strand);
        for (int i = 0; i < 30; i++)
        {
            float t = i / 29f;
            c.Disc(32 + MathF.Sin(t * 5f) * 6f, 15 + t * 30, d.BiteProof > 0f ? 1.4f : 1f, d.BiteProof > 0f ? 1.4f : 1f, strand);
        }
        if (d.ItemId == "przypon_jedwab_drut")
            for (int i = 0; i < 30; i += 3)
                c.Disc(32 + MathF.Sin(i / 29f * 5f) * 6f, 15 + i, 1.2f, 1.2f, C(0.6f, 0.6f, 0.62f));
        c.Disc(32, 47, 3, 4, C(0.55f, 0.56f, 0.6f));                                     // swivel
        c.Line(32, 50, 32, 58, 2f, C(0.62f, 0.64f, 0.68f));
        c.Line(32, 58, 26, 56, 2f, C(0.62f, 0.64f, 0.68f));
        c.Line(26, 56, 26, 52, 2f, C(0.62f, 0.64f, 0.68f));
        return c;
    });

    private static ImageTexture Keepnet(KeepnetData k) => Cached("keepnet:" + k.ItemId, c =>
    {
        var net = k.ItemId == "siatka_karpiowa" ? C(0.6f, 0.45f, 0.25f) : C(0.75f, 0.72f, 0.6f);
        int rings = k.ItemId == "siatka_mala" ? 3 : 5;
        for (int i = 0; i < rings; i++)
        {
            float y = 12 + i * (40f / (rings - 1));
            c.Ring(32, y, 16 - i * 1.5f, 2, C(0.45f, 0.3f, 0.18f));
        }
        for (int x = 18; x <= 46; x += 4)
            c.Line(x, 12, 32 + (x - 32) * 0.7f, 52, 1f, net);
        for (int y = 14; y <= 52; y += 4)
            c.Line(18 + (y - 12) * 0.13f, y, 46 - (y - 12) * 0.13f, y, 1f, net.Darkened(0.15f));
        return c;
    });

    private static ImageTexture LandingNet(LandingNetData n) => Cached("landingnet:" + n.ItemId, c =>
    {
        float r = n.MaxWeight > 10f ? 17f : n.MaxWeight > 5f ? 14f : 11f;
        c.Line(40, 40, 60, 60, 3.5f, C(0.5f, 0.33f, 0.18f));                            // handle
        for (int y = (int)(26 - r); y < 26 + r + 10; y += 3)
            for (int x = (int)(26 - r); x < 26 + r; x += 3)
            {
                float dx = (x - 26) / r, dy = (y - 26) / (r + 6);
                if (dx * dx + dy * dy < 1f && y > 22)
                    c.Px(x, y, C(0.8f, 0.76f, 0.62f));
            }
        c.Ring(26, 26, r, 2.5f, C(0.55f, 0.36f, 0.2f));
        return c;
    });

    private static ImageTexture Accessory(AccessoryData a) => Cached("acc:" + a.ItemId, c =>
    {
        switch (a.Kind)
        {
            case AccessoryKind.PolarizedGlasses:   // brimmed hat
                c.Disc(32, 40, 28, 7, C(0.5f, 0.36f, 0.2f), C(0.38f, 0.26f, 0.14f));
                c.Disc(32, 28, 14, 14, C(0.6f, 0.44f, 0.25f), C(0.46f, 0.32f, 0.18f));
                c.Rect(18, 32, 46, 35, C(0.7f, 0.2f, 0.15f));
                break;
            case AccessoryKind.Headlamp:           // tallow lantern
                c.Rect(20, 18, 44, 52, C(0.25f, 0.24f, 0.24f));
                c.Rect(23, 22, 41, 48, C(1f, 0.85f, 0.45f));
                c.Disc(32, 38, 4, 7, C(1f, 0.6f, 0.15f));
                c.Ring(32, 13, 6, 2, C(0.25f, 0.24f, 0.24f));
                break;
            case AccessoryKind.RodPod:             // forked stick with a bell
                c.Line(32, 60, 32, 26, 4f, C(0.5f, 0.33f, 0.18f));
                c.Line(32, 28, 20, 8, 3.5f, C(0.5f, 0.33f, 0.18f));
                c.Line(32, 28, 44, 8, 3.5f, C(0.5f, 0.33f, 0.18f));
                c.Disc(46, 22, 6, 7, C(0.95f, 0.78f, 0.3f), C(0.78f, 0.6f, 0.18f));
                break;
            case AccessoryKind.Chair:              // three-legged stool
                c.Disc(32, 20, 22, 6, C(0.62f, 0.42f, 0.24f), C(0.5f, 0.32f, 0.18f));
                c.Line(18, 22, 10, 58, 3.5f, C(0.45f, 0.3f, 0.17f));
                c.Line(46, 22, 54, 58, 3.5f, C(0.45f, 0.3f, 0.17f));
                c.Line(32, 24, 32, 60, 3.5f, C(0.4f, 0.26f, 0.15f));
                break;
            case AccessoryKind.Waders:             // greased leather waders
                c.Rect(18, 8, 46, 14, C(0.35f, 0.24f, 0.14f));
                c.Rect(18, 14, 30, 50, C(0.45f, 0.32f, 0.19f));
                c.Rect(34, 14, 46, 50, C(0.45f, 0.32f, 0.19f));
                c.Rect(14, 50, 30, 58, C(0.25f, 0.17f, 0.1f));
                c.Rect(34, 50, 50, 58, C(0.25f, 0.17f, 0.1f));
                c.Line(22, 8, 20, 2, 2f, C(0.3f, 0.2f, 0.12f));
                c.Line(42, 8, 44, 2, 2f, C(0.3f, 0.2f, 0.12f));
                break;
            case AccessoryKind.HookHone:           // whetstone with a hook
                c.Rect(10, 30, 54, 44, C(0.55f, 0.55f, 0.58f));
                c.Rect(10, 30, 54, 33, C(0.68f, 0.68f, 0.7f));
                c.Line(30, 14, 30, 26, 2.5f, C(0.62f, 0.64f, 0.68f));
                c.Line(30, 26, 24, 24, 2.5f, C(0.62f, 0.64f, 0.68f));
                break;
            case AccessoryKind.ReelGrease:         // a clay pot of goose fat
                c.Disc(32, 40, 18, 16, C(0.62f, 0.38f, 0.22f), C(0.48f, 0.28f, 0.16f));
                c.Disc(32, 26, 14, 4, C(0.96f, 0.93f, 0.82f));
                c.Rect(18, 22, 46, 24, C(0.5f, 0.3f, 0.17f));
                break;
            default:                                // wooden bucket
                c.Rect(16, 22, 48, 56, C(0.6f, 0.4f, 0.22f));
                for (int x = 20; x < 48; x += 6)
                    c.Line(x, 22, x, 56, 1f, C(0.48f, 0.3f, 0.16f));
                c.Rect(14, 28, 50, 31, C(0.35f, 0.35f, 0.38f));
                c.Rect(14, 48, 50, 51, C(0.35f, 0.35f, 0.38f));
                c.Disc(32, 22, 16, 4, C(0.25f, 0.45f, 0.6f));
                break;
        }
        return c;
    });

    // ------------------------------------------------------------------ fish
    /// <summary>The species drawn side-on with its painted skin (FishSkin), fins and eye, in its measured outline when
    /// a real fish of the species was measured (FishReference).</summary>
    public static ImageTexture Fish(FishSpecies sp) => Cached("fish:" + sp.SpeciesName, c =>
    {
        var skin = FishSkin.For(sp).GetImage();
        var reference = Fishing.FishReference.For(sp);
        float axis = reference?.Axis ?? 0f;
        float depth = Math.Clamp(sp.BodyDepth, 0.07f, 0.45f);
        float bodyLen = 46f, h = MathF.Max(bodyLen * depth, 5f);
        float pxPerLength = reference != null ? bodyLen / reference.BodyLength : 0f;
        if (reference != null)
        {
            float deepest = 0f;
            for (int i = 0; i <= 40; i++)
            {
                var (tp, bt) = reference.EdgesAt(i / 40f, axis);
                deepest = MathF.Max(deepest, tp + bt);
            }
            h = MathF.Max(deepest * pxPerLength, 5f);
        }
        float x0 = 6f, cy = 32f;
        var fin = FishSkin.Vivid(sp.FinColor, 1.5f);
        // Tail and fins first, the body is drawn over them.
        // Filled forked tail.
        for (float k = -h * 0.85f; k <= h * 0.85f; k += 0.5f)
            if (MathF.Abs(k) > h * 0.2f || MathF.Abs(k) < 0.6f)
                c.Line(x0 + bodyLen - 3, cy, 60 - (MathF.Abs(k) < h * 0.3f ? 4 : 0), cy + k, 1f, fin);
        float dorsal = x0 + bodyLen * (1f - sp.DorsalPos);
        c.Line(dorsal - 5, cy - h * 0.45f, dorsal + 3, cy - h * 0.95f, 3f, fin);
        c.Line(x0 + bodyLen * 0.6f, cy + h * 0.45f, x0 + bodyLen * 0.68f, cy + h * 0.8f, 2.5f, fin);
        const float step = 1f / Canvas.Scale;
        for (float y = MathF.Floor(cy - h); y <= cy + h; y += step)
            for (float x = x0; x <= x0 + bodyLen; x += step)
            {
                float u = (x - x0) / bodyLen;                                  // 0 nose, 1 tail
                float upper, lower;
                if (reference != null)
                {
                    var (tp, bt) = reference.EdgesAt(u, axis);
                    (upper, lower) = (tp * pxPerLength, bt * pxPerLength);
                }
                else
                {
                    float profile = MathF.Pow(MathF.Sin(MathF.PI * MathF.Pow(Math.Clamp(u, 0.001f, 1f), 0.75f)), 0.8f) * 0.88f + 0.12f * u;
                    upper = lower = h * 0.5f * profile;
                }
                if (y < cy - upper || y > cy + lower)
                    continue;
                float v = (y - (cy - upper)) / MathF.Max(upper + lower, 1f);   // 0 back, 1 belly
                int sw = skin.GetWidth(), sh = skin.GetHeight();
                var col = skin.GetPixel(Math.Clamp((int)(u * (sw - 1)), 0, sw - 1), Math.Clamp((int)(v * (sh - 1)), 0, sh - 1));
                c.Fine(x, y, col);
            }
        c.Disc(x0 + bodyLen * 0.1f, cy - h * 0.12f, 2.2f, 2.2f, C(0.95f, 0.78f, 0.25f));
        c.Px((int)(x0 + bodyLen * 0.1f), (int)(cy - h * 0.12f), C(0.02f, 0.02f, 0.02f));
        return c;
    });

    /// <summary>Category pictures for shop tabs.</summary>
    public static ImageTexture Category(string key) => key switch
    {
        "rod" => Rod(new RodData()),
        "reel" => Reel(new ReelData()),
        "line" => Line(new LineData()),
        "leader" => Leader(new LeaderData { BiteProof = 1f }),
        "hook" => Hook(new HookData { Size = 6 }),
        "bait" => Bait("worm"),
        "keepnet" => Keepnet(new KeepnetData()),
        "landingnet" => LandingNet(new LandingNetData { MaxWeight = 6 }),
        "accessory" => Accessory(new AccessoryData { Kind = AccessoryKind.PolarizedGlasses }),
        "fish" or "market" => Bait("livebait"),
        _ => Cached("unknown", c => { c.Disc(32, 32, 16, 16, C(0.6f, 0.6f, 0.6f)); return c; }),
    };
}
