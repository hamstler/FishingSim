using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Data;

namespace FishingSim.Graphics;

/// <summary>Body markings painted on a species' skin texture.</summary>
public enum FishPattern { Plain, Bars, LightSpots, RedSpots, DarkSpots, Scales, Stripe, Scutes, Marbled }

/// <summary>
/// Pixel-art skins for the fish: a small texture per species (U = nose to tail, V = belly to back) painted with
/// vivid colours, a darker back, pale belly, lateral line, gill cover and the species' markings. Read with nearest
/// filtering, so the fish look like crisp pixel sprites wrapped on the 3D body.
/// </summary>
public static class FishSkin
{
    public const int Width = 96, Height = 32;

    private static readonly Dictionary<string, ImageTexture> Cache = new();

    private static readonly Dictionary<string, FishPattern> Patterns = new()
    {
        ["Okoń"] = FishPattern.Bars, ["Sandacz"] = FishPattern.Bars, ["Jazgarz"] = FishPattern.DarkSpots,
        ["Szczupak"] = FishPattern.LightSpots, ["Pstrąg potokowy"] = FishPattern.RedSpots, ["Palia"] = FishPattern.LightSpots,
        ["Troć jeziorowa"] = FishPattern.DarkSpots, ["Łosoś"] = FishPattern.DarkSpots, ["Głowacica"] = FishPattern.DarkSpots,
        ["Lipień"] = FishPattern.DarkSpots, ["Karp"] = FishPattern.Scales, ["Lin"] = FishPattern.Scales,
        ["Karaś złocisty"] = FishPattern.Scales, ["Karaś srebrzysty"] = FishPattern.Scales, ["Leszcz"] = FishPattern.Scales,
        ["Brzana"] = FishPattern.Scales, ["Jaź"] = FishPattern.Scales, ["Kleń"] = FishPattern.Scales,
        ["Ukleja"] = FishPattern.Stripe, ["Sielawa"] = FishPattern.Stripe, ["Sieja"] = FishPattern.Stripe,
        ["Różanka"] = FishPattern.Stripe, ["Strzebla potokowa"] = FishPattern.Bars, ["Kiełb"] = FishPattern.DarkSpots,
        ["Jesiotr ostronosy"] = FishPattern.Scutes, ["Sum"] = FishPattern.Marbled, ["Miętus"] = FishPattern.Marbled,
        ["Piskorz"] = FishPattern.Stripe, ["Głowacz białopłetwy"] = FishPattern.Marbled, ["Węgorz"] = FishPattern.Plain,
    };

    public static FishPattern PatternOf(FishSpecies sp) => Patterns.TryGetValue(sp.SpeciesName, out var p) ? p : FishPattern.Plain;

    /// <summary>Richer colours than the raw data: pixel art reads best saturated.</summary>
    public static Color Vivid(Color c, float saturation = 1.35f)
    {
        float l = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
        return new Color(
            Math.Clamp(l + (c.R - l) * saturation, 0f, 1f),
            Math.Clamp(l + (c.G - l) * saturation, 0f, 1f),
            Math.Clamp(l + (c.B - l) * saturation, 0f, 1f));
    }

    public static ImageTexture For(FishSpecies sp)
    {
        if (Cache.TryGetValue(sp.SpeciesName, out var tex))
            return tex;
        var back = Vivid(sp.BackColor) * 0.85f;
        var belly = Vivid(sp.BellyColor, 1.2f);
        var fin = Vivid(sp.FinColor, 1.5f);
        var pattern = PatternOf(sp);
        var rng = new RandomNumberGenerator { Seed = (ulong)Math.Abs(sp.SpeciesName.GetHashCode()) + 1 };
        var img = Image.CreateEmpty(Width, Height, false, Image.Format.Rgb8);
        for (int y = 0; y < Height; y++)
        {
            float v = 1f - (y + 0.5f) / Height;                  // 1 = top of the back, 0 = belly
            for (int x = 0; x < Width; x++)
            {
                float u = (x + 0.5f) / Width;                     // 0 = nose, 1 = tail
                // Countershading in three crisp bands with a one-pixel highlight along the flank.
                float band = v > 0.68f ? 0f : v > 0.42f ? 0.45f : 1f;
                var c = back.Lerp(belly, band);
                if (Math.Abs(v - 0.55f) < 0.5f / Height)
                    c = c.Lightened(0.25f);                       // lateral line
                if (u > 0.15f && u < 0.19f && v > 0.25f)
                    c = c.Darkened(0.25f);                        // gill cover edge
                c = Paint(pattern, c, fin, u, v, x, y, rng);
                img.SetPixel(x, y, c);
            }
        }
        tex = ImageTexture.CreateFromImage(img);
        Cache[sp.SpeciesName] = tex;
        return tex;
    }

    private static Color Paint(FishPattern p, Color c, Color fin, float u, float v, int x, int y, RandomNumberGenerator rng)
    {
        switch (p)
        {
            case FishPattern.Bars:
                // Dark vertical bars over the back and flank.
                if (u > 0.22f && u < 0.85f && v > 0.35f && ((x / 5) % 2 == 0) && (x % 5 != 4))
                    return c.Darkened(0.4f);
                return c;
            case FishPattern.LightSpots:
                if (v > 0.38f && v < 0.85f && u > 0.2f && Hash(x / 3, y / 3) > 0.72f)
                    return c.Lightened(0.45f);
                return c;
            case FishPattern.RedSpots:
                if (v > 0.35f && v < 0.9f && u > 0.18f && Hash(x / 2, y / 2) > 0.86f)
                    return Hash(x / 2 + 7, y / 2) > 0.5f ? new Color(0.9f, 0.15f, 0.1f) : new Color(0.08f, 0.06f, 0.05f);
                return c;
            case FishPattern.DarkSpots:
                if (v > 0.45f && u > 0.18f && Hash(x / 2, y / 2) > 0.85f)
                    return c.Darkened(0.55f);
                return c;
            case FishPattern.Scales:
                // A grid of scale outlines: each scale lighter in the middle, darker at the rim.
                int sx = (x + (y / 4 % 2) * 2) % 4, sy = y % 4;
                if (v > 0.2f && u > 0.18f && u < 0.92f)
                    return sx == 0 || sy == 0 ? c.Darkened(0.18f) : sx == 2 && sy == 2 ? c.Lightened(0.12f) : c;
                return c;
            case FishPattern.Stripe:
                if (Math.Abs(v - 0.52f) < 1.6f / Height && u > 0.15f)
                    return new Color(0.75f, 0.85f, 0.95f);
                return c;
            case FishPattern.Scutes:
                foreach (float row in new[] { 0.9f, 0.55f, 0.2f })
                    if (Math.Abs(v - row) < 1.5f / Height && x % 6 < 3)
                        return new Color(0.85f, 0.82f, 0.72f);
                return c;
            case FishPattern.Marbled:
                if (Hash(x / 3, y / 2) > 0.55f && v > 0.3f)
                    return c.Darkened(0.3f);
                return c;
            default:
                return c;
        }
    }

    private static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }
    }
}
