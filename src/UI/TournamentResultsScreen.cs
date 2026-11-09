using System.Collections.Generic;
using Godot;
using FishingSim.Fishing;
using FishingSim.Progress;
using FishingSim.Tournaments;

namespace FishingSim.UI;

/// <summary>Modal results window shown when a tournament ends.</summary>
public partial class TournamentResultsScreen : CanvasLayer
{
    private FishingRod _rod = null!;
    private TournamentManager _manager = null!;
    private VBoxContainer _box = null!;

    public void Setup(FishingRod rod, TournamentManager manager)
    {
        _rod = rod;
        _manager = manager;
    }

    public override void _Ready()
    {
        Layer = 15;
        Visible = false;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);
        var panel = new PanelContainer { Theme = UITheme.Make() };
        panel.AnchorLeft = panel.AnchorRight = panel.AnchorTop = panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = -300; panel.OffsetRight = 300; panel.OffsetTop = -290; panel.OffsetBottom = 290;
        AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 22);
        panel.AddChild(margin);
        _box = new VBoxContainer();
        _box.AddThemeConstantOverride("separation", 6);
        margin.AddChild(_box);
        _manager.Finished += Show;
    }

    public override void _ExitTree()
    {
        if (_manager != null)
            _manager.Finished -= Show;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape or Key.Enter })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Show(TournamentRecord result, IReadOnlyList<Standing> standings)
    {
        foreach (var c in _box.GetChildren())
            c.QueueFree();
        var title = new Label { Text = $"Wyniki: {result.Name}", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 26);
        _box.AddChild(title);
        string headline = result.Rank == 1 ? "ZWYCIĘSTWO!" : result.Rank <= 3 ? $"Miejsce na podium: {result.Rank}." : $"Zająłeś {result.Rank}. miejsce z {result.Entrants}.";
        var rank = new Label { Text = headline, HorizontalAlignment = HorizontalAlignment.Center };
        rank.AddThemeFontSizeOverride("font_size", 22);
        rank.AddThemeColorOverride("font_color", result.Rank <= 3 ? new Color(1f, 0.85f, 0.3f) : Colors.White);
        _box.AddChild(rank);
        _box.AddChild(new Label
        {
            Text = result.PrizeMoney > 0 ? $"Nagroda: {result.PrizeMoney} zł i {result.PrizeXp} XP" : "Bez nagrody w tym razie. Spróbuj ponownie!",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.7f, 0.95f, 0.7f),
        });
        _box.AddChild(new HSeparator());
        _box.AddChild(StandingsGrid.Create(standings));
        var close = new Button { Text = "Zamknij", CustomMinimumSize = new Vector2(0, 42) };
        close.Pressed += Close;
        _box.AddChild(close);

        Visible = true;
        _rod.InputBlocked = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Close()
    {
        Visible = false;
        _rod.InputBlocked = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }
}
