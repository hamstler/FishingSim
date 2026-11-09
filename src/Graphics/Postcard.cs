using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Data;

namespace FishingSim.Graphics;

/// <summary>
/// Painted pixel-art postcards of the fishing grounds, generated from each map's own data (colours, hills, tree mix,
/// reeds, rocks, buildings): sky with clouds, far hills (snowy peaks in the mountains), the tree line, the lake with
/// reflections and glints, the shore, reeds, the pier and a thatched cottage, a windmill on the farm. Used by the map
/// selection, the login screen and the coaching stop. 320×180, shown with nearest filtering.
/// </summary>
public static class Postcard
{
    public const int Width = 320, Height = 180;
    private static readonly Dictionary<string, ImageTexture> Cache = new();

    private static Color C(float r, float g, float b) => new(r, g, b);

    public static ImageTexture For(LocationData loc, float hour = 10f)
    {
        string key = $"{loc.ResourcePath}:{hour}";
        if (Cache.TryGetValue(key, out var t))
            return t;
        t = ImageTexture.CreateFromImage(Paint(loc, hour));
        Cache[key] = t;
        return t;
    }

    public static Image Paint(LocationData loc, float hour)
    {
        var img = Image.CreateEmpty(Width, Height, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator { Seed = (ulong)Math.Abs(loc.DisplayName.GetHashCode()) + 3 };
        var noise = new FastNoiseLite { Seed = (int)(rng.Randi() % 10000), Frequency = 0.02f, FractalOctaves = 3 };
        bool dusk = hour >= 17.5f || hour <= 6.5f;
        int horizon = 92;

        // --- sky (banded gradient, sun, clouds) ---
        var skyTop = dusk ? C(0.2f, 0.22f, 0.45f) : C(0.25f, 0.52f, 0.84f);
        var skyLow = dusk ? C(0.98f, 0.62f, 0.38f) : C(0.72f, 0.88f, 0.97f);
        for (int y = 0; y < horizon; y++)
        {
            float k = MathF.Floor(y / (float)horizon * 7f) / 6f;          // flat bands, like hand pixelling
            var c = skyTop.Lerp(skyLow, k);
            for (int x = 0; x < Width; x++)
                img.SetPixel(x, y, c);
        }
        int sunX = dusk ? 230 : 250, sunY = dusk ? 70 : 26;
        Disc(img, sunX, sunY, 9, dusk ? C(1f, 0.82f, 0.45f) : C(1f, 0.97f, 0.82f));
        for (int i = 0; i < 4; i++)
        {
            int cx = (int)rng.RandfRange(10, Width - 40), cy = (int)rng.RandfRange(10, 50);
            for (int k = 0; k < 5; k++)
            {
                int bx = cx + k * 8 + (int)rng.RandfRange(-3, 3);
                int r = (int)rng.RandfRange(6, 11);
                Disc(img, bx, cy, r, dusk ? C(0.98f, 0.75f, 0.62f) : C(1f, 1f, 0.98f), 0.55f);
                Disc(img, bx, cy + r / 2 + 1, r - 2, dusk ? C(0.75f, 0.52f, 0.55f) : C(0.78f, 0.87f, 0.95f), 0.55f, bottomOnly: true);
            }
        }

        // --- far hills / mountains ---
        float hills = Math.Clamp(loc.HillScale, 0.3f, 3f);
        var hillCol = C(0.42f, 0.55f, 0.6f).Lerp(loc.DarkGrassColor, 0.35f);
        if (dusk) hillCol = hillCol.Darkened(0.35f);
        for (int x = 0; x < Width; x++)
        {
            float n = noise.GetNoise1D(x * 1.2f) * 0.5f + 0.5f;
            int top = horizon - (int)(8 + n * 22 * hills);
            if (hills > 1.8f)
                top -= (int)(MathF.Abs(MathF.Sin(x * 0.045f)) * 18);      // peaks
            for (int y = Math.Max(top, 0); y < horizon; y++)
            {
                var c = hillCol;
                if (hills > 1.8f && y < top + 5 && top < horizon - 30)
                    c = C(0.95f, 0.96f, 0.98f);                              // snow caps
                img.SetPixel(x, y, c);
            }
        }

        // --- tree line on the far shore ---
        float conifers = loc.TreeMix.Count == 0 ? 0.3f : 0f;
        float total = 0f;
        foreach (var k in loc.TreeMix.Keys)
        {
            float w = loc.TreeMix[k].AsSingle();
            total += w;
            if (k.AsString() is "spruce" or "pine")
                conifers += w;
        }
        conifers = total > 0f ? conifers / total : 0.4f;
        bool willows = loc.TreeMix.ContainsKey("willow");
        var treeDark = loc.DarkGrassColor.Darkened(dusk ? 0.55f : 0.25f);
        var treeLight = loc.GrassColor.Darkened(dusk ? 0.4f : 0.1f);
        int treeBase = horizon + 2;
        for (int x = -6; x < Width + 6; x += (int)(10 - Math.Min(loc.TreeDensity, 1.5f) * 4))
        {
            int h = (int)rng.RandfRange(10, 20);
            if (rng.Randf() < conifers)
            {
                for (int y = 0; y < h; y++)
                {
                    int half = (int)(y * 0.45f) + 1;
                    for (int dx = -half; dx <= half; dx++)
                        Set(img, x + dx, treeBase - h + y, dx < 0 ? treeLight : treeDark);
                }
            }
            else
            {
                int r = h / 2;
                Disc(img, x, treeBase - r, r, treeDark);
                Disc(img, x - 1, treeBase - r - 1, r - 2, treeLight);
                if (willows)
                    for (int dx = -r; dx <= r; dx += 2)
                        for (int y = 0; y < r; y++)
                            Set(img, x + dx, treeBase - r + y, treeDark);
            }
        }

        // --- lake with reflections and glints ---
        int shoreY = 146;
        var deep = loc.WaterDeepColor.Lerp(C(0.1f, 0.3f, 0.45f), 0.6f);
        var shallow = loc.WaterShallowColor.Lerp(C(0.25f, 0.55f, 0.65f), 0.5f);
        if (dusk) { deep = deep.Lerp(C(0.25f, 0.2f, 0.35f), 0.4f); shallow = shallow.Lerp(C(0.8f, 0.5f, 0.4f), 0.3f); }
        for (int y = treeBase; y < shoreY; y++)
        {
            float k = MathF.Floor((y - treeBase) / (float)(shoreY - treeBase) * 4f) / 3f;
            var c = deep.Lerp(shallow, k);
            for (int x = 0; x < Width; x++)
            {
                var px = c;
                if (y < treeBase + 6 && (x + y) % 3 != 0)
                    px = c.Lerp(treeDark, 0.45f);                            // tree reflection
                img.SetPixel(x, y, px);
            }
        }
        for (int i = 0; i < 60; i++)
        {
            int gx = (int)rng.RandfRange(0, Width - 8), gy = (int)rng.RandfRange(treeBase + 6, shoreY - 2);
            int len = (int)rng.RandfRange(3, 9);
            for (int k = 0; k < len; k++)
                Set(img, gx + k, gy, dusk ? C(1f, 0.75f, 0.5f) : C(0.92f, 0.97f, 1f));
        }

        // --- near shore ---
        var sand = loc.SandColor.Lightened(0.1f);
        var grass = loc.GrassColor;
        for (int y = shoreY; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float edge = noise.GetNoise1D(x * 3f) * 4f;
                var c = y < shoreY + 8 + edge ? sand : grass.Darkened(((x * 7 + y * 13) % 11 == 0) ? 0.15f : 0f);
                if (dusk) c = c.Darkened(0.3f);
                img.SetPixel(x, y, c);
            }
        // Reeds in the marsh.
        int reeds = (int)(loc.ReedDensity * 40);
        for (int i = 0; i < reeds; i++)
        {
            int rx = (int)rng.RandfRange(0, Width), ry = (int)rng.RandfRange(shoreY - 6, shoreY + 6);
            int rh = (int)rng.RandfRange(8, 20);
            for (int y = 0; y < rh; y++)
                Set(img, rx, ry - y, C(0.35f, 0.45f, 0.2f));
            Set(img, rx, ry - rh, C(0.4f, 0.25f, 0.12f));
            Set(img, rx, ry - rh + 1, C(0.4f, 0.25f, 0.12f));
        }
        // Rocks.
        int rocks = (int)(loc.RockDensity * 6);
        for (int i = 0; i < rocks; i++)
            Disc(img, (int)rng.RandfRange(0, Width), shoreY + (int)rng.RandfRange(0, 10), (int)rng.RandfRange(3, 7), C(0.55f, 0.53f, 0.5f));

        // Pier from the bottom centre out into the lake.
        var plank = dusk ? C(0.5f, 0.32f, 0.18f) : C(0.72f, 0.48f, 0.26f);
        for (int y = 126; y < Height; y++)
        {
            float k = (y - 126) / (float)(Height - 126);
            int half = (int)(8 + k * 26);
            for (int x = 160 - half; x <= 160 + half; x++)
                Set(img, x, y, ((y - 126) % 5 == 0) ? plank.Darkened(0.3f) : plank);
        }
        for (int x = 152; x <= 168; x += 16)
            for (int y = 112; y < 128; y++)
                Set(img, x, y, C(0.3f, 0.2f, 0.12f));                         // mooring posts

        // A thatched cottage on the shore (and the windmill on the farm).
        Cottage(img, 40, shoreY + 16, dusk);
        foreach (var b in loc.Buildings)
            if (b.StartsWith("windmill"))
                Windmill(img, 270, shoreY + 18);
        return img;
    }

    private static void Cottage(Image img, int x, int baseY, bool dusk)
    {
        var wall = dusk ? C(0.6f, 0.55f, 0.48f) : C(0.92f, 0.88f, 0.78f);
        var beam = C(0.32f, 0.2f, 0.12f);
        for (int y = baseY - 18; y < baseY; y++)
            for (int dx = 0; dx < 34; dx++)
                Set(img, x + dx, y, dx % 11 == 0 || y == baseY - 18 ? beam : wall);
        for (int y = 0; y < 14; y++)
        {
            int inset = 13 - y;                                      // narrow ridge on top, wide eaves below
            for (int dx = -4 + inset; dx < 38 - inset; dx++)
                Set(img, x + dx, baseY - 18 - 14 + y, (dx + y) % 4 == 0 ? C(0.7f, 0.55f, 0.3f) : C(0.86f, 0.7f, 0.4f));
        }
        for (int y = baseY - 12; y < baseY - 6; y++)
            for (int dx = 22; dx < 28; dx++)
                Set(img, x + dx, y, dusk ? C(1f, 0.8f, 0.4f) : C(0.25f, 0.3f, 0.35f));
    }

    private static void Windmill(Image img, int x, int baseY)
    {
        for (int y = baseY - 34; y < baseY; y++)
            for (int dx = -5; dx <= 5; dx++)
                Set(img, x + dx, y, C(0.85f, 0.82f, 0.74f));
        for (int k = 0; k < 4; k++)
        {
            float a = k * MathF.PI / 2f + 0.4f;
            for (int r = 0; r < 20; r++)
                Set(img, x + (int)(MathF.Cos(a) * r), baseY - 34 + (int)(MathF.Sin(a) * r), C(0.45f, 0.3f, 0.18f));
        }
    }

    private static void Set(Image img, int x, int y, Color c)
    {
        if (x >= 0 && y >= 0 && x < Width && y < Height)
            img.SetPixel(x, y, c);
    }

    private static void Disc(Image img, int cx, int cy, int r, Color c, float squash = 1f, bool bottomOnly = false)
    {
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                if (bottomOnly && y < 0)
                    continue;
                float yy = y / squash;
                if (x * x + yy * yy <= r * r)
                    Set(img, cx + x, cy + (int)(y), c);
            }
    }
}
