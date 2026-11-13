using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Core;
using FishingSim.Core.Camp;
using FishingSim.Data;
using FishingSim.Progress;
using FishingSim.World;
using FishingSim.World.Props;

namespace FishingSim.Fishing;

/// <summary>What the angler holds.</summary>
public enum HandTool { Rod, Shovel, Net, LiftNet }

/// <summary>
/// The tools besides the rod: Q switches between the rod, the shovel and the insect net (the ones owned; the rod only
/// while its line is in). The left mouse button uses them. Digging: the shovel rises, is driven into the ground in front
/// (the eyes go down to the work), levered back and lifted with a clod of soil (with the worms found wriggling on it)
/// that is thrown aside, leaving a hole; the bait goes into the bait box (Core/Camp/Digging). Sweeping: the net is
/// swung across in front and twisted shut; mayflies over the water at dusk, grasshoppers in a meadow by day, chafers
/// round the trees at dusk (Core/Camp/Sweeping). The lift net (podrywka): the first click lowers it from its pole into
/// the water ahead (a slice of bread is crumbled over it when there is bread), the second lifts it, and the small fish
/// that came over it flap in the netting before they go into the bait box as live bait (Core/Camp/LiftNetting). Both
/// hands hold the tool (FirstPersonBody.ToolPose -> ArmIK). The
/// animations are keyframes in the body's frame (this node is a child of the player, which only turns about Y).
/// </summary>
public partial class HandTools : Node3D
{
    private readonly record struct Key(float T, Vector3 At, Vector3 Dir, float Roll);

    // Shovel keys: where the blade's edge is and which way the handle points (g = the ground in front, relative to the
    // feet). The blade works to the right in front, the handle slants to the middle where both hands hold it near the top,
    // so from the eyes the blade is seen beside the hands, not hidden under them.
    private static Key[] DigKeys(float g) => new Key[]
    {
        new(0.00f, new(0.015f, 0.03f, -0.98f), new(0.12f, 0.82f, 0.55f), 0f),
        new(0.18f, new(0.22f, 0.35f, -1.25f), new(-0.1f, 0.9f, 0.8f), 0f),
        new(0.36f, new(0.3f, g - 0.12f, -1.2f), new(-0.3f, 0.85f, 0.45f), 0f),
        new(0.50f, new(0.3f, g - 0.2f, -1.2f), new(-0.3f, 0.85f, 0.45f), 0f),
        new(0.64f, new(0.3f, g - 0.1f, -1.18f), new(-0.3f, 0.75f, 0.68f), 0f),
        new(0.80f, new(0.3f, g + 0.6f, -1.25f), new(-0.25f, 0.35f, 0.9f), 0f),
        new(0.90f, new(0.7f, g + 0.8f, -1.0f), new(-0.6f, 0.15f, 0.8f), 1.9f),
        new(1.00f, new(0.015f, 0.03f, -0.98f), new(0.12f, 0.82f, 0.55f), 0f),
    };

    // Net keys: where the pole's end is and which way the pole points; the right hand swings it alone.
    private static readonly Key[] SweepKeys =
    {
        new(0.00f, new(0.22f, 1.0f, -0.3f), new(0.15f, 1f, -1.1f), 0f),
        new(0.22f, new(0.3f, 1.08f, -0.2f), new(1f, 0.75f, -0.55f), 0.2f),
        new(0.52f, new(0.02f, 1.0f, -0.38f), new(-0.95f, 0.35f, -0.8f), 0f),
        new(0.70f, new(0f, 1.0f, -0.36f), new(-0.6f, 0.6f, -0.9f), 1.7f),
        new(1.00f, new(0.22f, 1.0f, -0.3f), new(0.15f, 1f, -1.1f), 0f),
    };

    private const float DigSeconds = 2.1f, SweepSeconds = 1.35f;
    // Looking down only so far that the shoulders stay below the view: the hands and the blade in front are seen.
    private const float DigPitch = -0.45f, SweepPitch = -0.15f;

    /// <summary>How far the body bends over the work (forward, down) while digging, and where the head goes meanwhile
    /// (a little down and back, so the eyes see the blade in the ground beyond the hands).</summary>
    private static readonly Vector3 DigLean = new(0f, -0.3f, -0.28f), DigHead = new(0f, -0.3f, -0.28f);

    private Player _player = null!;
    private FishingRod _rod = null!;
    private GearCatalog _gear = null!;
    private Profile _profile = null!;
    private LakeBed _lake = null!;
    private WorldState _world = null!;
    private InsectSwarms? _swarms;
    private DigMarks? _marks;
    private readonly IRng _rng = new SystemRng();

    private Node3D _shovel = null!, _net = null!, _load = null!, _bag = null!, _liftPole = null!, _liftNet = null!, _liftCatch = null!;
    private readonly List<MeshInstance3D> _worms = new();
    private float _t = -1f, _ground, _blend, _bagX, _bagZ;
    private Vector3 _digAt, _lastRing;
    private Haul? _pending;
    private bool _impact, _tossed, _swished, _caught;

    public HandTool Current { get; private set; } = HandTool.Rod;

    /// <summary>The lift net's stage: carried up, going down, soaking in the water, coming up, showing the catch.</summary>
    public enum LiftStage { Up, Lowering, Down, Lifting, Showing }

    public LiftStage Lift { get; private set; } = LiftStage.Up;

    // Lift net: seconds of each stage, the pole's pitch (radians above the horizon) carried, held up to show the catch,
    // and the lowest it goes (from a high pier the net hangs higher).
    private const float LowerSeconds = 1.5f, LiftSeconds = 1.1f, ShowSeconds = 2.4f, ShowHold = 1.7f;
    private const float PitchUp = 1.15f, PitchShow = 0.3f, PitchLowest = -0.75f;

    /// <summary>The body bends towards the water while the lift net is down (and the head with it).</summary>
    private static readonly Vector3 LiftLean = new(0f, -0.22f, -0.28f);

    private float _liftT, _soak, _pitchDown, _liftFrom;
    private Vector3 _swing, _swingVel, _lastTip;
    private bool _groundbait;
    private Haul? _liftHaul;
    private readonly List<Node3D> _fry = new();

    /// <summary>True while a dig, a sweep or the lift net is under way (the tool cannot be put away).</summary>
    public bool Working => _t >= 0f || Lift != LiftStage.Up;

    /// <summary>The lift net's soak so far (s), for the HUD.</summary>
    public float Soak => _soak;

    /// <summary>Where the lift net's cord meets the pole's tip (the net hangs <see cref="CampProps.LiftDrop"/> below it).</summary>
    public Vector3 LiftNetTop => _liftNet.GlobalPosition;

    /// <summary>Screenshots: the action stops at this point (0..1) instead of running to its end.</summary>
    public float? FreezeAt { get; set; }

    /// <summary>The last dig's or sweep's result (tests, HUD).</summary>
    public Haul? LastHaul { get; private set; }

    public event Action<HandTool>? Changed;

    public void Setup(Player player, FishingRod rod, GearCatalog gear, Profile profile, LakeBed lake, WorldState world,
        InsectSwarms? swarms, DigMarks? marks)
    {
        _player = player;
        _rod = rod;
        _gear = gear;
        _profile = profile;
        _lake = lake;
        _world = world;
        _swarms = swarms;
        _marks = marks;
    }

    public override void _Ready()
    {
        _shovel = CampProps.Shovel(out _load);
        _net = CampProps.InsectNet(out _bag);
        _liftPole = CampProps.LiftNetPole();
        _liftNet = CampProps.LiftNet(out _liftCatch);
        _liftNet.TopLevel = true;
        _liftPole.AddChild(_liftNet);
        foreach (var tool in new[] { _shovel, _net, _liftPole })
        {
            tool.Visible = false;
            foreach (var n in tool.FindChildren("*", "GeometryInstance3D", true, false))
                ((GeometryInstance3D)n).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            AddChild(tool);
        }
        for (int i = 0; i < 3; i++)
        {
            var worm = CampProps.Worm(i);
            worm.Position = new Vector3((i - 1) * 0.035f, 0.06f, -0.04f + i * 0.01f);
            worm.Rotation = new Vector3(0f, i * 1.3f, 0f);
            worm.Visible = false;
            _load.AddChild(worm);
            _worms.Add(worm);
        }
    }

    /// <summary>The tools the angler can take in hand right now (the rod always).</summary>
    public List<HandTool> Available()
    {
        var list = new List<HandTool> { HandTool.Rod };
        if (_gear.Has(AccessoryKind.Shovel))
            list.Add(HandTool.Shovel);
        if (_gear.Has(AccessoryKind.InsectNet))
            list.Add(HandTool.Net);
        if (_gear.Has(AccessoryKind.LiftNet))
            list.Add(HandTool.LiftNet);
        return list;
    }

    public static string NameOf(HandTool t) => t switch
    {
        HandTool.Shovel => "Łopata",
        HandTool.Net => "Siatka na owady",
        HandTool.LiftNet => "Podrywka",
        _ => "Wędka",
    };

    /// <summary>Takes a tool in hand. False when busy, not owned, or the rod's line is still out.</summary>
    public bool Select(HandTool tool)
    {
        if (Lift != LiftStage.Up)
        {
            _rod.Notify("Najpierw podnieś podrywkę (LPM).", 2f);
            return false;
        }
        if (Working || tool == Current)
            return false;
        if (!Available().Contains(tool))
            return false;
        if (Current == HandTool.Rod && _rod.State != RodState.Idle)
        {
            _rod.Notify("Najpierw zwiń linkę.", 2f);
            return false;
        }
        Current = tool;
        _rod.Holstered = tool != HandTool.Rod;
        _player.Rig.Visible = tool == HandTool.Rod;
        _shovel.Visible = tool == HandTool.Shovel;
        _net.Visible = tool == HandTool.Net;
        _liftPole.Visible = tool == HandTool.LiftNet;
        _rod.Notify(tool switch
        {
            HandTool.LiftNet => "Podrywka: LPM opuszcza ją do wody przed tobą, drugie LPM podrywa (żywce). Q zmienia narzędzie.",
            HandTool.Shovel => "Łopata: LPM kopie (robaki w wilgotnej ziemi). Q zmienia narzędzie.",
            HandTool.Net => "Siatka: LPM macha (jętki nad wodą o zmierzchu, koniki na łące). Q zmienia narzędzie.",
            _ => "Wędka w ręku.",
        }, 3f);
        Changed?.Invoke(tool);
        return true;
    }

    /// <summary>The next (or previous) owned tool.</summary>
    public void Cycle(int step = 1)
    {
        var list = Available();
        if (list.Count < 2)
        {
            _rod.Notify("Łopatę, siatkę na owady i podrywkę kupisz w kramie wędkarskim.", 2.5f);
            return;
        }
        int at = Math.Max(list.IndexOf(Current), 0);
        Select(list[((at + step) % list.Count + list.Count) % list.Count]);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_rod == null || _rod.InputBlocked || _rod.AwaitingDecision)
            return;
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Godot.Key.Q } k)
        {
            Cycle(k.ShiftPressed ? -1 : 1);
            GetViewport().SetInputAsHandled();
        }
        else if (Current != HandTool.Rod && e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                 && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Use();
            GetViewport().SetInputAsHandled();
        }
    }

    private Vector3 Forward => -_player.GlobalTransform.Basis.Z with { Y = 0f };

    /// <summary>Digs or sweeps with the tool in hand. False when nothing starts (busy, nowhere to dig).</summary>
    public bool Use()
    {
        if (Current == HandTool.LiftNet)
            return UseLiftNet();
        if (Working || Current == HandTool.Rod)
            return false;
        if (Current == HandTool.Shovel)
        {
            var feet = _player.GlobalPosition;
            if (feet.Y - _lake.HeightAt(feet.X, feet.Z) > 0.35f)
            {
                _rod.Notify("Z pomostu nie dokopiesz się do ziemi.", 2f);
                return false;
            }
            var ahead = _player.GlobalTransform.Basis * new Vector3(0.3f, 0f, -1.2f);
            ahead.Y = 0f;
            var at = feet + ahead;
            var ground = _lake.GroundKindAt(at.X, at.Z);
            if (ground == GroundKind.Water)
            {
                _rod.Notify("Tu jest woda.", 2f);
                return false;
            }
            _digAt = new Vector3(at.X, _lake.HeightAt(at.X, at.Z), at.Z);
            _ground = _digAt.Y - feet.Y;
            // On a path or rock the blade still stabs the ground but bounces off: no clod, no hole, the reason in the banner.
            _pending = Digging.Dig(ground, _world.Hour, _world.Weather, _marks?.Used(_digAt) ?? false, _rng);
        }
        _t = 0f;
        _impact = _tossed = _swished = _caught = false;
        _player.Busy = Current == HandTool.Shovel;
        return true;
    }

    public override void _Process(double delta)
    {
        if (_player == null)
            return;
        float dt = (float)delta;
        if (Current == HandTool.LiftNet)
        {
            ProcessLiftNet(dt);
            return;
        }
        if (_t >= 0f)
        {
            _t += dt / (Current == HandTool.Shovel ? DigSeconds : SweepSeconds);
            if (FreezeAt is float stop)
                _t = MathF.Min(_t, stop);
            if (Current == HandTool.Shovel)
                DigEvents();
            else
                SweepEvents();
            if (_t >= 1f)
                _t = -1f;
        }
        // The eyes go down to the work and come back up after it.
        float want = Working ? 1f : 0f;
        _blend = Mathf.MoveToward(_blend, want, dt * (want > _blend ? 2.2f : 1.6f));
        _player.WorkPitch = Current == HandTool.Shovel ? DigPitch : SweepPitch;
        _player.WorkOffset = Current == HandTool.Shovel ? DigHead : Vector3.Zero;
        _player.WorkBlend = Smooth(_blend);
        _player.Busy = Working && Current == HandTool.Shovel;
        if (Current == HandTool.Rod)
            return;
        float t = Math.Max(_t, 0f);
        var tool = Current == HandTool.Shovel ? _shovel : _net;
        var keys = Current == HandTool.Shovel ? DigKeys(Working ? _ground : 0f) : SweepKeys;
        tool.Transform = Pose(keys, t);
        if (Current == HandTool.Net)
            SwingBag(dt);
        // The worms on the clod wriggle.
        foreach (var (w, i) in Enumerate(_worms))
            if (w.Visible)
                w.Rotation = new Vector3(0f, i * 1.3f + MathF.Sin((float)Time.GetTicksMsec() / 120f + i) * 0.6f, MathF.Sin((float)Time.GetTicksMsec() / 90f + i * 2f) * 0.3f);
    }

    private static IEnumerable<(T, int)> Enumerate<T>(IEnumerable<T> items)
    {
        int i = 0;
        foreach (var x in items)
            yield return (x, i++);
    }

    private void DigEvents()
    {
        var haul = _pending;
        bool soil = haul != null && haul.Message.StartsWith("Wykopano") || haul?.Message.StartsWith("Pusta") == true || haul?.Message.StartsWith("Tu już") == true;
        if (!_impact && _t >= 0.36f)
        {
            _impact = true;
            _rod.Cue(Core.Audio.OneShot.Dig, soil ? 1f : 0.5f);
            if (soil)
                _marks?.Throw(_digAt + Vector3.Up * 0.03f, Vector3.Up * 1.2f, 5);
        }
        // The clod rides on the blade from the lever to the throw, with whatever worms were in it.
        _load.Visible = soil && _t >= 0.58f && _t < 0.88f;
        int worms = haul == null ? 0 : Math.Min(3, haul.Total);
        for (int i = 0; i < _worms.Count; i++)
            _worms[i].Visible = _load.Visible && _t >= 0.66f && i < worms;
        if (!_tossed && _t >= 0.88f)
        {
            _tossed = true;
            if (haul == null)
                return;
            if (soil)
            {
                var side = _player.GlobalTransform.Basis.X;
                _marks?.Throw(_load.GlobalPosition, side * 2.2f + Vector3.Up * 1.6f, 9);
                if (!haul.Message.StartsWith("Tu już"))
                    _marks?.AddHole(_digAt, side);
            }
            Finish(haul);
        }
    }

    private void SweepEvents()
    {
        if (!_swished && _t >= 0.24f)
        {
            _swished = true;
            _rod.Cue(Core.Audio.OneShot.Swish, 0.9f);
        }
        if (!_caught && _t >= 0.5f)
        {
            _caught = true;
            var ring = _net.GlobalTransform * new Vector3(0f, CampProps.NetPoleLength + CampProps.NetRingRadius, 0f);
            bool nearWater = false;
            foreach (var o in new[] { Vector3.Zero, new Vector3(2.5f, 0, 0), new Vector3(-2.5f, 0, 0), new Vector3(0, 0, 2.5f), new Vector3(0, 0, -2.5f) })
                nearWater |= _lake.HeightAt(ring.X + o.X, ring.Z + o.Z) < 0.05f;
            var ground = _lake.GroundKindAt(ring.X, ring.Z);
            var groundAt = _lake.GroundAt(_lake.Location, ring.X, ring.Z, _lake.HeightAt(ring.X, ring.Z));
            float swarm = _swarms?.DensityAt(ring) ?? 0f;
            var haul = Sweeping.Sweep(new SweepContext(ground, nearWater, groundAt.Z > 0.3f || ground == GroundKind.Forest,
                _world.Hour, _world.Weather, swarm), _rng);
            if (haul.Baits.ContainsKey(Sweeping.Mayfly))
                _swarms?.Deplete(ring, 0.18f);
            Finish(haul);
        }
    }

    private void Finish(Haul haul)
    {
        LastHaul = haul;
        _profile.AddBait(haul.Baits);
        _rod.Notify(haul.Message, 2.8f);
        if (haul.Total > 0)
            _rod.Cue(Core.Audio.OneShot.Click, 0.6f);
    }

    // ------------------------------------------------------------------ lift net
    private static readonly Vector3 ButtUp = new(0.11f, 0.93f, -0.09f), ButtDown = new(0.08f, 1.04f, -0.36f);

    private static readonly Dictionary<string, string> FryFiles = new()
    {
        ["ukleja"] = "ukleja", ["płotka"] = "ploc", ["kiełb"] = "kielb", ["słonecznica"] = "slonecznica", ["okonek"] = "okon", ["wzdręga"] = "wzdrega",
    };

    /// <summary>Tests and screenshots: adds seconds to the lift net's soak.</summary>
    public void AddSoak(float seconds) => _soak += seconds;

    private bool UseLiftNet()
    {
        switch (Lift)
        {
            case LiftStage.Up:
                return LowerLiftNet();
            case LiftStage.Down:
                StartLifting();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Where the lowered net would hang: the pole's pitch, the water's depth there and the spot.</summary>
    private (float Pitch, float Depth, Vector3 Spot) PlanLower()
    {
        var feet = _player.GlobalPosition;
        var fwd = Forward.Normalized();
        float reach = 2.5f, pitch = -0.3f, depth = 0f;
        var spot = feet;
        for (int i = 0; i < 3; i++)
        {
            spot = feet + fwd * reach;
            depth = -_lake.HeightAt(spot.X, spot.Z);
            float sink = Math.Clamp(depth - 0.1f, 0.15f, 1.3f);
            float tipY = CampProps.LiftDrop - sink - feet.Y;
            pitch = Math.Clamp(MathF.Asin(Math.Clamp((tipY - ButtDown.Y) / CampProps.LiftPoleLength, -1f, 1f)), PitchLowest, 0.2f);
            reach = -ButtDown.Z + CampProps.LiftPoleLength * MathF.Cos(pitch);
        }
        return (pitch, depth, spot);
    }

    private bool LowerLiftNet()
    {
        var plan = PlanLower();
        if (plan.Depth < LiftNetting.MinDepth)
        {
            _rod.Notify(plan.Depth > 0f
                ? "Za płytko na podrywkę: wejdź głębiej w wodę albo stań na końcu pomostu."
                : "Podrywkę opuszcza się do wody: stań nad brzegiem albo na pomoście, twarzą do wody.", 3f);
            return false;
        }
        _pitchDown = plan.Pitch;
        _groundbait = _profile.BaitCounts.GetValueOrDefault("bread") > 0;
        if (_groundbait)
            _profile.AddBait(new Dictionary<string, int> { ["bread"] = -1 });
        Lift = LiftStage.Lowering;
        _liftT = 0f;
        _soak = 0f;
        _liftHaul = null;
        _player.Busy = true;
        _rod.Notify(_groundbait ? "Podrywka idzie do wody, nad nią kruszysz kromkę chleba." : "Podrywka idzie do wody.", 2.5f);
        return true;
    }

    private void StartLifting()
    {
        var spot = _liftNet.GlobalPosition;
        float depth = MathF.Max(-_lake.HeightAt(spot.X, spot.Z), 0f);
        _liftHaul = LiftNetting.Lift(new LiftContext(_soak, depth, _world.Hour, _world.Weather,
            Math.Clamp(_lake.Location.LilyDensity * 0.5f, 0f, 1f), _groundbait), _rng);
        Lift = LiftStage.Lifting;
        _liftT = 0f;
        _rod.Cue(Core.Audio.OneShot.Swish, 0.5f);
    }

    private static float Ease(float t) => Smooth(Math.Clamp(t, 0f, 1f));

    private static float EaseOut(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return 1f - (1f - t) * (1f - t);
    }

    private float LiftPitch()
    {
        float hold = ShowHold / ShowSeconds;
        return Lift switch
        {
            LiftStage.Lowering => Mathf.Lerp(PitchUp, _pitchDown, Ease(_liftT)),
            LiftStage.Down => _pitchDown,
            LiftStage.Lifting => Mathf.Lerp(_pitchDown, PitchShow, EaseOut(_liftT)),
            LiftStage.Showing => _liftT < hold ? PitchShow : Mathf.Lerp(PitchShow, PitchUp, Ease((_liftT - hold) / (1f - hold))),
            _ => PitchUp,
        };
    }

    private void ProcessLiftNet(float dt)
    {
        float before = _liftT;
        switch (Lift)
        {
            case LiftStage.Lowering:
                _liftT += dt / LowerSeconds;
                _soak += _liftT > 0.7f ? dt : 0f;
                if (_liftT >= 1f)
                {
                    Lift = LiftStage.Down;
                    _rod.Notify("Podrywka na dnie. Odczekaj chwilę, aż drobnica wpłynie nad siatkę, i poderwij (LPM).", 3f);
                }
                break;
            case LiftStage.Down:
                _soak += dt;
                break;
            case LiftStage.Lifting:
                _liftT += dt / LiftSeconds;
                if (_liftT >= 1f)
                {
                    Lift = LiftStage.Showing;
                    _liftT = 0f;
                }
                break;
            case LiftStage.Showing:
                _liftT += dt / ShowSeconds;
                if (FreezeAt is float stop)
                    _liftT = MathF.Min(_liftT, stop);
                if (before < ShowHold / ShowSeconds && _liftT >= ShowHold / ShowSeconds && _liftHaul != null)
                {
                    // The fish go into the bait bucket.
                    Finish(_liftHaul);
                    _liftHaul = null;
                    foreach (var f in _fry)
                        f.Visible = false;
                }
                if (_liftT >= 1f)
                {
                    Lift = LiftStage.Up;
                    _liftT = 0f;
                }
                break;
        }

        // The pole: the butt in the hands, pitched down to the water or up to carry.
        float pitch = LiftPitch();
        float k = Math.Clamp((PitchUp - pitch) / (PitchUp + 0.35f), 0f, 1f);
        // Carried, the pole leans up to the right so the net hangs out of the way; lowered, it points ahead.
        var dir = new Vector3(Mathf.Lerp(0.3f, -0.04f, Smooth(k)), MathF.Sin(pitch), -MathF.Cos(pitch)).Normalized();
        var x = dir.Cross(Vector3.Up).Normalized();
        _liftPole.Transform = new Transform3D(new Basis(x, dir, x.Cross(dir)), ButtUp.Lerp(ButtDown, Smooth(k)));

        // The net hangs from the tip on its cord and swings behind as the tip moves (slowly in the water).
        var tip = _liftPole.GlobalTransform * new Vector3(0f, CampProps.LiftPoleLength, 0f);
        var tipVel = dt > 0f ? (tip - _lastTip) / dt : Vector3.Zero;
        if (tipVel.Length() > 20f)
            tipVel = Vector3.Zero;   // the first frame, or a teleport
        _lastTip = tip;
        float bottom = tip.Y - CampProps.LiftDrop;
        bool wet = bottom < -0.05f;
        var lag = -new Vector3(tipVel.X, 0f, tipVel.Z) * (wet ? 0.3f : 0.12f);
        _swingVel += ((lag - _swing) * 14f - _swingVel * (wet ? 10f : 2.2f)) * dt;
        _swing += _swingVel * dt;
        if (_swing.Length() > 0.6f)
            _swing = _swing.Normalized() * 0.6f;
        var hang = new Vector3(_swing.X, -CampProps.LiftDrop, _swing.Z).Normalized();
        var up = -hang;
        var side = _player.GlobalTransform.Basis.X;
        side = (side - up * side.Dot(up)).Normalized();
        var at = tip;
        // The netting rests on the bottom when the cord is longer than the water is deep.
        float bed = _lake.HeightAt(tip.X + hang.X * CampProps.LiftDrop, tip.Z + hang.Z * CampProps.LiftDrop);
        if (bottom < bed + 0.03f)
            at.Y += bed + 0.03f - bottom;
        _liftNet.GlobalTransform = new Transform3D(new Basis(side, up, side.Cross(up)), at);

        bool wasWet = _liftWet;
        _liftWet = at.Y - CampProps.LiftDrop < 0f;
        if (_liftWet != wasWet && Lift != LiftStage.Up)
            _rod.Cue(_liftWet ? Core.Audio.OneShot.SmallSplash : Core.Audio.OneShot.Splash, _liftWet ? 0.7f : 0.45f);

        // The catch flaps in the netting once it is out of the water.
        bool show = _liftHaul is { Total: > 0 } && !_liftWet && Lift is LiftStage.Lifting or LiftStage.Showing;
        if (show && _fry.Count == 0)
            MakeFry();
        int count = _liftHaul == null ? 0 : Math.Min(_liftHaul.Total, _fry.Count);
        float time = (float)Time.GetTicksMsec() / 1000f;
        for (int i = 0; i < _fry.Count; i++)
        {
            var f = _fry[i];
            f.Visible = show && i < count;
            if (!f.Visible)
                continue;
            float ph = i * 1.7f;
            float jump = MathF.Max(0f, MathF.Sin(time * 7f + ph * 2.3f));
            f.Position = new Vector3(MathF.Cos(ph) * 0.13f * (0.5f + i % 3 * 0.25f), 0.012f + jump * jump * 0.06f, MathF.Sin(ph) * 0.12f);
            f.Rotation = new Vector3(MathF.Sin(time * 11f + ph) * 0.4f * jump, ph * 1.9f, Mathf.Pi / 2f + MathF.Sin(time * 17f + ph) * 0.9f * jump);
        }

        // The eyes follow the net down to the water and back; the body bends towards it and stays put.
        var eye = _player.Camera.GlobalPosition;
        var target = at + Vector3.Down * CampProps.LiftDrop * 0.8f;
        var d = target - eye;
        float look = MathF.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
        float want = Lift == LiftStage.Up ? 0f : 1f;
        _blend = Mathf.MoveToward(_blend, want, dt * (want > _blend ? 1.8f : 1.2f));
        _player.WorkPitch = Math.Clamp(look, -0.85f, 0.15f);
        _player.WorkOffset = LiftLean * Smooth(k);
        _player.WorkBlend = Smooth(_blend);
        _player.Busy = Lift != LiftStage.Up;
    }

    private bool _liftWet;

    private void MakeFry()
    {
        foreach (var (name, _) in LiftNetting.Fry)
        {
            string file = $"res://data/fish/{FryFiles.GetValueOrDefault(name, "ukleja")}.tres";
            if (!ResourceLoader.Exists(file))
                file = "res://data/fish/ukleja.tres";
            var sp = GD.Load<FishSpecies>(file);
            var holder = new Node3D { Visible = false };
            var model = FishModel.Create(sp, 0.035f + _fry.Count % 3 * 0.012f);
            model.Wiggle = 2.5f;
            foreach (var n in model.FindChildren("*", "GeometryInstance3D", true, false))
                ((GeometryInstance3D)n).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            holder.AddChild(model);
            _liftCatch.AddChild(holder);
            _fry.Add(holder);
        }
    }

    private void SwingBag(float dt)
    {
        // The bag trails behind the ring as it moves.
        var ring = _net.GlobalTransform * new Vector3(0f, CampProps.NetPoleLength + CampProps.NetRingRadius, 0f);
        var v = dt > 0f ? (ring - _lastRing) / dt : Vector3.Zero;
        _lastRing = ring;
        var local = _net.GlobalTransform.Basis.Inverse() * v;
        _bagX = Mathf.Lerp(_bagX, Math.Clamp(local.Z * 0.12f, -0.7f, 0.7f), Math.Min(dt * 6f, 1f));
        _bagZ = Mathf.Lerp(_bagZ, Math.Clamp(-local.X * 0.12f, -0.7f, 0.7f), Math.Min(dt * 6f, 1f));
        _bag.Rotation = new Vector3(_bagX, 0f, _bagZ);
    }

    private static float Smooth(float x) => x * x * (3f - 2f * x);

    /// <summary>The tool's transform at a point of the action (Catmull-Rom through the keys).</summary>
    private static Transform3D Pose(Key[] keys, float t)
    {
        int i = 0;
        while (i < keys.Length - 2 && t > keys[i + 1].T)
            i++;
        var k1 = keys[i];
        var k2 = keys[i + 1];
        var k0 = keys[Math.Max(i - 1, 0)];
        var k3 = keys[Math.Min(i + 2, keys.Length - 1)];
        float u = Math.Clamp((t - k1.T) / MathF.Max(k2.T - k1.T, 1e-4f), 0f, 1f);
        var at = CatmullRom(k0.At, k1.At, k2.At, k3.At, u);
        var dir = CatmullRom(k0.Dir.Normalized(), k1.Dir.Normalized(), k2.Dir.Normalized(), k3.Dir.Normalized(), u).Normalized();
        float roll = Mathf.Lerp(k1.Roll, k2.Roll, Smooth(u));
        var y = dir;
        var back = Vector3.Back - y * Vector3.Back.Dot(y);
        var z = back.LengthSquared() > 1e-6f ? back.Normalized() : Vector3.Up.Cross(y).Normalized();
        var x = y.Cross(z).Normalized();
        var basis = new Basis(y, roll) * new Basis(x, y, z);
        return new Transform3D(basis, at);
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }

    /// <summary>Where the hands hold the tool in hand now (world space), or null with the rod.</summary>
    public (FirstPersonBody.HandTarget Right, FirstPersonBody.HandTarget Left)? HandPose()
    {
        if (Current == HandTool.Rod || _player == null)
            return null;
        var body = _player.GlobalTransform;
        var bodyBasis = body.Basis.Orthonormalized();
        var rightPole = body * new Vector3(0.6f, 0.85f, 0.35f);
        var leftPole = body * new Vector3(-0.6f, 0.85f, 0.35f);
        // The free left hand hangs at the side, palm in.
        var rest = new FirstPersonBody.HandTarget(body * new Vector3(-0.27f, 0.82f, -0.04f), bodyBasis * new Vector3(0.05f, -1f, -0.1f),
            bodyBasis * new Vector3(1f, 0f, 0.1f), 0.35f, leftPole);
        if (Current == HandTool.Shovel)
        {
            var tr = _shovel.GlobalTransform;
            var right = Grip(tr, CampProps.ShovelTopGrip, -Vector3.Right, Vector3.Down, true, 0.014f, rightPole, 0.9f);
            var left = Grip(tr, CampProps.ShovelShaftGrip, Vector3.Up, Vector3.Right, false, CampProps.ShovelHandleRadius, leftPole, 0.85f);
            return (right, Blend(rest, left, Smooth(_blend)));
        }
        if (Current == HandTool.LiftNet)
        {
            // The right hand presses the butt down from above, the left one carries the pole from below.
            var tl = _liftPole.GlobalTransform;
            return (Grip(tl, CampProps.LiftButtGrip, Vector3.Up, Vector3.Forward, true, CampProps.LiftPoleRadius * 1.1f, rightPole, 0.9f),
                Grip(tl, CampProps.LiftPoleGrip, Vector3.Up, Vector3.Back, false, CampProps.LiftPoleRadius, leftPole, 0.85f));
        }
        var tn = _net.GlobalTransform;
        return (Grip(tn, CampProps.NetEndGrip, Vector3.Up, Vector3.Left, true, CampProps.NetPoleRadius, rightPole, 0.9f), rest);
    }

    private static FirstPersonBody.HandTarget Blend(FirstPersonBody.HandTarget a, FirstPersonBody.HandTarget b, float k) => new(
        a.Wrist.Lerp(b.Wrist, k), a.Fingers.Normalized().Lerp(b.Fingers.Normalized(), k).Normalized(),
        a.Palm.Normalized().Lerp(b.Palm.Normalized(), k).Normalized(), Mathf.Lerp(a.Grip, b.Grip, k), a.Pole.Lerp(b.Pole, k));

    /// <summary>Where the arms' model stands with a tool in hand: the eyes' place in the body frame, bent over the work.</summary>
    public Transform3D? BodyFrame()
    {
        if (Current == HandTool.Rod || _player == null)
            return null;
        // The body bends over the work further than the head goes (Player.WorkOffset); it does not take the camera's pitch.
        float k = Current == HandTool.Shovel ? _player.WorkBlend : 0f;
        var eyes = new Transform3D(Basis.Identity, _player.Camera.Position - DigHead * k + DigLean * k);
        if (Current == HandTool.LiftNet)
            eyes = new Transform3D(Basis.Identity, _player.Camera.Position);
        return _player.GlobalTransform * eyes * _player.Body.ModelFromEyes;
    }

    /// <summary>
    /// A hand closed round a bar: <paramref name="along"/> is the way the index finger lies along it and
    /// <paramref name="palm"/> the way the palm faces (both in the tool's space); the wrist sits behind the knuckles.
    /// </summary>
    private static FirstPersonBody.HandTarget Grip(Transform3D tool, Vector3 point, Vector3 along, Vector3 palm, bool right, float radius,
        Vector3 pole, float grip)
    {
        var b = tool.Basis.Orthonormalized();
        var s = (b * along).Normalized();
        var p = (b * palm).Normalized();
        var f = right ? p.Cross(s) : s.Cross(p);
        var at = tool * point;
        return new FirstPersonBody.HandTarget(at - p * (radius + 0.025f) - f * 0.065f, f, p, grip, pole);
    }
}
