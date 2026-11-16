using System;
using System.Collections.Generic;
using Godot;

namespace FishingSim.World.Props;

/// <summary>
/// Makes a villager walk about the village by the time of day: in the day from stop to stop (their spot, the square,
/// a neighbour's door, the well...) at a walking pace with the Walk_Loop animation, lingering at each stop; after dusk
/// they go home (the first stop, a door) and go in (hidden) until the morning. Follows the ground (LakeBed.HeightAt),
/// turns to face where it walks. Throttled: beyond 90 m from the camera a walker just jumps along without animating.
/// </summary>
public partial class Wanderer : Node
{
    private const float WalkSpeed = 1.15f;

    private Npc _npc = null!;
    private LakeBed _bed = null!;
    private WorldState _world = null!;
    private readonly List<Vector2> _stops = new();
    private int _target;
    private float _linger;
    private bool _walking, _home;
    private AnimationPlayer? _animator;
    private string _idle = "Idle_Loop";

    /// <summary>The villager is walking right now (tests).</summary>
    public bool Walking => _walking;

    /// <summary>Indoors for the night (tests).</summary>
    public bool Home => _home;

    /// <summary>What the villager says when the angler stops beside them (null = silent).</summary>
    public Func<string>? Talk { get; set; }

    /// <summary>A villager spoke to the angler.</summary>
    public static event Action<string>? Said;

    private float _talkWait, _skip;

    /// <summary>Day starts / ends (game hours).</summary>
    public float DayFrom { get; set; } = 6.5f;
    public float DayTo { get; set; } = 20.5f;

    public static Wanderer Attach(Npc npc, LakeBed bed, WorldState world, IReadOnlyList<Vector2> stops, int seed)
    {
        var w = new Wanderer { _npc = npc, _bed = bed, _world = world, Name = "Wanderer" };
        w._stops.AddRange(stops);
        w._target = stops.Count > 1 ? 1 + seed % (stops.Count - 1) : 0;
        w._linger = 2f + seed % 7;
        npc.AddChild(w);
        return w;
    }

    public override void _Ready()
    {
        _animator = CharacterAsset.PlayerOf(_npc);
        if (_animator != null && _animator.CurrentAnimation != "")
            _idle = _animator.CurrentAnimation;
    }

    private void Play(string name)
    {
        if (_animator != null && _animator.HasAnimation(name) && _animator.CurrentAnimation != name)
            _animator.Play(name, 0.3);
    }

    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("villagers");
        if (_stops.Count == 0)
            return;
        // Far from the camera (or out of sight) a walker is updated four times a second, not every frame.
        _skip += (float)delta;
        var cam = GetViewport()?.GetCamera3D();
        bool far = cam == null || cam.GlobalPosition.DistanceSquaredTo(_npc.GlobalPosition) > 70f * 70f;
        if (far && _skip < 0.25f)
            return;
        float dt = MathF.Min(far ? _skip : (float)delta, 0.3f);
        _skip = 0f;
        float hour = _world.Hour;
        bool day = hour >= DayFrom && hour < DayTo;
        if (!day)
        {
            // Evening: home along the way, then in through the door.
            if (_home)
                return;
            _target = 0;
        }
        else if (_home)
        {
            _home = false;
            _npc.Visible = true;
            _linger = 1f;
        }
        var p = new Vector2(_npc.GlobalPosition.X, _npc.GlobalPosition.Z);
        var goal = _stops[_target];
        var to = goal - p;
        float dist = to.Length();
        if (dist < 0.3f)
        {
            if (_walking)
            {
                _walking = false;
                Play(_idle);
            }
            if (!day && _target == 0)
            {
                _home = true;
                _npc.Visible = false;
                return;
            }
            // The angler stops beside a lingering villager: a word or two, at most once a minute.
            _talkWait -= dt;
            if (Talk != null && _talkWait <= 0f && cam != null
                && cam.GlobalPosition.DistanceTo(_npc.GlobalPosition + Vector3.Up * 1.5f) < 2.6f)
            {
                _talkWait = 60f;
                _linger = MathF.Max(_linger, 6f);
                Said?.Invoke(Talk());
                if (_animator != null && _animator.HasAnimation("Idle_Talking_Loop"))
                    Play("Idle_Talking_Loop");
            }
            _linger -= dt;
            if (_linger <= 0f)
            {
                // On to another stop (not the same one), after lingering 6-30 s at this one.
                int next = (int)(GD.Randi() % (uint)_stops.Count);
                _target = next == _target ? (next + 1) % _stops.Count : next;
                _linger = 6f + GD.Randf() * 24f;
            }
            return;
        }
        if (!_walking)
        {
            _walking = true;
            Play("Walk_Loop");
        }
        var step = to / dist * MathF.Min(WalkSpeed * dt, dist);
        var np = p + step;
        float h = _bed.HeightAt(_bed.Location, np.X, np.Y);
        _npc.GlobalPosition = new Vector3(np.X, h, np.Y);
        // Turn towards the way ahead (the figure faces +Z).
        float yaw = MathF.Atan2(to.X, to.Y);
        _npc.GlobalRotation = new Vector3(0f, Mathf.LerpAngle(_npc.GlobalRotation.Y, yaw, MathF.Min(dt * 6f, 1f)), 0f);
    }
}
