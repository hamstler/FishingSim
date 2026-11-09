using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Data;

namespace FishingSim.Graphics;

/// <summary>
/// Pixel-art pictures for the shops, the inventory and the fish lists, drawn in code (64×64, dark outline, two-tone
/// shading), one per item and per species. Shown with nearest filtering so they stay crisp at any size.
/// </summary>
public static class Icons
{
    public const int Size = 64;
    private static readonly Dictionary<string, ImageTexture> Cache = new();
    private static readonly Color Outline = new(0.13f, 0.08f, 0.05f);

    // ------------------------------------------------------------------ canvas
    private sealed class Canvas
    {
        public readonly Image Img = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);

        public void Px(int x, int y, Color c)
        {
            if (x >= 0 && y >= 0 && x < Size && y < Size)
                Img.SetPixel(x, y, c);
        }

        public void Disc(float cx, float cy, float rx, float ry, Color c, Color? shade = null)
        {
            for (int y = (int)(cy - ry - 1); y <= cy + ry + 1; y++)
                for (int x = (int)(cx - rx - 1); x <= cx + rx + 1; x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy > 1f)
                        continue;
                    // Light from the top-left: the lower-right part takes the shade colour.
                    bool dark = shade != null && dx * 0.6f + dy * 0.8f > 0.35f;
                    Px(x, y, dark ? shade!.Value : c);
                }
        }

        public void Rect(int x0, int y0, int x1, int y1, Color c)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    Px(x, y, c);
        }

        public void Line(float x0, float y0, float x1, float y1, float width, Color c)
        {
            int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(x1 - x0), MathF.Abs(y1 - y0)) * 2f) + 1;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float x = x0 + (x1 - x0) * t, y = y0 + (y1 - y0) * t;
                if (width <= 1.01f)
                    Px((int)x, (int)y, c);
                else
                    Disc(x, y, width * 0.5f, width * 0.5f, c);
            }
        }

        public void Ring(float cx, float cy, float r, float thickness, Color c)
        {
            for (int y = (int)(cy - r - 1); y <= cy + r + 1; y++)
                for (int x = (int)(cx - r - 1); x <= cx + r + 1; x++)
                {
                    float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    if (d <= r && d >= r - thickness)
                        Px(x, y, c);
                }
        }

        /// <summary>One-pixel dark outline around everything drawn (the pixel-art finish).</summary>
        public ImageTexture Finish()
        {
            var copy = (Image)Img.Duplicate();
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (copy.GetPixel(x, y).A > 0.5f)
                        continue;
                    bool near = false;
                    for (int k = 0; k < 4 && !near; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        near = nx >= 0 && ny >= 0 && nx < Size && ny < Size && copy.GetPixel(nx, ny).A > 0.5f;
                    }
                    if (near)
                        Img.SetPixel(x, y, Outline);
                }
            return ImageTexture.CreateFromImage(Img);
        }
    }

    private static Color C(float r, float g, float b) => new(r, g, b);

    private static ImageTexture Cached(string key, Func<Canvas, Canvas> draw)
    {
        if (Cache.TryGetValue(key, out var t))
            return t;
        t = draw(new Canvas()).Finish();
        Cache[key] = t;
        return t;
    }

    // ------------------------------------------------------------------ items
    public static ImageTexture For(TackleItem item) => item switch
    {
        RodData r => Rod(r),
        ReelData r => Reel(r),
        LineData l => Line(l),
        HookData h => Hook(h),
        BaitData b => Bait(b.ItemId),
        KeepnetData k => Keepnet(k),
        LandingNetData n => LandingNet(n),
        AccessoryData a => Accessory(a),
        _ => Cached("unknown", c => { c.Disc(32, 32, 16, 16, C(0.6f, 0.6f, 0.6f)); return c; }),
    };

    private static ImageTexture Rod(RodData r) => Cached("rod:" + r.ItemId, c =>
    {
        // Blank colour from the wood: hazel (light), ash (pale yellow), oak (dark).
        var wood = r.ItemId switch { "junior" => C(0.78f, 0.58f, 0.32f), "karpiowka" => C(0.45f, 0.29f, 0.16f), _ => C(0.86f, 0.74f, 0.46f) };
        var shade = wood.Darkened(0.3f);
        c.Line(10, 56, 58, 6, 3.2f, shade);
        c.Line(9, 55, 57, 5, 2.2f, wood);
        c.Line(6, 60, 20, 45, 5f, C(0.36f, 0.22f, 0.13f));                      // leather grip
        c.Line(6, 59, 19, 45, 3f, C(0.5f, 0.32f, 0.18f));
        foreach (float t in new[] { 0.35f, 0.55f, 0.72f, 0.86f })
            c.Disc(10 + 48 * t + 1.5f, 56 - 50 * t + 1.5f, 1.4f, 1.4f, C(0.75f, 0.6f, 0.3f));   // brass guides
        c.Disc(23, 41, 2.2f, 2.2f, C(0.85f, 0.8f, 0.6f));                                   // twine wrap
        return c;
    });

    private static ImageTexture Reel(ReelData r) => Cached("reel:" + r.ItemId, c =>
    {
        var body = r.ItemId switch { "mlynek1000" => C(0.55f, 0.36f, 0.2f), "mlynek3000" => C(0.5f, 0.52f, 0.56f), _ => C(0.85f, 0.66f, 0.25f) };
        c.Rect(28, 8, 34, 20, C(0.32f, 0.22f, 0.14f));                                   // foot
        c.Disc(30, 36, 18, 18, body, body.Darkened(0.3f));
        c.Disc(30, 36, 11, 11, C(0.85f, 0.78f, 0.6f), C(0.7f, 0.62f, 0.45f));          // line on the spool
        c.Disc(30, 36, 4, 4, body.Darkened(0.4f));
        c.Line(30, 36, 50, 50, 3f, body.Darkened(0.2f));                                // handle arm
        c.Disc(52, 52, 4.5f, 4.5f, C(0.3f, 0.2f, 0.12f), C(0.2f, 0.13f, 0.08f));       // knob
        return c;
    });

    private static ImageTexture Line(LineData l) => Cached("line:" + l.ItemId, c =>
    {
        var thread = l.ItemId == "plecionka030" ? C(0.82f, 0.74f, 0.55f) : l.ItemId == "zylka016" ? C(0.93f, 0.9f, 0.84f) : C(0.86f, 0.82f, 0.72f);
        c.Rect(14, 14, 50, 50, C(0.45f, 0.3f, 0.18f));                                  // spool flanges
        c.Rect(18, 18, 46, 46, thread);
        for (int y = 20; y < 46; y += 3)
            c.Line(18, y, 46, y + 1, 1f, thread.Darkened(0.18f));                      // windings
        c.Disc(32, 32, 4, 4, C(0.3f, 0.2f, 0.12f));
        c.Line(46, 40, 58, 56, 1f, thread);                                             // loose end
        return c;
    });

    private static ImageTexture Hook(HookData h) => Cached("hook:" + h.ItemId, c =>
    {
        float s = 0.55f + h.Size * 0.05f;
        var iron = C(0.62f, 0.64f, 0.68f);
        float cx = 32, top = 32 - 22 * s, bend = 32 + 14 * s;
        c.Ring(cx, top - 3, 3.5f * s + 1, 2, iron);                                      // eye
        c.Line(cx, top, cx, bend, 3f, iron);                                             // shank
        for (int a = 0; a <= 180; a += 6)
        {
            float rad = Mathf.DegToRad(a);
            c.Disc(cx - 9 * s + MathF.Cos(rad) * 9 * s, bend + MathF.Sin(rad) * 9 * s, 1.5f, 1.5f, iron);
        }
        c.Line(cx - 18 * s, bend, cx - 18 * s, bend - 9 * s, 3f, iron);               // point
        c.Line(cx - 18 * s, bend - 9 * s, cx - 14 * s, bend - 5 * s, 2f, iron);        // barb
        c.Line(cx + 1, top, cx + 1, bend, 1f, C(0.85f, 0.86f, 0.9f));                  // shine
        return c;
    });

    public static ImageTexture Bait(string id) => Cached("bait:" + id, c =>
    {
        switch (id)
        {
            case "worm":
                for (int i = 0; i < 40; i++)
                {
                    float t = i / 39f;
                    c.Disc(12 + t * 40, 32 + MathF.Sin(t * 9f) * 9, 4.2f, 4.2f, C(0.86f, 0.48f, 0.5f), C(0.7f, 0.34f, 0.38f));
                }
                break;
            case "maggot":
                foreach (var (x, y) in new[] { (20, 24), (40, 22), (30, 40), (46, 42) })
                    for (int k = 0; k < 4; k++)
                        c.Disc(x + k * 3, y + k, 3.2f, 3.6f, C(0.96f, 0.92f, 0.8f), C(0.85f, 0.8f, 0.66f));
                break;
            case "insect":
                c.Disc(30, 34, 15, 6, C(0.45f, 0.65f, 0.2f), C(0.32f, 0.5f, 0.15f));
                c.Disc(46, 30, 6, 5, C(0.5f, 0.7f, 0.22f));
                c.Line(24, 36, 14, 50, 2f, C(0.32f, 0.5f, 0.15f));
                c.Line(34, 38, 40, 52, 2f, C(0.32f, 0.5f, 0.15f));
                c.Line(48, 26, 58, 14, 1f, C(0.3f, 0.45f, 0.12f));
                c.Px(48, 28, C(0.05f, 0.05f, 0.05f));
                break;
            case "bread":
                c.Disc(32, 34, 24, 15, C(0.78f, 0.5f, 0.22f), C(0.62f, 0.38f, 0.16f));
                c.Disc(32, 32, 16, 8, C(0.96f, 0.86f, 0.62f));
                break;
            case "corn":
                foreach (var (x, y) in new[] { (22, 22), (36, 20), (46, 30), (28, 34), (40, 42), (20, 44), (32, 50) })
                    c.Disc(x, y, 5, 7, C(0.93f, 0.78f, 0.38f), C(0.8f, 0.62f, 0.25f));
                break;
            case "boilie":
                c.Disc(26, 36, 13, 13, C(0.85f, 0.55f, 0.25f), C(0.65f, 0.38f, 0.16f));
                c.Disc(44, 26, 9, 9, C(0.9f, 0.7f, 0.35f), C(0.7f, 0.5f, 0.2f));
                break;
            default:   // livebait: a small silvery fish
                c.Disc(30, 32, 20, 8, C(0.75f, 0.8f, 0.82f), C(0.55f, 0.6f, 0.62f));
                c.Line(48, 32, 58, 24, 3f, C(0.6f, 0.65f, 0.7f));
                c.Line(48, 32, 58, 40, 3f, C(0.6f, 0.65f, 0.7f));
                c.Disc(16, 30, 2, 2, C(0.05f, 0.05f, 0.05f));
                break;
        }
        return c;
    });

    private static ImageTexture Keepnet(KeepnetData k) => Cached("keepnet:" + k.ItemId, c =>
    {
        var net = k.ItemId == "siatka_karpiowa" ? C(0.6f, 0.45f, 0.25f) : C(0.75f, 0.72f, 0.6f);
        int rings = k.ItemId == "siatka_mala" ? 3 : 5;
        for (int i = 0; i < rings; i++)
        {
            float y = 12 + i * (40f / (rings - 1));
            c.Ring(32, y, 16 - i * 1.5f, 2, C(0.45f, 0.3f, 0.18f));
        }
        for (int x = 18; x <= 46; x += 4)
            c.Line(x, 12, 32 + (x - 32) * 0.7f, 52, 1f, net);
        for (int y = 14; y <= 52; y += 4)
            c.Line(18 + (y - 12) * 0.13f, y, 46 - (y - 12) * 0.13f, y, 1f, net.Darkened(0.15f));
        return c;
    });

    private static ImageTexture LandingNet(LandingNetData n) => Cached("landingnet:" + n.ItemId, c =>
    {
        float r = n.MaxWeight > 10f ? 17f : n.MaxWeight > 5f ? 14f : 11f;
        c.Line(40, 40, 60, 60, 3.5f, C(0.5f, 0.33f, 0.18f));                            // handle
        for (int y = (int)(26 - r); y < 26 + r + 10; y += 3)
            for (int x = (int)(26 - r); x < 26 + r; x += 3)
            {
                float dx = (x - 26) / r, dy = (y - 26) / (r + 6);
                if (dx * dx + dy * dy < 1f && y > 22)
                    c.Px(x, y, C(0.8f, 0.76f, 0.62f));
            }
        c.Ring(26, 26, r, 2.5f, C(0.55f, 0.36f, 0.2f));
        return c;
    });

    private static ImageTexture Accessory(AccessoryData a) => Cached("acc:" + a.ItemId, c =>
    {
        switch (a.Kind)
        {
            case AccessoryKind.PolarizedGlasses:   // brimmed hat
                c.Disc(32, 40, 28, 7, C(0.5f, 0.36f, 0.2f), C(0.38f, 0.26f, 0.14f));
                c.Disc(32, 28, 14, 14, C(0.6f, 0.44f, 0.25f), C(0.46f, 0.32f, 0.18f));
                c.Rect(18, 32, 46, 35, C(0.7f, 0.2f, 0.15f));
                break;
            case AccessoryKind.Headlamp:           // tallow lantern
                c.Rect(20, 18, 44, 52, C(0.25f, 0.24f, 0.24f));
                c.Rect(23, 22, 41, 48, C(1f, 0.85f, 0.45f));
                c.Disc(32, 38, 4, 7, C(1f, 0.6f, 0.15f));
                c.Ring(32, 13, 6, 2, C(0.25f, 0.24f, 0.24f));
                break;
            case AccessoryKind.RodPod:             // forked stick with a bell
                c.Line(32, 60, 32, 26, 4f, C(0.5f, 0.33f, 0.18f));
                c.Line(32, 28, 20, 8, 3.5f, C(0.5f, 0.33f, 0.18f));
                c.Line(32, 28, 44, 8, 3.5f, C(0.5f, 0.33f, 0.18f));
                c.Disc(46, 22, 6, 7, C(0.95f, 0.78f, 0.3f), C(0.78f, 0.6f, 0.18f));
                break;
            case AccessoryKind.Chair:              // three-legged stool
                c.Disc(32, 20, 22, 6, C(0.62f, 0.42f, 0.24f), C(0.5f, 0.32f, 0.18f));
                c.Line(18, 22, 10, 58, 3.5f, C(0.45f, 0.3f, 0.17f));
                c.Line(46, 22, 54, 58, 3.5f, C(0.45f, 0.3f, 0.17f));
                c.Line(32, 24, 32, 60, 3.5f, C(0.4f, 0.26f, 0.15f));
                break;
            default:                                // wooden bucket
                c.Rect(16, 22, 48, 56, C(0.6f, 0.4f, 0.22f));
                for (int x = 20; x < 48; x += 6)
                    c.Line(x, 22, x, 56, 1f, C(0.48f, 0.3f, 0.16f));
                c.Rect(14, 28, 50, 31, C(0.35f, 0.35f, 0.38f));
                c.Rect(14, 48, 50, 51, C(0.35f, 0.35f, 0.38f));
                c.Disc(32, 22, 16, 4, C(0.25f, 0.45f, 0.6f));
                break;
        }
        return c;
    });

    // ------------------------------------------------------------------ fish
    /// <summary>
    /// The species side-on: its real photograph when there is one (assets/fish, scaled into a 128 px tile), otherwise
    /// drawn with its painted skin (FishSkin), fins and eye.
    /// </summary>
    public static ImageTexture Fish(FishSpecies sp)
    {
        string key = "photo:" + sp.SpeciesName;
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        if (Fishing.PhotoFish.For(sp) is { } photo)
        {
            var img = (Image)photo.Image.Duplicate();
            img.ClearMipmaps();
            const int size = 128;
            int w = size, h = Math.Max((int)(size * photo.Aspect), 8);
            img.Resize(w, h, Image.Interpolation.Lanczos);
            var tile = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            tile.Fill(new Color(0, 0, 0, 0));
            tile.BlendRect(img, new Rect2I(0, 0, w, h), new Vector2I(0, (size - h) / 2));
            var tex = ImageTexture.CreateFromImage(tile);
            Cache[key] = tex;
            return tex;
        }
        return PaintedFish(sp);
    }

    private static ImageTexture PaintedFish(FishSpecies sp) => Cached("fish:" + sp.SpeciesName, c =>
    {
        var skin = FishSkin.For(sp).GetImage();
        float depth = Math.Clamp(sp.BodyDepth, 0.07f, 0.45f);
        float bodyLen = 46f, h = MathF.Max(bodyLen * depth, 5f);
        float x0 = 6f, cy = 32f;
        var fin = FishSkin.Vivid(sp.FinColor, 1.5f);
        // Tail and fins first, the body is drawn over them.
        // Filled forked tail.
        for (float k = -h * 0.85f; k <= h * 0.85f; k += 0.5f)
            if (MathF.Abs(k) > h * 0.2f || MathF.Abs(k) < 0.6f)
                c.Line(x0 + bodyLen - 3, cy, 60 - (MathF.Abs(k) < h * 0.3f ? 4 : 0), cy + k, 1f, fin);
        float dorsal = x0 + bodyLen * (1f - sp.DorsalPos);
        c.Line(dorsal - 5, cy - h * 0.45f, dorsal + 3, cy - h * 0.95f, 3f, fin);
        c.Line(x0 + bodyLen * 0.6f, cy + h * 0.45f, x0 + bodyLen * 0.68f, cy + h * 0.8f, 2.5f, fin);
        for (int y = (int)(cy - h); y <= cy + h; y++)
            for (int x = (int)x0; x <= x0 + bodyLen; x++)
            {
                float u = (x - x0) / bodyLen;                                  // 0 nose, 1 tail
                float profile = MathF.Pow(MathF.Sin(MathF.PI * MathF.Pow(Math.Clamp(u, 0.001f, 1f), 0.75f)), 0.8f) * 0.88f + 0.12f * u;
                float half = h * 0.5f * profile;
                if (MathF.Abs(y - cy) > half)
                    continue;
                float v = (y - (cy - half)) / MathF.Max(2f * half, 1f);       // 0 back, 1 belly
                int sw = skin.GetWidth(), sh = skin.GetHeight();
                var col = skin.GetPixel(Math.Clamp((int)(u * (sw - 1)), 0, sw - 1), Math.Clamp((int)(v * (sh - 1)), 0, sh - 1));
                c.Px(x, y, col);
            }
        c.Disc(x0 + bodyLen * 0.1f, cy - h * 0.12f, 2.2f, 2.2f, C(0.95f, 0.78f, 0.25f));
        c.Px((int)(x0 + bodyLen * 0.1f), (int)(cy - h * 0.12f), C(0.02f, 0.02f, 0.02f));
        return c;
    });

    /// <summary>Category pictures for shop tabs.</summary>
    public static ImageTexture Category(string key) => key switch
    {
        "rod" => Rod(new RodData()),
        "reel" => Reel(new ReelData()),
        "line" => Line(new LineData()),
        "hook" => Hook(new HookData { Size = 6 }),
        "bait" => Bait("worm"),
        "keepnet" => Keepnet(new KeepnetData()),
        "landingnet" => LandingNet(new LandingNetData { MaxWeight = 6 }),
        "accessory" => Accessory(new AccessoryData { Kind = AccessoryKind.PolarizedGlasses }),
        "fish" or "market" => Bait("livebait"),
        _ => Cached("unknown", c => { c.Disc(32, 32, 16, 16, C(0.6f, 0.6f, 0.6f)); return c; }),
    };
}
