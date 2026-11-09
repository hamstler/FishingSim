using System;
using Godot;
using FishingSim.Core.Animation;
using FishingSim.World.Props;

namespace FishingSim.Fishing;

/// <summary>
/// The angler's hands in first person. The right hand is built around the rod handle as a gripping fist (palm on the
/// side, four fingers wrapped under the blank with the reel foot between the middle and ring finger, thumb along the
/// top) and is a child of the rod rig, so the rod is really held. The left hand moves to the reel handle and turns
/// it while reeling, otherwise it rests out of view. Forearms in tunic sleeves connect both hands to the body.
/// The rod is raised when the player holds the right mouse button (strike, pumping, lifting the fish out).
/// </summary>
public partial class FirstPersonBody : Node3D
{
    private static readonly Vector3 RigBasePosition = new(0.28f, -0.33f, -0.42f);
    private static readonly Vector3 RigBaseRotation = new(Mathf.DegToRad(-78f), 0f, 0f);
    private static readonly Vector3 WristLocal = new(0.055f, 0.03f, -0.01f);
    private static readonly Vector3 CrankLocal = new(0.1f, 0.1f, -0.09f);
    private static readonly Vector3 LeftRest = new(-0.3f, -0.75f, -0.25f);

    private RodRig _rig = null!;
    private Node3D _camera = null!;
    private MeshInstance3D _rightArm = null!, _leftArm = null!, _rightCuff = null!, _leftCuff = null!;
    private Node3D _leftHand = null!;
    private StandardMaterial3D _skin = null!, _sleeve = null!;
    private float _leftOnReel, _raise, _crank, _leftOnFish;
    private Vector3 _lastFishGrip;

    /// <summary>0..1 how far the rod should be raised (right mouse button); set by Main from the rod.</summary>
    public Func<float>? RaiseSource { get; set; }

    /// <summary>The landed fish to hold in the left hand (camera child), or null.</summary>
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
        if (_rig == null || _rightArm == null)
            return;

        // The rod: raised with the right mouse button, lifted while casting/fighting, swaying a little with the gait.
        _raise = Mathf.MoveToward(_raise, Math.Clamp(RaiseSource?.Invoke() ?? 0f, 0f, 1f), dt * 4f);
        _rig.Position = RigBasePosition + new Vector3(pose.ArmSwing * 0.02f, pose.ArmLift * 0.06f + _raise * 0.07f, pose.Lean * 0.2f + _raise * 0.06f);
        _rig.Rotation = RigBaseRotation + new Vector3(pose.ArmLift * 0.3f + _raise * 0.65f, 0f, pose.ArmSwing * 0.04f);

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
        var crankCentre = new Vector3(0.1f, 0.1f, -0.065f);
        var knob = crankCentre + new Vector3(0f, MathF.Sin(_crank) * 0.03f, MathF.Cos(_crank) * 0.03f - 0.025f);
        var leftAt = LeftRest.Lerp(ToLocal(_rig.ToGlobal(knob)), _leftOnReel);
        // Holding the catch: the left hand grips the fish just behind its head.
        var held = HeldFishSource?.Invoke();
        _leftOnFish = Mathf.MoveToward(_leftOnFish, held != null ? 1f : 0f, dt * 6f);
        if (held != null)
            _lastFishGrip = ToLocal(held.GlobalPosition) + new Vector3(0.02f, -0.03f, 0.02f);
        leftAt = leftAt.Lerp(_lastFishGrip, _leftOnFish);
        _leftHand.Position = leftAt;
        _leftHand.Rotation = new Vector3(0.3f, 0.4f, 0.2f);
        var leftElbow = leftAt + new Vector3(-0.12f, -0.22f, 0.3f);
        Stretch(_leftArm, leftElbow, leftAt + new Vector3(-0.04f, -0.02f, 0.05f));
        Stretch(_leftCuff, leftAt + new Vector3(-0.05f, -0.045f, 0.09f), leftAt + new Vector3(-0.045f, -0.035f, 0.075f));
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
