using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using FishingSim.Data;

namespace FishingSim.Graphics;

/// <summary>Markings painted over a species' scales.</summary>
public enum FishPattern
{
    Plain, Bars, LightSpots, RedSpots, DarkSpots, Stripe, Stripes, Scutes, Marbled, Rainbow, Vermiculated, Mirror, Band,
    Saddles, SpotRows,
}

/// <summary>How a species' skin is painted: markings, scale size (fraction of body length, 0 = smooth skin),
/// silvery sheen (0..1) and how many bars or saddles.</summary>
public readonly record struct SkinStyle(FishPattern Pattern, float Scale, float Silver, int Count = 0);

/// <summary>
/// Painted fish skins (U = nose to tail, V = back to belly; both flanks share it). Every pixel is computed: smooth
/// countershading, a silvery or golden sheen along the flank, overlapping scales with dark free edges and soft
/// highlights (a staggered grid in body-proportional units so scales stay round), the lateral line with its pores,
/// the gill cover, the species' markings (bars, haloed red spots, pale bean spots, marbling, vermiculation, mirror
/// scales, bony scutes...) and fine grain. A matching normal map makes the scale edges catch the light.
/// The light skin (512×256) dresses fish swimming in the lake; the detailed skin (2048×1024, or 4096×2048 on
/// high quality) is for the fish on the line, in the hand and in the atlas. Painting runs on worker threads.
/// </summary>
public static class FishSkin
{
    public const int LightWidth = 512, LightHeight = 256;

    /// <summary>Size of the detailed skin for the current quality preset.</summary>
    public static Vector2I DetailedSize => GraphicsQuality.Level >= QualityLevel.High ? new Vector2I(4096, 2048) : new Vector2I(2048, 1024);

    private static readonly Dictionary<string, ImageTexture> Light = new();
    private static readonly Dictionary<string, (ImageTexture Albedo, ImageTexture Normal)> Detailed = new();
    private static readonly Dictionary<string, Task<(Image Albedo, Image Normal)>> Pending = new();
    private static readonly LinkedList<string> DetailedOrder = new();

    /// <summary>Detailed skins kept in memory (each 4096×2048 skin with mipmaps is ~45 MB).</summary>
    public const int DetailedCacheSize = 6;

    private static readonly Dictionary<string, SkinStyle> Styles = new()
    {
        ["Płoć"] = new(FishPattern.Plain, 0.024f, 0.9f), ["Leszcz"] = new(FishPattern.Plain, 0.022f, 0.55f),
        ["Karp"] = new(FishPattern.Plain, 0.03f, 0.25f), ["Szczupak"] = new(FishPattern.LightSpots, 0.007f, 0.15f),
        ["Okoń"] = new(FishPattern.Bars, 0.012f, 0.25f, 6), ["Sandacz"] = new(FishPattern.Bars, 0.011f, 0.45f, 9),
        ["Sum"] = new(FishPattern.Marbled, 0f, 0.1f), ["Lin"] = new(FishPattern.Plain, 0.008f, 0.08f),
        ["Karaś złocisty"] = new(FishPattern.Plain, 0.028f, 0.25f), ["Karaś srebrzysty"] = new(FishPattern.Plain, 0.028f, 0.85f),
        ["Krąp"] = new(FishPattern.Plain, 0.024f, 0.85f), ["Wzdręga"] = new(FishPattern.Plain, 0.024f, 0.6f),
        ["Jaź"] = new(FishPattern.Plain, 0.02f, 0.7f), ["Kleń"] = new(FishPattern.Plain, 0.03f, 0.5f),
        ["Boleń"] = new(FishPattern.Plain, 0.012f, 0.95f), ["Brzana"] = new(FishPattern.Plain, 0.013f, 0.35f),
        ["Świnka"] = new(FishPattern.Plain, 0.016f, 0.85f), ["Ukleja"] = new(FishPattern.Stripe, 0.016f, 1f),
        ["Kiełb"] = new(FishPattern.SpotRows, 0.02f, 0.5f), ["Jelec"] = new(FishPattern.Plain, 0.016f, 0.95f),
        ["Certa"] = new(FishPattern.Plain, 0.016f, 0.8f), ["Sapa"] = new(FishPattern.Plain, 0.016f, 0.9f),
        ["Rozpiór"] = new(FishPattern.Plain, 0.016f, 0.9f), ["Węgorz"] = new(FishPattern.Plain, 0f, 0.25f),
        ["Miętus"] = new(FishPattern.Marbled, 0f, 0.05f), ["Pstrąg potokowy"] = new(FishPattern.RedSpots, 0.006f, 0.25f),
        ["Palia"] = new(FishPattern.LightSpots, 0.005f, 0.2f), ["Lipień"] = new(FishPattern.DarkSpots, 0.014f, 0.65f),
        ["Głowacica"] = new(FishPattern.DarkSpots, 0.006f, 0.55f), ["Troć jeziorowa"] = new(FishPattern.DarkSpots, 0.007f, 0.9f),
        ["Sielawa"] = new(FishPattern.Plain, 0.014f, 1f), ["Sieja"] = new(FishPattern.Plain, 0.016f, 1f),
        ["Jazgarz"] = new(FishPattern.DarkSpots, 0.012f, 0.2f), ["Łosoś"] = new(FishPattern.DarkSpots, 0.007f, 1f),
        ["Ciosa"] = new(FishPattern.Plain, 0.012f, 1f), ["Strzebla potokowa"] = new(FishPattern.Bars, 0f, 0.3f, 11),
        ["Różanka"] = new(FishPattern.Stripe, 0.02f, 0.75f), ["Piskorz"] = new(FishPattern.Stripes, 0f, 0.1f),
        ["Głowacz białopłetwy"] = new(FishPattern.Marbled, 0f, 0.05f), ["Jesiotr ostronosy"] = new(FishPattern.Scutes, 0f, 0.15f),
        ["Pstrąg tęczowy"] = new(FishPattern.Rainbow, 0.006f, 0.65f), ["Pstrąg źródlany"] = new(FishPattern.Vermiculated, 0.004f, 0.15f),
        ["Troć wędrowna"] = new(FishPattern.DarkSpots, 0.007f, 1f), ["Amur biały"] = new(FishPattern.Plain, 0.026f, 0.4f),
        ["Tołpyga biała"] = new(FishPattern.Plain, 0.008f, 1f), ["Tołpyga pstra"] = new(FishPattern.Marbled, 0.008f, 0.45f),
        ["Sazan"] = new(FishPattern.Plain, 0.026f, 0.3f), ["Karp lustrzeń"] = new(FishPattern.Mirror, 0f, 0.3f),
        ["Sumik karłowaty"] = new(FishPattern.Marbled, 0f, 0.05f), ["Sumik kanałowy"] = new(FishPattern.DarkSpots, 0f, 0.6f),
        ["Bass wielkogębowy"] = new(FishPattern.Band, 0.012f, 0.3f), ["Sandacz wołżański"] = new(FishPattern.Bars, 0.01f, 0.4f, 8),
        ["Czop"] = new(FishPattern.Saddles, 0.01f, 0.25f, 4), ["Brzanka"] = new(FishPattern.DarkSpots, 0.012f, 0.3f),
        ["Kiełb białopłetwy"] = new(FishPattern.SpotRows, 0.02f, 0.6f), ["Śliz"] = new(FishPattern.Marbled, 0f, 0.05f),
        ["Koza"] = new(FishPattern.SpotRows, 0f, 0.2f), ["Babka bycza"] = new(FishPattern.Marbled, 0.014f, 0.1f),
        ["Ciernik"] = new(FishPattern.Plain, 0f, 0.8f), ["Cierniczek"] = new(FishPattern.Bars, 0f, 0.4f, 7),
        ["Słonecznica"] = new(FishPattern.Stripe, 0.02f, 1f), ["Strzebla błotna"] = new(FishPattern.DarkSpots, 0.012f, 0.3f),
        ["Głowacz pręgopłetwy"] = new(FishPattern.Marbled, 0f, 0.05f), ["Stynka"] = new(FishPattern.Stripe, 0.014f, 1f),
        ["Sterlet"] = new(FishPattern.Scutes, 0f, 0.15f), ["Orfa"] = new(FishPattern.Plain, 0.02f, 0.4f),
    };

    public static SkinStyle StyleOf(FishSpecies sp) =>
        Styles.TryGetValue(sp.SpeciesName, out var s) ? s : new SkinStyle(FishPattern.Plain, 0.018f, 0.5f);

    public static FishPattern PatternOf(FishSpecies sp) => StyleOf(sp).Pattern;

    /// <summary>Slightly richer colours than the raw data.</summary>
    public static Color Vivid(Color c, float saturation = 1.2f)
    {
        float l = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
        return new Color(
            Math.Clamp(l + (c.R - l) * saturation, 0f, 1f),
            Math.Clamp(l + (c.G - l) * saturation, 0f, 1f),
            Math.Clamp(l + (c.B - l) * saturation, 0f, 1f));
    }

    /// <summary>
    /// Paints the light skins of many species at once on worker threads (when a lake is stocked), so the first fish
    /// of a species to swim into view does not stall a frame while its skin is painted.
    /// </summary>
    public static void PrewarmLight(IEnumerable<FishSpecies> species)
    {
        var todo = new List<FishSpecies>();
        foreach (var sp in species)
            if (!Light.ContainsKey(sp.SpeciesName) && todo.TrueForAll(t => t.SpeciesName != sp.SpeciesName))
                todo.Add(sp);
        var images = new Image[todo.Count];
        Parallel.For(0, todo.Count, i => images[i] = PaintImages(todo[i], LightWidth, LightHeight, normals: false).Albedo);
        for (int i = 0; i < todo.Count; i++)
            Light[todo[i].SpeciesName] = ImageTexture.CreateFromImage(images[i]);
    }

    /// <summary>The light skin for fish swimming in the lake (and the icons).</summary>
    public static ImageTexture For(FishSpecies sp)
    {
        if (Light.TryGetValue(sp.SpeciesName, out var tex))
            return tex;
        var (albedo, _) = PaintImages(sp, LightWidth, LightHeight, normals: false);
        tex = ImageTexture.CreateFromImage(albedo);
        Light[sp.SpeciesName] = tex;
        return tex;
    }

    /// <summary>Starts painting the detailed skin in the background (e.g. when a fish bites), if not done yet.</summary>
    public static void Prewarm(FishSpecies sp)
    {
        string key = sp.SpeciesName;
        if (Detailed.ContainsKey(key) || Pending.ContainsKey(key))
            return;
        var size = DetailedSize;
        Pending[key] = Task.Run(() => PaintImages(sp, size.X, size.Y, normals: true));
    }

    /// <summary>The detailed skin if it is ready (null while it is still being painted; call <see cref="Prewarm"/>).</summary>
    public static (ImageTexture Albedo, ImageTexture Normal)? TryDetailed(FishSpecies sp)
    {
        string key = sp.SpeciesName;
        if (Detailed.TryGetValue(key, out var ready))
        {
            DetailedOrder.Remove(key);
            DetailedOrder.AddLast(key);
            return ready;
        }
        if (!Pending.TryGetValue(key, out var job))
        {
            Prewarm(sp);
            return null;
        }
        if (!job.IsCompleted)
            return null;
        Pending.Remove(key);
        var (albedo, normal) = job.Result;
        var pair = (ImageTexture.CreateFromImage(albedo), ImageTexture.CreateFromImage(normal));
        Detailed[key] = pair;
        DetailedOrder.AddLast(key);
        while (DetailedOrder.Count > DetailedCacheSize)
        {
            Detailed.Remove(DetailedOrder.First!.Value);
            DetailedOrder.RemoveFirst();
        }
        return pair;
    }

    /// <summary>Paints the detailed skin right now (blocking); for tools and tests.</summary>
    public static (Image Albedo, Image Normal) PaintNow(FishSpecies sp, int width, int height) => PaintImages(sp, width, height, normals: true);

    // ================================================================== painting
    private static (Image Albedo, Image Normal) PaintImages(FishSpecies sp, int w, int h, bool normals)
    {
        var painter = new Painter(sp, StyleOf(sp));
        var rgb = new byte[w * h * 3];
        var height = normals ? new float[w * h] : null;
        Parallel.For(0, h, y =>
        {
            float v = 1f - (y + 0.5f) / h;                        // 1 = top of the back, 0 = belly
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;                         // 0 = nose, 1 = tail
                var (c, bump) = painter.At(u, v, 1f / w);
                int i = (y * w + x) * 3;
                rgb[i] = (byte)(Math.Clamp(c.R, 0f, 1f) * 255f + 0.5f);
                rgb[i + 1] = (byte)(Math.Clamp(c.G, 0f, 1f) * 255f + 0.5f);
                rgb[i + 2] = (byte)(Math.Clamp(c.B, 0f, 1f) * 255f + 0.5f);
                if (height != null)
                    height[y * w + x] = bump;
            }
        });
        var albedo = Image.CreateFromData(w, h, false, Image.Format.Rgb8, rgb);
        albedo.GenerateMipmaps();
        if (height == null)
            return (albedo, albedo);

        // Normal map from the bump heights (scale edges, scutes), strength scaled to the texel size.
        var nrm = new byte[w * h * 3];
        float strength = w / 512f * 1.6f;
        Parallel.For(0, h, y =>
        {
            int ym = Math.Max(y - 1, 0), yp = Math.Min(y + 1, h - 1);
            for (int x = 0; x < w; x++)
            {
                int xm = Math.Max(x - 1, 0), xp = Math.Min(x + 1, w - 1);
                float dx = (height[y * w + xp] - height[y * w + xm]) * strength;
                float dy = (height[yp * w + x] - height[ym * w + x]) * strength;
                var n = new Vector3(-dx, dy, 1f).Normalized();
                int i = (y * w + x) * 3;
                nrm[i] = (byte)((n.X * 0.5f + 0.5f) * 255f);
                nrm[i + 1] = (byte)((n.Y * 0.5f + 0.5f) * 255f);
                nrm[i + 2] = (byte)((n.Z * 0.5f + 0.5f) * 255f);
            }
        });
        var normal = Image.CreateFromData(w, h, false, Image.Format.Rgb8, nrm);
        normal.GenerateMipmaps();
        return (albedo, normal);
    }

    /// <summary>Per-pixel paint of one species (thread-safe: reads only its own fields).</summary>
    private sealed class Painter
    {
        private readonly SkinStyle _s;
        private readonly Color _back, _belly, _fin, _sheen;
        private readonly float _flankH;        // physical height of the flank relative to the body length
        private readonly uint _seed;

        public Painter(FishSpecies sp, SkinStyle style)
        {
            _s = style;
            _back = Vivid(sp.BackColor) * 0.9f;
            _belly = Vivid(sp.BellyColor, 1.1f);
            _fin = Vivid(sp.FinColor, 1.4f);
            // Silvery fish shine cool white along the flank; others take a warm, golden glow from the belly colour.
            _sheen = new Color(0.86f, 0.9f, 0.95f).Lerp(_belly.Lightened(0.15f), 1f - style.Silver);
            _flankH = 1.3f * MathF.Max(sp.BodyDepth, 0.06f);
            _seed = (uint)Math.Abs(sp.SpeciesName.GetHashCode());
        }

        public (Color Colour, float Bump) At(float u, float v, float texel)
        {
            float X = u, Y = v * _flankH;
            float bump = 0f;

            // --- countershading: dark back, pale belly, a soft sheen along the flank ---
            float t = Smooth(0.36f, 0.74f, v);
            var c = _belly.Lerp(_back, t);
            c = c.Darkened(Smooth(0.86f, 1f, v) * 0.18f);                        // dorsal ridge
            float sheenBand = MathF.Exp(-MathF.Pow((v - 0.5f) / 0.16f, 2f)) * (0.25f + 0.55f * _s.Silver);
            c = c.Lerp(_sheen, sheenBand * 0.55f);
            c = c.Lerp(_belly.Lightened(0.1f), Smooth(0.3f, 0f, v) * 0.4f);       // white throat and belly

            // --- head: smooth skin, darker crown, gill cover ---
            float headEnd = 0.17f + 0.015f * MathF.Sin(v * 6f);
            bool head = u < headEnd;
            if (head)
            {
                c = c.Darkened(Smooth(0.6f, 1f, v) * 0.12f);
                float gill = MathF.Abs(u - (headEnd - 0.012f));
                float edge = MathF.Exp(-MathF.Pow(gill / 0.004f, 2f)) * Smooth(0.15f, 0.35f, v) * Smooth(0.95f, 0.75f, v);
                c = c.Darkened(edge * 0.4f);
                c = c.Lightened(MathF.Exp(-MathF.Pow((u - 0.12f) / 0.035f, 2f)) * Smooth(0.25f, 0.55f, v) * 0.12f * (0.4f + _s.Silver));
                bump -= edge * 0.6f;
            }

            // --- scales ---
            if (!head && _s.Scale > 0f)
            {
                // Scales fade in behind the gill cover instead of starting at a hard edge.
                float fade = Smooth(headEnd, headEnd + 0.04f, u) * (0.55f + 0.45f * Smooth(0.05f, 0.25f, v));
                var (shade, sb) = Scales(X, Y, _s.Scale, texel);
                c = c * (1f + shade * fade);
                bump += sb * fade;
            }

            // --- lateral line with pores ---
            float lineV = 0.56f - 0.06f * MathF.Sin(MathF.PI * Math.Clamp((u - 0.15f) / 0.85f, 0f, 1f)) - 0.04f * Smooth(0.7f, 1f, u);
            float dl = MathF.Abs(v - lineV) * _flankH;
            if (!head && dl < 0.004f)
            {
                float pore = _s.Scale > 0f ? Frac(X / _s.Scale) : Frac(X * 80f);
                c = c.Lerp(pore < 0.35f ? c.Darkened(0.35f) : c.Lightened(0.18f), Smooth(0.004f, 0.0015f, dl) * 0.7f);
            }

            // --- markings ---
            c = Mark(c, u, v, X, Y, ref bump);

            // --- the tail end and fins' base take a little of the fin colour; fine grain over everything ---
            c = c.Lerp(_fin, Smooth(0.9f, 1f, u) * 0.35f);
            float grain = (ValueNoise(X * 900f, Y * 900f) - 0.5f) * 0.05f + (ValueNoise(X * 60f, Y * 60f) - 0.5f) * 0.07f;
            c = new Color(c.R + grain, c.G + grain, c.B + grain);
            return (c, bump);
        }

        /// <summary>Overlapping scales: dark free edges, lighter exposed fields, a highlight near the front.</summary>
        private (float Shade, float Bump) Scales(float X, float Y, float r, float texel)
        {
            float rowH = r * 0.62f;
            float row = MathF.Floor(Y / rowH);
            float off = (((int)row & 1) == 0) ? 0f : 0.5f;
            float gx = X / r + off;
            float col = MathF.Floor(gx);
            float dx = gx - col;                                     // 0 front .. 1 back within the scale
            float dy = Y / rowH - row - 0.5f;                         // -0.5 .. 0.5
            float edge = dx - (0.82f - 1.5f * dy * dy);               // the free (posterior) edge of the scale
            float aa = MathF.Max(texel / r * 1.5f, 0.05f);            // blur thin edges when the texture is small
            float rim = MathF.Exp(-MathF.Pow(edge / (0.07f + aa), 2f));
            float jitter = (Hash((int)col, (int)row) - 0.5f) * 0.08f;
            float field = (1f - dx) * (1f - 4f * dy * dy) * 0.08f;
            float shade = -rim * 0.28f + field + jitter;
            float bump = (1f - Math.Clamp(dx, 0f, 1f)) * (1f - 4f * dy * dy) * 0.5f - rim * 0.5f;
            return (shade, bump);
        }

        private Color Mark(Color c, float u, float v, float X, float Y, ref float bump)
        {
            switch (_s.Pattern)
            {
                case FishPattern.Bars:
                {
                    int n = Math.Max(_s.Count, 3);
                    float pos = (u - 0.22f) / 0.68f * n;
                    if (pos > -0.3f && pos < n)
                    {
                        float wob = (ValueNoise(u * 30f, v * 8f) - 0.5f) * 0.35f;
                        float bar = 1f - Smooth(0.18f, 0.32f, MathF.Abs(Frac(pos + wob) - 0.5f));
                        float vertical = Smooth(0.25f, 0.6f, v) * (0.55f + 0.45f * Smooth(0.6f, 0.9f, v));
                        c = c.Darkened(bar * vertical * 0.5f);
                    }
                    return c;
                }
                case FishPattern.LightSpots:
                {
                    // Pale bean-shaped spots in loose rows over a dark flank.
                    var (d, id) = Spots(X * 1.6f, Y, 0.022f, 0.6f);
                    float spot = Smooth(0.5f, 0.32f, d) * Smooth(0.25f, 0.4f, v) * Smooth(0.98f, 0.85f, v);
                    var pale = _belly.Lightened(0.1f).Lerp(new Color(0.95f, 0.88f, 0.6f), 0.35f + 0.3f * id);
                    return c.Lerp(pale, spot * 0.75f);
                }
                case FishPattern.RedSpots:
                {
                    var (d, id) = Spots(X, Y, 0.022f, 0.7f);
                    float area = Smooth(0.3f, 0.45f, v) * Smooth(0.98f, 0.9f, v) * Smooth(0.08f, 0.2f, u);
                    // Red spots sit along the flank, black ones above them: chosen per spot, never cut by a line.
                    bool red = id > 0.62f - 0.5f * Smooth(0.75f, 0.5f, v);
                    float core = Smooth(0.36f, 0.22f, d) * area;
                    float halo = Smooth(0.6f, 0.42f, d) * area * (red ? 1f : 0f);
                    c = c.Lerp(new Color(0.96f, 0.92f, 0.8f), halo * 0.85f);
                    return c.Lerp(red ? new Color(0.85f, 0.12f, 0.08f) : new Color(0.08f, 0.06f, 0.05f), core);
                }
                case FishPattern.DarkSpots:
                {
                    var (d, id) = Spots(X, Y, _s.Scale > 0.01f ? 0.03f : 0.02f, 0.55f);
                    float area = Smooth(0.45f, 0.6f, v) * Smooth(0.1f, 0.25f, u);
                    float spot = Smooth(0.34f, 0.18f, d + (id - 0.5f) * 0.15f) * area;
                    return c.Darkened(spot * 0.7f);
                }
                case FishPattern.Stripe:
                {
                    float sv = MathF.Abs(v - 0.54f) * _flankH;
                    float stripe = Smooth(0.012f, 0.005f, sv) * Smooth(0.15f, 0.3f, u);
                    var tint = _back.B > _back.R ? new Color(0.45f, 0.75f, 0.95f) : new Color(0.75f, 0.88f, 0.98f);
                    return c.Lerp(tint, stripe * 0.65f);
                }
                case FishPattern.Stripes:
                {
                    foreach (float sv in new[] { 0.78f, 0.55f, 0.33f })
                    {
                        float d = MathF.Abs(v - sv) * _flankH;
                        c = c.Darkened(Smooth(0.008f, 0.003f, d + (ValueNoise(X * 200f, sv * 10f) - 0.5f) * 0.004f) * 0.55f);
                    }
                    return c;
                }
                case FishPattern.Scutes:
                {
                    foreach (float sv in new[] { 0.97f, 0.62f, 0.2f })
                    {
                        float d = MathF.Abs(v - sv) * _flankH;
                        float along = Frac(X / 0.035f);
                        float plate = Smooth(0.016f, 0.008f, d + MathF.Abs(along - 0.5f) * 0.02f) * Smooth(0.12f, 0.18f, u);
                        c = c.Lerp(new Color(0.88f, 0.84f, 0.72f), plate * 0.85f);
                        bump += plate;
                    }
                    return c;
                }
                case FishPattern.Marbled:
                {
                    float n = Fbm(X * 22f + 3f, Y * 22f);
                    float warp = Fbm(X * 40f + n * 2f, Y * 40f - n * 2f);
                    float blotch = Smooth(0.5f, 0.62f, warp) * Smooth(0.2f, 0.4f, v);
                    return c.Darkened(blotch * 0.45f);
                }
                case FishPattern.Rainbow:
                {
                    float band = MathF.Exp(-MathF.Pow((v - 0.5f) / 0.09f, 2f)) * Smooth(0.12f, 0.25f, u);
                    c = c.Lerp(new Color(0.92f, 0.45f, 0.55f), band * 0.6f);
                    var (d, _) = Spots(X, Y, 0.016f, 0.7f);
                    float spot = Smooth(0.3f, 0.15f, d) * Smooth(0.42f, 0.6f, v + Smooth(0.75f, 0.95f, u) * 0.3f);
                    return c.Darkened(spot * 0.75f);
                }
                case FishPattern.Vermiculated:
                {
                    float n = Fbm(X * 30f, Y * 30f);
                    float worms = MathF.Abs(MathF.Sin(n * 28f));
                    float back = Smooth(0.6f, 0.8f, v);
                    c = c.Lerp(new Color(0.8f, 0.78f, 0.55f), Smooth(0.25f, 0.1f, worms) * back * 0.7f);
                    var (d, id) = Spots(X, Y, 0.025f, 0.5f);
                    float flank = Smooth(0.35f, 0.45f, v) * Smooth(0.7f, 0.6f, v);
                    c = c.Lerp(new Color(0.55f, 0.65f, 0.95f), Smooth(0.45f, 0.3f, d) * flank * 0.6f);
                    return c.Lerp(id > 0.5f ? new Color(0.9f, 0.25f, 0.2f) : new Color(0.95f, 0.85f, 0.4f), Smooth(0.25f, 0.12f, d) * flank);
                }
                case FishPattern.Mirror:
                {
                    // A row of big golden scales along the back and a few along the lateral line.
                    float best = 9f;
                    foreach (var (rowV, step) in new[] { (0.9f, 0.075f), (0.55f, 0.11f) })
                    {
                        float k = MathF.Round((X - 0.2f) / step);
                        if (k < 0f || 0.2f + k * step > 0.88f || Hash((int)k, (int)(rowV * 10f)) < 0.35f)
                            continue;
                        float cx = 0.2f + k * step, cy = rowV * _flankH;
                        float d = new Vector2((X - cx) / 0.042f, (Y - cy) / 0.042f).Length();
                        best = MathF.Min(best, d);
                    }
                    if (best < 1f)
                    {
                        float rim = Smooth(0.75f, 1f, best);
                        c = c.Lerp(new Color(0.92f, 0.78f, 0.42f), (1f - best) * 0.45f).Darkened(rim * 0.45f);
                        bump += (1f - best) * 0.8f - rim * 0.6f;
                    }
                    return c;
                }
                case FishPattern.Band:
                {
                    float wob = (Fbm(X * 25f, 1f) - 0.5f) * 0.12f;
                    float band = Smooth(0.09f, 0.04f, MathF.Abs(v - 0.55f - wob)) * Smooth(0.15f, 0.25f, u);
                    return c.Darkened(band * 0.55f);
                }
                case FishPattern.Saddles:
                {
                    int n = Math.Max(_s.Count, 3);
                    float pos = (u - 0.2f) / 0.75f * n;
                    float saddle = (1f - Smooth(0.2f, 0.34f, MathF.Abs(Frac(pos) - 0.5f))) * Smooth(0.5f, 0.75f, v) * (pos > 0f && pos < n ? 1f : 0f);
                    return c.Darkened(saddle * 0.55f);
                }
                case FishPattern.SpotRows:
                {
                    float k = Frac((X - 0.2f) / 0.06f);
                    float d = new Vector2((k - 0.5f) * 0.06f, (v - 0.5f) * _flankH).Length();
                    float spot = Smooth(0.016f, 0.009f, d) * Smooth(0.18f, 0.25f, u);
                    float d2 = new Vector2((Frac((X - 0.17f) / 0.04f) - 0.5f) * 0.04f, (v - 0.82f) * _flankH).Length();
                    spot = MathF.Max(spot, Smooth(0.011f, 0.006f, d2) * Smooth(0.18f, 0.25f, u) * 0.7f);
                    return c.Darkened(spot * 0.6f);
                }
                default:
                    return c;
            }
        }

        /// <summary>Distance (in cell units, 0 = centre) to the nearest randomly placed spot, and that spot's id.</summary>
        private (float Dist, float Id) Spots(float X, float Y, float cell, float density)
        {
            float gx = X / cell, gy = Y / cell;
            int cx = (int)MathF.Floor(gx), cy = (int)MathF.Floor(gy);
            float best = 9f, id = 0f;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int x = cx + i, y = cy + j;
                    if (Hash(x, y) > density)
                        continue;
                    float px = x + 0.2f + 0.6f * Hash(x + 31, y), py = y + 0.2f + 0.6f * Hash(x, y + 57);
                    float r = 0.7f + 0.6f * Hash(x + 7, y + 3);
                    float d = new Vector2(gx - px, gy - py).Length() / r;
                    if (d < best) { best = d; id = Hash(x + 101, y + 13); }
                }
            return (best, id);
        }

        private float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ _seed;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xFFFF) / 65535f;
            }
        }

        private float ValueNoise(float x, float y)
        {
            int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        private float Fbm(float x, float y) =>
            ValueNoise(x, y) * 0.55f + ValueNoise(x * 2.1f, y * 2.1f) * 0.3f + ValueNoise(x * 4.3f, y * 4.3f) * 0.15f;
    }

    private static float Frac(float x) => x - MathF.Floor(x);

    private static float Smooth(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
