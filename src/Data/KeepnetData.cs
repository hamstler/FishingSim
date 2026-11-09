using Godot;
using FishingSim.Core.Gear;

namespace FishingSim.Data;

/// <summary>A keepnet (siatka). Limits how much fish you can keep before selling, and how fresh it stays.</summary>
[GlobalClass]
public partial class KeepnetData : TackleItem
{
    [Export] public int MaxFish { get; set; } = 15;
    [Export] public float MaxKg { get; set; } = 10f;

    /// <summary>Condition lost per in-game hour (fresh = 1). Bigger, knotless nets keep fish in better shape.</summary>
    [Export] public float ConditionLossPerHour { get; set; } = 0.04f;

    public KeepnetSpec Spec => new(MaxFish, MaxKg, ConditionLossPerHour);
}
