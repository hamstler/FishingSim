using Godot;

namespace FishingSim.Data;

[GlobalClass]
public partial class HookData : TackleItem
{
    /// <summary>1..10, bigger number = bigger hook.</summary>
    [Export] public float Size { get; set; } = 5f;
}
