using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;
using FishingSim.Data;

namespace FishingSim.Fishing;

/// <summary>
/// A real photograph of a species (assets/fish/&lt;id&gt;.webp or .png, cut out with tools/fish_photos/cutout.py) with its body
/// outline and credit (&lt;id&gt;.json). When present, the fish is built from it: the body is lofted to the photo's own
/// silhouette and wears the photo on both flanks, and the fins and tail come from the cut-out itself.
/// </summary>
public sealed class PhotoFish
{
    public ImageTexture Texture { get; private init; } = null!;
    public Image Image { get; private init; } = null!;

    /// <summary>Height / width of the photo.</summary>
    public float Aspect { get; private init; }

    /// <summary>Where the body (without the tail fin) starts and ends along the photo (0..1).</summary>
    public float BodyStart { get; private init; }
    public float BodyEnd { get; private init; }

    /// <summary>Top and bottom edge of the body (photo fractions) at evenly spaced points from BodyStart to BodyEnd.</summary>
    public IReadOnlyList<(float Top, float Bottom)> Outline { get; private init; } = Array.Empty<(float, float)>();

    public string Author { get; private init; } = "";
    public string Licence { get; private init; } = "";
    public string Source { get; private init; } = "";

    /// <summary>"Zdjęcie: author (licence)" for the atlas.</summary>
    public string Credit => $"Zdjęcie: {Author} ({Licence}), Wikimedia Commons";

    private static readonly Dictionary<string, PhotoFish?> Cache = new();

    public static string IdOf(FishSpecies sp) => sp.ResourcePath.GetFile().GetBaseName();

    public static bool Has(FishSpecies sp) => For(sp) != null;

    public static PhotoFish? For(FishSpecies sp)
    {
        string id = IdOf(sp);
        if (id == "")
            return null;
        if (Cache.TryGetValue(id, out var cached))
            return cached;
        PhotoFish? photo = null;
        string pic = $"res://assets/fish/{id}.webp", json = $"res://assets/fish/{id}.json";
        if (!FileAccess.FileExists(pic))
            pic = $"res://assets/fish/{id}.png";
        if (FileAccess.FileExists(pic) && FileAccess.FileExists(json) && JsonNode.Parse(FileAccess.GetFileAsString(json)) is JsonObject meta)
        {
            var img = Godot.Image.LoadFromFile(pic);
            if (img != null && !img.IsEmpty())
            {
                img.Convert(Godot.Image.Format.Rgba8);
                img.GenerateMipmaps();
                var outline = new List<(float, float)>();
                if (meta["outline"] is JsonArray arr)
                    foreach (var p in arr)
                        if (p is JsonArray pair && pair.Count == 2)
                            outline.Add(((float)pair[0]!.GetValue<double>(), (float)pair[1]!.GetValue<double>()));
                photo = new PhotoFish
                {
                    Image = img,
                    Texture = ImageTexture.CreateFromImage(img),
                    Aspect = (float)(meta["aspect"]?.GetValue<double>() ?? img.GetHeight() / (double)img.GetWidth()),
                    BodyStart = (float)(meta["body_start"]?.GetValue<double>() ?? 0.0),
                    BodyEnd = (float)(meta["body_end"]?.GetValue<double>() ?? 0.85),
                    Outline = outline,
                    Author = meta["author"]?.GetValue<string>() ?? "",
                    Licence = meta["licence"]?.GetValue<string>() ?? "",
                    Source = meta["source"]?.GetValue<string>() ?? "",
                };
                if (outline.Count < 8)
                    photo = null;
            }
        }
        Cache[id] = photo;
        return photo;
    }
}
