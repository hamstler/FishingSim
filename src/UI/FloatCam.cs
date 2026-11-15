using Godot;
using FishingSim.Core.Methods;
using FishingSim.Fishing;
using FishingSim.Graphics;

namespace FishingSim.UI;

/// <summary>
/// "Podgląd spławika": a small window over the HUD with a close view of the float, shown while the rod in the hands
/// fishes with a float in the water (the setting can turn it off). A second camera in the same world, a metre and a half
/// in front of the float on the angler's side, looks at it from just above the water, so every dip, lift and slide
/// of a bite is plain to see even at forty metres.
/// </summary>
public partial class FloatCam : PanelContainer
{
    private SubViewport _view = null!;
    private Camera3D _camera = null!;
    private Label _caption = null!;
    private Vector3 _smoothed;
    private bool _placed;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", UITheme.Box(new Color(0.03f, 0.03f, 0.03f, 0.75f), new Color(1f, 0.84f, 0.45f, 0.65f), 6, 2, 4));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        AddChild(box);
        var container = new SubViewportContainer { Stretch = true, CustomMinimumSize = new Vector2(336, 200), MouseFilter = MouseFilterEnum.Ignore };
        box.AddChild(container);
        _view = new SubViewport
        {
            Size = new Vector2I(504, 300), Msaa3D = Viewport.Msaa.Disabled, ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa, UseTaa = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenParentVisible,
        };
        container.AddChild(_view);
        _camera = new Camera3D { Fov = 32f, Near = 0.05f, Far = 400f, Current = true };
        _view.AddChild(_camera);
        _caption = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _caption.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(_caption);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var rod = FishingRod.ActiveRod;
        bool show = GraphicsQuality.FloatPreview.Value > 0 && rod != null && rod.InHands && !rod.OnRest
            && rod.CastMethod == FishingMethod.Float && RodSet.InWater(rod);
        if (Visible != show)
        {
            Visible = show;
            _placed = false;
        }
        if (!show || rod == null)
            return;
        var bob = rod.Bobber.GlobalPosition;
        var eye = GetViewport().GetCamera3D()?.GlobalPosition ?? bob + Vector3.Back * 10f;
        var toAngler = eye - bob;
        toAngler.Y = 0f;
        toAngler = toAngler.LengthSquared() > 1e-4f ? toAngler.Normalized() : Vector3.Back;
        // The camera sits on the angler's side of the float, low over the water; it eases after the drifting float
        // so the view does not shake with every ripple.
        var target = bob + toAngler * 1.6f + Vector3.Up * 0.38f;
        _smoothed = _placed ? _smoothed.Lerp(target, 1f - Mathf.Exp(-(float)delta * 4f)) : target;
        _placed = true;
        _camera.GlobalPosition = _smoothed;
        _camera.LookAt(bob + Vector3.Up * 0.04f, Vector3.Up);
        float distance = eye.DistanceTo(bob);
        string text = $"Spławik · {distance:0} m";
        if (_caption.Text != text)
            _caption.Text = text;
    }
}
