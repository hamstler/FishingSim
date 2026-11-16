using Godot;

namespace FishingSim.UI;

/// <summary>One look shared by every screen: see-through graphite-teal panels, flat rounded buttons with an amber main
/// action, cool light text, amber accents (painted by <see cref="UiArt"/>).</summary>
public static class UITheme
{
    public static readonly Color Background = new(0.06f, 0.075f, 0.085f, 0.9f);
    public static readonly Color BackgroundLight = new(0.13f, 0.16f, 0.18f, 1f);
    public static readonly Color Border = new(0.36f, 0.42f, 0.46f);
    public static readonly Color Accent = new(0.96f, 0.72f, 0.32f);
    public static readonly Color Text = new(0.92f, 0.94f, 0.93f);

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

        // Glass panels with an amber line on top; quieter wells for lists and details.
        t.SetStylebox("panel", "PanelContainer", UiArt.Nine(UiArt.PanelTexture, 24, 22));
        t.SetStylebox("panel", "Panel", UiArt.Nine(UiArt.PanelTexture, 24, 22));

        // Flat rounded buttons: lighter with an amber rim on hover, darker when pressed.
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
            t.SetColor("font_disabled_color", type, new Color(0.55f, 0.58f, 0.6f));
            t.SetConstant("outline_size", type, 0);
        }

        var field = UiArt.Nine(UiArt.InsetTexture, 4);
        field.ContentMarginLeft = field.ContentMarginRight = 10;
        field.ContentMarginTop = field.ContentMarginBottom = 6;
        t.SetStylebox("normal", "LineEdit", field);
        var fieldFocus = (StyleBoxTexture)field.Duplicate();
        fieldFocus.ModulateColor = new Color(1.6f, 1.45f, 1.1f);
        t.SetStylebox("focus", "LineEdit", fieldFocus);
        t.SetColor("font_color", "LineEdit", Text);
        t.SetColor("font_placeholder_color", "LineEdit", new Color(0.55f, 0.6f, 0.62f));
        t.SetColor("caret_color", "LineEdit", Accent);

        // Tabs are small flat buttons; the open one is amber.
        t.SetStylebox("tab_selected", "TabContainer", UiArt.Button("gold", 14, 6));
        t.SetStylebox("tab_unselected", "TabContainer", UiArt.Button("normal", 14, 6));
        t.SetStylebox("tab_hovered", "TabContainer", UiArt.Button("hover", 14, 6));
        t.SetStylebox("tab_disabled", "TabContainer", UiArt.Button("disabled", 14, 6));
        t.SetStylebox("panel", "TabContainer", UiArt.Nine(UiArt.InsetTexture, 4, 10));
        t.SetColor("font_selected_color", "TabContainer", new Color(0.1f, 0.08f, 0.05f));
        t.SetColor("font_unselected_color", "TabContainer", new Color(0.78f, 0.82f, 0.84f));
        t.SetColor("font_hovered_color", "TabContainer", Colors.White);
        t.SetConstant("side_margin", "TabContainer", 4);

        // Amber bars in a dark groove.
        t.SetStylebox("background", "ProgressBar", UiArt.Nine(UiArt.BarBack, 3));
        t.SetStylebox("fill", "ProgressBar", UiArt.Nine(UiArt.BarFill, 3));

        // Slim scroll bars, amber when held.
        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            // The margins give the bar its width (without them it is drawn 0 px wide and lists seem cut off).
            t.SetStylebox("scroll", bar, Box(new Color(0f, 0f, 0f, 0.3f), new Color(0f, 0f, 0f, 0.3f), 4, 0, 4));
            t.SetStylebox("grabber", bar, Box(new Color(0.45f, 0.52f, 0.56f), new Color(0.45f, 0.52f, 0.56f), 4, 0, 4));
            t.SetStylebox("grabber_highlight", bar, Box(Accent, Accent, 4, 0, 4));
            t.SetStylebox("grabber_pressed", bar, Box(new Color(1f, 0.86f, 0.5f), new Color(1f, 0.86f, 0.5f), 4, 0, 4));
        }

        // Pop-up menus and dialogs: the same glass.
        t.SetStylebox("panel", "PopupMenu", UiArt.Nine(UiArt.PanelTexture, 24, 10));
        t.SetStylebox("hover", "PopupMenu", Box(new Color(0.96f, 0.72f, 0.32f, 0.22f), new Color(0.96f, 0.72f, 0.32f, 0.6f), 4, 1, 2));
        t.SetColor("font_color", "PopupMenu", Text);
        t.SetColor("font_hover_color", "PopupMenu", Colors.White);
        t.SetStylebox("panel", "AcceptDialog", UiArt.Nine(UiArt.PanelTexture, 24, 18));
        t.SetStylebox("embedded_border", "Window", UiArt.Nine(UiArt.PanelTexture, 24, 10));
        t.SetColor("title_color", "Window", Accent);
        t.SetStylebox("tooltip_panel", "TooltipPanel", Box(new Color(0.05f, 0.06f, 0.07f, 0.94f), new Color(1f, 1f, 1f, 0.16f), 4, 1, 6));
        t.SetColor("font_color", "TooltipLabel", Text);

        t.SetColor("font_color", "Label", Text);
        t.SetColor("font_color", "CheckBox", Text);
        t.SetColor("font_hover_color", "CheckBox", Colors.White);
        t.SetColor("font_pressed_color", "CheckBox", Accent);
        _cached = t;
        return t;
    }

    /// <summary>Makes a button the screen's main action: amber, with dark lettering.</summary>
    public static void Primary(Button b)
    {
        b.AddThemeStyleboxOverride("normal", UiArt.Button("gold"));
        b.AddThemeStyleboxOverride("hover", UiArt.Button("goldhover"));
        b.AddThemeStyleboxOverride("pressed", UiArt.Button("goldpressed"));
        b.AddThemeStyleboxOverride("hover_pressed", UiArt.Button("goldpressed"));
        b.AddThemeColorOverride("font_color", new Color(0.12f, 0.08f, 0.03f));
        b.AddThemeColorOverride("font_hover_color", new Color(0.06f, 0.04f, 0.01f));
        b.AddThemeColorOverride("font_pressed_color", new Color(0.06f, 0.04f, 0.01f));
        b.AddThemeColorOverride("icon_normal_color", Colors.White);
    }
}
