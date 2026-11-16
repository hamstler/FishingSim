using System;
using System.Collections.Generic;
using Godot;

namespace FishingSim.UI;

/// <summary>
/// The pictures the interface is built from, painted in code once and cached: see-through graphite-teal panels with a
/// hairline border and an amber line along the top, flat rounded buttons (amber for the main action), quiet inner
/// wells, amber bars, parchment cards, coins, locks and badges. Nine-patch textures keep their corners crisp at any size.
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
    /// <summary>
    /// Coverage (0..1, antialiased) of a rounded rectangle w x h with corner radius r at pixel (x, y), and the distance
    /// of the pixel's centre inside its edge (negative outside).
    /// </summary>
    private static (float Cover, float Inside) Rounded(int x, int y, int w, int h, float r)
    {
        float px = x + 0.5f, py = y + 0.5f;
        float qx = MathF.Max(MathF.Max(r - px, px - (w - r)), 0f), qy = MathF.Max(MathF.Max(r - py, py - (h - r)), 0f);
        float outside = MathF.Sqrt(qx * qx + qy * qy) - r;
        float edge = MathF.Min(MathF.Min(px, w - px), MathF.Min(py, h - py));
        float inside = qx > 0f && qy > 0f ? -outside : MathF.Min(edge, -outside);
        return (Math.Clamp(0.5f - outside, 0f, 1f), inside);
    }

    /// <summary>A flat, see-through card: a soft vertical gradient, a hairline border, a faint light along the top
    /// inner edge; <paramref name="accent"/> lays a thin amber stripe along the top.</summary>
    private static Color Card(int x, int y, int w, int h, float r, Color top, Color bottom, Color border, bool accent = false)
    {
        var (cover, inside) = Rounded(x, y, w, h, r);
        if (cover <= 0f)
            return new Color(0, 0, 0, 0);
        var c = top.Lerp(bottom, (y + 0.5f) / h);
        if (inside < 1.2f)
            c = c.Lerp(border, Math.Clamp(1.2f - inside, 0f, 1f));
        else if (y <= 2 && inside < 2.6f)
            c = c.Lerp(new Color(1f, 1f, 1f, c.A), 0.07f);
        if (accent && y >= 2 && y <= 3 && inside >= 1.5f)
            c = new Color(0.96f, 0.72f, 0.32f, 0.95f);
        c.A *= cover;
        return c;
    }

    /// <summary>The big panel: deep graphite-teal glass with a hairline border and an amber line along the top.</summary>
    public static Texture2D PanelTexture => Make("panel2", 96, 96, (x, y) =>
        Card(x, y, 96, 96, 9f, new Color(0.085f, 0.105f, 0.118f, 0.94f), new Color(0.05f, 0.062f, 0.07f, 0.95f),
            new Color(0.62f, 0.7f, 0.74f, 0.32f), accent: true));

    /// <summary>An inner panel: a slightly lighter, quieter well inside a panel.</summary>
    public static Texture2D InsetTexture => Make("inset2", 48, 48, (x, y) =>
        Card(x, y, 48, 48, 5f, new Color(1f, 1f, 1f, 0.05f), new Color(1f, 1f, 1f, 0.035f), new Color(1f, 1f, 1f, 0.12f)));

    /// <summary>The light card behind item pictures: a cool, softly lit studio grey, darker towards the edges.</summary>
    public static Texture2D ParchmentTexture => Make("parchment2", 64, 64, (x, y) =>
    {
        int d = Math.Min(Math.Min(x, y), Math.Min(63 - x, 63 - y));
        float grain = Noise(x * 0.2f, y * 0.2f, 31) * 0.5f + 0.5f;
        float lightFall = 1f - 0.12f * (y / 63f);
        var c = new Color(0.84f, 0.87f, 0.88f) * ((0.97f + 0.03f * grain) * lightFall);
        c = Mix(c, new Color(0.62f, 0.67f, 0.7f), Smooth(6f, 0f, d) * 0.5f);
        c.A = 1f;
        if (d == 0)
            return new Color(0.3f, 0.35f, 0.38f, 1f);
        return c;
    });

    /// <summary>A soft pool of light behind an item picture.</summary>
    public static Texture2D Spotlight => Make("spotlight", 128, 128, (x, y) =>
    {
        float dx = (x - 63.5f) / 64f, dy = (y - 70f) / 64f;
        float r = MathF.Sqrt(dx * dx + dy * dy);
        float a = Smooth(1f, 0.1f, r);
        return new Color(1f, 0.97f, 0.9f, 0.5f * a);
    });

    // ------------------------------------------------------------------ buttons
    /// <summary>A flat button in one of its states: normal, hover, pressed, disabled, or gold (the main action, amber).</summary>
    public static Texture2D ButtonTexture(string state) => Make("button2:" + state, 48, 32, (x, y) =>
    {
        var (top, bottom, border) = state switch
        {
            "hover" => (new Color(0.2f, 0.245f, 0.27f, 0.95f), new Color(0.15f, 0.185f, 0.205f, 0.95f), new Color(0.96f, 0.72f, 0.32f, 0.95f)),
            "pressed" => (new Color(0.09f, 0.11f, 0.125f, 0.95f), new Color(0.12f, 0.145f, 0.16f, 0.95f), new Color(0.96f, 0.72f, 0.32f, 0.8f)),
            "disabled" => (new Color(0.11f, 0.12f, 0.13f, 0.6f), new Color(0.09f, 0.1f, 0.11f, 0.6f), new Color(1f, 1f, 1f, 0.06f)),
            "gold" => (new Color(0.97f, 0.73f, 0.32f, 1f), new Color(0.84f, 0.56f, 0.2f, 1f), new Color(1f, 0.86f, 0.55f, 1f)),
            "goldhover" => (new Color(1f, 0.8f, 0.42f, 1f), new Color(0.92f, 0.64f, 0.26f, 1f), new Color(1f, 0.94f, 0.75f, 1f)),
            "goldpressed" => (new Color(0.78f, 0.52f, 0.18f, 1f), new Color(0.86f, 0.6f, 0.24f, 1f), new Color(0.96f, 0.76f, 0.4f, 1f)),
            _ => (new Color(0.155f, 0.188f, 0.207f, 0.92f), new Color(0.112f, 0.137f, 0.152f, 0.92f), new Color(0.72f, 0.8f, 0.84f, 0.22f)),
        };
        return Card(x, y, 48, 32, 5f, top, bottom, border);
    });

    /// <summary>The fill of a progress bar: amber, a little brighter along the top.</summary>
    public static Texture2D BarFill => Make("barfill2", 16, 12, (x, y) =>
    {
        var c = new Color(1f, 0.78f, 0.38f).Lerp(new Color(0.86f, 0.55f, 0.18f), y / 11f);
        return Card(x, y, 16, 12, 3f, c, c, c.Lightened(0.25f));
    });

    public static Texture2D BarBack => Make("barback2", 16, 12, (x, y) =>
        Card(x, y, 16, 12, 3f, new Color(0f, 0f, 0f, 0.45f), new Color(0f, 0f, 0f, 0.35f), new Color(1f, 1f, 1f, 0.16f)));

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
