using Godot;
using FishingSim.Core.Gear;

namespace FishingSim.Data;

/// <summary>A landing net (podbierak). Fish heavier than <see cref="MaxWeight"/> often slip out when landed.</summary>
[GlobalClass]
public partial class LandingNetData : TackleItem
{
    [Export] public float MaxWeight { get; set; } = 3f;

    /// <summary>Handle length (m): lets you land fish from a high bank or pier.</summary>
    [Export] public float Reach { get; set; } = 1.8f;

    public LandingNetSpec Spec => new(MaxWeight, Reach);
}
