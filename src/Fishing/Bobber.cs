using System;
using Godot;

namespace FishingSim.Fishing;

/// <summary>Floating bobber: simple buoyancy and water damping. Frozen (kinematic) while stowed or during a fight.</summary>
public partial class Bobber : RigidBody3D
{
    private const float BodyRadius = 0.05f;

    private Node3D _visual = null!;
    private bool _pendingLaunch;
    private Vector3 _launchPosition;
    private Vector3 _launchVelocity;

    public float WaterY { get; set; }

    /// <summary>Optional (x, z) -> wave height offset, so the bobber rides the waves.</summary>
    public Func<float, float, float>? WaterHeight { get; set; }

    /// <summary>Horizontal velocity target while in water (used to reel the bobber in).</summary>
    public Vector3 DriveVelocity { get; set; }

    /// <summary>Downward pull as a multiple of the bobber's weight (bites).</summary>
    public float ExtraForce { get; set; }

    /// <summary>0 = fully above water, 1 = fully under.</summary>
    public float Submerged { get; private set; }

    public override void _Ready()
    {
        Mass = 0.05f;
        CanSleep = false;
        CollisionLayer = 0;
        CollisionMask = 1;
        FreezeMode = FreezeModeEnum.Kinematic;
        AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = BodyRadius } });
        BuildVisual();
        Stow();
    }

    /// <summary>Classic float (red head, white stem, orange tip). Stays upright and follows the body.</summary>
    private void BuildVisual()
    {
        _visual = new Node3D { TopLevel = true };
        AddChild(_visual);
        // Slightly self-lit colours so the float still reads at dusk and against glare.
        var red = Glowing(new Color(0.95f, 0.1f, 0.06f), 0.35f);
        var white = Glowing(new Color(0.96f, 0.96f, 0.92f), 0.2f);
        var orange = Glowing(new Color(1f, 0.6f, 0.05f), 0.6f);
        _visual.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.034f, Height = 0.07f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = red,
        });
        _visual.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.009f, Height = 0.1f, RadialSegments = 8, Rings = 1 },
            MaterialOverride = white,
            Position = new Vector3(0, 0.07f, 0),
        });
        _visual.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.004f, BottomRadius = 0.006f, Height = 0.045f, RadialSegments = 8, Rings = 1 },
            MaterialOverride = orange,
            Position = new Vector3(0, 0.145f, 0),
        });
    }

    private static StandardMaterial3D Glowing(Color color, float glow) => new()
    {
        AlbedoColor = color,
        Roughness = 0.4f,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = glow,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
    };

    /// <summary>
    /// The float is drawn larger the further it is from the camera (1x up to 6 m, up to 7x far out), so it stays a
    /// few pixels tall at any casting distance, like in most fishing games. Physics are not affected.
    /// </summary>
    public static float VisualScale(float distance) => Math.Clamp(distance / 6f, 1f, 7f);

    public void Launch(Vector3 position, Vector3 velocity)
    {
        Visible = true;
        // Move first (while frozen), so the first physics frame already sees the launch position instead of the
        // stale one; the velocity is applied inside the physics step.
        Freeze = true;
        GlobalPosition = position;
        Submerged = 0f;
        Freeze = false;
        DriveVelocity = Vector3.Zero;
        ExtraForce = 0f;
        _pendingLaunch = true;
        _launchPosition = position;
        _launchVelocity = velocity;
    }

    /// <summary>Pins the bobber at a point (used while a fish is fighting).</summary>
    public void Hold(Vector3 at)
    {
        Freeze = true;
        GlobalPosition = at;
    }

    public void Stow()
    {
        Visible = false;
        Freeze = true;
        DriveVelocity = Vector3.Zero;
        ExtraForce = 0f;
        Submerged = 0f;
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        if (!_pendingLaunch)
            return;
        state.Transform = new Transform3D(Basis.Identity, _launchPosition);
        state.LinearVelocity = _launchVelocity;
        state.AngularVelocity = Vector3.Zero;
        _pendingLaunch = false;
    }

    public override void _Process(double delta)
    {
        _visual.GlobalPosition = GlobalPosition;
        _visual.GlobalRotation = Vector3.Zero;
        var cam = GetViewport()?.GetCamera3D();
        float k = cam == null ? 1f : VisualScale(cam.GlobalPosition.DistanceTo(GlobalPosition));
        _visual.Scale = new Vector3(k, k, k);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Freeze)
            return;
        float waterLevel = WaterY;
        if (WaterHeight != null)
            waterLevel += WaterHeight(GlobalPosition.X, GlobalPosition.Z);
        Submerged = Math.Clamp((waterLevel + BodyRadius - GlobalPosition.Y) / (BodyRadius * 2f), 0f, 1f);
        float weight = Mass * 9.8f;
        // Buoyancy: equals the weight when half-submerged.
        ApplyCentralForce(Vector3.Up * weight * 2f * Submerged);
        if (Submerged > 0f)
        {
            if (ExtraForce > 0f)
                ApplyCentralForce(Vector3.Down * weight * ExtraForce);
            if (DriveVelocity != Vector3.Zero)
                ApplyCentralForce(DriveVelocity * Mass * LinearDamp);
        }
        LinearDamp = Mathf.Lerp(0.05f, 6f, Submerged);
        AngularDamp = Mathf.Lerp(0.5f, 4f, Submerged);
    }
}
