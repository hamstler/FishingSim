using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Core;
using FishingSim.Core.Gear;
using FishingSim.Core.Methods;
using MethodRules = FishingSim.Core.Methods.Method;
using FishingSim.Data;
using FishingSim.Progress;
using FishingSim.World;

namespace FishingSim.Fishing;

/// <summary>Close = the fish is beaten and at the surface in front of you: lift it (RMB) or net it (Space).</summary>
public enum RodState { Idle, Charging, Flying, Waiting, Nibble, Bite, Fight, Close, Landed, Broken }

/// <summary>Everything the rod needs from the rest of the game.</summary>
public sealed record RodDependencies(
    Camera3D Camera,
    Node3D RodTip,
    RodRig Rig,
    Bobber Bobber,
    IReadOnlyList<FishSpecies> Species,
    TackleLoadout Loadout,
    Profile Profile,
    WorldState World,
    LakeBed Lake,
    float WaterY,
    GearCatalog? Gear = null);

/// <summary>
/// The fishing loop as a state machine: charge, cast, wait, nibble, bite, hook, fight, land.
/// The fight maths lives in <see cref="FightModel"/> and the bite maths in <see cref="BiteModel"/>.
/// </summary>
public partial class FishingRod : Node3D
{
    private static readonly Dictionary<RodState, string> StateNames = new()
    {
        [RodState.Idle] = "Gotowy do rzutu",
        [RodState.Charging] = "Ładowanie rzutu",
        [RodState.Flying] = "Zarzucanie...",
        [RodState.Waiting] = "Czekanie na branie",
        [RodState.Nibble] = "Podgryzanie!",
        [RodState.Bite] = "BRANIE! Zacinaj (PPM)!",
        [RodState.Fight] = "Hol",
        [RodState.Close] = "Ryba przy brzegu: PPM podnieś",
        [RodState.Landed] = "Złowiona!",
        [RodState.Broken] = "Zerwanie",
    };

    private const float BaseCastMin = 6f;
    private const float BaseCastMax = 22f;
    private const float CastLoftDegrees = 12f;
    private const float BiteWaitMin = 4f;
    private const float BiteWaitMax = 12f;
    private const float HookWindow = 1.2f;
    private const float ResultSeconds = 3.5f;

    /// <summary>Chance that a live bait survives in the bucket and is not used up.</summary>
    public const float BaitBucketSaveChance = 0.35f;

    private readonly IRng _rng = new SystemRng();

    // dependencies
    private Camera3D _camera = null!;
    private Node3D _rodTip = null!;
    private Bobber _bobber = null!;
    private RodRig _rig = null!;
    private IReadOnlyList<FishSpecies> _species = Array.Empty<FishSpecies>();
    private float _waterY;

    // tackle (overwritten by ApplyTackle)
    private float _castMin = BaseCastMin;
    private float _castMax = BaseCastMax;
    private float _rodShock = 0.2f;
    private float _reelSpeed = 3f;
    private float _lineVisibility = 1f;

    // runtime
    private float _stateTime;
    private float _biteTimer;
    private float _nibbleLength;
    private float _pulseTimer;
    private float _lineOutOutsideFight;
    private FightModel? _fight;
    private ImmediateMesh _lineMesh = null!;
    private StandardMaterial3D _lineMaterial = null!;
    private Node3D _fishNode = null!;
    private FishModel? _fishModel;
    private FishModel? _heldFish;

    // methods: the lure being led (spinning) or the rig on the bottom
    private FishingMethod _castMethod = FishingMethod.Float;
    private LureMotion? _lure;
    private Vector3 _rigPos;          // where the lure runs or the feeder lies (x, z; y = the surface)
    private bool _rigSettled;
    private float _rigDepth;
    private bool _feederLoaded;
    private float _bitePull;          // the quiver tip pulled round by a bite on the bottom (0..1)
    private float _lastGroundbaitHour = float.NaN;
    private float _lureAppeal = 1f;
    private Vector3 _lastAnglerAt;
    private bool _anglerSeen;

    /// <summary>How disturbed the fish at the swim are (splashes, steps, a fight); slows the bites while it lasts.</summary>
    public Disturbance Disturbance { get; } = new();

    /// <summary>The noise scared the fish: bites come slower for a while.</summary>
    public bool Spooked => Disturbance.Spooked && State is RodState.Waiting or RodState.Nibble or RodState.Bite;

    /// <summary>The angler's steps near the bait (running, wading) and the calm coming back with time.</summary>
    private void TickDisturbance(float dt)
    {
        Disturbance.Decay(dt);
        var at = _camera.GlobalPosition;
        float speed = _anglerSeen && dt > 0f ? new Vector2(at.X - _lastAnglerAt.X, at.Z - _lastAnglerAt.Z).Length() / dt : 0f;
        _lastAnglerAt = at;
        _anglerSeen = true;
        if (State is RodState.Waiting or RodState.Nibble or RodState.Bite && speed < 12f)
        {
            var bait = _bobber.GlobalPosition;
            float dist = new Vector2(at.X - bait.X, at.Z - bait.Z).Length();
            Disturbance.Add(Disturbance.Steps(speed, dist, Lake.DepthAt(at.X, at.Z) > 0.05f, dt));
        }
    }

    // the line: a rope of points from the tip to the end tackle, as long as the line paid out
    private readonly Core.Physics.LineRope _rope = new();
    private float _lineLength;
    private bool _ropeLive;
    private bool _floatDrawn;

    /// <summary>Line paid out beyond the straight distance to the end tackle (lies on the water; reeling takes it up first).</summary>
    public float Slack => State is RodState.Waiting or RodState.Nibble or RodState.Bite ? MathF.Max(0f, _lineLength - TipDistance()) : 0f;

    /// <summary>The line's points from the tip to the end (for tests and tools).</summary>
    public Core.Physics.LineRope Rope => _rope;

    private float TipDistance() => _rodTip.GlobalPosition.DistanceTo(_bobber.GlobalPosition);

    /// <summary>
    /// Reeling in <paramref name="amount"/> metres of line: the slack lying on the water is taken up first; what is
    /// left is returned, and that much the end tackle comes in.
    /// </summary>
    private float TakeUpSlack(float amount)
    {
        float slack = _lineLength - TipDistance();
        if (slack > 0.03f && amount > 0f)
        {
            // A slack line offers no resistance: the reel takes it up twice as fast.
            float take = MathF.Min(slack - 0.02f, amount * 2f);
            _lineLength -= take;
            amount -= take / 2f;
        }
        return MathF.Max(amount, 0f);
    }

    /// <summary>After the end tackle moved: the line follows it (it never stretches).</summary>
    private void KeepLineTight(bool tight)
    {
        float d = TipDistance();
        _lineLength = tight ? d + 0.01f : MathF.Max(_lineLength, d);
    }

    private Vector3 WindVelocity => new Vector3(0.8f, 0f, 0.6f) * (World.WindTarget * 3f);

    /// <summary>The method the current tackle fishes with (a lure: spinning; bait on a bottom rod: bottom; else float).</summary>
    public FishingMethod Method => MethodRules.For(Loadout.Rod.Kind, Loadout.Bait.Kind);

    /// <summary>The method of the cast in the water (fixed when it was cast).</summary>
    public FishingMethod CastMethod => _castMethod;

    /// <summary>The lure being led (spinning), or null.</summary>
    public LureMotion? Lure => _lure;

    /// <summary>The bottom rig has reached the bottom.</summary>
    public bool RigSettled => _rigSettled;

    /// <summary>The feeder went out full of groundbait (it empties on the bottom).</summary>
    public bool FeederLoaded => _feederLoaded;

    /// <summary>Baited spots on the bottom (fed by the feeder, fading over game hours).</summary>
    public GroundbaitSpots Groundbait { get; } = new();

    /// <summary>How much groundbait works where the rig lies.</summary>
    public float GroundbaitHeat => Groundbait.HeatAt(_rigPos.X, _rigPos.Z);

    /// <summary>Shift held while reeling: the lure is drawn fast.</summary>
    public bool FastReel { get; private set; }

    /// <summary>Tests and tools: reel fast without input.</summary>
    public bool FastReelOverride { get; set; }

    public TackleLoadout Loadout { get; private set; } = null!;
    public Profile Profile { get; private set; } = null!;
    public WorldState World { get; private set; } = null!;
    public LakeBed Lake { get; private set; } = null!;

    /// <summary>The fish you can see; bites come from fish near the bait when there are any (null in minimal setups).</summary>
    public FishPopulation? Population { get; set; }

    private FishingSim.Core.FishAI.FishAgent? _biter;
    private readonly Dictionary<FishSpecies, float> _interest = new();
    private float _interestTimer;

    /// <summary>The visible fish that is taking (or fighting on) the bait, if the bite came from one.</summary>
    public FishingSim.Core.FishAI.FishAgent? Biter => _biter;

    /// <summary>Keepnets, landing nets and accessories (null in minimal setups: landing by hand, unlimited net).</summary>
    public GearCatalog? Gear { get; private set; }
    public IReadOnlyList<FishSpecies> Species => _species;
    public Bobber Bobber => _bobber;

    // dev flags (F2/F3/F4)
    public bool DevInstantBite { get; set; }
    public bool DevUnbreakable { get; set; }
    public int DevSpeciesIndex { get; set; } = -1;

    // input gating
    public bool InputBlocked { get; set; }
    public bool AllowHookKey { get; set; }
    public bool ReelOverride { get; set; }

    // public state read by the HUD
    public RodState State { get; private set; } = RodState.Idle;
    public float DragSetting { get; set; } = 0.5f;
    public float CastPower { get; private set; }
    public bool Reeling { get; private set; }
    public FishSpecies? CurrentSpecies { get; set; }
    public float CurrentWeight { get; set; }
    public string BannerText { get; private set; } = "";
    public float BannerTime { get; private set; }
    public int CatchCount { get; private set; }
    public float CatchTotalKg { get; private set; }

    public float LineStrength { get; private set; } = 6f;
    public float RodMaxLoad { get; private set; } = 7f;
    public float DragMax { get; private set; } = 5f;
    public float MaxLine { get; private set; } = 80f;

    /// <summary>Raised when a fish is landed (species, weight).</summary>
    public event Action<FishSpecies, float>? FishLanded;

    /// <summary>Sound cues for the audio engine (one-shot kind, gain).</summary>
    public event Action<Core.Audio.OneShot, float>? SoundCue;

    /// <summary>Plays a one-shot through the game's sound (the shops' coins and clicks).</summary>
    public void Cue(Core.Audio.OneShot sound, float gain = 1f) => SoundCue?.Invoke(sound, gain);

    /// <summary>True while the drag gives line to a running fish.</summary>
    public bool DragSlipping => State == RodState.Fight && (_fight?.Slipping ?? false);

    public float Tension => State == RodState.Close ? _closeTension : _fight?.Tension ?? 0f;

    // ------------------------------------------------------------------ lifting and landing
    private float _lift, _closeTension, _closeStamina, _closeLine, _liftStress, _netTime;
    private MeshInstance3D? _net;

    /// <summary>The rod is held up (right mouse button): strike, pump during the fight, lift the fish out.</summary>
    public bool Lifting { get; private set; }

    /// <summary>Tests and tools: keep the rod raised without input.</summary>
    public bool LiftOverride { get; set; }

    /// <summary>0 = beaten fish at the surface, 1 = lifted up to the angler (caught).</summary>
    public float Lift => _lift;

    /// <summary>True when a landing net is owned and equipped (Space uses it when the fish is close).</summary>
    public bool HasNet => Gear?.LandingNet != null;
    public float LineOut => _fight?.LineOut ?? _lineOutOutsideFight;
    public float FishStamina => _fight?.Stamina ?? 1f;

    /// <summary>Weakest link of the setup: the line (with its leader) or the rod, whichever gives first.</summary>
    public float EffectiveLimit => MathF.Min(LineStrength, RodMaxLoad);

    public string StateName => StateNames[State];

    public void Setup(RodDependencies deps)
    {
        _camera = deps.Camera;
        _rodTip = deps.RodTip;
        _rig = deps.Rig;
        _bobber = deps.Bobber;
        _species = deps.Species;
        Loadout = deps.Loadout;
        Profile = deps.Profile;
        World = deps.World;
        Lake = deps.Lake;
        _waterY = deps.WaterY;
        Gear = deps.Gear;
        _rope.WaterY = _waterY;
        _rope.BottomY = (x, z) => Lake.HeightAt(x, z);
        ApplyTackle();
    }

    public override void _Ready()
    {
        _lineMesh = new ImmediateMesh();
        _lineMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = _lineMesh,
            TopLevel = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _fishNode = new Node3D { TopLevel = true, Visible = false };
        AddChild(_fishNode);
    }

    // ------------------------------------------------------------------ tackle
    public void ApplyTackle()
    {
        var rod = Loadout.Rod;
        var reel = Loadout.Reel;
        var line = Loadout.Line;
        var leader = Loadout.Leader;
        RodMaxLoad = rod.MaxLoad;
        // A stretchy line soaks up part of every surge on top of the rod; a braid passes it all on.
        _rodShock = MathF.Min(rod.ShockAbsorb + 0.12f * line.Stretch, 0.5f);
        _castMin = BaseCastMin * rod.CastMult;
        _castMax = BaseCastMax * rod.CastMult;
        DragMax = reel.DragMax;
        _reelSpeed = reel.ReelSpeed * (1f + (Gear?.Accessory(AccessoryKind.ReelGrease)?.Power ?? 0f));
        MaxLine = reel.LineCapacity;
        // The leader is part of the line: the weaker of the two holds, and the fish see the leader, not the line.
        LineStrength = MathF.Min(line.Strength, leader.Strength);
        _lineVisibility = leader.Visibility > 0f ? leader.Visibility : line.Visibility;
        // Rated power: the rod's own limit; it reads as fully bent near that load.
        _bendModel = new Core.Physics.RodBendModel(RodRig.Joints * RodRig.SegmentLength, rod.MaxLoad, rod.Action);
        // Bottom and carp rods have a bright quiver tip that shows the bites.
        _rig?.SetQuiverTip(rod.Kind is RodKind.Bottom or RodKind.Carp);
        // The line floats (monofilament, braid) or sinks (fluorocarbon).
        var (sink, floats) = Core.Physics.LineRope.Behaviour((int)line.Kind);
        _rope.SinkSpeed = sink;
        _rope.Floats = floats;
        // A snapped rod is gone: nothing left in the hand until a new one is bought or handed out.
        if (_rig != null)
            _rig.Visible = Loadout.Profile == null || Loadout.Profile.Owns(TackleSlot.Rod, rod.ItemId);
    }

    private Core.Physics.RodBendModel? _bendModel;

    /// <summary>The blank's bend physics (for tests and tools).</summary>
    public Core.Physics.RodBendModel? BendModel => _bendModel;

    /// <summary>
    /// Bends the blank: the line pulls the tip towards the float (or the fish) with the current tension; with no line
    /// out only the rod's own weight sags it a little. The bend plane follows the line, so a fish running sideways
    /// bends the rod sideways.
    /// </summary>
    private void UpdateBend(float dt)
    {
        if (_bendModel == null || _rig == null)
            return;
        bool lineOut = State is RodState.Waiting or RodState.Nibble or RodState.Bite or RodState.Fight or RodState.Close;
        _bitePull = Mathf.MoveToward(_bitePull, State == RodState.Bite && _castMethod == FishingMethod.Bottom ? 1f : 0f, dt * 3f);
        float load = State is RodState.Fight or RodState.Close ? Tension
            : !lineOut ? 0.04f
            : _castMethod switch
            {
                // A lure pulls the tip as it is drawn; a snag bends the rod hard while it is pulled on.
                FishingMethod.Spinning => 0.14f + 0.3f * MathF.Min(_lure?.Speed ?? 0f, 2f)
                    + (_lure?.Snagged == true ? (Reeling ? 0.6f : 0.25f) * EffectiveLimit : 0f),
                // The line to the feeder is tight; a bite pulls the quiver tip round.
                FishingMethod.Bottom => (_rigSettled ? 0.22f : 0.1f) + _bitePull * 0.9f,
                _ => 0.12f + (_bobber.ExtraForce > 0f ? 0.35f : 0f),
            };
        Vector3 target = lineOut ? _bobber.GlobalPosition : _rig.GlobalPosition + Vector3.Down * 10f;
        var straightTip = new Vector3(0f, RodRig.BlankStart + _bendModel.Length, 0f);
        var dir = (_rig.ToLocal(target) - straightTip).Normalized();
        var perp = dir - Vector3.Up * dir.Y;
        float side = perp.Length();
        var axis = side > 1e-4f ? Vector3.Up.Cross(perp / side) : Vector3.Right;
        _bendModel.Step(dt, load, dir.Y, side);
        _rig.SetJointAngles(_bendModel.Angles, axis);
    }

    /// <summary>A kick to the blank (bites, the strike, the cast).</summary>
    private void Twitch(float strength) => _bendModel?.Impulse(strength);

    /// <summary>Rough horizontal range (m) of a full-power cast from the pier with the current rod.</summary>
    public float EstimateCastRange()
    {
        float v = _castMax * CastFactor;
        float ang = Mathf.DegToRad(CastLoftDegrees);
        const float height = 2.8f;
        float vy = v * MathF.Sin(ang);
        float t = (vy + MathF.Sqrt(vy * vy + 2f * 9.8f * height)) / 9.8f;
        return v * MathF.Cos(ang) * t;
    }

    // ------------------------------------------------------------------ bites
    public float CurrentDepth()
    {
        return Lake.DepthAt(_bobber.GlobalPosition.X, _bobber.GlobalPosition.Z);
    }

    /// <summary>A lure's size counts as hook sizes: a big lure draws big fish and puts small mouths off.</summary>
    public BiteContext MakeBiteContext(float depth) =>
        new(Loadout.Bait.ItemId, Loadout.Hook.Size + (Loadout.LureOn ? MethodRules.SizeHookShift(Loadout.Bait.SizeCm) : 0f), depth,
            Lake.Location, World.Period, World.Weather, BaitLayerAt(depth), WaterTemp);

    /// <summary>The water's temperature now (degrees Celsius).</summary>
    public float WaterTemp => WaterTemperature.At(Lake.Location.WaterTemperature, World.Hour, World.Weather);

    /// <summary>What slows or quickens every bite clock: frightened fish and the air pressure's trend.</summary>
    private float ConditionPace => Disturbance.Pace * World.Barometer.Pace;

    /// <summary>How the lure's colours suit the light and the water now (1 = neutral).</summary>
    public float LureAppeal => Loadout.LureOn
        ? MethodRules.ColourAppeal(Loadout.Bait.LureColor, Loadout.Bait.AccentColor, Loadout.Bait.Metallic,
            MethodRules.LightLevel(World.Hour, World.Weather), Lake.Location.WaterClarity)
        : 1f;

    /// <summary>The share of the rod's full cast the current lure (its weight) and line (its diameter) allow.</summary>
    public float CastFactor => MethodRules.CastFactor(Loadout.Bait.WeightG, Loadout.Line.Diameter, Loadout.LureOn);

    /// <summary>Weight of one species for the current setup at the current bobber depth.</summary>
    public float BiteWeight(FishSpecies sp) => BiteModel.Weight(sp, MakeBiteContext(CurrentDepth()));

    /// <summary>Share of bites per species for the current setup at a given depth.</summary>
    public IReadOnlyList<BiteChance> BiteChances(float depth) => BiteModel.Chances(_species, MakeBiteContext(depth));

    public float Attraction() => BiteModel.Attraction(_species, MakeBiteContext(CurrentDepth()));

    // ------------------------------------------------------------------ input
    /// <summary>True while another tool is in the hands (shovel, insect net): the rod is put away, it does nothing.</summary>
    public bool Holstered { get; set; }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Holstered)
            return;
        if (InputBlocked)
        {
            if (AllowHookKey && @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
                TryHook();
            return;
        }

        if (@event is InputEventMouseButton mb)
        {
            if (Input.MouseMode != Input.MouseModeEnum.Captured)
            {
                if (mb.Pressed)
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                return;
            }
            switch (mb.ButtonIndex)
            {
                case MouseButton.Left when mb.Pressed && State == RodState.Idle:
                    TryStartCharge();
                    break;
                case MouseButton.Left when !mb.Pressed && State == RodState.Charging:
                    Cast();
                    break;
                case MouseButton.Right when mb.Pressed:
                    TryHook();
                    break;
                case MouseButton.WheelUp when mb.CtrlPressed:
                    ChangeFloatDepth(0.1f);
                    break;
                case MouseButton.WheelDown when mb.CtrlPressed:
                    ChangeFloatDepth(-0.1f);
                    break;
                case MouseButton.WheelUp:
                    DragSetting = Math.Clamp(DragSetting + 0.05f, 0f, 1f);
                    break;
                case MouseButton.WheelDown:
                    DragSetting = Math.Clamp(DragSetting - 0.05f, 0f, 1f);
                    break;
            }
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            HandleKey(key);
        }
    }

    private void HandleKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Space:
                TryNet();
                break;
            case Key.Bracketright:
                ChangeFloatDepth(key.ShiftPressed ? 0.5f : 0.1f);
                break;
            case Key.Bracketleft:
                ChangeFloatDepth(key.ShiftPressed ? -0.5f : -0.1f);
                break;
            case Key.Z:
                if (OnRest)
                {
                    if (NearRest)
                        PickUpRod();
                    else
                        ShowBanner("Podejdź do wędki, aby ją podnieść", 2f);
                }
                else
                    PutOnRest();
                break;
            case Key.R when State == RodState.Landed && AwaitingDecision:
                ReleaseCatch();
                break;
            case Key.E when State == RodState.Landed && AwaitingDecision:
                KeepCatch();
                break;
            case Key.F2:
                DevInstantBite = !DevInstantBite;
                break;
            case Key.F3:
                DevUnbreakable = !DevUnbreakable;
                if (_fight != null)
                    _fight.Unbreakable = DevUnbreakable;
                break;
            case Key.F4:
                DevSpeciesIndex++;
                if (DevSpeciesIndex >= _species.Count)
                    DevSpeciesIndex = -1;
                break;
            case Key.F5:
                ResetToIdle();
                break;
            case Key.F6:
                Loadout.Refill();
                break;
            case Key.F7:
                World.Advance(3f);
                break;
            case Key.F8:
                World.CycleWeather();
                break;
            case Key.F9:
                Profile.Reset();
                Loadout.Attach(Profile);
                ApplyTackle();
                Profile.NotifyChanged();
                ShowBanner("Postęp zresetowany (dev)");
                break;
            case Key.F10:
                Profile.Money += 1000;
                Profile.NotifyChanged();
                break;
            case Key.L when State == RodState.Idle:
                if (Lake.CycleLocation(Profile.Level))
                {
                    ShowBanner("Łowisko: " + Lake.Location.DisplayName);
                    Profile.NotifyChanged();
                }
                else
                {
                    ShowBanner("Kolejne łowisko odblokujesz na wyższym poziomie");
                }
                break;
            case >= Key.Key1 and <= Key.Key6 when State == RodState.Idle:
                Loadout.Cycle((TackleSlot)(key.Keycode - Key.Key1), key.ShiftPressed ? -1 : 1);
                ApplyTackle();
                Profile.NotifyChanged();
                break;
        }
    }

    // ------------------------------------------------------------------ main loop
    public override void _PhysicsProcess(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("rod");
        if (_bobber == null)
            return;
        float dt = (float)delta;
        bool captured = !InputBlocked && !Holstered && Input.MouseMode == Input.MouseModeEnum.Captured;
        // A rod on the rest is not in the hands: nothing is reeled or lifted.
        Reeling = !OnRest && (ReelOverride || (captured && Input.IsMouseButtonPressed(MouseButton.Left)));
        Lifting = !OnRest && (LiftOverride || (captured && Input.IsMouseButtonPressed(MouseButton.Right)));
        FastReel = FastReelOverride || (captured && Input.IsKeyPressed(Key.Shift));
        // The groundbait on the bottom is eaten up as the game's hours pass.
        float hour = World.Hour;
        if (!float.IsNaN(_lastGroundbaitHour))
        {
            float passed = hour - _lastGroundbaitHour;
            if (passed < 0f)
                passed += 24f;
            if (passed > 0f && passed < 12f)
                Groundbait.Decay(passed);
        }
        _lastGroundbaitHour = hour;
        BannerTime = MathF.Max(BannerTime - dt, 0f);
        UpdateBaitAttraction(dt);
        TickDisturbance(dt);
        _stateTime += dt;
        switch (State)
        {
            case RodState.Charging: CastPower = MathF.Min(CastPower + dt / 1.2f, 1f); break;
            case RodState.Flying: TickFlying(); break;
            case RodState.Waiting: TickWaiting(dt); break;
            case RodState.Nibble: TickNibble(dt); break;
            case RodState.Bite: TickBite(); break;
            case RodState.Fight: TickFight(dt); break;
            case RodState.Close: TickClose(dt); break;
            case RodState.Landed:
            case RodState.Broken: TickResult(); break;
        }
        TickNet(dt);
    }

    /// <summary>The blank's bend and the line are drawn every rendered frame (the tip moves with the camera each frame;
    /// drawn only on physics ticks the line would judder on screens faster than the tick).</summary>
    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("rod-draw");
        if (_bobber == null)
            return;
        UpdateBend(Math.Min((float)delta, 0.05f));
        UpdateLine(Math.Min((float)delta, 0.05f));
    }

    /// <summary>While the float is in the water, nearby fish that like this bait (at this depth and hour) swim to it.</summary>
    private void UpdateBaitAttraction(float dt)
    {
        if (Population == null)
            return;
        bool baitIn = State is RodState.Waiting or RodState.Nibble or RodState.Bite;
        if (!baitIn)
        {
            Population.SetBait(null, null);
            _interestTimer = 0f;
            return;
        }
        _interestTimer -= dt;
        if (_interestTimer <= 0f)
        {
            _interestTimer = 0.5f;
            var ctx = MakeBiteContext(CurrentDepth());
            _interest.Clear();
            foreach (var sp in _species)
                _interest[sp] = BiteModel.Weight(sp, ctx);
            // Fish gather over groundbait.
            float baited = _castMethod == FishingMethod.Bottom ? GroundbaitSpots.PaceFor(GroundbaitHeat) : 1f;
            Population.SetBait(_bobber.GlobalPosition, sp => Math.Clamp(_interest.GetValueOrDefault(sp) * 0.8f * baited, 0f, 1.2f));
        }
    }

    /// <summary>The fish that was interested lets go: it swims off (a bite that came from a visible fish).</summary>
    private void LoseBiter(Vector3? at = null)
    {
        if (_biter != null)
            Population?.Release(_biter, at);
        _biter = null;
    }

    private void SetState(RodState s)
    {
        State = s;
        _stateTime = 0f;
        // Whatever a bite did to the float ends with it.
        if (s is not (RodState.Nibble or RodState.Bite) && _bobber != null)
        {
            _bobber.Tilt = 0f;
            _bobber.DriveVelocity = Vector3.Zero;
            if (_bobber.ExtraForce < 0f)
                _bobber.ExtraForce = 0f;
        }
    }

    // ------------------------------------------------------------------ the float's depth setting ("grunt")
    /// <summary>How deep under the float the bait hangs (m). Set with [ and ] (or Ctrl + wheel) before the cast.</summary>
    public float FloatDepth { get; private set; } = 1.2f;

    public const float FloatDepthMin = 0.2f, FloatDepthMax = 6f;

    public void SetFloatDepth(float metres) => FloatDepth = Math.Clamp(MathF.Round(metres * 10f) / 10f, FloatDepthMin, FloatDepthMax);

    /// <summary>The float is set deeper than the water under it: the bait lies on the bottom, the float lies flat.</summary>
    public bool OverDepth => _castMethod == FishingMethod.Float && State is RodState.Waiting or RodState.Nibble or RodState.Bite
        && FloatDepth > CurrentDepth() + 0.15f;

    /// <summary>Where the bait is in the water column over water this deep (1 = on the bottom, 0 = at the surface);
    /// null when it cannot be told (a lure not in the water).</summary>
    public float? BaitLayerAt(float depth)
    {
        bool cast = State is RodState.Waiting or RodState.Nibble or RodState.Bite;
        switch (cast ? _castMethod : Method)
        {
            case FishingMethod.Bottom:
                return 1f;
            case FishingMethod.Spinning:
                return cast && _lure != null ? FeedingHabits.BaitLayer(_lure.Depth, depth) : null;
            default:
                return FeedingHabits.BaitLayer(FloatDepth, depth);
        }
    }

    private void ChangeFloatDepth(float by)
    {
        if (State != RodState.Idle)
        {
            ShowBanner("Grunt zmienia się przed rzutem: zwiń zestaw", 2f);
            return;
        }
        SetFloatDepth(FloatDepth + by);
        ShowBanner($"Grunt spławika: {FloatDepth:0.0} m", 1.5f);
    }

    /// <summary>Shows a line in the banner (camp tools, cooking).</summary>
    public void Notify(string text, float seconds = 3f) => ShowBanner(text, seconds);

    private void ShowBanner(string text, float seconds = 3f)
    {
        BannerText = text;
        BannerTime = seconds;
    }

    private Vector3 Origin()
    {
        // The line runs from the rod: in the hands, or on its rest.
        var p = _rest?.GlobalPosition ?? _camera.GlobalPosition;
        p.Y = _waterY;
        return p;
    }

    private float FlatDistance(Vector3 p)
    {
        var o = Origin();
        return new Vector2(p.X - o.X, p.Z - o.Z).Length();
    }

    public void ResetToIdle()
    {
        // Walking away from an undecided catch puts it back into the water.
        if (AwaitingDecision)
        {
            AwaitingDecision = false;
            Profile.Release();
        }
        LoseBiter();
        ClearHeldFish();
        _bobber.Stow();
        _fishNode.Visible = false;
        _fight = null;
        _lineOutOutsideFight = 0f;
        _lure = null;
        _rigSettled = false;
        _bitePull = 0f;
        _feederLoaded = false;
        _bobber.LureSpeed = 0f;
        _floatDrawn = false;
        _lineLength = 0f;
        PickUpRod();
        SetState(RodState.Idle);
    }

    // ------------------------------------------------------------------ casting
    private void Cast()
    {
        Twitch(-3f);   // the blank whips forward as the cast is released
        var basis = _camera.GlobalTransform.Basis;
        var aim = (-basis.Z).Rotated(basis.X.Normalized(), Mathf.DegToRad(CastLoftDegrees));
        float speed = Mathf.Lerp(_castMin, _castMax, CastPower) * CastFactor;
        // What flies: a float, a lure, or a feeder that takes one portion of the bait as groundbait when there is some
        // to spare.
        _castMethod = Method;
        _bobber.SetTerminal(_castMethod, Loadout.Bait);
        _feederLoaded = _castMethod == FishingMethod.Bottom && Loadout.BaitCount >= 2;
        if (_feederLoaded)
            Loadout.UseBait();
        _bobber.FeederLoaded = _feederLoaded;
        _bobber.Launch(_rodTip.GlobalPosition, aim * speed);
        // The bail is open: the line pays out behind the end tackle as it flies.
        _lineLength = 0.3f;
        _ropeLive = false;
        _floatDrawn = false;
        SetState(RodState.Flying);
    }

    /// <summary>LMB in the idle state: starts charging a cast, unless the kit or the bait is missing (then says why).</summary>
    public bool TryStartCharge()
    {
        if (State != RodState.Idle)
            return false;
        if (!Loadout.Ready)
        {
            var missing = string.Join(", ", Profile.MissingKit().Select(m => m.MissingName()));
            ShowBanner($"Brak sprzętu: {missing}. Kram wędkarski: kup nowy, a bez pieniędzy odbierz darmowy zestaw.", 4f);
            return false;
        }
        if (Loadout.BaitCount <= 0)
        {
            ShowBanner("Brak przynęty! Zmień przynętę (klawisz 5)");
            return false;
        }
        CastPower = 0f;
        SetState(RodState.Charging);
        return true;
    }

    /// <summary>Starts a cast with the given power without input (tests, tools).</summary>
    public void CastNow(float power)
    {
        CastPower = power;
        SetState(RodState.Charging);
        Cast();
    }

    private void TickFlying()
    {
        _lineOutOutsideFight = FlatDistance(_bobber.GlobalPosition);
        // The line pays out a little ahead of the end tackle's flight (it trails behind it in an arc).
        _lineLength = MathF.Max(_lineLength, TipDistance() * 1.04f);
        if (_bobber.Submerged > 0.5f)
        {
            SoundCue?.Invoke(Core.Audio.OneShot.Splash, Math.Clamp(CastPower + 0.4f, 0.5f, 1f));
            // The splash puts the fish off: a heavy lure or a feeder into shallow water most of all.
            float splash = Disturbance.Splash(_castMethod, Loadout.Bait.WeightG, CurrentDepth());
            Disturbance.Add(splash);
            if (splash > 0.2f)
                Population?.Scare(_bobber.GlobalPosition, 3f + splash * 6f, 2f + splash * 4f);
            if (_castMethod != FishingMethod.Float)
                EnterWater();
            // The bail closes: the line that flew out lies on the water with some slack (less behind a lure: the
            // angler stops the line with a finger as it lands).
            _lineLength = TipDistance() * (_castMethod == FishingMethod.Spinning ? 1.02f : 1.07f);
            NewBiteTimer();
            SetState(RodState.Waiting);
        }
        else if (_stateTime > 1f && _bobber.LinearVelocity.Length() < 0.5f && _bobber.Submerged < 0.1f)
        {
            ShowBanner("Rzut na ląd - spróbuj ponownie");
            ResetToIdle();
        }
        else if (_bobber.GlobalPosition.Y < _waterY - 6f || _stateTime > 8f)
        {
            ResetToIdle();
        }
    }

    // ------------------------------------------------------------------ waiting / bite
    private void NewBiteTimer() =>
        _biteTimer = DevInstantBite
            ? 0.5f
            : BiteModel.WaitSeconds(_rng, BiteWaitMin, BiteWaitMax, _lineVisibility, Attraction())
              / (1f + (Profile.ActiveMeal?.BiteBonus ?? 0f));   // a well-fed angler waits a little less

    private void TickWaiting(float dt)
    {
        if (_castMethod == FishingMethod.Spinning)
        {
            TickSpinning(dt);
            return;
        }
        if (_castMethod == FishingMethod.Bottom)
        {
            TickBottom(dt);
            return;
        }
        _lineOutOutsideFight = FlatDistance(_bobber.GlobalPosition);
        if (Reeling)
        {
            // LMB: the slack is taken up first, then the float is drawn over the water towards the angler, through
            // the shallows up to the bank (Shift: faster); it comes out of the water at the bank or under the rod.
            float pull = TakeUpSlack(_reelSpeed * (FastReel ? 0.6f : 0.35f) * dt);
            if (pull > 0f)
            {
                var p = _bobber.GlobalPosition;
                var to = Origin() - p;
                to.Y = 0f;
                if (to.LengthSquared() > 1e-4f)
                    p += to.Normalized() * pull;
                p.Y = _waterY + (_bobber.WaterHeight?.Invoke(p.X, p.Z) ?? 0f) - 0.01f;
                _bobber.Hold(p);
                _floatDrawn = true;
                KeepLineTight(true);
                if (Lake.DepthAt(p.X, p.Z) <= 0.03f || FlatDistance(p) < 1.6f)
                {
                    ShowBanner("Zestaw wyjęty z wody", 1.2f);
                    ResetToIdle();
                    return;
                }
            }
        }
        else
        {
            // Let go: the float rides the water by itself again.
            if (_floatDrawn)
            {
                _bobber.Release();
                _floatDrawn = false;
            }
            _bobber.DriveVelocity = Vector3.Zero;
            // The wind pushes the float along (the tight line swings it round in an arc); set too deep it lies flat.
            _bobber.Drift = WindVelocity * 0.02f;
            _bobber.Tilt = OverDepth ? 1f : 0f;
            // The bail is closed: once the line is tight it holds the float (it slides no further out).
            if (TipDistance() >= _lineLength && !_bobber.Freeze)
            {
                var away = _bobber.GlobalPosition - _rodTip.GlobalPosition;
                away.Y = 0f;
                if (away.LengthSquared() > 1e-4f)
                {
                    away = away.Normalized();
                    float outward = _bobber.LinearVelocity.Dot(away);
                    if (outward > 0f)
                        _bobber.LinearVelocity -= away * outward;
                }
            }
            // A fish pulling the float under may take a little line.
            if (TipDistance() > _lineLength + 0.3f)
                KeepLineTight(false);
        }
        // A lure only fishes while it moves: the clock to the next bite runs while it is reeled in (faster on a spinning
        // rod), and barely at all while it lies still.
        float pace = 1f;
        if (Loadout.LureOn)
            pace = Reeling ? (Loadout.Rod.Kind == RodKind.Spinning ? 3.2f : 2.4f) : 0.1f;
        _biteTimer -= dt * pace * ConditionPace;
        if (_biteTimer <= 0f)
            StartNibble();
    }

    /// <summary>Chooses the fish that bites. Returns false when nothing here wants this bait and hook.</summary>
    private bool PickFish()
    {
        if (_species.Count == 0)
            return false;
        float hookSize = Loadout.Hook.Size;
        _biter = null;
        if (DevSpeciesIndex >= 0 && DevSpeciesIndex < _species.Count)
        {
            CurrentSpecies = _species[DevSpeciesIndex];
        }
        else
        {
            // A fish you can see near the float takes it if one wants this bait; otherwise one from out of sight.
            var ctx = MakeBiteContext(CurrentDepth());
            var agent = Population?.PickBiter(_bobber.GlobalPosition, 7f, sp => BiteModel.Weight(sp, ctx));
            if (agent != null && Population!.SpeciesOf(agent) is FishSpecies seen)
            {
                _biter = agent;
                CurrentSpecies = seen;
                CurrentWeight = agent.Weight;
                return true;
            }
            CurrentSpecies = BiteModel.Pick(_species, ctx, _rng);
        }
        if (CurrentSpecies == null)
            return false;
        CurrentWeight = BiteModel.RollWeight(CurrentSpecies, hookSize, _rng);
        return true;
    }

    private void StartNibble()
    {
        if (!PickFish())
        {
            NewBiteTimer();   // nothing here wants this bait/hook, keep waiting
            return;
        }
        // The live-bait bucket keeps spare baitfish alive: sometimes the used one goes back in.
        bool spared = Loadout.Bait.ItemId == "livebait" && Gear?.Has(AccessoryKind.BaitBucket) == true && _rng.NextFloat() < BaitBucketSaveChance;
        if (!spared)
            Loadout.UseBait();
        _bobber.DriveVelocity = Vector3.Zero;
        _bobber.LureSpeed = 0f;
        // A float bobs for a while, a lure is hit in one knock, the quiver tip plucks a few times.
        _nibbleLength = _castMethod switch
        {
            FishingMethod.Spinning => _rng.Range(0.15f, 0.4f),
            FishingMethod.Bottom => _rng.Range(1.2f, 3f),
            _ => _rng.Range(1f, 2.2f),
        };
        // The bell on the rod rest rings when a fish touches the bait.
        if (Gear?.Has(AccessoryKind.RodPod) == true && _castMethod != FishingMethod.Spinning)
        {
            SoundCue?.Invoke(Core.Audio.OneShot.Bell, 1f);
            (_rest as RodRest)?.Ring();
        }
        _pulseTimer = 0f;
        if (CurrentSpecies != null)
        {
            Graphics.FishSkin.Prewarm(CurrentSpecies);     // paint the close-up skin while the float twitches
            FishAsset.Prefetch(CurrentSpecies);            // and decode the species' model pictures
        }
        if (_castMethod == FishingMethod.Spinning)
        {
            Twitch(1f);   // the knock of the hit
            // A predator hitting a lure that is being reeled often hooks itself on it.
            if (CurrentSpecies != null && _rng.NextFloat() < MethodRules.SelfHookChance(CurrentSpecies.Aggression, Reeling))
            {
                ShowBanner("Uderzenie! Ryba sama zacięła się na prowadzonej przynęcie", 2.5f);
                StartFight();
                return;
            }
        }
        SetState(RodState.Nibble);
    }

    private void TickNibble(float dt)
    {
        _pulseTimer -= dt;
        if (_pulseTimer <= 0f)
        {
            switch (_castMethod)
            {
                case FishingMethod.Bottom:
                    // Plucks on the quiver tip.
                    _pulseTimer = _rng.Range(0.35f, 0.8f);
                    Twitch(0.45f);
                    break;
                case FishingMethod.Spinning:
                    _pulseTimer = 1f;
                    break;
                default:
                    FloatNibble();
                    break;
            }
        }
        if (_stateTime >= _nibbleLength)
        {
            if (_castMethod == FishingMethod.Float)
                FloatBite();
            else if (_castMethod == FishingMethod.Bottom && Gear?.Has(AccessoryKind.RodPod) == true)
            {
                SoundCue?.Invoke(Core.Audio.OneShot.Bell, 1f);
                (_rest as RodRest)?.Ring();
            }
            Twitch(_castMethod == FishingMethod.Spinning ? 1.8f : 1.2f);
            SetState(RodState.Bite);
        }
    }

    /// <summary>One sign of a fish at the bait under the float, the way its species takes: dips, quick plucks of
    /// small fish, the float rising as a bream lifts the bait off the bottom, or sliding off with a carp or a pike.</summary>
    private void FloatNibble()
    {
        var away = _bobber.GlobalPosition - Origin();
        away.Y = 0f;
        away = away.LengthSquared() > 1e-4f ? away.Normalized() : Vector3.Forward;
        switch (FeedingHabits.StyleOf(CurrentSpecies?.SpeciesName ?? ""))
        {
            case BiteStyle.Pluck:
                _pulseTimer = _rng.Range(0.12f, 0.3f);
                _bobber.ApplyCentralImpulse(Vector3.Down * _bobber.Mass * 0.6f);
                Twitch(0.3f);
                break;
            case BiteStyle.Lift:
                _pulseTimer = _rng.Range(0.5f, 0.9f);
                _bobber.ExtraForce = -0.45f;
                _bobber.Tilt = MathF.Min(_bobber.Tilt + 0.25f, 0.6f);
                Twitch(0.3f);
                break;
            case BiteStyle.Run:
                _pulseTimer = _rng.Range(0.4f, 0.8f);
                _bobber.DriveVelocity = away.Rotated(Vector3.Up, _rng.Range(-0.8f, 0.8f)) * 0.35f;
                _bobber.ApplyCentralImpulse(Vector3.Down * _bobber.Mass * 0.5f);
                Twitch(0.5f);
                break;
            default:
                _pulseTimer = _rng.Range(0.3f, 0.7f);
                _bobber.ApplyCentralImpulse(Vector3.Down * _bobber.Mass * 1.2f);
                Twitch(0.6f);
                break;
        }
    }

    /// <summary>The take itself under the float: under it goes, or (a lift bite) it lies flat on the water.</summary>
    private void FloatBite()
    {
        switch (FeedingHabits.StyleOf(CurrentSpecies?.SpeciesName ?? ""))
        {
            case BiteStyle.Lift:
                _bobber.ExtraForce = -0.7f;
                _bobber.Tilt = 1f;
                break;
            case BiteStyle.Run:
                var away = _bobber.GlobalPosition - Origin();
                away.Y = 0f;
                _bobber.DriveVelocity = (away.LengthSquared() > 1e-4f ? away.Normalized() : Vector3.Forward) * 0.9f;
                _bobber.ExtraForce = 1.3f;
                break;
            case BiteStyle.Pluck:
                _bobber.ExtraForce = 1.2f;
                break;
            default:
                _bobber.ExtraForce = 1.6f;
                break;
        }
    }

    private void TickBite()
    {
        if (_stateTime < MethodRules.HookWindow(_castMethod))
            return;
        _bobber.ExtraForce = 0f;
        LoseBiter(_bobber.GlobalPosition);
        ShowBanner("Za późno - ryba się zerwała");
        NewBiteTimer();
        SetState(RodState.Waiting);
    }

    public void TryHook()
    {
        // A rod on its rest has to be picked up first, and it is in reach only from close by.
        if (OnRest && State is RodState.Nibble or RodState.Bite)
        {
            if (!NearRest)
            {
                ShowBanner("Za daleko od wędki! Podejdź do podpórki", 2f);
                return;
            }
            PickUpRod();
        }
        switch (State)
        {
            case RodState.Waiting when _castMethod == FishingMethod.Spinning && _lure != null:
                JerkLure();
                break;
            case RodState.Nibble when _castMethod == FishingMethod.Spinning:
                // The knock of a hit on a lure is the moment to strike.
                goto case RodState.Bite;
            case RodState.Nibble:
                _bobber.ExtraForce = 0f;
                LoseBiter(_bobber.GlobalPosition);
                ShowBanner("Za wcześnie! Ryba uciekła");
                NewBiteTimer();
                SetState(RodState.Waiting);
                break;
            case RodState.Bite:
                // A badly sized hook loses fish; a sharpened hook and a braid that does not stretch set it better.
                float chance = (0.5f + 0.5f * BiteModel.HookFit(CurrentSpecies!, Loadout.Hook.Size))
                    * (1f + (Loadout.Line.Kind == LineKind.Braid ? 0.1f : 0f) + (Gear?.Accessory(AccessoryKind.HookHone)?.Power ?? 0f))
                    * (OverDepth ? FeedingHabits.OverDepthHookFactor : 1f);   // a float set too deep: slack between it and the hook
                if (DevInstantBite || _rng.NextFloat() <= chance)
                {
                    StartFight();
                }
                else
                {
                    _bobber.ExtraForce = 0f;
                    LoseBiter(_bobber.GlobalPosition);
                    ShowBanner("Ryba spadła z haka");
                    NewBiteTimer();
                    SetState(RodState.Waiting);
                }
                break;
        }
    }

    // ------------------------------------------------------------------ fight
    private void StartFight()
    {
        var species = CurrentSpecies!;
        _bobber.ExtraForce = 0f;
        _bobber.DriveVelocity = Vector3.Zero;
        // The lure or the feeder goes with the fish; only a float stays in sight on the line.
        _bobber.TerminalVisible = _castMethod == FishingMethod.Float;
        _lure = null;
        _bitePull = 0f;
        var to = _bobber.GlobalPosition - Origin();
        to.Y = 0f;
        var gear = new FightGear(DragMax, EffectiveLimit, _rodShock, _reelSpeed, MaxLine);
        _fight = new FightModel(FishProfile.From(species, CurrentWeight), gear, _rng, to.Length(), MathF.Atan2(to.X, to.Z), DragSetting)
        {
            Unbreakable = DevUnbreakable,
        };
        SoundCue?.Invoke(Core.Audio.OneShot.Strike, 1f);
        Disturbance.Add(0.3f);     // a hooked fish thrashing disturbs the swim
        Twitch(2.5f);
        if (_biter != null)
            Population?.Hook(_biter);
        _fishModel?.QueueFree();
        _fishModel = FishModel.Create(species, CurrentWeight, detailed: true);
        _fishNode.AddChild(_fishModel);
        _fishNode.Visible = true;
        _fishDepth = 0.3f;
        _jump = 0f;
        _fishYaw = _fight.Heading;
        SetState(RodState.Fight);
    }

    private void TickFight(float dt)
    {
        if (_fight == null)
            return;
        var outcome = _fight.Step(dt, DragSetting, Reeling, Lifting);
        switch (outcome)
        {
            case FightOutcome.LineBroke:
                if (RodMaxLoad < LineStrength)
                    BreakLine("Wędka się złamała!", TackleSlot.Rod);
                else
                    BreakLine("Linka pękła! Haczyk został w rybie.", TackleSlot.Hook);
                return;
            case FightOutcome.SpooledOut:
                BreakLine("Ryba wyciągnęła całą linkę! Żyłka przepadła.", TackleSlot.Line);
                return;
            case FightOutcome.Landed:
                EnterClose();
                return;
        }

        // Teeth: a pike (or a wels, a zander) may bite through anything but a wire leader.
        float bite = CurrentSpecies!.BiteOff * (1f - Loadout.Leader.BiteProof);
        if (bite > 0f && !DevUnbreakable && _rng.NextFloat() < bite * dt)
        {
            BreakLine($"{CurrentSpecies.SpeciesName} przegryzł linkę! Przyda się przypon z drutu.", TackleSlot.Hook);
            return;
        }

        // Fish position and visuals: where the fight model has it, facing the way it swims.
        var pos = Origin() + new Vector3(_fight.Position.X, 0f, _fight.Position.Y);
        _bobber.Hold(new Vector3(pos.X, _waterY - 0.02f, pos.Z));
        if (_fight.BurstStarted)
            Twitch(0.6f + 1.2f * Math.Clamp(_fight.PeakPull / MathF.Max(EffectiveLimit, 0.1f), 0f, 1f));   // the rod kicks with every surge
        // A fresh fish bores deep and only comes up as it tires; runs take it down, rests let it rise.
        float floor = MathF.Min(CurrentDepth() * 0.75f, 2.4f);
        float wantDepth = Mathf.Lerp(0.2f, floor, MathF.Pow(_fight.Stamina, 0.7f)) * (_fight.Running ? 1f : 0.7f);
        _fishDepth = Mathf.MoveToward(_fishDepth, MathF.Max(wantDepth, 0.15f), 0.8f * dt);
        float fishY = _waterY - _fishDepth;

        // Surges: head shakes, and the jumpers (pike, asp, trout, salmon) leap clear of the water.
        var sp = CurrentSpecies!;
        bool jumper = sp.Aggression >= 0.8f && sp.BurstMult >= 1.8f;
        if (_fight.BurstStarted && _jump <= 0f && jumper && _fight.Stamina > 0.25f && _rng.NextFloat() < 0.4f)
        {
            _jump = 0.0001f;
            SoundCue?.Invoke(Core.Audio.OneShot.Splash, 0.7f);
        }
        float pitch = 0f;
        if (_jump > 0f)
        {
            _jump += dt / 0.9f;
            float leap = MathF.Sin(MathF.Min(_jump, 1f) * MathF.PI);
            fishY = Mathf.Lerp(fishY, _waterY + 0.5f + 0.5f * Math.Clamp(CurrentWeight / 6f, 0f, 1f), leap);
            pitch = Mathf.Lerp(-0.9f, 0.9f, MathF.Min(_jump, 1f));
            if (_jump >= 1f)
            {
                _jump = 0f;
                SoundCue?.Invoke(Core.Audio.OneShot.Splash, 0.9f);
            }
        }
        else if (_fight.BurstStarted && _fishDepth < 0.5f)
        {
            SoundCue?.Invoke(Core.Audio.OneShot.SmallSplash, 0.6f);   // a tired fish thrashes at the surface
        }
        // The body follows its heading smoothly; head shakes during surges, a slight tail-beat sway otherwise.
        _fishYaw = Mathf.LerpAngle(_fishYaw, _fight.Heading, Math.Clamp(6f * dt, 0f, 1f));
        float shake = _fight.Bursting ? MathF.Sin(_stateTime * 17f) * 0.3f : 0f;
        float sway = MathF.Sin(_stateTime * (4f + 6f * _fight.Speed)) * 0.06f;
        _fishNode.GlobalPosition = new Vector3(pos.X, fishY, pos.Z);
        _fishNode.Rotation = new Vector3(pitch, _fishYaw + sway + shake, _fight.Bursting ? shake * 0.5f : 0f);
        if (_fishModel != null)
            _fishModel.Wiggle = Math.Clamp(0.5f + _fight.Speed * 1.2f, 0.5f, 2.8f) * (0.4f + 0.6f * _fight.Stamina);
    }

    private float _fishDepth = 0.3f;
    private float _fishYaw;
    private float _jump;

    // ------------------------------------------------------------------ fish at the surface: lift or net
    private void EnterClose()
    {
        _closeStamina = _fight!.Stamina;
        _closeLine = MathF.Min(_fight.LineOut, FightModel.LandDistance);
        _lift = 0f;
        _liftStress = 0f;
        SetState(RodState.Close);
        ShowBanner(HasNet ? "Ryba przy brzegu! PPM - podnieś, Spacja - kasarek" : "Ryba przy brzegu! Przytrzymaj PPM, aby ją podnieść", 2.5f);
    }

    private Vector3 TouchPoint() => _camera.ToGlobal(new Vector3(0.05f, -0.2f, -0.55f));

    private void TickClose(float dt)
    {
        var species = CurrentSpecies!;
        _closeStamina = MathF.Min(_closeStamina + 0.015f * dt, 1f);
        _lift = LiftModel.Step(_lift, Lifting, CurrentWeight, _closeStamina, dt);
        float target = Lifting || _lift > 0f
            ? LiftModel.Tension(CurrentWeight, _closeStamina, _lift)
            : CurrentWeight * 0.25f * (0.4f + _closeStamina);
        _closeTension = Mathf.MoveToward(_closeTension, target, 25f * dt);

        // Too heavy to lift on this tackle: the line (or the rod) gives.
        float ratio = _closeTension / EffectiveLimit;
        _liftStress = ratio > 1f ? _liftStress + (ratio - 1f) * 3f * dt : MathF.Max(_liftStress - 0.5f * dt, 0f);
        if (!DevUnbreakable && (_liftStress >= 1f || ratio > 1.6f))
        {
            if (RodMaxLoad < LineStrength)
                BreakLine("Wędka pękła pod ciężarem ryby!", TackleSlot.Rod);
            else
                BreakLine("Linka pękła przy podnoszeniu! Użyj kasarka.", TackleSlot.Hook);
            return;
        }

        // The fish swings from the surface up to the angler's hand.
        float angle = _fight?.FishAngle ?? 0f;
        var water = Origin() + new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle)) * _closeLine + Vector3.Down * 0.05f;
        float t = _lift * _lift * (3f - 2f * _lift);
        var pos = water.Lerp(TouchPoint(), t) + Vector3.Up * MathF.Sin(t * MathF.PI) * 0.5f;
        _fishNode.GlobalPosition = pos;
        _fishNode.Rotation = new Vector3(-t * 1.3f, angle + MathF.Sin(_stateTime * 9f) * 0.25f * (1f - t * 0.5f), 0f);
        // The float hangs on the line between the rod tip and the fish.
        _bobber.Hold(pos.Lerp(_rodTip.GlobalPosition, 0.3f + 0.2f * t));
        if (_fishModel != null)
            _fishModel.Wiggle = 0.6f + 1.4f * _closeStamina;

        if (_lift >= 1f)
            LandFish();
    }

    /// <summary>Space: scoop the fish with the landing net. Does nothing without a net or when the fish is not close.</summary>
    public void TryNet()
    {
        var net = Gear?.LandingNet;
        if (net == null || State != RodState.Close || _netTime > 0f)
            return;
        ShowNet(_fishNode.GlobalPosition);
        if (!DevUnbreakable && LandingModel.Resolve(_rng, CurrentWeight, net.Spec, _closeStamina) == LandingOutcome.Escaped)
        {
            BreakLine(CurrentWeight > net.MaxWeight ? "Ryba nie zmieściła się w kasarku i uciekła!" : "Ryba wyślizgnęła się z kasarka!");
            return;
        }
        LandFish();
    }

    /// <summary>A simple landing net (handle, hoop, mesh) that swings in under the fish and fades away.</summary>
    private void ShowNet(Vector3 at)
    {
        _net?.QueueFree();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.AppendFrom(new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.02f, Height = 1.6f, RadialSegments = 6 }, 0,
            new Transform3D(Basis.FromEuler(new Vector3(0, 0, 1.2f)), new Vector3(0.75f, 0.35f, 0)));
        st.AppendFrom(new TorusMesh { InnerRadius = 0.26f, OuterRadius = 0.3f, Rings = 12, RingSegments = 4 }, 0, Transform3D.Identity);
        st.AppendFrom(new SphereMesh { Radius = 0.27f, Height = 0.4f, RadialSegments = 10, Rings = 5, IsHemisphere = true }, 0,
            new Transform3D(Basis.FromEuler(new Vector3(MathF.PI, 0, 0)), Vector3.Zero));
        _net = new MeshInstance3D
        {
            Mesh = st.Commit(),
            TopLevel = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = FishingSim.World.Props.BuildingFactory.Pixel(new StandardMaterial3D
            {
                AlbedoColor = new Color(0.45f, 0.33f, 0.2f),
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            }),
        };
        AddChild(_net);
        _net.GlobalPosition = at + Vector3.Down * 0.1f;
        _netTime = 1.2f;
    }

    private void TickNet(float dt)
    {
        if (_net == null)
            return;
        _netTime -= dt;
        _net.GlobalPosition += Vector3.Up * dt * 0.6f;
        if (_netTime <= 0f)
        {
            _net.QueueFree();
            _net = null;
        }
    }

    /// <summary>Lands the given fish immediately (dev tools and tests).</summary>
    public void ForceLand(FishSpecies species, float weight)
    {
        CurrentSpecies = species;
        CurrentWeight = weight;
        LandFish();
    }

    private void LandFish()
    {
        var species = CurrentSpecies!;
        CatchCount++;
        CatchTotalKg += CurrentWeight;
        var context = new CatchContext(
            Lake.Location.DisplayName, Loadout.Bait.DisplayName, Loadout.Hook.DisplayName,
            MathF.Round(CurrentDepth() * 10f) / 10f, World.TimeText);
        // Counts, journal, records and XP now; the keepnet only when the angler chooses (catch panel: E / R).
        var result = Profile.AddCatch(species, CurrentWeight, context, toKeepnet: false);
        LastCatch = result;
        AwaitingDecision = true;
        if (_biter != null)
            Population?.Catch(_biter);
        _biter = null;
        string text = $"Złowiono: {species.SpeciesName}, {CurrentWeight:0.00} kg! +{result.Xp} XP";
        if (result.Record)
            text += "\nNowy rekord gatunku!";
        if (result.LevelUp)
            text += $"\nAwans na poziom {Profile.Level}!";
        ShowBanner(text, 4.5f);
        _lineOutOutsideFight = 0f;
        _fight = null;
        _bobber.Stow();
        _fishNode.Visible = false;
        ShowHeldFish(species, CurrentWeight);
        SetState(RodState.Landed);
        SoundCue?.Invoke(Core.Audio.OneShot.Catch, 1f);
        FishLanded?.Invoke(species, CurrentWeight);
    }

    /// <summary>Breaks the tackle as if a fish had done it (tools and tests).</summary>
    public void ForceBreak(TackleSlot slot) =>
        BreakLine(slot == TackleSlot.Rod ? "Wędka się złamała!" : "Linka pękła!", slot);

    /// <summary>
    /// Ends the fight with a loss. <paramref name="destroyed"/> is the piece of tackle lost for good: a snapped rod, the
    /// hook left in the fish, or the whole line pulled off the spool. It leaves the inventory.
    /// </summary>
    private void BreakLine(string text, TackleSlot? destroyed = null)
    {
        if (destroyed is TackleSlot slot && !DevUnbreakable)
        {
            var lost = Loadout.Selected(slot);
            if (slot == TackleSlot.Hook)
            {
                // The hook goes with the fish, and with it the lure on it and the leader above it.
                if (Loadout.Bait.Reusable && Loadout.BaitCount > 0)
                {
                    text += $" Stracono przynętę: {Loadout.Bait.DisplayName}.";
                    Loadout.LoseLure();
                }
                var leader = Loadout.Leader;
                if (Profile.Destroy(TackleSlot.Leader, leader.ItemId))
                    text += $" Stracono: {leader.DisplayName}.";
            }
            if (Profile.Destroy(slot, lost.ItemId))
            {
                Loadout.Resync();
                ApplyTackle();
                text += Loadout.Ready ? $" Stracono: {lost.DisplayName}." : $" Stracono: {lost.DisplayName}. Kup nowy w kramie wędkarskim.";
            }
        }
        ShowBanner(text, 3.5f);
        SoundCue?.Invoke(Core.Audio.OneShot.LineSnap, 1f);
        LoseBiter(_fishNode.Visible ? _fishNode.GlobalPosition : null);
        _fight = null;
        _lineOutOutsideFight = 0f;
        _bobber.Stow();
        _fishNode.Visible = false;
        SetState(RodState.Broken);
    }

    private void TickResult()
    {
        // A landed fish waits in the hand for the angler's choice; after it the rod is ready again shortly.
        if (State == RodState.Landed && AwaitingDecision)
            return;
        float since = State == RodState.Landed ? _stateTime - _decidedAt : _stateTime;
        if (since > (State == RodState.Landed ? 1f : ResultSeconds))
            ResetToIdle();
    }

    private void ShowHeldFish(FishSpecies species, float weight)
    {
        ClearHeldFish();
        // The detailed model at its real size; the catch panel puts it on its stage and turns it slowly. Until then
        // (or without a panel, in tests) it waits hidden by the camera, where its skin is painted in the background.
        var model = FishModel.Create(species, weight, detailed: true);
        model.Visible = false;
        _camera.AddChild(model);
        _heldFish = model;
    }

    /// <summary>A fish has just been landed: the catch panel shows it and asks where it goes.</summary>
    public bool AwaitingDecision { get; private set; }

    /// <summary>What the last landing earned (XP, record, value) for the catch panel.</summary>
    public CatchResult LastCatch { get; private set; }

    /// <summary>Kept for compatibility: the landed fish may still go back into the water.</summary>
    public bool CanRelease => State == RodState.Landed && AwaitingDecision;

    private float _decidedAt = -1f;

    /// <summary>The catch panel's "Do siatki": the fish goes into the keepnet (if it fits).</summary>
    public bool KeepCatch()
    {
        if (!AwaitingDecision || CurrentSpecies == null)
            return false;
        string? error = Profile.Keep(CurrentSpecies, CurrentWeight);
        if (error != null)
        {
            ShowBanner(error, 3f);
            return false;
        }
        Decided();
        SoundCue?.Invoke(Core.Audio.OneShot.SmallSplash, 0.5f);
        ShowBanner($"Do siatki: {CurrentSpecies.SpeciesName} {CurrentWeight:0.00} kg", 2f);
        return true;
    }

    /// <summary>The catch panel's "Do wody": the fish swims away (a little XP).</summary>
    public void ReleaseCatch()
    {
        if (!AwaitingDecision || CurrentSpecies == null)
            return;
        Profile.Release();
        Decided();
        SoundCue?.Invoke(Core.Audio.OneShot.SmallSplash, 0.8f);
        ShowBanner($"Wypuszczono: {CurrentSpecies.SpeciesName} {CurrentWeight:0.00} kg (+{Profile.ReleaseXp} XP)", 2.5f);
    }

    private void Decided()
    {
        AwaitingDecision = false;
        _decidedAt = _stateTime;
        ClearHeldFish();
    }

    /// <summary>The landed fish (on the catch panel's stage), or null.</summary>
    public Node3D? HeldFish => _heldFish;

    /// <summary>The held fish's model (to check its detailed skin).</summary>
    public FishModel? HeldFishModel => _heldFish;

    private void ClearHeldFish()
    {
        _heldFish?.QueueFree();
        _heldFish = null;
    }

    // ------------------------------------------------------------------ spinning and bottom
    /// <summary>The lure or the feeder lands: from now on the rod moves it (the float keeps floating by itself).</summary>
    private void EnterWater()
    {
        _rigPos = new Vector3(_bobber.GlobalPosition.X, _waterY, _bobber.GlobalPosition.Z);
        _rigSettled = false;
        _rigDepth = 0f;
        _lure = _castMethod == FishingMethod.Spinning
            ? new LureMotion(MethodRules.FamilyOf(Loadout.Bait.AffinityAs), LureTuning.From(Loadout.Bait))
            : null;
        _lureAppeal = LureAppeal;
        var to = Origin() - _rigPos;
        _bobber.Heading = MathF.Atan2(to.X, to.Z);
        _bobber.Hold(_rigPos + Vector3.Down * 0.03f);
    }

    /// <summary>
    /// Spinning: LMB draws the lure in (Shift: fast) and it runs at its depth; let go, it sinks (a wobbler floats up,
    /// a fly stays on the surface). It fishes as well as it is led; dragged along the bottom it may snag.
    /// </summary>
    private void TickSpinning(float dt)
    {
        var lure = _lure;
        if (lure == null)
        {
            ResetToIdle();
            return;
        }
        float bottom = Lake.DepthAt(_rigPos.X, _rigPos.Z);
        var to = Origin() - _rigPos;
        to.Y = 0f;
        float dist = to.Length();
        var dir = dist > 1e-3f ? to / dist : Vector3.Zero;
        if (lure.Snagged)
        {
            if (Reeling)
            {
                switch (lure.Pull(_rng, dt))
                {
                    case SnagResult.Broke:
                        BreakLine("Zaczep nie puścił, linka pękła.", TackleSlot.Hook);
                        return;
                    case SnagResult.Freed:
                        ShowBanner("Przynęta uwolniona z zaczepu", 2f);
                        break;
                }
            }
            lure.Step(dt, 0f, bottom, _rng);
        }
        else
        {
            float retrieve = Reeling ? _reelSpeed * (FastReel ? 0.6f : 0.3f) : 0f;
            // The slack left by the cast is taken up first; only then does the lure move.
            float pull = TakeUpSlack(retrieve * dt);
            lure.Step(dt, pull > 0f ? pull / dt : 0f, bottom, _rng);
            if (lure.Snagged)
            {
                ShowBanner("Zaczep! PPM - szarpnij wędką, LPM - ciągnij (linka może pęknąć)", 3.5f);
                Twitch(1.6f);
            }
            else
                _rigPos += dir * pull;
        }
        // Drawn in to the bank or the rod tip: out of the water.
        if (bottom <= 0.02f || dist < 2f)
        {
            ResetToIdle();
            return;
        }
        PlaceLure(dir);
        KeepLineTight(Reeling && Slack < 0.05f);
        _lineOutOutsideFight = dist;
        // The play, the rod, the lure's size and how its colours suit the light and the water.
        _biteTimer -= dt * lure.BitePace * MethodRules.LureRodMatch(Loadout.Rod.Kind) * 2f
            * MethodRules.SizePace(Loadout.Bait.SizeCm) * _lureAppeal * ConditionPace;
        if (_biteTimer <= 0f)
            StartNibble();
    }

    private void PlaceLure(Vector3 towardsAngler)
    {
        float y = _lure is { Surface: true }
            ? _waterY + 0.004f + (_bobber.WaterHeight?.Invoke(_rigPos.X, _rigPos.Z) ?? 0f)
            : _waterY - (_lure?.Depth ?? 0f);
        _bobber.Hold(new Vector3(_rigPos.X, y, _rigPos.Z));
        if (towardsAngler != Vector3.Zero)
            _bobber.Heading = MathF.Atan2(towardsAngler.X, towardsAngler.Z);
        _bobber.LureSpeed = _lure?.Speed ?? 0f;
    }

    /// <summary>RMB while a lure is in the water: a twitch (it darts and flashes), or a jerk to free a snag.</summary>
    private void JerkLure()
    {
        var lure = _lure!;
        Twitch(lure.Snagged ? 1.6f : 0.9f);
        if (lure.Snagged)
        {
            switch (lure.Jerk(_rng))
            {
                case SnagResult.Broke:
                    BreakLine("Szarpnięcie zerwało linkę na zaczepie.", TackleSlot.Hook);
                    return;
                case SnagResult.Freed:
                    ShowBanner("Przynęta uwolniona z zaczepu", 2f);
                    break;
                default:
                    ShowBanner("Zaczep trzyma...", 1.2f);
                    break;
            }
            return;
        }
        var to = Origin() - _rigPos;
        to.Y = 0f;
        // A twitch on a slack line mostly takes up the slack.
        float dart = TakeUpSlack(lure.Twitch());
        if (to.Length() > 0.01f)
            _rigPos += to.Normalized() * dart;
    }

    /// <summary>
    /// Bottom: the feeder sinks to the bottom and empties its groundbait there; the line is tight and bites show on the
    /// quiver tip. Reeling drags the rig slowly over the bottom towards the angler.
    /// </summary>
    private void TickBottom(float dt)
    {
        float bottom = Lake.DepthAt(_rigPos.X, _rigPos.Z);
        var to = Origin() - _rigPos;
        to.Y = 0f;
        float dist = to.Length();
        var dir = dist > 1e-3f ? to / dist : Vector3.Zero;
        float floor = MathF.Max(bottom - 0.03f, 0f);
        if (!_rigSettled)
        {
            _rigDepth += 1.6f * dt;
            _lineLength = MathF.Max(_lineLength, TipDistance() * 1.02f);      // the line follows the feeder down
            if (_rigDepth >= floor)
            {
                _rigDepth = floor;
                _rigSettled = true;
                _lineLength = TipDistance() + 0.02f;   // the angler tightens the line to the feeder
                Twitch(0.4f);      // the tip springs back as the feeder touches down
                if (_feederLoaded)
                {
                    Groundbait.Feed(_rigPos.X, _rigPos.Z);
                    _bobber.FeederLoaded = false;
                }
            }
        }
        else if (Reeling)
        {
            // Dragged over the bottom towards the angler (Shift: faster), up the slope to the bank.
            float pull = TakeUpSlack(_reelSpeed * (FastReel ? 0.5f : 0.25f) * dt);
            _rigPos += dir * pull;
            _rigDepth = MathF.Max(Lake.DepthAt(_rigPos.X, _rigPos.Z) - 0.03f, 0f);
            KeepLineTight(true);
            if (Lake.DepthAt(_rigPos.X, _rigPos.Z) <= 0.03f || dist < 1.6f)
            {
                ShowBanner("Zestaw wyjęty z wody", 1.2f);
                ResetToIdle();
                return;
            }
        }
        _bobber.Hold(new Vector3(_rigPos.X, _waterY - _rigDepth, _rigPos.Z));
        if (dir != Vector3.Zero)
            _bobber.Heading = MathF.Atan2(dir.X, dir.Z);
        _lineOutOutsideFight = dist;
        if (!_rigSettled)
            return;
        float pace = GroundbaitSpots.PaceFor(GroundbaitHeat) * (Reeling ? 0.5f : 1f);
        _biteTimer -= dt * pace * ConditionPace;
        if (_biteTimer <= 0f)
            StartNibble();
    }

    // ------------------------------------------------------------------ the rod rest (Z)
    private RodRest? _rest;
    private Node? _rigHome;

    /// <summary>How close (m) the angler must stand to pick the rod up from its rest.</summary>
    public const float RestReach = 3.5f;

    /// <summary>The rod lies on its rest (the hands are free; the line runs from the rest).</summary>
    public bool OnRest => _rest != null;

    /// <summary>The rest's foot, or null.</summary>
    public Vector3? RestPosition => _rest?.GlobalPosition;

    public bool NearRest => _rest != null
        && new Vector2(_camera.GlobalPosition.X - _rest.GlobalPosition.X, _camera.GlobalPosition.Z - _rest.GlobalPosition.Z).Length() <= RestReach;

    /// <summary>
    /// Z with a float or a bottom rig in the water: a rest is pushed into the ground in front of the angler (a forked
    /// hazel stick, or the bought rod rest with its bell) and the rod is laid on it, pointing at the line's end, its
    /// butt on a short back rest. The angler's hands are free; RMB at a bite (from close by) picks it up and strikes.
    /// </summary>
    public bool PutOnRest()
    {
        if (_rest != null || _rig == null)
            return false;
        if (State is not (RodState.Waiting or RodState.Nibble or RodState.Bite) || _castMethod == FishingMethod.Spinning)
        {
            ShowBanner(_castMethod == FishingMethod.Spinning && State is RodState.Waiting
                ? "Spinningiem łowi się z ręki: przynętę trzeba prowadzić"
                : "Na podpórkę odkłada się wędkę z zarzuconym zestawem", 2.5f);
            return false;
        }
        var eye = _camera.GlobalTransform;
        var flat = -eye.Basis.Z;
        flat.Y = 0f;
        flat = flat.LengthSquared() > 1e-4f ? flat.Normalized() : Vector3.Forward;
        var from = eye.Origin + flat * 0.8f;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 4f, 1));
        if (hit.Count == 0)
        {
            ShowBanner("Tu nie ma gdzie postawić podpórki", 2f);
            return false;
        }
        var ground = (Vector3)hit["position"];
        var toEnd = _bobber.GlobalPosition - ground;
        toEnd.Y = 0f;
        var dir = toEnd.LengthSquared() > 1e-4f ? toEnd.Normalized() : flat;
        _rest = RodRest.Create(Gear?.Has(AccessoryKind.RodPod) == true);
        AddChild(_rest);
        _rest.GlobalPosition = ground;
        _rest.Rotation = new Vector3(0f, MathF.Atan2(dir.X, dir.Z), 0f);
        // The rod rises at 25 degrees towards the line's end, lying in the fork; the reel hangs under it.
        float rise = Mathf.DegToRad(25f);
        var rodDir = (dir * MathF.Cos(rise) + Vector3.Up * MathF.Sin(rise)).Normalized();
        var fork = ground + Vector3.Up * RodRest.ForkHeight;
        var up = (Vector3.Up - rodDir * rodDir.Dot(Vector3.Up)).Normalized();
        var basis = new Basis(rodDir.Cross(up), rodDir, up);
        _rigHome = _rig.GetParent();
        _rig.Reparent(_rest, true);
        _rig.GlobalTransform = new Transform3D(basis, fork - rodDir * 0.45f);
        _rest.PlaceBackRest(fork - rodDir * 0.78f);
        ShowBanner(_rest.HasBell
            ? "Wędka na podpórce z dzwoneczkiem. Z - podnieś, PPM przy braniu - zacięcie"
            : "Wędka na patyku. Z - podnieś, PPM przy braniu - zacięcie (podejdź bliżej)", 3f);
        return true;
    }

    /// <summary>Takes the rod back into the hands (the rest is pulled out).</summary>
    public bool PickUpRod()
    {
        if (_rest == null)
            return false;
        if (_rigHome != null && _rig != null)
        {
            _rig.Reparent(_rigHome, false);
            _rig.Position = new Vector3(0.1f, -0.25f, -0.33f);
            _rig.RotationDegrees = new Vector3(-78f, 0f, 0f);
        }
        _rest.QueueFree();
        _rest = null;
        return true;
    }

    // ------------------------------------------------------------------ line rendering
    /// <summary>
    /// The line: the rope steps with the tip and the end where they are this frame, and is drawn as a thin ribbon
    /// facing the camera, about a pixel wide and a little wider for thicker lines, in the line's colour (a multicoloured
    /// braid in metre bands), reddening as the tension nears the breaking point.
    /// </summary>
    private void UpdateLine(float dt)
    {
        _lineMesh.ClearSurfaces();
        if (State is RodState.Idle or RodState.Charging or RodState.Landed or RodState.Broken)
        {
            _ropeLive = false;
            return;
        }
        var a = _rodTip.GlobalPosition;
        var b = _bobber.GlobalPosition;
        float ratio = Math.Clamp(Tension / EffectiveLimit, 0f, 1f);
        if (State is RodState.Fight or RodState.Close)
            _lineLength = a.DistanceTo(b) * (1f + 0.012f * (1f - ratio));      // a fish keeps the line tight
        if (!_ropeLive)
        {
            _rope.Reset(a, b);
            _ropeLive = true;
        }
        _rope.Length = MathF.Max(_lineLength, 0.05f);
        _rope.Wind = WindVelocity;
        _rope.Step(dt, a, b);

        var line = Loadout.Line;
        bool multi = line.ItemId.Contains("wielokolorowa");
        var colour = line.LineColor.Lerp(new Color(1f, 0.2f, 0.15f), Math.Clamp((ratio - 0.5f) * 2f, 0f, 1f));
        float d = line.Diameter;
        // The ribbon faces whichever camera draws the frame.
        var eye = (GetViewport()?.GetCamera3D() ?? _camera).GlobalPosition;
        var pts = _rope.Positions;
        _lineMesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, _lineMaterial);
        float along = 0f;
        for (int i = 0; i < pts.Length; i++)
        {
            var p = pts[i];
            var tangent = i < pts.Length - 1 ? pts[i + 1] - p : p - pts[i - 1];
            if (tangent.LengthSquared() < 1e-10f)
                tangent = b - a;
            var toEye = eye - p;
            var side = tangent.Cross(toEye);
            side = side.LengthSquared() > 1e-12f ? side.Normalized() : Vector3.Up;
            float half = 0.5f * MathF.Max(d * 0.003f, toEye.Length() * 0.0008f * (0.8f + d * 2.5f));
            var c = multi ? ((int)along % 4) switch
            {
                0 => new Color(0.9f, 0.25f, 0.2f), 1 => new Color(0.95f, 0.85f, 0.2f), 2 => new Color(0.25f, 0.5f, 0.9f), _ => new Color(0.3f, 0.75f, 0.3f),
            } : colour;
            _lineMesh.SurfaceSetColor(c);
            _lineMesh.SurfaceAddVertex(p - side * half);
            _lineMesh.SurfaceSetColor(c);
            _lineMesh.SurfaceAddVertex(p + side * half);
            if (i < pts.Length - 1)
                along += (pts[i + 1] - p).Length();
        }
        _lineMesh.SurfaceEnd();
    }
}
