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
