using Godot;

namespace FishingSim.World;

/// <summary>A labelled point on the map screen (shops and other places the player can visit).</summary>
public sealed record MapMarker(Vector2 Position, string Label, Color Color);
