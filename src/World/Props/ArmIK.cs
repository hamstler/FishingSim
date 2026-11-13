using System;
using System.Collections.Generic;
using Godot;

namespace FishingSim.World.Props;

/// <summary>
/// Poses a character's arms by code, after any animation: each arm reaches its wrist target with a two-bone solve
/// (shoulder, elbow towards a pole point, wrist), the hand turns to a frame given by where the fingers point and where
/// the palm faces, half of the hand's twist goes into the forearm (no candy-wrapped wrists), and the fingers close
/// around a handle as far as Grip says (thumb a little less). When the arm is given obstacles (the handle, the reel's
/// stem, a knob: capsules in world space), each finger closes only until it touches one: first the whole finger, then
/// its outer two joints, then the tip, so it wraps round the handle instead of sinking into it. Bones point along their
/// local +Y (glTF from Blender); the rest pose is the packs' T-pose with the palms facing down. Targets are in world
/// space.
/// </summary>
public partial class ArmIK : SkeletonModifier3D
{
    /// <summary>A rounded rod in world space the fingers must not enter: from A to B, this thick.</summary>
    public readonly record struct Capsule(Vector3 A, Vector3 B, float Radius);

    /// <summary>One finger: its three joints (bone, curl axis in the bone's space, full curl), the tip, how thick it is,
    /// and the axis it spreads sideways about (middle towards the index finger, ring towards the little finger).</summary>
    internal sealed class Chain
    {
        public string Name = "";
        public readonly int[] Bones = new int[3];
        public readonly Vector3[] Axes = new Vector3[3];
        public readonly float[] Angles = new float[3];
        public int Tip = -1;
        public float Radius;
        public Vector3 SplayAxis;
        public float SplaySign;
    }

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

        /// <summary>What the fingers close round (world space); empty = they close as far as Grip says.</summary>
        public readonly List<Capsule> Obstacles = new();

        /// <summary>How far the middle and ring fingers spread apart (radians), e.g. round a reel's stem.</summary>
        public float Splay;

        internal int Upper = -1, Lower = -1, Hand = -1;
        internal Basis RestFrame;
        internal readonly List<(int Bone, Vector3 Axis, float Angle)> Curl = new();
        internal readonly List<Chain> Chains = new();
        /// <summary>The point between the middle and ring knuckles, in the hand's rest frame (fingers, palm, side).</summary>
        internal Vector3 GapLocal;

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
            var midK = sk.GetBoneGlobalRest(sk.FindBone($"middle_01_{s}")).Origin;
            var ringK = sk.GetBoneGlobalRest(sk.FindBone($"ring_01_{s}")).Origin;
            arm.GapLocal = arm.RestFrame.Inverse() * ((midK + ringK) * 0.5f - hand.Origin);
            // Each finger joint turns about the axis that swings its segment towards the palm.
            foreach (var (finger, angles) in new (string, float[])[]
                     {
                         ("index", new[] { 78f, 92f, 55f }), ("middle", new[] { 82f, 95f, 55f }),
                         ("ring", new[] { 86f, 95f, 55f }), ("pinky", new[] { 90f, 92f, 50f }), ("thumb", new[] { 18f, 28f, 30f }),
                     })
            {
                var chain = new Chain
                {
                    Name = finger,
                    Radius = finger switch { "thumb" => 0.0095f, "middle" => 0.009f, "pinky" => 0.0075f, _ => 0.0085f },
                };
                bool whole = true;
                for (int k = 0; k < 3; k++)
                {
                    int bone = sk.FindBone($"{finger}_0{k + 1}_{s}");
                    int child = sk.FindBone(k < 2 ? $"{finger}_0{k + 2}_{s}" : $"{finger}_04_leaf_{s}");
                    if (bone < 0 || child < 0)
                    {
                        whole = false;
                        continue;
                    }
                    var g = sk.GetBoneGlobalRest(bone);
                    var seg = (sk.GetBoneGlobalRest(child).Origin - g.Origin).Normalized();
                    var towards = finger == "thumb" ? (palm + fingers * 0.6f).Normalized() : palm;
                    var axis = seg.Cross(towards);
                    if (axis.LengthSquared() < 1e-8f)
                    {
                        whole = false;
                        continue;
                    }
                    var local = (g.Basis.Inverse() * axis.Normalized()).Normalized();
                    arm.Curl.Add((bone, local, Mathf.DegToRad(angles[k])));
                    chain.Bones[k] = bone;
                    chain.Axes[k] = local;
                    chain.Angles[k] = Mathf.DegToRad(angles[k]);
                    if (k == 2)
                        chain.Tip = child;
                    if (k == 0 && finger is "middle" or "ring")
                    {
                        // Spreading: the middle finger leans towards the index finger, the ring finger towards the little one.
                        var other = sk.GetBoneGlobalRest(sk.FindBone(finger == "middle" ? $"index_01_{s}" : $"pinky_01_{s}")).Origin - g.Origin;
                        var spread = seg.Cross(other.Normalized());
                        if (spread.LengthSquared() > 1e-8f)
                        {
                            chain.SplayAxis = (g.Basis.Inverse() * spread.Normalized()).Normalized();
                            chain.SplaySign = 1f;
                        }
                    }
                }
                if (whole)
                    arm.Chains.Add(chain);
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
        CloseFingers(sk, arm, toSkeleton);
        if (Debug && Engine.GetProcessFrames() % 60 == 0)
        {
            var g = sk.GetBoneGlobalPose(arm.Hand);
            int mid = sk.FindBone($"middle_01_{arm.Side}"), tip = sk.FindBone($"middle_04_leaf_{arm.Side}");
            var fingersNow = (sk.GetBoneGlobalPose(mid).Origin - g.Origin).Normalized();
            GD.Print($"IK {arm.Side}: wrist {g.Origin} want {target} reach {d:F3}/{l1 + l2:F3} fingers {fingersNow} want {want.Column0} " +
                     $"tip {sk.GetBoneGlobalPose(tip).Origin} curl bones {arm.Curl.Count}");
        }
    }

    /// <summary>
    /// Closes the fingers: as far as Grip says, or, with obstacles, each finger until it touches one (the whole finger,
    /// then the outer two joints, then the tip, so it wraps round a handle).
    /// </summary>
    private static void CloseFingers(Skeleton3D sk, Arm arm, Transform3D toSkeleton)
    {
        if (arm.Chains.Count == 0 || arm.Obstacles.Count == 0)
        {
            foreach (var (bone, ax, angle) in arm.Curl)
                sk.SetBonePoseRotation(bone, (sk.GetBoneRest(bone).Basis * new Basis(ax, angle * arm.Grip)).GetRotationQuaternion());
            foreach (var chain in arm.Chains)
                if (chain.SplaySign != 0f && arm.Splay != 0f)
                    sk.SetBonePoseRotation(chain.Bones[0], Rotation(sk, chain, 0, arm.Grip, arm.Splay).GetRotationQuaternion());
            return;
        }
        // Obstacles in the skeleton's space (its scale shrinks or grows the radii and the fingers alike).
        float scale = toSkeleton.Basis.Column0.Length();
        Span<Capsule> obstacles = stackalloc Capsule[Math.Min(arm.Obstacles.Count, 12)];
        for (int i = 0; i < obstacles.Length; i++)
        {
            var o = arm.Obstacles[i];
            obstacles[i] = new Capsule(toSkeleton * o.A, toSkeleton * o.B, o.Radius * scale);
        }
        Span<float> c = stackalloc float[3];
        Span<float> depth = stackalloc float[Steps + 1];
        foreach (var chain in arm.Chains)
        {
            var parent = sk.GetBoneGlobalPose(sk.GetBoneParent(chain.Bones[0]));
            float radius = chain.Radius * scale;
            c[0] = c[1] = c[2] = 0f;
            float limit = chain.Name == "thumb" ? arm.Grip * 0.8f : arm.Grip;
            // Joint by joint from the knuckle out: each phase turns the joints from j0 outwards together, as far as
            // the finger keeps out of the obstacles (or, when it starts inside one, as far as it keeps coming out).
            for (int j0 = 0; j0 < 3; j0++)
            {
                float from = c[j0];
                int best = 0;
                for (int k = 0; k <= Steps; k++)
                {
                    SetFrom(c, j0, from + (limit - from) * k / Steps);
                    depth[k] = Depth(sk, chain, parent, c, arm.Splay, obstacles, radius);
                }
                // The least overlap reached first, then on along its plateau while it stays as small.
                float min = float.MaxValue;
                for (int k = 0; k <= Steps; k++)
                    min = MathF.Min(min, depth[k]);
                while (depth[best] > min + 1e-5f)
                    best++;
                while (best < Steps && depth[best + 1] <= min + 1e-5f)
                    best++;
                float good = from + (limit - from) * best / Steps;
                if (best < Steps)
                {
                    // Refine between the last good step and the one that touches.
                    float bad = from + (limit - from) * (best + 1) / Steps;
                    for (int k = 0; k < 4; k++)
                    {
                        float mid = (good + bad) * 0.5f;
                        SetFrom(c, j0, mid);
                        if (Depth(sk, chain, parent, c, arm.Splay, obstacles, radius) <= min + 1e-5f)
                            good = mid;
                        else
                            bad = mid;
                    }
                }
                SetFrom(c, j0, good);
                if (best == Steps)
                    break;      // nothing in the way: closed as far as the grip goes
            }
            for (int k = 0; k < 3; k++)
                sk.SetBonePoseRotation(chain.Bones[k], Rotation(sk, chain, k, c[k], arm.Splay).GetRotationQuaternion());
        }
    }

    private const int Steps = 12;

    /// <summary>Sets the joints from <paramref name="j0"/> outwards to <paramref name="t"/> (they are equal when a
    /// phase starts: each phase carries the outer joints on from where the last one stopped).</summary>
    private static void SetFrom(Span<float> c, int j0, float t)
    {
        for (int k = j0; k < 3; k++)
            c[k] = t;
    }

    /// <summary>A finger joint's local rotation for curl fraction <paramref name="t"/> (and spread for the first joint).</summary>
    private static Basis Rotation(Skeleton3D sk, Chain chain, int k, float t, float splay)
    {
        var b = sk.GetBoneRest(chain.Bones[k]).Basis * new Basis(chain.Axes[k], chain.Angles[k] * t);
        if (k == 0 && chain.SplaySign != 0f && splay != 0f)
            b = sk.GetBoneRest(chain.Bones[k]).Basis * new Basis(chain.SplayAxis, splay * chain.SplaySign) * new Basis(chain.Axes[k], chain.Angles[k] * t);
        return b;
    }

    /// <summary>How deep a finger at curl fractions <paramref name="c"/> is inside the obstacles (summed over points
    /// along it, the knuckle excluded); 0 when it is clear.</summary>
    private static float Depth(Skeleton3D sk, Chain chain, Transform3D parent, ReadOnlySpan<float> c, float splay,
        ReadOnlySpan<Capsule> obstacles, float radius)
    {
        var g = parent;
        var prev = Vector3.Zero;
        float sum = 0f;
        for (int k = 0; k < 3; k++)
        {
            int bone = chain.Bones[k];
            g = g * new Transform3D(Rotation(sk, chain, k, c[k], splay), sk.GetBonePosePosition(bone));
            var at = g.Origin;
            if (k > 0)
                sum += Overlap(at, obstacles, radius) + Overlap((prev + at) * 0.5f, obstacles, radius);
            prev = at;
        }
        var tip = g * sk.GetBoneRest(chain.Tip).Origin;
        return sum + Overlap(tip, obstacles, radius * 0.85f) + Overlap((prev + tip) * 0.5f, obstacles, radius);
    }

    private static float Overlap(Vector3 p, ReadOnlySpan<Capsule> obstacles, float radius)
    {
        float worst = 0f;
        foreach (var o in obstacles)
        {
            var ab = o.B - o.A;
            float t = Math.Clamp((p - o.A).Dot(ab) / MathF.Max(ab.LengthSquared(), 1e-10f), 0f, 1f);
            worst = MathF.Max(worst, o.Radius + radius - (o.A + ab * t).DistanceTo(p));
        }
        return worst;
    }

    /// <summary>
    /// The world vector from the wrist to the point between the middle and ring knuckles when the hand takes the frame
    /// of <paramref name="fingers"/> and <paramref name="palm"/> (null until the skeleton is ready): to put a reel's stem
    /// between those fingers.
    /// </summary>
    public Vector3? KnuckleGap(Arm arm, Vector3 fingers, Vector3 palm)
    {
        var sk = GetSkeleton();
        if (!_ready || sk == null || arm.Hand < 0)
            return null;
        var f = fingers.Normalized();
        var p = (palm - f * palm.Dot(f)).Normalized();
        var g = arm.GapLocal;
        return (f * g.X + p * g.Y + f.Cross(p) * g.Z) * sk.GlobalTransform.Basis.Column0.Length();
    }

    private static readonly bool Debug = System.Environment.GetEnvironmentVariable("FISHSIM_IK_DEBUG") == "1";
}
