using Godot;

namespace FishingSim.Data;

/// <summary>
/// A monofilament stretches and floats a little; a braid hardly stretches, is thin for its strength and floats;
/// a fluorocarbon is nearly invisible in water and sinks.
/// </summary>
public enum LineKind { Mono, Braid, Fluoro }

[GlobalClass]
public partial class LineData : TackleItem
{
    [Export] public LineKind Kind { get; set; } = LineKind.Mono;

    /// <summary>Breaking strength in kg.</summary>
    [Export] public float Strength { get; set; } = 7f;

    /// <summary>Above 1 the fish see the line better and bites come slower.</summary>
    [Export] public float Visibility { get; set; } = 1f;

    /// <summary>How much the line gives (0..1): a stretchy line soaks up part of a fish's surge, a braid passes it on.</summary>
    [Export(PropertyHint.Range, "0,1")] public float Stretch { get; set; } = 0.5f;

    /// <summary>Diameter in mm: thicker casts shorter and is drawn a little thicker.</summary>
    [Export] public float Diameter { get; set; } = 0.25f;

    /// <summary>The line's colour (drawn in the world and on the spool's icon).</summary>
    [Export] public Color LineColor { get; set; } = new(0.9f, 0.9f, 0.86f);
}
