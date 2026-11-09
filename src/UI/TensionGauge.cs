using Godot;
using FishingSim.Core;
using FishingSim.Fishing;

namespace FishingSim.UI;

/// <summary>
/// The fight bar in the middle of the screen: four equal segments for 0-10%, 10-25%, 25-50% and 50-100% of the
/// tackle's strength in reserve, painted as one gradient from red (left, about to break) to green (right, slack).
/// A needle shows the current reserve; the ideal spot is the light orange middle. See <see cref="FightGauge"/>.
/// </summary>
public partial class TensionGauge : Control
{
    private static readonly Color[] Stops =
    {
        new(0.86f, 0.14f, 0.1f), new(0.95f, 0.45f, 0.12f), new(0.98f, 0.72f, 0.3f), new(0.62f, 0.8f, 0.26f), new(0.22f, 0.74f, 0.3f),
    };
    private static readonly string[] Labels = { "0-10%", "10-25%", "25-50%", "50-100%" };

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

    private static Color At(float x)
    {
        float f = Mathf.Clamp(x, 0f, 1f) * (Stops.Length - 1);
        int i = Mathf.Min((int)f, Stops.Length - 2);
        return Stops[i].Lerp(Stops[i + 1], f - i);
    }

    public override void _Draw()
    {
        if (Rod == null)
            return;
        var size = Size;
        float barH = size.Y * 0.55f;
        var font = ThemeDB.FallbackFont;
        // Gradient bar, drawn per segment so the joints are crisp.
        DrawRect(new Rect2(-2, -2, size.X + 4, barH + 4), new Color(0.08f, 0.05f, 0.03f, 0.85f));
        for (int s = 0; s < 4; s++)
        {
            float x0 = size.X * s / 4f, x1 = size.X * (s + 1) / 4f;
            var pts = new[] { new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(x1, barH), new Vector2(x0, barH) };
            var cols = new[] { At(x0 / size.X), At(x1 / size.X), At(x1 / size.X), At(x0 / size.X) };
            DrawPolygon(pts, cols);
            if (s > 0)
                DrawRect(new Rect2(x0 - 1, 0, 2, barH), new Color(0.08f, 0.05f, 0.03f));
            DrawString(font, new Vector2(x0, barH + 16), Labels[s], HorizontalAlignment.Center, x1 - x0, 13, new Color(0.97f, 0.92f, 0.8f, 0.85f));
        }
        // The ideal spot in the middle.
        DrawRect(new Rect2(size.X * 0.5f - 1, -6, 2, 4), new Color(1f, 1f, 1f, 0.8f));
        // Needle.
        float nx = _needle * size.X;
        DrawRect(new Rect2(nx - 2, -6, 4, barH + 12), new Color(0.06f, 0.04f, 0.02f));
        DrawRect(new Rect2(nx - 1, -5, 2, barH + 10), Colors.White);
        DrawColoredPolygon(new[] { new Vector2(nx - 7, -12), new Vector2(nx + 7, -12), new Vector2(nx, -4) }, Colors.White);
    }
}
