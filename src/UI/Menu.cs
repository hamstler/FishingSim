using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
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
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.12f, 0.24f, 0.40f));
        gradient.SetColor(1, new Color(0.02f, 0.05f, 0.10f));
        var background = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = gradient, FillFrom = Vector2.Zero, FillTo = new Vector2(0, 1), Width = 4, Height = 256 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var title = new Label { Text = "Fishing Sim", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 64);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.96f, 1f));
        title.SetAnchorsPreset(LayoutPreset.TopWide);
        title.OffsetTop = 30f;
        title.OffsetBottom = 120f;
        AddChild(title);
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
        Center(panel, 420, 340);
        AddChild(panel);
        _loginBox = panel;
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 24);
        panel.AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        margin.AddChild(box);

        var head = new Label { Text = "Logowanie" };
        head.AddThemeFontSizeOverride("font_size", 28);
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
    private void ShowMaps()
    {
        _loginBox.Visible = false;
        foreach (var c in _mapsBox.GetChildren())
            c.QueueFree();
        _mapsBox.Visible = true;
        Center(_mapsBox, 1100, 560);
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
        row.AddThemeConstantOverride("separation", 20);
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
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(320, 0) };
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 14);
        panel.AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);

        if (!_previews.ContainsKey(index))
            _previews[index] = MapPreview.Render(loc, 260, (int)MathF.Round(260f * MapPreview.Aspect(loc)));
        box.AddChild(new TextureRect
        {
            Texture = _previews[index],
            CustomMinimumSize = new Vector2(290, 207),
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
            CustomMinimumSize = new Vector2(290, 70),
            Modulate = new Color(1, 1, 1, 0.8f),
        });
        var fish = loc.Abundance.Keys.Where(k => loc.AbundanceOf(k.AsString()) > 0f).Select(k => k.AsString());
        box.AddChild(new Label
        {
            Text = $"Maks. głębokość: {loc.MaxDepth + loc.HoleDepth:0} m\nRyby: {string.Join(", ", fish)}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(290, 0),
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
