using System.Collections.Generic;
using Godot;
using FishingSim.Core;
using FishingSim.Fishing;
using FishingSim.Progress;
using FishingSim.Tournaments;
using FishingSim.World;

namespace FishingSim.UI;

/// <summary>
/// In-game overlay: status, line tension gauge, cast power, player card, world info, equipped tackle, banner,
/// help (H) and the dev console (F1).
/// </summary>
public partial class Hud : CanvasLayer
{
    private static readonly Color PanelFill = new(0.13f, 0.09f, 0.05f, 0.72f);
    private static readonly Color PanelBorder = new(0.6f, 0.45f, 0.22f, 0.75f);

    private FishingRod _rod = null!;
    private TournamentManager _tournaments = null!;
    private InteractionManager _interactions = null!;
    private PanelContainer _promptPanel = null!;
    private Label _prompt = null!;
    private PanelContainer _tournamentPanel = null!;
    private Label _tournament = null!;
    private Label _status = null!, _stats = null!, _player = null!, _world = null!, _tackle = null!, _banner = null!, _hint = null!, _dev = null!;
    private ProgressBar _xp = null!, _power = null!;
    private PanelContainer _bannerPanel = null!, _helpPanel = null!, _devPanel = null!;
    private TensionGauge _gauge = null!;
    private bool _devOpen;
    private bool _helpOpen = true;

    public void Setup(FishingRod rod, TournamentManager tournaments, InteractionManager interactions)
    {
        _rod = rod;
        _tournaments = tournaments;
        _interactions = interactions;
    }

    private PanelContainer Panel(Node parent, Color? fill = null, Color? border = null, int radius = 0, int margin = 10)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", UITheme.Box(fill ?? PanelFill, border ?? PanelBorder, radius, 2, margin));
        parent.AddChild(p);
        return p;
    }

    private static Label MakeLabel(Node parent, int fontSize = 16, Color? color = null)
    {
        var l = new Label();
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", color ?? Colors.White);
        parent.AddChild(l);
        return l;
    }

    private static void Anchor(Control c, float left, float top, float right, float bottom, float ox = 0, float oy = 0, float ow = 0, float oh = 0)
    {
        c.AnchorLeft = left; c.AnchorTop = top; c.AnchorRight = right; c.AnchorBottom = bottom;
        c.OffsetLeft = ox; c.OffsetTop = oy; c.OffsetRight = ow; c.OffsetBottom = oh;
    }

    /// <summary>Assigns text only when it changed, which avoids needless layout work every frame.</summary>
    private static void SetText(Label label, string text)
    {
        if (label.Text != text)
            label.Text = text;
    }

    public override void _Ready()
    {
        var theme = UITheme.Make();

        // top-left: status + line stats, then the player card
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 8);
        AddChild(left);
        Anchor(left, 0, 0, 0, 0, 14, 14, 330, 0);
        var statusBox = new VBoxContainer();
        Panel(left).AddChild(statusBox);
        _status = MakeLabel(statusBox, 21, new Color(1f, 0.86f, 0.5f));
        _stats = MakeLabel(statusBox, 15);
        var playerBox = new VBoxContainer();
        Panel(left).AddChild(playerBox);
        _player = MakeLabel(playerBox, 15);
        _xp = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10), Theme = theme };
        playerBox.AddChild(_xp);
        _tournamentPanel = Panel(left, new Color(0.12f, 0.1f, 0.02f, 0.72f), new Color(0.85f, 0.7f, 0.25f, 0.8f));
        _tournament = MakeLabel(_tournamentPanel, 15, new Color(1f, 0.93f, 0.6f));

        // top-right: world, tackle, dev
        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 8);
        AddChild(right);
        Anchor(right, 1, 0, 1, 0, -344, 14, -14, 0);
        _world = MakeLabel(Panel(right), 15);
        _tackle = MakeLabel(Panel(right), 14, new Color(0.96f, 0.9f, 0.76f));
        _devPanel = Panel(right, new Color(0.12f, 0.1f, 0.02f, 0.8f), new Color(0.8f, 0.7f, 0.2f, 0.8f));
        _dev = MakeLabel(_devPanel, 14, new Color(1f, 0.9f, 0.3f));

        // banner
        _bannerPanel = Panel(this, new Color(0.12f, 0.08f, 0.05f, 0.85f), new Color(0.85f, 0.65f, 0.3f, 0.9f), 0, 14);
        Anchor(_bannerPanel, 0.5f, 0, 0.5f, 0, -300, 110, 300, 0);
        _banner = MakeLabel(_bannerPanel, 28);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;

        // prompt shown at a shop door
        _promptPanel = Panel(this, new Color(0.12f, 0.08f, 0.05f, 0.88f), new Color(0.95f, 0.8f, 0.3f, 0.9f), 0, 12);
        Anchor(_promptPanel, 0.5f, 1, 0.5f, 1, -260, -170, 260, -140);
        _prompt = MakeLabel(_promptPanel, 20, new Color(1f, 0.93f, 0.6f));
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
        _promptPanel.Visible = false;

        // crosshair
        var cross = MakeLabel(this, 22);
        cross.Text = "+";
        Anchor(cross, 0.5f, 0.5f, 0.5f, 0.5f, -10, -14, 10, 14);
        cross.HorizontalAlignment = HorizontalAlignment.Center;

        // gauge + power
        _gauge = new TensionGauge { Rod = _rod };
        Anchor(_gauge, 0.5f, 1, 0.5f, 1, -210, -78, 210, -50);
        AddChild(_gauge);
        var caption = MakeLabel(this, 13, new Color(0.96f, 0.9f, 0.76f, 0.8f));
        caption.Text = "NAPIĘCIE ŻYŁKI (biała kreska = hamulec)";
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        Anchor(caption, 0.5f, 1, 0.5f, 1, -210, -100, 210, -80);
        _power = new ProgressBar { Theme = theme, ShowPercentage = false };
        Anchor(_power, 0.5f, 1, 0.5f, 1, -210, -132, 210, -112);
        AddChild(_power);

        // help
        _helpPanel = Panel(this);
        Anchor(_helpPanel, 0, 1, 0, 1, 14, -250, 400, -14);
        var help = MakeLabel(_helpPanel, 14);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        help.CustomMinimumSize = new Vector2(380, 0);
        help.Text = "WASD - ruch | Mysz - rozglądanie | Esc - menu | H - ukryj pomoc\n"
            + "LPM przytrzymaj - ładuj rzut, puść - rzut; potem LPM - zwijaj\n"
            + "PPM / Spacja - zacięcie (gdy spławik się zanurzy)\n"
            + "Kółko myszy - hamulec | 1-5 - zmiana sprzętu (Shift = wstecz)\n"
            + "E - wejdź do sklepu | I - ekwipunek | M - mapa | J - dziennik | T - turnieje | F1 - dev\n"
            + "Shift - bieg (możesz chodzić po całej mapie)";
        _hint = MakeLabel(this, 14, new Color(1, 1, 1, 0.55f));
        _hint.Text = "H - pomoc";
        Anchor(_hint, 0, 1, 0, 1, 16, -34, 200, -10);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.F1)
                _devOpen = !_devOpen;
            else if (key.Keycode == Key.H)
                _helpOpen = !_helpOpen;
        }
    }

    public override void _Process(double delta)
    {
        if (_rod == null || _rod.Loadout == null)
            return;
        var rod = _rod;
        SetText(_status, rod.StateName);
        SetText(_stats, $"Hamulec: {Mathf.RoundToInt(rod.DragSetting * 100f)}% ({rod.DragSetting * rod.DragMax:0.0} kg)\nŻyłka: {rod.LineOut:0.0} m");
        _bannerPanel.Visible = rod.BannerTime > 0f;
        SetText(_banner, rod.BannerTime > 0f ? rod.BannerText : "");
        _power.Visible = rod.State == RodState.Charging;
        _power.Value = rod.CastPower * 100f;
        _gauge.QueueRedraw();
        _helpPanel.Visible = _helpOpen;
        _hint.Visible = !_helpOpen;

        var pr = rod.Profile;
        SetText(_player, $"{pr.Username} | poziom {pr.Level} | {pr.Money} zł\nSiatka: {pr.Keepnet.Count} ryb ({pr.KeepnetWeight:0.0} kg, ok. {pr.KeepnetValue} zł)");
        _xp.MinValue = Progression.XpForLevel(pr.Level);
        _xp.MaxValue = pr.XpToNext;
        _xp.Value = pr.Xp;

        bool inPlay = rod.State is RodState.Waiting or RodState.Nibble or RodState.Bite or RodState.Fight;
        string depth = inPlay ? $"{rod.CurrentDepth():0.0} m" : "-";
        SetText(_world, $"{rod.World.TimeText}, {rod.World.Period.DisplayName()}, {rod.World.Weather.DisplayName()}\n{rod.Lake.Location.DisplayName}\nGłębokość pod spławikiem: {depth}");
        var lo = rod.Loadout;
        SetText(_tackle, $"{lo.Rod.DisplayName}\n{lo.Reel.DisplayName}\n{lo.Line.DisplayName} | {lo.Hook.DisplayName}\n{lo.Bait.DisplayName} x{lo.BaitCount}   (I - ekwipunek)");

        var spot = _interactions.Current;
        _promptPanel.Visible = spot != null;
        if (spot != null)
            SetText(_prompt, spot.Prompt);

        var session = _tournaments.Active;
        _tournamentPanel.Visible = session != null;
        if (session != null)
        {
            var me = session.PlayerStanding;
            var leader = session.Leader;
            SetText(_tournament, $"TURNIEJ: {session.Data.DisplayName}\nCzas: {TournamentTab.TimeText(session.Remaining)} | pozycja {me.Rank}/{session.Entrants}\n"
                + $"Wynik: {me.Score:0.00} kg ({me.Fish} ryb)\nLider: {(leader.IsPlayer ? "Ty" : leader.Name)} {leader.Score:0.00} kg");
        }

        _devPanel.Visible = _devOpen;
        if (_devOpen)
            SetText(_dev, DevText());
    }

    private string DevText()
    {
        var rod = _rod;
        string forced = rod.DevSpeciesIndex >= 0 && rod.DevSpeciesIndex < rod.Species.Count ? rod.Species[rod.DevSpeciesIndex].SpeciesName : "losowy";
        string fish = "-";
        if (rod.CurrentSpecies != null && rod.State is RodState.Nibble or RodState.Bite or RodState.Fight)
            fish = $"{rod.CurrentSpecies.SpeciesName} {rod.CurrentWeight:0.00} kg, kondycja {Mathf.RoundToInt(rod.FishStamina * 100f)}%";
        var weights = new List<string>();
        foreach (var sp in rod.Species)
            weights.Add($"{sp.SpeciesName} {rod.BiteWeight(sp):0.00}");
        return "KONSOLA DEV (F1)\n"
            + $"F2 natychmiastowe branie + pewne zacięcie: {(rod.DevInstantBite ? "TAK" : "nie")}\n"
            + $"F3 nieśmiertelna żyłka: {(rod.DevUnbreakable ? "TAK" : "nie")}\n"
            + $"F4 gatunek: {forced}\nF5 reset rzutu\nF6 uzupełnij przynęty\nF7 +3 godz. | F8 zmiana pogody\nF9 reset postępu | F10 +1000 zł\n"
            + $"Szansa brania (waga): {string.Join(", ", weights)}\nRyba: {fish}";
    }
}
