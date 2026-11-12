using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using FishingSim.Data;

namespace FishingSim.Graphics;

/// <summary>Which fin a texture is painted for.</summary>
public enum FinKind { Caudal, Dorsal, SpinyDorsal, Sail, Anal, Pectoral, Pelvic, Adipose, Continuous }

/// <summary>
/// Painted fins for close-up fish: U runs along the fin's base (front to back, or top to bottom on the tail), V from
/// the base (0) out to the edge (1). Each fin has jointed rays that branch towards the edge (stiff, pointed spines on
/// spiny fins), a translucent membrane between them that is scalloped and a little ragged at the edge, a darker base,
/// and the species' own markings: the perch's black spot, the pike's spots, the trout's white-edged fins and orange
/// adipose tip, the grayling's spotted, red-rimmed sail, the zander's spotted dorsal. RGBA with straight alpha.
/// </summary>
public static class FinPainter
{
    private static readonly Dictionary<string, ImageTexture> Cache = new();
    private static readonly Dictionary<string, Task<Dictionary<FinKind, Image>>> Pending = new();
    private static readonly FinKind[] AllKinds = (FinKind[])Enum.GetValues(typeof(FinKind));

    private static Vector2I SizeOf(FinKind kind) =>
        kind is FinKind.Caudal or FinKind.Continuous ? new Vector2I(1024, 512) : new Vector2I(512, 256);

    /// <summary>Paints all of a species' fins in the background (when a fish bites), ready for the close-up.</summary>
    public static void Prewarm(FishSpecies sp)
    {
        if (Pending.ContainsKey(sp.SpeciesName) || Cache.ContainsKey($"{sp.SpeciesName}:{FinKind.Caudal}"))
            return;
        Pending[sp.SpeciesName] = Task.Run(() =>
        {
            var images = new Dictionary<FinKind, Image>();
            foreach (var kind in AllKinds)
            {
                var size = SizeOf(kind);
                images[kind] = Paint(sp, kind, size.X, size.Y);
            }
            return images;
        });
    }

    public static ImageTexture For(FishSpecies sp, FinKind kind)
    {
        string key = $"{sp.SpeciesName}:{kind}";
        if (Cache.TryGetValue(key, out var tex))
            return tex;
        if (Pending.TryGetValue(sp.SpeciesName, out var job))
        {
            Pending.Remove(sp.SpeciesName);
            foreach (var (k, img) in job.Result)          // waits only if the background job has not finished yet
                Cache[$"{sp.SpeciesName}:{k}"] = ImageTexture.CreateFromImage(img);
            return Cache[key];
        }
        var size = SizeOf(kind);
        tex = ImageTexture.CreateFromImage(Paint(sp, kind, size.X, size.Y));
        Cache[key] = tex;
        return tex;
    }

    public static Image Paint(FishSpecies sp, FinKind kind, int w, int h)
    {
        var fin = FishSkin.Vivid(sp.FinColor, 1.25f);
        var belly = FishSkin.Vivid(sp.BellyColor);
        string name = sp.SpeciesName;
        uint seed = (uint)Math.Abs((name + kind).GetHashCode());
        int rays = kind switch
        {
            FinKind.Caudal => 19, FinKind.Dorsal => 11, FinKind.SpinyDorsal => 14, FinKind.Sail => 19, FinKind.Anal => 10,
            FinKind.Pectoral => 14, FinKind.Pelvic => 9, FinKind.Continuous => 70, _ => 0,
        };
        var rgba = new byte[w * h * 4];
        Parallel.For(0, h, y =>
        {
            float v = (y + 0.5f) / h;                          // 0 = base, 1 = edge (rows top to bottom)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                var (c, a) = Pixel(kind, name, fin, belly, rays, u, v, seed, 1f / w);
                int i = (y * w + x) * 4;
                rgba[i] = (byte)(Math.Clamp(c.R, 0f, 1f) * 255f);
                rgba[i + 1] = (byte)(Math.Clamp(c.G, 0f, 1f) * 255f);
                rgba[i + 2] = (byte)(Math.Clamp(c.B, 0f, 1f) * 255f);
                rgba[i + 3] = (byte)(Math.Clamp(a, 0f, 1f) * 255f);
            }
        });
        var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
        img.GenerateMipmaps();
        return img;
    }

    private static (Color Colour, float Alpha) Pixel(FinKind kind, string name, Color fin, Color belly, int rays, float u, float v, uint seed, float texel)
    {
        bool spiny = kind == FinKind.SpinyDorsal && u < 0.55f;
        // --- base colour: darker and denser at the root, clearer towards the edge ---
        var c = fin.Darkened(0.25f * (1f - v)).Lerp(fin.Lightened(0.12f), v * 0.4f);
        float membrane = Mathf.Lerp(0.9f, 0.5f, v);

        // --- rays: jointed, branching near the edge; spines are stiff and taper to a point ---
        float ray = 0f, edgeCut = 1f;
        if (rays > 0)
        {
            float x = u * rays;
            float f = x - MathF.Floor(x) - 0.5f;                         // -0.5..0.5 across the cell, 0 = on the ray
            float width = spiny ? 0.16f * (1f - 0.8f * v) : 0.09f * (1f - 0.45f * v);
            width = MathF.Max(width, texel * rays * 0.8f);
            float d = MathF.Abs(f);
            if (!spiny && v > 0.5f)
            {
                float split = 0.16f * (v - 0.5f) / 0.5f;                // the ray forks into two
                d = MathF.Min(MathF.Abs(f - split), MathF.Abs(f + split));
            }
            ray = Smooth(width, width * 0.4f, d);
            if (!spiny && Frac(v / 0.075f + Hash((int)x, 3, seed) * 0.5f) < 0.12f)
                ray *= 0.55f;                                             // joints along a soft ray
            // The membrane is scalloped between rays; between spines it hangs much lower.
            float between = 1f - 2f * MathF.Abs(f);
            edgeCut = spiny ? 1f - 0.32f * between * between : 1f - 0.06f * between - 0.02f * Hash((int)(x * 3f), 7, seed);
        }
        else
        {
            edgeCut = 0.96f + 0.04f * Hash((int)(u * 40f), 9, seed);   // adipose: a fleshy lobe
            membrane = 0.95f;
        }
        bool onRay = ray > 0.05f;
        if (v > edgeCut && !(spiny && onRay))
            return (c, 0f);
        // A thin rim along the outer edge.
        float rim = Smooth(edgeCut - 0.05f, edgeCut, v);
        c = c.Darkened(rim * 0.15f);
        c = c.Lerp(fin.Darkened(0.4f), ray * 0.7f);
        float alpha = Mathf.Lerp(membrane, 0.97f, ray);

        // --- the species' markings ---
        switch (name)
        {
            case "Okoń":
                if (kind == FinKind.SpinyDorsal && u < 0.55f)
                    c = c.Lerp(new Color(0.55f, 0.6f, 0.5f), 0.35f);      // the spiny dorsal is grey, the rest red
                if (kind == FinKind.SpinyDorsal && u > 0.3f && u < 0.52f && v > 0.12f && v < 0.88f)
                    c = c.Lerp(new Color(0.04f, 0.04f, 0.05f), Smooth(0.52f, 0.47f, u) * Smooth(0.3f, 0.36f, u) * 0.92f);
                break;
            case "Szczupak":
                if (kind is FinKind.Caudal or FinKind.Dorsal or FinKind.Anal)
                    c = Spots(c, u, v, seed, 0.09f, 0.55f, new Color(0.12f, 0.1f, 0.06f), 0.7f);
                break;
            case "Pstrąg potokowy" or "Pstrąg źródlany" or "Palia" or "Głowacica":
                if (kind is FinKind.Pelvic or FinKind.Anal or FinKind.Pectoral && u < 0.14f)
                    c = c.Lerp(new Color(0.96f, 0.95f, 0.9f), 0.85f);     // white leading edge
                else if (kind is FinKind.Pelvic or FinKind.Anal && u < 0.22f && name != "Pstrąg potokowy")
                    c = c.Lerp(new Color(0.05f, 0.05f, 0.05f), 0.8f);     // with a black stripe behind it
                if (kind == FinKind.Adipose && name == "Pstrąg potokowy")
                    c = c.Lerp(new Color(0.9f, 0.3f, 0.12f), Smooth(0.4f, 0.8f, v));
                if (kind is FinKind.Dorsal && name == "Pstrąg potokowy")
                    c = Spots(c, u, v, seed, 0.12f, 0.45f, new Color(0.15f, 0.1f, 0.06f), 0.6f);
                break;
            case "Pstrąg tęczowy" or "Troć wędrowna" or "Troć jeziorowa" or "Łosoś":
                if (kind is FinKind.Caudal or FinKind.Dorsal or FinKind.Adipose)
                    c = Spots(c, u, v, seed, 0.07f, 0.5f, new Color(0.08f, 0.08f, 0.08f), 0.8f);
                break;
            case "Lipień":
                if (kind == FinKind.Sail)
                {
                    c = c.Lerp(new Color(0.35f, 0.55f, 0.5f), 0.3f * (1f - v));
                    c = Spots(c, u, v * 1.6f, seed, 0.08f, 0.6f, new Color(0.12f, 0.1f, 0.12f), 0.8f);
                    c = c.Lerp(new Color(0.8f, 0.2f, 0.4f), Smooth(edgeCut - 0.2f, edgeCut - 0.08f, v) * 0.85f);
                }
                break;
            case "Sandacz" or "Sandacz wołżański":
                if (kind is FinKind.SpinyDorsal or FinKind.Dorsal or FinKind.Caudal)
                    c = Spots(c, u, v, seed, 0.06f, 0.65f, new Color(0.12f, 0.12f, 0.1f), 0.7f);
                break;
            case "Sum" or "Miętus" or "Węgorz" or "Sumik karłowaty":
                c = c.Lerp(fin.Darkened(0.5f), Smooth(0.6f, 0.95f, v) * 0.6f);
                break;
            case "Karp" or "Sazan" or "Karp lustrzeń" or "Brzana":
                if (kind is FinKind.Dorsal or FinKind.Anal && u < 1f / MathF.Max(rays, 1))
                    c = c.Darkened(0.35f);                                   // the serrated first ray
                break;
        }
        return (c, alpha);
    }

    private static Color Spots(Color c, float u, float v, uint seed, float cell, float density, Color spot, float strength)
    {
        float gx = u / cell, gy = v / cell;
        int ix = (int)MathF.Floor(gx), iy = (int)MathF.Floor(gy);
        float best = 9f;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int x = ix + i, y = iy + j;
                if (Hash(x, y, seed) > density)
                    continue;
                float px = x + 0.2f + 0.6f * Hash(x + 5, y, seed), py = y + 0.2f + 0.6f * Hash(x, y + 9, seed);
                best = MathF.Min(best, new Vector2(gx - px, gy - py).Length());
            }
        return c.Lerp(spot, Smooth(0.32f, 0.18f, best) * strength);
    }

    private static float Hash(int x, int y, uint seed)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ seed;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }
    }

    private static float Frac(float x) => x - MathF.Floor(x);

    private static float Smooth(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

/// <summary>
/// The iris of a close-up eye: a disc with the species' iris colour in fine radial streaks, a dark ring at its rim, a
/// golden inner ring and the round black pupil (slightly teardrop-shaped towards the snout, as in most fish).
/// </summary>
public static class EyePainter
{
    private static readonly Dictionary<string, ImageTexture> Cache = new();

    public static ImageTexture For(Color iris)
    {
        string key = iris.ToHtml();
        if (Cache.TryGetValue(key, out var tex))
            return tex;
        const int size = 256;
        var data = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = MathF.Sqrt(dx * dx + dy * dy);
                float ang = MathF.Atan2(dy, dx);
                // Pupil, a little drawn out towards the snout (+x in the disc's texture).
                float pr = 0.42f * (1f + 0.12f * MathF.Max(MathF.Cos(ang), 0f));
                Color c;
                if (r < pr)
                {
                    c = new Color(0.01f, 0.01f, 0.015f);
                }
                else
                {
                    float t = (r - pr) / (1f - pr);
                    float streak = 0.85f + 0.15f * MathF.Sin(ang * 46f + MathF.Sin(ang * 7f) * 2f);
                    c = iris.Lightened(0.25f * (1f - t)) * streak;
                    c = c.Lerp(new Color(1f, 0.88f, 0.5f), MathF.Max(0f, 1f - t * 8f) * 0.5f);   // bright inner ring
                    c = c.Darkened(MathF.Max(0f, (t - 0.82f) / 0.18f) * 0.7f);                    // dark rim
                }
                float a = r < 1f ? 1f : 0f;
                int i = (y * size + x) * 4;
                data[i] = (byte)(Math.Clamp(c.R, 0f, 1f) * 255f);
                data[i + 1] = (byte)(Math.Clamp(c.G, 0f, 1f) * 255f);
                data[i + 2] = (byte)(Math.Clamp(c.B, 0f, 1f) * 255f);
                data[i + 3] = (byte)(a * 255f);
            }
        var img = Image.CreateFromData(size, size, false, Image.Format.Rgba8, data);
        img.GenerateMipmaps();
        tex = ImageTexture.CreateFromImage(img);
        Cache[key] = tex;
        return tex;
    }
}
