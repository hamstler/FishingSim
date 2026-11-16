using Godot;
using FishingSim.Core.Methods;
using FishingSim.Fishing;

namespace FishingSim.UI;

/// <summary>
/// The drag dial in the middle of the screen while the drag is being turned (mouse wheel): a ring filled clockwise from
/// the top from 0 to 100 %, coloured by <see cref="TackleCheck"/> (green good, yellow at the limit or too loose, red
/// stronger than the tackle), with a red tick where the drag would hold as much as the weakest link takes. In the middle:
/// the setting and the hold in kilograms. It fades out a moment after the last turn.
/// </summary>
public partial class DragRing : Control
{
    private const float Radius = 54f, Thickness = 9f, ShowSeconds = 1.4f;

    private float _shown, _alpha, _last = -1f;

    public FishingRod? Rod { get; set; }

    /// <summary>The ring is on screen (tests).</summary>
    public bool Showing => _alpha > 0.01f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Modulate = new Color(1, 1, 1, 0);
    }

    public override void _Process(double delta)
    {
        if (Rod == null)
            return;
        float dt = (float)delta;
        float setting = Rod.DragSetting;
        if (_last >= 0f && Mathf.Abs(setting - _last) > 0.001f && !Rod.Holstered)
            _shown = ShowSeconds;
        _last = setting;
        _shown -= dt;
        float target = _shown > 0f ? 1f : 0f;
        float a = Mathf.MoveToward(_alpha, target, dt * (target > 0f ? 8f : 2.5f));
        if (a != _alpha || _shown > 0f)
        {
            _alpha = a;
            Modulate = new Color(1, 1, 1, a);
            Visible = a > 0.01f;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (Rod == null)
            return;
        var c = Size / 2f;
        float setting = Rod.DragSetting;
        float kg = setting * Rod.DragMax;
        var verdict = TackleCheck.Judge(kg, Rod.LineStrength, Rod.RodMaxLoad);
        var col = verdict switch
        {
            TackleVerdict.TooTight => new Color(0.92f, 0.2f, 0.14f),
            TackleVerdict.Tight or TackleVerdict.Loose => new Color(0.97f, 0.78f, 0.26f),
            _ => new Color(0.36f, 0.84f, 0.4f),
        };
        const float start = -Mathf.Pi / 2f;
        // Dark disc behind, the empty track, then the filled arc.
        DrawCircle(c, Radius + Thickness, new Color(0f, 0f, 0f, 0.28f));
        DrawArc(c, Radius, 0f, Mathf.Tau, 72, new Color(1f, 0.97f, 0.9f, 0.22f), Thickness, true);
        if (setting > 0.001f)
            DrawArc(c, Radius, start, start + Mathf.Tau * setting, Mathf.Max(4, (int)(72 * setting)), col, Thickness, true);
        DrawArc(c, Radius + Thickness / 2f + 1f, 0f, Mathf.Tau, 72, new Color(1f, 0.97f, 0.9f, 0.55f), 1.2f, true);
        // Where the drag holds as much as the tackle takes.
        float br = TackleCheck.BreakSetting(Rod.DragMax, Rod.LineStrength, Rod.RodMaxLoad);
        if (br < 0.999f)
        {
            float ang = start + Mathf.Tau * br;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            DrawLine(c + dir * (Radius - Thickness), c + dir * (Radius + Thickness), new Color(0.95f, 0.16f, 0.12f), 3f, true);
        }
        var font = GetThemeDefaultFont();
        DrawCentred(font, c + new Vector2(0, -16), "Hamulec", 14, new Color(1f, 0.95f, 0.85f, 0.85f));
        DrawCentred(font, c + new Vector2(0, 10), $"{Mathf.RoundToInt(setting * 100f)}%", 26, Colors.White);
        DrawCentred(font, c + new Vector2(0, 30), $"{kg:0.0} kg", 14, col);
    }

    private void DrawCentred(Font font, Vector2 at, string text, int size, Color colour)
    {
        var w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var pos = at + new Vector2(-w / 2f, size * 0.35f);
        DrawStringOutline(font, pos, text, HorizontalAlignment.Left, -1, size, 4, new Color(0f, 0f, 0f, 0.7f));
        DrawString(font, pos, text, HorizontalAlignment.Left, -1, size, colour);
    }
}
