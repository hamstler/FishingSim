using System;
using System.Collections.Generic;
using Godot;

namespace FishingSim.UI;

/// <summary>
/// The controls screen (main menu, map selection and pause menu): every key in <see cref="Bindings"/> by group with a
/// button that waits for the new key (Esc cancels; a key used by another action swaps with it), the fixed mouse controls
/// below, and a reset to the defaults. Saved at once (user://controls.cfg).
/// </summary>
public partial class ControlsPanel : Control
{
    private readonly Dictionary<Act, Button> _buttons = new();
    private Act? _waiting;
    private Label _hint = null!;

    public event Action? Closed;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Theme = UITheme.Make();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = new Color(0.02f, 0.03f, 0.04f, 0.82f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(shade);

        var panel = new PanelContainer();
        panel.AnchorLeft = 0.5f; panel.AnchorRight = 0.5f; panel.AnchorTop = 0.05f; panel.AnchorBottom = 0.95f;
        panel.OffsetLeft = -430; panel.OffsetRight = 430;
        AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 22);
        panel.AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var title = new Label { Text = "Sterowanie", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", UITheme.Accent);
        root.AddChild(title);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(grid);

        string group = "";
        foreach (var entry in Bindings.All)
        {
            if (entry.Group != group)
            {
                group = entry.Group;
                Section(grid, group);
            }
            var e = entry;
            grid.AddChild(new Label { Text = e.Title, SizeFlagsHorizontal = SizeFlags.ExpandFill });
            var b = new Button { CustomMinimumSize = new Vector2(220, 38), TooltipText = "Kliknij i naciśnij nowy klawisz (Esc anuluje)" };
            b.Pressed += () => Wait(e.Act);
            grid.AddChild(b);
            _buttons[e.Act] = b;
        }
        Section(grid, "Mysz i stałe klawisze");
        foreach (var (what, how) in Bindings.Fixed)
        {
            grid.AddChild(new Label { Text = what, SizeFlagsHorizontal = SizeFlags.ExpandFill });
            grid.AddChild(new Label { Text = how, HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, 0.8f) });
        }

        _hint = new Label
        {
            Text = $"Kliknij klawisz, żeby go zmienić. {Bindings.Name(Act.QuickHelp)} w grze pokazuje szybką instrukcję.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.75f),
        };
        root.AddChild(_hint);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 14);
        root.AddChild(buttons);
        var reset = new Button { Text = "Przywróć domyślne", CustomMinimumSize = new Vector2(220, 44) };
        reset.Pressed += () =>
        {
            Bindings.ResetAll();
            _waiting = null;
            Refresh();
        };
        buttons.AddChild(reset);
        var close = new Button { Text = "Gotowe", CustomMinimumSize = new Vector2(200, 44) };
        close.Pressed += Close;
        buttons.AddChild(close);
        close.CallDeferred(Control.MethodName.GrabFocus);
        Refresh();
    }

    private static void Section(GridContainer grid, string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 20);
        label.AddThemeColorOverride("font_color", UITheme.Accent);
        grid.AddChild(label);
        grid.AddChild(new Control());
    }

    private void Wait(Act act)
    {
        _waiting = act;
        Refresh();
        _hint.Text = $"Naciśnij nowy klawisz: {Bindings.EntryOf(act).Title} (Esc anuluje).";
    }

    private void Refresh()
    {
        foreach (var (act, b) in _buttons)
            b.Text = _waiting == act ? "naciśnij klawisz…" : Bindings.Name(act);
    }

    public override void _Input(InputEvent @event)
    {
        if (_waiting is not { } act || @event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        GetViewport().SetInputAsHandled();
        _waiting = null;
        if (key.Keycode == Key.Escape)
            _hint.Text = "Anulowano.";
        else if (!Bindings.Rebind(act, key.Keycode))
            _hint.Text = $"Klawisz {Bindings.KeyName(key.Keycode)} jest zarezerwowany (Esc, 1-9, F2-F10).";
        else
            _hint.Text = $"{Bindings.EntryOf(act).Title}: {Bindings.Name(act)}";
        Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_waiting == null && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Close()
    {
        Closed?.Invoke();
        QueueFree();
    }
}
