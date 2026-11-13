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

    // ------------------------------------------------------------------ lures (Kind = Lure)
    /// <summary>The model within the family: spinner round/long/tail/headed, spoon classic/narrow/wide, wobbler
    /// minnow/crank/jerk/popper, soft bait ripper/twister/worm/craw, fly dry/nymph/wet/streamer.</summary>
    [Export] public string Shape { get; set; } = "";

    /// <summary>The lure's main colour (the blade, the spoon, a wobbler's back, the soft body, a fly's body).</summary>
    [Export] public Color LureColor { get; set; } = new(0.86f, 0.87f, 0.9f);

    /// <summary>The second colour (dots, stripes, a wobbler's flanks and belly, a tail, a fly's hackle).</summary>
    [Export] public Color AccentColor { get; set; } = new(0.8f, 0.15f, 0.12f);

    /// <summary>How the two colours are laid out: plain, dots, stripes, bars, halves, glitter, redhead.</summary>
    [Export] public string Pattern { get; set; } = "plain";

    /// <summary>A polished metal finish (blades and spoons that flash), not paint.</summary>
    [Export] public bool Metallic { get; set; }

    /// <summary>Length in cm (0 = the family's usual size): bigger lures draw bigger fish and fewer bites.</summary>
    [Export] public float SizeCm { get; set; }

    /// <summary>Weight in grams (0 = the family's usual): heavier lures cast further and sink faster.</summary>
    [Export] public float WeightG { get; set; }

    /// <summary>Multiplies how fast it sinks (a heavier head drops faster).</summary>
    [Export] public float SinkScale { get; set; } = 1f;

    /// <summary>Added to the sink rate (m/s): a suspending or sinking wobbler, a nymph that goes down.</summary>
    [Export] public float SinkOffset { get; set; }

    /// <summary>Multiplies the running depth (a wobbler's lip: shallow 0.5, deep 2; 0 = stays at the surface).</summary>
    [Export] public float DepthScale { get; set; } = 1f;

    /// <summary>Added to the running depth (m): a nymph or a streamer that swims under the surface.</summary>
    [Export] public float DepthOffset { get; set; }

    /// <summary>The lure families species are given affinities for.</summary>
    public static readonly string[] LureFamilies = { "spinner", "spoon", "wobbler", "softbait", "fly" };
}
