using System;
using Godot;
using FishingSim.World;

namespace FishingSim.Fishing;

/// <summary>First-person angler standing on the pier. Builds its own camera and rod rig.</summary>
public partial class Player : CharacterBody3D
{
    private const float WalkSpeed = 3.2f;
    private const float SprintSpeed = 5.8f;
    private const float MouseSensitivity = 0.0025f;
    private const float Gravity = 9.8f;
    private const float BoundsX = 160f;
    private const float BoundsZMin = -190f;
    private const float BoundsZMax = 66f;

    /// <summary>Where the player starts on every map: the end of the pier.</summary>
    public static readonly Vector3 SpawnPoint = new(0f, 1.1f, -3f);

    private Vector3 _lastValid;

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
        Camera = new Camera3D { Position = new Vector3(0, 1.6f, 0) };
        AddChild(Camera);
        Camera.Current = true;
        Rig = new RodRig();
        Camera.AddChild(Rig);
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
            float pitch = Camera.Rotation.X - motion.Relative.Y * MouseSensitivity;
            Camera.Rotation = new Vector3(Math.Clamp(pitch, -1.4f, 1.4f), 0f, 0f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
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
    }
}
