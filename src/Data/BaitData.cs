using Godot;

namespace FishingSim.Data;

/// <summary>Shop groups of bait. Lures are made by hand, are not eaten and only work while they move.</summary>
public enum BaitKind { Natural, Plant, Carp, Lure }

[GlobalClass]
public partial class BaitData : TackleItem
{
    [Export] public BaitKind Kind { get; set; } = BaitKind.Natural;

    /// <summary>Amount granted by the dev refill key.</summary>
    [Export] public int StartCount { get; set; } = 20;

    /// <summary>Pieces per purchased pack.</summary>
    [Export] public int PackSize { get; set; } = 10;

    /// <summary>
    /// The bait family the fish judge this one by, when a species has no entry for this bait itself (e.g. a hemp
    /// seed is judged as "corn", a brass spoon as "spoon"); see <see cref="FishSpecies.AffinityFor"/>.
    /// </summary>
    [Export] public string AffinityAs { get; set; } = "";

    /// <summary>Multiplies the family's affinity (a better bait of the family).</summary>
    [Export] public float AffinityScale { get; set; } = 1f;

    /// <summary>A lure: it is not used up by a bite, only lost with the hook when the line breaks.</summary>
    [Export] public bool Reusable { get; set; }

    /// <summary>The lure families species are given affinities for.</summary>
    public static readonly string[] LureFamilies = { "spinner", "spoon", "wobbler", "softbait", "fly" };
}
