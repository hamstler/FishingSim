using Godot;

namespace FishingSim.Data;

[GlobalClass]
public partial class LineData : TackleItem
{
    /// <summary>Breaking strength in kg.</summary>
    [Export] public float Strength { get; set; } = 7f;

    /// <summary>Above 1 the fish see the line better and bites come slower.</summary>
    [Export] public float Visibility { get; set; } = 1f;
}
