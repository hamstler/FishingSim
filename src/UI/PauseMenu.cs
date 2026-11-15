using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Accounts;
using FishingSim.Fishing;
using FishingSim.Tournaments;

namespace FishingSim.UI;

/// <summary>Esc menu: resume, switch map, log out, quit. Pauses the game while open.</summary>
public partial class PauseMenu : CanvasLayer
{
    private FishingRod _rodFixed = null!;
    /// <summary>The rod in use (the active one of the rod set, Fishing/RodSet).</summary>
    private FishingRod _rod => FishingRod.ActiveRod ?? _rodFixed;
    private readonly List<CanvasLayer> _otherScreens = new();
    private Action _save = () => { };
    private TournamentManager _tournaments = null!;
    private Label _warning = null!;

    public void Setup(FishingRod rod, TournamentManager tournaments, Action save, params CanvasLayer[] otherScreens)
    {
        _rodFixed = rod;
        _tournaments = tournaments;
        _save = save;
        _otherScreens.AddRange(otherScreens);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 20;
        Visible = false;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var panel = new PanelContainer { Theme = UITheme.Make() };
        panel.AnchorLeft = panel.AnchorRight = panel.AnchorTop = panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = -190; panel.OffsetRight = 190; panel.OffsetTop = -230; panel.OffsetBottom = 230;
        AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 22);
        panel.AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        margin.AddChild(box);
        var title = new Label { Text = "Pauza", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(title);
        _warning = new Label
        {
            Text = "Turniej w toku: zmiana mapy lub wylogowanie oznacza utratę wpisowego.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        _warning.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.3f));
        box.AddChild(_warning);
        AddButton(box, "Wznów", Resume);
        AddButton(box, "Zmień mapę", ChangeMap);
        AddButton(box, "Atlas ryb", () => AddChild(new FishAtlas()));
        // Every graphics setting (effects, view distance, 3D resolution, window), each with arrows both ways.
        AddButton(box, "Ustawienia grafiki", () => AddChild(new GraphicsPanel()));
        // Every key with its action, rebindable; F1 in the game shows the same as a quick card.
        AddButton(box, "Sterowanie", () => AddChild(new ControlsPanel()));
        AddButton(box, "Wyloguj", Logout);
        AddButton(box, "Wyjdź z gry", Quit);
    }

    private static void AddButton(Container parent, string text, Action onPressed)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 46) };
        b.Pressed += onPressed;
        parent.AddChild(b);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            return;
        if (Visible)
        {
            Resume();
            GetViewport().SetInputAsHandled();
        }
        else if (!_otherScreens.Exists(s => s.Visible))
        {
            Open();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Open()
    {
        _warning.Visible = _tournaments.Active != null;
        Visible = true;
        _rod.InputBlocked = true;
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Resume()
    {
        Visible = false;
        _rod.InputBlocked = false;
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void ChangeMap()
    {
        _tournaments.Abandon();
        _save();
        Session.MapIndex = -1;
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile("res://menu.tscn");
    }

    private void Logout()
    {
        _tournaments.Abandon();
        _save();
        Session.Logout();
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile("res://menu.tscn");
    }

    private void Quit()
    {
        _save();
        GetTree().Quit();
    }
}
