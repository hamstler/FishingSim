using Godot;

namespace FishingSim.Data;

[GlobalClass]
public partial class ReelData : TackleItem
{
    /// <summary>Drag limit (kg) at a 100% setting.</summary>
    [Export] public float DragMax { get; set; } = 5f;

    /// <summary>Retrieve speed in m/s.</summary>
    [Export] public float ReelSpeed { get; set; } = 3f;

    /// <summary>Metres of line on the spool.</summary>
    [Export] public float LineCapacity { get; set; } = 100f;
}
