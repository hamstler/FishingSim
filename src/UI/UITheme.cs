using Godot;

namespace FishingSim.UI;

/// <summary>One medieval look (dark oak panels, brass trim, parchment text, square pixel corners) shared by every screen.</summary>
public static class UITheme
{
    public static readonly Color Background = new(0.12f, 0.08f, 0.05f, 0.88f);
    public static readonly Color BackgroundLight = new(0.21f, 0.14f, 0.09f, 1f);
    public static readonly Color Border = new(0.55f, 0.40f, 0.20f);
    public static readonly Color Accent = new(0.93f, 0.72f, 0.30f);
    public static readonly Color Text = new(0.96f, 0.90f, 0.76f);

    private static Theme? _cached;

    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 0, int borderWidth = 1, int margin = 0)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border ?? Border };
        sb.SetBorderWidthAll(borderWidth);
        sb.SetCornerRadiusAll(radius);
        if (margin > 0)
        {
            sb.ContentMarginLeft = sb.ContentMarginRight = sb.ContentMarginTop = sb.ContentMarginBottom = margin;
        }
        return sb;
    }

    private static StyleBoxFlat Padded(StyleBoxFlat sb, float horizontal, float vertical)
    {
        sb.ContentMarginLeft = sb.ContentMarginRight = horizontal;
        sb.ContentMarginTop = sb.ContentMarginBottom = vertical;
        return sb;
    }

    public static Theme Make()
    {
        if (_cached != null)
            return _cached;
        var t = new Theme { DefaultFontSize = 17 };

        // Carved oak panels with brass studs; inner boards for lists and details.
        t.SetStylebox("panel", "PanelContainer", UiArt.Nine(UiArt.PanelTexture, 24, 22, tile: true));
        t.SetStylebox("panel", "Panel", UiArt.Nine(UiArt.PanelTexture, 24, 22, tile: true));

        // Planked buttons: lighter on hover with a gilt rim, pressed into the board.
        foreach (var type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            t.SetStylebox("normal", type, UiArt.Button("normal"));
            t.SetStylebox("hover", type, UiArt.Button("hover"));
            t.SetStylebox("pressed", type, UiArt.Button("pressed"));
            t.SetStylebox("hover_pressed", type, UiArt.Button("pressed"));
            t.SetStylebox("disabled", type, UiArt.Button("disabled"));
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, Accent);
            t.SetColor("font_disabled_color", type, new Color(0.58f, 0.52f, 0.42f));
            t.SetConstant("outline_size", type, 0);
        }

        var field = UiArt.Nine(UiArt.InsetTexture, 4);
        field.ContentMarginLeft = field.ContentMarginRight = 10;
        field.ContentMarginTop = field.ContentMarginBottom = 6;
        t.SetStylebox("normal", "LineEdit", field);
        var fieldFocus = (StyleBoxTexture)field.Duplicate();
        fieldFocus.ModulateColor = new Color(1.25f, 1.15f, 0.95f);
        t.SetStylebox("focus", "LineEdit", fieldFocus);
        t.SetColor("font_color", "LineEdit", Text);
        t.SetColor("font_placeholder_color", "LineEdit", new Color(0.58f, 0.52f, 0.42f));
        t.SetColor("caret_color", "LineEdit", Accent);

        // Tabs are small planks; the open one is gilded.
        t.SetStylebox("tab_selected", "TabContainer", UiArt.Button("gold", 14, 6));
        t.SetStylebox("tab_unselected", "TabContainer", UiArt.Button("normal", 14, 6));
        t.SetStylebox("tab_hovered", "TabContainer", UiArt.Button("hover", 14, 6));
        t.SetStylebox("tab_disabled", "TabContainer", UiArt.Button("disabled", 14, 6));
        t.SetStylebox("panel", "TabContainer", UiArt.Nine(UiArt.InsetTexture, 4, 10));
        t.SetColor("font_selected_color", "TabContainer", new Color(0.18f, 0.1f, 0.04f));
        t.SetColor("font_unselected_color", "TabContainer", new Color(0.86f, 0.76f, 0.58f));
        t.SetColor("font_hovered_color", "TabContainer", Colors.White);
        t.SetConstant("side_margin", "TabContainer", 4);

        // Gilded bars in a dark groove.
        t.SetStylebox("background", "ProgressBar", UiArt.Nine(UiArt.BarBack, 3));
        t.SetStylebox("fill", "ProgressBar", UiArt.Nine(UiArt.BarFill, 3));

        // Slim brass scroll bars.
        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            // The margins give the bar its width (without them it is drawn 0 px wide and lists seem cut off).
            t.SetStylebox("scroll", bar, Box(new Color(0.07f, 0.05f, 0.03f, 0.6f), new Color(0.07f, 0.05f, 0.03f, 0.6f), 4, 0, 4));
            t.SetStylebox("grabber", bar, Box(new Color(0.62f, 0.46f, 0.22f), new Color(0.62f, 0.46f, 0.22f), 4, 0, 4));
            t.SetStylebox("grabber_highlight", bar, Box(Accent, Accent, 4, 0, 4));
            t.SetStylebox("grabber_pressed", bar, Box(new Color(1f, 0.86f, 0.5f), new Color(1f, 0.86f, 0.5f), 4, 0, 4));
        }

        t.SetColor("font_color", "Label", Text);
        t.SetColor("font_color", "CheckBox", Text);
        t.SetColor("font_hover_color", "CheckBox", Colors.White);
        t.SetColor("font_pressed_color", "CheckBox", Accent);
        _cached = t;
        return t;
    }

    /// <summary>Makes a button the screen's main action: gilded, with dark lettering.</summary>
    public static void Primary(Button b)
    {
        b.AddThemeStyleboxOverride("normal", UiArt.Button("gold"));
        b.AddThemeStyleboxOverride("hover", UiArt.Button("goldhover"));
        b.AddThemeStyleboxOverride("pressed", UiArt.Button("goldpressed"));
        b.AddThemeStyleboxOverride("hover_pressed", UiArt.Button("goldpressed"));
        b.AddThemeColorOverride("font_color", new Color(0.2f, 0.11f, 0.04f));
        b.AddThemeColorOverride("font_hover_color", new Color(0.12f, 0.06f, 0.02f));
        b.AddThemeColorOverride("font_pressed_color", new Color(0.12f, 0.06f, 0.02f));
        b.AddThemeColorOverride("icon_normal_color", Colors.White);
    }
}
