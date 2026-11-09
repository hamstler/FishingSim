using System;
using Godot;

namespace FishingSim.World.Props;

/// <summary>
/// Pixel-art textures for the medieval village (boards, plaster, thatch, fieldstone), generated at start-up and read
/// with nearest filtering. Each tile is <see cref="Size"/>² pixels covering 2 m, i.e. fine pixel detail (128 px/m)
/// rather than chunky blocks. Built from byte arrays, so generation stays fast.
/// </summary>
public static class Textures
{
    public const int Size = 256;

    /// <summary>Texture repeats per metre for triplanar mapping (one tile = 2 m).</summary>
    public const float TriplanarScale = 0.5f;

    private static ImageTexture? _planks, _shingles, _planksPainted, _stone, _plaster;

    private static readonly FastNoiseLite Grain = new() { Seed = 3, Frequency = 0.05f, FractalOctaves = 3 };

    private sealed class Canvas
    {
        public readonly byte[] Data = new byte[Size * Size * 3];

        public void Set(int x, int y, float r, float g, float b)
        {
            x = ((x % Size) + Size) % Size;
            y = ((y % Size) + Size) % Size;
            int i = (y * Size + x) * 3;
            Data[i] = (byte)(Math.Clamp(r, 0f, 1f) * 255f);
            Data[i + 1] = (byte)(Math.Clamp(g, 0f, 1f) * 255f);
            Data[i + 2] = (byte)(Math.Clamp(b, 0f, 1f) * 255f);
        }

        public ImageTexture Texture()
        {
            var img = Image.CreateFromData(Size, Size, false, Image.Format.Rgb8, Data);
            img.GenerateMipmaps();
            return ImageTexture.CreateFromImage(img);
        }
    }

    /// <summary>Seamless noise in [-1, 1] on the tile.</summary>
    private static float Tile(float x, float y, float scale, int seed)
    {
        float a = x / Size * Mathf.Tau, b = y / Size * Mathf.Tau;
        Grain.Seed = seed;
        return Grain.GetNoise3D(MathF.Cos(a) * scale, MathF.Sin(a) * scale + MathF.Cos(b) * scale, MathF.Sin(b) * scale);
    }

    /// <summary>Horizontal boards with wavy grain, knots and dark seams.</summary>
    public static ImageTexture Planks()
    {
        if (_planks != null)
            return _planks;
        var c = new Canvas();
        var rng = new RandomNumberGenerator { Seed = 7 };
        const int boards = 7, bh = Size / boards + 1;
        for (int b = 0; b < boards; b++)
        {
            float tone = rng.RandfRange(0.82f, 1.08f);
            float kx = rng.RandfRange(0f, Size), ky = b * bh + bh * 0.5f;
            int joint = rng.RandiRange(20, Size - 20);
            for (int y = b * bh; y < Math.Min((b + 1) * bh, Size); y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int ly = y - b * bh;
                    // Grain lines bend around the knot.
                    float dk = MathF.Sqrt((x - kx) * (x - kx) * 0.15f + (y - ky) * (y - ky));
                    float wave = MathF.Sin((y + Tile(x, y, 2f, b) * 6f + 40f / (dk + 4f)) * 1.3f);
                    float v = tone * (0.9f + 0.08f * wave + 0.05f * Tile(x, y, 6f, 10 + b));
                    if (dk < 4f) v *= 0.55f;                                    // knot
                    if (ly == 0) v *= 0.45f; else if (ly == 1) v *= 0.7f;       // seam
                    else if (ly == 2) v *= 1.1f;                                 // lit edge
                    if (Math.Abs(x - joint) < 1 && b % 2 == 0) v *= 0.5f;        // butt joint
                    c.Set(x, y, 0.52f * v, 0.36f * v, 0.22f * v);
                }
            }
        }
        _planks = c.Texture();
        return _planks;
    }

    /// <summary>The same boards in light grey, so a material colour can stain them.</summary>
    public static ImageTexture PlanksPainted()
    {
        if (_planksPainted != null)
            return _planksPainted;
        var src = Planks().GetImage();
        src.ClearMipmaps();
        var c = new Canvas();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float v = Math.Clamp(src.GetPixel(x, y).R * 1.85f, 0f, 1f);
                c.Set(x, y, v, v, v);
            }
        _planksPainted = c.Texture();
        return _planksPainted;
    }

    /// <summary>Lime plaster for the timber-framed walls: off-white, mottled, with a few hairline cracks.</summary>
    public static ImageTexture Plaster()
    {
        if (_plaster != null)
            return _plaster;
        var c = new Canvas();
        var rng = new RandomNumberGenerator { Seed = 3 };
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float v = 0.92f + 0.04f * Tile(x, y, 3f, 21) + 0.03f * Tile(x, y, 12f, 22) + rng.RandfRange(-0.012f, 0.012f);
                c.Set(x, y, v, v * 0.98f, v * 0.93f);
            }
        for (int k = 0; k < 4; k++)
        {
            float x = rng.RandfRange(0, Size), y = rng.RandfRange(0, Size), dir = rng.RandfRange(0, Mathf.Tau);
            for (int i = 0; i < 40; i++)
            {
                dir += rng.RandfRange(-0.5f, 0.5f);
                x += MathF.Cos(dir);
                y += MathF.Sin(dir);
                c.Set((int)x, (int)y, 0.7f, 0.68f, 0.64f);
            }
        }
        _plaster = c.Texture();
        return _plaster;
    }

    /// <summary>Thatch: layered rows of straw, each made of individual stalks with shadow under the row above.</summary>
    public static ImageTexture Shingles()
    {
        if (_shingles != null)
            return _shingles;
        var c = new Canvas();
        var rng = new RandomNumberGenerator { Seed = 11 };
        const int rows = 8, rh = Size / rows;
        for (int r = 0; r < rows; r++)
        {
            for (int x = 0; x < Size; x++)
            {
                float stalk = rng.RandfRange(0.78f, 1.12f);
                int len = rng.RandiRange(rh - 8, rh + 2);
                for (int ly = 0; ly < rh; ly++)
                {
                    int y = r * rh + ly;
                    float v = stalk * (0.7f + 0.35f * ly / rh) * (0.95f + 0.05f * Tile(x, y, 8f, 31));
                    if (ly >= len) v *= 0.5f;          // gap where the stalk ends
                    if (ly < 3) v *= 0.55f + ly * 0.12f; // shadow under the row above
                    c.Set(x, y, v, v * 0.88f, v * 0.62f);
                }
            }
        }
        _shingles = c.Texture();
        return _shingles;
    }

    /// <summary>Rough fieldstones with dark mortar: rows of irregular, rounded stones with lit tops.</summary>
    public static ImageTexture Stone()
    {
        if (_stone != null)
            return _stone;
        var c = new Canvas();
        var rng = new RandomNumberGenerator { Seed = 5 };
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                c.Set(x, y, 0.22f, 0.21f, 0.2f);                               // mortar
        const int rows = 6, rh = Size / rows;
        for (int r = 0; r < rows; r++)
        {
            int x = -rng.RandiRange(0, 30);
            while (x < Size)
            {
                int w = rng.RandiRange(28, 60);
                int h = rh - rng.RandiRange(3, 8);
                float tone = rng.RandfRange(0.62f, 1.0f);
                float cx = x + w * 0.5f, cy = r * rh + rh * 0.5f;
                for (int py = r * rh; py < (r + 1) * rh; py++)
                    for (int px = x; px < x + w; px++)
                    {
                        float dx = (px - cx) / (w * 0.5f - 2f), dy = (py - cy) / (h * 0.5f);
                        float d = dx * dx * dx * dx + dy * dy * dy * dy;      // rounded rectangle
                        if (d > 1f)
                            continue;
                        float light = 1f - 0.25f * dy - 0.15f * d;             // lit from above
                        float v = tone * light * (0.92f + 0.08f * Tile(px, py, 10f, 41 + r));
                        c.Set(px, py, 0.62f * v, 0.6f * v, 0.56f * v);
                    }
                x += w + rng.RandiRange(2, 4);
            }
        }
        _stone = c.Texture();
        return _stone;
    }
}
