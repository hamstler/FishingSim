using System.Linq;
using System.Collections.Generic;
using Godot;
using FishingSim.Core;
using FishingSim.Fishing;
using FishingSim.Tournaments;
using FishingSim.World;
using FishingSim.Core.Methods;
using FishingSim.Progress;

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

    private FishingRod _rodFixed = null!;
    /// <summary>The rod in use (the active one of the rod set, Fishing/RodSet).</summary>
    private FishingRod _rod => FishingRod.ActiveRod ?? _rodFixed;
    private TournamentManager _tournaments = null!;
    private InteractionManager _interactions = null!;
    private HBoxContainer _toolbar = null!;
    private Label _meal = null!;
    private string _toolbarKey = "";
    private float _toolbarShown;
    /// <summary>The hotbar's opacity while it is not being used.</summary>
    private const float ToolbarIdleAlpha = 0.3f;

    /// <summary>The angler's tools (set by Main), shown as a little bar when more than the rod is owned.</summary>
    public HandTools? Tools { get; set; }

    private float _toolbarCheck;

    /// <summary>
    /// The hotbar (keys 1-9) along the bottom: rods 1-3 (their rod's picture, and where each is: in the hand, on its
    /// rest in the water, in the backpack), the shovel, the insect net and the lift net (4-6, dim until bought), 7-9 free.
    /// The slot in hand is framed in gold.
    /// </summary>
    private void UpdateToolbar()
    {
        if (Tools == null)
            return;
        _toolbar.Visible = !_helpOpen;
        // Like the rest of a light HUD the bar steps back while it is not used: full for a few seconds after a change
        // (a key 1-9, a rod put on its rest), then faint, so the reel and the water under it show.
        float dt = (float)GetProcessDeltaTime();
        _toolbarShown -= dt;
        float alpha = _toolbarShown > 0f ? 1f : ToolbarIdleAlpha;
        var m = _toolbar.Modulate;
        if (Mathf.Abs(m.A - alpha) > 0.005f)
            _toolbar.Modulate = new Color(1f, 1f, 1f, Mathf.MoveToward(m.A, alpha, dt * 2.5f));
        _toolbarCheck -= dt;
        if (_toolbarCheck > 0f)
            return;
        _toolbarCheck = 0.15f;
        var owned = Tools.Available();
        var sb = new System.Text.StringBuilder();
        var slots = new (Texture2D? Icon, string Note, bool On, bool Dim)[9];
        for (int i = 0; i < 9; i++)
        {
            if (i < RodSet.MaxRods)
            {
                var rod = Tools.Rods?.At(i) ?? (i == 0 ? _rod : null);
                string rodId = rod?.Loadout.Rod.ItemId ?? _rod.Profile.EquippedFor(i).GetValueOrDefault("rod", "");
                bool ready = rod?.Loadout.Ready ?? (rodId != "" && _rod.Profile.Owns(TackleSlot.Rod, rodId));
                var item = rodId == "" ? null : _rod.Loadout.Items(TackleSlot.Rod).FirstOrDefault(t => t.ItemId == rodId);
                bool on = Tools.Current == HandTool.Rod && (Tools.Rods?.ActiveSlot ?? 0) == i && rod is { OnRest: false };
                string note = rod == null ? (ready ? "plecak" : "pusty")
                    : rod.OnRest ? "podpórka"
                    : RodSet.InWater(rod) ? "w wodzie"
                    : on ? "w ręku" : ready ? "plecak" : "pusty";
                slots[i] = (ready && item != null ? Graphics.Icons.For(item) : null, note, on, !ready);
            }
            else if (HandTools.ToolSlots[i - RodSet.MaxRods] is HandTool tool)
                slots[i] = (IconOf(tool), "", Tools.Current == tool, !owned.Contains(tool));
            else
                slots[i] = (null, "", false, true);
            sb.Append(slots[i].Icon?.GetInstanceId() ?? 0).Append(slots[i].Note).Append(slots[i].On).Append(slots[i].Dim).Append('|');
        }
        string key = sb.ToString();
        if (key == _toolbarKey)
            return;
        _toolbarKey = key;
        _toolbarShown = 4f;
        foreach (var c in _toolbar.GetChildren())
            c.QueueFree();
        for (int i = 0; i < 9; i++)
        {
            var (icon, note, on, dim) = slots[i];
            var slot = new PanelContainer { CustomMinimumSize = new Vector2(54, 50) };
            slot.AddThemeStyleboxOverride("panel", on
                ? UITheme.Box(new Color(0.3f, 0.22f, 0.1f, 0.9f), UITheme.Accent, 4, 2, 3)
                : UiArt.Nine(UiArt.InsetTexture, 4, 3));
            slot.Modulate = on ? Colors.White : new Color(1f, 1f, 1f, dim ? 0.4f : 0.75f);
            var stack = new Control { CustomMinimumSize = new Vector2(48, 44) };
            if (icon != null)
                stack.AddChild(new TextureRect
                {
                    Texture = icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Size = new Vector2(48, 36),
                });
            var num = new Label { Text = (i + 1).ToString(), Position = new Vector2(-2, -6) };
            num.AddThemeFontSizeOverride("font_size", 13);
            num.AddThemeColorOverride("font_color", on ? Gold : new Color(Ink, 0.9f));
            num.AddThemeConstantOverride("outline_size", 4);
            num.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
            stack.AddChild(num);
            if (note != "")
            {
                var lab = new Label { Text = note, Position = new Vector2(-4, 31), Size = new Vector2(56, 14), HorizontalAlignment = HorizontalAlignment.Center };
                lab.AddThemeFontSizeOverride("font_size", 9);
                lab.AddThemeColorOverride("font_color", note == "w wodzie" || note == "podpórka" ? new Color(0.6f, 0.85f, 1f) : new Color(Ink, 0.85f));
                lab.AddThemeConstantOverride("outline_size", 3);
                lab.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
                stack.AddChild(lab);
            }
            slot.AddChild(stack);
            _toolbar.AddChild(slot);
        }
    }

    /// <summary>The hotbar's slots as text (tests): "1:w ręku 2:pusty ...".</summary>
    public string HotbarText => _toolbarKey;

    private Texture2D IconOf(HandTool tool) => tool switch
    {
        HandTool.Shovel => Graphics.Icons.For(GD.Load<Data.AccessoryData>("res://data/tackle/accessories/lopata.tres")),
        HandTool.Net => Graphics.Icons.For(GD.Load<Data.AccessoryData>("res://data/tackle/accessories/siatka_na_owady.tres")),
        HandTool.LiftNet => Graphics.Icons.For(GD.Load<Data.AccessoryData>("res://data/tackle/accessories/podrywka.tres")),
        _ => Graphics.Icons.For(_rod.Loadout.Rod),
    };

    private Label _status = null!, _purse = null!, _tackle = null!, _time = null!, _banner = null!, _prompt = null!, _tournament = null!;
    private Label _dev = null!;
    private QuickHelp _quickHelp = null!;
    private DragRing _dragRing = null!;
    private TensionGauge _gauge = null!;
    private Color _statusColour = Ink;

    private const float GaugeHalfWidth = 240f, GaugeTop = -78f, GaugeHeight = 14f;

    /// <summary>The gap between the status line and the bar, reused between the bar and the drag reading.</summary>
    private const float GaugeGap = 8f;

    private bool _devOpen, _helpOpen;
    private float _purseFlash, _tackleFlash;
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
        _rodFixed = rod;
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

        // The hotbar (keys 1-9) along the bottom in the middle; the meal's buff on the left.
        _toolbar = new HBoxContainer { Visible = false, Alignment = BoxContainer.AlignmentMode.Center };
        _toolbar.AddThemeConstantOverride("separation", 4);
        Anchor(_toolbar, 0.5f, 1f, -270, -60, 270, -6);
        AddChild(_toolbar);
        _meal = Text(14, new Color(1f, 0.86f, 0.55f));
        Anchor(_meal, 0f, 1f, 16, -196, 520, -170);

        _prompt = Text(19, Gold, HorizontalAlignment.Center);
        Anchor(_prompt, 0.5f, 1f, -400, -170, 400, -140);

        // Fishing readout: state, then a thin tension bar with drag and line below it.
        _status = Text(17, Ink, HorizontalAlignment.Center);
        Anchor(_status, 0.5f, 1f, -720, -106, 720, -82);
        // The fight bar sits right under the status line ("Hol · 43 m"); during a fight the drag reading moves to
        // its right, the same gap away.
        _gauge = new TensionGauge { Rod = _rod };
        Anchor(_gauge, 0.5f, 1f, -GaugeHalfWidth, GaugeTop, GaugeHalfWidth, GaugeTop + GaugeHeight);
        AddChild(_gauge);
        // After a landing: the catch on its stage, keep it or let it go (over everything, the world dimmed).
        _catchPanel = new CatchPanel();
        _catchPanel.Setup(_rod);
        AddChild(_catchPanel);
        // The drag (mouse wheel): a ring in the middle of the screen while it is turned.
        _dragRing = new DragRing { Rod = _rod };
        Anchor(_dragRing, 0.5f, 0.5f, -80, -80, 80, 80);
        AddChild(_dragRing);
        _purse = Text(15, new Color(Ink, 0.9f), HorizontalAlignment.Right);
        Anchor(_purse, 1f, 1f, -560, -92, -16, -40);
        _purse.VerticalAlignment = VerticalAlignment.Bottom;
        _tackle = Text(14, new Color(Ink, 0.85f));
        Anchor(_tackle, 0f, 1f, 16, -100, 520, -14);

        // F1 (Bindings: QuickHelp): the quick instructions with the keys as they are bound now.
        _quickHelp = new QuickHelp { Visible = false };
        Anchor(_quickHelp, 0f, 0.5f, 16, -230, 820, 230);
        AddChild(_quickHelp);

        _dev = Text(13, Gold);
        Anchor(_dev, 1f, 0f, -460, Minimap.WidgetHeight + 30f, -16, Minimap.WidgetHeight + 390f);
        _dev.HorizontalAlignment = HorizontalAlignment.Right;

        // F11: frames per second and where the frame's time goes.
        _perf = new PerfOverlay();
        Anchor(_perf, 0f, 0f, 12f, 12f, 12f, 12f);
        AddChild(_perf);

        // The float preview window (graphics settings, "Podgląd spławika"), right side above the drag readout.
        var floatCam = new FloatCam();
        Anchor(floatCam, 1f, 1f, -364f, -400f, -16f, -166f);
        AddChild(floatCam);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Bindings.Of(Act.DevConsole))
                _devOpen = !_devOpen;
            else if (key.Keycode == Bindings.Of(Act.Performance))
                _perf.Toggle();
            else if (key.Keycode == Bindings.Of(Act.QuickHelp))
            {
                _helpOpen = !_helpOpen;
                _quickHelp.Visible = _helpOpen;
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
        bool details = Bindings.Held(Act.Details);

        // Over the load bar: before the cast, whether the drag suits the tackle; with the line out, only how far the
        // end tackle is (and in the fight the moments that need an answer). Tab shows the full readout.
        bool fighting = rod.State is RodState.Fight or RodState.Close;
        bool inHand = !rod.Holstered && rod.InHands && !rod.OnRest && !_catchPanel.Visible && !_helpOpen;
        string state;
        var colour = Ink;
        if (details)
            state = FullState(rod);
        else if (rod.State is RodState.Idle or RodState.Charging)
        {
            float dragKg = rod.DragSetting * rod.DragMax;
            state = TackleCheck.Describe(dragKg, rod.LineStrength, rod.RodMaxLoad);
            colour = VerdictColour(TackleCheck.Judge(dragKg, rod.LineStrength, rod.RodMaxLoad));
            if (rod.Method == FishingMethod.Float)
                state = $"grunt {rod.FloatDepth:0.0} m  ·  " + state;
        }
        else
        {
            state = rod.LineOut > 0.5f ? $"{rod.LineOut:0} m" : "";
            if (rod.State == RodState.Fight)
            {
                string alert = (rod.FightSlack ? "  ·  LUZ!" : "") + (rod.FishInWeeds ? rod.FishInWood ? "  ·  W GAŁĘZIACH!" : "  ·  W ZIELSKU!" : "")
                    + (rod.FishJumping ? "  ·  SKOK!" : "");
                if (alert.Length > 0)
                {
                    state += alert;
                    colour = new Color(1f, 0.55f, 0.4f);
                }
            }
        }
        SetText(_status, state);
        if (colour != _statusColour)
        {
            _statusColour = colour;
            _status.AddThemeColorOverride("font_color", colour);
        }
        Fade(_status, (inHand || details) && state.Length > 0, dt);
        _gauge.Visible = inHand || (fighting && !_catchPanel.Visible);
        _gauge.Power = rod.State == RodState.Charging ? rod.CastPower : -1f;
        _dragRing.Rod = rod;
        _gauge.Rod = rod;
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
        string fray = Core.Methods.LineWear.Word(rod.LineWearNow);
        string twist = Core.Methods.LineTwist.Word(rod.LineTwistNow);
        if (twist.Length > 0)
            fray = fray.Length > 0 ? fray + ", " + twist : twist;
        string tackle = $"{lo.Rod.DisplayName}, {lo.Reel.DisplayName}\n{lo.Line.DisplayName}{(fray.Length > 0 ? $" ({fray})" : "")}{leader}, {lo.Hook.DisplayName}{(HookWear.Word(rod.HookDullNow) is { Length: > 0 } dull ? $" ({dull}, K - osełka)" : "")}\n{lo.Bait.DisplayName} ×{lo.BaitCount}";
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


        _dev.Visible = _devOpen;
        if (_devOpen)
            SetText(_dev, DevText());
    }

    /// <summary>The full fishing readout (held Tab): the state, the method's line, the fight's warnings.</summary>
    private static string FullState(FishingRod rod)
    {
        string state = rod.StateName;
        if (rod.State is RodState.Waiting or RodState.Nibble or RodState.Bite)
            state += MethodLine(rod);
        else if (rod.State == RodState.Idle)
            state += $"  ·  {rod.Method.Title()}" + (rod.Method == FishingMethod.Float ? $", grunt {rod.FloatDepth:0.0} m" : "");
        if (rod.State == RodState.Waiting && rod.World.TimeScale > 1f)
            state += "  ·  odpoczynek na zydlu";
        if (rod.State == RodState.Fight)
            state += $"  ·  {rod.LineOut:0} m{(rod.Lifting ? "  ·  pompowanie" : "")}{(rod.FightSlack ? "  ·  LUZ! zwijaj" : "")}{(rod.FishInWeeds ? rod.FishInWood ? "  ·  W GAŁĘZIACH! podnieś wędkę (PPM)" : "  ·  W ZIELSKU! podnieś wędkę (PPM)" : "")}{(rod.FishJumping ? "  ·  SKOK! opuść wędkę" : "")}";
        return state + $"  ·  hamulec {Mathf.RoundToInt(rod.DragSetting * 100f)}%";
    }

    private static Color VerdictColour(TackleVerdict v) => v switch
    {
        TackleVerdict.TooTight => new Color(1f, 0.45f, 0.38f),
        TackleVerdict.Tight or TackleVerdict.Loose => new Color(1f, 0.85f, 0.45f),
        _ => new Color(0.62f, 0.92f, 0.6f),
    };

    /// <summary>What the line in the water is doing: the float's depth; the lure, how deep it runs over how deep a
    /// bottom and how well it is led; the feeder sinking or on the bottom, the baited spot, the rod on its rest.</summary>
    private static string MethodLine(FishingRod rod)
    {
        string rest = (rod.OnRest ? "  ·  wędka na podpórce (Z)" : "") + (rod.Slack > 0.3f ? $"  ·  luz {rod.Slack:0.0} m" : "")
            + (rod.Spooked ? "  ·  ryby spłoszone" : "")
            + (rod.FishedOut ? "  ·  miejsce wyłowione" : rod.ShoalHere ? "  ·  ławica w pobliżu" : "")
            + (rod.CastToRise ? "  ·  przy kręgu" : "")
            + (rod.CrowdingAngler ? "  ·  łowisko innego wędkarza" : "")
            + (BaitWear.Word(rod.BaitOnHook, rod.BaitFreshness) is { Length: > 0 } baitWord && rod.CastMethod != FishingMethod.Spinning ? "  ·  " + baitWord : "");
        switch (rod.CastMethod)
        {
            case Core.Methods.FishingMethod.Spinning when rod.Lure is { } lure:
                string colour = Method.ColourWord(rod.LureAppeal);
                return $"  ·  {lure.Family.Title()} {lure.Depth:0.0} m (dno {rod.CurrentDepth():0.0} m)  ·  {lure.Judgement.Title()}"
                    + (colour.Length > 0 ? $"  ·  {colour}" : "") + rest;
            case Core.Methods.FishingMethod.Bottom:
                string rig = rod.RigSettled ? $"koszyczek na dnie, {rod.CurrentDepth():0.0} m" : "koszyczek opada";
                return $"  ·  {rig}{Baited(rod.GroundbaitHeat)}{rest}";
            default:
                float water = rod.CurrentDepth();
                string hang = rod.OverDepth ? "przegruntowany: przynęta leży na dnie" : $"przynęta {System.MathF.Max(water - rod.FloatDepth, 0f):0.0} m nad dnem";
                return $"  ·  woda {water:0.0} m, grunt {rod.FloatDepth:0.0} m ({hang}){Baited(rod.GroundbaitHeat)}{rest}";
        }
    }

    /// <summary>The baited spot's strength as dots ("" when there is none).</summary>
    private static string Baited(float heat)
    {
        int dots = heat < 0.05f ? 0 : heat < 0.8f ? 1 : heat < 1.8f ? 2 : 3;
        return dots > 0 ? $"  ·  nęcone miejsce {new string('●', dots)}{new string('○', 3 - dots)}" : "";
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
        return $"KONSOLA DEV ({Bindings.Name(Act.DevConsole)})\n"
            + $"F2 natychmiastowe branie: {(rod.DevInstantBite ? "TAK" : "nie")}\n"
            + $"F3 nieśmiertelna linka: {(rod.DevUnbreakable ? "TAK" : "nie")}\n"
            + $"F4 gatunek: {forced}\nF5 reset rzutu | F6 przynęty\nF7 +3 godz. | F8 pogoda\nF9 reset postępu | F10 +1000 gr\n"
            + $"Szansa brania: {string.Join(", ", weights)}\nRyba: {fish}";
    }
}
