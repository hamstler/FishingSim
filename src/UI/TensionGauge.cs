using Godot;
using FishingSim.Fishing;

namespace FishingSim.UI;

/// <summary>Line tension bar with the drag setting marked by a white line.</summary>
public partial class TensionGauge : Control
{
    public FishingRod? Rod { get; set; }

    public override void _Draw()
    {
        if (Rod == null)
            return;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.03f, 0.06f, 0.09f, 0.75f));
        float limit = Rod.EffectiveLimit;
        float ratio = Mathf.Clamp(Rod.Tension / limit, 0f, 1f);
        var color = new Color(0.3f, 0.8f, 0.4f).Lerp(new Color(0.95f, 0.25f, 0.2f), Mathf.Clamp((ratio - 0.5f) * 2f, 0f, 1f));
        DrawRect(new Rect2(2, 2, (Size.X - 4) * ratio, Size.Y - 4), color);
        float dx = (Size.X - 4) * (Rod.DragSetting * Rod.DragMax / limit) + 2;
        DrawLine(new Vector2(dx, 0), new Vector2(dx, Size.Y), Colors.White, 3f);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.4f, 0.55f, 0.7f), false, 1f);
        DrawString(ThemeDB.FallbackFont, new Vector2(0, Size.Y * 0.5f + 6f), $"{Rod.Tension:0.0} / {limit:0.0} kg",
            HorizontalAlignment.Center, Size.X, 16, Colors.White);
    }
}
