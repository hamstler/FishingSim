using System;
using Godot;
using FishingSim.Core.Animation;

namespace FishingSim.Fishing;

/// <summary>
/// The angler's visible body in first person: forearms and hands (the right one on the rod handle, the left one on
/// the reel or swinging while walking) and the rod's lift/lean during casting and fighting. Driven by the pose that
/// <see cref="BodyMotion"/> computes; a child of the camera. A rigged human model can later replace the primitive
/// arms without touching the gameplay code (see docs/ROZWOJ.md).
/// </summary>
public partial class FirstPersonBody : Node3D
{
    private static readonly Vector3 RigBasePosition = new(0.3f, -0.35f, -0.4f);
    private static readonly Vector3 RigBaseRotation = new(Mathf.DegToRad(-80f), 0f, 0f);
    // The right hand grips the reel seat with the reel foot between the fingers, as anglers hold a spinning rod.
    private static readonly Vector3 HandleLocal = new(0f, 0.07f, 0.02f);
    private static readonly Vector3 CrankLocal = new(0.1f, 0.1f, -0.09f);
    private static readonly Vector3 LeftRest = new(-0.28f, -0.62f, -0.3f);

    private RodRig _rig = null!;
    private Node3D _camera = null!;
    private MeshInstance3D _rightArm = null!, _leftArm = null!, _rightHand = null!, _leftHand = null!;
    private float _leftOnReel;

    public void Setup(Node3D camera, RodRig rig)
    {
        _camera = camera;
        _rig = rig;
    }

    public override void _Ready()
    {
        var sleeve = World.Props.BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.26f, 0.16f), Roughness = 0.95f });   // wool tunic
        var skin = World.Props.BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.66f, 0.52f), Roughness = 0.7f });
        _rightArm = Part(new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = 1f, RadialSegments = 10 }, sleeve);
        _leftArm = Part(new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = 1f, RadialSegments = 10 }, sleeve);
        _rightHand = Part(new SphereMesh { Radius = 0.042f, Height = 0.075f, RadialSegments = 10, Rings = 6 }, skin);
        _leftHand = Part(new SphereMesh { Radius = 0.042f, Height = 0.075f, RadialSegments = 10, Rings = 6 }, skin);
    }

    private MeshInstance3D Part(Mesh mesh, Material mat)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(mi);
        return mi;
    }

    /// <summary>Applies one frame of pose. Positions are in the camera's local space.</summary>
    public void Apply(BodyPose pose, BodyAction action, float dt)
    {
        if (_rig == null || _rightArm == null)
            return;

        // The rod: raised while casting and fighting, swaying a little with the gait.
        _rig.Position = RigBasePosition + new Vector3(pose.ArmSwing * 0.02f, pose.ArmLift * 0.08f, pose.Lean * 0.2f);
        _rig.Rotation = RigBaseRotation + new Vector3(pose.ArmLift * 0.35f, 0f, pose.ArmSwing * 0.04f);

        // Right forearm: from below the view up to the rod handle.
        var rightHand = ToLocal(_rig.ToGlobal(HandleLocal));
        Stretch(_rightArm, rightHand + new Vector3(0.06f, -0.16f, 0.3f), rightHand);
        _rightHand.Position = rightHand;

        // Left hand: on the reel handle while fighting/reeling, otherwise hanging and swinging with the steps.
        bool reeling = action is BodyAction.Fight or BodyAction.Reel;
        _leftOnReel += ((reeling ? 1f : 0f) - _leftOnReel) * Math.Clamp(dt * 6f, 0f, 1f);
        var crank = ToLocal(_rig.ToGlobal(CrankLocal));
        var rest = LeftRest + new Vector3(0f, MathF.Abs(pose.ArmSwing) * 0.05f, -pose.ArmSwing * 0.12f);
        var leftHand = rest.Lerp(crank, _leftOnReel);
        Stretch(_leftArm, leftHand + new Vector3(-0.08f, -0.16f, 0.3f), leftHand);
        _leftHand.Position = leftHand;
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
