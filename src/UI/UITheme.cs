using Godot;

namespace FishingSim.UI;

/// <summary>One dark-blue look shared by the menu, shop, pause menu and the player screen.</summary>
public static class UITheme
{
    public static readonly Color Background = new(0.07f, 0.11f, 0.16f, 0.96f);
    public static readonly Color BackgroundLight = new(0.11f, 0.17f, 0.24f, 1f);
    public static readonly Color Border = new(0.22f, 0.36f, 0.50f);
    public static readonly Color Accent = new(0.35f, 0.68f, 0.90f);
    public static readonly Color Text = new(0.90f, 0.94f, 0.98f);

    private static Theme? _cached;

    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 8, int borderWidth = 1, int margin = 0)
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

        t.SetStylebox("panel", "PanelContainer", Box(Background, Border, 10));
        t.SetStylebox("panel", "Panel", Box(Background, Border, 10));

        t.SetStylebox("normal", "Button", Padded(Box(new Color(0.14f, 0.22f, 0.31f), Border, 6), 14, 7));
        t.SetStylebox("hover", "Button", Padded(Box(new Color(0.20f, 0.32f, 0.45f), Accent, 6), 14, 7));
        t.SetStylebox("pressed", "Button", Padded(Box(new Color(0.10f, 0.17f, 0.25f), Accent, 6), 14, 7));
        t.SetStylebox("disabled", "Button", Padded(Box(new Color(0.09f, 0.12f, 0.16f, 0.85f), new Color(0.16f, 0.22f, 0.28f), 6), 14, 7));
        t.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), Accent, 6, 2));
        t.SetColor("font_color", "Button", Text);
        t.SetColor("font_hover_color", "Button", Colors.White);
        t.SetColor("font_disabled_color", "Button", new Color(0.5f, 0.56f, 0.62f));

        t.SetStylebox("normal", "LineEdit", Padded(Box(new Color(0.04f, 0.07f, 0.10f), Border, 6), 10, 6));
        t.SetStylebox("focus", "LineEdit", Padded(Box(new Color(0.04f, 0.07f, 0.10f), Accent, 6, 2), 10, 6));
        t.SetColor("font_color", "LineEdit", Text);
        t.SetColor("font_placeholder_color", "LineEdit", new Color(0.5f, 0.56f, 0.62f));

        t.SetStylebox("tab_selected", "TabContainer", Padded(Box(BackgroundLight, Accent, 6), 14, 6));
        t.SetStylebox("tab_unselected", "TabContainer", Padded(Box(new Color(0.08f, 0.12f, 0.17f), Border, 6), 14, 6));
        t.SetStylebox("tab_hovered", "TabContainer", Padded(Box(new Color(0.14f, 0.22f, 0.31f), Border, 6), 14, 6));
        t.SetStylebox("panel", "TabContainer", Box(BackgroundLight, Border, 8, 1, 10));
        t.SetColor("font_selected_color", "TabContainer", Colors.White);
        t.SetColor("font_unselected_color", "TabContainer", new Color(0.65f, 0.72f, 0.8f));
        t.SetColor("font_hovered_color", "TabContainer", Colors.White);

        t.SetStylebox("background", "ProgressBar", Box(new Color(0.04f, 0.07f, 0.10f), Border, 5));
        t.SetStylebox("fill", "ProgressBar", Box(Accent, Accent, 5, 0));
        t.SetColor("font_color", "Label", Text);
        _cached = t;
        return t;
    }
}
