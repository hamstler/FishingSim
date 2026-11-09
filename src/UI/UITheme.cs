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

        t.SetStylebox("panel", "PanelContainer", Box(Background, Border, 0));
        t.SetStylebox("panel", "Panel", Box(Background, Border, 0));

        t.SetStylebox("normal", "Button", Padded(Box(new Color(0.30f, 0.20f, 0.12f), Border, 0), 14, 7));
        t.SetStylebox("hover", "Button", Padded(Box(new Color(0.42f, 0.28f, 0.15f), Accent, 0), 14, 7));
        t.SetStylebox("pressed", "Button", Padded(Box(new Color(0.22f, 0.14f, 0.08f), Accent, 0), 14, 7));
        t.SetStylebox("disabled", "Button", Padded(Box(new Color(0.16f, 0.12f, 0.09f, 0.85f), new Color(0.30f, 0.24f, 0.18f), 0), 14, 7));
        t.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), Accent, 0, 1));
        t.SetColor("font_color", "Button", Text);
        t.SetColor("font_hover_color", "Button", Colors.White);
        t.SetColor("font_disabled_color", "Button", new Color(0.58f, 0.52f, 0.42f));

        t.SetStylebox("normal", "LineEdit", Padded(Box(new Color(0.09f, 0.06f, 0.04f), Border, 0), 10, 6));
        t.SetStylebox("focus", "LineEdit", Padded(Box(new Color(0.09f, 0.06f, 0.04f), Accent, 0, 1), 10, 6));
        t.SetColor("font_color", "LineEdit", Text);
        t.SetColor("font_placeholder_color", "LineEdit", new Color(0.58f, 0.52f, 0.42f));

        t.SetStylebox("tab_selected", "TabContainer", Padded(Box(new Color(0, 0, 0, 0), Accent, 0, 0), 14, 6));
        ((StyleBoxFlat)t.GetStylebox("tab_selected", "TabContainer")).BorderWidthBottom = 2;
        t.SetStylebox("tab_unselected", "TabContainer", Padded(Box(new Color(0, 0, 0, 0), Border, 0, 0), 14, 6));
        t.SetStylebox("tab_hovered", "TabContainer", Padded(Box(new Color(1, 1, 1, 0.05f), Border, 0, 0), 14, 6));
        t.SetStylebox("panel", "TabContainer", Box(new Color(0, 0, 0, 0.15f), new Color(Border, 0.4f), 0, 1, 10));
        t.SetColor("font_selected_color", "TabContainer", Colors.White);
        t.SetColor("font_unselected_color", "TabContainer", new Color(0.78f, 0.68f, 0.52f));
        t.SetColor("font_hovered_color", "TabContainer", Colors.White);

        t.SetStylebox("background", "ProgressBar", Box(new Color(0.09f, 0.06f, 0.04f), Border, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Accent, Accent, 0, 0));
        t.SetColor("font_color", "Label", Text);
        _cached = t;
        return t;
    }
}
