using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Data;

namespace FishingSim.Graphics;

/// <summary>
/// Painted pixel-art postcards of the fishing grounds, 320×180, shown with nearest filtering on the map selection, the
/// login slideshow and in the shop. Every map has its own composition built around its landmarks:
/// the green lake with the ruined tower on its island and the chapel on the hill, the reed marsh with the hut on stilts
/// and a heron, the carp ponds seen from above with the poplar dike, sluice and windmill, and the mountain lake with
/// its waterfall, snowy peaks and rock islet. Dusk versions are graded from the same picture.
/// </summary>
public static class Postcard
{
    public const int Width = 320, Height = 180;
    private static readonly Dictionary<string, ImageTexture> Cache = new();

    private static Color C(float r, float g, float b) => new(r, g, b);

    public static ImageTexture For(LocationData loc, float hour = 10f)
    {
        string key = $"{loc.ResourcePath}:{loc.DisplayName}:{hour}";
        if (Cache.TryGetValue(key, out var t))
            return t;
        t = ImageTexture.CreateFromImage(Paint(loc, hour));
        Cache[key] = t;
        return t;
    }

    /// <summary>Which composition a map gets (by its resource file name, then by its name).</summary>
    public static string SceneFor(LocationData loc)
    {
        string id = loc.ResourcePath.GetFile().GetBaseName();
        if (id is "jezioro" or "zatoka" or "staw" or "gory")
            return id;
        if (loc.Squareness > 1.5f) return "staw";
        if (loc.HillScale > 1.8f) return "gory";
        if (loc.ReedDensity > 1.5f) return "zatoka";
        return "jezioro";
    }

    public static Image Paint(LocationData loc, float hour)
    {
        var cv = new Canvas(loc, hour >= 17.5f || hour <= 6.5f);
        switch (SceneFor(loc))
        {
            case "zatoka": Marsh(cv); break;
            case "staw": FishPonds(cv); break;
            case "gory": Mountains(cv); break;
            default: GreenLake(cv); break;
        }
        cv.Finish();
        return cv.Img;
    }

    // ================================================================== the canvas
    private sealed class Canvas
    {
        public readonly Image Img = Image.CreateEmpty(Width, Height, false, Image.Format.Rgb8);
        public readonly LocationData Loc;
        public readonly bool Dusk;
        public readonly RandomNumberGenerator Rng;
        public readonly FastNoiseLite Noise;
        private readonly bool[] _sky = new bool[Width * Height];
        private readonly List<Rect2I> _windows = new();

        /// <summary>While set, drawing goes into the water below this line, mirrored, faded and broken by ripples.</summary>
        public int? MirrorY;

        public Canvas(LocationData loc, bool dusk)
        {
            Loc = loc;
            Dusk = dusk;
            Rng = new RandomNumberGenerator { Seed = (ulong)Math.Abs(loc.DisplayName.GetHashCode()) + 3 };
            Noise = new FastNoiseLite { Seed = (int)(Rng.Randi() % 10000), Frequency = 0.02f, FractalOctaves = 3 };
        }

        public bool In(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public Color Get(int x, int y) => Img.GetPixel(Math.Clamp(x, 0, Width - 1), Math.Clamp(y, 0, Height - 1));

        public void Set(int x, int y, Color c)
        {
            if (MirrorY is int m)
            {
                int ty = 2 * m - y - 1;
                if (ty < m || !In(x, ty) || ((ty - m) & 3) == 3)
                    return;
                Img.SetPixel(x, ty, Img.GetPixel(x, ty).Lerp(c.Darkened(0.2f), 0.55f));
                return;
            }
            if (In(x, y))
                Img.SetPixel(x, y, c);
        }

        public void SetSky(int x, int y, Color c)
        {
            if (!In(x, y))
                return;
            Img.SetPixel(x, y, c);
            _sky[y * Width + x] = true;
        }

        public void Window(int x, int y, int w, int h)
        {
            _windows.Add(new Rect2I(x, y, w, h));
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    Set(xx, yy, C(0.2f, 0.22f, 0.26f));
        }

        /// <summary>Dusk: warm, darker grade of everything but the sky, then lit windows.</summary>
        public void Finish()
        {
            if (!Dusk)
                return;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    if (_sky[y * Width + x])
                        continue;
                    var c = Img.GetPixel(x, y);
                    Img.SetPixel(x, y, new Color(c.R * 0.74f + 0.07f, c.G * 0.56f + 0.03f, c.B * 0.62f + 0.07f));
                }
            foreach (var r in _windows)
                for (int yy = r.Position.Y; yy < r.End.Y; yy++)
                    for (int xx = r.Position.X; xx < r.End.X; xx++)
                        if (In(xx, yy))
                            Img.SetPixel(xx, yy, (xx + yy) % 3 == 0 ? C(1f, 0.92f, 0.6f) : C(1f, 0.74f, 0.32f));
        }
    }

    // ================================================================== shared pieces
    private static void Sky(Canvas cv, int horizon, int sunX, int sunY, int clouds, int cloudMaxY, bool cold = false)
    {
        var top = cv.Dusk ? C(0.2f, 0.2f, 0.42f) : cold ? C(0.18f, 0.42f, 0.78f) : C(0.25f, 0.52f, 0.84f);
        var low = cv.Dusk ? C(0.98f, 0.6f, 0.36f) : cold ? C(0.7f, 0.86f, 0.96f) : C(0.74f, 0.89f, 0.97f);
        for (int y = 0; y < horizon; y++)
        {
            float k = MathF.Floor(y / (float)horizon * 7f) / 6f;          // flat bands, like hand pixelling
            var c = top.Lerp(low, k);
            for (int x = 0; x < Width; x++)
            {
                // Dithered seam between bands.
                bool seam = y > 0 && MathF.Floor((y - 1) / (float)horizon * 7f) != MathF.Floor(y / (float)horizon * 7f);
                cv.SetSky(x, y, seam && (x & 1) == 0 ? top.Lerp(low, MathF.Max(k - 1f / 6f, 0f)) : c);
            }
        }
        if (cv.Dusk) { sunY = horizon - 18; }
        Disc(cv, sunX, sunY, 10, cv.Dusk ? C(1f, 0.8f, 0.45f) : C(1f, 0.97f, 0.82f), sky: true);
        Disc(cv, sunX, sunY, 7, cv.Dusk ? C(1f, 0.9f, 0.6f) : C(1f, 1f, 0.94f), sky: true);
        for (int i = 0; i < clouds; i++)
        {
            int cx = (int)cv.Rng.RandfRange(-10, Width - 30), cy = (int)cv.Rng.RandfRange(8, cloudMaxY);
            int puffs = (int)cv.Rng.RandfRange(3, 7);
            for (int k = 0; k < puffs; k++)
            {
                int bx = cx + k * 8 + (int)cv.Rng.RandfRange(-3, 3);
                int r = (int)cv.Rng.RandfRange(5, 11);
                Disc(cv, bx, cy, r, cv.Dusk ? C(0.98f, 0.74f, 0.62f) : C(1f, 1f, 0.98f), 0.55f, sky: true);
                Disc(cv, bx, cy + r / 2 + 1, r - 2, cv.Dusk ? C(0.74f, 0.5f, 0.56f) : C(0.8f, 0.88f, 0.96f), 0.55f, bottomOnly: true, sky: true);
            }
        }
    }

    /// <summary>Soft distant ridge from <paramref name="baseY"/> up by <paramref name="height"/> pixels.</summary>
    private static void Ridge(Canvas cv, int baseY, float height, Color c, float freq, float seed)
    {
        for (int x = 0; x < Width; x++)
        {
            float n = cv.Noise.GetNoise1D(x * freq + seed) * 0.5f + 0.5f;
            int top = baseY - (int)(height * (0.35f + 0.65f * n));
            for (int y = Math.Max(top, 0); y < baseY; y++)
                cv.Set(x, y, c);
        }
    }

    /// <summary>A jagged peak: lit left flank, shaded right flank, snow above <paramref name="snow"/> of its height.</summary>
    private static void Mountain(Canvas cv, int px, int py, int halfW, int baseY, Color lit, Color shade, float snow, float seed)
    {
        for (int x = px - halfW; x <= px + halfW; x++)
        {
            float t = MathF.Abs(x - px) / (float)halfW;
            int jag = (int)(cv.Noise.GetNoise1D(x * 4f + seed) * 5f);
            int top = py + (int)((baseY - py) * MathF.Pow(t, 0.85f)) + (t > 0.05f ? jag : 0);
            int snowLine = py + (int)((baseY - py) * snow) + (int)(cv.Noise.GetNoise1D(x * 9f + seed + 40f) * 6f);
            for (int y = Math.Max(top, 0); y < baseY; y++)
            {
                bool left = x < px + (int)(cv.Noise.GetNoise1D(y * 5f + seed) * 3f);
                var c = left ? lit : shade;
                if (y < snowLine)
                    c = left ? C(0.96f, 0.97f, 1f) : C(0.72f, 0.78f, 0.9f);
                else if (cv.Noise.GetNoise2D(x * 9f + seed * 50f, y * 9f) > 0.3f)
                    c = c.Darkened(0.1f);                                       // rock texture
                cv.Set(x, y, c);
            }
        }
    }

    /// <summary>Water between two rows, mirroring everything above <paramref name="top"/>.</summary>
    private static void Water(Canvas cv, int top, int bottom, Color far, Color near, float reflect = 0.45f)
    {
        for (int y = top; y < bottom; y++)
        {
            float k = MathF.Floor((y - top) / (float)(bottom - top) * 5f) / 4f;
            var c = far.Lerp(near, Math.Clamp(k, 0f, 1f));
            int src = top - 1 - (y - top);
            for (int x = 0; x < Width; x++)
            {
                var px = c;
                if (src >= 0)
                {
                    int sx = x + (int)(MathF.Sin(y * 0.9f) * 1.5f);                 // ripples shift the reflection
                    px = c.Lerp(cv.Get(sx, src), reflect * (1f - 0.6f * k));
                }
                if (((y * 5 + x / 7) % 9) == 0)
                    px = px.Lightened(0.07f);
                cv.Set(x, y, px);
            }
        }
        for (int i = 0; i < 50; i++)
        {
            int gx = (int)cv.Rng.RandfRange(0, Width - 8), gy = (int)cv.Rng.RandfRange(top + 3, bottom - 1);
            int len = (int)cv.Rng.RandfRange(2, 7);
            for (int k = 0; k < len; k++)
                cv.Set(gx + k, gy, C(0.92f, 0.97f, 1f));
        }
    }

    private static void Rect(Canvas cv, int x0, int y0, int x1, int y1, Color c)
    {
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                cv.Set(x, y, c);
    }

    private static void Ellipse(Canvas cv, int cx, int cy, int rx, int ry, Color c)
    {
        rx = Math.Max(rx, 1); ry = Math.Max(ry, 1);
        for (int dy = -ry; dy <= ry; dy++)
            for (int dx = -rx; dx <= rx; dx++)
                if (dx * dx / (float)(rx * rx) + dy * dy / (float)(ry * ry) <= 1f)
                    cv.Set(cx + dx, cy + dy, c);
    }

    private static void Disc(Canvas cv, int cx, int cy, int r, Color c, float squash = 1f, bool bottomOnly = false, bool sky = false)
    {
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                if (bottomOnly && y < 0)
                    continue;
                float yy = y / squash;
                if (x * x + yy * yy > r * r)
                    continue;
                if (sky) cv.SetSky(cx + x, cy + y, c);
                else cv.Set(cx + x, cy + y, c);
            }
    }

    private static void Line(Canvas cv, int x0, int y0, int x1, int y1, Color c)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            cv.Set(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static void Pine(Canvas cv, int x, int baseY, int h, Color dark, Color light)
    {
        Rect(cv, x, baseY - 2, x + 1, baseY, C(0.3f, 0.2f, 0.12f));
        for (int y = 0; y < h; y++)
        {
            int half = (int)(y * 0.36f) + 1 - ((y % 5 == 0 && y > 4) ? 2 : 0);
            for (int dx = -half; dx <= half; dx++)
                cv.Set(x + dx, baseY - 2 - h + y, dx < 0 ? light : dark);
        }
    }

    private static void Leafy(Canvas cv, int x, int baseY, int h, Color dark, Color light)
    {
        int r = Math.Max(h / 2, 2);
        Rect(cv, x, baseY - h / 2, x + 2, baseY, C(0.32f, 0.22f, 0.14f));
        Disc(cv, x, baseY - h + r, r, dark);
        Disc(cv, x - 1, baseY - h + r - 1, r - 2, light);
        Disc(cv, x + r / 2, baseY - h + r + 1, r / 2, dark);
    }

    private static void Willow(Canvas cv, int x, int baseY, int h, Color dark, Color light)
    {
        int r = Math.Max(h / 2, 3);
        Rect(cv, x, baseY - h / 2, x + 2, baseY, C(0.3f, 0.22f, 0.15f));
        Disc(cv, x, baseY - h + r, r, dark, 0.8f);
        Disc(cv, x - 2, baseY - h + r - 1, r - 3, light, 0.8f);
        for (int dx = -r; dx <= r; dx += 2)                                   // hanging curtains of twigs
            for (int y = 0; y < r + 2; y++)
                cv.Set(x + dx, baseY - h + r + y, (dx / 2 & 1) == 0 ? dark : light);
    }

    private static void Poplar(Canvas cv, int x, int baseY, int h, Color dark, Color light)
    {
        Rect(cv, x, baseY - 3, x + 1, baseY, C(0.32f, 0.24f, 0.18f));
        int rx = Math.Max(h / 7, 2);
        Ellipse(cv, x, baseY - 3 - h / 2, rx, h / 2, dark);
        Ellipse(cv, x - 1, baseY - 4 - h / 2, Math.Max(rx - 1, 1), h / 2 - 2, light);
        Ellipse(cv, x + 1, baseY - 2 - h / 2, Math.Max(rx - 2, 1), h / 2 - 4, dark);
    }

    private static void Treeline(Canvas cv, int baseY, int minH, int maxH, int step, float conifers, bool willows, Color dark, Color light)
    {
        for (int x = -4; x < Width + 4; x += step + (int)cv.Rng.RandfRange(0, step))
        {
            int h = (int)cv.Rng.RandfRange(minH, maxH);
            if (cv.Rng.Randf() < conifers) Pine(cv, x, baseY, h, dark, light);
            else if (willows && cv.Rng.Randf() < 0.6f) Willow(cv, x, baseY, h, dark, light);
            else Leafy(cv, x, baseY, h, dark, light);
        }
    }

    private static void Reed(Canvas cv, int x, int baseY, int h, Color c, float slant, bool head)
    {
        for (int y = 0; y < h; y++)
            cv.Set(x + (int)(slant * y), baseY - y, c);
        if (head)
            Rect(cv, x + (int)(slant * h) , baseY - h - 1, x + (int)(slant * h) + 2, baseY - h + 4, C(0.42f, 0.25f, 0.12f));
    }

    private static void Birds(Canvas cv, int x, int y, int count)
    {
        var c = cv.Dusk ? C(0.2f, 0.12f, 0.15f) : C(0.18f, 0.2f, 0.24f);
        for (int i = 0; i < count; i++)
        {
            int bx = x + i * 9 + (int)cv.Rng.RandfRange(-2, 2), by = y + Math.Abs(i - count / 2) * 4 + (int)cv.Rng.RandfRange(-1, 2);
            for (int k = 0; k < 3; k++)
            {
                cv.SetSky(bx - k, by - (k == 2 ? 1 : 0) + (k == 0 ? 0 : 0), c);
                cv.SetSky(bx + k, by - (k == 2 ? 1 : 0), c);
            }
        }
    }

    private static (Color Dark, Color Light) Foliage(LocationData loc) => (loc.DarkGrassColor.Darkened(0.2f), loc.GrassColor.Darkened(0.05f));

    private static float ConiferShare(LocationData loc)
    {
        float total = 0f, conifers = 0f;
        foreach (var k in loc.TreeMix.Keys)
        {
            float w = loc.TreeMix[k].AsSingle();
            total += w;
            if (k.AsString() is "spruce" or "pine")
                conifers += w;
        }
        return total > 0f ? conifers / total : 0.4f;
    }

    // ================================================================== Jezioro Zielone: ruin on the island, chapel on the hill
    private static void GreenLake(Canvas cv)
    {
        var loc = cv.Loc;
        const int horizon = 100;
        Sky(cv, horizon, 160, 28, 4, 50);
        Ridge(cv, horizon, 16, C(0.48f, 0.6f, 0.62f).Lerp(loc.DarkGrassColor, 0.3f), 1.1f, 0f);
        var (dark, light) = Foliage(loc);
        Treeline(cv, horizon + 1, 5, 10, 4, ConiferShare(loc), false, dark.Lightened(0.12f), light.Lightened(0.1f));

        // A rounded green hill on the right with the white chapel and its spire.
        var grass = loc.GrassColor;
        for (int x = 196; x < Width; x++)
        {
            float t = Math.Min((x - 196) / 150f, 1f);
            int top = horizon - (int)(26f * MathF.Sin(MathF.PI * t * 0.95f) + cv.Noise.GetNoise1D(x * 6f) * 2f);
            for (int y = top; y <= horizon + 1; y++)
                cv.Set(x, y, ((x + y) % 7 == 0) ? grass.Darkened(0.12f) : grass.Lightened(0.04f));
        }
        Chapel(cv, 256, 76);
        Leafy(cv, 232, 84, 14, dark, light);
        Leafy(cv, 296, 80, 16, dark, light);
        Pine(cv, 310, 82, 18, dark, light);

        Water(cv, horizon + 2, Height, C(0.55f, 0.75f, 0.85f).Lerp(loc.WaterShallowColor, 0.3f), loc.WaterDeepColor.Lerp(C(0.1f, 0.3f, 0.42f), 0.6f));

        // The island with the ruined tower, and its reflection.
        for (int pass = 0; pass < 2; pass++)
        {
            cv.MirrorY = pass == 0 ? 118 : null;
            Ellipse(cv, 118, 117, 34, 5, loc.SandColor);
            Ellipse(cv, 116, 115, 30, 5, grass);
            Leafy(cv, 94, 116, 11, dark, light);
            Leafy(cv, 140, 116, 9, dark, light);
            Willow(cv, 148, 117, 10, dark, light);
            RuinedTower(cv, 112, 115);
            Ellipse(cv, 176, 115, 6, 2, loc.SandColor);
            Leafy(cv, 176, 114, 6, dark, light);
        }
        cv.MirrorY = null;

        // Rowboat moored off the bank.
        for (int pass = 0; pass < 2; pass++)
        {
            cv.MirrorY = pass == 0 ? 146 : null;
            Rowboat(cv, 214, 146);
        }
        cv.MirrorY = null;

        // Grassy bank in the lower left with reeds, flowers and a tall birch framing the picture.
        for (int x = 0; x < Width; x++)
        {
            int edge = 136 + (int)(x * 0.26f) + (int)(cv.Noise.GetNoise1D(x * 5f + 30f) * 3f);
            for (int y = Math.Max(edge, 0); y < Height; y++)
            {
                var c = y < edge + 3 ? loc.SandColor.Lightened(0.08f) : ((x * 7 + y * 13) % 11 == 0 ? grass.Darkened(0.15f) : grass);
                cv.Set(x, y, c);
            }
            if (x % 3 == 0 && x < 170 && cv.Rng.Randf() < 0.6f)
                Reed(cv, x, edge + 1, (int)cv.Rng.RandfRange(6, 15), C(0.38f, 0.48f, 0.2f), -0.15f, cv.Rng.Randf() < 0.25f);
        }
        Color[] flowers = { C(0.97f, 0.97f, 0.9f), C(0.98f, 0.84f, 0.2f), C(0.92f, 0.42f, 0.6f) };
        for (int i = 0; i < 40; i++)
        {
            int fx = (int)cv.Rng.RandfRange(0, 150), fy = (int)cv.Rng.RandfRange(150, Height);
            if (fy > 140 + fx * 0.26f + 4)
                cv.Set(fx, fy, flowers[i % flowers.Length]);
        }
        Birch(cv, 22, 172, 165, dark, light);
    }

    private static void Chapel(Canvas cv, int x, int baseY)
    {
        var wall = C(0.95f, 0.93f, 0.86f);
        var shade = C(0.78f, 0.75f, 0.7f);
        var roof = C(0.45f, 0.26f, 0.18f);
        Rect(cv, x, baseY - 12, x + 22, baseY, wall);                               // nave
        Rect(cv, x + 16, baseY - 12, x + 22, baseY, shade);
        for (int y = 0; y < 8; y++)                                                 // roof
            Rect(cv, x - 2 + y, baseY - 20 + y, x + 24 - y, baseY - 19 + y, (y & 1) == 0 ? roof : roof.Darkened(0.15f));
        Rect(cv, x - 8, baseY - 26, x, baseY, wall);                                // tower
        Rect(cv, x - 3, baseY - 26, x, baseY, shade);
        for (int y = 0; y < 12; y++)                                                // spire
            Rect(cv, x - 8 + y / 3, baseY - 38 + y, x - y / 3, baseY - 37 + y, roof.Darkened(0.1f));
        Rect(cv, x - 5, baseY - 44, x - 4, baseY - 37, C(0.2f, 0.17f, 0.15f));       // cross
        Rect(cv, x - 7, baseY - 42, x - 2, baseY - 41, C(0.2f, 0.17f, 0.15f));
        Rect(cv, x - 6, baseY - 22, x - 2, baseY - 18, C(0.15f, 0.12f, 0.1f));       // belfry
        Rect(cv, x - 6, baseY - 6, x - 2, baseY, C(0.35f, 0.24f, 0.16f));            // door
        cv.Window(x + 5, baseY - 9, 3, 5);
        cv.Window(x + 12, baseY - 9, 3, 5);
    }

    private static void RuinedTower(Canvas cv, int x, int baseY)
    {
        var stone = C(0.78f, 0.75f, 0.68f);
        var shade = C(0.56f, 0.54f, 0.5f);
        int[] crown = { 4, 1, 6, 6, 2, 0, 3, 5, 5, 2, 1, 4, 3 };
        for (int dx = 0; dx < 13; dx++)
        {
            int top = baseY - 30 - crown[dx];
            for (int y = top; y < baseY; y++)
            {
                var c = dx < 4 ? stone.Lightened(0.06f) : dx > 9 ? shade : stone;
                if ((y + dx * 3) % 6 == 0 || (y % 4 == 0 && (dx + y / 4) % 3 == 0))
                    c = c.Darkened(0.14f);                                           // block joints
                cv.Set(x + dx, y, c);
            }
        }
        Rect(cv, x + 5, baseY - 6, x + 8, baseY, C(0.1f, 0.08f, 0.07f));           // doorway
        Rect(cv, x + 3, baseY - 20, x + 4, baseY - 16, C(0.1f, 0.08f, 0.07f));      // arrow slits
        Rect(cv, x + 9, baseY - 26, x + 10, baseY - 22, C(0.1f, 0.08f, 0.07f));
        var ivy = C(0.22f, 0.4f, 0.16f);
        for (int y = 0; y < 16; y++)                                                // ivy up the left side
            for (int dx = 0; dx < 3 + (y % 3); dx++)
                if ((y * 5 + dx) % 4 != 0)
                    cv.Set(x + dx, baseY - 2 - y, ivy);
        Rect(cv, x + 14, baseY - 2, x + 17, baseY, shade);                          // fallen blocks
        Rect(cv, x - 3, baseY - 1, x - 1, baseY, stone);
    }

    private static void Rowboat(Canvas cv, int x, int y)
    {
        var hull = C(0.62f, 0.24f, 0.16f);
        for (int i = 0; i < 4; i++)
            Rect(cv, x - 12 + i * 2, y - 4 + i, x + 12 - i * 2, y - 3 + i, i == 0 ? C(0.75f, 0.6f, 0.42f) : hull);
        Line(cv, x - 4, y - 4, x + 10, y - 9, C(0.6f, 0.45f, 0.3f));                // oar
        Line(cv, x - 16, y - 2, x - 26, y - 8, C(0.85f, 0.82f, 0.75f));             // mooring rope
    }

    private static void Birch(Canvas cv, int x, int baseY, int h, Color dark, Color light)
    {
        for (int y = 0; y < h; y++)
        {
            int bx = x + (int)(MathF.Sin(y * 0.02f) * 3f);
            for (int dx = 0; dx < 4; dx++)
            {
                var c = dx == 3 ? C(0.75f, 0.74f, 0.7f) : C(0.95f, 0.94f, 0.9f);
                if ((y * 7 + dx * 3) % 11 == 0 || (y % 9 == 0 && dx < 3))
                    c = C(0.15f, 0.14f, 0.13f);                                       // black marks of the bark
                cv.Set(bx + dx, baseY - y, c);
            }
        }
        // A compact crown at the top edge of the frame: dark mass first, sunlit clumps on top.
        var leaf = light.Lightened(0.15f);
        (int X, int Y, int R)[] clumps = { (-6, 2, 12), (10, -2, 11), (26, 4, 9), (40, 0, 8), (4, 14, 8), (20, 16, 6) };
        foreach (var (cx, cy, r) in clumps)
            Disc(cv, x + cx, cy, r, dark);
        foreach (var (cx, cy, r) in clumps)
            Disc(cv, x + cx - 2, cy - 2, r - 4, leaf);
        Line(cv, x + 2, 26, x + 22, 12, C(0.9f, 0.88f, 0.84f));                       // a branch
    }

    // ================================================================== Zatoka Szuwarowa: marsh, stilt hut, heron
    private static void Marsh(Canvas cv)
    {
        var loc = cv.Loc;
        const int horizon = 118;
        Sky(cv, horizon, 248, 62, 7, 70);
        Birds(cv, 70, 34, 5);
        Birds(cv, 150, 52, 3);
        var (dark, light) = Foliage(loc);
        // Flat far shore: a low band of willows and alders.
        Rect(cv, 0, horizon - 3, Width, horizon, dark.Lerp(C(0.5f, 0.6f, 0.62f), 0.4f));
        Treeline(cv, horizon, 6, 14, 6, 0.1f, true, dark.Lerp(C(0.5f, 0.6f, 0.62f), 0.3f), light.Lerp(C(0.6f, 0.7f, 0.66f), 0.3f));

        var water = loc.WaterShallowColor.Lerp(C(0.45f, 0.58f, 0.5f), 0.5f);
        Water(cv, horizon, Height, C(0.66f, 0.78f, 0.82f).Lerp(water, 0.35f), water.Darkened(0.2f), 0.5f);
        // Morning mist over the water.
        for (int y = horizon - 10; y < horizon + 8; y++)
            for (int x = 0; x < Width; x++)
                if (((x + y * 3) & 1) == 0 || Math.Abs(y - horizon) < 3)
                    cv.Set(x, y, cv.Get(x, y).Lerp(C(0.88f, 0.9f, 0.86f), 0.35f));

        // Reed islands with their reflections.
        (int X, int Y, int Rx)[] islands = { (56, 128, 24), (176, 124, 30), (128, 138, 14) };
        for (int pass = 0; pass < 2; pass++)
        {
            cv.MirrorY = null;
            foreach (var (ix, iy, rx) in islands)
            {
                cv.MirrorY = pass == 0 ? iy + 1 : null;
                Ellipse(cv, ix, iy, rx, 2, C(0.42f, 0.38f, 0.22f));
                for (int dx = -rx + 2; dx < rx - 1; dx += 2)
                    Reed(cv, ix + dx, iy, 5 + (int)(MathF.Abs(MathF.Sin(dx * 1.7f + ix)) * 10f), (dx & 2) == 0 ? C(0.5f, 0.55f, 0.24f) : C(0.66f, 0.6f, 0.3f), 0.1f, dx % 6 == 0);
            }
        }
        cv.MirrorY = null;

        for (int pass = 0; pass < 2; pass++)
        {
            cv.MirrorY = pass == 0 ? 136 : null;
            StiltHut(cv, 236, 136);
            cv.MirrorY = pass == 0 ? 156 : null;
            Heron(cv, 96, 156);
        }
        cv.MirrorY = null;

        // Lily pads in the near water.
        for (int i = 0; i < 14; i++)
        {
            int lx = (int)cv.Rng.RandfRange(100, 230), ly = (int)cv.Rng.RandfRange(150, 172);
            Ellipse(cv, lx, ly, 4, 1, C(0.25f, 0.45f, 0.18f));
            if (i % 4 == 0)
                Rect(cv, lx, ly - 2, lx + 2, ly, C(0.98f, 0.7f, 0.82f));
        }
        // Tall reeds and cattails framing the bottom corners.
        for (int x = 0; x < Width; x += 2)
        {
            float side = x < 100 ? 1f - x / 100f : x > 220 ? (x - 220) / 100f : 0f;
            if (side <= 0f && cv.Rng.Randf() < 0.85f)
                continue;
            int h = (int)(12 + side * 58 + cv.Rng.RandfRange(-6, 8));
            var c = cv.Rng.Randf() < 0.3f ? C(0.62f, 0.56f, 0.28f) : cv.Rng.Randf() < 0.5f ? C(0.32f, 0.44f, 0.16f) : C(0.42f, 0.52f, 0.2f);
            Reed(cv, x, Height - 1, h, c, cv.Rng.RandfRange(-0.12f, 0.18f), cv.Rng.Randf() < 0.15f * (side + 0.3f));
        }
    }

    private static void StiltHut(Canvas cv, int x, int water)
    {
        var post = C(0.3f, 0.2f, 0.12f);
        var plank = C(0.62f, 0.46f, 0.3f);
        foreach (int px in new[] { x + 2, x + 12, x + 24, x + 34 })
            Rect(cv, px, water - 18, px + 2, water + 2, post);
        Rect(cv, x - 2, water - 19, x + 40, water - 16, plank.Darkened(0.2f));          // deck
        for (int y = water - 36; y < water - 19; y++)                                   // walls
            for (int dx = 4; dx < 30; dx++)
                cv.Set(x + dx, y, (y % 3 == 0) ? plank.Darkened(0.18f) : dx > 24 ? plank.Darkened(0.1f) : plank);
        for (int y = 0; y < 14; y++)                                                    // thatched roof
        {
            int inset = 13 - y;
            for (int dx = inset - 2; dx < 36 - inset; dx++)
                cv.Set(x + dx, water - 50 + y, (dx + y) % 4 == 0 ? C(0.66f, 0.52f, 0.28f) : C(0.82f, 0.68f, 0.38f));
        }
        Rect(cv, x + 8, water - 30, x + 13, water - 19, C(0.16f, 0.11f, 0.08f));         // door
        cv.Window(x + 18, water - 31, 5, 4);
        Rect(cv, x + 38, water - 26, x + 39, water - 16, post);                         // railing post
        Rect(cv, x - 2, water - 24, x + 40, water - 23, post);
        for (int y = water - 16; y < water; y += 3)                                     // ladder
            Rect(cv, x + 36, y, x + 41, y + 1, post);
        for (int i = 0; i < 4; i++)                                                     // punt
            Rect(cv, x + 44 + i, water - 3 + i, x + 70 - i, water - 2 + i, i == 0 ? C(0.62f, 0.5f, 0.34f) : C(0.38f, 0.3f, 0.2f));
        Line(cv, x + 66, water - 3, x + 74, water - 22, C(0.55f, 0.42f, 0.28f));         // pole
    }

    private static void Heron(Canvas cv, int x, int water)
    {
        var grey = C(0.66f, 0.68f, 0.72f);
        var darkGrey = C(0.36f, 0.38f, 0.42f);
        Line(cv, x, water, x + 1, water - 10, C(0.55f, 0.45f, 0.3f));                   // legs
        Line(cv, x + 3, water, x + 3, water - 10, C(0.55f, 0.45f, 0.3f));
        Ellipse(cv, x + 2, water - 13, 6, 3, grey);                                      // body
        Rect(cv, x - 4, water - 13, x + 3, water - 11, darkGrey);                        // wing tip
        for (int i = 0; i < 9; i++)                                                      // S-shaped neck
            cv.Set(x + 6 + (int)(MathF.Sin(i * 0.6f) * 2f), water - 15 - i, i < 3 ? grey : C(0.9f, 0.9f, 0.92f));
        Rect(cv, x + 6, water - 26, x + 9, water - 23, C(0.92f, 0.92f, 0.94f));         // head
        cv.Set(x + 6, water - 26, C(0.1f, 0.1f, 0.12f));                                 // crest
        cv.Set(x + 5, water - 27, C(0.1f, 0.1f, 0.12f));
        Line(cv, x + 9, water - 24, x + 14, water - 23, C(0.85f, 0.7f, 0.25f));          // beak
    }

    // ================================================================== Staw Karpiowy: ponds from above, poplar dike, sluice
    private static void FishPonds(Canvas cv)
    {
        var loc = cv.Loc;
        const int horizon = 64;
        Sky(cv, horizon, 182, 22, 4, 40);
        Ridge(cv, horizon, 10, C(0.5f, 0.6f, 0.66f), 1.6f, 7f);
        var (dark, light) = Foliage(loc);

        // Patchwork fields with hedgerows, getting taller towards us.
        Color[] fields = { C(0.8f, 0.7f, 0.32f), loc.GrassColor.Lightened(0.08f), C(0.62f, 0.66f, 0.28f), loc.GrassColor, C(0.86f, 0.78f, 0.42f) };
        int fy = horizon, band = 3, fi = 0;
        while (fy < 102)
        {
            int split = 30 + (int)(cv.Rng.Randf() * 260);
            for (int y = fy; y < Math.Min(fy + band, 102); y++)
                for (int x = 0; x < Width; x++)
                {
                    var c = fields[(fi + (x > split ? 2 : 0)) % fields.Length];
                    if (((x + y * 2) % 9) == 0) c = c.Darkened(0.08f);                    // furrows
                    cv.Set(x, y, c);
                }
            for (int x = 0; x < Width; x += 2)                                            // hedgerow
                if (cv.Rng.Randf() < 0.7f)
                    cv.Set(x, fy, dark);
            fy += band;
            band += 2;
            fi++;
        }
        for (int i = 0; i < 8; i++)
            Leafy(cv, (int)cv.Rng.RandfRange(0, Width), (int)cv.Rng.RandfRange(70, 90), (int)cv.Rng.RandfRange(5, 9), dark, light);
        Barn(cv, 262, 90);
        Windmill(cv, 46, 96);
        foreach (int hx in new[] { 72, 84, 78 })
        {
            Ellipse(cv, hx, 97 - (hx == 78 ? 3 : 0), 5, 4, C(0.86f, 0.7f, 0.3f));
            Rect(cv, hx - 5, 97 - (hx == 78 ? 3 : 0), hx + 5, 99 - (hx == 78 ? 3 : 0), C(0.66f, 0.52f, 0.2f));
        }

        // Meadow under everything in front of the fields.
        Rect(cv, 0, 102, Width, Height, loc.GrassColor.Darkened(0.04f));
        for (int i = 0; i < 300; i++)
            cv.Set((int)cv.Rng.RandfRange(0, Width), (int)cv.Rng.RandfRange(102, Height), loc.GrassColor.Darkened(0.15f));

        // The big pond, a trapezoid in perspective, with its far dike and row of poplars mirrored in the water.
        var bank = loc.GrassColor.Lightened(0.06f);
        const int far = 106;
        int Left(int y) => 98 - (int)((y - far) * 1.05f);
        int Right(int y) => 302 + (int)((y - far) * 0.75f);
        Rect(cv, 90, far - 6, 312, far, bank);
        Rect(cv, 90, far - 5, 312, far - 4, loc.PathColor);
        var waterFar = C(0.62f, 0.78f, 0.86f).Lerp(loc.WaterShallowColor, 0.25f);
        var waterNear = loc.WaterShallowColor.Lerp(C(0.15f, 0.36f, 0.4f), 0.5f);
        for (int y = far; y < Height; y++)
        {
            float k = MathF.Floor((y - far) / (float)(Height - far) * 5f) / 4f;
            var c = waterFar.Lerp(waterNear, k);
            for (int x = Left(y); x < Right(y); x++)
                cv.Set(x, y, ((y * 5 + x / 6) % 11) == 0 ? c.Lightened(0.07f) : c);
            for (int dx = 0; dx < 5; dx++)                                              // straight banks
            {
                cv.Set(Left(y) - dx, y, dx == 4 ? loc.PathColor : bank);
                cv.Set(Right(y) + dx, y, dx == 4 ? loc.PathColor : bank);
            }
        }
        for (int pass = 0; pass < 2; pass++)
        {
            cv.MirrorY = pass == 0 ? far : null;
            for (int x = 100, i = 0; x < 306; x += 14, i++)
                if (Math.Abs(x - 205) > 8)
                    Poplar(cv, x, far - 5, 22 + (i * 5 % 7), dark, light);
            Rect(cv, 200, far - 9, 210, far + 1, C(0.7f, 0.68f, 0.62f));                  // sluice tower on the dike
            Rect(cv, 202, far - 7, 208, far, C(0.45f, 0.33f, 0.22f));
        }
        cv.MirrorY = null;
        // The smaller nursery pond to the left, beyond a narrow bank.
        for (int y = 106; y < 124; y++)
            for (int x = 0; x < Left(y) - 10; x++)
                cv.Set(x, y, x > Left(y) - 15 || y < 108 ? bank : waterFar.Lerp(waterNear, (y - 106) / 30f));
        // Angling platforms along the left bank.
        foreach (int py in new[] { 126, 150 })
        {
            int px = Left(py) + 1;
            Rect(cv, px, py, px + 12, py + 4, C(0.62f, 0.46f, 0.3f));
            Rect(cv, px + 10, py + 4, px + 11, py + 7, C(0.35f, 0.24f, 0.15f));
        }
        // Monk sluice in the foreground with a plank bridge to the right bank.
        Rect(cv, 238, 132, 256, 170, C(0.72f, 0.7f, 0.64f));
        Rect(cv, 252, 132, 256, 170, C(0.55f, 0.53f, 0.5f));
        for (int y = 140; y < 168; y += 4)
            Rect(cv, 241, y, 251, y + 3, C(0.45f, 0.32f, 0.2f));
        Rect(cv, 256, 134, Right(134) + 2, 137, C(0.6f, 0.44f, 0.28f));
        Ellipse(cv, 247, 130, 5, 1, C(0.25f, 0.25f, 0.28f));
        Rect(cv, 238, 170, 256, 172, C(0.85f, 0.92f, 0.95f));                           // water at its foot
        // A carp jumping, and rings where another went down.
        for (int i = 0; i < 14; i++)
        {
            float a = i / 13f * MathF.PI;
            int jx = 150 + i, jy = 132 - (int)(MathF.Sin(a) * 8f);
            Rect(cv, jx, jy, jx + 2, jy + 3, i < 3 ? C(0.6f, 0.38f, 0.12f) : C(0.86f, 0.62f, 0.22f));
        }
        Ellipse(cv, 156, 136, 9, 2, C(0.85f, 0.93f, 0.96f));
        Ellipse(cv, 156, 136, 6, 1, waterNear.Lerp(waterFar, 0.5f));
        Ellipse(cv, 120, 160, 12, 3, C(0.8f, 0.9f, 0.95f));
        Ellipse(cv, 120, 160, 9, 2, waterNear);
    }

    private static void Windmill(Canvas cv, int x, int baseY)
    {
        for (int y = 0; y < 30; y++)
        {
            int half = 7 - y / 6;
            for (int dx = -half; dx <= half; dx++)
                cv.Set(x + dx, baseY - y, dx > half - 3 ? C(0.72f, 0.7f, 0.64f) : C(0.92f, 0.9f, 0.84f));
        }
        Rect(cv, x - 2, baseY - 6, x + 2, baseY, C(0.38f, 0.26f, 0.18f));
        cv.Window(x - 1, baseY - 18, 2, 3);
        for (int y = 0; y < 6; y++)
            Rect(cv, x - 6 + y, baseY - 36 + y, x + 7 - y, baseY - 35 + y, C(0.42f, 0.24f, 0.16f));
        int hx = x, hy = baseY - 32;
        for (int k = 0; k < 4; k++)
        {
            float a = k * MathF.PI / 2f + 0.5f;
            float cs = MathF.Cos(a), sn = MathF.Sin(a);
            for (int r = 0; r < 24; r++)
            {
                int sx = hx + (int)(cs * r), sy = hy + (int)(sn * r);
                cv.Set(sx, sy, C(0.4f, 0.28f, 0.18f));
                if (r > 6)
                    for (int w = 1; w <= 3; w++)
                        cv.Set(sx + (int)(-sn * w), sy + (int)(cs * w), (r + w) % 3 == 0 ? C(0.55f, 0.42f, 0.3f) : C(0.92f, 0.86f, 0.74f));
            }
        }
    }

    private static void Barn(Canvas cv, int x, int baseY)
    {
        Rect(cv, x, baseY - 12, x + 28, baseY, C(0.62f, 0.18f, 0.13f));
        Rect(cv, x + 22, baseY - 12, x + 28, baseY, C(0.48f, 0.14f, 0.1f));
        for (int y = 0; y < 7; y++)
            Rect(cv, x - 2 + y, baseY - 19 + y, x + 30 - y, baseY - 18 + y, C(0.4f, 0.4f, 0.44f));
        Rect(cv, x + 10, baseY - 8, x + 17, baseY, C(0.92f, 0.9f, 0.84f));
        Rect(cv, x + 11, baseY - 7, x + 16, baseY, C(0.42f, 0.28f, 0.2f));
    }

    // ================================================================== Górskie Jezioro: peaks, waterfall, rock islet
    private static void Mountains(Canvas cv)
    {
        var loc = cv.Loc;
        const int horizon = 112;
        Sky(cv, horizon, 52, 24, 3, 30, cold: true);
        // Far snowy range, then the darker valley walls that close in on the lake.
        var farLit = C(0.62f, 0.68f, 0.8f); var farShade = C(0.46f, 0.52f, 0.66f);
        Mountain(cv, 66, 26, 86, horizon, farLit, farShade, 0.32f, 1f);
        Mountain(cv, 160, 10, 96, horizon, farLit, farShade, 0.36f, 2f);
        Mountain(cv, 248, 30, 80, horizon, farLit, farShade, 0.3f, 3f);
        Mountain(cv, 316, 44, 60, horizon, farLit, farShade, 0.25f, 4f);
        var midLit = C(0.46f, 0.5f, 0.46f); var midShade = C(0.3f, 0.34f, 0.34f);
        Mountain(cv, 28, 50, 110, horizon, midLit, midShade, 0.12f, 5f);
        Mountain(cv, 282, 44, 104, horizon, midLit, midShade, 0.14f, 6f);
        var (dark, light) = Foliage(loc);
        // Forest on the lower slopes, thinning towards the valley in the middle.
        for (int i = 0; i < 70; i++)
        {
            int px = (int)cv.Rng.RandfRange(-4, Width + 4);
            if (px > 128 && px < 196)
                continue;
            int baseY = horizon - (int)cv.Rng.RandfRange(0, 22);
            Pine(cv, px, baseY, (int)cv.Rng.RandfRange(5, 10), dark.Darkened(0.1f), light.Darkened(0.15f));
        }
        // The waterfall pouring out of the right valley wall, with spray at its foot.
        for (int y = 58; y < horizon; y++)
        {
            int wx = 226 + (int)(MathF.Sin(y * 0.15f) * 0.8f);
            for (int dx = -4; dx <= 4; dx++)
                cv.Set(wx + dx, y, Math.Abs(dx) > 2 ? C(0.24f, 0.26f, 0.28f) : ((y + dx * 4) % 5 == 0 ? C(0.62f, 0.8f, 0.92f) : C(0.92f, 0.96f, 1f)));
        }
        Ellipse(cv, 226, horizon - 1, 12, 3, C(0.95f, 0.97f, 1f));
        Ellipse(cv, 222, horizon - 4, 7, 2, C(0.86f, 0.92f, 0.98f));

        Water(cv, horizon, Height, C(0.5f, 0.66f, 0.82f), loc.WaterDeepColor.Lerp(C(0.08f, 0.28f, 0.42f), 0.6f), 0.55f);

        // Rock islet with a lone wind-bent pine, mirrored.
        for (int pass = 0; pass < 2; pass++)
        {
            cv.MirrorY = pass == 0 ? 134 : null;
            Ellipse(cv, 132, 131, 14, 6, C(0.52f, 0.52f, 0.55f));
            Ellipse(cv, 128, 128, 8, 6, C(0.66f, 0.66f, 0.68f));
            Ellipse(cv, 141, 131, 6, 4, C(0.4f, 0.4f, 0.44f));
            Ellipse(cv, 130, 124, 6, 2, C(0.32f, 0.44f, 0.22f));
            Pine(cv, 131, 124, 16, dark, light);
            Ellipse(cv, 160, 132, 4, 2, C(0.5f, 0.5f, 0.54f));
        }
        cv.MirrorY = null;

        // Rocky shore and boulders in the lower left, big pines framing both sides.
        for (int x = 0; x < 150; x++)
        {
            int edge = 160 + (int)(x * 0.12f) + (int)(cv.Noise.GetNoise1D(x * 6f + 70f) * 4f);
            for (int y = edge; y < Height; y++)
                cv.Set(x, y, ((x * 5 + y * 3) % 7 == 0) ? C(0.42f, 0.42f, 0.4f) : y < edge + 3 ? C(0.62f, 0.6f, 0.56f) : loc.GrassColor.Darkened(0.1f));
        }
        foreach (var (bx, by, r) in new[] { (60, 166, 7), (78, 170, 5), (104, 172, 6), (40, 172, 4) })
        {
            Disc(cv, bx, by, r, C(0.5f, 0.5f, 0.52f), 0.7f);
            Disc(cv, bx - 1, by - 1, r - 2, C(0.66f, 0.66f, 0.68f), 0.7f);
        }
        Pine(cv, 14, Height + 2, 150, dark.Darkened(0.15f), light.Darkened(0.1f));
        Pine(cv, 306, Height + 2, 130, dark.Darkened(0.2f), light.Darkened(0.15f));
    }
}
