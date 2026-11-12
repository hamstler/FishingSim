using System;
using System.Collections.Generic;
using Godot;

namespace FishingSim.World.Props;

/// <summary>
/// Poses a character's arms by code, after any animation: each arm reaches its wrist target with a two-bone solve
/// (shoulder, elbow towards a pole point, wrist), the hand turns to a frame given by where the fingers point and where
/// the palm faces, half of the hand's twist goes into the forearm (no candy-wrapped wrists), and the fingers close
/// around a handle as far as Grip says (thumb a little less). Bones point along their local +Y (glTF from Blender);
/// the rest pose is the packs' T-pose with the palms facing down. Targets are in world space.
/// </summary>
public partial class ArmIK : SkeletonModifier3D
{
    public sealed class Arm
    {
        public readonly string Side;
        /// <summary>Where the wrist (the hand bone's origin) should be.</summary>
        public Vector3 Wrist;
        /// <summary>A point the elbow bends towards.</summary>
        public Vector3 Pole;
        /// <summary>Direction from the wrist to the knuckles, and the way the palm faces.</summary>
        public Vector3 Fingers = Vector3.Forward, Palm = Vector3.Down;
        /// <summary>0 = open hand, 1 = tight fist.</summary>
        public float Grip = 0.85f;
        public bool Enabled = true;

        internal int Upper = -1, Lower = -1, Hand = -1;
        internal Basis RestFrame;
        internal readonly List<(int Bone, Vector3 Axis, float Angle)> Curl = new();

        public Arm(string side) => Side = side;
    }

    public Arm Right { get; } = new("r");
    public Arm Left { get; } = new("l");

    private bool _ready;

    private void Prepare(Skeleton3D sk)
    {
        _ready = true;
        foreach (var arm in new[] { Right, Left })
        {
            string s = arm.Side;
            arm.Upper = sk.FindBone($"upperarm_{s}");
            arm.Lower = sk.FindBone($"lowerarm_{s}");
            arm.Hand = sk.FindBone($"hand_{s}");
            if (arm.Hand < 0)
                continue;
            var hand = sk.GetBoneGlobalRest(arm.Hand);
            var fingers = (sk.GetBoneGlobalRest(sk.FindBone($"middle_01_{s}")).Origin - hand.Origin).Normalized();
            var palm = Vector3.Down;
            palm = (palm - fingers * palm.Dot(fingers)).Normalized();
            arm.RestFrame = Frame(fingers, palm);
            // Each finger joint turns about the axis that swings its segment towards the palm.
            foreach (var (finger, angles) in new (string, float[])[]
                     {
                         ("index", new[] { 78f, 92f, 55f }), ("middle", new[] { 82f, 95f, 55f }),
                         ("ring", new[] { 86f, 95f, 55f }), ("pinky", new[] { 90f, 92f, 50f }), ("thumb", new[] { 18f, 28f, 30f }),
                     })
            {
                for (int k = 0; k < 3; k++)
                {
                    int bone = sk.FindBone($"{finger}_0{k + 1}_{s}");
                    int child = sk.FindBone(k < 2 ? $"{finger}_0{k + 2}_{s}" : $"{finger}_04_leaf_{s}");
                    if (bone < 0 || child < 0)
                        continue;
                    var g = sk.GetBoneGlobalRest(bone);
                    var seg = (sk.GetBoneGlobalRest(child).Origin - g.Origin).Normalized();
                    var towards = finger == "thumb" ? (palm + fingers * 0.6f).Normalized() : palm;
                    var axis = seg.Cross(towards);
                    if (axis.LengthSquared() < 1e-8f)
                        continue;
                    arm.Curl.Add((bone, (g.Basis.Inverse() * axis.Normalized()).Normalized(), Mathf.DegToRad(angles[k])));
                }
            }
        }
    }

    /// <summary>An orthonormal frame whose columns are fingers, palm and their cross product.</summary>
    private static Basis Frame(Vector3 fingers, Vector3 palm)
    {
        var f = fingers.Normalized();
        var p = (palm - f * palm.Dot(f)).Normalized();
        return new Basis(f, p, f.Cross(p));
    }

    /// <summary>Turns <paramref name="b"/> the shortest way so that its +Y axis points along <paramref name="dir"/>.</summary>
    private static Basis Aim(Basis b, Vector3 dir)
    {
        var cur = (b * Vector3.Up).Normalized();
        var axis = cur.Cross(dir);
        float s = axis.Length();
        float c = Math.Clamp(cur.Dot(dir), -1f, 1f);
        if (s < 1e-6f)
            return b;
        return new Basis(axis / s, MathF.Atan2(s, c)) * b;
    }

    public override void _ProcessModification()
    {
        var sk = GetSkeleton();
        if (sk == null)
            return;
        if (!_ready)
            Prepare(sk);
        var toSkeleton = sk.GlobalTransform.AffineInverse();
        foreach (var arm in new[] { Right, Left })
            if (arm.Enabled && arm.Hand >= 0 && arm.Upper >= 0 && arm.Lower >= 0)
                Solve(sk, arm, toSkeleton);
    }

    private static void Solve(Skeleton3D sk, Arm arm, Transform3D toSkeleton)
    {
        var target = toSkeleton * arm.Wrist;
        var pole = toSkeleton * arm.Pole;
        var want = Frame(toSkeleton.Basis * arm.Fingers, toSkeleton.Basis * arm.Palm);

        int parent = sk.GetBoneParent(arm.Upper);
        var parentG = parent >= 0 ? sk.GetBoneGlobalPose(parent) : Transform3D.Identity;
        var upG = parentG * sk.GetBoneRest(arm.Upper);
        float l1 = sk.GetBoneRest(arm.Lower).Origin.Length(), l2 = sk.GetBoneRest(arm.Hand).Origin.Length();
        var shoulder = upG.Origin;
        var to = target - shoulder;
        float d = Math.Clamp(to.Length(), 0.02f, l1 + l2 - 0.002f);
        var dir = to.Normalized();
        float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
        float h = MathF.Sqrt(MathF.Max(l1 * l1 - a * a, 0f));
        var bend = pole - shoulder;
        bend = (bend - dir * bend.Dot(dir)).Normalized();
        var elbow = shoulder + dir * a + bend * h;
        var wrist = shoulder + dir * d;

        var upper = Aim(upG.Basis, (elbow - shoulder).Normalized());
        var loRest = new Transform3D(upper, shoulder) * sk.GetBoneRest(arm.Lower);
        var lower = Aim(loRest.Basis, (wrist - elbow).Normalized());
        // The hand: from its rest frame to the wanted one.
        var handRest = sk.GetBoneGlobalRest(arm.Hand).Basis;
        var hand = want * arm.RestFrame.Inverse() * handRest;
        // Half the twist about the forearm goes into the forearm.
        var local = (lower.Inverse() * hand).GetRotationQuaternion();
        var axis = Vector3.Up;
        var proj = axis * new Vector3(local.X, local.Y, local.Z).Dot(axis);
        var twist = new Quaternion(proj.X, proj.Y, proj.Z, local.W);
        if (twist.LengthSquared() > 1e-8f)
        {
            twist = twist.Normalized();
            float angle = 2f * MathF.Atan2(new Vector3(twist.X, twist.Y, twist.Z).Dot(axis), twist.W);
            lower = lower * new Basis(axis, angle * 0.5f);
        }
        sk.SetBonePoseRotation(arm.Upper, (parentG.Basis.Inverse() * upper).GetRotationQuaternion());
        sk.SetBonePoseRotation(arm.Lower, (upper.Inverse() * lower).GetRotationQuaternion());
        sk.SetBonePoseRotation(arm.Hand, (lower.Inverse() * hand).GetRotationQuaternion());
        foreach (var (bone, ax, angle) in arm.Curl)
            sk.SetBonePoseRotation(bone, (sk.GetBoneRest(bone).Basis * new Basis(ax, angle * arm.Grip)).GetRotationQuaternion());
        if (Debug && arm.Side == "r" && Engine.GetProcessFrames() % 60 == 0)
        {
            var g = sk.GetBoneGlobalPose(arm.Hand);
            int mid = sk.FindBone("middle_01_r"), tip = sk.FindBone("middle_04_leaf_r");
            var fingersNow = (sk.GetBoneGlobalPose(mid).Origin - g.Origin).Normalized();
            GD.Print($"IK r: wrist {g.Origin} want {target} reach {d:F3}/{l1 + l2:F3} fingers {fingersNow} want {want.Column0} " +
                     $"tip {sk.GetBoneGlobalPose(tip).Origin} curl bones {arm.Curl.Count}");
        }
    }

    private static readonly bool Debug = System.Environment.GetEnvironmentVariable("FISHSIM_IK_DEBUG") == "1";
}
