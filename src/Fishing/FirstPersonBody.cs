using System;
using Godot;
using FishingSim.Core.Animation;
using FishingSim.World.Props;

namespace FishingSim.Fishing;

/// <summary>
/// The angler's arms in first person: the peasant villager's own arms (linen sleeves, real hands with fingers;
/// World/Props/CharacterAsset), the rest of the body hidden, its skeleton set so the eyes are at the camera. ArmIK
/// bends shoulder, elbow and wrist each frame so the right hand grips the rod handle (palm on the right of the blank,
/// fingers round under it with the reel foot between them, thumb along the top) and the left hand turns the reel
/// handle while reeling, otherwise hangs at the side (a landed fish is shown on the catch panel, not held up).
/// Without a screen (tests) the old built hands stand in. The rod is raised when the player holds the right mouse
/// button (strike, pumping, lifting the fish out).
/// </summary>
public partial class FirstPersonBody : Node3D
{
    // The rod in front of the body, a little right of the middle, so the left hand reaches the crank on the reel's left.
    private static readonly Vector3 RigBasePosition = new(0.1f, -0.25f, -0.33f);
    private static readonly Vector3 RigBaseRotation = new(Mathf.DegToRad(-78f), 0f, 0f);
    private static readonly Vector3 WristLocal = new(0.055f, 0.03f, -0.01f);
    private static readonly Vector3 LeftRest = new(-0.3f, -0.75f, -0.25f);

    private RodRig _rig = null!;
    private Node3D _camera = null!;
    private MeshInstance3D _rightArm = null!, _leftArm = null!, _rightCuff = null!, _leftCuff = null!;
    private Node3D _leftHand = null!;
    private StandardMaterial3D _skin = null!, _sleeve = null!;
    private float _leftOnReel, _raise, _crank, _leftOnFish;
    private Vector3 _lastFishGrip;
    private ArmIK? _ik;
    private Node3D? _model;
    private Transform3D _modelRest;

    /// <summary>
    /// With a tool in the hands the body stands in the player's frame (not the camera's: looking down at the work does
    /// not swing the shoulders back); this gives the model's world transform then (null with the rod).
    /// </summary>
    public Func<Transform3D?>? ToolBodyFrame { get; set; }

    /// <summary>The model's transform relative to the eyes (the camera), to place it in another frame.</summary>
    public Transform3D ModelFromEyes => _modelRest;
    private BodyAction _action;

    public override void _Process(double delta)
    {
        // The arms follow the rod every frame, whoever moves it.
        if (_ik != null && _rig != null)
            ApplyArms(_action, (float)delta);
    }

    /// <summary>Where the camera sits on the model (its eyes), in the model's own space (it faces +Z).</summary>
    private static readonly Vector3 Eyes = new(0f, 1.655f, 0.075f);
    private const float Lean = 0.2f;

    // The right hand's grip in rod-rig space (rod along +Y, reel hanging at -Z, +X to the right): the palm faces the
    // blank from the right, the knuckles run forward and down, so the fingers wrap under the handle.
    private static readonly Vector3 GripHandle = RodRig.GripPoint;
    private static readonly Vector3 GripPalm = new(-1f, 0f, 0.1f);
    // Fingers straight down the side of the seat, across the rod, so they wrap round it.
    private static readonly Vector3 GripFingers = new(0.05f, 0.18f, -1f);

    /// <summary>0..1 how far the rod should be raised (right mouse button); set by Main from the rod.</summary>
    public Func<float>? RaiseSource { get; set; }

    /// <summary>Where one hand should be (world space): wrist, the way the fingers and the palm point, grip, elbow hint.</summary>
    /// <summary>Where a hand goes; <paramref name="Bar"/> = the handle it closes round (the fingers stop on it).</summary>
    public readonly record struct HandTarget(Vector3 Wrist, Vector3 Fingers, Vector3 Palm, float Grip, Vector3 Pole,
        World.Props.ArmIK.Capsule? Bar = null);

    /// <summary>A tool in the hands (shovel, insect net): both hands go where it says instead of the rod.</summary>
    public Func<(HandTarget Right, HandTarget Left)?>? ToolPose { get; set; }

    /// <summary>A fish to hold in the left hand, or null (unused since the catch panel shows the landed fish).</summary>
    public Func<Node3D?>? HeldFishSource { get; set; }

    /// <summary>True while the reel handle should turn (set by Main from the rod).</summary>
    public Func<bool>? ReelingSource { get; set; }

    public void Setup(Node3D camera, RodRig rig)
    {
        _camera = camera;
        _rig = rig;
    }

    public override void _Ready()
    {
        if (BuildArms())
            return;
        _sleeve = BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.26f, 0.16f), Roughness = 0.95f });   // wool tunic
        _skin = BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.84f, 0.64f, 0.5f), Roughness = 0.7f });
        var cuffMat = BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.18f, 0.11f), Roughness = 0.95f });
        _rightArm = Part(this, new CylinderMesh { TopRadius = 0.042f, BottomRadius = 0.055f, Height = 1f, RadialSegments = 8 }, _sleeve);
        _leftArm = Part(this, new CylinderMesh { TopRadius = 0.042f, BottomRadius = 0.055f, Height = 1f, RadialSegments = 8 }, _sleeve);
        _rightCuff = Part(this, new CylinderMesh { TopRadius = 0.047f, BottomRadius = 0.047f, Height = 1f, RadialSegments = 8 }, cuffMat);
        _leftCuff = Part(this, new CylinderMesh { TopRadius = 0.047f, BottomRadius = 0.047f, Height = 1f, RadialSegments = 8 }, cuffMat);
        BuildRightHand();
        _leftHand = new Node3D { Scale = new Vector3(0.85f, 0.85f, 0.85f) };
        AddChild(_leftHand);
        BuildLeftHand(_leftHand);
    }

    /// <summary>The villager's arms, if the model can be shown; false leaves the built hands to do.</summary>
    private bool BuildArms()
    {
        var look = new Appearance("male_peasant", 0, null, Colors.White, new Color(1f, 0.97f, 0.95f), false, false, Colors.White);
        if (CharacterAsset.Create(look, idle: null) is not { } body || CharacterAsset.SkeletonOf(body) is not { } skeleton)
            return false;
        foreach (var node in body.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)node;
            mi.Visible = mi.Name == "Arms";
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mi.VisibilityRangeEnd = 0f;
            // The arms are always right in front of the eyes: never culled by their rest-pose bounds.
            mi.ExtraCullMargin = 2f;
        }
        body.RotationDegrees = new Vector3(0f, 180f, 0f);
        // A little forward of standing straight: leaning over the rod, the hands reach the handle with the elbows bent.
        body.Position = new Vector3(Eyes.X, -Eyes.Y, Eyes.Z - Lean);
        AddChild(body);
        _model = body;
        _modelRest = body.Transform;
        _ik = new ArmIK();
        skeleton.AddChild(_ik);
        return true;
    }

    private static MeshInstance3D Part(Node3D parent, Mesh mesh, Material mat, Vector3? pos = null, Vector3? rot = null)
    {
        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = pos ?? Vector3.Zero,
            Rotation = rot ?? Vector3.Zero,
        };
        parent.AddChild(mi);
        return mi;
    }

    private MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Vector3? rot = null) =>
        Part(parent, new BoxMesh { Size = size }, _skin, pos, rot);

    /// <summary>A fist closed around the handle, in rod-rig space (rod along +Y, reel hanging at -Z, +X to the right).</summary>
    private void BuildRightHand()
    {
        var hand = new Node3D { Name = "RightHand", Scale = new Vector3(0.85f, 0.85f, 0.85f), Position = new Vector3(-0.004f, 0.012f, 0f) };
        _rig.AddChild(hand);
        Box(hand, new Vector3(0.048f, 0.115f, 0.078f), new Vector3(0.042f, 0.097f, 0.004f));               // palm on the right of the blank
        foreach (float y in new[] { 0.052f, 0.077f, 0.122f, 0.147f })                                    // reel foot sits between 2nd and 3rd
        {
            Box(hand, new Vector3(0.058f, 0.022f, 0.025f), new Vector3(0.01f, y, -0.034f));                // finger wrapped under the rod
            Box(hand, new Vector3(0.022f, 0.021f, 0.042f), new Vector3(-0.03f, y, -0.012f));               // fingertip curling up the left side
            Box(hand, new Vector3(0.012f, 0.023f, 0.014f), new Vector3(-0.036f, y, 0.01f));                // nail joint
        }
        Box(hand, new Vector3(0.024f, 0.08f, 0.026f), new Vector3(0.004f, 0.16f, 0.03f), new Vector3(0f, 0f, 0.25f));   // thumb along the top
        Box(hand, new Vector3(0.062f, 0.072f, 0.068f), WristLocal);                                       // heel of the hand / wrist
    }

    /// <summary>A loose fist pinching the reel handle knob (thumb and index finger).</summary>
    private void BuildLeftHand(Node3D hand)
    {
        Box(hand, new Vector3(0.06f, 0.07f, 0.075f), new Vector3(-0.035f, 0f, 0.02f));
        foreach (float z in new[] { -0.012f, 0.012f, 0.036f })
            Box(hand, new Vector3(0.025f, 0.022f, 0.022f), new Vector3(-0.005f, -0.03f, z));
        Box(hand, new Vector3(0.022f, 0.05f, 0.022f), new Vector3(-0.005f, 0.035f, 0.0f), new Vector3(0f, 0f, -0.4f));
    }

    /// <summary>Applies one frame of pose. Positions are in the camera's local space.</summary>
    public void Apply(BodyPose pose, BodyAction action, float dt)
    {
        if (_rig == null || _rightArm == null && _ik == null)
            return;

        // The rod: raised with the right mouse button, lifted while casting/fighting, swaying a little with the gait.
        _raise = Mathf.MoveToward(_raise, Math.Clamp(RaiseSource?.Invoke() ?? 0f, 0f, 1f), dt * 4f);
        _rig.Position = RigBasePosition + new Vector3(pose.ArmSwing * 0.02f, pose.ArmLift * 0.06f + _raise * 0.07f, pose.Lean * 0.2f + _raise * 0.06f);
        _rig.Rotation = RigBaseRotation + new Vector3(pose.ArmLift * 0.3f + _raise * 0.65f, 0f, pose.ArmSwing * 0.04f);

        if (_ik != null)
        {
            _action = action;
            return;
        }
        if (_rightArm == null)
            return;

        // Right forearm: from the wrist back and down towards the elbow, below the view.
        var wrist = ToLocal(_rig.ToGlobal(WristLocal));
        var elbow = wrist + new Vector3(0.06f, -0.2f, 0.34f);
        Stretch(_rightArm, elbow, wrist + new Vector3(0.0f, -0.01f, 0.025f));
        Stretch(_rightCuff, wrist + new Vector3(0.01f, -0.035f, 0.065f), wrist + new Vector3(0.008f, -0.028f, 0.05f));

        // Left hand: turns the reel handle while reeling or fighting, otherwise rests out of view.
        bool onReel = action is BodyAction.Fight || (ReelingSource?.Invoke() ?? false);
        _leftOnReel = Mathf.MoveToward(_leftOnReel, onReel ? 1f : 0f, dt * 5f);
        if (ReelingSource?.Invoke() ?? false)
            _crank += dt * 9f;
        _rig.SetCrank(_crank);
        var knob = RodRig.KnobAt(_crank);
        var leftAt = LeftRest.Lerp(ToLocal(_rig.ToGlobal(knob)), _leftOnReel);
        // Holding the catch: the left hand supports the fish from below, under the belly behind the head, so the
        // fish itself stays in view.
        var held = HeldFishSource?.Invoke();
        _leftOnFish = Mathf.MoveToward(_leftOnFish, held != null ? 1f : 0f, dt * 6f);
        if (held is FishModel fm)
            _lastFishGrip = ToLocal(fm.ToGlobal(new Vector3(0f, -0.5f * fm.MaxHeight, 0.12f * fm.Length))) + new Vector3(0f, -0.02f, 0f);
        else if (held != null)
            _lastFishGrip = ToLocal(held.GlobalPosition) + new Vector3(0.02f, -0.03f, 0.02f);
        leftAt = leftAt.Lerp(_lastFishGrip, _leftOnFish);
        _leftHand.Position = leftAt;
        _leftHand.Rotation = new Vector3(0.3f, 0.4f, 0.2f);
        var leftElbow = leftAt + new Vector3(-0.12f, -0.22f, 0.3f);
        Stretch(_leftArm, leftElbow, leftAt + new Vector3(-0.04f, -0.02f, 0.05f));
        Stretch(_leftCuff, leftAt + new Vector3(-0.05f, -0.045f, 0.09f), leftAt + new Vector3(-0.045f, -0.035f, 0.075f));
    }

    /// <summary>The real arms: the right hand on the rod handle, the left on the reel, the fish or at the side.</summary>
    private void ApplyArms(BodyAction action, float dt)
    {
        var ik = _ik!;
        if (_model != null)
        {
            var frame = ToolBodyFrame?.Invoke();
            if (frame is Transform3D bodyFrame)
                _model.GlobalTransform = bodyFrame;
            else
                _model.Transform = _modelRest;

        }
        if (ToolPose?.Invoke() is { } tool)
        {
            foreach (var (arm, t) in new[] { (ik.Right, tool.Right), (ik.Left, tool.Left) })
            {
                arm.Wrist = t.Wrist;
                arm.Fingers = t.Fingers;
                arm.Palm = t.Palm;
                arm.Grip = t.Grip;
                arm.Pole = t.Pole;
                arm.Splay = 0f;
                arm.Obstacles.Clear();
                if (t.Bar is { } bar)
                    arm.Obstacles.Add(bar);
            }
            return;
        }
        var rigBasis = _rig.GlobalTransform.Basis.Orthonormalized();
        var palm = (rigBasis * GripPalm).Normalized();
        var fingers = (rigBasis * GripFingers).Normalized();
        ik.Right.Palm = palm;
        ik.Right.Fingers = fingers;
        // The hand closes round the reel seat, the reel's stem between the middle and ring fingers: the hand slides
        // along the rod until the gap between those knuckles lies over the stem, the two fingers spread a little, and
        // every finger closes only until it touches the seat (not into it).
        var wrist = _rig.ToGlobal(GripHandle) - palm * (RodRig.SeatRadius + 0.024f) - fingers * 0.062f;
        var rodAxis = (rigBasis * Vector3.Up).Normalized();
        if (_ik.KnuckleGap(ik.Right, fingers, palm) is Vector3 gap)
            wrist += rodAxis * (_rig.ToGlobal(RodRig.StemAt) - (wrist + gap)).Dot(rodAxis);
        ik.Right.Wrist = wrist;
        ik.Right.Splay = 0.11f;
        ik.Right.Obstacles.Clear();
        foreach (var (a, b, r) in RodRig.SeatObstacles())
            ik.Right.Obstacles.Add(new World.Props.ArmIK.Capsule(_rig.ToGlobal(a), _rig.ToGlobal(b), r));
        ik.Right.Pole = ToGlobal(new Vector3(0.5f, -0.6f, 0.35f));
        ik.Right.Grip = 0.9f;

        bool onReel = action is BodyAction.Fight || (ReelingSource?.Invoke() ?? false);
        _leftOnReel = Mathf.MoveToward(_leftOnReel, onReel ? 1f : 0f, dt * 5f);
        if (ReelingSource?.Invoke() ?? false)
            _crank += dt * 9f;
        _rig.SetCrank(_crank);
        // The crank is on the reel's left: the hand lies over its knob, palm down, fingers forward round it.
        var reelPalmRig = new Vector3(0.1f, 0.12f, -1f).Normalized();
        var reelFingersRig = new Vector3(0.1f, 1f, 0.12f).Normalized();
        var knob = RodRig.KnobAt(_crank) - reelPalmRig * 0.034f - reelFingersRig * 0.058f;
        var held = HeldFishSource?.Invoke();
        _leftOnFish = Mathf.MoveToward(_leftOnFish, held != null ? 1f : 0f, dt * 6f);
        if (held is FishModel fm)
            _lastFishGrip = ToLocal(fm.ToGlobal(new Vector3(0f, -0.5f * fm.MaxHeight, 0.12f * fm.Length))) + new Vector3(0f, -0.035f, 0f);
        else if (held != null)
            _lastFishGrip = ToLocal(held.GlobalPosition) + new Vector3(0.02f, -0.04f, 0.02f);
        // Hanging at the side; pinching the reel knob from above; flat under the fish's belly.
        var restAt = new Vector3(-0.26f, -0.78f, 0.05f);
        var reelAt = ToLocal(_rig.ToGlobal(knob));
        var at = restAt.Lerp(reelAt, _leftOnReel).Lerp(_lastFishGrip + new Vector3(0.03f, 0f, 0.08f), _leftOnFish);
        var basis = GlobalTransform.Basis.Orthonormalized();
        var restFingers = new Vector3(0.05f, -1f, -0.1f);
        var restPalm = new Vector3(1f, 0f, 0.1f);
        var reelFingers = basis.Inverse() * (rigBasis * reelFingersRig);
        var reelPalm = basis.Inverse() * (rigBasis * reelPalmRig);
        var fishFingers = new Vector3(0.15f, 0.1f, -1f);
        var fishPalm = new Vector3(0f, 1f, 0f);
        var f = restFingers.Normalized().Lerp(reelFingers.Normalized(), _leftOnReel).Lerp(fishFingers.Normalized(), _leftOnFish);
        var p = restPalm.Normalized().Lerp(reelPalm.Normalized(), _leftOnReel).Lerp(fishPalm, _leftOnFish);
        ik.Left.Wrist = ToGlobal(at);
        ik.Left.Fingers = basis * f;
        ik.Left.Palm = basis * p;
        ik.Left.Pole = ToGlobal(new Vector3(-0.6f, -0.55f, 0.25f));
        ik.Left.Grip = Mathf.Lerp(Mathf.Lerp(0.35f, 0.75f, _leftOnReel), 0.3f, _leftOnFish);
        // On the crank the fingers close round the knob and stop on it.
        ik.Left.Splay = 0f;
        ik.Left.Obstacles.Clear();
        if (_leftOnReel > 0.5f && _leftOnFish < 0.5f)
            foreach (var (a, b, r) in RodRig.CrankObstacles(_crank))
                ik.Left.Obstacles.Add(new World.Props.ArmIK.Capsule(_rig.ToGlobal(a), _rig.ToGlobal(b), r));
    }

    /// <summary>Places a unit-height cylinder so it runs from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static void Stretch(MeshInstance3D part, Vector3 a, Vector3 b)
    {
        var dir = b - a;
        float len = dir.Length();
        if (len < 1e-4f)
            return;
        var y = dir / len;
        var x = MathF.Abs(y.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : y.Cross(Vector3.Up).Normalized();
        var z = x.Cross(y).Normalized();
        part.Transform = new Transform3D(new Basis(x, y * len, z), (a + b) * 0.5f);
    }
}
