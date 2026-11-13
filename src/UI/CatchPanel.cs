using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Fishing;
using FishingSim.Progress;

namespace FishingSim.UI;

/// <summary>
/// Pops up when a fish is landed: the world dims and a carved card shows the catch on a little stage under a
/// spotlight, the fish turning slowly about its own axis (it spins in and settles, its fins and tail stirring), with
/// species, weight, length, value, XP, record and keepnet, any warning (undersized, keepnet full), and the choice:
/// into the keepnet (E) or back into the water (R).
/// </summary>
public partial class CatchPanel : Control
{
    private const float TurnSpeed = 0.42f;     // rad/s once settled: one turn in about 15 s
    private const float SpinIn = 5.5f;          // rad/s when the card appears

    private FishingRod _rod = null!;
    private Control _card = null!, _stage = null!;
    private ColorRect _dim = null!;
    private Label _title = null!, _latin = null!, _status = null!, _warning = null!;
    private HBoxContainer _chips = null!;
    private Button _keep = null!, _release = null!;
    private SubViewport _viewport = null!;
    private Node3D _turntable = null!;
    private FishModel? _fish;
    private bool _shown;
    private float _angle, _speed, _time;
    private Tween? _tween;

    /// <summary>The "keep" button (tests).</summary>
    public Button KeepButton => _keep;

    public Button ReleaseButton => _release;

    /// <summary>The fish on the stage, or null.</summary>
    public FishModel? ShownFish => _fish;

    public void Setup(FishingRod rod) => _rod = rod;

    public override void _Ready()
    {
        Theme = UITheme.Make();
        Visible = false;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 5;
        MouseFilter = MouseFilterEnum.Stop;

        _dim = new ColorRect { Color = new Color(0.02f, 0.03f, 0.04f, 0.45f), MouseFilter = MouseFilterEnum.Ignore };
        _dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_dim);

        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(860, 0) };
        centre.AddChild(card);
        _card = card;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        card.AddChild(box);

        var caught = new Label { Text = "ZŁOWIONO", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1f, 1f, 1f, 0.7f) };
        caught.AddThemeFontSizeOverride("font_size", 15);
        caught.AddThemeColorOverride("font_color", UITheme.Accent);
        box.AddChild(caught);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 34);
        _title.AddThemeColorOverride("font_color", UITheme.Accent);
        _title.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.6f));
        _title.AddThemeConstantOverride("shadow_offset_y", 2);
        box.AddChild(_title);
        _latin = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1f, 1f, 1f, 0.65f) };
        _latin.AddThemeFontSizeOverride("font_size", 16);
        box.AddChild(_latin);

        // The stage: a parchment frame, a spotlight behind, a soft shadow under the fish, the 3D fish over them.
        var frame = new PanelContainer();
        frame.AddThemeStyleboxOverride("panel", UiArt.Nine(UiArt.InsetTexture, 4, 4));
        box.AddChild(frame);
        var stage = new Control { ClipContents = true, CustomMinimumSize = new Vector2(0, 372) };
        frame.AddChild(stage);
        _stage = stage;
        var backdrop = new ColorRect { Color = new Color(0.08f, 0.1f, 0.11f) };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        stage.AddChild(backdrop);
        var light = new TextureRect
        {
            Texture = UiArt.Spotlight, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            Modulate = new Color(0.75f, 0.85f, 0.9f, 0.55f),
        };
        light.SetAnchorsPreset(LayoutPreset.FullRect);
        stage.AddChild(light);
        var shadow = new TextureRect
        {
            Texture = Shadow(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            AnchorLeft = 0.2f, AnchorRight = 0.8f, AnchorTop = 0.74f, AnchorBottom = 0.92f,
        };
        stage.AddChild(shadow);
        var view = new SubViewportContainer { Stretch = true };
        view.SetAnchorsPreset(LayoutPreset.FullRect);
        stage.AddChild(view);
        _viewport = new SubViewport
        {
            OwnWorld3D = true,
            TransparentBg = true,
            Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        view.AddChild(_viewport);
        BuildStudio();

        _chips = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _chips.AddThemeConstantOverride("separation", 10);
        box.AddChild(_chips);
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1f, 1f, 1f, 0.85f) };
        _status.AddThemeFontSizeOverride("font_size", 16);
        box.AddChild(_status);
        _warning = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _warning.AddThemeFontSizeOverride("font_size", 16);
        _warning.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.4f));
        box.AddChild(_warning);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 18);
        box.AddChild(row);
        _keep = new Button { Text = "Do siatki  [E]", CustomMinimumSize = new Vector2(230, 48) };
        UITheme.Primary(_keep);
        _keep.Pressed += () => _rod.KeepCatch();
        row.AddChild(_keep);
        _release = new Button { Text = "Do wody  [R]", CustomMinimumSize = new Vector2(230, 48) };
        _release.Pressed += () => _rod.ReleaseCatch();
        row.AddChild(_release);
    }

    /// <summary>A soft studio: warm key light from above in front, cool rim from behind, a gentle fill; no floor.</summary>
    private void BuildStudio()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.6f, 0.66f, 0.74f),
            AmbientLightEnergy = 0.55f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            GlowEnabled = true,
            GlowIntensity = 0.3f,
        };
        _viewport.AddChild(new WorldEnvironment { Environment = env });
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-48, 24, 0), LightEnergy = 1.7f, LightColor = new Color(1f, 0.94f, 0.84f) });
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-12, -160, 0), LightEnergy = 0.9f, LightColor = new Color(0.62f, 0.78f, 1f) });
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(20, 200, 0), LightEnergy = 0.35f, LightColor = new Color(0.9f, 0.85f, 0.8f) });
        // The fish is scaled to unit length; the camera keeps 1.3 units across in view, slightly from above.
        var cam = new Camera3D { Fov = 30f, KeepAspect = Camera3D.KeepAspectEnum.Width, Current = true };
        _viewport.AddChild(cam);
        cam.Position = new Vector3(0f, 0.32f, 0.65f / MathF.Tan(Mathf.DegToRad(15f)));
        cam.LookAt(new Vector3(0f, 0.0f, 0f));
        _turntable = new Node3D();
        _viewport.AddChild(_turntable);
    }

    /// <summary>A soft oval shadow (radial gradient).</summary>
    private static Texture2D Shadow()
    {
        var g = new Gradient();
        g.SetColor(0, new Color(0f, 0f, 0f, 0.55f));
        g.SetColor(1, new Color(0f, 0f, 0f, 0f));
        return new GradientTexture2D
        {
            Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
            Width = 256, Height = 64,
        };
    }

    public override void _Process(double delta)
    {
        if (_rod == null)
            return;
        bool show = _rod.AwaitingDecision && _rod.CurrentSpecies != null;
        if (show && !_shown)
            Open();
        else if (!show && _shown)
            Close();
        if (!_shown)
            return;
        float dt = (float)delta;
        _time += dt;
        // Spins in, then settles to a slow turn; a little rise and fall as if it hung in water.
        _speed = Mathf.Lerp(_speed, TurnSpeed, 1f - MathF.Exp(-dt * 1.6f));
        _angle += _speed * dt;
        _turntable.Rotation = new Vector3(MathF.Sin(_time * 0.9f) * 0.04f, _angle, 0f);
        _turntable.Position = new Vector3(0f, MathF.Sin(_time * 1.3f) * 0.012f, 0f);
        Adopt();
    }

    /// <summary>Puts the rod's landed fish on the stage (the rod made it; it frees it once the angler decides).</summary>
    private void Adopt()
    {
        var model = _rod.HeldFishModel;
        if (model == null || model == _fish || !IsInstanceValid(model))
            return;
        _fish = model;
        if (model.GetParent() != null)
            model.GetParent().RemoveChild(model);
        _turntable.AddChild(model);
        float scale = 1f / MathF.Max(model.Length, 0.01f);
        model.Transform = new Transform3D(Basis.FromEuler(new Vector3(0f, Mathf.DegToRad(90f), 0f)).Scaled(Vector3.One * scale), Vector3.Zero);
        model.Wiggle = 0.3f;
        model.Visible = true;
    }

    private void Open()
    {
        _shown = true;
        Fill();
        // The stage takes what the screen has left over the rest of the card (it fits 720p as well as 4K).
        float room = GetViewportRect().Size.Y - 470f;
        _stage.CustomMinimumSize = new Vector2(0f, Math.Clamp(room, 200f, 460f));
        _card.CustomMinimumSize = new Vector2(Math.Clamp(GetViewportRect().Size.X * 0.55f, 760f, 1100f), 0f);
        Visible = true;
        _angle = -0.9f;
        _speed = SpinIn;
        _time = 0f;
        _fish = null;
        Adopt();
        // A free cursor so the buttons can be clicked; the keys work either way.
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Visible;
        _tween?.Kill();
        _card.PivotOffset = _card.Size / 2f;
        _card.Scale = new Vector2(0.94f, 0.94f);
        _card.Modulate = new Color(1f, 1f, 1f, 0f);
        _dim.Modulate = new Color(1f, 1f, 1f, 0f);
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(_card, "scale", Vector2.One, 0.32f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_card, "modulate:a", 1f, 0.22f);
        _tween.TweenProperty(_dim, "modulate:a", 1f, 0.3f);
    }

    private void Close()
    {
        _shown = false;
        Visible = false;
        _fish = null;
        _tween?.Kill();
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void Fill()
    {
        var sp = _rod.CurrentSpecies!;
        var profile = _rod.Profile;
        float w = _rod.CurrentWeight;
        var result = _rod.LastCatch;
        _title.Text = sp.SpeciesName;
        _latin.Text = sp.LatinName;
        foreach (var c in _chips.GetChildren())
            c.QueueFree();
        Chip(null, "Waga", w < 1f ? $"{w * 1000f:0} g" : $"{w:0.00} kg");
        if (_rod.HeldFishModel is { } model)
            Chip(null, "Długość", $"{model.Length * 100f:0} cm");
        Chip(UiArt.Coin, "Skup", Lore.Money(result.Value));
        Chip(null, "Doświadczenie", $"+{result.Xp} XP");
        if (result.Record)
            Chip(UiArt.Star, "Rekord", "nowy!");
        float best = profile.Records.GetValueOrDefault(sp.SpeciesName);
        string record = result.Record ? "Nowy rekord gatunku!" : $"Twój rekord: {best:0.00} kg";
        string net = profile.KeepnetLimit is { } spec ? $"siatka {profile.Keepnet.Count}/{spec.MaxFish}" : $"siatka {profile.Keepnet.Count}";
        _status.Text = $"{record}   ·   {net}";
        bool undersized = w < sp.LegalWeight;
        bool fits = profile.KeepnetFits(w);
        _warning.Text = !fits
            ? "Siatka pełna albo za słaba na tę rybę: możesz ją tylko wypuścić."
            : undersized
                ? $"Niewymiarowa (wymiar ochronny {sp.LegalWeight:0.00} kg): zatrzymanie grozi karą strażnika."
                : "";
        _warning.Visible = _warning.Text != "";
        _keep.Disabled = !fits;
        _release.Text = $"Do wody  [R]  +{Profile.ReleaseXp} XP";
    }

    /// <summary>A small inset board with a caption above its value (and an icon).</summary>
    private void Chip(Texture2D? icon, string caption, string value)
    {
        var chip = new PanelContainer { CustomMinimumSize = new Vector2(140, 0) };
        chip.AddThemeStyleboxOverride("panel", UiArt.Nine(UiArt.InsetTexture, 4, 8));
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);
        chip.AddChild(col);
        var cap = new Label { Text = caption, HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1f, 1f, 1f, 0.6f) };
        cap.AddThemeFontSizeOverride("font_size", 13);
        col.AddChild(cap);
        var line = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        line.AddThemeConstantOverride("separation", 6);
        col.AddChild(line);
        if (icon != null)
            line.AddChild(new TextureRect
            {
                Texture = icon, CustomMinimumSize = new Vector2(20, 20), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
        var val = new Label { Text = value };
        val.AddThemeFontSizeOverride("font_size", 20);
        val.AddThemeColorOverride("font_color", UITheme.Text);
        line.AddChild(val);
        _chips.AddChild(chip);
    }
}
