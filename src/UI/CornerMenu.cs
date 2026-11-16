using System;
using Godot;
using FishingSim.Graphics;

namespace FishingSim.UI;

/// <summary>A small square button that draws its own glyph (a gear, an exit door) over the theme's button.</summary>
public partial class GlyphButton : Button
{
    public enum Glyph { Gear, Door }

    public Glyph Shape { get; set; }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(46, 46);
        FocusMode = FocusModeEnum.None;
        MouseEntered += QueueRedraw;
        MouseExited += QueueRedraw;
    }

    public override void _Draw()
    {
        var c = Size / 2f;
        var ink = IsHovered() ? new Color(1f, 0.86f, 0.5f) : new Color(0.93f, 0.9f, 0.84f);
        if (Shape == Glyph.Gear)
        {
            // Eight teeth round a ring, a hole in the middle.
            const float r = 11f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.Tau / 8f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var side = new Vector2(-dir.Y, dir.X);
                var p = new[]
                {
                    c + dir * (r - 1f) + side * 3.6f, c + dir * (r + 4.5f) + side * 2.6f,
                    c + dir * (r + 4.5f) - side * 2.6f, c + dir * (r - 1f) - side * 3.6f,
                };
                DrawColoredPolygon(p, ink);
            }
            DrawCircle(c, r, ink);
            DrawCircle(c, 4.5f, new Color(0.12f, 0.1f, 0.08f));
        }
        else
        {
            // An open door in its frame with an arrow leaving it.
            var frame = new Rect2(c.X - 13f, c.Y - 14f, 16f, 28f);
            DrawRect(frame, ink, false, 2.2f, true);
            DrawColoredPolygon(new[]
            {
                new Vector2(frame.Position.X, frame.Position.Y), new Vector2(frame.Position.X + 10f, frame.Position.Y + 4f),
                new Vector2(frame.Position.X + 10f, frame.End.Y + 3f), new Vector2(frame.Position.X, frame.End.Y),
            }, ink);
            DrawCircle(new Vector2(frame.Position.X + 7.5f, c.Y + 1f), 1.4f, new Color(0.12f, 0.1f, 0.08f));
            DrawLine(new Vector2(c.X + 1f, c.Y), new Vector2(c.X + 14f, c.Y), ink, 2.4f, true);
            DrawColoredPolygon(new[] { new Vector2(c.X + 16f, c.Y), new Vector2(c.X + 10f, c.Y - 5.5f), new Vector2(c.X + 10f, c.Y + 5.5f) }, ink);
        }
    }
}

/// <summary>
/// The main menu's corner widget (top right): a gear that opens the settings (graphics, controls) and a door that leaves
/// the game after a confirmation. Always on screen, whatever the window's height.
/// </summary>
public partial class CornerMenu : HBoxContainer
{
    /// <summary>Asked to open a settings screen over the menu: 0 graphics, 1 controls.</summary>
    public event Action<int>? OpenSettings;

    private PopupMenu _settings = null!;
    private ConfirmationDialog _quit = null!;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        var gear = new GlyphButton { Shape = GlyphButton.Glyph.Gear, TooltipText = "Ustawienia" };
        var door = new GlyphButton { Shape = GlyphButton.Glyph.Door, TooltipText = "Wyjdź z gry" };
        AddChild(gear);
        AddChild(door);

        _settings = new PopupMenu();
        _settings.AddItem("Ustawienia grafiki", 0);
        _settings.AddItem("Sterowanie", 1);
        _settings.AddThemeFontSizeOverride("font_size", 18);
        _settings.IdPressed += id => OpenSettings?.Invoke((int)id);
        AddChild(_settings);
        gear.Pressed += () =>
        {
            var at = gear.GetScreenTransform() * new Vector2(gear.Size.X, gear.Size.Y + 6f);
            _settings.ResetSize();
            _settings.Position = new Vector2I((int)(at.X - _settings.Size.X), (int)at.Y);
            _settings.Popup();
        };

        _quit = new ConfirmationDialog
        {
            Title = "Wyjście", DialogText = "Wyjść z gry?", OkButtonText = "Wyjdź", CancelButtonText = "Zostań",
        };
        _quit.Confirmed += () => GetTree().Quit();
        AddChild(_quit);
        door.Pressed += () => _quit.PopupCentered();
    }
}
