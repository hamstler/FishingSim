using System;
using System.Collections.Generic;
using Godot;

namespace FishingSim.UI;

/// <summary>
/// The pictures the interface is built from, painted in code once and cached: carved oak frames with brass studs,
/// planked buttons with a bevel, parchment cards, gilded bars, coins, locks and badges. Nine-patch textures keep their
/// corners crisp at any size.
/// </summary>
public static class UiArt
{
    private static readonly Dictionary<string, Texture2D> Cache = new();

    // ------------------------------------------------------------------ painting helpers
    private static float Hash(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 362437);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
    }

    /// <summary>Smooth value noise in [0,1].</summary>
    private static float Noise(float x, float y, int seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    private static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    private static Texture2D Make(string key, int w, int h, Func<int, int, Color> paint)
    {
        if (Cache.TryGetValue(key, out var t))
            return t;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img.SetPixel(x, y, paint(x, y));
        t = ImageTexture.CreateFromImage(img);
        Cache[key] = t;
        return t;
    }

    /// <summary>Oak with its grain running along x (planks of the given height).</summary>
    private static Color Oak(int x, int y, Color baseColor, int seed, float plank = 0f)
    {
        float grain = Noise(x * 0.05f, y * 0.9f, seed) * 0.6f + Noise(x * 0.2f, y * 2.3f, seed + 1) * 0.4f;
        float c = 0.86f + 0.24f * grain;
        if (plank > 0f && (y % (int)plank) == 0)
            c *= 0.62f;                                         // the seam between two planks
        return new Color(baseColor.R * c, baseColor.G * c, baseColor.B * c, baseColor.A);
    }

    private static Color Mix(Color a, Color b, float t) => a.Lerp(b, Math.Clamp(t, 0f, 1f));

    /// <summary>A brass stud: a small dome with a highlight.</summary>
    private static Color Stud(Color under, float dx, float dy, float r)
    {
        float d = MathF.Sqrt(dx * dx + dy * dy) / r;
        if (d > 1f)
            return d < 1.25f ? Mix(under, new Color(0, 0, 0, under.A), 0.5f * (1.25f - d) / 0.25f) : under;
        float light = 0.55f + 0.45f * (-(dx + dy) / (r * 1.4f));
        var brass = new Color(0.86f, 0.66f, 0.3f) * Math.Clamp(light, 0.35f, 1.2f);
        if (MathF.Abs(dx + r * 0.35f) < r * 0.3f && MathF.Abs(dy + r * 0.35f) < r * 0.3f)
            brass = brass.Lerp(new Color(1f, 0.95f, 0.75f), 0.6f);
        brass.A = 1f;
        return brass;
    }

    // ------------------------------------------------------------------ frames and panels
    /// <summary>The big panel: dark oak planks, a carved frame with a bevel, a gilt inner line and brass studs.</summary>
    public static Texture2D PanelTexture => Make("panel", 96, 96, (x, y) =>
    {
        const int W = 96;
        int d = Math.Min(Math.Min(x, y), Math.Min(W - 1 - x, W - 1 - y));
        var c = Oak(x, y, new Color(0.17f, 0.115f, 0.07f, 0.96f), 11, 16);
        if (d < 2)
            return new Color(0.05f, 0.035f, 0.02f, 1f);
        if (d < 8)
        {
            // The carved frame: light on the top and left, in shade on the bottom and right.
            bool lit = Math.Min(x, y) == d;
            var frame = Oak(x, y, new Color(0.33f, 0.21f, 0.12f), 5);
            float bevel = d < 4 ? (lit ? 1.35f : 0.65f) : d > 6 ? (lit ? 0.7f : 1.2f) : 1f;
            return new Color(frame.R * bevel, frame.G * bevel, frame.B * bevel, 1f);
        }
        if (d == 10)
            c = Mix(c, new Color(0.85f, 0.66f, 0.3f, 1f), 0.75f);   // the gilt line
        if (d == 9 || d == 11)
            c = Mix(c, new Color(0f, 0f, 0f, c.A), 0.35f);
        foreach (var (sx, sy) in new[] { (15, 15), (W - 16, 15), (15, W - 16), (W - 16, W - 16) })
            c = Stud(c, x - sx, y - sy, 4.2f);
        return c;
    });

    /// <summary>An inner panel: a lighter, plain oak board with a thin dark rim.</summary>
    public static Texture2D InsetTexture => Make("inset", 48, 48, (x, y) =>
    {
        int d = Math.Min(Math.Min(x, y), Math.Min(47 - x, 47 - y));
        var c = Oak(x, y, new Color(0.24f, 0.16f, 0.1f, 0.9f), 21);
        if (d == 0)
            return new Color(0.07f, 0.045f, 0.03f, 1f);
        if (d == 1)
            return Mix(c, new Color(0, 0, 0, 1), Math.Min(x, y) == 1 ? 0.5f : 0.15f);
        return c;
    });

    /// <summary>Parchment: warm, mottled, a little burnt at the edges.</summary>
    public static Texture2D ParchmentTexture => Make("parchment", 64, 64, (x, y) =>
    {
        int d = Math.Min(Math.Min(x, y), Math.Min(63 - x, 63 - y));
        float mottle = Noise(x * 0.12f, y * 0.12f, 31) * 0.6f + Noise(x * 0.5f, y * 0.5f, 32) * 0.4f;
        var c = new Color(0.95f, 0.88f, 0.71f) * (0.9f + 0.12f * mottle);
        c = Mix(c, new Color(0.62f, 0.44f, 0.24f), Smooth(7f, 0f, d) * 0.65f);
        c.A = 1f;
        if (d == 0)
            return new Color(0.36f, 0.23f, 0.12f, 1f);
        return c;
    });

    /// <summary>A soft pool of light behind an item picture.</summary>
    public static Texture2D Spotlight => Make("spotlight", 128, 128, (x, y) =>
    {
        float dx = (x - 63.5f) / 64f, dy = (y - 70f) / 64f;
        float r = MathF.Sqrt(dx * dx + dy * dy);
        float a = Smooth(1f, 0.1f, r);
        return new Color(1f, 0.93f, 0.7f, 0.55f * a);
    });

    // ------------------------------------------------------------------ buttons
    /// <summary>A planked button in one of its states: normal, hover, pressed, disabled, or gold (the main action).</summary>
    public static Texture2D ButtonTexture(string state) => Make("button:" + state, 48, 32, (x, y) =>
    {
        bool gold = state.StartsWith("gold");
        var baseColor = state switch
        {
            "hover" => new Color(0.46f, 0.31f, 0.17f), "pressed" => new Color(0.24f, 0.15f, 0.09f),
            "disabled" => new Color(0.2f, 0.17f, 0.14f), "gold" => new Color(0.74f, 0.54f, 0.22f),
            "goldhover" => new Color(0.86f, 0.66f, 0.28f), "goldpressed" => new Color(0.6f, 0.42f, 0.17f),
            _ => new Color(0.34f, 0.22f, 0.13f),
        };
        int d = Math.Min(Math.Min(x, y), Math.Min(47 - x, 31 - y));
        if (d == 0)
            return state is "hover" or "goldhover" ? new Color(1f, 0.82f, 0.42f, 1f) : new Color(0.06f, 0.04f, 0.025f, 1f);
        var c = gold ? new Color(baseColor.R * (0.9f + 0.2f * Noise(x * 0.3f, y * 0.3f, 3)), baseColor.G * (0.9f + 0.2f * Noise(x * 0.3f, y * 0.3f, 3)), baseColor.B, 1f)
            : Oak(x, y, baseColor, 41);
        // Light from above: brighter at the top, darker at the foot, a bevel inside the rim.
        float v = y / 31f;
        float shade = state is "pressed" or "goldpressed" ? 0.85f + 0.2f * v : 1.18f - 0.36f * v;
        c = new Color(c.R * shade, c.G * shade, c.B * shade, 1f);
        if (d == 1)
        {
            bool top = Math.Min(x, y) == 1 && y <= x;
            bool pressed = state is "pressed" or "goldpressed";
            c = Mix(c, top ^ pressed ? new Color(1f, 0.9f, 0.7f, 1f) : new Color(0f, 0f, 0f, 1f), 0.35f);
        }
        if (state == "disabled")
        {
            float g = (c.R + c.G + c.B) / 3f;
            c = new Color(g, g * 0.95f, g * 0.88f, 1f);
        }
        return c;
    });

    /// <summary>The fill of a progress bar: gilded, brighter along the top.</summary>
    public static Texture2D BarFill => Make("barfill", 16, 12, (x, y) =>
    {
        float v = y / 11f;
        var c = new Color(1f, 0.84f, 0.42f).Lerp(new Color(0.7f, 0.46f, 0.15f), v);
        if (y == 2)
            c = c.Lerp(Colors.White, 0.45f);
        if (y == 0 || y == 11)
            c = c.Darkened(0.35f);
        return c;
    });

    public static Texture2D BarBack => Make("barback", 16, 12, (x, y) =>
    {
        bool rim = y == 0 || y == 11 || x == 0 || x == 15;
        return rim ? new Color(0.06f, 0.04f, 0.025f, 1f) : new Color(0.1f, 0.07f, 0.045f, 1f).Lerp(new Color(0.16f, 0.11f, 0.07f, 1f), y / 11f);
    });

    // ------------------------------------------------------------------ small pictures
    /// <summary>A gold coin with a fish stamped on it.</summary>
    public static Texture2D Coin => Make("coin", 32, 32, (x, y) =>
    {
        float dx = x - 15.5f, dy = y - 15.5f, r = MathF.Sqrt(dx * dx + dy * dy);
        if (r > 14.5f)
            return new Color(0, 0, 0, Math.Clamp(15.5f - r, 0f, 1f) * 0.6f);
        float light = 1.05f - 0.35f * (dx + dy) / 20f;
        var c = new Color(0.96f, 0.76f, 0.3f) * light;
        if (r > 12f)
            c = c.Darkened(0.25f);                                  // the rim
        // The stamped fish: an ellipse body and a tail.
        float fx = (x - 14f) / 7f, fy = (y - 16f) / 3.8f;
        bool fish = fx * fx + fy * fy < 1f || (x >= 20 && x <= 24 && MathF.Abs(y - 16f) < (x - 19) * 0.9f);
        if (fish)
            c = c.Darkened(0.28f);
        c.A = 1f;
        return c;
    });

    /// <summary>A padlock.</summary>
    public static Texture2D Lock => Make("lock", 32, 32, (x, y) =>
    {
        float dx = x - 15.5f;
        bool shackle = y >= 4 && y <= 15 && MathF.Abs(MathF.Sqrt(dx * dx + (y - 13f) * (y - 13f)) - 7f) < 2f && y <= 14;
        bool body = x >= 6 && x <= 25 && y >= 13 && y <= 28;
        if (body)
        {
            bool hole = (MathF.Abs(dx) < 2f && y >= 18 && y <= 24) || (dx * dx + (y - 18f) * (y - 18f) < 6f);
            var c = new Color(0.62f, 0.6f, 0.58f) * (1.15f - 0.3f * (y - 13) / 15f);
            if (x == 6 || x == 25 || y == 13 || y == 28)
                c = new Color(0.12f, 0.1f, 0.08f);
            c.A = 1f;
            return hole ? new Color(0.08f, 0.06f, 0.05f, 1f) : c;
        }
        return shackle ? new Color(0.72f, 0.7f, 0.68f, 1f) : new Color(0, 0, 0, 0);
    });

    /// <summary>A round green seal with a tick: owned.</summary>
    public static Texture2D Owned => Make("owned", 32, 32, (x, y) =>
    {
        float dx = x - 15.5f, dy = y - 15.5f, r = MathF.Sqrt(dx * dx + dy * dy);
        if (r > 14f)
            return new Color(0, 0, 0, Math.Clamp(15f - r, 0f, 1f) * 0.5f);
        var c = r > 12f ? new Color(0.12f, 0.25f, 0.1f, 1f) : new Color(0.35f, 0.62f, 0.25f, 1f) * (1.1f - 0.25f * (dy + 12f) / 24f);
        c.A = 1f;
        // The tick: two strokes.
        float d1 = DistToSegment(x, y, 9, 16, 14, 21), d2 = DistToSegment(x, y, 14, 21, 23, 10);
        if (MathF.Min(d1, d2) < 1.8f)
            c = new Color(0.97f, 0.97f, 0.9f, 1f);
        return c;
    });

    /// <summary>A gold star: the item is in use.</summary>
    public static Texture2D Star => Make("star", 32, 32, (x, y) =>
    {
        float dx = x - 15.5f, dy = y - 16.5f;
        float a = MathF.Atan2(dy, dx), r = MathF.Sqrt(dx * dx + dy * dy);
        float edge = 13f * (0.55f + 0.45f * MathF.Cos(5f * (a + MathF.PI / 2f)));
        if (r > edge + 1f)
            return new Color(0, 0, 0, 0);
        var c = new Color(1f, 0.82f, 0.3f) * (1.15f - 0.4f * (dy + 13f) / 26f);
        c.A = Math.Clamp(edge + 1f - r, 0f, 1f);
        return c;
    });

    private static float DistToSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        float vx = bx - ax, vy = by - ay;
        float t = Math.Clamp(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy), 0f, 1f);
        float qx = ax + t * vx - px, qy = ay + t * vy - py;
        return MathF.Sqrt(qx * qx + qy * qy);
    }

    // ------------------------------------------------------------------ styleboxes
    public static StyleBoxTexture Nine(Texture2D tex, int margin, float content = -1f, bool tile = false)
    {
        var sb = new StyleBoxTexture { Texture = tex };
        sb.TextureMarginLeft = sb.TextureMarginRight = sb.TextureMarginTop = sb.TextureMarginBottom = margin;
        if (content >= 0f)
            sb.ContentMarginLeft = sb.ContentMarginRight = sb.ContentMarginTop = sb.ContentMarginBottom = content;
        if (tile)
        {
            sb.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile;
            sb.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile;
        }
        return sb;
    }

    public static StyleBoxTexture Button(string state, float padX = 14f, float padY = 7f)
    {
        var sb = Nine(ButtonTexture(state), 6);
        sb.ContentMarginLeft = sb.ContentMarginRight = padX;
        sb.ContentMarginTop = sb.ContentMarginBottom = padY;
        return sb;
    }
}
