using Godot;
using FishingSim.Fishing;

namespace FishingSim.UI;

/// <summary>Thin line-tension bar; the gold tick marks the drag setting.</summary>
public partial class TensionGauge : Control
{
    public FishingRod? Rod { get; set; }

    public override void _Draw()
    {
        if (Rod == null)
            return;
        // A thin bar: fill = tension, tick = where the drag starts to give line.
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.05f, 0.03f, 0.02f, 0.55f));
        float limit = Rod.EffectiveLimit;
        float ratio = Mathf.Clamp(Rod.Tension / limit, 0f, 1f);
        var color = new Color(0.95f, 0.88f, 0.7f).Lerp(new Color(0.95f, 0.25f, 0.15f), Mathf.Clamp((ratio - 0.55f) * 2.2f, 0f, 1f));
        DrawRect(new Rect2(0, 0, Size.X * ratio, Size.Y), color);
        float dx = Mathf.Clamp(Size.X * (Rod.DragSetting * Rod.DragMax / limit), 0f, Size.X - 2f);
        DrawRect(new Rect2(dx, -3, 2, Size.Y + 6), new Color(1f, 0.84f, 0.45f));
    }
}
