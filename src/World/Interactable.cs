using Godot;

namespace FishingSim.World;

/// <summary>What happens when the player presses E next to an <see cref="Interactable"/>.</summary>
public enum InteractKind { Tackle, Bait, FishBuyer, General, Office, BusStop }

/// <summary>A spot in front of a door (or sign) where the player can press E.</summary>
public partial class Interactable : Node3D
{
    public InteractKind Kind { get; set; }
    public string Title { get; set; } = "";
    public float Radius { get; set; } = 3.6f;

    public string Prompt => Kind switch
    {
        InteractKind.Office => $"E - {Title} (zapisy na zawody)",
        InteractKind.BusStop => $"E - {Title} (zmiana łowiska)",
        InteractKind.FishBuyer => $"E - {Title} (sprzedaj ryby)",
        _ => $"E - {Title}",
    };

    /// <summary>Horizontal distance from a world point to this spot.</summary>
    public float DistanceTo(Vector3 p) => new Vector2(p.X - GlobalPosition.X, p.Z - GlobalPosition.Z).Length();
}
