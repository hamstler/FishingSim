using System.Collections.Generic;
using Godot;
using FishingSim.Core;
using FishingSim.Fishing;
using FishingSim.Tournaments;
using FishingSim.World;

namespace FishingSim.UI;

/// <summary>
/// Minimal in-game overlay. Nothing is on screen while you just look around; information appears only when it
/// matters and fades out again:
/// a tiny crosshair; the rod state, line, drag and a thin tension bar while fishing; a thin power bar while casting;
/// the purse and keepnet when they change; the tackle when it changes; catch messages; the door prompt;
/// the tournament line during a tournament. Hold Tab to see everything at once, H for the controls, F1 for dev.
/// </summary>
public partial class Hud : CanvasLayer
{
    private const float FlashSeconds = 4f;
    private static readonly Color Ink = new(0.97f, 0.92f, 0.8f);
    private static readonly Color Gold = new(1f, 0.84f, 0.45f);

    private FishingRod _rod = null!;
    private TournamentManager _tournaments = null!;
    private InteractionManager _interactions = null!;
    private Label _status = null!, _purse = null!, _tackle = null!, _time = null!, _banner = null!, _prompt = null!, _tournament = null!;
    private Label _help = null!, _hint = null!, _dev = null!, _drag = null!;
    private ProgressBar _power = null!;
    private TensionGauge _gauge = null!;
    private bool _devOpen, _helpOpen;
    private float _purseFlash, _tackleFlash, _hintTime = 10f;
    private string _lastPurse = "", _lastTackle = "";

    public void Setup(FishingRod rod, TournamentManager tournaments, InteractionManager interactions)
    {
        _rod = rod;
        _tournaments = tournaments;
        _interactions = interactions;
    }

    /// <summary>A borderless label with a dark outline so it reads over any background.</summary>
    private Label Text(int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { HorizontalAlignment = align };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.04f, 0.02f, 0.9f));
        l.AddThemeConstantOverride("outline_size", 5);
        AddChild(l);
        return l;
    }

    private static void Anchor(Control c, float ax, float ay, float left, float top, float right, float bottom)
    {
        c.AnchorLeft = c.AnchorRight = ax;
        c.AnchorTop = c.AnchorBottom = ay;
        c.OffsetLeft = left; c.OffsetTop = top; c.OffsetRight = right; c.OffsetBottom = bottom;
    }

    private static void SetText(Label label, string text)
    {
        if (label.Text != text)
            label.Text = text;
    }

    public override void _Ready()
    {
        var cross = Text(14, new Color(1f, 1f, 1f, 0.7f), HorizontalAlignment.Center);
        cross.Text = "·";
        Anchor(cross, 0.5f, 0.5f, -10, -12, 10, 12);

        _time = Text(15, new Color(Ink, 0.75f), HorizontalAlignment.Right);
        Anchor(_time, 1f, 0f, -420, 12, -16, 60);

        _tournament = Text(15, Gold);
        Anchor(_tournament, 0f, 0f, 16, 12, 520, 100);

        _banner = Text(26, Ink, HorizontalAlignment.Center);
        Anchor(_banner, 0.5f, 0f, -420, 90, 420, 220);

        _prompt = Text(19, Gold, HorizontalAlignment.Center);
        Anchor(_prompt, 0.5f, 1f, -400, -170, 400, -140);

        // Fishing readout: state, then a thin tension bar with drag and line below it.
        _status = Text(18, Ink, HorizontalAlignment.Center);
        Anchor(_status, 0.5f, 1f, -300, -112, 300, -86);
        _gauge = new TensionGauge { Rod = _rod };
        Anchor(_gauge, 0.5f, 1f, -160, -80, 160, -72);
        AddChild(_gauge);
        _power = new ProgressBar { ShowPercentage = false, Theme = UITheme.Make() };
        Anchor(_power, 0.5f, 1f, -160, -80, 160, -72);
        AddChild(_power);

        // Drag (mouse wheel) is always shown in the bottom-right corner.
        _drag = Text(17, Ink, HorizontalAlignment.Right);
        Anchor(_drag, 1f, 1f, -300, -38, -16, -12);
        _purse = Text(15, new Color(Ink, 0.9f), HorizontalAlignment.Right);
        Anchor(_purse, 1f, 1f, -460, -66, -16, -40);
        _tackle = Text(14, new Color(Ink, 0.85f));
        Anchor(_tackle, 0f, 1f, 16, -100, 520, -14);

        _hint = Text(13, new Color(Ink, 0.5f));
        _hint.Text = "H - sterowanie";
        Anchor(_hint, 0f, 1f, 16, -30, 300, -10);
        _help = Text(14, Ink);
        Anchor(_help, 0f, 0.5f, 16, -90, 600, 90);
        _help.Text = "WASD - ruch, Shift - bieg, mysz - rozglądanie\n"
            + "LPM przytrzymaj i puść - rzut, potem LPM - zwijanie linki\n"
            + "PPM - podniesienie wędki: zacięcie, pompowanie, wyciągnięcie ryby\n"
            + "Spacja - kasarek (trzeba go kupić), kółko - hamulec\n"
            + "1-5 - zmiana sprzętu (Shift = wstecz), Tab - stan\n"
            + "E - wejście | I ekwipunek | M mapa | J dziennik | T turnieje\n"
            + "Esc - menu | F1 - dev";

        _dev = Text(13, Gold);
        Anchor(_dev, 1f, 0f, -460, 40, -16, 400);
        _dev.HorizontalAlignment = HorizontalAlignment.Right;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.F1)
                _devOpen = !_devOpen;
            else if (key.Keycode == Key.H)
            {
                _helpOpen = !_helpOpen;
                _hintTime = 0f;
            }
        }
    }

    /// <summary>Fades a label in while <paramref name="shown"/> and out over half a second afterwards.</summary>
    private static void Fade(Label l, bool shown, float dt)
    {
        float a = Mathf.MoveToward(l.Modulate.A, shown ? 1f : 0f, dt * (shown ? 6f : 2f));
        l.Modulate = new Color(1, 1, 1, a);
        l.Visible = a > 0.01f;
    }

    public override void _Process(double delta)
    {
        if (_rod == null || _rod.Loadout == null)
            return;
        float dt = (float)delta;
        var rod = _rod;
        bool details = Input.IsKeyPressed(Key.Tab);

        // Fishing readout only while a line is out (or while casting).
        bool fishing = rod.State != RodState.Idle;
        bool fighting = rod.State is RodState.Fight or RodState.Close;
        string state = rod.StateName;
        if (rod.State is RodState.Waiting or RodState.Nibble or RodState.Bite)
            state += $"  ·  {rod.CurrentDepth():0.0} m";
        if (rod.State == RodState.Waiting && rod.World.MinutesPerSecond > 1f)
            state += "  ·  odpoczynek na zydlu";
        if (rod.State == RodState.Fight)
            state += $"  ·  {rod.LineOut:0} m{(rod.Lifting ? "  ·  pompowanie" : "")}";
        if (rod.State == RodState.Close && rod.HasNet)
            state += ", Spacja - kasarek";
        SetText(_status, state);
        Fade(_status, fishing || details, dt);
        _gauge.Visible = fighting;
        _gauge.QueueRedraw();
        _power.Visible = rod.State == RodState.Charging;
        _power.Value = rod.CastPower * 100f;

        SetText(_drag, $"Hamulec {Mathf.RoundToInt(rod.DragSetting * 100f)}%");
        SetText(_banner, rod.BannerTime > 0f ? rod.BannerText : _banner.Text);
        Fade(_banner, rod.BannerTime > 0f, dt);

        // Purse and keepnet: flash when they change.
        var pr = rod.Profile;
        string limit = pr.KeepnetLimit is { } net ? $"/{net.MaxFish}" : "";
        string purse = $"{Lore.Money(pr.Money)}  ·  siatka {pr.Keepnet.Count}{limit}  ·  poziom {pr.Level}";
        if (purse != _lastPurse)
        {
            if (_lastPurse != "")
                _purseFlash = FlashSeconds;
            _lastPurse = purse;
            SetText(_purse, purse);
        }
        _purseFlash -= dt;
        Fade(_purse, _purseFlash > 0f || details, dt);

        var lo = rod.Loadout;
        string tackle = $"{lo.Rod.DisplayName}, {lo.Reel.DisplayName}\n{lo.Line.DisplayName}, {lo.Hook.DisplayName}\n{lo.Bait.DisplayName} ×{lo.BaitCount}";
        if (tackle != _lastTackle)
        {
            if (_lastTackle != "")
                _tackleFlash = FlashSeconds;
            _lastTackle = tackle;
            SetText(_tackle, tackle);
        }
        _tackleFlash -= dt;
        Fade(_tackle, (_tackleFlash > 0f || details) && !_helpOpen, dt);

        SetText(_time, $"{rod.World.TimeText}  ·  {rod.World.Weather.DisplayName()}\n{rod.Lake.Location.DisplayName}");
        Fade(_time, details, dt);

        var spot = _interactions.Current;
        if (spot != null)
            SetText(_prompt, spot.Prompt);
        Fade(_prompt, spot != null, dt);

        var session = _tournaments.Active;
        if (session != null)
        {
            var me = session.PlayerStanding;
            SetText(_tournament, $"{session.Data.DisplayName}  ·  {TournamentTab.TimeText(session.Remaining)}\n"
                + $"miejsce {me.Rank}/{session.Entrants}  ·  {me.Score:0.00} kg ({me.Fish} ryb)");
        }
        Fade(_tournament, session != null, dt);

        _hintTime -= dt;
        Fade(_hint, _hintTime > 0f && !_helpOpen, dt);
        Fade(_help, _helpOpen, dt);

        _dev.Visible = _devOpen;
        if (_devOpen)
            SetText(_dev, DevText());
    }

    private string DevText()
    {
        var rod = _rod;
        string forced = rod.DevSpeciesIndex >= 0 && rod.DevSpeciesIndex < rod.Species.Count ? rod.Species[rod.DevSpeciesIndex].SpeciesName : "losowy";
        string fish = "-";
        if (rod.CurrentSpecies != null && rod.State is RodState.Nibble or RodState.Bite or RodState.Fight or RodState.Close)
            fish = $"{rod.CurrentSpecies.SpeciesName} {rod.CurrentWeight:0.00} kg, kondycja {Mathf.RoundToInt(rod.FishStamina * 100f)}%";
        var weights = new List<string>();
        foreach (var sp in rod.Species)
            weights.Add($"{sp.SpeciesName} {rod.BiteWeight(sp):0.00}");
        return "KONSOLA DEV (F1)\n"
            + $"F2 natychmiastowe branie: {(rod.DevInstantBite ? "TAK" : "nie")}\n"
            + $"F3 nieśmiertelna linka: {(rod.DevUnbreakable ? "TAK" : "nie")}\n"
            + $"F4 gatunek: {forced}\nF5 reset rzutu | F6 przynęty\nF7 +3 godz. | F8 pogoda\nF9 reset postępu | F10 +1000 gr\n"
            + $"Szansa brania: {string.Join(", ", weights)}\nRyba: {fish}";
    }
}
