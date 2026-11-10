using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;
using FishingSim.Data;

namespace FishingSim.Fishing;

/// <summary>
/// Measurements of a real fish of the species, taken from a freely licensed reference photograph
/// (assets/fish/&lt;id&gt;.json, made by tools/fish_photos: cutout.py measures the body outline, measure.py the colours).
/// The painted model is lofted to this outline and painted in these colours, so it keeps the game's look while its
/// silhouette and colouring match the species; the photograph itself is not part of the game.
/// </summary>
public sealed class FishReference
{
    /// <summary>Height / length of the photographed fish (with its tail).</summary>
    public float Aspect { get; private init; }

    /// <summary>Where the body (without the tail fin) starts and ends along the fish (0..1 of its whole length).</summary>
    public float BodyStart { get; private init; }
    public float BodyEnd { get; private init; }

    /// <summary>Top and bottom edge of the body (fractions of the photo height) at evenly spaced points from BodyStart to BodyEnd.</summary>
    public IReadOnlyList<(float Top, float Bottom)> Outline { get; private init; } = Array.Empty<(float, float)>();

    /// <summary>Where the tip of the snout is (fraction of the whole length).</summary>
    public float Snout { get; private init; }

    /// <summary>
    /// The cleaned side view (measure.py): back height above and belly depth below the body axis, in fractions of the
    /// whole length, evenly from the snout to the tail stalk. Empty when only the raw outline was measured.
    /// </summary>
    public IReadOnlyList<(float Up, float Down)> Profile { get; private init; } = Array.Empty<(float, float)>();

    /// <summary>Measured colours of the back, the middle of the flank and the belly (null when not measured).</summary>
    public Color? Back { get; private init; }
    public Color? Flank { get; private init; }
    public Color? Belly { get; private init; }

    public string Author { get; private init; } = "";
    public string Licence { get; private init; } = "";
    public string Source { get; private init; } = "";

    /// <summary>For the atlas: whose photograph the shape and colours were measured from.</summary>
    public string Credit => $"Kształt i barwy według zdjęcia: {Author} ({Licence}), Wikimedia Commons";

    private static readonly Dictionary<string, FishReference?> Cache = new();

    public static string IdOf(FishSpecies sp) => sp.ResourcePath.GetFile().GetBaseName();

    public static bool Has(FishSpecies sp) => For(sp) != null;

    public static FishReference? For(FishSpecies sp)
    {
        string id = IdOf(sp);
        if (id == "")
            return null;
        if (Cache.TryGetValue(id, out var cached))
            return cached;
        FishReference? found = null;
        string json = $"res://assets/fish/{id}.json";
        if (FileAccess.FileExists(json) && JsonNode.Parse(FileAccess.GetFileAsString(json)) is JsonObject meta)
        {
            var outline = new List<(float, float)>();
            if (meta["outline"] is JsonArray arr)
                foreach (var p in arr)
                    if (p is JsonArray pair && pair.Count == 2)
                        outline.Add(((float)pair[0]!.GetValue<double>(), (float)pair[1]!.GetValue<double>()));
            var profile = new List<(float, float)>();
            if (meta["profile"] is JsonArray parr)
                foreach (var p in parr)
                    if (p is JsonArray pair && pair.Count == 2)
                        profile.Add(((float)pair[0]!.GetValue<double>(), (float)pair[1]!.GetValue<double>()));
            var palette = meta["palette"] as JsonObject;
            Color? Colour(string key) => palette?[key] is JsonArray c && c.Count == 3
                ? new Color((float)c[0]!.GetValue<double>(), (float)c[1]!.GetValue<double>(), (float)c[2]!.GetValue<double>())
                : null;
            if (outline.Count >= 8)
                found = new FishReference
                {
                    Aspect = (float)(meta["aspect"]?.GetValue<double>() ?? 0.3),
                    BodyStart = (float)(meta["body_start"]?.GetValue<double>() ?? 0.0),
                    BodyEnd = Math.Clamp((float)(meta["body_end"]?.GetValue<double>() ?? 0.82), 0.6f, 0.92f),
                    Outline = outline,
                    Snout = (float)(meta["snout"]?.GetValue<double>() ?? meta["body_start"]?.GetValue<double>() ?? 0.0),
                    Profile = profile.Count >= 8 ? profile : Array.Empty<(float, float)>(),
                    Back = Colour("back"), Flank = Colour("flank"), Belly = Colour("belly"),
                    Author = meta["author"]?.GetValue<string>() ?? "",
                    Licence = meta["licence"]?.GetValue<string>() ?? "",
                    Source = meta["source"]?.GetValue<string>() ?? "",
                };
        }
        Cache[id] = found;
        return found;
    }

    /// <summary>The body axis: the mean centre of the outline (photo fraction of the height).</summary>
    public float Axis
    {
        get
        {
            float yc = 0f;
            foreach (var (top, bottom) in Outline)
                yc += (top + bottom) * 0.5f;
            return yc / Outline.Count;
        }
    }

    /// <summary>Length of the body from the snout to the tail stalk, as a fraction of the whole fish.</summary>
    public float BodyLength => BodyEnd - (Profile.Count >= 8 ? Snout : 0f);

    /// <summary>
    /// Height of the back above the axis and depth of the belly below it, in units of the fish's whole length, at
    /// <paramref name="t"/> along the body (0 = snout, 1 = tail stalk).
    /// </summary>
    public (float Top, float Bottom) EdgesAt(float t, float axis)
    {
        if (Profile.Count >= 8)
        {
            // The cleaned profile runs from the snout (t = 0) to the tail stalk (t = 1).
            float g = Math.Clamp(t, 0f, 1f) * (Profile.Count - 1);
            int a = Math.Min((int)g, Profile.Count - 2);
            float w = g - a;
            return (MathF.Max(Mathf.Lerp(Profile[a].Up, Profile[a + 1].Up, w), 0.002f),
                MathF.Max(Mathf.Lerp(Profile[a].Down, Profile[a + 1].Down, w), 0.002f));
        }
        float x = Math.Clamp(t, 0f, 1f) * BodyEnd;                       // along the whole fish
        float f = Math.Clamp((x - BodyStart) / MathF.Max(BodyEnd - BodyStart, 1e-4f), 0f, 1f) * (Outline.Count - 1);
        int i = Math.Min((int)f, Outline.Count - 2);
        float k = f - i;
        float top = Mathf.Lerp(Outline[i].Top, Outline[i + 1].Top, k);
        float bottom = Mathf.Lerp(Outline[i].Bottom, Outline[i + 1].Bottom, k);
        return (MathF.Max((axis - top) * Aspect, 0.002f), MathF.Max((bottom - axis) * Aspect, 0.002f));
    }
}
