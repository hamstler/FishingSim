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
        using var perfScope = Graphics.Perf.Scope("hands");
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
        World.Props.ArmIK.Capsule? Bar = null, float BarWeight = 1f);

    /// <summary>A tool in the hands (shovel, insect net): both hands go where it says instead of the rod.</summary>
    public Func<(HandTarget Right, HandTarget Left)?>? ToolPose { get; set; }

    /// <summary>A fish to hold in the left hand, or null (unused since the catch panel shows the landed fish).</summary>
    public Func<Node3D?>? HeldFishSource { get; set; }

    /// <summary>True while the rod lies on its rest: the rig is left where it is and the arms are put away.</summary>
    public Func<bool>? RigOnRest { get; set; }

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
        if (RigOnRest?.Invoke() == true)
        {
            _action = action;
            return;
        }

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
        // The rod on its rest: nothing in the hands, the arms out of sight.
        bool resting = RigOnRest?.Invoke() == true && ToolPose?.Invoke() == null;
        if (_model != null)
            _model.Visible = !resting;
        if (resting)
            return;
        if (_model != null)
        {
            var frame = ToolBodyFrame?.Invoke();
            if (frame is Transform3D bodyFrame)
                _model.GlobalTransform = bodyFrame;
            else
                _model.Transform = _modelRest;
        }
        var bodyBasis = GlobalTransform.Basis.Orthonormalized();
        ArmShape Shape(ArmIK.Arm arm, Vector3 fallbackShoulder)
        {
            var len = ik.Lengths(arm) ?? (0.27f, 0.25f);
            return new ArmShape(ik.Shoulder(arm) ?? ToGlobal(fallbackShoulder), len.Upper, len.Fore, bodyBasis.X, bodyBasis.Y);
        }
        var rightArm = Shape(ik.Right, new Vector3(0.17f, -0.235f, -0.05f));
        var leftArm = Shape(ik.Left, new Vector3(-0.17f, -0.235f, -0.05f));

        if (ToolPose?.Invoke() is { } tool)
        {
            _rodRight.Has = _rodLeft.Has = false;
            foreach (var (arm, t, shape, state, right) in new[]
                     {
                         (ik.Right, tool.Right, rightArm, _toolRight, true), (ik.Left, tool.Left, leftArm, _toolLeft, false),
                     })
            {
                var target = t;
                if (t.Bar is { } bar && t.BarWeight > 0f)
                {
                    // The hand round the tool's handle in the most natural way it can take, blended in as it reaches it.
                    var g = BarGrip((bar.A + bar.B) * 0.5f, bar.B - bar.A, bar.Radius, right, shape, 0f, 0.25f, 0.02f, t.Grip,
                        0.065f, true, bar.A.DistanceTo(bar.B) * 0.5f, state, dt, t.Pole);
                    target = Mix(t, g, t.BarWeight);
                }
                else
                    state.Has = false;
                Apply(arm, target, 0f);
                arm.ThumbAlong = null;
            }
            return;
        }
        _toolRight.Has = _toolLeft.Has = false;
        var rigBasis = _rig.GlobalTransform.Basis.Orthonormalized();
        var rodAxis = (rigBasis * Vector3.Up).Normalized();
        // The right hand round the reel seat as anglers hold it: the palm on the seat's right side (the back of the hand
        // out), the thumb along the top of the blank, the index and middle fingers round the seat in front of the reel's
        // stem and the ring and little fingers behind it, under the seat; the elbow hanging down at the side, and of the
        // ways the hand can lie so, the one with the least bent wrist (a little towards the little finger, as anglers'
        // wrists are).
        var rightPole = ToGlobal(new Vector3(0.55f, -0.65f, 0.3f));
        var seat = BarGrip(_rig.ToGlobal(GripHandle), rodAxis, RodRig.SeatRadius, true, rightArm, Mathf.DegToRad(20f), Mathf.DegToRad(55f),
            0.012f, 0.92f, 0.068f, false, 0.07f, _rodRight, dt, rightPole, (rigBasis * new Vector3(-1f, 0f, 0.15f)).Normalized(),
            MathF.Cos(Mathf.DegToRad(35f)));
        if (_ik.KnuckleGap(ik.Right, seat.Fingers, seat.Palm) is Vector3 gap)
        {
            var shift = rodAxis * (_rig.ToGlobal(RodRig.StemAt) - (seat.Wrist + gap)).Dot(rodAxis);
            seat = seat with { Wrist = seat.Wrist + shift, Pole = seat.Pole + shift };
        }
        Apply(ik.Right, seat, 0.11f);
        ik.Right.ThumbAlong = (rodAxis + rigBasis * Vector3.Back * 0.15f).Normalized();
        ik.Right.Obstacles.Clear();
        foreach (var (a, b, r) in RodRig.SeatObstacles())
            ik.Right.Obstacles.Add(new ArmIK.Capsule(_rig.ToGlobal(a), _rig.ToGlobal(b), r));

        bool onReel = action is BodyAction.Fight || (ReelingSource?.Invoke() ?? false);
        _leftOnReel = Mathf.MoveToward(_leftOnReel, onReel ? 1f : 0f, dt * 4f);
        if (ReelingSource?.Invoke() ?? false)
            _crank += dt * 9f;
        _rig.SetCrank(_crank);
        // The crank's knob is pinched in the fingertips, the hand lying the most natural way round it.
        var pinch = BarGrip(_rig.ToGlobal(RodRig.KnobAt(_crank)), rigBasis * Vector3.Right, 0.0085f, false, leftArm, 0f, Mathf.DegToRad(20f),
            0f, 0.62f, 0.095f, true, 0.012f, _rodLeft, dt, ToGlobal(new Vector3(-0.55f, -0.6f, 0.3f)));
        var held = HeldFishSource?.Invoke();
        _leftOnFish = Mathf.MoveToward(_leftOnFish, held != null ? 1f : 0f, dt * 6f);
        if (held is FishModel fm)
            _lastFishGrip = ToLocal(fm.ToGlobal(new Vector3(0f, -0.5f * fm.MaxHeight, 0.12f * fm.Length))) + new Vector3(0f, -0.035f, 0f);
        else if (held != null)
            _lastFishGrip = ToLocal(held.GlobalPosition) + new Vector3(0.02f, -0.04f, 0.02f);
        // Hanging relaxed at the side; on the crank; flat under a fish's belly.
        var rest = new HandTarget(ToGlobal(new Vector3(-0.26f, -0.78f, 0.05f)), bodyBasis * new Vector3(0.05f, -1f, -0.1f),
            bodyBasis * new Vector3(1f, 0f, 0.1f), 0.35f, ToGlobal(new Vector3(-0.45f, -0.5f, 0.3f)));
        var fish = new HandTarget(ToGlobal(_lastFishGrip + new Vector3(0.03f, 0f, 0.08f)), bodyBasis * new Vector3(0.15f, 0.1f, -1f),
            bodyBasis * Vector3.Up, 0.3f, ToGlobal(new Vector3(-0.6f, -0.55f, 0.25f)));
        float onCrank = Smooth01(_leftOnReel);
        var left = Mix(Mix(rest, pinch, onCrank), fish, Smooth01(_leftOnFish));
        Apply(ik.Left, left, 0f);
        ik.Left.ThumbAlong = null;
        // On the crank the fingers close round the knob and stop on it.
        ik.Left.Obstacles.Clear();
        if (_leftOnReel > 0.5f && _leftOnFish < 0.5f)
            foreach (var (a, b, r) in RodRig.CrankObstacles(_crank))
                ik.Left.Obstacles.Add(new ArmIK.Capsule(_rig.ToGlobal(a), _rig.ToGlobal(b), r));
    }

    private static float Smooth01(float x) => x * x * (3f - 2f * x);

    private static void Apply(ArmIK.Arm arm, HandTarget t, float splay)
    {
        arm.Wrist = t.Wrist;
        arm.Fingers = t.Fingers;
        arm.Palm = t.Palm;
        arm.Grip = t.Grip;
        arm.Pole = t.Pole;
        arm.Splay = splay;
        arm.Obstacles.Clear();
        if (t.Bar is { } bar && t.BarWeight > 0.5f)
            arm.Obstacles.Add(bar);
    }

    /// <summary>Blends two hand targets (positions, directions, grip and elbow pole).</summary>
    private static HandTarget Mix(HandTarget a, HandTarget b, float k) => new(
        a.Wrist.Lerp(b.Wrist, k), a.Fingers.Normalized().Lerp(b.Fingers.Normalized(), k).Normalized(),
        a.Palm.Normalized().Lerp(b.Palm.Normalized(), k).Normalized(), Mathf.Lerp(a.Grip, b.Grip, k), a.Pole.Lerp(b.Pole, k),
        k > 0.5f ? b.Bar ?? a.Bar : a.Bar ?? b.Bar, k > 0.5f ? b.BarWeight : a.BarWeight);

    /// <summary>An arm's shoulder (world), its upper arm and forearm lengths, and the body's right and up.</summary>
    public readonly record struct ArmShape(Vector3 Shoulder, float Upper, float Fore, Vector3 BodyRight, Vector3 BodyUp);

    /// <summary>The way a hand lay round its bar last frame (eased towards the best way each frame).</summary>
    public sealed class GripState
    {
        public float Phi, Delta, Slide, Sign = 1f;
        public bool Has;
    }

    private readonly GripState _rodRight = new(), _rodLeft = new(), _toolRight = new(), _toolLeft = new();

    /// <summary>
    /// A hand closed round a bar (world space): <paramref name="axis"/> is the bar's direction, <paramref name="at"/> the
    /// point the hand should hold. It tries every way the hand can lie round the bar (turned round it, the bar slanting
    /// across the palm between <paramref name="deltaMin"/> and <paramref name="deltaMax"/>, slid a little along it) and
    /// for each puts the forearm straight behind the hand: the wrist is never bent. It takes the way whose elbow the
    /// upper arm can reach exactly, hanging down and to the side, with the forearm not twisted past what a forearm turns
    /// (palm inwards is neutral), and close to last frame's way; then eases towards it. <paramref name="along"/> = how far
    /// from the wrist the bar lies along the fingers (0.065 m round the palm, more for a fingertip pinch). The pole puts
    /// the elbow where that forearm needs it.
    /// </summary>
    public static HandTarget BarGrip(Vector3 at, Vector3 axis, float radius, bool right, ArmShape arm, float deltaMin, float deltaMax,
        float slideRange, float grip, float along, bool eitherWay, float barLength, GripState state, float dt, Vector3 pole,
        Vector3? palmToward = null, float palmCone = -1f)
    {
        var a0 = axis.Normalized();
        var up = arm.BodyUp;
        float bestScore = float.MaxValue, bestPhi = 0f, bestDelta = deltaMin, bestSlide = 0f, bestSign = 1f;
        foreach (float sign in eitherWay ? new[] { 1f, -1f } : new[] { 1f })
        {
            var a = a0 * sign;
            var (u, v) = Around(a, up, arm.BodyRight);
            for (int i = 0; i < 48; i++)
            {
                float phi = i / 48f * Mathf.Tau;
                for (int j = 0; j < 4; j++)
                {
                    float delta = Mathf.Lerp(deltaMin, deltaMax, j / 3f);
                    for (int k = -1; k <= 1; k++)
                    {
                        float slide = k * slideRange;
                        if (palmToward is Vector3 want && (u * MathF.Cos(phi) + v * MathF.Sin(phi)).Dot(want) < palmCone)
                            continue;   // the hand must lie the way the grip is held (e.g. a rod: palm on the seat's side)
                        float score = Cost(at, a, u, v, phi, delta, slide, radius, along, right, arm, pole)
                                      + (state.Has ? Change(state, phi, delta, slide, sign) : 0f);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            (bestPhi, bestDelta, bestSlide, bestSign) = (phi, delta, slide, sign);
                        }
                    }
                }
            }
        }
        // Ease the way the hand lies towards the best one (a switch of the index finger's side is taken at once).
        if (state.Has && state.Sign == bestSign)
        {
            float ease = 1f - MathF.Exp(-9f * dt);
            state.Phi = Mathf.LerpAngle(state.Phi, bestPhi, ease);
            state.Delta = Mathf.Lerp(state.Delta, bestDelta, ease);
            state.Slide = Mathf.Lerp(state.Slide, bestSlide, ease);
        }
        else
            (state.Phi, state.Delta, state.Slide, state.Sign) = (bestPhi, bestDelta, bestSlide, bestSign);
        state.Has = true;
        {
            var a = a0 * state.Sign;
            var (u, v) = Around(a, up, arm.BodyRight);
            var (wrist, f, p) = Pose(at, a, u, v, state.Phi, state.Delta, state.Slide, radius, along, right);
            return new HandTarget(wrist, f, p, grip, pole, new ArmIK.Capsule(at - a * barLength, at + a * barLength, radius));
        }
    }

    /// <summary>Two directions square to the bar, fixed to the body (so a way of lying round it keeps its meaning).</summary>
    private static (Vector3 U, Vector3 V) Around(Vector3 a, Vector3 up, Vector3 right)
    {
        var u = up - a * up.Dot(a);
        if (u.LengthSquared() < 1e-4f)
            u = right - a * right.Dot(a);
        u = u.Normalized();
        return (u, a.Cross(u));
    }

    private static (Vector3 Wrist, Vector3 Fingers, Vector3 Palm) Pose(Vector3 at, Vector3 a, Vector3 u, Vector3 v, float phi, float delta,
        float slide, float radius, float along, bool right)
    {
        var p = u * MathF.Cos(phi) + v * MathF.Sin(phi);                // the palm faces the bar
        var e2 = right ? p.Cross(a) : a.Cross(p);                       // the fingers' way when the bar lies square across
        var f = (a * MathF.Sin(delta) + e2 * MathF.Cos(delta)).Normalized();
        var wrist = at + a * slide - p * (radius + 0.025f) - f * along;
        return (wrist, f, p);
    }

    /// <summary>
    /// How unnatural a way of holding is: the elbow hangs where the arm puts it (two-bone solve towards the natural
    /// pole: down and out), and the cost is the wrist's bend between that forearm and the hand beyond what a wrist
    /// does easily (about 30 degrees), the forearm twisted past its turn (palm towards the body's middle is neutral),
    /// and a hand out of the arm's reach.
    /// </summary>
    private static float Cost(Vector3 at, Vector3 a, Vector3 u, Vector3 v, float phi, float delta, float slide, float radius, float along,
        bool right, ArmShape arm, Vector3 pole)
    {
        var (wrist, f, p) = Pose(at, a, u, v, phi, delta, slide, radius, along, right);
        var to = wrist - arm.Shoulder;
        float d = to.Length(), max = arm.Upper + arm.Fore;
        float outOfReach = MathF.Max(0f, d - (max - 0.01f));
        var dir = to / MathF.Max(d, 1e-4f);
        d = Math.Clamp(d, 0.05f, max - 0.002f);
        float x = (arm.Upper * arm.Upper - arm.Fore * arm.Fore + d * d) / (2f * d);
        float h = MathF.Sqrt(MathF.Max(arm.Upper * arm.Upper - x * x, 0f));
        var bend = pole - arm.Shoulder;
        bend = (bend - dir * bend.Dot(dir)).Normalized();
        var elbow = arm.Shoulder + dir * x + bend * h;
        var forearm = (arm.Shoulder + dir * d - elbow).Normalized();
        float wristBend = MathF.Acos(Math.Clamp(forearm.Dot(f), -1f, 1f));
        // Forearm twist: palm towards the body's middle is neutral; a forearm turns about 85 degrees each way.
        var neutral = right ? arm.BodyUp.Cross(forearm) : forearm.Cross(arm.BodyUp);
        float twist = 0f;
        if (neutral.LengthSquared() > 1e-4f)
        {
            var pp = p - forearm * p.Dot(forearm);
            if (pp.LengthSquared() > 1e-6f)
                twist = MathF.Acos(Math.Clamp(pp.Normalized().Dot(neutral.Normalized()), -1f, 1f));
        }
        return outOfReach * 30f + MathF.Max(0f, wristBend - Mathf.DegToRad(28f)) * 2.5f + wristBend * 0.3f
               + MathF.Max(0f, twist - Mathf.DegToRad(85f)) * 2f;
    }

    private static float Change(GripState s, float phi, float delta, float slide, float sign) =>
        MathF.Abs(Mathf.AngleDifference(s.Phi, phi)) * 0.35f + MathF.Abs(s.Delta - delta) * 0.8f + MathF.Abs(s.Slide - slide) * 6f
        + (s.Sign != sign ? 0.6f : 0f);

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
