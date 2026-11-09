using Godot;

namespace FishingSim.World.Props;

/// <summary>Small procedural textures shared by the pier, buildings and props (generated once).</summary>
public static class Textures
{
    private static ImageTexture? _planks;
    private static ImageTexture? _shingles;
    private static ImageTexture? _planksPainted;
    private static ImageTexture? _stone;

    /// <summary>Horizontal wooden boards (7 per metre) with grain and darker seams.</summary>
    public static ImageTexture Planks()
    {
        if (_planks != null)
            return _planks;
        var img = Image.CreateEmpty(128, 128, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator { Seed = 7 };
        const int boards = 7;
        int bh = 128 / boards;
        for (int b = 0; b <= boards; b++)
        {
            float tone = rng.RandfRange(0.8f, 1.1f);
            for (int y = b * bh; y < System.Math.Min((b + 1) * bh, 128); y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    float grain = 0.92f + 0.08f * Mathf.Sin(x * 0.35f + b * 3f) + rng.RandfRange(-0.03f, 0.03f);
                    float edge = (y - b * bh) < 2 ? 0.55f : 1f;
                    float v = tone * grain * edge;
                    img.SetPixel(x, y, new Color(0.5f * v, 0.36f * v, 0.22f * v));
                }
            }
        }
        img.GenerateMipmaps();
        _planks = ImageTexture.CreateFromImage(img);
        return _planks;
    }

    /// <summary>The same boards in grey, so a material colour can paint them any colour.</summary>
    public static ImageTexture PlanksPainted()
    {
        if (_planksPainted != null)
            return _planksPainted;
        var src = Planks().GetImage();
        var img = Image.CreateEmpty(128, 128, false, Image.Format.Rgb8);
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                float v = Mathf.Clamp(src.GetPixel(x, y).R * 1.9f, 0f, 1f);
                img.SetPixel(x, y, new Color(v, v, v));
            }
        }
        img.GenerateMipmaps();
        _planksPainted = ImageTexture.CreateFromImage(img);
        return _planksPainted;
    }

    /// <summary>Roof shingles: rows of tiles, every other row shifted.</summary>
    public static ImageTexture Shingles()
    {
        if (_shingles != null)
            return _shingles;
        var img = Image.CreateEmpty(128, 128, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator { Seed = 11 };
        const int rows = 8, cols = 6;
        int rh = 128 / rows, cw = 128 / cols;
        for (int r = 0; r < rows; r++)
        {
            int shift = (r % 2) * (cw / 2);
            for (int c = -1; c <= cols; c++)
            {
                float tone = rng.RandfRange(0.82f, 1.08f);
                for (int y = r * rh; y < (r + 1) * rh; y++)
                {
                    for (int x = c * cw + shift; x < (c + 1) * cw + shift; x++)
                    {
                        if (x < 0 || x >= 128)
                            continue;
                        float seam = (y - r * rh) < 2 || (x - (c * cw + shift)) < 2 ? 0.6f : 1f;
                        float fade = 0.9f + 0.1f * (y - r * rh) / rh;
                        float v = tone * seam * fade;
                        img.SetPixel(x, y, new Color(v, v, v));
                    }
                }
            }
        }
        img.GenerateMipmaps();
        _shingles = ImageTexture.CreateFromImage(img);
        return _shingles;
    }

    /// <summary>Irregular grey stones with mortar, for foundations and chimneys.</summary>
    public static ImageTexture Stone()
    {
        if (_stone != null)
            return _stone;
        var noise = new FastNoiseLite { Seed = 5, Frequency = 0.06f, NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular };
        var src = noise.GetSeamlessImage(128, 128);
        var img = Image.CreateEmpty(128, 128, false, Image.Format.Rgb8);
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                float v = 0.55f + 0.45f * src.GetPixel(x, y).R;
                img.SetPixel(x, y, new Color(v, v * 0.98f, v * 0.94f));
            }
        }
        img.GenerateMipmaps();
        _stone = ImageTexture.CreateFromImage(img);
        return _stone;
    }
}
