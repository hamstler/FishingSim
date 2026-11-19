using FishingSim.Compat;

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

    /// <summary>The 3D model on the rod (PropAsset id under assets/models with its .json frame, e.g. "reels/small_hokuto";
    /// empty = RodRig.ModelReelId).</summary>
    [Export] public string Model { get; set; } = "";

    /// <summary>Housing colour of the reel.</summary>
    [Export] public Color BodyColor { get; set; } = new(0.2f, 0.21f, 0.23f);

    /// <summary>Trim (rotor ring, spool lip, drag knob) colour.</summary>
    [Export] public Color TrimColor { get; set; } = new(0.62f, 0.52f, 0.3f);
}
