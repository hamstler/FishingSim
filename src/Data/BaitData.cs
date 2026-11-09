using Godot;

namespace FishingSim.Data;

[GlobalClass]
public partial class BaitData : TackleItem
{
    /// <summary>Amount granted by the dev refill key.</summary>
    [Export] public int StartCount { get; set; } = 20;

    /// <summary>Pieces per purchased pack.</summary>
    [Export] public int PackSize { get; set; } = 10;
}
