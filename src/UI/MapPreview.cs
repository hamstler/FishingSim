using System;
using Godot;
using FishingSim.Data;
using FishingSim.World;

namespace FishingSim.UI;

/// <summary>Top-down image of a location, drawn from the same functions the game uses for terrain and depth.</summary>
public static class MapPreview
{
    /// <summary>World rectangle (x, z, width, length) that frames the lake and the pier.</summary>
    public static Rect2 Bounds(LocationData loc)
    {
        float x0 = -loc.OutlineRx - 12f;
        float x1 = loc.OutlineRx + 12f;
        float z0 = loc.OutlineCz - loc.OutlineRz - 12f;
        float z1 = 16f;
        // Bays and arms beyond the main ellipse.
        foreach (var entry in loc.Lobes)
        {
            var n = LocationData.Numbers(entry);
            if (n.Length < 4)
                continue;
            x0 = MathF.Min(x0, n[0] - n[2] - 8f);
            x1 = MathF.Max(x1, n[0] + n[2] + 8f);
            z0 = MathF.Min(z0, n[1] - n[3] - 8f);
        }
        // Include the village: every building and the plaza, with a margin.
        foreach (var spec in loc.BuildingSpecs())
        {
            z1 = MathF.Max(z1, spec.Position.Y + 14f);
            x0 = MathF.Min(x0, spec.Position.X - 14f);
            x1 = MathF.Max(x1, spec.Position.X + 14f);
        }
        return new Rect2(x0, z0, x1 - x0, z1 - z0);
    }

    public static float Aspect(LocationData loc)
    {
        var b = Bounds(loc);
        return b.Size.Y / b.Size.X;
    }

    /// <summary>
    /// A topographic picture of a map (for the HUD's round map; safe to call on a worker thread, returns an image):
    /// land tinted by height (meadow, olive slopes, brown hillsides, grey rock above the map's rock line, snow above its
    /// snow line) and shaded by the relief as if lit from the north-west, contour lines every 2 m (heavier every 10 m);
    /// the lake from pale shallows to dark deep water with depth contours every metre (heavier every 5 m); the shore
    /// drawn as a firm line.
    /// </summary>
    public static Image RenderTopo(LocationData loc, int w, int h, Rect2 area)
    {
        var bed = new LakeBed();
        // Heights on the pixel grid with a one-pixel border (for the slopes and the contours at the edges).
        var height = new float[(w + 2) * (h + 2)];
        for (int py = -1; py <= h; py++)
        {
            float z = area.Position.Y + (py + 0.5f) / h * area.Size.Y;
            for (int px = -1; px <= w; px++)
            {
                float x = area.Position.X + (px + 0.5f) / w * area.Size.X;
                height[(py + 1) * (w + 2) + px + 1] = bed.HeightAt(loc, x, z);
            }
        }
        bed.Free();
        float H(int px, int py) => height[(py + 1) * (w + 2) + px + 1];
        float cellX = area.Size.X / w, cellZ = area.Size.Y / h;
        var light = new Vector3(-1f, 1.6f, -1f).Normalized();     // from the north-west, fairly high
        var data = new byte[w * h * 3];
        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                float hc = H(px, py);
                float dx = (H(px + 1, py) - H(px - 1, py)) / (2f * cellX), dz = (H(px, py + 1) - H(px, py - 1)) / (2f * cellZ);
                var n = new Vector3(-dx, 1f, -dz).Normalized();
                float shade = Math.Clamp(n.Dot(light), 0f, 1f);
                Color c;
                bool water = hc < 0f;
                if (water)
                {
                    float depth = -hc;
                    c = new Color(0.55f, 0.82f, 0.86f).Lerp(new Color(0.04f, 0.16f, 0.32f), Math.Clamp(depth / 9f, 0f, 1f));
                    c = c * (0.9f + 0.15f * shade);
                }
                else
                {
                    if (hc < 0.6f)
                        c = new Color(0.8f, 0.74f, 0.54f);                                           // beach
                    else if (hc < loc.RockLine)
                        c = new Color(0.45f, 0.62f, 0.3f).Lerp(new Color(0.55f, 0.55f, 0.3f), Math.Clamp((hc - 0.6f) / Math.Max(loc.RockLine - 0.6f, 1f), 0f, 1f));
                    else if (hc < loc.SnowLine)
                        c = new Color(0.55f, 0.5f, 0.38f).Lerp(new Color(0.58f, 0.57f, 0.55f), Math.Clamp((hc - loc.RockLine) / Math.Max(loc.SnowLine - loc.RockLine, 1f), 0f, 1f));
                    else
                        c = new Color(0.92f, 0.94f, 0.96f);
                    c = c * (0.5f + 0.75f * shade);
                }
                // Contours: a line where the band of height changes towards a neighbour.
                float step = water ? 1f : 2f;
                int band = (int)MathF.Floor(hc / step);
                int bandR = (int)MathF.Floor(H(px + 1, py) / step), bandD = (int)MathF.Floor(H(px, py + 1) / step);
                bool shore = hc < 0f != H(px + 1, py) < 0f || hc < 0f != H(px, py + 1) < 0f;
                if (shore)
                    c = new Color(0.12f, 0.1f, 0.08f);
                else if (band != bandR || band != bandD)
                {
                    int edge = Math.Max(Math.Max(band, bandR), bandD);
                    bool major = water ? edge % 5 == 0 : edge % 5 == 0;    // every 5 m in the lake, every 10 m on land
                    if (water)
                        c = c.Lerp(new Color(0.88f, 0.96f, 1f), major ? 0.55f : 0.28f);
                    else
                        c = c.Lerp(new Color(0.22f, 0.14f, 0.07f), major ? 0.72f : 0.45f);
                }
                int o = (py * w + px) * 3;
                data[o] = (byte)(Math.Clamp(c.R, 0f, 1f) * 255f);
                data[o + 1] = (byte)(Math.Clamp(c.G, 0f, 1f) * 255f);
                data[o + 2] = (byte)(Math.Clamp(c.B, 0f, 1f) * 255f);
            }
        }
        var img = Image.CreateFromData(w, h, false, Image.Format.Rgb8, data);
        img.GenerateMipmaps();
        return img;
    }

    public static ImageTexture Render(LocationData loc, int w = 210, int h = 150, Rect2? area = null)
    {
        var rect = area ?? Bounds(loc);
        var bed = new LakeBed();
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
        for (int py = 0; py < h; py++)
        {
            float z = rect.Position.Y + (py + 0.5f) / h * rect.Size.Y;
            for (int px = 0; px < w; px++)
            {
                float x = rect.Position.X + (px + 0.5f) / w * rect.Size.X;
                float d = bed.DepthAt(loc, x, z);
                Color c;
                if (d > 0f)
                {
                    c = new Color(0.42f, 0.78f, 0.82f).Lerp(new Color(0.03f, 0.14f, 0.3f), Mathf.Clamp(d / 9f, 0f, 1f));
                }
                else
                {
                    float hg = bed.HeightAt(loc, x, z);
                    c = hg < 0.6f
                        ? new Color(0.76f, 0.7f, 0.5f)
                        : new Color(0.36f, 0.55f, 0.22f).Lerp(new Color(0.16f, 0.32f, 0.12f), Mathf.Clamp(hg / 12f, 0f, 1f));
                }
                img.SetPixel(px, py, c);
            }
        }
        // Pier marker (only in the small previews; the live map draws its own).
        if (w <= 300)
        {
            int mx = (int)((0f - rect.Position.X) / rect.Size.X * w);
            int my = (int)((0f - rect.Position.Y) / rect.Size.Y * h);
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    img.SetPixel(Mathf.Clamp(mx + dx, 0, w - 1), Mathf.Clamp(my + dy, 0, h - 1), new Color(0.9f, 0.2f, 0.15f));
        }
        bed.Free();
        return ImageTexture.CreateFromImage(img);
    }
}
