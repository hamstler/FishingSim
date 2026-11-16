using Godot;

namespace FishingSim.Data;

[GlobalClass]
public partial class HookData : TackleItem
{
    /// <summary>The game's internal scale for the bite model (0.5 for #24 .. 10 for 4/0): bigger = bigger hook.</summary>
    [Export] public float Size { get; set; } = 5f;

    /// <summary>The real size: n &gt; 0 is #n (a bigger number is a smaller hook, #24 the smallest), n &lt; 0 is |n|/0
    /// (4/0 the largest). Written by tools/tackle/generate_hooks.py.</summary>
    [Export] public int RealSize { get; set; } = 10;

    /// <summary>Load (kg) the hook takes before it opens: part of the tackle's weakest link.</summary>
    [Export] public float Strength { get; set; } = 4f;

    /// <summary>Weight of the hook (grams).</summary>
    [Export] public float WeightG { get; set; } = 0.1f;

    /// <summary>"#12" or "2/0".</summary>
    public string Label => RealSize > 0 ? $"#{RealSize}" : $"{-RealSize}/0";
}
