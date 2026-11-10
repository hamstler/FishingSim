using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Accounts;
using FishingSim.Data;
using FishingSim.Fishing;
using FishingSim.Progress;
using FishingSim.World;

namespace FishingSim.UI;

/// <summary>
/// The fish atlas: every species with a turning 3D model in its detailed skin, Latin name, description, sizes, price,
/// baits, depth, best time and the fishing grounds where it lives, plus the player's own catches when logged in.
/// Opened from the main menu and the pause menu; works while the game is paused.
/// </summary>
public partial class FishAtlas : Control
{
    private readonly List<FishSpecies> _species = SpeciesCatalog.Paths.Select(p => GD.Load<FishSpecies>(p)).Where(s => s != null).ToList();
    private readonly List<LocationData> _maps = LakeBed.LocationPaths.Select(p => GD.Load<LocationData>(p)).ToList();
    private VBoxContainer _list = null!;
    private LineEdit _search = null!;
    private OptionButton _mapFilter = null!;
    private Label _name = null!, _latin = null!, _facts = null!, _mine = null!;
    private RichTextLabel _description = null!;
    private SubViewport _viewport = null!;
    private Node3D _turntable = null!;
    private FishModel? _model;
    private Profile? _profile;
    private float _spin, _time;
    private bool _dragging;

    /// <summary>The species shown on the right.</summary>
    public FishSpecies? Selected { get; private set; }

    /// <summary>Species currently listed (after the search and map filter).</summary>
    public IReadOnlyList<FishSpecies> Listed => _listed;
    private readonly List<FishSpecies> _listed = new();

    public event Action? Closed;

    public void SetSearch(string text)
    {
        _search.Text = text;
        Refill();
    }

    public void SetMapFilter(int index)
    {
        _mapFilter.Select(index);
        Refill();
    }

    /// <summary>The facts shown for the selected species (for tests).</summary>
    public string FactsText => $"{_name.Text}\n{_latin.Text}\n{_description.Text}\n{_facts.Text}";

    /// <summary>The shown fish wears its detailed skin (it is painted in the background after a selection).</summary>
    public bool ModelReady => _model?.HasDetailedSkin ?? false;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Theme = UITheme.Make();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = new Color(0.05f, 0.035f, 0.02f, 0.94f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(shade);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 28);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);

        // Header: title, search, map filter, close.
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        root.AddChild(head);
        var title = new Label { Text = $"Atlas ryb  ·  {_species.Count} gatunków", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", UITheme.Accent);
        head.AddChild(title);
        _search = new LineEdit { PlaceholderText = "Szukaj gatunku...", CustomMinimumSize = new Vector2(240, 40) };
        _search.TextChanged += _ => Refill();
        head.AddChild(_search);
        _mapFilter = new OptionButton { CustomMinimumSize = new Vector2(220, 40) };
        _mapFilter.AddItem("Wszystkie łowiska");
        foreach (var m in _maps)
            _mapFilter.AddItem(m.DisplayName);
        _mapFilter.ItemSelected += _ => Refill();
        head.AddChild(_mapFilter);
        var close = new Button { Text = "Zamknij", CustomMinimumSize = new Vector2(140, 40) };
        close.Pressed += Close;
        head.AddChild(close);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 18);
        root.AddChild(body);

        // Left: the species list.
        var listPanel = new PanelContainer { CustomMinimumSize = new Vector2(330, 0) };
        body.AddChild(listPanel);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        listPanel.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_list);

        // Right: the model on a turntable and the facts.
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        body.AddChild(right);
        var stage = new SubViewportContainer
        {
            Stretch = true,
            CustomMinimumSize = new Vector2(0, 360),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        stage.GuiInput += OnStageInput;
        right.AddChild(stage);
        _viewport = new SubViewport
        {
            OwnWorld3D = true,
            TransparentBg = false,
            Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        stage.AddChild(_viewport);
        BuildStage();

        _name = new Label();
        _name.AddThemeFontSizeOverride("font_size", 30);
        _name.AddThemeColorOverride("font_color", UITheme.Accent);
        right.AddChild(_name);
        _latin = new Label { Modulate = new Color(1, 1, 1, 0.7f) };
        _latin.AddThemeFontSizeOverride("font_size", 17);
        right.AddChild(_latin);
        _description = new RichTextLabel { FitContent = true, BbcodeEnabled = false, ScrollActive = false, CustomMinimumSize = new Vector2(0, 50) };
        _description.AddThemeFontSizeOverride("normal_font_size", 17);
        right.AddChild(_description);
        _facts = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _facts.AddThemeFontSizeOverride("font_size", 16);
        right.AddChild(_facts);
        _mine = new Label { Modulate = new Color(1f, 0.88f, 0.55f) };
        _mine.AddThemeFontSizeOverride("font_size", 16);
        right.AddChild(_mine);

        if (Session.IsLoggedIn)
            _profile = Profile.LoadFor(Session.Username);
        Refill();
        if (_listed.Count > 0)
            Select(_listed[0]);
    }

    /// <summary>A lit little studio: soft key light, cool fill, a dark vignette backdrop.</summary>
    private void BuildStage()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.1f, 0.13f, 0.15f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.62f, 0.7f),
            AmbientLightEnergy = 0.6f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            GlowEnabled = true,
            GlowIntensity = 0.4f,
            SsaoEnabled = true,
        };
        _viewport.AddChild(new WorldEnvironment { Environment = env });
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 30, 0), LightEnergy = 1.6f, LightColor = new Color(1f, 0.95f, 0.86f), ShadowEnabled = true });
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-10, -140, 0), LightEnergy = 0.6f, LightColor = new Color(0.6f, 0.75f, 1f) });
        _viewport.AddChild(new OmniLight3D { Position = new Vector3(0, 0.4f, -1.2f), LightEnergy = 1.2f, OmniRange = 4f, LightColor = new Color(0.9f, 0.95f, 1f) });
        // A pale, rippled floor of sand catching the shadow.
        _viewport.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(40, 40) },
            Position = new Vector3(0, -0.32f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.3f, 0.25f), Roughness = 0.95f },
        });
        _viewport.AddChild(new Camera3D { Position = new Vector3(0, 0.12f, 1.25f), RotationDegrees = new Vector3(-5, 0, 0), Fov = 40f, Current = true });
        _turntable = new Node3D();
        _viewport.AddChild(_turntable);
    }

    private void OnStageInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } b)
            _dragging = b.Pressed;
        else if (e is InputEventMouseMotion m && _dragging)
            _spin += m.Relative.X * 0.01f;
    }

    private void Refill()
    {
        foreach (var c in _list.GetChildren())
            c.QueueFree();
        _listed.Clear();
        string q = _search.Text.Trim().ToLowerInvariant();
        LocationData? map = _mapFilter.Selected > 0 ? _maps[_mapFilter.Selected - 1] : null;
        foreach (var sp in _species.OrderBy(s => s.SpeciesName, StringComparer.Create(new System.Globalization.CultureInfo("pl-PL"), false)))
        {
            if (q != "" && !sp.SpeciesName.ToLowerInvariant().Contains(q) && !sp.LatinName.ToLowerInvariant().Contains(q))
                continue;
            if (map != null && map.AbundanceOf(sp.SpeciesName) <= 0f)
                continue;
            _listed.Add(sp);
            int caught = _profile?.CatchCounts.GetValueOrDefault(sp.SpeciesName) ?? 0;
            var button = new Button
            {
                Text = $"{sp.SpeciesName}{(caught > 0 ? "  ✓" : "")}",
                Icon = Graphics.Icons.Fish(sp),
                ExpandIcon = false,
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 52),
                ToggleMode = true,
                ButtonPressed = sp == Selected,
                TooltipText = sp.LatinName,
            };
            button.AddThemeConstantOverride("icon_max_width", 48);
            var species = sp;
            button.Pressed += () => Select(species);
            _list.AddChild(button);
        }
    }

    public void Select(FishSpecies sp)
    {
        Selected = sp;
        foreach (var b in _list.GetChildren().OfType<Button>())
            b.SetPressedNoSignal(b.TooltipText == sp.LatinName && b.Text.StartsWith(sp.SpeciesName));

        _model?.QueueFree();
        float typical = sp.MinWeight + (sp.MaxWeight - sp.MinWeight) * 0.35f;
        _model = FishModel.Create(sp, typical, detailed: true);
        _model.Wiggle = 0.35f;
        float scale = 1.0f / MathF.Max(_model.Length, 0.01f);
        _model.Scale = new Vector3(scale, scale, scale);
        _model.RotationDegrees = new Vector3(0, 90, 0);
        _turntable.AddChild(_model);

        _name.Text = sp.SpeciesName;
        _latin.Text = sp.LatinName;
        _description.Text = sp.Description;
        var homes = _maps.Where(m => m.AbundanceOf(sp.SpeciesName) > 0f).Select(m => m.DisplayName).ToList();
        _facts.Text =
            $"Waga: {Weight(sp.MinWeight)} - {Weight(sp.MaxWeight)}   ·   trofeum od {Weight(sp.TrophyThreshold)}   ·   wymiar ochronny {Weight(sp.LegalWeight)}\n"
            + $"Skup: {sp.PricePerKg:0} gr/kg   ·   siła w holu: {Stars(sp.PullFactor, 0.4f, 1.7f)}   ·   waleczność: {Stars(sp.Aggression, 0.3f, 0.9f)}\n"
            + $"{GameUI.SpeciesTips(sp)}\n"
            + $"Łowiska: {(homes.Count > 0 ? string.Join(", ", homes) : "-")}"
            + (Fishing.FishReference.For(sp) is { } reference ? $"\n{reference.Credit}" : "");
        if (_profile != null)
        {
            int n = _profile.CatchCounts.GetValueOrDefault(sp.SpeciesName);
            _mine.Text = n > 0
                ? $"Twoje połowy: {n}   ·   rekord {_profile.Records.GetValueOrDefault(sp.SpeciesName):0.00} kg"
                : "Jeszcze jej nie złowiłeś.";
        }
        else
        {
            _mine.Text = "";
        }
    }

    private static string Weight(float kg) => kg < 1f ? $"{kg * 1000f:0} g" : $"{kg:0.0} kg";

    private static string Stars(float v, float lo, float hi)
    {
        int n = Math.Clamp((int)MathF.Round(1f + 4f * (v - lo) / (hi - lo)), 1, 5);
        return new string('●', n) + new string('○', 5 - n);
    }

    public override void _Process(double delta)
    {
        if (!Visible || _turntable == null)
            return;
        // Mostly side-on (that is how anglers look at a fish), swaying gently; drag with the mouse to turn it.
        _time += (float)delta;
        _turntable.Rotation = new Vector3(0f, _spin + MathF.Sin(_time * 0.6f) * 0.45f, 0f);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Visible && e.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Close()
    {
        Closed?.Invoke();
        QueueFree();
    }
}
