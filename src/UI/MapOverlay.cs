using System;
using Godot;

namespace FishingSim.UI;

/// <summary>Transparent control drawn over the map image; the owner supplies the painting and reads the hover point.</summary>
public partial class MapOverlay : Control
{
    public Action<MapOverlay>? Painter { get; set; }

    /// <summary>Mouse position inside the overlay, or (-1, -1) when the mouse is elsewhere.</summary>
    public Vector2 Hover { get; private set; } = new(-1, -1);

    public bool HasHover => Hover.X >= 0f;

    public void SetHover(Vector2 p)
    {
        Hover = p;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
            SetHover(motion.Position);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit)
            SetHover(new Vector2(-1, -1));
    }

    public override void _Draw() => Painter?.Invoke(this);
}
