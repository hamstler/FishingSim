using Godot;

namespace FishingSim.Data;

/// <summary>Twisted line (horsehair, gut, silk) stretches; a braid hardly does.</summary>
public enum LineKind { Mono, Braid }

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
}
