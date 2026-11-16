using System;
using Godot;
using FishingSim.Fishing;

namespace FishingSim.World;

/// <summary>
/// The angler's rowboat, moored beside the pier: E boards it (the angler sits on the after thwart facing the bow),
/// W/S row ahead and astern, A/D turn (one oar pulls, the other backs), Shift pulls harder; the oars sweep through their
/// strokes in the rowlocks, the boat carries on and slows in the water, rides the waves (heave, pitch and roll from
/// <see cref="Water.HeightAt"/>) and stops at the shallows (under 0.35 m of water), the pier and the map's edge. E again
/// steps out onto the pier or the dry bank when one is near. One can fish from it like from the bank.
/// </summary>
public partial class Rowboat : Node3D
{
    /// <summary>The angler is in a boat (rod rests and the like refuse).</summary>
    public static bool Aboard { get; private set; }

    private const float Draft = 0.17f;              // the keel this far under the water
    private const float HalfLength = 1.8f, HalfBeam = 0.72f;
    private const float StrokeHz = 0.85f, HardStrokeHz = 1.15f;
    private const float Thrust = 1.7f, TurnThrust = 2.6f;          // peak accelerations of a stroke (m/s², rad/s²)
    private const float DragAhead = 0.55f, DragSide = 2.6f, DragTurn = 1.8f;
    private const float MinDepth = 0.35f;

    private LakeBed _bed = null!;
    private Water _water = null!;
    private Node3D _hull = null!;
    private Node3D? _oarL, _oarR;
    private float _heading, _turnRate, _phaseL, _phaseR, _rowL, _rowR;
    private Vector2 _velocity;
    private Vector3 _mooring;
    private float _mooringHeading;
    private Player? _rower;

    /// <summary>The E spot (follows the boat).</summary>
    public Interactable Spot { get; private set; } = null!;

    /// <summary>Speed over the water (m/s; tests).</summary>
    public float Speed => _velocity.Length();

    /// <summary>The boat's heading: the bow points along (sin, 0, cos) of it.</summary>
    public float Heading => _heading;

    public Vector3 Forward => new(MathF.Sin(_heading), 0f, MathF.Cos(_heading));

    /// <summary>Who sits in the boat now.</summary>
    public Player? Rower => _rower;

    /// <summary>
    /// Asked when the rower wants to row: true when the hands are free for the oars (Main puts an idle rod away; a rod
    /// fishing keeps them busy and the boat stays put).
    /// </summary>
    public Func<bool>? HandsFree { get; set; }

    /// <summary>The oars' handles (where the hands hold them), for the arms.</summary>
    public Node3D? GripL => _oarL?.GetNodeOrNull<Node3D>("Grip") ?? _oarL?.GetChild<Node3D>(0)?.GetNodeOrNull<Node3D>("Grip");

    public Node3D? GripR => _oarR?.GetNodeOrNull<Node3D>("Grip") ?? _oarR?.GetChild<Node3D>(0)?.GetNodeOrNull<Node3D>("Grip");

    /// <summary>The wind over the water (m/s, x/z); without the anchor it drifts the boat downwind.</summary>
    public Func<Vector2>? Wind { get; set; }

    /// <summary>The anchor is down: the boat holds its place (it swings a little on the rope).</summary>
    public bool Anchored { get; private set; }

    private Node3D? _anchorRope;
    private Vector3 _anchorAt;

    /// <summary>X in the boat: drops the anchor or weighs it (null = done, else why not).</summary>
    public string? ToggleAnchor()
    {
        if (_rower == null)
            return "Kotwicę rzuca się z łódki";
        if (Anchored)
        {
            Anchored = false;
            if (_anchorRope != null)
                _anchorRope.Visible = false;
            return null;
        }
        var p = GlobalPosition;
        float depth = _bed.DepthAt(p.X, p.Z);
        if (depth > 12f)
            return "Za głęboko na kotwicę (lina 12 m)";
        Anchored = true;
        _anchorAt = new Vector3(p.X, Main.WaterY - depth, p.Z);
        _velocity *= 0.2f;
        if (_anchorRope == null)
        {
            _anchorRope = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.008f, Height = 1f, RadialSegments = 6, Rings = 1 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.48f, 0.34f), Roughness = 0.95f },
            };
            GetParent().AddChild(_anchorRope);
        }
        _anchorRope.Visible = true;
        return null;
    }

    /// <summary>The row input for tests: (ahead -1..1, turn left -1..1); null = the keys.</summary>
    public Vector2? RowOverride { get; set; }

    public void Setup(LakeBed bed, Water water)
    {
        _bed = bed;
        _water = water;
        _hull = Props.Everyday.Rowboat(1, rowing: true);
        AddChild(_hull);
        _oarL = _hull.GetNodeOrNull<Node3D>("OarL");
        _oarR = _hull.GetNodeOrNull<Node3D>("OarR");
        Spot = new Interactable { Kind = InteractKind.Boat, Title = "wsiądź do łódki", Radius = 2.6f, Target = this, Position = new Vector3(0f, 0.5f, 0f) };
        AddChild(Spot);
        ResetToMooring();
    }

    /// <summary>Back to its berth beside the pier (map change, respawn): the angler steps out first.</summary>
    public void ResetToMooring()
    {
        if (_rower != null)
            PutAshore(_rower, Player.SpawnPoint);
        // On the pier's right, bow out to the lake, where the water is deep enough.
        _mooring = new Vector3(2.75f, 0f, -1.5f);
        for (int i = 0; i < 12 && _bed.Location != null && _bed.DepthAt(_mooring.X, _mooring.Z + HalfLength) < MinDepth + 0.2f; i++)
            _mooring.Z -= 1f;
        _mooringHeading = MathF.PI;                     // bow towards -Z (out over the lake)
        Anchored = false;
        if (_anchorRope != null)
            _anchorRope.Visible = false;
        Position = _mooring;
        _heading = _mooringHeading;
        _velocity = Vector2.Zero;
        _turnRate = 0f;
        Float(0f);
    }

    /// <summary>E at the boat: board it, or step out when ashore is near (null = done, else why not).</summary>
    public string? Toggle(Player player)
    {
        if (_rower == null)
        {
            _rower = player;
            Aboard = true;
            player.Boat = this;
            // Facing the bow (the player looks along its -Z).
            player.Rotation = new Vector3(0f, _heading + MathF.PI, 0f);
            Spot.Title = "wysiądź";
            return null;
        }
        if (FindLanding(out var landing))
        {
            PutAshore(player, landing);
            return null;
        }
        return "Podpłyń do pomostu albo do brzegu, żeby wysiąść";
    }

    private void PutAshore(Player player, Vector3 at)
    {
        player.Boat = null;
        player.GlobalPosition = at;
        player.Velocity = Vector3.Zero;
        _rower = null;
        Aboard = false;
        Anchored = false;
        if (_anchorRope != null)
            _anchorRope.Visible = false;
        Spot.Title = "wsiądź do łódki";
        _rowL = _rowR = 0f;
    }

    /// <summary>A place to step out: the pier within reach, else dry walkable bank within 3.5 m.</summary>
    public bool FindLanding(out Vector3 at)
    {
        var p = GlobalPosition;
        // The pier: x within 1.6 m of its axis, z from -4.2 to 6.2 (LakeBed.OnPier); the deck is at 1.05 m.
        float px = Math.Clamp(p.X, -1.2f, 1.2f), pz = Math.Clamp(p.Z, -4.0f, 6.0f);
        if (new Vector2(p.X - px, p.Z - pz).Length() < 3.4f)
        {
            at = new Vector3(px, Player.SpawnPoint.Y, pz);
            return true;
        }
        for (float r = 2f; r <= 3.5f; r += 0.75f)
            for (int i = 0; i < 16; i++)
            {
                float a = i * MathF.Tau / 16f;
                float x = p.X + MathF.Cos(a) * r, z = p.Z + MathF.Sin(a) * r;
                if (_bed.IsWalkable(x, z) && _bed.DepthAt(x, z) <= 0.02f)
                {
                    at = new Vector3(x, _bed.HeightAt(_bed.Location, x, z) + 0.15f, z);
                    return true;
                }
            }
        at = default;
        return false;
    }

    /// <summary>The thwart the angler sits on, in world space (hips).</summary>
    public Vector3 Seat => GlobalTransform * new Vector3(0f, 0.41f, -0.9f);

    public override void _PhysicsProcess(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("rowboat");
        float dt = (float)delta;
        // The oars: each one pulls (+1), backs (-1) or rests (0).
        float ahead = 0f, turn = 0f, hard = 0f;
        if (_rower != null && !_rower.Busy)
        {
            if (RowOverride is { } o)
                (ahead, turn) = (o.X, o.Y);
            else
            {
                if (Bindings.Held(Act.Forward)) ahead += 1f;
                if (Bindings.Held(Act.Back)) ahead -= 1f;
                if (Bindings.Held(Act.Left)) turn += 1f;
                if (Bindings.Held(Act.Right)) turn -= 1f;
                hard = Bindings.Held(Act.Sprint) ? 1f : 0f;
            }
        }
        // The oars need both hands: with a rod fishing they stay where they are.
        if ((ahead != 0f || turn != 0f) && HandsFree?.Invoke() == false)
            ahead = turn = hard = 0f;
        // Turning left: the right oar pulls (or pulls more), the left one backs.
        _rowR = Math.Clamp(ahead + turn, -1f, 1f);
        _rowL = Math.Clamp(ahead - turn, -1f, 1f);
        float hz = Mathf.Lerp(StrokeHz, HardStrokeHz, hard);
        float pushL = Stroke(ref _phaseL, _rowL, hz, dt), pushR = Stroke(ref _phaseR, _rowR, hz, dt);
        float power = 1f + 0.4f * hard;

        if (Anchored)
            ahead = turn = hard = 0f;
        var fwd = new Vector2(MathF.Sin(_heading), MathF.Cos(_heading));
        var side = new Vector2(fwd.Y, -fwd.X);
        float along = _velocity.Dot(fwd), across = _velocity.Dot(side);
        along += ((pushL + pushR) * 0.5f * Thrust * power - along * DragAhead) * dt;
        across -= across * DragSide * dt;
        _turnRate += ((pushR - pushL) * 0.5f * TurnThrust * power - _turnRate * DragTurn) * dt;
        _velocity = fwd * along + side * across;
        // The wind pushes the boat downwind (it settles at about a fifth of the wind's speed); the anchor rope holds
        // it within a metre of where the anchor lies, and it swings slowly bow to the wind.
        var wind = Wind?.Invoke() ?? Vector2.Zero;
        if (Anchored)
        {
            var off = new Vector2(Position.X - _anchorAt.X, Position.Z - _anchorAt.Z);
            _velocity += (-off * 0.8f - _velocity * 1.5f) * dt + wind * 0.04f * dt;
            if (wind.LengthSquared() > 0.01f)
                _turnRate += Mathf.AngleDifference(_heading, MathF.Atan2(-wind.X, -wind.Y)) * 0.05f * dt;
        }
        else if (_rower != null)
            _velocity += wind * 0.12f * dt;     // the hull's own drag sets the drift speed; a boat left at its berth is tied up

        // Move, unless the hull would touch the shallows, the pier or the edge: then it stops (a soft bump back).
        float newHeading = _heading + _turnRate * dt;
        var newPos = Position + new Vector3(_velocity.X, 0f, _velocity.Y) * dt;
        if (Clear(newPos, newHeading))
        {
            Position = newPos;
            _heading = newHeading;
        }
        else if (Clear(Position, newHeading))
        {
            _heading = newHeading;
            _velocity *= -0.15f;
        }
        else
        {
            _velocity *= -0.15f;
            _turnRate = 0f;
        }
        Float(dt);
        AnimateOars(dt);
        if (_anchorRope is { Visible: true })
        {
            // The rope from the bow down to the anchor.
            var bow = GlobalTransform * new Vector3(0f, 0.6f, HalfLength - 0.1f);
            var d = _anchorAt - bow;
            _anchorRope.GlobalTransform = new Transform3D(Basis.LookingAt(d.Normalized(), MathF.Abs(d.Normalized().Y) > 0.98f ? Vector3.Forward : Vector3.Up)
                * Basis.FromEuler(new Vector3(MathF.PI / 2f, 0f, 0f)) * Basis.FromScale(new Vector3(1f, d.Length(), 1f)), (bow + _anchorAt) / 2f);
        }
        if (_rower != null)
        {
            // The angler sits on the thwart and turns with the boat.
            _rower.GlobalPosition = Seat - Vector3.Up * Player.SeatedEyeDrop;
        }
    }

    /// <summary>The hull's outline (bow, stern, the beam's ends, the quarters) clear of shallows, the pier and the edge.</summary>
    private bool Clear(Vector3 pos, float heading)
    {
        if (_bed.Location == null)
            return true;
        var f = new Vector2(MathF.Sin(heading), MathF.Cos(heading));
        var s = new Vector2(f.Y, -f.X);
        Span<Vector2> probes = stackalloc Vector2[]
        {
            f * HalfLength, -f * HalfLength, s * HalfBeam, -s * HalfBeam,
            f * (HalfLength * 0.55f) + s * (HalfBeam * 0.8f), f * (HalfLength * 0.55f) - s * (HalfBeam * 0.8f),
            -f * (HalfLength * 0.8f) + s * (HalfBeam * 0.8f), -f * (HalfLength * 0.8f) - s * (HalfBeam * 0.8f),
        };
        foreach (var o in probes)
        {
            float x = pos.X + o.X, z = pos.Z + o.Y;
            if (MathF.Abs(x) <= 1.75f && z >= -4.4f && z <= 6.4f)      // the pier and its posts
                return false;
            if (_bed.DepthAt(x, z) < MinDepth || _bed.PlayDistance(x, z) > -1f)
                return false;
        }
        return true;
    }

    /// <summary>One oar's stroke: the phase runs while it rows; the push is the drive half (blade in the water).</summary>
    private static float Stroke(ref float phase, float row, float hz, float dt)
    {
        if (MathF.Abs(row) < 0.01f)
        {
            // Resting: the oar comes back to the start of a stroke (blade forward, ready).
            phase = Mathf.MoveToward(phase, 0f, dt * 3f);
            return 0f;
        }
        phase = (phase + dt * hz * MathF.Tau) % MathF.Tau;
        float drive = MathF.Max(MathF.Sin(phase), 0f);
        return row * drive;
    }

    private void AnimateOars(float dt)
    {
        void Oar(Node3D? pivot, float side, float phase, float row)
        {
            if (pivot == null)
                return;
            float sweep, dip;
            if (MathF.Abs(row) < 0.01f && phase < 0.01f)
            {
                // At rest: the blades trail aft, flat on the water.
                sweep = -0.45f;
                dip = 0.3f;
            }
            else
            {
                // Pulling: blade from forward to aft in the water, back through the air; backing: the other way.
                sweep = 0.42f * MathF.Cos(phase) * MathF.Sign(row == 0f ? 1f : row);
                dip = 0.06f + 0.3f * Smooth(-0.25f, 0.25f, MathF.Sin(phase));
            }
            var target = new Vector3(0f, -side * sweep, -side * dip);
            pivot.Rotation = pivot.Rotation.Lerp(target, MathF.Min(dt * 12f, 1f));
        }
        Oar(_oarL, -1f, _phaseL, _rowL);
        Oar(_oarR, 1f, _phaseR, _rowR);
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Rides the waves: heave from the mean of four points round the hull, pitch and roll from their differences.</summary>
    private void Float(float dt)
    {
        var p = Position;
        var f = Forward;
        var s = new Vector3(f.Z, 0f, -f.X);
        float H(Vector3 o) => _water.HeightAt(p.X + o.X, p.Z + o.Z);
        float bow = H(f * HalfLength), stern = H(-f * HalfLength), right = H(-s * HalfBeam), left = H(s * HalfBeam);
        float mean = (bow + stern + right + left) * 0.25f;
        float pitch = -MathF.Atan2(bow - stern, HalfLength * 2f);
        float roll = MathF.Atan2(right - left, HalfBeam * 2f);
        // A little extra heel into a turn.
        roll += Math.Clamp(_turnRate * Speed * 0.04f, -0.06f, 0.06f);
        var basis = new Basis(Vector3.Up, _heading) * new Basis(Vector3.Right, pitch) * new Basis(Vector3.Back, roll);
        GlobalTransform = new Transform3D(basis, new Vector3(p.X, Main.WaterY + mean - Draft, p.Z));
    }

    public override void _ExitTree() => Aboard = false;

    /// <summary>A message for the angler (the anchor dropped or why not).</summary>
    public event Action<string>? Said;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_rower == null || !Bindings.Is(@event, Act.Anchor))
            return;
        Said?.Invoke(ToggleAnchor() ?? (Anchored ? "Kotwica rzucona: łódka stoi" : "Kotwica podniesiona"));
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        // The angler's view turns with the boat (yaw only; the head stays level).
        if (_rower != null)
        {
            float yaw = _heading + MathF.PI;
            float lastYaw = _lastYaw ?? yaw;
            _rower.RotateY(Mathf.AngleDifference(lastYaw, yaw));
            _lastYaw = yaw;
        }
        else
            _lastYaw = null;
    }

    private float? _lastYaw;
}
