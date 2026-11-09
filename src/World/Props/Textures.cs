using System;
using System.Threading.Tasks;
using Godot;

namespace FishingSim.World.Props;

/// <summary>
/// High-resolution textures for the medieval village (boards, plaster, thatch, fieldstone): each tile is
/// <see cref="Size"/>² pixels covering 2 m (512 px per metre, sharp at QHD), read with trilinear anisotropic filtering.
/// They are painted from byte arrays on worker threads with a tiling value noise of their own, and kept in a disk
/// cache (user://texcache) so later launches load them instead of painting them again.
/// </summary>
public static class Textures
{
    public const int Size = 1024;

    /// <summary>Scale of the original 256-pixel designs: every distance in pixels is multiplied by this.</summary>
    private const float S = Size / 256f;

    /// <summary>Texture repeats per metre for triplanar mapping (one tile = 2 m).</summary>
    public const float TriplanarScale = 0.5f;

    /// <summary>Bump when a texture design changes, so stale cached images are repainted.</summary>
    private const int CacheVersion = 2;

    private static ImageTexture? _planks, _shingles, _planksPainted, _stone, _plaster;

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

        public float Red(int x, int y) => Data[(((y % Size) + Size) % Size * Size + ((x % Size) + Size) % Size) * 3] / 255f;

        public Image Image()
        {
            var img = Godot.Image.CreateFromData(Size, Size, false, Godot.Image.Format.Rgb8, Data);
            return img;
        }
    }

    private static string CachePath(string name) => $"user://texcache/{name}_{Size}_v{CacheVersion}.png";

    /// <summary>Loads a cached texture, or paints it and stores it in the cache.</summary>
    private static ImageTexture Cached(string name, Func<Canvas> paint)
    {
        string path = CachePath(name);
        Image? img = null;
        if (FileAccess.FileExists(path))
        {
            img = Godot.Image.LoadFromFile(path);
            if (img != null && (img.GetWidth() != Size || img.GetHeight() != Size))
                img = null;
            img?.Convert(Godot.Image.Format.Rgb8);
        }
        if (img == null)
        {
            img = paint().Image();
            if (!OS.HasEnvironment("FISHSIM_NO_CACHE"))
            {
                DirAccess.MakeDirRecursiveAbsolute("user://texcache");
                img.SavePng(path);
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    // ------------------------------------------------------------------ tiling value noise (thread-safe)
    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }
    }

    /// <summary>Value noise with <paramref name="cells"/> cells across the tile, wrapping at the edges.</summary>
    private static float Periodic(float x, float y, int cells, int seed)
    {
        float fx = x / Size * cells, fy = y / Size * cells;
        int ix = (int)MathF.Floor(fx), iy = (int)MathF.Floor(fy);
        float tx = fx - ix, ty = fy - iy;
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);
        int x0 = ((ix % cells) + cells) % cells, x1 = (x0 + 1) % cells;
        int y0 = ((iy % cells) + cells) % cells, y1 = (y0 + 1) % cells;
        float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
        return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
    }

    /// <summary>Seamless fractal noise in [-1, 1] on the tile; <paramref name="scale"/> ~ features across the tile / 4.</summary>
    private static float Tile(float x, float y, float scale, int seed)
    {
        int c = Math.Max(2, (int)(scale * 4f));
        float n = Periodic(x, y, c, seed) * 0.55f + Periodic(x, y, c * 2, seed + 1) * 0.3f + Periodic(x, y, c * 4, seed + 2) * 0.15f;
        return n * 2f - 1f;
    }

    private static void Rows(Action<int> row) => Parallel.For(0, Size, row);

    // ------------------------------------------------------------------ textures
    /// <summary>Horizontal boards with wavy grain, knots and dark seams.</summary>
    public static ImageTexture Planks() => _planks ??= Cached("planks", PaintPlanks);

    private static Canvas PaintPlanks()
    {
        var c = new Canvas();
        var rng = new RandomNumberGenerator { Seed = 7 };
        const int boards = 7;
        int bh = Size / boards + 1;
        var tone = new float[boards];
        var kx = new float[boards];
        var joint = new int[boards];
        for (int b = 0; b < boards; b++)
        {
            tone[b] = rng.RandfRange(0.82f, 1.08f);
            kx[b] = rng.RandfRange(0f, Size);
            joint[b] = rng.RandiRange((int)(20 * S), (int)(Size - 20 * S));
        }
        Rows(y =>
        {
            int b = Math.Min(y / bh, boards - 1);
            int ly = y - b * bh;
            float ky = b * bh + bh * 0.5f;
            for (int x = 0; x < Size; x++)
            {
                // Grain lines bend around the knot.
                float dk = MathF.Sqrt((x - kx[b]) * (x - kx[b]) * 0.15f + (y - ky) * (y - ky)) / S;
                float wave = MathF.Sin((y / S + Tile(x, y, 2f, b) * 6f + 40f / (dk + 4f)) * 1.3f);
                float fine = Tile(x, y, 24f, 50 + b) * 0.03f;                // fibres between the grain lines
                float v = tone[b] * (0.9f + 0.08f * wave + 0.05f * Tile(x, y, 6f, 10 + b) + fine);
                if (dk < 4f) v *= 0.55f + 0.1f * (dk / 4f);                  // knot
                if (ly < S) v *= 0.45f; else if (ly < 2f * S) v *= 0.7f;    // seam
                else if (ly < 3f * S) v *= 1.1f;                              // lit edge
                if (Math.Abs(x - joint[b]) < S && b % 2 == 0) v *= 0.5f;     // butt joint
                c.Set(x, y, 0.52f * v, 0.36f * v, 0.22f * v);
            }
        });
        return c;
    }

    /// <summary>The same boards in light grey, so a material colour can stain them.</summary>
    public static ImageTexture PlanksPainted() => _planksPainted ??= Cached("planks_painted", () =>
    {
        var src = PaintPlanks();
        var c = new Canvas();
        Rows(y =>
        {
            for (int x = 0; x < Size; x++)
            {
                float v = Math.Clamp(src.Red(x, y) * 1.85f, 0f, 1f);
                c.Set(x, y, v, v, v);
            }
        });
        return c;
    });

    /// <summary>Lime plaster for the timber-framed walls: off-white, mottled, with a few hairline cracks.</summary>
    public static ImageTexture Plaster() => _plaster ??= Cached("plaster", () =>
    {
        var c = new Canvas();
        Rows(y =>
        {
            for (int x = 0; x < Size; x++)
            {
                float v = 0.92f + 0.04f * Tile(x, y, 3f, 21) + 0.03f * Tile(x, y, 12f, 22) + (Hash(x, y, 23) - 0.5f) * 0.024f;
                c.Set(x, y, v, v * 0.98f, v * 0.93f);
            }
        });
        var rng = new RandomNumberGenerator { Seed = 3 };
        for (int k = 0; k < 4; k++)
        {
            float x = rng.RandfRange(0, Size), y = rng.RandfRange(0, Size), dir = rng.RandfRange(0, Mathf.Tau);
            for (int i = 0; i < 40 * S; i++)
            {
                dir += rng.RandfRange(-0.5f, 0.5f) / MathF.Sqrt(S);
                x += MathF.Cos(dir);
                y += MathF.Sin(dir);
                c.Set((int)x, (int)y, 0.7f, 0.68f, 0.64f);
                c.Set((int)x + 1, (int)y, 0.8f, 0.78f, 0.74f);
            }
        }
        return c;
    });

    /// <summary>Thatch: layered rows of straw, each made of individual stalks with shadow under the row above.</summary>
    public static ImageTexture Shingles() => _shingles ??= Cached("thatch", () =>
    {
        var c = new Canvas();
        const int rows = 8;
        int rh = Size / rows;
        Rows(y =>
        {
            int r = Math.Min(y / rh, rows - 1), ly = y - r * rh;
            for (int x = 0; x < Size; x++)
            {
                // Each stalk is about S pixels wide, with its own tone and length.
                int stalkId = x / Math.Max((int)(S * 0.75f), 1);
                float stalk = 0.78f + 0.34f * Hash(stalkId, r, 31);
                int len = rh - (int)((2f + 8f * Hash(stalkId, r, 32)) * S);
                float edge = 1f - 0.25f * MathF.Abs(MathF.Sin(MathF.PI * (x % Math.Max((int)(S * 0.75f), 1)) / MathF.Max(S * 0.75f, 1f) + 0.5f));
                float v = stalk * edge * (0.7f + 0.35f * ly / rh) * (0.95f + 0.05f * Tile(x, y, 8f, 33));
                if (ly >= len) v *= 0.5f;                            // gap where the stalk ends
                if (ly < 3f * S) v *= 0.55f + ly / (3f * S) * 0.36f;  // shadow under the row above
                c.Set(x, y, v, v * 0.88f, v * 0.62f);
            }
        });
        return c;
    });

    /// <summary>Rough fieldstones with dark mortar: rows of irregular, rounded stones with lit tops.</summary>
    public static ImageTexture Stone() => _stone ??= Cached("stone", () =>
    {
        var c = new Canvas();
        Rows(y =>
        {
            for (int x = 0; x < Size; x++)
            {
                float m = 0.21f + 0.03f * Tile(x, y, 20f, 40);                // gritty mortar
                c.Set(x, y, m, m * 0.97f, m * 0.92f);
            }
        });
        var rng = new RandomNumberGenerator { Seed = 5 };
        const int rows = 6;
        int rh = Size / rows;
        for (int r = 0; r < rows; r++)
        {
            int x = -rng.RandiRange(0, (int)(30 * S));
            while (x < Size)
            {
                int w = rng.RandiRange((int)(28 * S), (int)(60 * S));
                int h = rh - rng.RandiRange((int)(3 * S), (int)(8 * S));
                float tone = rng.RandfRange(0.62f, 1.0f);
                float cx = x + w * 0.5f, cy = r * rh + rh * 0.5f;
                int x0 = x, row = r;
                Parallel.For(r * rh, (r + 1) * rh, py =>
                {
                    for (int px = x0; px < x0 + w; px++)
                    {
                        float dx = (px - cx) / (w * 0.5f - 2f * S), dy = (py - cy) / (h * 0.5f);
                        float d = dx * dx * dx * dx + dy * dy * dy * dy;      // rounded rectangle
                        if (d > 1f)
                            continue;
                        float light = 1f - 0.25f * dy - 0.15f * d;             // lit from above
                        float grain = Tile(px, py, 10f, 41 + row) * 0.08f + Tile(px, py, 40f, 60 + row) * 0.04f;
                        float v = tone * light * (0.92f + grain);
                        c.Set(px, py, 0.62f * v, 0.6f * v, 0.56f * v);
                    }
                });
                x += w + rng.RandiRange((int)(2 * S), (int)(4 * S));
            }
        }
        return c;
    });
}
