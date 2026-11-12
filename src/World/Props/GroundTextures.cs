using System;
using System.Threading.Tasks;
using Godot;

namespace FishingSim.World.Props;

/// <summary>
/// The painted materials of the ground for the terrain splat (shaders/terrain.gdshader): meadow grass, forest floor,
/// sand, a dirt path, the pebbly lake bottom, rock and snow. Each layer is a seamless tile of <see cref="Size"/>²
/// pixels covering <see cref="TileMetres"/> (512 px per metre), painted stroke by stroke (blades, clover, fallen
/// leaves, needles, twigs, moss, grains, pebbles, cracks, mineral grains, lichen) with a height for every pixel. The
/// height goes into the albedo's alpha (the shader blends the layers by it: grass grows over the sand, pebbles poke
/// out of the silt) and into the normal map. Painted on worker threads and kept in the disk cache (user://texcache)
/// like <see cref="Textures"/>; bump <see cref="CacheVersion"/> when a design changes.
/// </summary>
public static class GroundTextures
{
    public const int Size = 1024;
    public const float TileMetres = 2f;
    private const int CacheVersion = 3;

    public const int Grass = 0, Forest = 1, Sand = 2, Path = 3, Bed = 4, Rock = 5, Snow = 6, Count = 7;
    private static readonly string[] Names = { "grass", "forest", "sand", "path", "bed", "rock", "snow" };

    private static readonly object Gate = new();
    private static Texture2DArray? _albedo, _normal;
    private static readonly Color[] Means = new Color[Count];

    /// <summary>Albedo (sRGB) with the height in alpha, one array layer per ground.</summary>
    public static Texture2DArray Albedo
    {
        get
        {
            Load();
            return _albedo!;
        }
    }

    /// <summary>Normal map (RG = the surface's tilt along x and z, B = cavity, A = roughness), one layer per ground.</summary>
    public static Texture2DArray Normal
    {
        get
        {
            Load();
            return _normal!;
        }
    }

    /// <summary>A layer's average colour (linear), so a map's own colours can tint it.</summary>
    public static Color Mean(int layer)
    {
        Load();
        return Means[layer];
    }

    private static string CachePath(int layer, string part) => $"user://texcache/ground_{Names[layer]}_{part}_{Size}_v{CacheVersion}.png";

    private static void Load()
    {
        lock (Gate)
        {
            if (_albedo != null)
                return;
            var albedo = new Image[Count];
            var normal = new Image[Count];
            Parallel.For(0, Count, i => (albedo[i], normal[i]) = LoadOrPaint(i));
            for (int i = 0; i < Count; i++)
                Means[i] = MeanOf(albedo[i]);
            Parallel.For(0, Count, i =>
            {
                albedo[i].GenerateMipmaps();
                normal[i].GenerateMipmaps();
                // Block compressed the albedo takes a quarter of the memory (the alpha height keeps its own block).
                if (Fishing.FishAsset.CompressTextures)
                    albedo[i].Compress(Image.CompressMode.S3Tc, Image.CompressSource.Srgb);
            });
            _albedo = new Texture2DArray();
            _albedo.CreateFromImages(new Godot.Collections.Array<Image>(albedo));
            _normal = new Texture2DArray();
            _normal.CreateFromImages(new Godot.Collections.Array<Image>(normal));
        }
    }

    private static (Image Albedo, Image Normal) LoadOrPaint(int layer)
    {
        string pa = CachePath(layer, "a"), pn = CachePath(layer, "n");
        if (FileAccess.FileExists(pa) && FileAccess.FileExists(pn))
        {
            var a = Image.LoadFromFile(pa);
            var n = Image.LoadFromFile(pn);
            if (a != null && n != null && a.GetWidth() == Size && n.GetWidth() == Size)
            {
                a.Convert(Image.Format.Rgba8);
                n.Convert(Image.Format.Rgba8);
                return (a, n);
            }
        }
        var sheet = layer switch
        {
            Grass => PaintGrass(),
            Forest => PaintForest(),
            Sand => PaintSand(),
            Path => PaintPath(),
            Bed => PaintBed(),
            Rock => PaintRock(),
            _ => PaintSnow(),
        };
        var (albedo, normal) = sheet.Finish();
        if (!OS.HasEnvironment("FISHSIM_NO_CACHE"))
        {
            DirAccess.MakeDirRecursiveAbsolute("user://texcache");
            albedo.SavePng(pa);
            normal.SavePng(pn);
        }
        return (albedo, normal);
    }

    private static Color MeanOf(Image img)
    {
        byte[] d = img.GetData();
        double r = 0, g = 0, b = 0;
        int n = 0;
        for (int i = 0; i + 3 < d.Length && n < Size * Size; i += 4, n++)
        {
            r += Lin(d[i] / 255f);
            g += Lin(d[i + 1] / 255f);
            b += Lin(d[i + 2] / 255f);
        }
        return new Color((float)(r / n), (float)(g / n), (float)(b / n));
    }

    public static float Lin(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    // ------------------------------------------------------------------ the canvas
    /// <summary>Colour, height (0..1) and roughness per pixel, wrapping at the edges.</summary>
    private sealed class Sheet
    {
        public const int N = Size * Size;
        public readonly float[] R = new float[N], G = new float[N], B = new float[N], H = new float[N], Q = new float[N];

        /// <summary>How strongly the height tilts the normal map, and how much the cavities darken the colour.</summary>
        public float Bump = 2.5f, Cavity = 0.5f, Depth = 0f;

        public static int At(int x, int y) => (y & (Size - 1)) * Size + (x & (Size - 1));

        /// <summary>Paints where the new surface lies at least as high as the old one; <paramref name="cover"/> anti-aliases edges.</summary>
        public void Plot(int x, int y, Color c, float h, float cover = 1f, float rough = -1f)
        {
            if (cover <= 0f)
                return;
            int i = At(x, y);
            if (h < H[i])
                return;
            cover = MathF.Min(cover, 1f);
            R[i] += (c.R - R[i]) * cover;
            G[i] += (c.G - G[i]) * cover;
            B[i] += (c.B - B[i]) * cover;
            H[i] += (h - H[i]) * cover;
            if (rough >= 0f)
                Q[i] += (rough - Q[i]) * cover;
        }

        /// <summary>Tints a pixel without changing its height.</summary>
        public void Tint(int x, int y, Color c, float amount)
        {
            int i = At(x, y);
            R[i] += (c.R - R[i]) * amount;
            G[i] += (c.G - G[i]) * amount;
            B[i] += (c.B - B[i]) * amount;
        }

        public void Fill(Func<int, int, (Color C, float H, float Rough)> paint) => Parallel.For(0, Size, y =>
        {
            for (int x = 0; x < Size; x++)
            {
                var (c, h, q) = paint(x, y);
                int i = y * Size + x;
                R[i] = c.R;
                G[i] = c.G;
                B[i] = c.B;
                H[i] = h;
                Q[i] = q;
            }
        });

        /// <summary>The albedo (cavities darkened, height in alpha) and the normal map with cavity and roughness.</summary>
        public (Image Albedo, Image Normal) Finish()
        {
            var blur = BoxBlur(BoxBlur(H, 6), 6);
            var a = new byte[N * 4];
            var n = new byte[N * 4];
            Parallel.For(0, Size, y =>
            {
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    float cav = Math.Clamp(0.5f + (H[i] - blur[i]) * 3f, 0f, 1f);
                    float shade = (1f + Cavity * (cav - 0.5f) * 1.2f) * (1f - Depth * (1f - H[i]));
                    a[i * 4] = Byte(R[i] * shade);
                    a[i * 4 + 1] = Byte(G[i] * shade);
                    a[i * 4 + 2] = Byte(B[i] * shade);
                    a[i * 4 + 3] = Byte(H[i]);
                    float dx = H[At(x + 1, y)] - H[At(x - 1, y)];
                    float dy = H[At(x, y + 1)] - H[At(x, y - 1)];
                    var v = new Vector3(-dx * Bump, -dy * Bump, 1f).Normalized();
                    n[i * 4] = Byte(v.X * 0.5f + 0.5f);
                    n[i * 4 + 1] = Byte(v.Y * 0.5f + 0.5f);
                    n[i * 4 + 2] = Byte(cav);
                    n[i * 4 + 3] = Byte(Q[i]);
                }
            });
            return (Image.CreateFromData(Size, Size, false, Image.Format.Rgba8, a), Image.CreateFromData(Size, Size, false, Image.Format.Rgba8, n));
        }

        private static byte Byte(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);

        private static float[] BoxBlur(float[] src, int r)
        {
            var tmp = new float[N];
            var dst = new float[N];
            float k = 1f / (2 * r + 1);
            Parallel.For(0, Size, y =>
            {
                float sum = 0f;
                for (int dx = -r; dx <= r; dx++)
                    sum += src[At(dx, y)];
                for (int x = 0; x < Size; x++)
                {
                    tmp[y * Size + x] = sum * k;
                    sum += src[At(x + r + 1, y)] - src[At(x - r, y)];
                }
            });
            Parallel.For(0, Size, x =>
            {
                float sum = 0f;
                for (int dy = -r; dy <= r; dy++)
                    sum += tmp[At(x, dy)];
                for (int y = 0; y < Size; y++)
                {
                    dst[y * Size + x] = sum * k;
                    sum += tmp[At(x, y + r + 1)] - tmp[At(x, y - r)];
                }
            });
            return dst;
        }
    }

    // ------------------------------------------------------------------ seamless noise (thread-safe)
    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    /// <summary>Value noise in [0, 1] with <paramref name="cells"/> cells across the tile, wrapping at its edges.</summary>
    private static float Value(float x, float y, int cells, int seed)
    {
        float fx = x / Size * cells, fy = y / Size * cells;
        int ix = (int)MathF.Floor(fx), iy = (int)MathF.Floor(fy);
        float tx = fx - ix, ty = fy - iy;
        tx = tx * tx * tx * (tx * (tx * 6f - 15f) + 10f);
        ty = ty * ty * ty * (ty * (ty * 6f - 15f) + 10f);
        int x0 = ((ix % cells) + cells) % cells, x1 = (x0 + 1) % cells;
        int y0 = ((iy % cells) + cells) % cells, y1 = (y0 + 1) % cells;
        float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
        return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
    }

    /// <summary>Fractal value noise in [0, 1], seamless on the tile.</summary>
    private static float Fbm(float x, float y, int cells, int octaves, int seed)
    {
        float sum = 0f, amp = 1f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += Value(x, y, cells << o, seed + o * 31) * amp;
            norm += amp;
            amp *= 0.5f;
        }
        return sum / norm;
    }

    /// <summary>Seamless cellular noise: distances (in cells) to the nearest and second-nearest feature point.</summary>
    private static (float F1, float F2, int Id) Worley(float x, float y, int cells, int seed)
    {
        float fx = x / Size * cells, fy = y / Size * cells;
        int ix = (int)MathF.Floor(fx), iy = (int)MathF.Floor(fy);
        float f1 = 9f, f2 = 9f;
        int id = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = ix + dx, cy = iy + dy;
                int wx = ((cx % cells) + cells) % cells, wy = ((cy % cells) + cells) % cells;
                float px = cx + Hash(wx, wy, seed), py = cy + Hash(wx, wy, seed + 7);
                float d = MathF.Sqrt((px - fx) * (px - fx) + (py - fy) * (py - fy));
                if (d < f1)
                {
                    f2 = f1;
                    f1 = d;
                    id = wy * cells + wx;
                }
                else if (d < f2)
                    f2 = d;
            }
        return (f1, f2, id);
    }

    /// <summary>Seamless cellular noise with different cell counts across and along (stretched cells: bark plates).</summary>
    private static (float F1, float F2, int Id) Worley(float x, float y, int cellsX, int cellsY, int seed, float stretch = 1f)
    {
        float fx = x / Size * cellsX, fy = y / Size * cellsY;
        int ix = (int)MathF.Floor(fx), iy = (int)MathF.Floor(fy);
        float f1 = 9f, f2 = 9f;
        int id = 0;
        float ky = cellsX / (float)cellsY;      // distances measured in the across-cells' scale
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = ix + dx, cy = iy + dy;
                int wx = ((cx % cellsX) + cellsX) % cellsX, wy = ((cy % cellsY) + cellsY) % cellsY;
                float px = cx + Hash(wx, wy, seed), py = cy + Hash(wx, wy, seed + 7);
                float ddx = px - fx, ddy = (py - fy) * ky * stretch;
                float d = MathF.Sqrt(ddx * ddx + ddy * ddy);
                if (d < f1)
                {
                    f2 = f1;
                    f1 = d;
                    id = wy * cellsX + wx;
                }
                else if (d < f2)
                    f2 = d;
            }
        return (f1, f2, id);
    }

    // ------------------------------------------------------------------ bark
    private static readonly System.Collections.Generic.Dictionary<bool, (Texture2D, Texture2D, Color)> BarkCache = new();

    /// <summary>
    /// Bark wrapped round a trunk (u around, v up; one tile spans the trunk's girth and as much height): furrowed plates
    /// with lichen for most trees, white papery birch bark with dark lenticels and black rough patches. Returns the
    /// albedo (height in alpha), the normal map (RG tilt, B cavity, A roughness) and the albedo's linear average.
    /// </summary>
    public static (Texture2D Albedo, Texture2D Normal, Color Mean) Bark(bool birch)
    {
        lock (Gate)
        {
            if (BarkCache.TryGetValue(birch, out var cached))
                return cached;
            string name = birch ? "bark_birch" : "bark";
            string pa = $"user://texcache/{name}_a_{Size}_v{CacheVersion}.png", pn = $"user://texcache/{name}_n_{Size}_v{CacheVersion}.png";
            Image? a = null, n = null;
            if (FileAccess.FileExists(pa) && FileAccess.FileExists(pn))
            {
                a = Image.LoadFromFile(pa);
                n = Image.LoadFromFile(pn);
            }
            if (a == null || n == null || a.GetWidth() != Size)
            {
                (a, n) = (birch ? PaintBirchBark() : PaintBark()).Finish();
                if (!OS.HasEnvironment("FISHSIM_NO_CACHE"))
                {
                    DirAccess.MakeDirRecursiveAbsolute("user://texcache");
                    a.SavePng(pa);
                    n.SavePng(pn);
                }
            }
            a.Convert(Image.Format.Rgba8);
            n.Convert(Image.Format.Rgba8);
            var mean = MeanOf(a);
            a.GenerateMipmaps();
            n.GenerateMipmaps();
            var result = ((Texture2D)ImageTexture.CreateFromImage(a), (Texture2D)ImageTexture.CreateFromImage(n), mean);
            BarkCache[birch] = result;
            return result;
        }
    }

    private static Sheet PaintBark()
    {
        var s = new Sheet { Bump = 5f, Cavity = 0.75f };
        s.Fill((x, y) =>
        {
            // Plates: cells stretched along the trunk, split by deep furrows; small cross cracks on each plate.
            float wx = x + (Fbm(x, y, 8, 3, 91) - 0.5f) * 50f;
            var (f1, f2, id) = Worley(wx, y, 13, 4, 92);
            float furrow = Math.Clamp(1f - (f2 - f1) / 0.22f, 0f, 1f);
            float cross = Math.Clamp(1f - MathF.Abs(((y / (float)Size * 28f + Hash(id, 2, 93) * 7f) % 1f) - 0.5f) / 0.06f, 0f, 1f) * Hash(id, 3, 93);
            float n = Fbm(x, y, 16, 3, 94), grain = Value(x, y, 160, 95);
            float tone = 0.85f + 0.3f * Hash(id, 1, 93);
            var c = new Color(0.47f, 0.42f, 0.36f) * tone * (0.85f + 0.2f * n + 0.08f * grain);
            c = c.Lerp(new Color(0.13f, 0.11f, 0.09f), furrow * furrow * 0.9f);
            c = c.Lerp(new Color(0.2f, 0.17f, 0.14f), cross * 0.6f);
            // Lichen crusts on some plates.
            float lichen = Fbm(x, y, 6, 4, 96);
            if (lichen > 0.62f && furrow < 0.5f)
                c = c.Lerp(new Color(0.58f, 0.64f, 0.5f) * (0.85f + 0.2f * Hash(x, y, 97)), Math.Clamp((lichen - 0.62f) * 8f, 0f, 0.75f));
            float h = 0.75f - 0.65f * furrow * furrow + 0.1f * n + 0.04f * grain - 0.12f * cross;
            return (c, Math.Clamp(h, 0f, 1f), 0.9f);
        });
        return s;
    }

    private static Sheet PaintBirchBark()
    {
        var s = new Sheet { Bump = 3f, Cavity = 0.5f };
        s.Fill((x, y) =>
        {
            float n = Fbm(x, y, 6, 4, 101), m = Fbm(x, y, 24, 3, 102);
            var c = new Color(0.9f, 0.89f, 0.85f) * (0.92f + 0.08f * n);
            // Papery layers peeling a warmer cream colour.
            float peel = Fbm(x, y, 5, 3, 103);
            if (peel > 0.63f)
                c = c.Lerp(new Color(0.88f, 0.78f, 0.66f), Math.Clamp((peel - 0.63f) * 9f, 0f, 0.7f));
            float h = 0.6f + 0.08f * n + 0.04f * m;
            // Lenticels: thin dark dashes running round the trunk (rows of 16 px, slots of 64 px: seamless).
            int row = y / 16, slot = ((x + (int)(Hash(row, 0, 104) * 64f)) & (Size - 1)) / 64;
            float rowY = row * 16 + 4f + Hash(row, slot, 105) * 8f;
            float dashLen = 10f + 34f * Hash(row, slot, 106);
            float slotX = ((x + (int)(Hash(row, 0, 104) * 64f)) & (Size - 1)) - slot * 64f - 32f;
            float dash = Hash(row, slot, 110) < 0.55f
                ? Math.Clamp(1.4f - MathF.Abs(y - rowY), 0f, 1f) * Math.Clamp(dashLen * 0.5f - MathF.Abs(slotX), 0f, 1f)
                : 0f;
            if (dash > 0f)
            {
                c = c.Lerp(new Color(0.22f, 0.2f, 0.18f), dash * 0.85f);
                h -= dash * 0.2f;
            }
            // Black rough patches (diamonds where branches were), grey-black and cracked.
            float wx = x + (Fbm(x, y, 8, 3, 106) - 0.5f) * 60f;
            var (b1, b2, bid) = Worley(wx, y, 6, 6, 107, 2.6f);     // wide, low patches
            float patch = Hash(bid, 2, 108) < 0.35f ? Math.Clamp((0.42f - b1) * 6f, 0f, 1f) : 0f;
            if (patch > 0f)
            {
                float crack = Math.Clamp(Fbm(x, y, 40, 2, 109) * 2f - 0.6f, 0f, 1f);
                c = c.Lerp(new Color(0.12f, 0.11f, 0.1f) * (0.8f + 0.5f * crack), patch);
                h += patch * (0.15f + 0.15f * crack);
            }
            return (c, Math.Clamp(h, 0f, 1f), 0.75f - 0.2f * patch);
        });
        return s;
    }

    // ------------------------------------------------------------------ strokes
    private static Color C(float r, float g, float b) => new(r, g, b);

    private static float Rf(Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    private static Color Vary(Color c, Random rng, float amount)
    {
        float k = 1f + Rf(rng, -amount, amount);
        return new Color(c.R * k * (1f + Rf(rng, -0.04f, 0.04f)), c.G * k, c.B * k * (1f + Rf(rng, -0.04f, 0.04f)));
    }

    private static Color Pick(Random rng, Color[] palette) => palette[rng.Next(palette.Length)];

    /// <summary>
    /// A grass blade lying in the picture: tapering from <paramref name="width"/> at the root to a point, curving by
    /// <paramref name="curve"/> radians, rising from <paramref name="h0"/> to <paramref name="h1"/>, a lighter midrib.
    /// </summary>
    private static void Blade(Sheet s, float x, float y, float angle, float length, float width, Color root, Color tip, float h0, float h1, float curve, float rough)
    {
        int steps = Math.Max(2, (int)(length * 2f));
        for (int k = 0; k <= steps; k++)
        {
            float t = k / (float)steps;
            float a = angle + curve * t * t;
            if (k > 0)
            {
                x += MathF.Cos(a) * length / steps;
                y += MathF.Sin(a) * length / steps;
            }
            float w = width * (1f - MathF.Pow(t, 1.6f) * 0.92f);
            float nx = -MathF.Sin(a), ny = MathF.Cos(a);
            var c = root.Lerp(tip, t);
            float h = h0 + (h1 - h0) * t;
            float half = w * 0.5f + 0.75f;
            for (float o = -half; o <= half; o += 0.5f)
            {
                float cover = Math.Clamp(w * 0.5f + 0.5f - MathF.Abs(o), 0f, 1f);
                float rib = MathF.Abs(o) < w * 0.18f ? 1.08f : 1f - 0.12f * MathF.Abs(o) / MathF.Max(w * 0.5f, 0.5f);
                s.Plot((int)MathF.Round(x + nx * o), (int)MathF.Round(y + ny * o), c * rib, h + 0.04f * (1f - MathF.Abs(o) / half), cover, rough);
            }
        }
    }

    /// <summary>A thin stroke along a straight line (a needle, a twig): <paramref name="width"/> pixels wide.</summary>
    private static void Stroke(Sheet s, float x0, float y0, float x1, float y1, float width, Color c0, Color c1, float h0, float h1, float rough)
    {
        float len = MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        int steps = Math.Max(2, (int)(len * 2f));
        float nx = -(y1 - y0) / MathF.Max(len, 0.001f), ny = (x1 - x0) / MathF.Max(len, 0.001f);
        float half = width * 0.5f + 0.75f;
        for (int k = 0; k <= steps; k++)
        {
            float t = k / (float)steps;
            float x = x0 + (x1 - x0) * t, y = y0 + (y1 - y0) * t;
            var c = c0.Lerp(c1, t);
            for (float o = -half; o <= half; o += 0.5f)
            {
                float cover = Math.Clamp(width * 0.5f + 0.5f - MathF.Abs(o), 0f, 1f);
                float round = 1f - (o / half) * (o / half);
                s.Plot((int)MathF.Round(x + nx * o), (int)MathF.Round(y + ny * o), c * (0.8f + 0.25f * round - 0.1f * MathF.Max(o, 0f) / half),
                    h0 + (h1 - h0) * t + 0.08f * round, cover, rough);
            }
        }
    }

    /// <summary>A rounded stone: an ellipse domed to <paramref name="dome"/>, darker at its rim, a fine speckle.</summary>
    private static void Pebble(Sheet s, float cx, float cy, float rx, float ry, float angle, Color c, float h0, float dome, float rough, int seed)
    {
        int r = (int)MathF.Ceiling(MathF.Max(rx, ry)) + 1;
        float ca = MathF.Cos(angle), sa = MathF.Sin(angle);
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                float px = dx + 0.5f - (cx - MathF.Floor(cx)), py = dy + 0.5f - (cy - MathF.Floor(cy));
                float u = (px * ca + py * sa) / rx, v = (-px * sa + py * ca) / ry;
                float d2 = u * u + v * v;
                if (d2 >= 1.15f)
                    continue;
                float d = MathF.Sqrt(d2);
                float cover = Math.Clamp((1f - d) * MathF.Min(rx, ry) + 0.5f, 0f, 1f);
                float z = MathF.Sqrt(MathF.Max(1f - MathF.Min(d2, 1f), 0f));
                int x = (int)MathF.Floor(cx) + dx, y = (int)MathF.Floor(cy) + dy;
                float speck = 0.92f + 0.16f * Hash(x, y, seed);
                s.Plot(x, y, c * ((0.72f + 0.28f * z) * speck), h0 + dome * MathF.Pow(z, 0.7f), cover, rough);
            }
    }

    /// <summary>
    /// A fallen leaf: pointed tip, rounded base, a midrib with side veins, edges curled up by <paramref name="curl"/>,
    /// a short stalk.
    /// </summary>
    private static void Leaf(Sheet s, float cx, float cy, float angle, float length, float width, Color c, float h, float curl, Random rng)
    {
        float ca = MathF.Cos(angle), sa = MathF.Sin(angle);
        float hl = length * 0.5f, hw = width * 0.5f;
        int r = (int)MathF.Ceiling(hl) + 2;
        float spot = rng.NextDouble() < 0.25 ? Rf(rng, -0.5f, 0.5f) : 9f;
        float spotV = Rf(rng, -0.4f, 0.4f);
        var edge = c.Lerp(C(0.3f, 0.2f, 0.1f), 0.35f);
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                float u = (dx * ca + dy * sa) / hl, v = (-dx * sa + dy * ca) / hw;
                if (u < -1.05f || u > 1.05f)
                    continue;
                float prof = MathF.Pow(MathF.Max(1f - u * u, 0f), 0.55f) * (u > 0f ? 1f - 0.25f * u : 1f);
                float dist = (prof - MathF.Abs(v)) * hw;
                if (dist < -0.6f)
                    continue;
                float cover = Math.Clamp(dist + 0.5f, 0f, 1f);
                float av = MathF.Abs(v) / MathF.Max(prof, 0.05f);
                var col = c.Lerp(edge, av * av * 0.6f);
                if (MathF.Abs(v) * hw < 0.7f)
                    col *= 0.8f;                                            // midrib
                else if (MathF.Abs(((u * 3.2f + av * 1.3f) % 1f + 1f) % 1f - 0.5f) < 0.06f)
                    col *= 0.88f;                                           // side veins
                if ((u - spot) * (u - spot) + (v - spotV) * (v - spotV) < 0.03f)
                    col *= 0.6f;
                col *= 0.92f + 0.1f * u;
                s.Plot((int)cx + dx, (int)cy + dy, col, h + curl * av * av * 0.18f + 0.03f * (1f - u * u), cover, 0.8f);
            }
        // The stalk.
        Stroke(s, cx - ca * hl, cy - sa * hl, cx - ca * hl * 1.3f, cy - sa * hl * 1.3f, 1.4f, edge, edge, h, h, 0.8f);
    }

    // ------------------------------------------------------------------ the layers
    private static Sheet PaintGrass()
    {
        var s = new Sheet { Bump = 3.2f, Cavity = 0.55f, Depth = 0.45f };
        var rng = new Random(101);
        s.Fill((x, y) =>
        {
            float n = Fbm(x, y, 8, 4, 11), m = Fbm(x, y, 48, 2, 12);
            var soil = C(0.17f, 0.14f, 0.08f).Lerp(C(0.34f, 0.31f, 0.17f), n * 0.6f + m * 0.4f);
            return (soil, 0.04f + 0.08f * m, 0.95f);
        });
        Color[] greens =
        {
            C(0.27f, 0.42f, 0.13f), C(0.31f, 0.47f, 0.15f), C(0.23f, 0.37f, 0.11f), C(0.36f, 0.5f, 0.17f), C(0.29f, 0.45f, 0.18f),
        };
        Color[] dry = { C(0.5f, 0.47f, 0.24f), C(0.44f, 0.42f, 0.2f), C(0.56f, 0.5f, 0.3f) };
        // A dense turf of short blades close to the ground.
        for (int i = 0; i < 52000; i++)
        {
            var c = Vary(Pick(rng, greens), rng, 0.12f) * 0.8f;
            Blade(s, Rf(rng, 0, Size), Rf(rng, 0, Size), Rf(rng, 0, MathF.Tau), Rf(rng, 7f, 16f), Rf(rng, 1.6f, 2.6f), c * 0.75f, c, Rf(rng, 0.08f, 0.3f),
                Rf(rng, 0.3f, 0.45f), Rf(rng, -0.5f, 0.5f), 0.85f);
        }
        // Longer blades lying over it, now and then a dry one; patches where the meadow is lusher.
        for (int i = 0; i < 24000; i++)
        {
            float x = Rf(rng, 0, Size), y = Rf(rng, 0, Size);
            bool isDry = rng.NextDouble() < 0.1 + 0.25 * Fbm(x, y, 6, 2, 13);
            var c = Vary(isDry ? Pick(rng, dry) : Pick(rng, greens), rng, 0.1f);
            float h0 = Rf(rng, 0.25f, 0.6f);
            Blade(s, x, y, Rf(rng, 0, MathF.Tau), Rf(rng, 18f, 46f), Rf(rng, 2.4f, 4.2f), c * 0.7f, c * 1.12f, h0, h0 + Rf(rng, 0.2f, 0.4f),
                Rf(rng, -0.6f, 0.6f), isDry ? 0.9f : 0.75f);
        }
        // Clover in patches: three heart-shaped leaflets with a pale chevron.
        for (int p = 0; p < 22; p++)
        {
            float px = Rf(rng, 0, Size), py = Rf(rng, 0, Size), pr = Rf(rng, 30f, 70f);
            int n = rng.Next(14, 34);
            for (int k = 0; k < n; k++)
            {
                float a = Rf(rng, 0, MathF.Tau), d = pr * MathF.Sqrt((float)rng.NextDouble());
                float cx = px + MathF.Cos(a) * d, cy = py + MathF.Sin(a) * d;
                float leaf = Rf(rng, 5f, 8.5f), turn = Rf(rng, 0, MathF.Tau), h = Rf(rng, 0.62f, 0.82f);
                var col = Vary(C(0.21f, 0.4f, 0.14f), rng, 0.1f);
                for (int l = 0; l < 3; l++)
                {
                    float la = turn + l * MathF.Tau / 3f;
                    float lx = cx + MathF.Cos(la) * leaf * 0.75f, ly = cy + MathF.Sin(la) * leaf * 0.75f;
                    int r = (int)leaf + 1;
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            float u = (dx * MathF.Cos(la) + dy * MathF.Sin(la)) / leaf, v = (-dx * MathF.Sin(la) + dy * MathF.Cos(la)) / leaf;
                            float dd = u * u + v * v * 1.3f;
                            if (dd > 1f || (u > 0.7f && MathF.Abs(v) < 0.12f))      // the notch at the tip
                                continue;
                            bool chevron = MathF.Abs(MathF.Abs(v) * 1.4f - (u + 0.05f)) < 0.09f && u > -0.1f && u < 0.45f;
                            float cover = Math.Clamp((1f - dd) * leaf * 0.5f + 0.5f, 0f, 1f);
                            s.Plot((int)lx + dx, (int)ly + dy, chevron ? col * 1.45f : col * (0.85f + 0.2f * u), h + 0.05f * (1f - dd), cover, 0.7f);
                        }
                }
            }
        }
        // A few small flowers: daisies and buttercups.
        for (int i = 0; i < 40; i++)
        {
            float x = Rf(rng, 0, Size), y = Rf(rng, 0, Size);
            if (rng.NextDouble() < 0.6)
            {
                for (int k = 0; k < 11; k++)
                {
                    float a = k * MathF.Tau / 11f + Rf(rng, -0.1f, 0.1f);
                    Blade(s, x, y, a, 7.5f, 2.6f, C(0.93f, 0.92f, 0.86f), C(0.98f, 0.96f, 0.95f), 0.92f, 0.95f, 0f, 0.6f);
                }
                Pebble(s, x, y, 2.4f, 2.4f, 0f, C(0.95f, 0.78f, 0.18f), 0.96f, 0.03f, 0.6f, 5);
            }
            else
            {
                for (int k = 0; k < 5; k++)
                    Pebble(s, x + MathF.Cos(k * 1.2566f) * 2.6f, y + MathF.Sin(k * 1.2566f) * 2.6f, 2.6f, 2.2f, k * 1.2566f, C(0.98f, 0.82f, 0.12f), 0.92f, 0.05f, 0.4f, 6);
            }
        }
        return s;
    }

    private static Sheet PaintForest()
    {
        var s = new Sheet { Bump = 3.6f, Cavity = 0.6f, Depth = 0.35f };
        var rng = new Random(202);
        s.Fill((x, y) =>
        {
            float n = Fbm(x, y, 6, 4, 21), m = Fbm(x, y, 64, 2, 22);
            var humus = C(0.13f, 0.095f, 0.065f).Lerp(C(0.25f, 0.18f, 0.11f), n * 0.7f + m * 0.3f);
            return (humus, 0.05f + 0.06f * m, 0.9f);
        });
        // Needles first, under everything.
        Color[] needle = { C(0.5f, 0.31f, 0.15f), C(0.42f, 0.26f, 0.13f), C(0.58f, 0.38f, 0.2f), C(0.33f, 0.22f, 0.12f) };
        for (int i = 0; i < 14000; i++)
        {
            float x = Rf(rng, 0, Size), y = Rf(rng, 0, Size), a = Rf(rng, 0, MathF.Tau), l = Rf(rng, 10f, 24f);
            var c = Vary(Pick(rng, needle), rng, 0.1f);
            float h = Rf(rng, 0.1f, 0.45f);
            Stroke(s, x, y, x + MathF.Cos(a) * l, y + MathF.Sin(a) * l, Rf(rng, 1.1f, 1.7f), c, c * 0.85f, h, h + 0.04f, 0.8f);
        }
        // Moss cushions.
        var moss = new float[Size * Size];
        Parallel.For(0, Size, y =>
        {
            for (int x = 0; x < Size; x++)
                moss[y * Size + x] = Fbm(x, y, 5, 5, 23) + (Value(x, y, 128, 27) - 0.5f) * 0.05f;
        });
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float m = moss[y * Size + x];
                if (m < 0.6f)
                    continue;
                float t = Math.Clamp((m - 0.6f) / 0.06f, 0f, 1f);
                // Tiny moss stars: bright tips, dark hollows between them.
                var (w1, _, wid) = Worley(x, y, 220, 24);
                float star = Math.Clamp(1f - w1 * 1.6f, 0f, 1f);
                float speck = Hash(wid, 0, 28);
                var c = C(0.16f, 0.25f, 0.07f).Lerp(C(0.4f, 0.5f, 0.16f), star * (0.6f + 0.4f * speck));
                s.Plot(x, y, c, 0.28f + 0.14f * t + 0.12f * star, t, 0.95f);
            }
        // Fallen leaves of last autumn.
        Color[] leaves =
        {
            C(0.42f, 0.27f, 0.13f), C(0.58f, 0.4f, 0.16f), C(0.27f, 0.18f, 0.1f), C(0.5f, 0.24f, 0.11f), C(0.4f, 0.34f, 0.24f), C(0.62f, 0.48f, 0.22f),
        };
        for (int i = 0; i < 2300; i++)
        {
            float len = Rf(rng, 20f, 46f);
            Leaf(s, Rf(rng, 0, Size), Rf(rng, 0, Size), Rf(rng, 0, MathF.Tau), len, len * Rf(rng, 0.45f, 0.65f), Vary(Pick(rng, leaves), rng, 0.12f),
                Rf(rng, 0.2f, 0.8f), Rf(rng, 0f, 1f), rng);
        }
        // Twigs with a side shoot.
        for (int i = 0; i < 70; i++)
        {
            float x = Rf(rng, 0, Size), y = Rf(rng, 0, Size), a = Rf(rng, 0, MathF.Tau);
            var c = Vary(C(0.3f, 0.23f, 0.15f), rng, 0.15f);
            float h = Rf(rng, 0.7f, 0.9f), w = Rf(rng, 2.5f, 4.5f);
            int segs = rng.Next(2, 5);
            for (int k = 0; k < segs; k++)
            {
                float l = Rf(rng, 20f, 45f);
                float nx = x + MathF.Cos(a) * l, ny = y + MathF.Sin(a) * l;
                Stroke(s, x, y, nx, ny, w, c, c * 0.95f, h, h, 0.85f);
                if (rng.NextDouble() < 0.5)
                {
                    float sa = a + Rf(rng, 0.5f, 1f) * (rng.NextDouble() < 0.5 ? -1 : 1), sl = Rf(rng, 10f, 22f);
                    Stroke(s, nx, ny, nx + MathF.Cos(sa) * sl, ny + MathF.Sin(sa) * sl, w * 0.55f, c, c, h, h - 0.05f, 0.85f);
                }
                x = nx;
                y = ny;
                a += Rf(rng, -0.4f, 0.4f);
                w *= 0.85f;
            }
        }
        // A few spruce cones.
        for (int i = 0; i < 9; i++)
        {
            float x = Rf(rng, 0, Size), y = Rf(rng, 0, Size), a = Rf(rng, 0, MathF.Tau);
            Pebble(s, x, y, 22f, 9f, a, C(0.4f, 0.26f, 0.14f), 0.75f, 0.2f, 0.9f, 25);
            for (int k = -4; k <= 4; k++)
                for (int side = -1; side <= 1; side += 2)
                {
                    float px = x + MathF.Cos(a) * k * 4.6f, py = y + MathF.Sin(a) * k * 4.6f;
                    float sx = px - MathF.Sin(a) * side * 3.5f, sy = py + MathF.Cos(a) * side * 3.5f;
                    Pebble(s, sx, sy, 3.2f, 2.4f, a, C(0.48f, 0.32f, 0.17f), 0.85f, 0.08f, 0.9f, 26);
                }
        }
        return s;
    }

    private static Sheet PaintSand()
    {
        var s = new Sheet { Bump = 1.6f, Cavity = 0.35f };
        var rng = new Random(303);
        s.Fill((x, y) =>
        {
            float n = Fbm(x, y, 6, 4, 31), m = Fbm(x, y, 24, 3, 32);
            // Wind ripples: a wave whose crests run across the tile (integer wave numbers keep it seamless), warped.
            float warp = Fbm(x, y, 4, 3, 33) * 6f;
            float ripple = MathF.Sin((x * 9f + y * 4f) / Size * MathF.Tau + warp);
            ripple = ripple * 0.5f + 0.5f;
            float g = Hash(x, y, 34);
            var c = C(0.75f, 0.67f, 0.49f) * (0.9f + 0.12f * n + 0.06f * m - 0.05f * (1f - ripple));
            if (g < 0.025f)
                c = C(0.34f, 0.3f, 0.25f);
            else if (g > 0.965f)
                c = C(0.94f, 0.91f, 0.82f);
            else
                c *= 0.94f + 0.12f * Hash(x, y, 35);
            return (c, 0.25f + 0.15f * ripple * (0.6f + 0.4f * m) + 0.06f * g, 0.92f);
        });
        Color[] stones = { C(0.55f, 0.52f, 0.47f), C(0.45f, 0.38f, 0.3f), C(0.66f, 0.6f, 0.5f), C(0.3f, 0.29f, 0.27f), C(0.62f, 0.48f, 0.34f) };
        for (int i = 0; i < 260; i++)
        {
            float r = Rf(rng, 1.8f, 6.5f);
            Pebble(s, Rf(rng, 0, Size), Rf(rng, 0, Size), r, r * Rf(rng, 0.6f, 1f), Rf(rng, 0, MathF.Tau), Vary(Pick(rng, stones), rng, 0.1f), 0.35f, 0.3f, 0.6f, 36);
        }
        // Bits of shell and driftwood.
        for (int i = 0; i < 14; i++)
        {
            float r = Rf(rng, 4f, 7f);
            Pebble(s, Rf(rng, 0, Size), Rf(rng, 0, Size), r, r * 0.7f, Rf(rng, 0, MathF.Tau), C(0.9f, 0.85f, 0.8f), 0.4f, 0.12f, 0.5f, 37);
        }
        for (int i = 0; i < 10; i++)
        {
            float x = Rf(rng, 0, Size), y = Rf(rng, 0, Size), a = Rf(rng, 0, MathF.Tau), l = Rf(rng, 15f, 40f);
            Stroke(s, x, y, x + MathF.Cos(a) * l, y + MathF.Sin(a) * l, Rf(rng, 2f, 3.5f), C(0.42f, 0.36f, 0.28f), C(0.5f, 0.44f, 0.34f), 0.45f, 0.45f, 0.85f);
        }
        return s;
    }

    private static Sheet PaintPath()
    {
        var s = new Sheet { Bump = 2.6f, Cavity = 0.55f };
        var rng = new Random(404);
        s.Fill((x, y) =>
        {
            float n = Fbm(x, y, 5, 4, 41), m = Fbm(x, y, 32, 3, 42);
            // Dried-mud cracks: warped cells, only where the ground dried out (patches), thin and broken.
            float wx = x + (Fbm(x, y, 6, 3, 47) - 0.5f) * 110f, wy = y + (Fbm(x, y, 6, 3, 48) - 0.5f) * 110f;
            var (f1, f2, _) = Worley(wx, wy, 8, 43);
            float dry = Math.Clamp((Fbm(x, y, 4, 3, 49) - 0.5f) * 6f, 0f, 1f) * Math.Clamp((Fbm(x, y, 40, 2, 50) - 0.3f) * 4f, 0f, 1f);
            float crack = Math.Clamp(1f - (f2 - f1) / 0.022f, 0f, 1f) * dry;
            var c = C(0.44f, 0.35f, 0.24f) * (0.82f + 0.25f * n + 0.1f * m);
            c = c.Lerp(C(0.24f, 0.18f, 0.12f), crack * 0.8f);
            c *= 0.95f + 0.1f * Hash(x, y, 44);
            return (c, 0.3f + 0.12f * n + 0.06f * m - 0.22f * crack, 0.9f);
        });
        Color[] gravel = { C(0.45f, 0.42f, 0.37f), C(0.38f, 0.32f, 0.25f), C(0.5f, 0.45f, 0.37f), C(0.3f, 0.28f, 0.25f), C(0.48f, 0.39f, 0.28f) };
        for (int i = 0; i < 1300; i++)
        {
            float r = Rf(rng, 1.6f, 6.5f);
            Pebble(s, Rf(rng, 0, Size), Rf(rng, 0, Size), r, r * Rf(rng, 0.55f, 1f), Rf(rng, 0, MathF.Tau), Vary(Pick(rng, gravel), rng, 0.12f), 0.35f, 0.3f, 0.7f, 45);
        }
        for (int i = 0; i < 22; i++)
        {
            float r = Rf(rng, 9f, 18f);
            Pebble(s, Rf(rng, 0, Size), Rf(rng, 0, Size), r, r * Rf(rng, 0.6f, 0.9f), Rf(rng, 0, MathF.Tau), Vary(Pick(rng, gravel), rng, 0.1f), 0.32f, 0.22f, 0.75f, 46);
        }
        // Dry stalks trodden into the dirt.
        for (int i = 0; i < 160; i++)
        {
            var c = Vary(C(0.55f, 0.5f, 0.3f), rng, 0.15f);
            Blade(s, Rf(rng, 0, Size), Rf(rng, 0, Size), Rf(rng, 0, MathF.Tau), Rf(rng, 12f, 30f), Rf(rng, 1.5f, 2.5f), c * 0.8f, c, 0.4f, 0.45f, Rf(rng, -0.3f, 0.3f), 0.9f);
        }
        return s;
    }

    private static Sheet PaintBed()
    {
        var s = new Sheet { Bump = 3.4f, Cavity = 0.6f };
        var rng = new Random(505);
        s.Fill((x, y) =>
        {
            float n = Fbm(x, y, 6, 4, 51), m = Fbm(x, y, 40, 2, 52);
            var silt = C(0.35f, 0.31f, 0.23f) * (0.85f + 0.2f * n + 0.08f * m);
            silt = silt.Lerp(C(0.28f, 0.32f, 0.18f), Math.Clamp((Fbm(x, y, 4, 3, 53) - 0.5f) * 3f, 0f, 0.5f));    // algae film
            return (silt, 0.08f + 0.06f * n, 0.55f);
        });
        Color[] stones =
        {
            C(0.46f, 0.45f, 0.42f), C(0.42f, 0.36f, 0.27f), C(0.27f, 0.26f, 0.24f), C(0.56f, 0.47f, 0.33f), C(0.38f, 0.39f, 0.36f), C(0.5f, 0.42f, 0.36f),
        };
        for (int i = 0; i < 2600; i++)
        {
            float r = 3.5f + 18f * MathF.Pow((float)rng.NextDouble(), 2.4f);
            float x = Rf(rng, 0, Size), y = Rf(rng, 0, Size);
            var c = Vary(Pick(rng, stones), rng, 0.12f);
            c = c.Lerp(C(0.3f, 0.36f, 0.2f), Math.Clamp(Fbm(x, y, 4, 2, 54) - 0.45f, 0f, 0.4f));
            Pebble(s, x, y, r, r * Rf(rng, 0.6f, 0.95f), Rf(rng, 0, MathF.Tau), c, Rf(rng, 0.1f, 0.3f), 0.35f + r / 60f, 0.4f, 55);
        }
        return s;
    }

    private static Sheet PaintRock()
    {
        var s = new Sheet { Bump = 4.5f, Cavity = 0.7f };
        s.Fill((x, y) =>
        {
            float big = Fbm(x, y, 3, 5, 61), mid = Fbm(x, y, 16, 3, 62);
            // Warped cells: the rock splits into irregular slabs, each a slightly tilted plane.
            float wx = x + (Fbm(x, y, 4, 3, 73) - 0.5f) * 160f, wy = y + (Fbm(x, y, 4, 3, 74) - 0.5f) * 160f;
            var (f1, f2, slab) = Worley(wx, wy, 4, 63);
            float plane = Hash(slab, 1, 75) - 0.5f;      // each slab sits a little higher or lower (seamless: no absolute x)
            float crackW = 0.012f + 0.02f * Fbm(x, y, 8, 2, 76);
            float crack = Math.Clamp(1f - (f2 - f1) / crackW, 0f, 1f);
            float fx2 = x + (Fbm(x, y, 10, 2, 77) - 0.5f) * 60f, fy2 = y + (Fbm(x, y, 10, 2, 78) - 0.5f) * 60f;
            var (g1, g2, _) = Worley(fx2, fy2, 11, 64);
            float fine = Math.Clamp(1f - (g2 - g1) / 0.018f, 0f, 1f) * Math.Clamp((Fbm(x, y, 7, 3, 79) - 0.55f) * 5f, 0f, 1f);
            // Mineral grains: dark mica, white quartz, pinkish feldspar.
            var (_, _, grain) = Worley(x, y, 150, 65);
            float gk = Hash(grain, 3, 66);
            float slabTone = 0.9f + 0.2f * Hash(slab, 3, 75);
            var c = C(0.47f, 0.46f, 0.44f) * (0.74f + 0.3f * big + 0.12f * mid) * slabTone;
            if (gk < 0.12f)
                c *= 0.6f;
            else if (gk > 0.9f)
                c = c.Lerp(C(0.78f, 0.77f, 0.74f), 0.5f);
            else if (gk > 0.78f)
                c = c.Lerp(C(0.6f, 0.5f, 0.46f), 0.35f);
            // Lichen: crusts with ragged edges, a few orange rosettes; moss in the cracks.
            float lichen = Fbm(x, y, 7, 5, 67) + (Hash(x, y, 68) - 0.5f) * 0.06f;
            if (lichen > 0.62f)
                c = c.Lerp(C(0.62f, 0.65f, 0.52f) * (0.85f + 0.2f * Hash(x / 3, y / 3, 68)), Math.Clamp((lichen - 0.62f) * 10f, 0f, 0.8f));
            var (o1, _, oid) = Worley(x, y, 26, 69);
            if (Hash(oid, 1, 70) < 0.06f && o1 < 0.18f)
                c = c.Lerp(C(0.78f, 0.55f, 0.22f), Math.Clamp((0.18f - o1) * 14f, 0f, 0.85f) * (0.6f + 0.4f * Hash(x, y, 71)));
            float near = Math.Clamp(1f - (f2 - f1) / 0.07f, 0f, 1f);
            if (near > 0f && Fbm(x, y, 12, 2, 72) > 0.55f)
                c = c.Lerp(C(0.24f, 0.33f, 0.12f), near * 0.65f);
            c = c.Lerp(C(0.12f, 0.12f, 0.11f), MathF.Max(crack, fine * 0.7f) * 0.85f);
            float h = 0.45f + 0.22f * big + 0.1f * mid + 0.1f * plane + 0.03f * gk - 0.4f * crack - 0.15f * fine;
            return (c, Math.Clamp(h, 0f, 1f), 0.75f - 0.1f * (gk > 0.9f ? 1f : 0f));
        });
        return s;
    }

    private static Sheet PaintSnow()
    {
        var s = new Sheet { Bump = 1.4f, Cavity = 0.3f };
        s.Fill((x, y) =>
        {
            float n = Fbm(x, y, 4, 5, 81), m = Fbm(x, y, 20, 3, 82);
            float sparkle = Hash(x, y, 83);
            var c = C(0.86f, 0.89f, 0.95f) * (0.93f + 0.07f * n + 0.03f * m);
            if (sparkle > 0.995f)
                c = C(1f, 1f, 1f);
            else if (sparkle < 0.0015f)
                c = C(0.35f, 0.33f, 0.3f);
            return (c, 0.3f + 0.4f * n + 0.1f * m, sparkle > 0.995f ? 0.15f : 0.55f);
        });
        return s;
    }
}
