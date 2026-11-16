using Godot;
using FishingSim.Graphics;

namespace FishingSim.UI;

/// <summary>
/// The FPS counter from the graphics settings: a small label in the top-left corner over every screen (menu and game),
/// refreshed four times a second; the "FPS + szczegóły" step also opens the full performance panel (F11) in the game.
/// </summary>
public partial class FpsCounter : CanvasLayer
{
    private static FpsCounter? _instance;
    private Label _label = null!;
    private float _refresh;

    /// <summary>Shows, hides or creates the counter to match the setting.</summary>
    public static void Sync()
    {
        if (Renderer.IsHeadless || Engine.GetMainLoop() is not SceneTree tree)
            return;
        int mode = GraphicsQuality.FpsCounter.Value;
        if (mode > 0 && (_instance == null || !IsInstanceValid(_instance)))
        {
            _instance = new FpsCounter { Name = "FpsCounter", Layer = 120 };
            tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
        }
        if (_instance != null && IsInstanceValid(_instance))
            _instance.Visible = mode == 1;
        PerfOverlay.Current?.Show(mode >= 2);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _label = new Label { Position = new Vector2(10, 6), MouseFilter = Control.MouseFilterEnum.Ignore };
        _label.AddThemeFontSizeOverride("font_size", 16);
        _label.AddThemeColorOverride("font_color", new Color(0.95f, 0.97f, 0.9f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _label.AddThemeConstantOverride("outline_size", 5);
        AddChild(_label);
        Visible = GraphicsQuality.FpsCounter.Value == 1;
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        _refresh -= (float)delta;
        if (_refresh > 0f)
            return;
        _refresh = 0.25f;
        double fps = Engine.GetFramesPerSecond();
        _label.Text = $"{fps:0} FPS  ·  {(fps > 0 ? 1000.0 / fps : 0):0.0} ms";
    }
}
