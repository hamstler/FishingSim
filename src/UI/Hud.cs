using System.Linq;
using System.Collections.Generic;
using Godot;
using FishingSim.Core;
using FishingSim.Fishing;
using FishingSim.Tournaments;
using FishingSim.World;
using FishingSim.Core.Methods;

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
    private HBoxContainer _toolbar = null!;
    private Label _meal = null!;
    private string _toolbarKey = "";

    /// <summary>The angler's tools (set by Main), shown as a little bar when more than the rod is owned.</summary>
    public HandTools? Tools { get; set; }

    private void UpdateToolbar()
    {
        if (Tools == null)
            return;
        var list = Tools.Available();
        string key = string.Join(",", list) + "|" + Tools.Current;
        if (key != _toolbarKey)
        {
            _toolbarKey = key;
            foreach (var c in _toolbar.GetChildren())
                c.QueueFree();
            foreach (var tool in list)
            {
                bool on = tool == Tools.Current;
                var slot = new PanelContainer { CustomMinimumSize = new Vector2(52, 52), TooltipText = HandTools.NameOf(tool) };
                slot.AddThemeStyleboxOverride("panel", UiArt.Nine(UiArt.InsetTexture, 4, 3));
                slot.Modulate = on ? Colors.White : new Color(1f, 1f, 1f, 0.55f);
                slot.AddChild(new TextureRect
                {
                    Texture = IconOf(tool), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(44, 44),
                });
                if (on)
                    slot.AddThemeStyleboxOverride("panel", UITheme.Box(new Color(0.3f, 0.22f, 0.1f, 0.85f), UITheme.Accent, 4, 2, 3));
                _toolbar.AddChild(slot);
            }
            var hint = new Label { Text = "Q", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            hint.AddThemeFontSizeOverride("font_size", 14);
            hint.AddThemeColorOverride("font_color", new Color(Ink, 0.8f));
            _toolbar.AddChild(hint);
        }
        _toolbar.Visible = list.Count > 1 && !_helpOpen;
    }

    private Texture2D IconOf(HandTool tool) => tool switch
    {
        HandTool.Shovel => Graphics.Icons.For(GD.Load<Data.AccessoryData>("res://data/tackle/accessories/lopata.tres")),
        HandTool.Net => Graphics.Icons.For(GD.Load<Data.AccessoryData>("res://data/tackle/accessories/siatka_na_owady.tres")),
        HandTool.LiftNet => Graphics.Icons.For(GD.Load<Data.AccessoryData>("res://data/tackle/accessories/podrywka.tres")),
        _ => Graphics.Icons.For(_rod.Loadout.Rod),
    };

    private Label _status = null!, _purse = null!, _tackle = null!, _time = null!, _banner = null!, _prompt = null!, _tournament = null!;
    private Label _help = null!, _hint = null!, _dev = null!, _drag = null!;
    private ProgressBar _power = null!;
    private TensionGauge _gauge = null!;
    private bool _dragBesideGauge = true;

    private const float GaugeHalfWidth = 240f, GaugeTop = -78f, GaugeHeight = 14f;

    /// <summary>The gap between the status line and the bar, reused between the bar and the drag reading.</summary>
    private const float GaugeGap = 8f;

    private void PlaceDrag(bool besideGauge)
    {
        _dragBesideGauge = besideGauge;
        if (besideGauge)
        {
            _drag.HorizontalAlignment = HorizontalAlignment.Left;
            float mid = GaugeTop + GaugeHeight / 2f;
            Anchor(_drag, 0.5f, 1f, GaugeHalfWidth + GaugeGap, mid - 13f, GaugeHalfWidth + GaugeGap + 220f, mid + 13f);
        }
        else
        {
            _drag.HorizontalAlignment = HorizontalAlignment.Right;
            Anchor(_drag, 1f, 1f, -300, -38, -16, -12);
        }
    }
    private bool _devOpen, _helpOpen;
    private float _purseFlash, _tackleFlash, _hintTime = 10f;
    private string _lastPurse = "", _lastTackle = "";

    private Minimap? _minimap;
    private PerfOverlay _perf = null!;
    private CatchPanel _catchPanel = null!;

    /// <summary>The keep-or-release panel shown after a landing.</summary>
    public CatchPanel CatchPanel => _catchPanel;

    public void Setup(FishingRod rod, TournamentManager tournaments, InteractionManager interactions, Node3D? player = null, Scenery? scenery = null)
    {
        if (player != null && scenery != null)
        {
            _minimap = new Minimap();
            _minimap.Setup(rod, player, scenery);
        }
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
        // Minimap and clock in the top-right corner (they replace the time line).
        if (_minimap != null)
        {
            Anchor(_minimap, 1f, 0f, -Minimap.WidgetWidth - 14f, 12f, -14f, 12f + Minimap.WidgetHeight);
            AddChild(_minimap);
            _time.Visible = false;
        }

        _tournament = Text(15, Gold);
        Anchor(_tournament, 0f, 0f, 16, 12, 520, 100);

        _banner = Text(26, Ink, HorizontalAlignment.Center);
        Anchor(_banner, 0.5f, 0f, -420, 90, 420, 220);

        // The tools in hand (Q): rod, shovel, insect net; and the meal's buff under them.
        _toolbar = new HBoxContainer { Visible = false };
        _toolbar.AddThemeConstantOverride("separation", 6);
        Anchor(_toolbar, 0f, 1f, 16, -168, 300, -112);
        AddChild(_toolbar);
        _meal = Text(14, new Color(1f, 0.86f, 0.55f));
        Anchor(_meal, 0f, 1f, 16, -196, 520, -170);

        _prompt = Text(19, Gold, HorizontalAlignment.Center);
        Anchor(_prompt, 0.5f, 1f, -400, -170, 400, -140);

        // Fishing readout: state, then a thin tension bar with drag and line below it.
        _status = Text(18, Ink, HorizontalAlignment.Center);
        Anchor(_status, 0.5f, 1f, -300, -112, 300, -86);
        // The fight bar sits right under the status line ("Hol · 43 m"); during a fight the drag reading moves to
        // its right, the same gap away.
        _gauge = new TensionGauge { Rod = _rod };
        Anchor(_gauge, 0.5f, 1f, -GaugeHalfWidth, GaugeTop, GaugeHalfWidth, GaugeTop + GaugeHeight);
        AddChild(_gauge);
        // After a landing: the catch on its stage, keep it or let it go (over everything, the world dimmed).
        _catchPanel = new CatchPanel();
        _catchPanel.Setup(_rod);
        AddChild(_catchPanel);
        _power = new ProgressBar { ShowPercentage = false, Theme = UITheme.Make() };
        Anchor(_power, 0.5f, 1f, -160, -80, 160, -72);
        AddChild(_power);

        // Drag (mouse wheel) is shown in the bottom-right corner, and next to the fight bar while fighting.
        _drag = Text(17, Ink, HorizontalAlignment.Right);
        PlaceDrag(false);
        _purse = Text(15, new Color(Ink, 0.9f), HorizontalAlignment.Right);
        Anchor(_purse, 1f, 1f, -560, -92, -16, -40);
        _purse.VerticalAlignment = VerticalAlignment.Bottom;
        _tackle = Text(14, new Color(Ink, 0.85f));
        Anchor(_tackle, 0f, 1f, 16, -100, 520, -14);

        _hint = Text(13, new Color(Ink, 0.5f));
        _hint.Text = "H - sterowanie";
        Anchor(_hint, 0f, 1f, 16, -30, 300, -10);
        _help = Text(14, Ink);
        Anchor(_help, 0f, 0.5f, 16, -120, 720, 120);
        _help.Text = "WASD - ruch, Shift - bieg, mysz - rozglądanie\n"
            + "LPM przytrzymaj i puść - rzut, potem LPM - zwijanie linki\n"
            + "PPM - podniesienie wędki: zacięcie, pompowanie, wyciągnięcie ryby\n"
            + "Spacja - kasarek (trzeba go kupić), kółko - hamulec\n"
            + "Spinning (przynęta sztuczna): LPM prowadzi (Shift szybciej), puszczona opada, PPM podszarpnięcie / zaczep\n"
            + "Spławik: [ i ] (lub Ctrl + kółko) ustawiają grunt, czyli głębokość przynęty pod spławikiem\n"
            + "Grunt (wędka gruntowa): koszyczek nęci miejsce, brania na szczytówce; Z - wędka na podpórkę / podnieś\n"
            + "1-5 - zmiana sprzętu (Shift = wstecz), Tab - stan\n"
            + "E - wejście | I ekwipunek | M mapa | J dziennik | T turnieje\n"
            + "Esc - menu | F1 - dev | F11 - wydajność (FPS)";

        _dev = Text(13, Gold);
        Anchor(_dev, 1f, 0f, -460, Minimap.WidgetHeight + 30f, -16, Minimap.WidgetHeight + 390f);
        _dev.HorizontalAlignment = HorizontalAlignment.Right;

        // F11: frames per second and where the frame's time goes.
        _perf = new PerfOverlay();
        Anchor(_perf, 0f, 0f, 12f, 12f, 12f, 12f);
        AddChild(_perf);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.F1)
                _devOpen = !_devOpen;
            else if (key.Keycode == Key.F11)
                _perf.Toggle();
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
        using var perfScope = Graphics.Perf.Scope("hud");
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
            state += MethodLine(rod);
        else if (rod.State == RodState.Idle)
            state += $"  ·  {rod.Method.Title()}" + (rod.Method == FishingMethod.Float ? $", grunt {rod.FloatDepth:0.0} m ([ ])" : "");
        if (rod.State == RodState.Waiting && rod.World.TimeScale > 1f)
            state += "  ·  odpoczynek na zydlu";
        if (rod.State == RodState.Fight)
            state += $"  ·  {rod.LineOut:0} m{(rod.Lifting ? "  ·  pompowanie" : "")}";
        if (rod.State == RodState.Close && rod.HasNet)
            state += ", Spacja - kasarek";
        SetText(_status, state);
        Fade(_status, fishing || details, dt);
        _gauge.Visible = fighting;
        if (fighting != _dragBesideGauge)
            PlaceDrag(fighting);
        _power.Visible = rod.State == RodState.Charging;
        _power.Value = rod.CastPower * 100f;

        SetText(_drag, $"Hamulec {Mathf.RoundToInt(rod.DragSetting * 100f)}%");
        // The drag belongs to the rod: not shown with a tool in hand.
        _drag.Visible = !rod.Holstered;
        SetText(_banner, rod.BannerTime > 0f ? rod.BannerText : _banner.Text);
        // While the catch panel is open it says it all.
        Fade(_banner, rod.BannerTime > 0f && !_catchPanel.Visible, dt);

        // Purse and keepnet: flash when they change.
        var pr = rod.Profile;
        string limit = pr.KeepnetLimit is { } net ? $"/{net.MaxFish}" : "";
        string purse = $"{Lore.Money(pr.Money)}  ·  siatka {pr.Keepnet.Count}{limit}  ·  poziom {pr.Level}";
        // The pantry and the smoked fish, when there are any.
        string pantry = Core.Camp.Foraging.Describe(pr.Pantry);
        if (pantry.Length > 0 || pr.Smoked.Count > 0)
            purse += "\n" + string.Join("  ·  ", new[] { pantry, pr.Smoked.Count > 0 ? $"wędzonki ×{pr.Smoked.Count}" : "" }.Where(x => x.Length > 0));
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
        string leader = lo.Leader.ItemId == Progress.TackleSlotExtensions.NoLeader ? "" : $", {lo.Leader.DisplayName}";
        string tackle = $"{lo.Rod.DisplayName}, {lo.Reel.DisplayName}\n{lo.Line.DisplayName}{leader}, {lo.Hook.DisplayName}\n{lo.Bait.DisplayName} ×{lo.BaitCount}";
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
        if (_minimap == null)
            Fade(_time, details, dt);

        UpdateToolbar();
        bool soaking = Tools is { Current: HandTool.LiftNet, Lift: HandTools.LiftStage.Down };
        SetText(_meal, soaking
            ? $"Podrywka w wodzie: {Tools!.Soak:0} s  (LPM podrywa)"
            : pr.ActiveMeal is { } meal
            ? $"{meal.Name}: +{meal.XpBonus * 100f:0}% XP, szybsze brania ({pr.MealHoursLeft:0.0} h)"
            : "");
        Fade(_meal, soaking || pr.ActiveMeal != null, dt);

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

    /// <summary>What the line in the water is doing: the float's depth; the lure, how deep it runs over how deep a
    /// bottom and how well it is led; the feeder sinking or on the bottom, the baited spot, the rod on its rest.</summary>
    private static string MethodLine(FishingRod rod)
    {
        string rest = (rod.OnRest ? "  ·  wędka na podpórce (Z)" : "") + (rod.Slack > 0.3f ? $"  ·  luz {rod.Slack:0.0} m" : "")
            + (rod.Spooked ? "  ·  ryby spłoszone" : "");
        switch (rod.CastMethod)
        {
            case Core.Methods.FishingMethod.Spinning when rod.Lure is { } lure:
                string colour = Method.ColourWord(rod.LureAppeal);
                return $"  ·  {lure.Family.Title()} {lure.Depth:0.0} m (dno {rod.CurrentDepth():0.0} m)  ·  {lure.Judgement.Title()}"
                    + (colour.Length > 0 ? $"  ·  {colour}" : "") + rest;
            case Core.Methods.FishingMethod.Bottom:
                string rig = rod.RigSettled ? $"koszyczek na dnie, {rod.CurrentDepth():0.0} m" : "koszyczek opada";
                float heat = rod.GroundbaitHeat;
                int dots = heat < 0.05f ? 0 : heat < 0.8f ? 1 : heat < 1.8f ? 2 : 3;
                string baited = dots > 0 ? $"  ·  nęcone miejsce {new string('●', dots)}{new string('○', 3 - dots)}" : "";
                return $"  ·  {rig}{baited}{rest}";
            default:
                float water = rod.CurrentDepth();
                string hang = rod.OverDepth ? "przegruntowany: przynęta leży na dnie" : $"przynęta {System.MathF.Max(water - rod.FloatDepth, 0f):0.0} m nad dnem";
                return $"  ·  woda {water:0.0} m, grunt {rod.FloatDepth:0.0} m ({hang}){rest}";
        }
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
