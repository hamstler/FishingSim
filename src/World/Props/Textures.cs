using System;
using System.Threading.Tasks;
using Godot;

namespace FishingSim.World.Props;

/// <summary>
/// High-resolution textures for the medieval village (boards, plaster, thatch, fieldstone): each tile is
/// <see cref="Size"/>² pixels covering 2 m (2048 px per metre), block compressed, read with trilinear anisotropic filtering.
/// They are painted from byte arrays on worker threads with a tiling value noise of their own, and kept in a disk
/// cache (user://texcache) so later launches load them instead of painting them again.
/// </summary>
public static class Textures
{
    public const int Size = 4096;

    /// <summary>Scale of the original 256-pixel designs: every distance in pixels is multiplied by this.</summary>
    private const float S = Size / 256f;

    /// <summary>Texture repeats per metre for triplanar mapping (one tile = 2 m).</summary>
    public const float TriplanarScale = 0.5f;

    /// <summary>Bump when a texture design changes, so stale cached images are repainted.</summary>
    private const int CacheVersion = 4;

    private static ImageTexture? _planks, _shingles, _planksPainted, _stone, _plaster, _logs, _boards, _woodShingles, _tiles, _canvas;

    // Every design's normal map, found by its colour texture.
    private static readonly System.Collections.Generic.Dictionary<Texture2D, ImageTexture> Normals = new();

    /// <summary>
    /// The normal map belonging to a texture made here (its brightness read as height: dark seams and gaps lie deeper),
    /// or null for other textures.
    /// </summary>
    public static ImageTexture? NormalFor(Texture2D albedo)
    {
        lock (Normals)
            return Normals.TryGetValue(albedo, out var n) ? n : null;
    }

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

    /// <summary>
    /// A texture from the disk cache (block compressed, Graphics/TextureStore), or painted and stored there, with its
    /// normal map (painted once for both). Without a screen (tests) a plain stand-in: nothing is drawn.
    /// </summary>
    private static ImageTexture Cached(string name, Func<Canvas> paint, float bump = 2f)
    {
        if (Graphics.Renderer.IsHeadless)
            return ImageTexture.CreateFromImage(Godot.Image.CreateEmpty(4, 4, false, Godot.Image.Format.Rgb8));
        var (albedo, normal) = Pictures(name, paint, bump);
        var tex = ImageTexture.CreateFromImage(albedo);
        lock (Normals)
            Normals[tex] = ImageTexture.CreateFromImage(normal);
        return tex;
    }

    private static (Godot.Image Albedo, Godot.Image Normal) Pictures(string name, Func<Canvas> paint, float bump)
    {
        Canvas? painted = null;
        Canvas Paint() => painted ??= paint();
        var albedo = Graphics.TextureStore.Get($"{name}_{Size}", CacheVersion, Graphics.BlockCompress.Kind.Colour, () => Paint().Image()).Image;
        var normal = Graphics.TextureStore.Get($"{name}_n_{Size}", CacheVersion, Graphics.BlockCompress.Kind.TwoChannel,
            () => NormalFrom(Paint(), bump), normalMap: true).Image;
        return (albedo, normal);
    }

    /// <summary>A normal map from a design's brightness (Sobel), <paramref name="bump"/> as strong at any resolution.</summary>
    private static Godot.Image NormalFrom(Canvas c, float bump)
    {
        var lum = new float[Size * Size];
        Rows(y =>
        {
            for (int x = 0; x < Size; x++)
            {
                int i = (y * Size + x) * 3;
                lum[y * Size + x] = (c.Data[i] * 0.3f + c.Data[i + 1] * 0.59f + c.Data[i + 2] * 0.11f) / 255f;
            }
        });
        float L(int x, int y) => lum[((y % Size + Size) % Size) * Size + (x % Size + Size) % Size];
        var data = new byte[Size * Size * 4];
        float k = bump * Size / 1024f;
        Rows(y =>
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = (L(x + 1, y - 1) + 2f * L(x + 1, y) + L(x + 1, y + 1) - L(x - 1, y - 1) - 2f * L(x - 1, y) - L(x - 1, y + 1)) * 0.25f;
                float dy = (L(x - 1, y + 1) + 2f * L(x, y + 1) + L(x + 1, y + 1) - L(x - 1, y - 1) - 2f * L(x, y - 1) - L(x + 1, y - 1)) * 0.25f;
                var n = new Vector3(-dx * k, dy * k, 1f).Normalized();      // OpenGL style (green up), as Godot reads it
                int i = (y * Size + x) * 4;
                data[i] = (byte)((n.X * 0.5f + 0.5f) * 255f);
                data[i + 1] = (byte)((n.Y * 0.5f + 0.5f) * 255f);
                data[i + 2] = (byte)((n.Z * 0.5f + 0.5f) * 255f);
                data[i + 3] = 255;
            }
        });
        return Godot.Image.CreateFromData(Size, Size, false, Godot.Image.Format.Rgba8, data);
    }

    /// <summary>Paints every design into the disk cache (no textures are made: safe on a worker thread).</summary>
    public static void Prewarm()
    {
        // Designs a photographed material replaces (Graphics/PhotoTextures) are only painted when that set is missing.
        var replacedBy = new System.Collections.Generic.Dictionary<string, string>
        {
            ["planks"] = "weathered_brown_planks", ["planks_painted"] = "white_planks_clean", ["plaster"] = "medieval_wall_01", ["stone"] = "stone_wall",
            ["logs"] = "weathered_brown_planks", ["boards"] = "white_planks_clean", ["wood_shingles"] = "roof_slates_02", ["tiles"] = "clay_roof_tiles_02",
            ["canvas"] = "rough_linen",
        };
        foreach (var (name, paint, bump) in Designs)
            if (!replacedBy.TryGetValue(name, out var photo) || !Graphics.PhotoTextures.Has(photo))
                Pictures(name, paint, bump);
    }

    private static readonly (string Name, Func<Canvas> Paint, float Bump)[] Designs =
    {
        ("planks", PaintPlanks, 2f), ("planks_painted", PaintPlanksPainted, 2f), ("plaster", PaintPlaster, 1.5f), ("thatch", PaintThatch, 3f),
        ("stone", PaintStone, 4f), ("logs", PaintLogs, 3.5f), ("boards", PaintBoards, 2.5f), ("wood_shingles", PaintWoodShingles, 3f),
        ("tiles", PaintTiles, 3.5f), ("canvas", PaintCanvas, 1.2f),
    };

    private static float Bump(string name) => Array.Find(Designs, d => d.Name == name).Bump;

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

    /// <summary>Value noise with different cell counts across and along (streaks: wood grain, fibres).</summary>
    private static float Streaks(float x, float y, int cellsX, int cellsY, int seed)
    {
        float fx = x / Size * cellsX, fy = y / Size * cellsY;
        int ix = (int)MathF.Floor(fx), iy = (int)MathF.Floor(fy);
        float tx = fx - ix, ty = fy - iy;
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);
        int x0 = ((ix % cellsX) + cellsX) % cellsX, x1 = (x0 + 1) % cellsX;
        int y0 = ((iy % cellsY) + cellsY) % cellsY, y1 = (y0 + 1) % cellsY;
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
    public static ImageTexture Planks() => _planks ??= Cached("planks", PaintPlanks, Bump("planks"));

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
                float fine = Tile(x, y, 24f, 50 + b) * 0.03f + (Streaks(x, y, 30, 700, 60 + b) - 0.5f) * 0.08f;   // fibres
                float v = tone[b] * (0.9f + 0.05f * wave + 0.05f * Tile(x, y, 6f, 10 + b) + fine);
                if (dk < 4f) v *= 0.55f + 0.1f * (dk / 4f);                  // knot
                if (ly < S) v *= 0.45f; else if (ly < 2f * S) v *= 0.7f;    // seam
                else if (ly < 3f * S) v *= 1.1f;                              // lit edge
                if (Math.Abs(x - joint[b]) < S && b % 2 == 0) v *= 0.5f;     // butt joint
                c.Set(x, y, 0.68f * v, 0.5f * v, 0.33f * v);
            }
        });
        return c;
    }

    /// <summary>The same boards in light grey, so a material colour can stain them.</summary>
    public static ImageTexture PlanksPainted() => _planksPainted ??= Cached("planks_painted", PaintPlanksPainted, Bump("planks_painted"));

    private static Canvas PaintPlanksPainted()
    {
        var src = PaintPlanks();
        var c = new Canvas();
        Rows(y =>
        {
            for (int x = 0; x < Size; x++)
            {
                float v = Math.Clamp(src.Red(x, y) * 1.42f, 0f, 1f);
                c.Set(x, y, v, v, v);
            }
        });
        return c;
    }

    /// <summary>Lime plaster for the timber-framed walls: off-white, mottled, with a few hairline cracks.</summary>
    public static ImageTexture Plaster() => _plaster ??= Cached("plaster", PaintPlaster, Bump("plaster"));

    private static Canvas PaintPlaster()
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
    }

    /// <summary>Thatch: layered rows of straw, each made of individual stalks with shadow under the row above.</summary>
    public static ImageTexture Shingles() => _shingles ??= Cached("thatch", PaintThatch, Bump("thatch"));

    private static Canvas PaintThatch()
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
    }

    /// <summary>Rough fieldstones with dark mortar: rows of irregular, rounded stones with lit tops.</summary>
    public static ImageTexture Stone() => _stone ??= Cached("stone", PaintStone, Bump("stone"));

    private static Canvas PaintStone()
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
    }

    // ------------------------------------------------------------------ Nordic building materials
    /// <summary>Round logs laid on each other (cabins): six to a tile, shaded round, grain, checks and knots, mossy chinking.</summary>
    public static ImageTexture Logs() => _logs ??= Cached("logs", PaintLogs, Bump("logs"));

    private static Canvas PaintLogs()
    {
        var c = new Canvas();
        const int logs = 6;
        int lh = Size / logs;
        var rng = new RandomNumberGenerator { Seed = 71 };
        var tone = new float[logs];
        var crackV = new float[logs * 2];
        var knots = new (float X, float V, float R)[logs * 3];
        for (int l = 0; l < logs; l++)
        {
            tone[l] = rng.RandfRange(0.82f, 1.1f);
            crackV[l * 2] = rng.RandfRange(0.3f, 0.45f);
            crackV[l * 2 + 1] = rng.RandfRange(0.55f, 0.72f);
            for (int k = 0; k < 3; k++)
                knots[l * 3 + k] = (rng.RandfRange(0, Size), rng.RandfRange(0.25f, 0.75f), rng.RandfRange(3f, 6f) * S);
        }
        Rows(y =>
        {
            int l = Math.Min(y / lh, logs - 1);
            float v = (y - l * lh) / (float)lh;
            for (int x = 0; x < Size; x++)
            {
                const float chink = 0.07f;
                if (v < chink || v > 1f - chink * 0.5f)
                {
                    // Moss and clay pressed between the logs.
                    float m = 0.75f + 0.25f * Tile(x, y, 30f, 72) + 0.15f * (Hash(x / 3, y / 3, 73) - 0.5f);
                    c.Set(x, y, 0.27f * m, 0.3f * m, 0.18f * m);
                    continue;
                }
                float u = (v - chink) / (1f - chink * 1.5f);
                float round = MathF.Sqrt(MathF.Max(1f - (2f * u - 1f) * (2f * u - 1f), 0f));
                float grain = Streaks(x, y, 6, 240, 74 + l) * 0.6f + Streaks(x, y, 24, 900, 75) * 0.4f;
                float shade = (0.5f + 0.5f * round) * tone[l] * (0.86f + 0.2f * grain);
                // Checks: thin cracks running along the log, wavering.
                foreach (int k in new[] { l * 2, l * 2 + 1 })
                {
                    float wav = crackV[k] + 0.012f * MathF.Sin(x / (40f * S) * MathF.Tau) + 0.006f * Tile(x, y, 8f, 76 + k);
                    if (MathF.Abs(u - wav) < 0.008f && Periodic(x, 0, 6, 77 + k) > 0.35f)
                        shade *= 0.45f;
                }
                for (int k = l * 3; k < l * 3 + 3; k++)
                {
                    float dx = MathF.Abs(x - knots[k].X);
                    dx = MathF.Min(dx, Size - dx);
                    float d = MathF.Sqrt(dx * dx * 0.6f + (u - knots[k].V) * (u - knots[k].V) * lh * lh) / knots[k].R;
                    if (d < 1f)
                        shade *= 0.55f + 0.35f * MathF.Abs(MathF.Sin(d * 9f));
                }
                // Grey weathering on the sun side.
                float grey = Math.Clamp(0.4f + 0.5f * Tile(x, y, 4f, 78), 0f, 1f) * 0.6f;
                float r = 0.58f + (0.5f - 0.58f) * grey, g = 0.44f + (0.47f - 0.44f) * grey, b = 0.31f + (0.43f - 0.31f) * grey;
                c.Set(x, y, r * shade, g * shade, b * shade);
            }
        });
        return c;
    }

    /// <summary>Board-and-batten wall in a light worn paint (tinted per house: falu red, ochre, white), vertical grain, nails.</summary>
    public static ImageTexture Boards() => _boards ??= Cached("boards", PaintBoards, Bump("boards"));

    private static Canvas PaintBoards()
    {
        var c = new Canvas();
        const int boards = 8;
        int bw = Size / boards;
        int batten = (int)(bw * 0.2f);
        Rows(y =>
        {
            for (int x = 0; x < Size; x++)
            {
                int b = x / bw, lx = x - b * bw;
                // Battens over the seams (the seam lies at lx = 0).
                int dSeam = Math.Min(lx, bw - lx);
                bool onBatten = dSeam < batten / 2;
                float grain = Streaks(x, y, 160, 5, 81 + (onBatten ? 7 : b)) * 0.6f + Streaks(x, y, 600, 20, 82) * 0.4f;
                float v = 0.88f * (0.9f + 0.12f * grain) * (0.95f + 0.1f * Hash(b, 0, 83));
                if (onBatten)
                {
                    float e = dSeam / (batten * 0.5f);
                    v *= e > 0.82f ? 0.7f : e < 0.12f ? 1.06f : 1f;              // lit middle, shadowed sides
                    if ((y + (int)(Hash(b, 1, 84) * 300 * S)) % (int)(150 * S) < 2 * S && MathF.Abs(e - 0.5f) < 0.18f)
                        v *= 0.35f;                                                  // nail heads
                }
                else if (lx < S * 0.6f || bw - lx < S * 0.6f)
                    v *= 0.4f;
                // Paint worn through to grey wood in patches, more near the bottom of each tile row.
                float wear = Tile(x, y, 6f, 85) * 0.6f + Tile(x, y, 26f, 86) * 0.4f;
                float rr = 1f, gg = 1f, bb = 1f;
                if (wear > 0.42f)
                {
                    float t = Math.Clamp((wear - 0.42f) * 6f, 0f, 1f);
                    rr = 1f - 0.42f * t;
                    gg = 1f - 0.45f * t;
                    bb = 1f - 0.45f * t;
                }
                c.Set(x, y, v * rr, v * 0.985f * gg, v * 0.96f * bb);
            }
        });
        return c;
    }

    /// <summary>Split wooden shingles in overlapping rows, staggered joints, grain, shadows under each row, lichen.</summary>
    public static ImageTexture WoodShingles() => _woodShingles ??= Cached("wood_shingles", PaintWoodShingles, Bump("wood_shingles"));

    private static Canvas PaintWoodShingles()
    {
        var c = new Canvas();
        const int rows = 8;
        int rh = Size / rows;
        var rng = new RandomNumberGenerator { Seed = 91 };
        var edges = new System.Collections.Generic.List<int>[rows];
        for (int r = 0; r < rows; r++)
        {
            edges[r] = new System.Collections.Generic.List<int>();
            int x = rng.RandiRange(0, (int)(20 * S));
            while (x < Size)
            {
                edges[r].Add(x);
                x += rng.RandiRange((int)(13 * S), (int)(26 * S));
            }
        }
        Rows(y =>
        {
            int r = Math.Min(y / rh, rows - 1);
            float v = (y - r * rh) / (float)rh;     // 0 top (under the row above) .. 1 the butt
            var e = edges[r];
            for (int x = 0; x < Size; x++)
            {
                int i = e.BinarySearch(x);
                if (i < 0)
                    i = ~i - 1;
                int left = i >= 0 ? e[i] : e[^1] - Size, right = i + 1 < e.Count ? e[i + 1] : e[0] + Size;
                int id = i >= 0 ? i : e.Count - 1;
                float tone = 0.78f + 0.3f * Hash(id, r, 92);
                float grain = Streaks(x, y, 180, 10, 93 + r) * 0.7f + Streaks(x, y, 700, 30, 94) * 0.3f;
                float shade = tone * (0.85f + 0.2f * grain);
                shade *= 0.55f + 0.45f * MathF.Min(v / 0.18f, 1f);          // shadow of the row above
                if (v > 0.93f) shade *= 1.12f;                                // the lit butt edge
                if (x - left < S * 0.7f || right - x < S * 0.5f) shade *= 0.35f;   // gaps between shingles
                float lichen = Tile(x, y, 10f, 95);
                float lr = 0.52f, lg = 0.45f, lb = 0.38f;
                if (lichen > 0.55f)
                {
                    float t = Math.Clamp((lichen - 0.55f) * 6f, 0f, 0.7f);
                    lr += (0.6f - lr) * t;
                    lg += (0.64f - lg) * t;
                    lb += (0.5f - lb) * t;
                }
                c.Set(x, y, lr * shade, lg * shade, lb * shade);
            }
        });
        return c;
    }

    /// <summary>Clay roof tiles: rounded, rows overlapping with shadows, every tile its own shade of terracotta, moss.</summary>
    public static ImageTexture Tiles() => _tiles ??= Cached("tiles", PaintTiles, Bump("tiles"));

    private static Canvas PaintTiles()
    {
        var c = new Canvas();
        const int rows = 8, perRow = 8;
        int rh = Size / rows, tw = Size / perRow;
        Rows(y =>
        {
            int r = Math.Min(y / rh, rows - 1);
            float v = (y - r * rh) / (float)rh;
            int shift = r % 2 == 0 ? 0 : tw / 2;
            for (int x = 0; x < Size; x++)
            {
                int xs = (x + shift) % Size;
                int t = xs / tw;
                float u = (xs - t * tw) / (float)tw;
                float crest = MathF.Sin(MathF.PI * u);
                float tone = 0.82f + 0.3f * Hash(t, r, 101);
                float shade = tone * (0.55f + 0.5f * crest) * (0.93f + 0.07f * Tile(x, y, 30f, 102));
                shade *= 0.5f + 0.5f * MathF.Min(v / 0.16f, 1f);
                if (u < 0.03f || u > 0.97f) shade *= 0.5f;
                float red = 0.66f, green = 0.31f, blue = 0.19f;
                float burnt = Hash(t, r, 103);
                if (burnt > 0.8f) { red = 0.52f; green = 0.25f; blue = 0.17f; }
                else if (burnt < 0.15f) { red = 0.74f; green = 0.42f; blue = 0.24f; }
                float moss = Tile(x, y, 7f, 104) + (1f - v) * 0.15f;
                if (moss > 0.62f)
                {
                    float k = Math.Clamp((moss - 0.62f) * 5f, 0f, 0.6f);
                    red += (0.34f - red) * k;
                    green += (0.38f - green) * k;
                    blue += (0.2f - blue) * k;
                }
                c.Set(x, y, red * shade, green * shade, blue * shade);
            }
        });
        return c;
    }

    /// <summary>Woven canvas for tents, awnings and clothes: over-and-under threads, light and tintable, faint stains.</summary>
    public static ImageTexture Cloth() => _canvas ??= Cached("canvas", PaintCanvas, Bump("canvas"));

    private static Canvas PaintCanvas()
    {
        var c = new Canvas();
        int t = Math.Max(2, (int)(S * 0.5f));     // one thread
        Rows(y =>
        {
            for (int x = 0; x < Size; x++)
            {
                int cx = x / t, cy = y / t;
                bool warpOnTop = ((cx + cy) & 1) == 0;
                float along = warpOnTop ? (y % t) / (float)t : (x % t) / (float)t;
                float across = warpOnTop ? (x % t) / (float)t : (y % t) / (float)t;
                float thread = 0.8f + 0.2f * MathF.Sin(MathF.PI * across) - 0.06f * MathF.Abs(along - 0.5f);
                float v = 0.9f * thread * (0.95f + 0.05f * Hash(warpOnTop ? cx : cy, 0, 111));
                float stain = Tile(x, y, 3f, 112) * 0.6f + Tile(x, y, 12f, 113) * 0.4f;
                v *= 0.93f + 0.07f * stain;
                c.Set(x, y, v, v * 0.97f, v * 0.9f);
            }
        });
        return c;
    }
}
