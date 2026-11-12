using Godot;

namespace FishingSim.Data;

/// <summary>What a rod is made for (shop categories; a spinning rod also brings lure bites on faster).</summary>
public enum RodKind { Float, Spinning, Bottom, Carp }

[GlobalClass]
public partial class RodData : TackleItem
{
    [Export] public RodKind Kind { get; set; } = RodKind.Float;

    /// <summary>Multiplies cast speed (range).</summary>
    [Export] public float CastMult { get; set; } = 1f;

    /// <summary>Load (kg) above which the rod breaks.</summary>
    [Export] public float MaxLoad { get; set; } = 7f;

    /// <summary>How much of a sudden burst the rod absorbs (0..0.5).</summary>
    [Export(PropertyHint.Range, "0,0.5")] public float ShockAbsorb { get; set; } = 0.2f;

    /// <summary>How the blank bends: 0 = fast (only the tip), 1 = slow/parabolic (the whole rod works).</summary>
    [Export(PropertyHint.Range, "0,1")] public float Action { get; set; } = 0.5f;
}
