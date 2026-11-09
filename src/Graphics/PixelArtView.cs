using System;
using Godot;

namespace FishingSim.Graphics;

/// <summary>
/// Renders the 3D world at a low resolution (window size / <see cref="GraphicsQuality.PixelSize"/>) into a
/// SubViewport that shares the game's World3D, then shows it scaled up with nearest-neighbour filtering through
/// shaders/pixel_post.gdshader. The main viewport stops drawing 3D, so the HUD and menus stay sharp on top.
/// The pixel camera follows the player's camera through a <see cref="RemoteTransform3D"/>.
/// </summary>
public partial class PixelArtView : Node
{
    private readonly Camera3D _source;
    private SubViewport? _viewport;
    private Camera3D? _camera;
    private TextureRect? _rect;
    private ShaderMaterial? _post;

    public PixelArtView(Camera3D source) => _source = source;

    public PixelArtView() : this(null!) { }

    /// <summary>The low-resolution viewport the world is drawn into (null when headless).</summary>
    public SubViewport? Viewport => _viewport;

    public override void _Ready()
    {
        if (Renderer.IsHeadless || _source == null)
            return;
        var root = GetViewport();
        _viewport = new SubViewport
        {
            World3D = root.World3D,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
            Msaa3D = Godot.Viewport.Msaa.Disabled,
            ScreenSpaceAA = Godot.Viewport.ScreenSpaceAAEnum.Disabled,
            UseTaa = false,
            PositionalShadowAtlasSize = 2048,
        };
        AddChild(_viewport);
        _camera = new Camera3D { Current = true };
        _viewport.AddChild(_camera);
        var remote = new RemoteTransform3D { UpdateScale = false };
        _source.AddChild(remote);
        remote.RemotePath = remote.GetPathTo(_camera);

        var layer = new CanvasLayer { Layer = -1 };
        AddChild(layer);
        _post = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/pixel_post.gdshader") };
        _rect = new TextureRect
        {
            Texture = _viewport.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Material = _post,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_rect);

        root.Disable3D = true;
        root.SizeChanged += Resize;
        GraphicsQuality.Changed += Resize;
        Resize();
    }

    public override void _ExitTree()
    {
        if (_viewport == null)
            return;
        var root = GetViewport();
        root.Disable3D = false;
        root.SizeChanged -= Resize;
        GraphicsQuality.Changed -= Resize;
    }

    private void Resize()
    {
        if (_viewport == null || _post == null)
            return;
        var size = GetViewport().GetVisibleRect().Size;
        int px = GraphicsQuality.PixelSize;
        var low = new Vector2I(Math.Max((int)(size.X / px), 64), Math.Max((int)(size.Y / px), 36));
        _viewport.Size = low;
        _post.SetShaderParameter("src_size", new Vector2(low.X, low.Y));
        GraphicsQuality.ApplyViewport(_viewport);
        // Crisp pixels: no smoothing of edges in the low-resolution image.
        _viewport.Msaa3D = Godot.Viewport.Msaa.Disabled;
        _viewport.UseTaa = false;
        _viewport.ScreenSpaceAA = Godot.Viewport.ScreenSpaceAAEnum.Disabled;
    }

    public override void _Process(double delta)
    {
        if (_camera == null)
            return;
        _camera.Fov = _source.Fov;
        _camera.Near = _source.Near;
        _camera.Far = _source.Far;
    }
}
