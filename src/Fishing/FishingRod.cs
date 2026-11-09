using System;
using System.Collections.Generic;
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
    private float _bend;
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

    /// <summary>Weakest link of the setup: the line or the rod, whichever gives first.</summary>
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
        RodMaxLoad = rod.MaxLoad;
        _rodShock = rod.ShockAbsorb;
        _castMin = BaseCastMin * rod.CastMult;
        _castMax = BaseCastMax * rod.CastMult;
        DragMax = reel.DragMax;
        _reelSpeed = reel.ReelSpeed;
        MaxLine = reel.LineCapacity;
        LineStrength = line.Strength;
        _lineVisibility = line.Visibility;
    }

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
                    if (Loadout.BaitCount <= 0)
                    {
                        ShowBanner("Brak przynęty! Zmień przynętę (klawisz 5)");
                        return;
                    }
                    CastPower = 0f;
                    SetState(RodState.Charging);
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
            case >= Key.Key1 and <= Key.Key5 when State == RodState.Idle:
                Loadout.Cycle((TackleSlot)(key.Keycode - Key.Key1), key.ShiftPressed ? -1 : 1);
                ApplyTackle();
                Profile.NotifyChanged();
                break;
        }
    }

    // ------------------------------------------------------------------ main loop
    public override void _PhysicsProcess(double delta)
    {
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
        float bendTarget = State is RodState.Fight or RodState.Close ? Math.Clamp(Tension / EffectiveLimit, 0f, 1f) : 0f;
        TickNet(dt);
        _bend = Mathf.Lerp(_bend, bendTarget, MathF.Min(dt * 8f, 1f));
        _rig.SetBend(_bend);
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
        var basis = _camera.GlobalTransform.Basis;
        var aim = (-basis.Z).Rotated(basis.X.Normalized(), Mathf.DegToRad(CastLoftDegrees));
        float speed = Mathf.Lerp(_castMin, _castMax, CastPower);
        _bobber.Launch(_rodTip.GlobalPosition, aim * speed);
        SetState(RodState.Flying);
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
        _biteTimer -= dt;
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
        Loadout.UseBait();
        _bobber.DriveVelocity = Vector3.Zero;
        _nibbleLength = _rng.Range(1f, 2.2f);
        _pulseTimer = 0f;
        SetState(RodState.Nibble);
    }

    private void TickNibble(float dt)
    {
        _pulseTimer -= dt;
        if (_pulseTimer <= 0f)
        {
            _pulseTimer = _rng.Range(0.3f, 0.7f);
            _bobber.ApplyCentralImpulse(Vector3.Down * _bobber.Mass * 1.2f);
        }
        if (_stateTime >= _nibbleLength)
        {
            _bobber.ExtraForce = 1.6f;
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
                // A badly sized hook loses fish.
                float chance = 0.5f + 0.5f * BiteModel.HookFit(CurrentSpecies!, Loadout.Hook.Size);
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
        if (_biter != null)
            Population?.Hook(_biter);
        _fishModel?.QueueFree();
        _fishModel = FishModel.Create(species, CurrentWeight);
        _fishNode.AddChild(_fishModel);
        _fishNode.Visible = true;
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
                BreakLine(RodMaxLoad < LineStrength ? "Wędka się złamała!" : "Linka pękła!");
                return;
            case FightOutcome.SpooledOut:
                BreakLine("Ryba wyciągnęła całą linkę!");
                return;
            case FightOutcome.Landed:
                EnterClose();
                return;
        }

        // Fish position and visuals.
        float angle = _fight.FishAngle;
        var dir = new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle));
        var pos = Origin() + dir * _fight.LineOut;
        _bobber.Hold(new Vector3(pos.X, _waterY - 0.02f, pos.Z));
        float fishY = _waterY - MathF.Min(0.35f, CurrentDepth() * 0.5f);
        _fishNode.GlobalPosition = new Vector3(pos.X, fishY, pos.Z);
        _fishNode.Rotation = new Vector3(0f, angle + MathF.Sin(_stateTime * 2f) * 0.15f, 0f);
        if (_fishModel != null)
            _fishModel.Wiggle = (_fight.Running ? 1.6f : 0.7f) * (0.4f + 0.6f * _fight.Stamina);
    }

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
            BreakLine(RodMaxLoad < LineStrength ? "Wędka pękła pod ciężarem ryby!" : "Linka pękła przy podnoszeniu! Użyj kasarka.");
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
        var result = Profile.AddCatch(species, CurrentWeight, context);
        if (_biter != null)
            Population?.Catch(_biter);
        _biter = null;
        string text = $"Złowiono: {species.SpeciesName}, {CurrentWeight:0.00} kg! +{result.Xp} XP";
        if (result.Record)
            text += "\nNowy rekord gatunku!";
        if (result.LevelUp)
            text += $"\nAwans na poziom {Profile.Level}!";
        if (result.Released)
            text += "\nSiatka pełna: ryba wypuszczona. Sprzedaj ryby albo kup większą siatkę.";
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

    private void BreakLine(string text)
    {
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
        if (_stateTime > ResultSeconds)
            ResetToIdle();
    }

    private void ShowHeldFish(FishSpecies species, float weight)
    {
        ClearHeldFish();
        var model = FishModel.Create(species, weight);
        float fit = MathF.Min(1f, 0.55f / model.Length);
        model.Scale = new Vector3(fit, fit, fit);
        model.Wiggle = 0.25f;
        model.Position = new Vector3(0.05f, -0.32f, -0.85f);
        model.RotationDegrees = new Vector3(-8f, -80f, 12f);
        _camera.AddChild(model);
        _heldFish = model;
    }

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
