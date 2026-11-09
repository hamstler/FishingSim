using System;
using System.Linq;
using Godot;
using FishingSim.Fishing;

namespace FishingSim.World;

/// <summary>Finds the door the player is standing at and raises <see cref="Activated"/> when E is pressed there.</summary>
public partial class InteractionManager : Node
{
    private Player _player = null!;
    private Scenery _scenery = null!;
    private FishingRod _rod = null!;

    /// <summary>The spot in reach right now, or null.</summary>
    public Interactable? Current { get; private set; }

    public event Action<Interactable>? Activated;

    public void Setup(Player player, Scenery scenery, FishingRod rod)
    {
        _player = player;
        _scenery = scenery;
        _rod = rod;
    }

    /// <summary>The nearest interactable whose radius contains the point, or null.</summary>
    public static Interactable? Nearest(System.Collections.Generic.IEnumerable<Interactable> spots, Vector3 position) =>
        spots.Where(s => GodotObject.IsInstanceValid(s) && s.DistanceTo(position) <= s.Radius)
             .OrderBy(s => s.DistanceTo(position))
             .FirstOrDefault();

    public override void _Process(double delta)
    {
        bool free = !_rod.InputBlocked && _rod.State == RodState.Idle;
        Current = free ? Nearest(_scenery.Interactables, _player.GlobalPosition) : null;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.E } && Current != null)
        {
            Activated?.Invoke(Current);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Fires the event for a spot directly (tests and tools).</summary>
    public void Activate(Interactable spot) => Activated?.Invoke(spot);
}
