using Godot;
using FishingSim.Core;
using FishingSim.Fishing;

namespace FishingSim.UI;

/// <summary>
/// The fight bar under the fishing status line: one gradient from red (left, about to break) to green (right, slack)
/// over four segments 7%, 18%, 25% and 50% wide (0-10%, 10-25%, 25-50%, 50-100% of the tackle's strength in reserve),
/// with no labels or dividers. A needle shows the current reserve; the ideal spot is the light orange middle.
/// See <see cref="FightGauge"/>.
/// </summary>
public partial class TensionGauge : Control
{
    /// <summary>Colour at each segment edge: red, orange, light orange (middle), yellow-green, green.</summary>
    private static readonly Color[] Stops =
    {
        new(0.86f, 0.14f, 0.1f), new(0.93f, 0.36f, 0.12f), new(0.97f, 0.62f, 0.24f), new(0.95f, 0.8f, 0.4f), new(0.22f, 0.74f, 0.3f),
    };

    private float _needle = 0.75f;

    public FishingRod? Rod { get; set; }

    /// <summary>Smoothed needle position 0..1 (for tests and tools).</summary>
    public float Needle => _needle;

    public override void _Process(double delta)
    {
        if (Rod == null)
            return;
        float target = FightGauge.Position(FightGauge.Reserve(Rod.Tension, Rod.EffectiveLimit));
        _needle = Mathf.Lerp(_needle, target, Mathf.Min((float)delta * 10f, 1f));
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Rod == null)
            return;
        var size = Size;
        float barH = size.Y;
        DrawRect(new Rect2(-2, -2, size.X + 4, barH + 4), new Color(0.08f, 0.05f, 0.03f, 0.85f));
        // One continuous gradient: each segment blends from the colour at its left edge to the one at its right.
        for (int s = 0; s < FightGauge.Widths.Length; s++)
        {
            float x0 = size.X * FightGauge.SegmentStart(s), x1 = size.X * FightGauge.SegmentStart(s + 1);
            var pts = new[] { new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(x1, barH), new Vector2(x0, barH) };
            var cols = new[] { Stops[s], Stops[s + 1], Stops[s + 1], Stops[s] };
            DrawPolygon(pts, cols);
        }
        // Needle.
        float nx = _needle * size.X;
        DrawRect(new Rect2(nx - 2, -5, 4, barH + 10), new Color(0.06f, 0.04f, 0.02f));
        DrawRect(new Rect2(nx - 1, -4, 2, barH + 8), Colors.White);
    }
}
