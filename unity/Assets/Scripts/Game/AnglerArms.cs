using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The angler's own arms in first person (Fishing/FirstPersonBody + ArmIK): the player model's "Arms" mesh
    /// (assets/characters/player_angler.glb: MakeHuman hands and flannel sleeves on the Quaternius skeleton) under the
    /// camera. Every frame the bones go back to their rest (no drift), then each arm is solved by two-bone IK to a wrist
    /// point, the hand is turned into its grip and the fingers close:
    /// <list type="bullet">
    /// <item>right hand on the handle by the reel seat: the palm on the handle's side, the fingers (wrist to knuckles)
    /// running down towards the reel and wrapping round the handle, the thumb laid along the top of the handle
    /// (FirstPersonBody: GripPalm/GripFingers, PalmDepth, ThumbOn 7.5 cm ahead);</item>
    /// <item>left hand pinching the crank's knob with a straight wrist: the fingers along the forearm (from an elbow
    /// guessed below the shoulder to the knob), the palm towards the reel, the wrist 7.2 cm back from the knob, the
    /// thumb on top of the knob, the fingers half closed.</item>
    /// </list>
    /// The hand's own frame (fingers and palm directions in the hand bone's space) is read once from the rest pose,
    /// a T-pose with the palms down, so it does not depend on how the import oriented the bones.
    /// </summary>
    public sealed class AnglerArms : MonoBehaviour
    {
        public Fishing Rod { get; set; } = null!;

        /// <summary>Half the hand's thickness: the wrist bone runs through the middle of the hand (FirstPersonBody.PalmDepth).</summary>
        public const float PalmDepth = 0.012f;

        /// <summary>The handle's radius at the grip (14.3 mm on the Meshy rod, measured by grip_view.py).</summary>
        public const float HandleRadius = 0.0143f;

        /// <summary>How far ahead of Fishing.GripRight the right hand holds the handle (grip_view.py: the reel's stem
        /// between the middle and ring fingers).</summary>
        public const float SeatSlide = 0.097f;

        private Transform? _model;
        private Hand? _right, _left;
        private readonly List<(Transform Bone, Quaternion AtRest)> _rest = new();

        private sealed class Hand
        {
            public Transform Upper = null!, Lower = null!, Wrist = null!;
            public Vector3 FingersLocal, PalmLocal;     // in the hand bone's space, from the rest pose
            public float IndexSign = 1f;                // which way of fingers x palm the index finger (the thumb) lies
            public float Knuckles = 0.1f;               // wrist to the middle finger's knuckle (m)
            public GripState Grip = new();
            public readonly List<Transform[]> Fingers = new();   // index, middle, ring, pinky: 01, 02, 03, leaf
            public Transform[]? Thumb;                  // 01, 02, 03, leaf
        }

        public static AnglerArms? Create(Camera camera, Fishing rod)
        {
            var model = ModelLibrary.Spawn("characters/player_angler");
            if (model == null)
                return null;
            var holder = new GameObject("AnglerArms");
            holder.transform.SetParent(camera.transform, false);
            model.transform.SetParent(holder.transform, false);
            var arms = holder.AddComponent<AnglerArms>();
            arms.Rod = rod;
            arms._model = model.transform;
            Person.DressModel(model, "player_angler", ModelLibrary.Json("characters/player_angler"));
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                string n = r.gameObject.name;
                if (n.StartsWith("Body") || n.StartsWith("Arms_Meshy"))
                    r.enabled = false;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer s)
                    s.updateWhenOffscreen = true;
            }
            var t = model.transform;
            // eyes (about 12 cm over the neck joint, 9 cm ahead of it) a little behind the camera
            if (ModelLibrary.Find(t, "neck_01") is { } neck)
            {
                var eyes = holder.transform.InverseTransformPoint(neck.position) + new Vector3(0f, 0.12f, 0.09f);
                model.transform.localPosition = -eyes + new Vector3(0f, 0f, -0.04f);
            }
            arms._right = arms.MakeHand(t, "r");
            arms._left = arms.MakeHand(t, "l");
            return arms;
        }

        private Hand? MakeHand(Transform model, string side)
        {
            var upper = ModelLibrary.Find(model, "upperarm_" + side);
            var lower = ModelLibrary.Find(model, "lowerarm_" + side);
            var wrist = ModelLibrary.Find(model, "hand_" + side);
            if (upper == null || lower == null || wrist == null)
                return null;
            var hand = new Hand { Upper = upper, Lower = lower, Wrist = wrist };
            Remember(upper);
            Remember(lower);
            Remember(wrist);
            foreach (var name in new[] { "index", "middle", "ring", "pinky" })
            {
                var chain = Chain(model, name, side);
                if (chain != null)
                    hand.Fingers.Add(chain);
            }
            hand.Thumb = Chain(model, "thumb", side);
            // the hand's frame at rest (T-pose, palms down): fingers = wrist to the middle knuckle, palm = the model's down
            var middle = ModelLibrary.Find(model, "middle_01_" + side);
            var fingers = middle != null ? middle.position - wrist.position : wrist.up;
            hand.FingersLocal = wrist.InverseTransformDirection(fingers.normalized);
            // (as imported the rest palms face up: read as down, the grips showed the palms and the fingers curled away)
            hand.PalmLocal = wrist.InverseTransformDirection(Vector3.ProjectOnPlane(model.up, fingers).normalized);
            hand.Knuckles = fingers.magnitude;
            // the index finger's side of the hand, read from the model (not from a cross product's handedness)
            var index = ModelLibrary.Find(model, "index_01_" + side);
            if (index != null && middle != null)
            {
                var indexSide = index.position - middle.position;
                var fl = fingers.normalized;
                var pl = Vector3.ProjectOnPlane(model.up, fingers).normalized;
                hand.IndexSign = Vector3.Dot(indexSide, Vector3.Cross(fl, pl)) >= 0f ? 1f : -1f;
            }
            return hand;
        }

        private Transform[]? Chain(Transform model, string finger, string side)
        {
            var a = ModelLibrary.Find(model, $"{finger}_01_{side}");
            var b = ModelLibrary.Find(model, $"{finger}_02_{side}");
            var c = ModelLibrary.Find(model, $"{finger}_03_{side}");
            var tip = ModelLibrary.Find(model, $"{finger}_04_leaf_{side}");
            if (a == null || b == null || c == null || tip == null)
                return null;
            Remember(a);
            Remember(b);
            Remember(c);
            return new[] { a, b, c, tip };
        }

        private void Remember(Transform bone) => _rest.Add((bone, bone.localRotation));

        private void LateUpdate()
        {
            if (_model == null || Rod == null)
                return;
            bool show = Rod.RodVisible;
            _model.gameObject.SetActive(show);
            if (!show)
                return;
            foreach (var (bone, rest) in _rest)
                bone.localRotation = rest;
            var cam = transform.parent;
            if (_right != null && Rod.GripRight != null)
                HoldHandle(_right, cam);
            if (_left != null && Rod.GripLeft != null)
                PinchKnob(_left, cam);
        }

        /// <summary>
        /// The right hand round the reel seat (FirstPersonBody: BarGrip): every way the hand can lie round the handle is
        /// tried (turned round it, the handle slanting 20-55 degrees across the palm, slid a little along it), the palm on
        /// the handle's right side, the index finger and the thumb towards the tip; for each the elbow is placed where
        /// the arm puts it and the way with the straightest wrist (and a forearm not twisted past its turn) is taken,
        /// eased from last frame's. Then the arm reaches, half the hand's turn goes into the forearm, and the fingers close.
        /// </summary>
        private void HoldHandle(Hand h, Transform cam)
        {
            var frame = Rod.GripRight.parent;
            var along = frame.up;                       // the blank's axis, butt to tip
            var top = frame.forward;                    // away from the reel
            // the reel seat: measured on the meshes (tools/assets/grip_view.py), the hand slides 97 mm ahead of the old
            // grip point to put the reel's stem between the middle and ring fingers (index and middle in front of it, as
            // anglers hold it)
            var at = frame.TransformPoint(new Vector3(0f, Rod.GripRight.localPosition.y + SeatSlide, 0f));
            var side = Vector3.Dot(frame.right, cam.right) >= 0f ? frame.right : -frame.right;
            var pole = h.Upper.position - cam.up * 0.6f + cam.right * 0.3f;
            // the user's description: the four fingers round the handle from below, the thumb on top. So the hand lies
            // under the handle and to its right, the palm facing up and left onto it (palm within 35 degrees of that),
            // the handle across the palm slanting 5-30 degrees (Godot's 20-55 let the search lay the fingers along the
            // handle, straight up the rod: seen in Blender, nothing wrapped round)
            var palmUp = (top - side).normalized;
            var (wrist, fingers, palm) = BarGrip(h, at, along, HandleRadius, h.Knuckles * 0.62f, 5f, 30f, 0.012f, palmUp, 35f, cam, true, pole);
            Reach(h, wrist, fingers, palm, pole);
            for (int i = 0; i < h.Fingers.Count; i++)
                Curl(h.Fingers[i], palm, i == 0 ? 72f : 88f);
            // the thumb on top of the handle, just ahead of the hand
            if (h.Thumb != null)
                PointThumb(h.Thumb, at + along * 0.03f + top * (HandleRadius + 0.008f));
        }

        /// <summary>
        /// The left hand on the crank's knob with a straight wrist: the fingers run the way the forearm does (the elbow
        /// found by the arm's own solve, a few rounds), the palm down onto the knob from above as in the user's reference,
        /// the knob under the base of the fingers; half the turn in the forearm, the fingers closed round the knob.
        /// </summary>
        private void PinchKnob(Hand h, Transform cam)
        {
            var knob = Rod.GripLeft.position;
            var pole = h.Upper.position - cam.up * 0.55f - cam.right * 0.35f;
            float l1 = Vector3.Distance(h.Upper.position, h.Lower.position), l2 = Vector3.Distance(h.Lower.position, h.Wrist.position);
            var elbow = h.Upper.position - cam.up * l1 * 0.9f - cam.right * 0.08f;
            Vector3 fingers = Vector3.forward, palm = Vector3.down, wrist = knob;
            for (int round = 0; round < 4; round++)
            {
                fingers = (knob - elbow).normalized;
                palm = Vector3.ProjectOnPlane(-cam.up, fingers).normalized;
                // the knob under the middle of the closed fingers (with the knuckles' 11 cm it stayed ahead of them)
                wrist = knob - fingers * (h.Knuckles * 0.72f) - palm * (0.011f + PalmDepth);
                elbow = Elbow(h.Upper.position, l1, l2, wrist, pole).Elbow;
            }
            Reach(h, wrist, fingers, palm, pole);
            for (int i = 0; i < h.Fingers.Count; i++)
                Curl(h.Fingers[i], palm, i == 0 ? 55f : 68f);
            if (h.Thumb != null)
                PointThumb(h.Thumb, knob - fingers * 0.012f + palm * 0.01f);
        }

        /// <summary>The arm to the wrist, half the hand's turn about the forearm given to the forearm, then the hand turned.</summary>
        private static void Reach(Hand h, Vector3 wrist, Vector3 fingers, Vector3 palm, Vector3 pole)
        {
            Solve(h.Upper, h.Lower, h.Wrist, wrist, pole);
            var forearm = (h.Wrist.position - h.Lower.position).normalized;
            var nowPalm = Vector3.ProjectOnPlane(h.Wrist.TransformDirection(h.PalmLocal), forearm);
            var wantPalm = Vector3.ProjectOnPlane(palm, forearm);
            if (nowPalm.sqrMagnitude > 1e-6f && wantPalm.sqrMagnitude > 1e-6f)
            {
                float turn = Vector3.SignedAngle(nowPalm, wantPalm, forearm);
                h.Lower.rotation = Quaternion.AngleAxis(turn * 0.5f, forearm) * h.Lower.rotation;
            }
            Turn(h, fingers, palm);
        }

        /// <summary>Where a two-bone arm puts its elbow for a wrist point (the same solve as <see cref="Solve"/>).</summary>
        private static (Vector3 Elbow, float OutOfReach) Elbow(Vector3 shoulder, float l1, float l2, Vector3 wrist, Vector3 pole)
        {
            var to = wrist - shoulder;
            float d = to.magnitude, max = l1 + l2;
            float outOfReach = Mathf.Max(0f, d - (max - 0.01f));
            var dir = to / Mathf.Max(d, 1e-4f);
            d = Mathf.Clamp(d, 0.05f, max - 0.002f);
            float x = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            float hgt = Mathf.Sqrt(Mathf.Max(l1 * l1 - x * x, 0f));
            var bend = Vector3.ProjectOnPlane(pole - shoulder, dir).normalized;
            return (shoulder + dir * x + bend * hgt, outOfReach);
        }

        /// <summary>The way a hand lay round its bar last frame (eased towards the best way each frame).</summary>
        private sealed class GripState
        {
            public float Phi, Delta, Slide;
            public bool Has;
        }

        /// <summary>
        /// FirstPersonBody.BarGrip: the hand round a bar (world space), the way with the least bent wrist. palmToward and
        /// cone (degrees) keep the palm on one side; the index finger (and the thumb) lie towards the bar's +axis.
        /// </summary>
        private static (Vector3 Wrist, Vector3 Fingers, Vector3 Palm) BarGrip(Hand h, Vector3 at, Vector3 axis, float radius, float alongFingers,
            float deltaMin, float deltaMax, float slideRange, Vector3 palmToward, float cone, Transform cam, bool rightArm, Vector3 pole)
        {
            var a = axis.normalized;
            var u = Vector3.ProjectOnPlane(cam.up, a);
            if (u.sqrMagnitude < 1e-4f)
                u = Vector3.ProjectOnPlane(cam.right, a);
            u.Normalize();
            var v = Vector3.Cross(a, u);
            var shoulder = h.Upper.position;
            float l1 = Vector3.Distance(shoulder, h.Lower.position), l2 = Vector3.Distance(h.Lower.position, h.Wrist.position);
            float coneCos = Mathf.Cos(cone * Mathf.Deg2Rad);
            float best = float.MaxValue, bPhi = 0f, bDelta = deltaMin * Mathf.Deg2Rad, bSlide = 0f;
            for (int i = 0; i < 48; i++)
            {
                float phi = i / 48f * Mathf.PI * 2f;
                var p = u * Mathf.Cos(phi) + v * Mathf.Sin(phi);
                if (Vector3.Dot(p, palmToward) < coneCos)
                    continue;
                for (int j = 0; j < 4; j++)
                {
                    float delta = Mathf.Lerp(deltaMin, deltaMax, j / 3f) * Mathf.Deg2Rad;
                    for (int k = -1; k <= 1; k++)
                    {
                        float slide = k * slideRange;
                        var (wrist, f, _) = Pose(h, at, a, p, delta, slide, radius, alongFingers);
                        var (elbow, outOfReach) = Elbow(shoulder, l1, l2, wrist, pole);
                        var forearm = (wrist - elbow).normalized;
                        float bend = Mathf.Acos(Mathf.Clamp(Vector3.Dot(forearm, f), -1f, 1f));
                        // forearm twist: the palm towards the body's middle is neutral; a forearm turns ~85 degrees each way
                        var neutral = Vector3.ProjectOnPlane(rightArm ? -cam.right : cam.right, forearm);
                        var pp = Vector3.ProjectOnPlane(p, forearm);
                        float twist = neutral.sqrMagnitude > 1e-6f && pp.sqrMagnitude > 1e-6f ? Vector3.Angle(pp, neutral) * Mathf.Deg2Rad : 0f;
                        float score = outOfReach * 30f + Mathf.Max(0f, bend - 28f * Mathf.Deg2Rad) * 2.5f + bend * 0.3f
                                      + Mathf.Max(0f, twist - 85f * Mathf.Deg2Rad) * 2f;
                        if (h.Grip.Has)
                            score += Mathf.Abs(Mathf.DeltaAngle(h.Grip.Phi * Mathf.Rad2Deg, phi * Mathf.Rad2Deg)) * Mathf.Deg2Rad * 0.35f
                                     + Mathf.Abs(h.Grip.Delta - delta) * 0.8f + Mathf.Abs(h.Grip.Slide - slide) * 6f;
                        if (score < best)
                            (best, bPhi, bDelta, bSlide) = (score, phi, delta, slide);
                    }
                }
            }
            if (h.Grip.Has)
            {
                float ease = 1f - Mathf.Exp(-9f * Time.deltaTime);
                h.Grip.Phi = Mathf.LerpAngle(h.Grip.Phi * Mathf.Rad2Deg, bPhi * Mathf.Rad2Deg, ease) * Mathf.Deg2Rad;
                h.Grip.Delta = Mathf.Lerp(h.Grip.Delta, bDelta, ease);
                h.Grip.Slide = Mathf.Lerp(h.Grip.Slide, bSlide, ease);
            }
            else
                (h.Grip.Phi, h.Grip.Delta, h.Grip.Slide, h.Grip.Has) = (bPhi, bDelta, bSlide, true);
            var palm = u * Mathf.Cos(h.Grip.Phi) + v * Mathf.Sin(h.Grip.Phi);
            return Pose(h, at, a, palm, h.Grip.Delta, h.Grip.Slide, radius, alongFingers);
        }

        /// <summary>
        /// One way round the bar: the palm facing it, the fingers square across it slanted by delta towards the bar's
        /// +axis, wrapping so the index finger lies towards +axis; the wrist half the hand's thickness off the skin.
        /// </summary>
        private static (Vector3 Wrist, Vector3 Fingers, Vector3 Palm) Pose(Hand h, Vector3 at, Vector3 a, Vector3 p, float delta, float slide, float radius, float alongFingers)
        {
            var e2 = Vector3.Cross(p, a).normalized;
            // the index side of a hand with fingers e2 and palm p is IndexSign x (fingers x palm): put it towards +a
            if (Vector3.Dot(h.IndexSign * Vector3.Cross(e2, p), a) < 0f)
                e2 = -e2;
            var f = (a * Mathf.Sin(delta) + e2 * Mathf.Cos(delta)).normalized;
            var wrist = at + a * slide - p * (radius + PalmDepth) - f * alongFingers;
            return (wrist, f, p);
        }

        /// <summary>Turns the hand bone so its fingers and palm point the given ways.</summary>
        private static void Turn(Hand h, Vector3 fingers, Vector3 palm)
        {
            var nowF = h.Wrist.TransformDirection(h.FingersLocal);
            var nowP = h.Wrist.TransformDirection(h.PalmLocal);
            var want = Quaternion.LookRotation(fingers, palm);
            var now = Quaternion.LookRotation(nowF, nowP);
            h.Wrist.rotation = want * Quaternion.Inverse(now) * h.Wrist.rotation;
        }

        /// <summary>Bends each joint of a finger towards the palm by the angle (degrees per joint, a little less at the tip).</summary>
        private static void Curl(Transform[] chain, Vector3 palm, float degrees)
        {
            for (int j = 0; j < 3; j++)
            {
                var dir = chain[j + 1].position - chain[j].position;
                var axis = Vector3.Cross(dir, palm);
                if (axis.sqrMagnitude < 1e-8f)
                    continue;
                chain[j].rotation = Quaternion.AngleAxis(degrees * (j == 2 ? 0.8f : 1f), axis.normalized) * chain[j].rotation;
            }
        }

        /// <summary>Lays the thumb towards a point (its first joint turned, the others kept nearly straight).</summary>
        private static void PointThumb(Transform[] chain, Vector3 target)
        {
            var from = chain[3].position - chain[0].position;
            var to = target - chain[0].position;
            if (from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f)
                return;
            chain[0].rotation = Quaternion.FromToRotation(from, to) * chain[0].rotation;
        }

        /// <summary>Two-bone IK: the elbow in the plane of the pole, the hand at the target (or as far as the arm reaches).</summary>
        private static void Solve(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole)
        {
            var a = upper.position;
            float l1 = Vector3.Distance(a, lower.position), l2 = Vector3.Distance(lower.position, hand.position);
            var toTarget = target - a;
            float d = Mathf.Clamp(toTarget.magnitude, 0.02f, l1 + l2 - 0.002f);
            var dir = toTarget.normalized;
            float cos = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            float angle = Mathf.Acos(cos);
            var bendDir = Vector3.ProjectOnPlane(pole - a, dir);
            if (bendDir.sqrMagnitude < 1e-6f)
                bendDir = Vector3.ProjectOnPlane(Vector3.down, dir);
            bendDir.Normalize();
            var elbow = a + (dir * Mathf.Cos(angle) + bendDir * Mathf.Sin(angle)) * l1;
            upper.rotation = Quaternion.FromToRotation(lower.position - a, elbow - a) * upper.rotation;
            var reach = a + dir * d;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, reach - lower.position) * lower.rotation;
        }

        /// <summary>
        /// The scene of the grips for tools/assets/grip_view.py (Blender): every bone of the angler at rest and as posed
        /// now, and the rod's and the reel's parts (glTF node names: the rod's Model{i} are its Seg{i}), all as 4x4
        /// matrices (row-major) in the angler model's own space, with the glb ids to load.
        /// </summary>
        public string DumpPose()
        {
            if (_model == null || Rod?.GripRight == null)
                return "{}";
            var root = _model;
            var inv = root.worldToLocalMatrix;
            var bones = root.GetComponentsInChildren<Transform>(true);
            var sb = new System.Text.StringBuilder();
            static string M(Matrix4x4 m) => string.Join(",", new[]
            {
                m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23, m.m30, m.m31, m.m32, m.m33,
            }.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            // at rest: the arm bones back to their rest turns (the rest of the skeleton is never moved)
            foreach (var (bone, rest) in _rest)
                bone.localRotation = rest;
            var restM = new Dictionary<Transform, Matrix4x4>();
            foreach (var b in bones)
                restM[b] = inv * b.localToWorldMatrix;
            LateUpdate();
            sb.Append("{\"angler\":\"characters/player_angler\",\"bones\":{");
            bool first = true;
            foreach (var b in bones)
            {
                if (b == root)
                    continue;
                sb.Append(first ? "" : ",").Append('"').Append(b.name).Append("\":{\"rest\":[").Append(M(restM[b]))
                  .Append("],\"now\":[").Append(M(inv * b.localToWorldMatrix)).Append("]}");
                first = false;
            }
            sb.Append("},\"parts\":[");
            first = true;
            var rodRoot = Rod.GripRight.parent.parent;
            foreach (var t in rodRoot.GetComponentsInChildren<Transform>(false))
            {
                string node = t.name.StartsWith("Model") && int.TryParse(t.name.Substring(5), out int i) ? "Seg" + i : t.name;
                if (node is not ("Handle" or "Body" or "Rotor" or "Crank" or "Bail") && !node.StartsWith("Seg"))
                    continue;
                string glb = IsUnder(t, "ReelModel") ? "reel" : "rod";
                sb.Append(first ? "" : ",").Append("{\"glb\":\"").Append(glb).Append("\",\"node\":\"").Append(node)
                  .Append("\",\"m\":[").Append(M(inv * t.localToWorldMatrix)).Append("]}");
                first = false;
            }
            var frame = Rod.GripRight.parent;
            sb.Append("],\"rod\":\"").Append(Rod.ShownRodId).Append("\",\"reel\":\"").Append(Rod.ShownReelId)
              .Append("\",\"grip\":[").Append(M(inv * Matrix4x4.Translate(frame.TransformPoint(new Vector3(0f, Rod.GripRight.localPosition.y + SeatSlide, 0f)))))
              .Append("],\"knob\":[").Append(M(inv * Matrix4x4.Translate(Rod.GripLeft.position))).Append("]}");
            return sb.ToString();
        }

        private static bool IsUnder(Transform t, string name)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.name == name)
                    return true;
            return false;
        }

        /// <summary>For the smoke test: how far each wrist ended from its target (mm), to check the arms reach.</summary>
        public string Report()
        {
            if (_right == null || _left == null || Rod?.GripRight == null || Rod.GripLeft == null)
                return "no arms";
            float r = Vector3.Distance(_right.Wrist.position, Rod.GripRight.position) * 1000f;
            float l = Vector3.Distance(_left.Wrist.position, Rod.GripLeft.position) * 1000f;
            return $"right wrist {r:0} mm from the grip point, left wrist {l:0} mm from the knob";
        }
    }
}
