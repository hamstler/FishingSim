using Godot;
using Godot.Collections;

namespace FishingSim.Data;

/// <summary>A fishing spot: bed shape (depth under the bobber) and which species live there.</summary>
[GlobalClass]
public partial class LocationData : Resource
{
    [Export] public string DisplayName { get; set; } = "Jezioro";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export] public int RequiredLevel { get; set; } = 1;

    // Outline: an ellipse around (0, OutlineCz).
    [Export] public float OutlineCz { get; set; } = -75f;
    [Export] public float OutlineRx { get; set; } = 120f;
    [Export] public float OutlineRz { get; set; } = 77f;

    // Bed shape.
    /// <summary>Metres of depth gained per metre out from the pier end.</summary>
    [Export] public float Slope { get; set; } = 0.15f;
    [Export] public float MaxDepth { get; set; } = 8f;
    [Export] public Vector2 HolePos { get; set; } = new(12, -40);
    [Export] public float HoleDepth { get; set; } = 4f;
    [Export] public float HoleRadius { get; set; } = 9f;
    [Export] public Vector2 BarPos { get; set; } = new(-8, -22);
    [Export] public float BarHeight { get; set; } = 1.5f;
    [Export] public float BarRadius { get; set; } = 6f;

    /// <summary>Species name -> abundance multiplier (a missing species is not present).</summary>
    [Export] public Dictionary Abundance { get; set; } = new();

    // --- look: terrain palette and water ---
    [Export] public Color GrassColor { get; set; } = new(0.26f, 0.42f, 0.15f);
    [Export] public Color DarkGrassColor { get; set; } = new(0.14f, 0.28f, 0.11f);
    [Export] public Color SandColor { get; set; } = new(0.55f, 0.48f, 0.32f);
    [Export] public Color PathColor { get; set; } = new(0.45f, 0.36f, 0.24f);
    [Export] public Color WaterShallowColor { get; set; } = new(0.04f, 0.26f, 0.26f);
    [Export] public Color WaterDeepColor { get; set; } = new(0.01f, 0.07f, 0.11f);

    /// <summary>Extra fog density (mist over marshes, haze in the mountains).</summary>
    [Export] public float FogBoost { get; set; }

    /// <summary>Multiplier for how tall the surrounding hills are.</summary>
    [Export] public float HillScale { get; set; } = 1f;

    // --- look: vegetation ---
    /// <summary>Overall amount of trees (1 = the default forest).</summary>
    [Export] public float TreeDensity { get; set; } = 1f;

    /// <summary>Tree kind ("spruce", "oak", "birch", "pine", "willow") -> relative share.</summary>
    [Export] public Dictionary TreeMix { get; set; } = new();
    [Export] public float BushDensity { get; set; } = 1f;
    [Export] public float FlowerDensity { get; set; } = 1f;
    [Export] public float ReedDensity { get; set; } = 1f;
    [Export] public float RockDensity { get; set; } = 1f;

    // --- village ---
    /// <summary>Where the paths meet; buildings face this point.</summary>
    [Export] public Vector2 PlazaCenter { get; set; } = new(0, 28);

    /// <summary>One entry per building or prop: "kind,x,z" (see <see cref="BuildingSpec"/>).</summary>
    [Export] public string[] Buildings { get; set; } = System.Array.Empty<string>();

    public float TreeShare(string kind) =>
        TreeMix.TryGetValue(kind, out var v) ? v.AsSingle() : 0f;

    public System.Collections.Generic.List<BuildingSpec> BuildingSpecs()
    {
        var list = new System.Collections.Generic.List<BuildingSpec>();
        foreach (var raw in Buildings)
        {
            var spec = BuildingSpec.Parse(raw);
            if (spec != null)
                list.Add(spec);
        }
        return list;
    }

    public float AbundanceOf(string speciesName) =>
        Abundance.TryGetValue(speciesName, out var v) ? v.AsSingle() : 0f;
}
