using Godot;

namespace FishingSim.World;

/// <summary>What happens when the player presses E next to an <see cref="Interactable"/>.</summary>
public enum InteractKind { Tackle, Bait, FishBuyer, Market, General, Office, BusStop, Campfire, Forage, Smokehouse, NoticeBoard }

/// <summary>A spot in front of a door (or sign) where the player can press E.</summary>
public partial class Interactable : Node3D
{
    public InteractKind Kind { get; set; }
    public string Title { get; set; } = "";
    public float Radius { get; set; } = 3.6f;

    /// <summary>The thing behind the spot (a campfire), when it is not a building.</summary>
    public Node? Target { get; set; }

    public string Prompt => Kind switch
    {
        InteractKind.Office => $"E - {Title} (zapisy na zawody)",
        InteractKind.BusStop => $"E - {Title} (podróż na inne łowisko)",
        InteractKind.FishBuyer => $"E - {Title} (sprzedaj ryby)",
        InteractKind.Market => $"E - {Title} (ceny dnia, premia za trofea)",
        InteractKind.Campfire => $"E - {Title} (rozpal, gotuj ryby)",
        InteractKind.Forage => $"E - zbierz: {Title}",
        InteractKind.Smokehouse => $"E - {Title} (uwędź ryby z siatki)",
        InteractKind.NoticeBoard => $"E - {Title} (zlecenia mieszkańców)",
        _ => $"E - {Title}",
    };

    /// <summary>False while there is nothing to do here (a picked mushroom spot).</summary>
    public bool Active => Target is not Props.ForagePatch { Ripe: false };

    /// <summary>Horizontal distance from a world point to this spot.</summary>
    public float DistanceTo(Vector3 p) => new Vector2(p.X - GlobalPosition.X, p.Z - GlobalPosition.Z).Length();
}
