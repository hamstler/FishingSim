using FishingSim.Compat;

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

    /// <summary>Lacquer of the blank (the maker's colour).</summary>
    [Export] public Color BlankColor { get; set; } = new(0.24f, 0.12f, 0.09f);

    /// <summary>Thread of the guide whippings.</summary>
    [Export] public Color WhipColor { get; set; } = new(0.75f, 0.62f, 0.3f);

    /// <summary>Length butt to tip (m); 0 = RodRig's default (3.58 m). Longer rods cast further (CastMult).</summary>
    [Export] public float Length { get; set; }

    /// <summary>The 3D model in the angler's hands (PropAsset id under assets/models, e.g. "rods/carp_ostoja";
    /// empty = RodRig.ModelRodId).</summary>
    [Export] public string Model { get; set; } = "";

    /// <summary>Grips of EVA foam (dark) instead of cork.</summary>
    [Export] public bool FoamGrip { get; set; }

    /// <summary>The model's finish: its pictures multiplied by this colour (white = as modelled), so series on the same
    /// model look apart.</summary>
    [Export] public Color ModelTint { get; set; } = new(1f, 1f, 1f);
}
