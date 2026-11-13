using Godot;

namespace FishingSim.Data;

/// <summary>
/// The short length between the line and the hook. A thin leader is hard for the fish to see; a wire one cannot be
/// bitten through by a pike. The leader is part of the tackle's strength: the weakest of rod, line and leader decides.
/// </summary>
[GlobalClass]
public partial class LeaderData : TackleItem
{
    /// <summary>Breaking strength in kg (999 for "no leader").</summary>
    [Export] public float Strength { get; set; } = 999f;

    /// <summary>How visible the end tackle is to the fish (replaces the line's visibility; 0 = no leader, the line's own).</summary>
    [Export] public float Visibility { get; set; }

    /// <summary>0..1: how well it stands up to teeth (1 = a toothy fish never bites it through).</summary>
    [Export(PropertyHint.Range, "0,1")] public float BiteProof { get; set; }
}
