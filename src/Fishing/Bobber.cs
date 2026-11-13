using System;
using Godot;
using FishingSim.Core.Methods;

namespace FishingSim.Fishing;

/// <summary>
/// The end of the line: a floating bobber with simple buoyancy and water damping (frozen, kinematic, while stowed or
/// during a fight). It flies the cast for every method; once in the water a lure or a feeder is moved by the rod
/// (<see cref="FishingRod"/> holds it where the lure runs or the feeder lies). It shows the float, the lure (spinner,
/// spoon, wobbler, jig, fly; animated by <see cref="LureSpeed"/> and facing <see cref="Heading"/>) or the feeder.
/// </summary>
public partial class Bobber : RigidBody3D
{
    private const float BodyRadius = 0.05f;

    private Node3D _visual = null!;
    private Node3D _float = null!, _lureRoot = null!, _feeder = null!;
    private Node3D? _lure, _spin, _tail;
    private Node3D _feederBait = null!;
    private FishingMethod _method = FishingMethod.Float;
    private LureFamily _family;
    private float _time;

    /// <summary>The lure's speed through the water (m/s): spins the blade, rocks the body, wags the tail.</summary>
    public float LureSpeed { get; set; }

    /// <summary>The way the lure or the feeder points (yaw, radians; +Z of the model faces it).</summary>
    public float Heading { get; set; }

    /// <summary>The feeder carries groundbait (shown in its cage).</summary>
    public bool FeederLoaded { get; set; }

    /// <summary>False hides the lure or the feeder (in the fish's mouth during the fight).</summary>
    public bool TerminalVisible { get; set; } = true;

    public FishingMethod Method => _method;
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
        _float = new Node3D();
        _visual.AddChild(_float);
        _lureRoot = new Node3D { Visible = false, Scale = new Vector3(1.4f, 1.4f, 1.4f) };
        _visual.AddChild(_lureRoot);
        _feeder = BuildFeeder();
        _feeder.Visible = false;
        _visual.AddChild(_feeder);
        // Slightly self-lit colours so the float still reads at dusk and against glare.
        var red = Glowing(new Color(0.95f, 0.1f, 0.06f), 0.35f);
        var white = Glowing(new Color(0.96f, 0.96f, 0.92f), 0.2f);
        var orange = Glowing(new Color(1f, 0.6f, 0.05f), 0.6f);
        _float.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.034f, Height = 0.07f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = red,
        });
        _float.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.009f, Height = 0.1f, RadialSegments = 8, Rings = 1 },
            MaterialOverride = white,
            Position = new Vector3(0, 0.07f, 0),
        });
        _float.AddChild(new MeshInstance3D
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
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
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
        float dt = (float)delta;
        _time += dt;
        _visual.GlobalPosition = GlobalPosition;
        _visual.GlobalRotation = Vector3.Zero;
        _float.Visible = _method == FishingMethod.Float;
        _lureRoot.Visible = _method == FishingMethod.Spinning && TerminalVisible;
        _feeder.Visible = _method == FishingMethod.Bottom && TerminalVisible;
        if (_float.Visible)
        {
            var cam = GetViewport()?.GetCamera3D();
            float k = cam == null ? 1f : VisualScale(cam.GlobalPosition.DistanceTo(GlobalPosition));
            _float.Scale = new Vector3(k, k, k);
        }
        else if (_lureRoot.Visible)
        {
            // Like the float, the lure is drawn a little larger further out (up to 2.5x), so it can be followed in
            // clear water as it comes in.
            var cam = GetViewport()?.GetCamera3D();
            float k = 1.4f * (cam == null ? 1f : Math.Clamp(cam.GlobalPosition.DistanceTo(GlobalPosition) / 5f, 1f, 2.5f));
            _lureRoot.Scale = new Vector3(k, k, k);
            AnimateLure(dt);
        }
        else if (_feeder.Visible)
        {
            _feeder.Rotation = new Vector3(0f, Heading, 0f);
            _feederBait.Visible = FeederLoaded;
        }
    }

    // ------------------------------------------------------------------ lures and the feeder
    /// <summary>Chooses what hangs on the line for the next cast.</summary>
    public void SetTerminal(FishingMethod method, LureFamily family)
    {
        _method = method;
        TerminalVisible = true;
        LureSpeed = 0f;
        if (method != FishingMethod.Spinning || (_lure != null && family == _family))
            return;
        _family = family;
        _lure?.QueueFree();
        _lure = BuildLure(family, out _spin, out _tail);
        _lureRoot.AddChild(_lure);
    }

    /// <summary>The lure works as it is drawn: the spinner's blade turns round the wire, a spoon rolls and swings,
    /// a wobbler rocks from side to side, a jig's paddle tail wags, a fly sits still on the surface film.</summary>
    private void AnimateLure(float dt)
    {
        if (_lure == null)
            return;
        float v = Math.Clamp(LureSpeed, 0f, 2.5f);
        float roll = 0f, yaw = 0f;
        switch (_family)
        {
            case LureFamily.Spinner:
                if (_spin != null)
                    _spin.Rotation = new Vector3(0f, 0f, _spin.Rotation.Z + dt * (4f + 28f * v));
                break;
            case LureFamily.Spoon:
                roll = MathF.Sin(_time * (3f + 7f * v)) * (0.15f + 0.5f * MathF.Min(v, 1f));
                yaw = MathF.Sin(_time * (3f + 7f * v) + 1.2f) * 0.3f * MathF.Min(v, 1f);
                break;
            case LureFamily.Wobbler:
                roll = MathF.Sin(_time * (4f + 9f * v)) * 0.4f * MathF.Min(v * 1.5f, 1f);
                yaw = MathF.Sin(_time * (4f + 9f * v) + 1.5f) * 0.12f * MathF.Min(v * 1.5f, 1f);
                break;
            case LureFamily.Softbait:
                if (_tail != null)
                    _tail.Rotation = new Vector3(0f, MathF.Sin(_time * (5f + 12f * v)) * (0.15f + 0.6f * MathF.Min(v, 1f)), 0f);
                break;
            default:
                yaw = MathF.Sin(_time * 0.7f) * 0.1f;
                break;
        }
        _lure.Rotation = new Vector3(0f, Heading + yaw, roll);
    }

    private static StandardMaterial3D Metal(Color c, float rough = 0.25f) => new() { AlbedoColor = c, Metallic = 0.95f, Roughness = rough };

    private static StandardMaterial3D Paint(Color c, float rough = 0.45f) => new() { AlbedoColor = c, Roughness = rough };

    private static MeshInstance3D Part(Node3D parent, Mesh mesh, Material mat, Vector3 at, Vector3? rot = null, Vector3? scale = null)
    {
        var mi = new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = mat, Position = at, Rotation = rot ?? Vector3.Zero, Scale = scale ?? Vector3.One,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>A small treble hook hanging at a point (three bent points round a shank).</summary>
    private static void Treble(Node3D parent, Vector3 at, float size)
    {
        var steel = Metal(new Color(0.3f, 0.3f, 0.32f), 0.35f);
        var hook = new Node3D { Position = at };
        parent.AddChild(hook);
        Part(hook, new TorusMesh { InnerRadius = size * 0.12f, OuterRadius = size * 0.2f, Rings = 8, RingSegments = 4 }, steel,
            new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, Mathf.Pi / 2f));
        Part(hook, new CylinderMesh { TopRadius = size * 0.04f, BottomRadius = size * 0.04f, Height = size * 0.8f, RadialSegments = 4 }, steel,
            new Vector3(0f, 0f, -size * 0.45f), new Vector3(Mathf.Pi / 2f, 0f, 0f));
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * Mathf.Tau;
            var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
            Part(hook, new TorusMesh { InnerRadius = size * 0.26f, OuterRadius = size * 0.32f, Rings = 10, RingSegments = 4 }, steel,
                new Vector3(0f, 0f, -size * 0.85f) + dir * size * 0.28f, new Vector3(0f, Mathf.Pi / 2f, a), new Vector3(1f, 1f, 0.6f));
        }
    }

    /// <summary>One lure, nose towards +Z, the line's eye at the origin, about 5-7 cm long.</summary>
    private static Node3D BuildLure(LureFamily family, out Node3D? spin, out Node3D? tail)
    {
        spin = null;
        tail = null;
        var root = new Node3D { Name = "Lure" };
        var wire = Metal(new Color(0.78f, 0.78f, 0.8f), 0.2f);
        switch (family)
        {
            case LureFamily.Spinner:
            {
                // A wire shaft, a clevis with the blade turning round it, brass beads and body, a treble at the back.
                Part(root, new CylinderMesh { TopRadius = 0.0007f, BottomRadius = 0.0007f, Height = 0.06f, RadialSegments = 4 }, wire,
                    new Vector3(0f, 0f, -0.03f), new Vector3(Mathf.Pi / 2f, 0f, 0f));
                var brass = Metal(new Color(0.85f, 0.66f, 0.3f));
                Part(root, new SphereMesh { Radius = 0.0035f, Height = 0.007f, RadialSegments = 8, Rings = 4 }, brass, new Vector3(0f, 0f, -0.018f));
                Part(root, new SphereMesh { Radius = 0.0045f, Height = 0.018f, RadialSegments = 10, Rings = 6 }, brass, new Vector3(0f, 0f, -0.03f));
                Part(root, new SphereMesh { Radius = 0.0025f, Height = 0.005f, RadialSegments = 8, Rings = 4 }, Paint(new Color(0.85f, 0.1f, 0.08f)), new Vector3(0f, 0f, -0.04f));
                spin = new Node3D { Position = new Vector3(0f, 0f, -0.008f) };
                root.AddChild(spin);
                var blade = Part(spin, new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 14, Rings = 6 }, Metal(new Color(0.92f, 0.93f, 0.95f), 0.12f),
                    new Vector3(0.0075f, 0f, -0.012f), new Vector3(0f, 0.35f, 0f), new Vector3(0.012f, 0.0018f, 0.024f));
                blade.Name = "Blade";
                Treble(root, new Vector3(0f, 0f, -0.062f), 0.012f);
                break;
            }
            case LureFamily.Spoon:
            {
                // A curved brass plate, polished inside, a split ring at each end and the treble.
                var plate = Part(root, new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 18, Rings = 8, IsHemisphere = true },
                    Metal(new Color(0.9f, 0.72f, 0.35f), 0.18f), new Vector3(0f, 0f, -0.028f), new Vector3(Mathf.Pi, 0f, 0f), new Vector3(0.019f, 0.006f, 0.052f));
                plate.Name = "Plate";
                Part(root, new SphereMesh { Radius = 0.003f, Height = 0.001f, RadialSegments = 10, Rings = 2 }, Paint(new Color(0.8f, 0.08f, 0.06f)), new Vector3(0f, 0.0005f, -0.035f));
                Part(root, new TorusMesh { InnerRadius = 0.002f, OuterRadius = 0.003f, Rings = 8, RingSegments = 4 }, wire, new Vector3(0f, 0f, -0.002f), new Vector3(0f, 0f, Mathf.Pi / 2f));
                Treble(root, new Vector3(0f, 0f, -0.058f), 0.012f);
                break;
            }
            case LureFamily.Wobbler:
            {
                // A painted wooden fish: green back, yellow flanks, a clear diving lip under the nose, eyes, two trebles.
                var bodyMat = Paint(new Color(0.42f, 0.55f, 0.18f), 0.35f);
                Part(root, new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 10 }, bodyMat,
                    new Vector3(0f, 0f, -0.028f), null, new Vector3(0.013f, 0.017f, 0.056f));
                Part(root, new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 10 }, Paint(new Color(0.95f, 0.78f, 0.2f), 0.35f),
                    new Vector3(0f, -0.003f, -0.028f), null, new Vector3(0.0136f, 0.012f, 0.05f));
                foreach (float z in new[] { -0.018f, -0.03f, -0.042f })
                    Part(root, new BoxMesh { Size = new Vector3(0.0138f, 0.009f, 0.0025f) }, Paint(new Color(0.12f, 0.2f, 0.06f)), new Vector3(0f, 0.004f, z));
                Part(root, new BoxMesh { Size = new Vector3(0.012f, 0.0012f, 0.016f) },
                    new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.95f, 1f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f },
                    new Vector3(0f, -0.006f, 0.005f), new Vector3(0.6f, 0f, 0f));
                foreach (float x in new[] { -0.0062f, 0.0062f })
                    Part(root, new SphereMesh { Radius = 0.0022f, Height = 0.0044f, RadialSegments = 8, Rings = 4 }, Paint(new Color(0.05f, 0.04f, 0.03f), 0.1f), new Vector3(x, 0.003f, -0.008f));
                Treble(root, new Vector3(0f, -0.012f, -0.02f), 0.009f);
                Treble(root, new Vector3(0f, -0.004f, -0.06f), 0.009f);
                break;
            }
            case LureFamily.Softbait:
            {
                // A lead head painted red with the hook's point up through the back, a pale soft body and a paddle tail.
                Part(root, new SphereMesh { Radius = 0.0068f, Height = 0.0136f, RadialSegments = 12, Rings = 6 }, Paint(new Color(0.75f, 0.12f, 0.1f), 0.4f), new Vector3(0f, 0f, -0.005f));
                Part(root, new SphereMesh { Radius = 0.0018f, Height = 0.0036f, RadialSegments = 8, Rings = 4 }, Paint(new Color(0.95f, 0.85f, 0.2f), 0.2f), new Vector3(0.0055f, 0.002f, -0.003f));
                var soft = Paint(new Color(0.9f, 0.92f, 0.8f), 0.6f);
                Part(root, new CylinderMesh { TopRadius = 0.005f, BottomRadius = 0.0035f, Height = 0.045f, RadialSegments = 10, Rings = 4 }, soft,
                    new Vector3(0f, 0f, -0.033f), new Vector3(-Mathf.Pi / 2f, 0f, 0f));
                Part(root, new CylinderMesh { TopRadius = 0.0006f, BottomRadius = 0.0006f, Height = 0.012f, RadialSegments = 4 }, Metal(new Color(0.35f, 0.35f, 0.37f)),
                    new Vector3(0f, 0.006f, -0.03f), new Vector3(0.25f, 0f, 0f));
                tail = new Node3D { Position = new Vector3(0f, 0f, -0.055f) };
                root.AddChild(tail);
                Part(tail, new CylinderMesh { TopRadius = 0.0035f, BottomRadius = 0.0022f, Height = 0.008f, RadialSegments = 8 }, soft,
                    new Vector3(0f, 0f, -0.004f), new Vector3(-Mathf.Pi / 2f, 0f, 0f));
                Part(tail, new BoxMesh { Size = new Vector3(0.0025f, 0.012f, 0.009f) }, soft, new Vector3(0f, 0f, -0.011f));
                break;
            }
            default:
            {
                // A fly: a dark body wound on the hook, a ruff of cock's hackle, a short tail.
                Part(root, new CylinderMesh { TopRadius = 0.002f, BottomRadius = 0.0025f, Height = 0.014f, RadialSegments = 8 }, Paint(new Color(0.18f, 0.1f, 0.05f), 0.8f),
                    new Vector3(0f, 0f, -0.008f), new Vector3(Mathf.Pi / 2f, 0f, 0f));
                var hackle = Paint(new Color(0.75f, 0.45f, 0.18f), 0.9f);
                for (int i = 0; i < 10; i++)
                {
                    float a = i / 10f * Mathf.Tau;
                    Part(root, new CylinderMesh { TopRadius = 0.0002f, BottomRadius = 0.0006f, Height = 0.012f, RadialSegments = 3 }, hackle,
                        new Vector3(MathF.Cos(a) * 0.005f, MathF.Sin(a) * 0.005f, -0.001f), new Vector3(0f, 0f, a - Mathf.Pi / 2f));
                }
                Part(root, new CylinderMesh { TopRadius = 0.0003f, BottomRadius = 0.0012f, Height = 0.012f, RadialSegments = 4 }, hackle,
                    new Vector3(0f, 0.001f, -0.02f), new Vector3(Mathf.Pi / 2f + 0.2f, 0f, 0f));
                Part(root, new TorusMesh { InnerRadius = 0.003f, OuterRadius = 0.0036f, Rings = 10, RingSegments = 4 }, Metal(new Color(0.3f, 0.3f, 0.32f)),
                    new Vector3(0f, -0.004f, -0.014f), new Vector3(0f, Mathf.Pi / 2f, 0f), new Vector3(1f, 1f, 0.5f));
                break;
            }
        }
        return root;
    }

    /// <summary>The feeder: an open green cage with a lead strip along its bottom, lying on its side, and its groundbait.</summary>
    private Node3D BuildFeeder()
    {
        var root = new Node3D { Name = "Feeder", Scale = new Vector3(1.3f, 1.3f, 1.3f) };
        var cage = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.3f, 0.14f), Roughness = 0.5f, Metallic = 0.3f };
        var lead = Metal(new Color(0.42f, 0.42f, 0.44f), 0.6f);
        var lying = new Vector3(Mathf.Pi / 2f, 0f, 0f);
        foreach (float z in new[] { -0.02f, 0f, 0.02f })
            Part(root, new TorusMesh { InnerRadius = 0.0135f, OuterRadius = 0.0155f, Rings = 16, RingSegments = 4 }, cage,
                new Vector3(0f, 0.015f, z), lying);
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.Tau;
            Part(root, new CylinderMesh { TopRadius = 0.0008f, BottomRadius = 0.0008f, Height = 0.042f, RadialSegments = 4 }, cage,
                new Vector3(MathF.Cos(a) * 0.0145f, 0.015f + MathF.Sin(a) * 0.0145f, 0f), lying);
        }
        Part(root, new BoxMesh { Size = new Vector3(0.012f, 0.004f, 0.044f) }, lead, new Vector3(0f, 0.001f, 0f));
        _feederBait = Part(root, new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.036f, RadialSegments = 10 },
            Paint(new Color(0.42f, 0.3f, 0.17f), 0.95f), new Vector3(0f, 0.015f, 0f), lying);
        Part(root, new TorusMesh { InnerRadius = 0.0025f, OuterRadius = 0.0035f, Rings = 8, RingSegments = 4 }, Metal(new Color(0.7f, 0.7f, 0.72f)),
            new Vector3(0f, 0.03f, 0.024f));
        return root;
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
