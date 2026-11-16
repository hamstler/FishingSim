using Godot;
using FishingSim.Core;
using FishingSim.Fishing;

namespace FishingSim.UI;

/// <summary>
/// The line's load bar at the bottom of the screen, shown whenever the rod is in hand: an empty, see-through bar with a
/// thin light outline. When a fish pulls, it fills from the left with the load on the tackle (tension over the weakest
/// link, <see cref="FishingRod.EffectiveLimit"/>): green while there is room, yellow, orange and red as it nears the
/// breaking point. While a cast is charged it shows the cast's power in gold instead.
/// </summary>
public partial class TensionGauge : Control
{
    /// <summary>Fill colour by load: green, yellow-green, yellow, orange, red (at the break).</summary>
    private static readonly Color[] Stops =
    {
        new(0.3f, 0.82f, 0.36f), new(0.66f, 0.84f, 0.3f), new(0.96f, 0.82f, 0.28f), new(0.96f, 0.5f, 0.16f), new(0.9f, 0.14f, 0.1f),
    };

    private static readonly Color PowerColour = new(1f, 0.82f, 0.4f);

    private float _fill;

    public FishingRod? Rod { get; set; }

    /// <summary>The cast's power 0..1 while charging (Hud sets it), or below 0 for the line's load.</summary>
    public float Power { get; set; } = -1f;

    /// <summary>Smoothed fill 0..1: the load on the tackle (1 = breaking), or the cast power (tests and tools).</summary>
    public float Needle => _fill;

    /// <summary>The fill colour for a load 0..1 (1 = the tackle breaks).</summary>
    public static Color LoadColour(float load)
    {
        float x = Mathf.Clamp(load, 0f, 1f) * (Stops.Length - 1);
        int i = Mathf.Min((int)x, Stops.Length - 2);
        return Stops[i].Lerp(Stops[i + 1], x - i);
    }

    public override void _Process(double delta)
    {
        if (Rod == null || !Visible)
            return;
        float target = Power >= 0f ? Power : Mathf.Clamp(Rod.Tension / Mathf.Max(Rod.EffectiveLimit, 0.01f), 0f, 1f);
        float rate = Power >= 0f ? 30f : 10f;
        float next = Mathf.Lerp(_fill, target, Mathf.Min((float)delta * rate, 1f));
        if (Mathf.Abs(next - _fill) > 0.0005f || Mathf.Abs(next - target) > 0.0005f)
        {
            _fill = next;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (Rod == null)
            return;
        var size = Size;
        // See-through body with a soft shadow line and a thin light outline.
        DrawRect(new Rect2(0, 0, size.X, size.Y), new Color(0f, 0f, 0f, 0.16f));
        if (_fill > 0.002f)
        {
            var col = Power >= 0f ? PowerColour : LoadColour(_fill);
            float w = _fill * (size.X - 2f);
            DrawRect(new Rect2(1, 1, w, size.Y - 2f), new Color(col, 0.82f));
            // A lighter top half gives the fill some body.
            DrawRect(new Rect2(1, 1, w, (size.Y - 2f) * 0.45f), new Color(1f, 1f, 1f, 0.14f));
        }
        DrawRect(new Rect2(-1, -1, size.X + 2, size.Y + 2), new Color(0f, 0f, 0f, 0.35f), false, 1f);
        DrawRect(new Rect2(0, 0, size.X, size.Y), new Color(1f, 0.97f, 0.9f, 0.7f), false, 1.2f);
    }
}
