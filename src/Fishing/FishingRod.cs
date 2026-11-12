using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Core;
using FishingSim.Core.Gear;
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
        ApplyTackle();
    }

    public override void _Ready()
    {
        _lineMesh = new ImmediateMesh();
        _lineMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
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
        float load = State is RodState.Fight or RodState.Close ? Tension
            : lineOut ? 0.12f + (_bobber.ExtraForce > 0f ? 0.35f : 0f)
            : 0.04f;
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
        float v = _castMax;
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

    public BiteContext MakeBiteContext(float depth) =>
        new(Loadout.Bait.ItemId, Loadout.Hook.Size, depth, Lake.Location, World.Period, World.Weather);

    /// <summary>Weight of one species for the current setup at the current bobber depth.</summary>
    public float BiteWeight(FishSpecies sp) => BiteModel.Weight(sp, MakeBiteContext(CurrentDepth()));

    /// <summary>Share of bites per species for the current setup at a given depth.</summary>
    public IReadOnlyList<BiteChance> BiteChances(float depth) => BiteModel.Chances(_species, MakeBiteContext(depth));

    public float Attraction() => BiteModel.Attraction(_species, MakeBiteContext(CurrentDepth()));

    // ------------------------------------------------------------------ input
    public override void _UnhandledInput(InputEvent @event)
    {
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
        bool captured = !InputBlocked && Input.MouseMode == Input.MouseModeEnum.Captured;
        Reeling = ReelOverride || (captured && Input.IsMouseButtonPressed(MouseButton.Left));
        Lifting = LiftOverride || (captured && Input.IsMouseButtonPressed(MouseButton.Right));
        BannerTime = MathF.Max(BannerTime - dt, 0f);
        UpdateBaitAttraction(dt);
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
        UpdateBend(dt);
        UpdateLine();
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
            Population.SetBait(_bobber.GlobalPosition, sp => Math.Clamp(_interest.GetValueOrDefault(sp) * 0.8f, 0f, 1.2f));
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
    }

    private void ShowBanner(string text, float seconds = 3f)
    {
        BannerText = text;
        BannerTime = seconds;
    }

    private Vector3 Origin()
    {
        var p = _camera.GlobalPosition;
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
        SetState(RodState.Idle);
    }

    // ------------------------------------------------------------------ casting
    private void Cast()
    {
        Twitch(-3f);   // the blank whips forward as the cast is released
        var basis = _camera.GlobalTransform.Basis;
        var aim = (-basis.Z).Rotated(basis.X.Normalized(), Mathf.DegToRad(CastLoftDegrees));
        float speed = Mathf.Lerp(_castMin, _castMax, CastPower);
        _bobber.Launch(_rodTip.GlobalPosition, aim * speed);
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
        if (_bobber.Submerged > 0.5f)
        {
            SoundCue?.Invoke(Core.Audio.OneShot.Splash, Math.Clamp(CastPower + 0.4f, 0.5f, 1f));
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
            : BiteModel.WaitSeconds(_rng, BiteWaitMin, BiteWaitMax, _lineVisibility, Attraction());

    private void TickWaiting(float dt)
    {
        _lineOutOutsideFight = FlatDistance(_bobber.GlobalPosition);
        if (Reeling)
        {
            var to = Origin() - _bobber.GlobalPosition;
            to.Y = 0f;
            _bobber.DriveVelocity = to.Normalized() * _reelSpeed * 0.6f;
            if (_lineOutOutsideFight < 2.5f)
            {
                ResetToIdle();
                return;
            }
        }
        else
        {
            _bobber.DriveVelocity = Vector3.Zero;
        }
        // A lure only fishes while it moves: the clock to the next bite runs while it is reeled in (faster on a spinning
        // rod), and barely at all while it lies still.
        float pace = 1f;
        if (Loadout.LureOn)
            pace = Reeling ? (Loadout.Rod.Kind == RodKind.Spinning ? 3.2f : 2.4f) : 0.1f;
        _biteTimer -= dt * pace;
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
        _nibbleLength = _rng.Range(1f, 2.2f);
        // The bell on the rod rest rings when a fish touches the bait.
        if (Gear?.Has(AccessoryKind.RodPod) == true)
            SoundCue?.Invoke(Core.Audio.OneShot.Bell, 1f);
        _pulseTimer = 0f;
        if (CurrentSpecies != null)
        {
            Graphics.FishSkin.Prewarm(CurrentSpecies);     // paint the close-up skin while the float twitches
            FishAsset.Prefetch(CurrentSpecies);            // and decode the species' model pictures
        }
        SetState(RodState.Nibble);
    }

    private void TickNibble(float dt)
    {
        _pulseTimer -= dt;
        if (_pulseTimer <= 0f)
        {
            _pulseTimer = _rng.Range(0.3f, 0.7f);
            _bobber.ApplyCentralImpulse(Vector3.Down * _bobber.Mass * 1.2f);
            Twitch(0.6f);
        }
        if (_stateTime >= _nibbleLength)
        {
            _bobber.ExtraForce = 1.6f;
            Twitch(1.2f);
            SetState(RodState.Bite);
        }
    }

    private void TickBite()
    {
        if (_stateTime < HookWindow)
            return;
        _bobber.ExtraForce = 0f;
        LoseBiter(_bobber.GlobalPosition);
        ShowBanner("Za późno - ryba się zerwała");
        NewBiteTimer();
        SetState(RodState.Waiting);
    }

    public void TryHook()
    {
        switch (State)
        {
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
                    * (1f + (Loadout.Line.Kind == LineKind.Braid ? 0.1f : 0f) + (Gear?.Accessory(AccessoryKind.HookHone)?.Power ?? 0f));
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
        var to = _bobber.GlobalPosition - Origin();
        to.Y = 0f;
        var gear = new FightGear(DragMax, EffectiveLimit, _rodShock, _reelSpeed, MaxLine);
        _fight = new FightModel(FishProfile.From(species, CurrentWeight), gear, _rng, to.Length(), MathF.Atan2(to.X, to.Z), DragSetting)
        {
            Unbreakable = DevUnbreakable,
        };
        SoundCue?.Invoke(Core.Audio.OneShot.Strike, 1f);
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
        // Held up side-on above the catch panel, like a photo of the catch, at its real size: a bigger, heavier fish
        // fills more of the view (only a giant is held further off so it still fits).
        var model = FishModel.Create(species, weight, detailed: true);
        float fit = MathF.Min(1f, 1.15f / model.Length);
        float shown = model.Length * fit, distance = Math.Clamp(0.42f + 0.32f * shown, 0.42f, 0.8f);
        model.Scale = new Vector3(fit, fit, fit);
        model.Wiggle = 0.25f;
        model.Position = new Vector3(0.02f, 0.1f * distance / 0.6f, -distance);
        model.RotationDegrees = new Vector3(-4f, -80f, 6f);
        _camera.AddChild(model);
        _heldFish = model;
    }

    /// <summary>A fish has just been landed and is held up: the catch panel asks where it goes.</summary>
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

    /// <summary>The landed fish shown in front of the camera (held in the left hand), or null.</summary>
    public Node3D? HeldFish => _heldFish;

    /// <summary>The held fish's model (to check its detailed skin).</summary>
    public FishModel? HeldFishModel => _heldFish;

    private void ClearHeldFish()
    {
        _heldFish?.QueueFree();
        _heldFish = null;
    }

    // ------------------------------------------------------------------ line rendering
    private void UpdateLine()
    {
        _lineMesh.ClearSurfaces();
        if (State is RodState.Idle or RodState.Charging or RodState.Landed or RodState.Broken)
            return;
        var a = _rodTip.GlobalPosition;
        var b = _bobber.GlobalPosition;
        float ratio = Math.Clamp(Tension / EffectiveLimit, 0f, 1f);
        float slack = State is RodState.Fight or RodState.Close ? 1f - Math.Clamp(ratio * 2f, 0f, 1f) : 1f;
        float sag = slack * Math.Clamp(a.DistanceTo(b) * 0.04f, 0f, 1.5f);
        _lineMaterial.AlbedoColor = Colors.White.Lerp(new Color(1f, 0.2f, 0.15f), Math.Clamp((ratio - 0.5f) * 2f, 0f, 1f));
        _lineMesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip, _lineMaterial);
        const int steps = 16;
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            var p = a.Lerp(b, t);
            p.Y -= sag * 4f * t * (1f - t);
            p.Y = MathF.Max(p.Y, _waterY + 0.01f);
            _lineMesh.SurfaceAddVertex(p);
        }
        _lineMesh.SurfaceEnd();
    }
}
