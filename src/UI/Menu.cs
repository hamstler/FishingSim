using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FishingSim.Graphics;
using FishingSim.Accounts;
using FishingSim.Data;
using FishingSim.Progress;
using FishingSim.World;

namespace FishingSim.UI;

/// <summary>Start screen: login (admin / admin) -> map selection -> game.</summary>
public partial class Menu : Control
{
    private const string MainScene = "res://main.tscn";

    private readonly List<LocationData> _locations = new();
    private readonly Dictionary<int, ImageTexture> _previews = new();
    private Control _loginBox = null!;
    private VBoxContainer _mapsBox = null!;
    private LineEdit _user = null!, _pass = null!;
    private Label _error = null!, _loading = null!;
    private Button _loginButton = null!;

    public bool LoginVisible => _loginBox.Visible;
    public bool MapsVisible => _mapsBox.Visible;
    public string ErrorText => _error.Text;
    public bool LoginLocked => _loginButton.Disabled;
    public IEnumerable<Button> MapButtons => _mapsBox.FindChildren("*", "Button", true, false).OfType<Button>();

    public override void _Ready()
    {
        Theme = UITheme.Make();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().Paused = false;
        UserStore.EnsureDefault();
        foreach (var path in LakeBed.LocationPaths)
            _locations.Add(GD.Load<LocationData>(path));
        BuildBackground();
        BuildLogin();
        _mapsBox = new VBoxContainer();
        AddChild(_mapsBox);
        _loading = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _loading.AddThemeFontSizeOverride("font_size", 30);
        _loading.SetAnchorsPreset(LayoutPreset.CenterBottom);
        _loading.AnchorLeft = 0f;
        _loading.AnchorRight = 1f;
        _loading.OffsetTop = -80f;
        _loading.OffsetBottom = -30f;
        AddChild(_loading);
        if (Session.IsLoggedIn)
            ShowMaps();
        else
            ShowLogin();
    }

    // ------------------------------------------------------------------ layout
    private void BuildBackground()
    {
        // A slideshow of painted postcards of the fishing grounds, cross-fading every few seconds.
        foreach (var path in LakeBed.LocationPaths)
        {
            var loc = GD.Load<LocationData>(path);
            if (loc == null)
                continue;
            _slides.Add(Postcard.For(loc, 10f));
            _slides.Add(Postcard.For(loc, 18.5f));
        }
        for (int i = 0; i < 2; i++)
        {
            var layer = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                TextureFilter = TextureFilterEnum.Nearest,
                Texture = _slides.Count > 0 ? _slides[0] : null,
                Modulate = new Color(1, 1, 1, i == 0 ? 1f : 0f),
            };
            layer.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(layer);
            _slideLayers[i] = layer;
        }
        // Darken the top and bottom a little so the title and the panels read well over the picture.
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.05f, 0.03f, 0.02f, 0.55f));
        gradient.AddPoint(0.35f, new Color(0f, 0f, 0f, 0f));
        gradient.SetColor(gradient.GetPointCount() - 1, new Color(0.05f, 0.03f, 0.02f, 0.7f));
        var shade = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = gradient, FillFrom = Vector2.Zero, FillTo = new Vector2(0, 1), Width = 4, Height = 256 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(shade);

        var title = new Label { Text = "Fishing Sim", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 64);
        title.AddThemeColorOverride("font_color", new Color(0.98f, 0.84f, 0.5f));
        title.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.07f, 0.03f));
        title.AddThemeConstantOverride("outline_size", 8);
        title.SetAnchorsPreset(LayoutPreset.TopWide);
        title.OffsetTop = 30f;
        title.OffsetBottom = 120f;
        AddChild(title);
        _subtitle = new Label { Text = "średniowieczny symulator wędkarski", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, 0.6f) };
        _subtitle.AddThemeFontSizeOverride("font_size", 18);
        _subtitle.SetAnchorsPreset(LayoutPreset.TopWide);
        _subtitle.OffsetTop = 112f;
        _subtitle.OffsetBottom = 140f;
        AddChild(_subtitle);
    }

    private readonly List<ImageTexture> _slides = new();
    private readonly TextureRect[] _slideLayers = new TextureRect[2];
    private float _slideTime;
    private int _slide;

    public override void _Process(double delta)
    {
        if (_slides.Count < 2 || _slideLayers[1] == null)
            return;
        _slideTime += (float)delta;
        const float hold = 6f, fade = 1.5f;
        if (_slideTime > hold)
        {
            float k = Math.Clamp((_slideTime - hold) / fade, 0f, 1f);
            _slideLayers[1].Texture = _slides[(_slide + 1) % _slides.Count];
            _slideLayers[1].Modulate = new Color(1, 1, 1, k);
            if (k >= 1f)
            {
                _slide = (_slide + 1) % _slides.Count;
                _slideLayers[0].Texture = _slides[_slide];
                _slideLayers[1].Modulate = new Color(1, 1, 1, 0f);
                _slideTime = 0f;
            }
        }
    }

    private static void Center(Control box, float width, float height)
    {
        box.AnchorLeft = box.AnchorRight = box.AnchorTop = box.AnchorBottom = 0.5f;
        box.OffsetLeft = -width / 2f;
        box.OffsetRight = width / 2f;
        box.OffsetTop = -height / 2f + 40f;
        box.OffsetBottom = height / 2f + 40f;
    }

    private void BuildLogin()
    {
        var panel = new PanelContainer();
        Center(panel, 420, 460);
        AddChild(panel);
        _loginBox = panel;
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 24);
        panel.AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        margin.AddChild(box);

        // The golden carp of the fishmongers' sign, as an emblem over the form.
        var carp = GD.Load<FishSpecies>("res://data/fish/karp.tres");
        var emblem = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        emblem.AddThemeConstantOverride("separation", 10);
        box.AddChild(emblem);
        foreach (var id in new[] { "okon", "karp", "pstrag" })
        {
            var sp = GD.Load<FishSpecies>($"res://data/fish/{id}.tres");
            emblem.AddChild(new TextureRect
            {
                Texture = sp != null ? Icons.Fish(sp) : null,
                CustomMinimumSize = id == "karp" ? new Vector2(150, 112) : new Vector2(96, 72),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = TextureFilterEnum.Nearest,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            });
        }
        _ = carp;
        var head = new Label { Text = "Logowanie", HorizontalAlignment = HorizontalAlignment.Center };
        head.AddThemeFontSizeOverride("font_size", 28);
        head.AddThemeColorOverride("font_color", new Color(1f, 0.84f, 0.45f));
        box.AddChild(head);
        _user = new LineEdit { PlaceholderText = "Login", CustomMinimumSize = new Vector2(0, 40) };
        _user.TextSubmitted += text => _pass.GrabFocus();
        box.AddChild(_user);
        _pass = new LineEdit { PlaceholderText = "Hasło", Secret = true, CustomMinimumSize = new Vector2(0, 40) };
        _pass.TextSubmitted += text => { _ = TryLogin(); };
        box.AddChild(_pass);
        _loginButton = new Button { Text = "Zaloguj", CustomMinimumSize = new Vector2(0, 44) };
        _loginButton.Pressed += () => { _ = TryLogin(); };
        box.AddChild(_loginButton);
        _error = new Label();
        _error.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.4f));
        box.AddChild(_error);
        box.AddChild(new Label { Text = "Domyślne konto: admin / admin", Modulate = new Color(1, 1, 1, 0.55f) });
    }

    private void ShowLogin()
    {
        _loginBox.Visible = true;
        _mapsBox.Visible = false;
        _user.GrabFocus();
    }

    // ------------------------------------------------------------------ login
    /// <summary>Fills the login form (used by tests and tools).</summary>
    public void FillCredentials(string user, string password)
    {
        _user.Text = user;
        _pass.Text = password;
    }

    public async Task TryLogin()
    {
        if (_loginButton.Disabled)
            return;
        string user = _user.Text.Trim();
        if (UserStore.Verify(user, _pass.Text))
        {
            Session.Username = user.ToLowerInvariant();
            Session.MapIndex = -1;
            _pass.Text = "";
            _error.Text = "";
            ShowMaps();
            return;
        }
        _error.Text = "Nieprawidłowy login lub hasło";
        _pass.Text = "";
        _loginButton.Disabled = true;
        await ToSignal(GetTree().CreateTimer(1.0), Godot.Timer.SignalName.Timeout);
        if (IsInstanceValid(_loginButton))
            _loginButton.Disabled = false;
    }

    // ------------------------------------------------------------------ map selection
    private Label? _subtitle;

    private void ShowMaps()
    {
        _loginBox.Visible = false;
        if (_subtitle != null)
            _subtitle.Visible = false;
        foreach (var c in _mapsBox.GetChildren())
            c.QueueFree();
        _mapsBox.Visible = true;
        Center(_mapsBox, 1240, 560);
        _mapsBox.AddThemeConstantOverride("separation", 14);

        var profile = Profile.LoadFor(Session.Username);
        var head = new Label
        {
            Text = $"Witaj, {Session.Username}  |  poziom {profile.Level}  |  {profile.Money} gr\nWybierz łowisko",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        head.AddThemeFontSizeOverride("font_size", 22);
        _mapsBox.AddChild(head);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        _mapsBox.AddChild(row);
        foreach (int i in Enumerable.Range(0, _locations.Count).OrderBy(i => _locations[i].RequiredLevel))
            row.AddChild(MapCard(i, profile));

        var logout = new Button { Text = "Wyloguj", CustomMinimumSize = new Vector2(160, 40), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        logout.Pressed += Logout;
        _mapsBox.AddChild(logout);
    }

    private Control MapCard(int index, Profile profile)
    {
        var loc = _locations[index];
        bool locked = profile.Level < loc.RequiredLevel;
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(292, 0) };
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 10);
        panel.AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);

        if (!_previews.ContainsKey(index))
            _previews[index] = Postcard.For(loc, 10f);
        box.AddChild(new TextureRect
        {
            Texture = _previews[index],
            CustomMinimumSize = new Vector2(270, 152),
            TextureFilter = TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            Modulate = locked ? new Color(0.45f, 0.45f, 0.45f) : Colors.White,
        });
        var name = new Label { Text = loc.DisplayName };
        name.AddThemeFontSizeOverride("font_size", 24);
        box.AddChild(name);
        box.AddChild(new Label
        {
            Text = loc.Description,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(270, 66),
            Modulate = new Color(1, 1, 1, 0.8f),
        });
        // The most common fish first; the rest is just counted.
        var fish = loc.Abundance.Keys.Select(k => k.AsString()).Where(k => loc.AbundanceOf(k) > 0f)
            .OrderByDescending(k => loc.AbundanceOf(k)).ToList();
        string list = string.Join(", ", fish.Take(5)) + (fish.Count > 5 ? $" i {fish.Count - 5} innych" : "");
        box.AddChild(new Label
        {
            Text = $"Maks. głębokość: {loc.MaxDepth + loc.HoleDepth:0} m\nGatunki ({fish.Count}): {list}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(270, 0),
        });
        var button = new Button { CustomMinimumSize = new Vector2(0, 44) };
        if (locked)
        {
            button.Text = $"Zablokowane (poziom {loc.RequiredLevel})";
            button.Disabled = true;
        }
        else
        {
            button.Text = "Łów tutaj";
            button.Pressed += () => { _ = ChooseMap(index); };
        }
        box.AddChild(button);
        return panel;
    }

    private async Task ChooseMap(int index)
    {
        Session.MapIndex = index;
        _loading.Text = "Ładowanie łowiska...";
        _mapsBox.Visible = false;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().ChangeSceneToFile(MainScene);
    }

    private void Logout()
    {
        Session.Logout();
        ShowLogin();
    }
}
