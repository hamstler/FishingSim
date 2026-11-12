using System;
using Godot;
using FishingSim.Core.Animation;
using FishingSim.World;

namespace FishingSim.Fishing;

/// <summary>
/// First-person angler (the game has no third-person camera by design). Builds its own camera, rod rig and visible
/// forearms; <see cref="BodyMotion"/> animates head bob, footsteps, breathing and the arms.
/// </summary>
public partial class Player : CharacterBody3D
{
    private const float WalkSpeed = 3.2f;
    private const float SprintSpeed = 5.8f;
    private const float MouseSensitivity = 0.0025f;
    private const float Gravity = 9.8f;
    // A last safety net: the real edge is LakeBed.PlayDistance (the forest at the foot of the mountains, see IsWalkable).
    private const float BoundsX = World.LakeBed.XMax;
    private const float BoundsZMin = World.LakeBed.ZMin;
    private const float BoundsZMax = World.LakeBed.ZMax;

    /// <summary>Where the player starts on every map: the end of the pier.</summary>
    public static readonly Vector3 SpawnPoint = new(0f, 1.1f, -3f);

    private const float EyeHeight = 1.6f;

    private Vector3 _lastValid;

    /// <summary>Procedural body animation (head bob, steps, arm pose).</summary>
    public BodyMotion Motion { get; } = new();

    /// <summary>What the hands are doing (set by Main from the rod state).</summary>
    public Func<BodyAction>? ActionSource { get; set; }

    public FirstPersonBody Body { get; private set; } = null!;

    /// <summary>The terrain, used to keep the player out of deep water.</summary>
    public LakeBed? Bed { get; set; }

    public Camera3D Camera { get; private set; } = null!;
    public RodRig Rig { get; private set; } = null!;
    public Node3D RodTip => Rig.Tip;

    public override void _Ready()
    {
        CollisionLayer = 2;
        CollisionMask = 1;
        FloorSnapLength = 0.6f;
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
        });
        Camera = new Camera3D { Position = new Vector3(0, EyeHeight, 0) };
        AddChild(Camera);
        Camera.Current = true;
        Rig = new RodRig();
        Camera.AddChild(Rig);
        Body = new FirstPersonBody();
        Body.Setup(Camera, Rig);
        Camera.AddChild(Body);
        _lastValid = Position;
    }

    /// <summary>Puts the player back on the pier, facing the water.</summary>
    public void Respawn()
    {
        GlobalPosition = SpawnPoint;
        _lastValid = SpawnPoint;
        Velocity = Vector3.Zero;
        Rotation = Vector3.Zero;
        Camera.Rotation = Vector3.Zero;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-motion.Relative.X * MouseSensitivity);
            float pitch = Math.Clamp(Camera.Rotation.X - motion.Relative.Y * MouseSensitivity, -1.4f, 1.4f);
            Camera.Rotation = new Vector3(pitch, 0f, Camera.Rotation.Z);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("player");
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W)) dir.Y -= 1f;
        if (Input.IsKeyPressed(Key.S)) dir.Y += 1f;
        if (Input.IsKeyPressed(Key.A)) dir.X -= 1f;
        if (Input.IsKeyPressed(Key.D)) dir.X += 1f;
        var move = (GlobalTransform.Basis * new Vector3(dir.X, 0f, dir.Y)).Normalized();
        var v = Velocity;
        float speed = Input.IsKeyPressed(Key.Shift) ? SprintSpeed : WalkSpeed;
        v.X = move.X * speed;
        v.Z = move.Z * speed;
        v.Y = IsOnFloor() ? 0f : v.Y - Gravity * (float)delta;
        Velocity = v;
        MoveAndSlide();
        TryStepUp(new Vector3(move.X, 0f, move.Z) * speed * (float)delta);
        var p = Position;
        p.X = Math.Clamp(p.X, -BoundsX, BoundsX);
        p.Z = Math.Clamp(p.Z, BoundsZMin, BoundsZMax);
        // Deep water is a wall: step back to the last place that was fine (the pier counts as fine).
        if (Bed != null && !Bed.IsWalkable(p.X, p.Z))
        {
            p.X = _lastValid.X;
            p.Z = _lastValid.Z;
            Velocity = new Vector3(0f, Velocity.Y, 0f);
        }
        else
        {
            _lastValid = p;
        }
        Position = p;
        Animate((float)delta);
    }

    /// <summary>Highest ledge (m) the angler steps onto without jumping: pier deck, door steps, kerbs.</summary>
    public const float StepHeight = 0.8f;

    /// <summary>
    /// Walking into a low ledge (for example the pier deck from the sand beside it) lifts the angler onto it, as long as
    /// there is room above it and the ledge is not taller than <see cref="StepHeight"/>.
    /// </summary>
    private void TryStepUp(Vector3 motion)
    {
        if (motion.LengthSquared() < 1e-8f || !IsOnWall())
            return;
        var start = GlobalTransform;
        var up = Vector3.Up * StepHeight;
        if (TestMove(start, up))
            return;                                   // no headroom
        var raised = start.Translated(up);
        var ahead = motion.Normalized() * MathF.Max(motion.Length(), 0.12f);
        if (TestMove(raised, ahead))
            return;                                   // still blocked: it is a real wall
        GlobalTransform = raised.Translated(ahead);
        MoveAndCollide(Vector3.Down * (StepHeight + 0.05f));
    }

    private void Animate(float dt)
    {
        float speed = new Vector2(Velocity.X, Velocity.Z).Length();
        var action = ActionSource?.Invoke() ?? BodyAction.None;
        var pose = Motion.Update(dt, speed, IsOnFloor() || Bed == null, action);
        Camera.Position = new Vector3(pose.HeadBobX, EyeHeight + pose.HeadBobY + pose.Breath, 0f);
        Camera.Rotation = new Vector3(Camera.Rotation.X, 0f, pose.HeadRoll);
        Body.Apply(pose, action, dt);
    }
}
