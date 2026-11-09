using System;
using Godot;

namespace FishingSim.World.Props;

/// <summary>
/// Small pixel-art textures (32×32, read with nearest filtering) for the medieval village: boards, plaster,
/// thatch, cobblestones. Generated once; materials map them at ~16 texels per metre.
/// </summary>
public static class Textures
{
    public const int Size = 32;

    /// <summary>Texture repeats per metre for triplanar mapping (32 px over 2 m = 16 px/m).</summary>
    public const float TriplanarScale = 0.5f;

    private static ImageTexture? _planks, _shingles, _planksPainted, _stone, _plaster;

    private static ImageTexture Make(Image img)
    {
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static Color Shade(Color c, float v) => new(c.R * v, c.G * v, c.B * v);

    /// <summary>Horizontal boards with knots and dark seams.</summary>
    public static ImageTexture Planks()
    {
        if (_planks != null)
            return _planks;
        var img = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator { Seed = 7 };
        var wood = new Color(0.52f, 0.36f, 0.22f);
        const int boards = 4, bh = Size / boards;
        for (int b = 0; b < boards; b++)
        {
            float tone = rng.RandfRange(0.82f, 1.08f);
            int knot = rng.RandiRange(2, Size - 3);
            for (int y = b * bh; y < (b + 1) * bh; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int ly = y - b * bh;
                    float v = tone;
                    if (ly == 0) v *= 0.5f;                                   // seam
                    else if (ly == 1) v *= 1.12f;                             // lit board edge
                    else if ((x + b * 5 + ly * 3) % 11 == 0) v *= 0.82f;     // grain streaks
                    if (Math.Abs(x - knot) <= 1 && ly == bh / 2) v *= 0.6f;  // knot
                    img.SetPixel(x, y, Shade(wood, v));
                }
            }
        }
        _planks = Make(img);
        return _planks;
    }

    /// <summary>The same boards in light grey, so a material colour can stain them.</summary>
    public static ImageTexture PlanksPainted()
    {
        if (_planksPainted != null)
            return _planksPainted;
        var src = Planks().GetImage();
        var img = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float v = Mathf.Clamp(src.GetPixel(x, y).R * 1.85f, 0f, 1f);
                img.SetPixel(x, y, new Color(v, v, v));
            }
        _planksPainted = Make(img);
        return _planksPainted;
    }

    /// <summary>Lime plaster for the timber-framed walls: off-white with speckles and a few cracks.</summary>
    public static ImageTexture Plaster()
    {
        if (_plaster != null)
            return _plaster;
        var img = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator { Seed = 3 };
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float v = 0.93f + rng.RandfRange(-0.04f, 0.04f);
                if (rng.Randf() < 0.05f) v -= 0.12f;
                img.SetPixel(x, y, new Color(v, v * 0.98f, v * 0.93f));
            }
        for (int i = 0; i < 6; i++)                                        // a hairline crack
            img.SetPixel(20 + i, 9 + i / 2, new Color(0.72f, 0.7f, 0.66f));
        _plaster = Make(img);
        return _plaster;
    }

    /// <summary>Thatch: rows of straw bundles (grey-gold, tinted by the roof colour).</summary>
    public static ImageTexture Shingles()
    {
        if (_shingles != null)
            return _shingles;
        var img = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator { Seed = 11 };
        const int rows = 4, rh = Size / rows;
        for (int r = 0; r < rows; r++)
            for (int x = 0; x < Size; x++)
            {
                float strand = rng.RandfRange(0.8f, 1.1f);
                int len = rng.RandiRange(rh - 3, rh);
                for (int ly = 0; ly < rh; ly++)
                {
                    int y = r * rh + ly;
                    float v = strand * (0.75f + 0.3f * ly / rh);
                    if (ly >= len) v *= 0.55f;                               // shadow under the row above
                    if (ly == 0) v *= 0.7f;
                    img.SetPixel(x, y, new Color(v, v * 0.9f, v * 0.66f));
                }
            }
        _shingles = Make(img);
        return _shingles;
    }

    /// <summary>Rough fieldstones with dark mortar, for foundations, chimneys and walls.</summary>
    public static ImageTexture Stone()
    {
        if (_stone != null)
            return _stone;
        var img = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator { Seed = 5 };
        const int rows = 4, rh = Size / rows;
        for (int r = 0; r < rows; r++)
        {
            int x = -rng.RandiRange(0, 6);
            while (x < Size)
            {
                int w = rng.RandiRange(6, 11);
                float tone = rng.RandfRange(0.7f, 1.05f);
                for (int lx = 0; lx < w; lx++)
                    for (int ly = 0; ly < rh; ly++)
                    {
                        int px = x + lx, py = r * rh + ly;
                        if (px < 0 || px >= Size)
                            continue;
                        bool mortar = lx == 0 || ly == 0;
                        float v = mortar ? 0.38f : tone * (ly == 1 || lx == 1 ? 1.1f : ly == rh - 1 ? 0.85f : 1f);
                        img.SetPixel(px, py, new Color(0.62f * v, 0.6f * v, 0.56f * v));
                    }
                x += w;
            }
        }
        _stone = Make(img);
        return _stone;
    }
}
