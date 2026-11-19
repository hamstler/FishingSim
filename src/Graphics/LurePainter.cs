using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace FishingSim.Graphics;

/// <summary>
/// Paints the lures' skins the way a lure maker does: a hard bait gets a holographic scale foil under translucent
/// paint (back in the main colour, flanks and belly in the accent fading to pearl), its pattern (tiger bars, spots
/// with halos, a red head, stripes), a gill plate, a dark back line and a painted 3D eye; a soft bait is tinted plastic
/// with glitter flakes and speckles; a spoon's face is polished or hammered metal with its paint; a jig head is
/// lacquered lead with an eye. Every set is an albedo, an ORM map (occlusion, roughness, metal: the foil and the flakes
/// shine, the paint does not) and a normal map (scales, gill crease, the eye's rim, hammer dimples), block compressed.
/// Coordinates: u runs along the body from the nose (0) to the tail (1), v round it from the belly (0, 1) over the
/// right flank (0.25) to the back (0.5); a plate's v runs across it.
/// </summary>
public static class LurePainter
{
    public enum Kind { Hard, Soft, Head, Plate, Blade }

    /// <summary>A painted eye: its centre along the body and up the flank (-1 belly .. 1 back), radii in u and v.</summary>
    public readonly record struct EyeSpot(float U, float H, float Ru, float Rv);

    /// <summary>What to paint. <paramref name="Aspect"/> = the body's girth over its length (keeps scales and spots round).</summary>
    public readonly record struct Spec(Kind Kind, Color Main, Color Accent, string Pattern, bool Metal, EyeSpot? Eye,
        float Aspect, int Seed, float GillU = 0.2f);

    public sealed class Set
    {
        public Texture2D Albedo = null!;
        public Texture2D Orm = null!;
        public Texture2D Normal = null!;
    }

    private static readonly Dictionary<string, Set> Cache = new();
    private static readonly List<string> Order = new();
    private const int Keep = 24;

    /// <summary>Pixels along a body skin: 1024 on High/Ultra, 512 on lower settings.</summary>
    public static int Detail => TextureStore.MaxSize >= 2048 ? 1024 : 512;

    public static Set Paint(Spec s)
    {
        int w = s.Kind switch { Kind.Head => Detail / 4, Kind.Plate => Detail / 2, Kind.Blade => Detail / 4, _ => Detail };
        int h = s.Kind is Kind.Hard or Kind.Soft ? w / 2 : w;
        string key = $"{s.Kind}|{s.Main.ToHtml()}|{s.Accent.ToHtml()}|{s.Pattern}|{s.Metal}|{s.Eye}|{s.Aspect:0.00}|{s.Seed}|{s.GillU:0.00}|{w}";
        if (Cache.TryGetValue(key, out var set))
            return set;
        set = Build(s, w, h);
        Cache[key] = set;
        Order.Add(key);
        if (Order.Count > Keep)
        {
            Cache.Remove(Order[0]);
            Order.RemoveAt(0);
        }
        return set;
    }

    // ------------------------------------------------------------------ noise
    private static float Hash(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
    }

    private static float Noise(float x, float y, int seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    private static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    private static Color Mix(Color a, Color b, float t) => a.Lerp(b, Math.Clamp(t, 0f, 1f));

    // ------------------------------------------------------------------ painting
    private struct Texel
    {
        public Color C;
        public float Rough, Metal, Ao, Height;
    }

    private static Set Build(Spec s, int w, int h)
    {
        var px = new Texel[w * h];
        var spots = Spots(s);
        Parallel.For(0, h, FishingSim.Graphics.Workers.Options, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                px[y * w + x] = s.Kind switch
                {
                    Kind.Hard => Hard(s, u, v, spots),
                    Kind.Soft => Soft(s, u, v),
                    Kind.Head => Head(s, u, v),
                    _ => Plate(s, u, v, spots),
                };
            }
        });
        return Finish(px, w, h, s.Kind is Kind.Plate or Kind.Blade ? 2.5f : s.Kind == Kind.Soft ? 1.2f : 3f);
    }

    /// <summary>Random spots for dotted patterns: (u, h or v, radius in body lengths).</summary>
    private static List<Vector3> Spots(Spec s)
    {
        var list = new List<Vector3>();
        if (s.Pattern != "dots")
            return list;
        var rng = new Random(s.Seed * 31 + 7);
        int n = s.Kind == Kind.Hard ? 34 : s.Kind == Kind.Blade ? 5 : 9;
        for (int i = 0; i < n; i++)
        {
            if (s.Kind == Kind.Hard)
                list.Add(new Vector3(0.24f + (float)rng.NextDouble() * 0.62f, -0.15f + (float)rng.NextDouble() * 0.95f, 0.012f + (float)rng.NextDouble() * 0.012f));
            else
                list.Add(new Vector3(0.15f + (float)rng.NextDouble() * 0.7f, 0.2f + (float)rng.NextDouble() * 0.6f, 0.05f + (float)rng.NextDouble() * 0.05f));
        }
        return list;
    }

    /// <summary>Fish scales on a staggered grid: the most forward scale covering a point shows, its rear rim raised.
    /// Returns (height 0..1, rim 0..1, the scale's random value).</summary>
    private static (float Height, float Rim, float Id) Scales(float x, float y, float size)
    {
        float row = y / (size * 0.5f);
        int r0 = (int)MathF.Floor(row);
        float best = float.MaxValue, height = 0f, rim = 0f, id = 0f;
        for (int r = r0 - 1; r <= r0 + 2; r++)
        {
            float off = (r & 1) == 0 ? 0f : 0.5f;
            float cy = r * size * 0.5f;
            int c0 = (int)MathF.Floor(x / size - off);
            for (int c = c0 - 1; c <= c0 + 1; c++)
            {
                float cx = (c + off) * size;
                float dx = x - cx, dy = y - cy;
                float d = MathF.Sqrt(dx * dx + dy * dy) / (size * 0.72f);
                // Only the rear half-disc is exposed: the scale in front covers the rest.
                if (d > 1f || dx < -0.15f * size)
                    continue;
                if (cx < best)
                {
                    best = cx;
                    height = (1f - d) * 0.7f + Math.Clamp(dx / size, 0f, 1f) * 0.3f;
                    rim = Smooth(0.78f, 0.97f, d);
                    id = Hash(c, r, 17);
                }
            }
        }
        return (height, rim, id);
    }

    private static float EyeAt(EyeSpot e, float u, float h, float v, out float dist, out float dy)
    {
        // The eye sits at the same height on both flanks: v of the right flank, mirrored for the left.
        float ve = MathF.Acos(Math.Clamp(-e.H, -1f, 1f)) / Mathf.Tau;
        float vv = v > 0.5f ? 1f - v : v;
        float du = (u - e.U) / e.Ru, dv = (vv - ve) / e.Rv;
        dy = -dv;
        dist = MathF.Sqrt(du * du + dv * dv);
        return dist;
    }

    /// <summary>A lure eye: a gold, streaked iris with a dark rim, a big black pupil and a sparkle.</summary>
    private static Color EyeColour(float d, float dy, float angle, Color iris)
    {
        var dark = new Color(0.03f, 0.03f, 0.035f);
        if (d < 0.46f)
            return dark.Lerp(new Color(0.1f, 0.1f, 0.12f), Smooth(0.46f, 0f, d) * 0.3f);
        float streak = 0.85f + 0.15f * MathF.Sin(angle * 23f);
        var c = iris.Lightened(0.25f * Smooth(0.9f, 0.5f, d)) * streak;
        c = c.Lerp(iris.Darkened(0.6f), Smooth(0.8f, 1f, d));
        c = c.Lerp(c.Lightened(0.5f), Smooth(0.2f, 0.9f, dy) * 0.35f);
        return c.Lerp(dark, Smooth(0.5f, 0.46f, d));
    }

    private static Texel Hard(Spec s, float u, float v, List<Vector3> spots)
    {
        float th = v * Mathf.Tau;
        float hgt = -MathF.Cos(th);                    // -1 belly .. 1 back
        float x = u, y = v * s.Aspect;
        var main = s.Main;
        var acc = s.Accent;
        var pearl = new Color(0.94f, 0.93f, 0.9f);
        // Paint layers: belly pearl -> flank in the accent -> back in the main colour, soft airbrushed borders.
        var flank = acc;
        var belly = Mix(acc, pearl, 0.55f);
        var c = Mix(belly, flank, Smooth(-0.95f, -0.25f, hgt));
        float back = Smooth(0.05f, 0.62f, hgt + 0.08f * (Noise(u * 9f, v * 4f, s.Seed) - 0.5f));
        c = Mix(c, main, back);
        c = Mix(c, main.Darkened(0.45f), Smooth(0.82f, 0.98f, hgt));      // the dark line along the back
        // The holographic foil under the clear paint: strongest on the flank, a scale pattern, a slight rainbow.
        var sc = Scales(x, y, 0.042f);
        float scaleZone = Smooth(0.2f, 0.32f, u) * (1f - Smooth(0.95f, 1.0f, u));
        float foil = (1f - back * 0.85f) * scaleZone * (0.55f + 0.45f * Smooth(-0.95f, -0.5f, hgt));
        float hue = sc.Id * 0.25f + u * 0.4f;
        var rainbow = Color.FromHsv(hue % 1f, 0.18f, 1f);
        var foilCol = Mix(flank, pearl, 0.35f) * rainbow;
        c = Mix(c, foilCol.Lightened(0.1f * sc.Height), foil * 0.55f);
        c = c.Darkened(sc.Rim * 0.22f * scaleZone);
        float metal = foil * 0.75f;
        float rough = 0.42f - foil * 0.25f;
        float height = sc.Height * 0.45f * scaleZone;
        // Patterns.
        switch (s.Pattern)
        {
            case "bars":
            {
                var bar = main.Darkened(0.62f);
                for (int i = 0; i < 5; i++)
                {
                    float cu = 0.3f + i * 0.13f + 0.025f * MathF.Sin(hgt * 2.6f + i * 1.7f);
                    float wb = 0.03f * (0.55f + 0.45f * Smooth(-0.5f, 0.6f, hgt));
                    float b = Smooth(wb, wb * 0.55f, MathF.Abs(u - cu)) * Smooth(-0.55f, -0.15f, hgt);
                    c = Mix(c, bar, b * 0.88f);
                    metal *= 1f - b * 0.8f;
                }
                break;
            }
            case "dots":
                foreach (var sp in spots)
                {
                    float vs = MathF.Acos(Math.Clamp(-sp.Y, -1f, 1f)) / Mathf.Tau;
                    float vv = v > 0.5f ? 1f - v : v;
                    float du = (u - sp.X), dv = (vv - vs) * s.Aspect;
                    float d = MathF.Sqrt(du * du + dv * dv) / sp.Z;
                    if (d < 1.6f)
                    {
                        // A spot of the accent with a pale halo (trout-like), or a pale freckle.
                        c = Mix(c, main.Lightened(0.6f), Smooth(1.6f, 1.15f, d) * 0.7f);
                        c = Mix(c, acc.Darkened(0.15f), Smooth(1.05f, 0.75f, d));
                        metal *= Smooth(0.7f, 1.4f, d);
                    }
                }
                break;
            case "redhead":
            {
                float head = Smooth(0.26f, 0.2f, u + 0.02f * hgt);
                c = Mix(c, acc, head);
                metal *= 1f - head;
                break;
            }
            case "stripes":
                c = Mix(c, acc.Darkened(0.2f), Smooth(0.09f, 0.05f, MathF.Abs(hgt - 0.15f)) * scaleZone);
                break;
            case "glitter":
                if (Hash((int)(u * 160), (int)(v * 80), s.Seed) > 0.93f)
                {
                    c = acc.Lightened(0.4f);
                    metal = 1f;
                    rough = 0.15f;
                }
                break;
        }
        // Gill plate: a darker head behind an arched crease.
        float gill = s.GillU - 0.045f * hgt * hgt;
        float headSide = Smooth(gill + 0.006f, gill - 0.006f, u);
        c = c.Darkened(headSide * 0.06f);
        float crease = Smooth(0.006f, 0.0f, MathF.Abs(u - gill)) * Smooth(-0.85f, -0.6f, hgt) * Smooth(0.85f, 0.6f, hgt);
        c = c.Darkened(crease * 0.45f);
        height -= crease * 0.6f;
        // The mouth: a dark slit at the nose.
        float mouth = Smooth(0.035f, 0.0f, u) * Smooth(0.12f, 0.0f, MathF.Abs(hgt + 0.05f));
        c = c.Darkened(mouth * 0.7f);
        // The eye: painted, with a raised rim (a clear dome goes over it).
        if (s.Eye is EyeSpot e)
        {
            EyeAt(e, u, hgt, v, out float d, out float dy);
            if (d < 1.25f)
            {
                float ang = MathF.Atan2(dy, (u - e.U) / e.Ru);
                var iris = s.Pattern == "redhead" ? new Color(0.95f, 0.82f, 0.2f) : new Color(0.98f, 0.72f, 0.12f);
                if (d < 1f)
                {
                    c = EyeColour(d, dy, ang, iris);
                    metal = 0.6f;
                    rough = 0.2f;
                }
                height += Smooth(1.25f, 1.02f, d) * Smooth(0.9f, 1.02f, d) * 1.2f;
            }
        }
        float ao = 1f - sc.Rim * 0.15f * scaleZone - crease * 0.3f;
        return new Texel { C = c, Rough = rough, Metal = metal, Ao = ao, Height = height };
    }

    private static Texel Soft(Spec s, float u, float v)
    {
        float hgt = -MathF.Cos(v * Mathf.Tau);
        var c = s.Main;
        // Laminated two-tone ("halves") or a slightly darker back; the plastic's swirls.
        if (s.Pattern == "halves")
            c = Mix(s.Accent, s.Main, Smooth(-0.1f, 0.1f, hgt));
        else
            c = Mix(c, c.Darkened(0.25f), Smooth(0.3f, 0.95f, hgt));
        c = c.Darkened(0.08f * Noise(u * 30f, v * 12f, s.Seed));
        float metal = 0f, rough = 0.55f;
        // Glitter flakes (the accent) and dark pepper.
        int fx = (int)(u * 220), fy = (int)(v * 110);
        float r = Hash(fx, fy, s.Seed + 3);
        if (s.Pattern == "glitter" && r > 0.9f)
        {
            c = s.Accent.Lightened(0.5f);
            metal = 1f;
            rough = 0.18f;
        }
        else if (r < 0.02f)
            c = c.Darkened(0.6f);
        return new Texel { C = c, Rough = rough, Metal = metal, Ao = 1f, Height = 0.3f * Noise(u * 60f, v * 25f, s.Seed + 9) };
    }

    private static Texel Head(Spec s, float u, float v)
    {
        var c = s.Metal ? new Color(0.42f, 0.42f, 0.44f) : s.Main;
        float rough = s.Metal ? 0.55f : 0.3f, metal = s.Metal ? 0.7f : 0f;
        c = c.Darkened(0.06f * Noise(u * 20f, v * 20f, s.Seed));
        float height = s.Metal ? 0.25f * Noise(u * 50f, v * 50f, s.Seed + 1) : 0f;
        if (s.Eye is EyeSpot e)
        {
            float hgt = -MathF.Cos(v * Mathf.Tau);
            EyeAt(e, u, hgt, v, out float d, out float dy);
            if (d < 1f)
            {
                c = EyeColour(d, dy, MathF.Atan2(dy, (u - e.U) / e.Ru), new Color(0.98f, 0.82f, 0.15f));
                metal = 0.5f;
                rough = 0.2f;
            }
            height += Smooth(1.2f, 1f, d) * Smooth(0.85f, 1f, d);
        }
        return new Texel { C = c, Rough = rough, Metal = metal, Ao = 1f, Height = height };
    }

    /// <summary>The convex face of a spoon or a spinner blade: polished or hammered metal and its paint.</summary>
    private static Texel Plate(Spec s, float u, float v, List<Vector3> spots)
    {
        bool blade = s.Kind == Kind.Blade;
        var metalCol = s.Metal ? s.Main : new Color(0.86f, 0.87f, 0.9f);
        var c = metalCol;
        float metal = 1f, rough = 0.12f;
        // Hammered dimples: a cell pattern of shallow dents.
        float hx = u * (blade ? 9f : 14f), hy = v * (blade ? 6f : 8f);
        float dimple = 1f;
        int ix = (int)MathF.Floor(hx), iy = (int)MathF.Floor(hy);
        for (int j = iy - 1; j <= iy + 1; j++)
            for (int i = ix - 1; i <= ix + 1; i++)
            {
                float cx = i + Hash(i, j, s.Seed), cy = j + Hash(i, j, s.Seed + 5);
                float d = MathF.Sqrt((hx - cx) * (hx - cx) + (hy - cy) * (hy - cy));
                dimple = MathF.Min(dimple, d);
            }
        float height = s.Metal && s.Pattern is "plain" or "halves" ? -Smooth(0.75f, 0f, dimple) * 0.5f : 0f;
        void PaintOn(Color p, float a)
        {
            c = Mix(c, p, a);
            metal *= 1f - a;
            rough = rough + (0.38f - rough) * a;
        }
        float across = MathF.Abs(v - 0.5f) * 2f;
        switch (s.Pattern)
        {
            case "halves":
                PaintOn(s.Accent, Smooth(0.47f, 0.53f, u + 0.08f * (v - 0.5f)));
                break;
            case "stripes":
                PaintOn(s.Accent, Smooth(0.07f, 0.04f, MathF.Abs((u * 0.9f + v * 0.35f) % 0.3f - 0.15f)) * Smooth(0.15f, 0.25f, u));
                break;
            case "bars":
            {
                // A painted perch: green body, darker bars, an orange throat.
                if (!s.Metal)
                    PaintOn(s.Main, 1f);
                PaintOn(s.Accent, Smooth(0.6f, 0.95f, across) * 0.9f);
                for (int i = 0; i < (blade ? 2 : 4); i++)
                {
                    float cu = (blade ? 0.35f : 0.22f) + i * (blade ? 0.3f : 0.18f);
                    PaintOn(s.Main.Darkened(0.6f), Smooth(0.045f, 0.025f, MathF.Abs(u - cu - 0.03f * across)) * Smooth(0.95f, 0.6f, across));
                }
                break;
            }
            case "dots":
                if (!s.Metal)
                    PaintOn(s.Main, 1f);
                foreach (var sp in spots)
                {
                    float d = MathF.Sqrt((u - sp.X) * (u - sp.X) + (v - sp.Y) * (v - sp.Y)) / sp.Z;
                    PaintOn(s.Accent, Smooth(1f, 0.85f, d));
                }
                break;
            default:
                if (!s.Metal)
                    PaintOn(s.Main, 1f);
                else if (!blade)
                {
                    // A red flash tag near the front, as on the classic spoons.
                    float d = MathF.Sqrt((u - 0.3f) * (u - 0.3f) * 4f + (v - 0.5f) * (v - 0.5f)) / 0.12f;
                    PaintOn(new Color(0.82f, 0.08f, 0.06f), Smooth(1f, 0.9f, d));
                }
                break;
        }
        // A fine engraved fish-scale texture on the metal of the long spoons and the blades.
        if (metal > 0.5f && !blade && s.Pattern != "plain")
        {
            var sc = Scales(u, v * 0.4f, 0.05f);
            height += sc.Height * 0.25f;
            c = c.Darkened(sc.Rim * 0.12f);
        }
        return new Texel { C = c, Rough = rough, Metal = metal, Ao = 1f, Height = height };
    }

    // ------------------------------------------------------------------ textures
    private static Set Finish(Texel[] px, int w, int h, float strength)
    {
        var alb = new byte[w * h * 4];
        var orm = new byte[w * h * 4];
        var nrm = new byte[w * h * 4];
        Parallel.For(0, h, FishingSim.Graphics.Workers.Options, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, o = i * 4;
                var t = px[i];
                alb[o] = B(t.C.R); alb[o + 1] = B(t.C.G); alb[o + 2] = B(t.C.B); alb[o + 3] = 255;
                orm[o] = B(t.Ao); orm[o + 1] = B(Math.Clamp(t.Rough, 0.04f, 1f)); orm[o + 2] = B(t.Metal); orm[o + 3] = 255;
                // Height differences to a tangent-space normal (wrapping round the body).
                float hl = px[y * w + (x + w - 1) % w].Height, hr = px[y * w + (x + 1) % w].Height;
                float hd = px[((y + h - 1) % h) * w + x].Height, hu = px[((y + 1) % h) * w + x].Height;
                var n = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).Normalized();
                nrm[o] = B(n.X * 0.5f + 0.5f); nrm[o + 1] = B(n.Y * 0.5f + 0.5f); nrm[o + 2] = B(n.Z * 0.5f + 0.5f); nrm[o + 3] = 255;
            }
        });
        return new Set
        {
            Albedo = Tex(alb, w, h, BlockCompress.Kind.Colour),
            Orm = Tex(orm, w, h, BlockCompress.Kind.Colour),
            Normal = Tex(nrm, w, h, BlockCompress.Kind.TwoChannel),
        };
    }

    private static byte B(float f) => (byte)Math.Clamp((int)(f * 255f + 0.5f), 0, 255);

    private static Texture2D Tex(byte[] data, int w, int h, BlockCompress.Kind kind)
    {
        var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(BlockCompress.Compress(img, kind));
    }
}
