using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Graphics;

namespace FishingSim.UI;

/// <summary>
/// The graphics settings screen (from the login form and the pause menu). Every row steps both ways with ◀ ▶ arrows
/// (the arrows stop at the ends instead of wrapping round); the 3D resolution has a slider. The preset row sets all
/// the effects at once and reads "Własne" once one of them is changed by hand. Changes apply at once and are saved.
/// </summary>
public partial class GraphicsPanel : Control
{
    private readonly List<Action> _refresh = new();
    private Label _hint = null!;

    public event Action? Closed;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Theme = UITheme.Make();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = new Color(0.05f, 0.035f, 0.02f, 0.86f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(shade);

        var panel = new PanelContainer();
        // Fixed width, the height follows the screen (720p shows a few rows less and scrolls).
        panel.AnchorLeft = 0.5f; panel.AnchorRight = 0.5f; panel.AnchorTop = 0.05f; panel.AnchorBottom = 0.95f;
        panel.OffsetLeft = -430; panel.OffsetRight = 430; panel.OffsetTop = 0; panel.OffsetBottom = 0;
        AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 22);
        panel.AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var title = new Label { Text = "Ustawienia grafiki", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", UITheme.Accent);
        root.AddChild(title);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(grid);

        // The preset: all effects at once.
        Row(grid, "Jakość (zestaw)", "Ustawia wszystkie efekty naraz. Każdą opcję poniżej można potem zmienić osobno.",
            () => GraphicsQuality.Custom ? $"Własne (od: {GraphicsQuality.Title(GraphicsQuality.Level)})" : GraphicsQuality.Title(GraphicsQuality.Level),
            () => (int)GraphicsQuality.Level, 4, i => GraphicsQuality.Set((QualityLevel)i));
        Section(grid, "Efekty");
        foreach (var st in GraphicsQuality.Settings)
        {
            var s = st;
            Row(grid, s.Name, s.Hint, () => s.Steps[s.Value], () => s.Value, s.Steps.Length, i => GraphicsQuality.SetStep(s, i));
        }
        Row(grid, "Zasięg widzenia", "Jak daleko widać las, rośliny, ludzi i cienie. Krótszy = płynniej na słabszym sprzęcie.",
            () => GraphicsQuality.ViewTitle(GraphicsQuality.View), () => (int)GraphicsQuality.View, 4, i => GraphicsQuality.SetView((ViewDistance)i));
        ScaleRow(grid);
        Section(grid, "Ekran");
        foreach (var st in GraphicsQuality.DisplaySettings)
        {
            var s = st;
            Row(grid, s.Name, s.Hint, () => s.Steps[s.Value], () => s.Value, s.Steps.Length, i => GraphicsQuality.SetStep(s, i));
        }

        _hint = new Label
        {
            Text = "Najedź na opcję, żeby zobaczyć opis.", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 44), Modulate = new Color(1, 1, 1, 0.75f),
        };
        root.AddChild(_hint);
        if (GraphicsQuality.PickedForIntegratedGpu)
            _hint.Text = "Wykryto zintegrowaną kartę graficzną: wybrano lekkie ustawienia.";
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 14);
        root.AddChild(buttons);
        var close = new Button { Text = "Gotowe", CustomMinimumSize = new Vector2(200, 44) };
        close.Pressed += Close;
        buttons.AddChild(close);
        close.CallDeferred(Control.MethodName.GrabFocus);
        RefreshAll();
    }

    private void Section(GridContainer grid, string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 20);
        label.AddThemeColorOverride("font_color", UITheme.Accent);
        grid.AddChild(label);
        for (int i = 0; i < 3; i++)
            grid.AddChild(new Control());
    }

    /// <summary>A row: the name, ◀, the value, ▶ (the arrows stop at the first and the last step).</summary>
    private void Row(GridContainer grid, string name, string hint, Func<string> value, Func<int> index, int count, Action<int> set)
    {
        var label = new Label { Text = name, SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = hint, MouseFilter = MouseFilterEnum.Pass };
        grid.AddChild(label);
        var left = new Button { Text = "◀", CustomMinimumSize = new Vector2(44, 38), TooltipText = hint };
        grid.AddChild(left);
        var shown = new Label { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(280, 0), TooltipText = hint, MouseFilter = MouseFilterEnum.Pass };
        grid.AddChild(shown);
        var right = new Button { Text = "▶", CustomMinimumSize = new Vector2(44, 38), TooltipText = hint };
        grid.AddChild(right);
        void Step(int d)
        {
            int next = Math.Clamp(index() + d, 0, count - 1);
            if (next == index() && !(name.StartsWith("Jakość") && GraphicsQuality.Custom))
                return;
            set(next);
            RefreshAll();
        }
        left.Pressed += () => Step(-1);
        right.Pressed += () => Step(1);
        foreach (Control c in new Control[] { label, left, shown, right })
            c.MouseEntered += () => _hint.Text = $"{name}: {hint}";
        _refresh.Add(() =>
        {
            int i = index();
            shown.Text = value();
            left.Disabled = i <= 0;
            right.Disabled = i >= count - 1;
        });
    }

    /// <summary>The 3D resolution: a slider from 50 to 100 % in steps of 5, with arrows for single steps.</summary>
    private void ScaleRow(GridContainer grid)
    {
        const string hint = "Rozdzielczość obrazu 3D (interfejs zostaje ostry). Poniżej 100% obraz jest skalowany przez FSR: największa ulga dla karty graficznej.";
        var label = new Label { Text = "Rozdzielczość 3D", TooltipText = hint, MouseFilter = MouseFilterEnum.Pass };
        grid.AddChild(label);
        var left = new Button { Text = "◀", CustomMinimumSize = new Vector2(44, 38) };
        grid.AddChild(left);
        var box = new HBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        box.AddThemeConstantOverride("separation", 8);
        grid.AddChild(box);
        var slider = new HSlider
        {
            MinValue = 50, MaxValue = 100, Step = 5, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            Value = GraphicsQuality.RenderScale, TooltipText = hint,
        };
        box.AddChild(slider);
        var shown = new Label { CustomMinimumSize = new Vector2(120, 0), HorizontalAlignment = HorizontalAlignment.Right };
        box.AddChild(shown);
        var right = new Button { Text = "▶", CustomMinimumSize = new Vector2(44, 38) };
        grid.AddChild(right);
        bool applying = false;
        slider.ValueChanged += v =>
        {
            if (applying)
                return;
            GraphicsQuality.SetRenderScale((int)v);
            RefreshAll();
        };
        left.Pressed += () => slider.Value = Math.Max(50, GraphicsQuality.RenderScale - 5);
        right.Pressed += () => slider.Value = Math.Min(100, GraphicsQuality.RenderScale + 5);
        foreach (Control c in new Control[] { label, left, slider, right })
            c.MouseEntered += () => _hint.Text = $"Rozdzielczość 3D: {hint}";
        _refresh.Add(() =>
        {
            applying = true;
            slider.Value = GraphicsQuality.RenderScale;
            applying = false;
            shown.Text = GraphicsQuality.RenderScaleTitle(GraphicsQuality.RenderScale);
            left.Disabled = GraphicsQuality.RenderScale <= 50;
            right.Disabled = GraphicsQuality.RenderScale >= 100;
        });
    }

    private void RefreshAll()
    {
        foreach (var r in _refresh)
            r();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
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
