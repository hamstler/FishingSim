using System.Collections.Generic;
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

        /// <summary>The cork handle's radius at the grip (the built handle's 13.5-16 mm).</summary>
        public const float HandleRadius = 0.015f;

        private Transform? _model;
        private Hand? _right, _left;
        private readonly List<(Transform Bone, Quaternion AtRest)> _rest = new();

        private sealed class Hand
        {
            public Transform Upper = null!, Lower = null!, Wrist = null!;
            public Vector3 FingersLocal, PalmLocal;     // in the hand bone's space, from the rest pose
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

        /// <summary>The right hand round the handle (FirstPersonBody: the seat grip).</summary>
        private void HoldHandle(Hand h, Transform cam)
        {
            var frame = Rod.GripRight.parent;
            var along = frame.up;                       // the blank's axis, butt to tip
            var top = frame.forward;                    // away from the reel
            var at = frame.TransformPoint(new Vector3(0f, Rod.GripRight.localPosition.y, 0f));
            // the rig's own side axis (GripPalm runs along it), the one on the camera's right: the camera's right itself
            // is nearly the fingers' way once the rig is rolled, and the palm projected from it turned up
            var side = Vector3.Dot(frame.right, cam.right) >= 0f ? frame.right : -frame.right;
            // the hand on the handle's right side, its fingers running down past the handle towards the reel
            // (GripFingers (0.05, 0.18, -1): towards the reel, a little forward), the palm against the handle
            var fingers = (-top * 1f + along * 0.18f + side * 0.05f).normalized;
            var palm = Vector3.ProjectOnPlane(-side, fingers).normalized;
            var wrist = at - palm * (HandleRadius + PalmDepth) - fingers * 0.05f;
            var pole = h.Upper.position - cam.up * 0.5f + cam.right * 0.35f;
            Solve(h.Upper, h.Lower, h.Wrist, wrist, pole);
            Turn(h, fingers, palm);
            // fingers round the handle (the index a little less: it rests ahead of the reel's stem)
            for (int i = 0; i < h.Fingers.Count; i++)
                Curl(h.Fingers[i], palm, i == 0 ? 72f : 88f);   // tight round a 3 cm handle (68 left the fingers sticking out)
            // the thumb along the top of the handle, 7.5 cm ahead of the hand
            if (h.Thumb != null)
                PointThumb(h.Thumb, at + along * 0.075f + top * (HandleRadius + 0.009f));
        }

        /// <summary>The left hand pinching the crank's knob with a straight wrist (FirstPersonBody: the crank pinch).</summary>
        private void PinchKnob(Hand h, Transform cam)
        {
            var knob = Rod.GripLeft.position;
            float upperLength = Vector3.Distance(h.Upper.position, h.Lower.position);
            var elbow = h.Upper.position - cam.up * upperLength * 0.9f;
            var fingers = (knob - elbow).normalized;
            // the palm towards the reel: from the knob to the nearest point of the rod's axis
            var frame = Rod.GripRight.parent;
            var onAxis = frame.position + Vector3.Project(knob - frame.position, frame.up);
            var palm = Vector3.ProjectOnPlane(onAxis - knob, fingers);
            palm = palm.sqrMagnitude > 1e-6f ? palm.normalized : Vector3.ProjectOnPlane(cam.right, fingers).normalized;
            var wrist = knob - fingers * 0.072f - palm * 0.016f;
            var pole = h.Upper.position - cam.up * 0.5f - cam.right * 0.35f;
            Solve(h.Upper, h.Lower, h.Wrist, wrist, pole);
            Turn(h, fingers, palm);
            // the index and the thumb pinch the knob, the other fingers fold below it (grip 0.8)
            for (int i = 0; i < h.Fingers.Count; i++)
                Curl(h.Fingers[i], palm, i == 0 ? 45f : 62f);
            if (h.Thumb != null)
                PointThumb(h.Thumb, knob + cam.up * 0.012f);
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
