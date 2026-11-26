using System.Collections.Generic;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The angler's arms as the user posed them in Blender (characters/angler_grip.glb, written by
    /// tools/assets/export_grip.py from the grip .blend: the Meshy fisherman's arms with finger bones, laid in the rod's
    /// own frame). The grip is read off the posed file once: each wrist's place and turn relative to the rod (the right
    /// hand on the handle, the left on the crank's knob). Then the arms hang from the body at the camera (the figure's
    /// eyes on the camera, looking ahead) and every frame each arm reaches its hand's place on the rod by two-bone IK
    /// from the shoulder, the hand taking its posed turn; so the hands stay on the rod wherever it is held (the user's
    /// framing: upright, a little right of the screen's middle, the tip on the line of sight; lifted for the cast) and
    /// the left hand goes round with the knob while reeling, keeping its turn (the knob spins in the fingers).
    /// Used instead of <see cref="AnglerArms"/> when the glb is there.
    /// </summary>
    public sealed class GripArms : MonoBehaviour
    {
        public const string ModelId = "characters/angler_grip";

        public Fishing Rod { get; set; } = null!;

        /// <summary>Where the reel seat (the right hand) sits in the camera's space before the shift right: low in the
        /// middle of the screen, within the arm's reach.</summary>
        public static readonly Vector3 SeatOnScreen = new(0f, -0.16f, 0.38f);

        /// <summary>How far right of the screen's middle the rod stands, as a share of the screen's width (the user: 15 %).</summary>
        public const float ShiftRight = 0.15f;

        /// <summary>The angler's arms are shown (the user: hidden for now, only the rod in its place).</summary>
        public static bool ShowAngler = false;

        private sealed class Arm
        {
            public Transform Upper = null!, Lower = null!, Hand = null!;
            public Vector3 Offset;           // the wrist from its anchor on the rod, in the rod's frame
            public Quaternion Turn;          // the hand's turn in the rod's frame
            public Vector3 PoleSide;         // where the elbow points, in the body's space (from the shoulder)
            public bool OnKnob;
        }

        private Transform _model = null!, _frame = null!;
        private Arm? _right, _left;
        private readonly List<(Transform Bone, Quaternion AtRest)> _rest = new();

        public static GripArms? Create(Camera camera, Fishing rod)
        {
            if (rod.Frame == null || ModelLibrary.Spawn(ModelId, rod.Frame) is not { } model)
                return null;
            // the holder keeps the old arms' name: the smoke test and the menu shots look the arms up by it
            var holder = new GameObject("AnglerArms");
            holder.transform.SetParent(camera.transform, false);
            var arms = holder.AddComponent<GripArms>();
            arms.Rod = rod;
            arms._model = model.transform;
            arms._frame = rod.Frame;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer s)
                    s.updateWhenOffscreen = true;
            }
            // the grip as posed (the model still lies on the rod as in the file), then the arms onto the body
            arms._right = arms.Bind("r");
            arms._left = arms.Bind("l");
            arms.ToBody();
            arms.FrameRod(camera);
            return arms;
        }

        private static Transform? Bone(Transform model, string side, string mine, string humanoid) =>
            ModelLibrary.Find(model, $"{mine}_{(side == "r" ? "P" : "L")}") ?? ModelLibrary.Find(model, (side == "r" ? "Right" : "Left") + humanoid);

        /// <summary>One arm's grip, read while the model lies on the rod as posed.</summary>
        private Arm? Bind(string side)
        {
            var upper = Bone(_model, side, "ramie", "UpperArm");
            var lower = Bone(_model, side, "przedramie", "LowerArm");
            var hand = Bone(_model, side, "dlon", "Hand");
            if (upper == null || lower == null || hand == null)
                return null;
            foreach (var b in new[] { upper, lower, hand })
                _rest.Add((b, b.localRotation));
            var arm = new Arm { Upper = upper, Lower = lower, Hand = hand, OnKnob = side == "l" };
            arm.Turn = Quaternion.Inverse(_frame.rotation) * hand.rotation;
            if (arm.OnKnob)
            {
                // the knob keeps its place in the fist: the wrist's offset from where the knob was when posed (KnobRef)
                var knobRef = ModelLibrary.Find(_model, "KnobRef");
                var crankRef = ModelLibrary.Find(_model, "CrankRef");
                var knobThen = knobRef != null ? knobRef.position : crankRef != null ? crankRef.TransformPoint(Rod.GripLeft.localPosition) : hand.position;
                arm.Offset = Quaternion.Inverse(_frame.rotation) * (hand.position - knobThen);
            }
            else
                arm.Offset = _frame.InverseTransformPoint(hand.position);
            // the elbows point out and down from the body
            arm.PoleSide = new Vector3(side == "r" ? 0.35f : -0.35f, -0.55f, -0.1f);
            return arm;
        }

        /// <summary>The arms onto the body: the model under the camera with the figure's eyes on it, looking ahead.</summary>
        private void ToBody()
        {
            var eyes = ModelLibrary.Find(_model, "Eyes");
            var ahead = ModelLibrary.Find(_model, "EyesAhead");
            var up = ModelLibrary.Find(_model, "EyesUp");
            if (eyes == null || ahead == null || up == null)
                return;
            var e = _model.InverseTransformPoint(eyes.position);
            var f = _model.InverseTransformPoint(ahead.position) - e;
            var u = _model.InverseTransformPoint(up.position) - e;
            var turn = Quaternion.Inverse(Quaternion.LookRotation(f.normalized, u.normalized));
            _model.SetParent(transform, false);
            _model.localRotation = turn;
            _model.localPosition = -(turn * Vector3.Scale(e, _model.localScale));
        }

        /// <summary>
        /// The rod where the user wants it: upright on the screen, ShiftRight of the width right of the middle, the reel
        /// seat low (SeatOnScreen) and the tip on the line of sight, the reel hanging under the blank.
        /// </summary>
        private void FrameRod(Camera camera)
        {
            float k = 2f * ShiftRight * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * camera.aspect;
            var seat = new Vector3(k * SeatOnScreen.z, SeatOnScreen.y, SeatOnScreen.z);
            float reach = Rod.TipRestLocal.y - 0.1f;
            float zt = seat.z + Mathf.Sqrt(Mathf.Max(reach * reach - seat.y * seat.y, 0.01f));
            var along = (new Vector3(k * zt, 0f, zt) - seat).normalized;
            var down = Vector3.ProjectOnPlane(Vector3.down, along).normalized;
            // the rod's frame: +Y along the blank, the reel on its -Z side
            var frameTurn = Quaternion.LookRotation(-down, along);
            var rootTurn = frameTurn * Quaternion.Inverse(_frame.localRotation);
            var pos = seat - rootTurn * (_frame.localRotation * new Vector3(0f, 0.1f, 0f));
            Rod.SetFraming(pos, rootTurn);
            Debug.Log($"GRIPARMS the reel seat at {seat}, the tip {zt:0.00} m ahead on the line of sight");
        }

        private void LateUpdate()
        {
            if (Rod == null || _model == null)
                return;
            bool show = Rod.RodVisible && ShowAngler;
            _model.gameObject.SetActive(show);
            if (!show)
                return;
            foreach (var (bone, rest) in _rest)
                bone.localRotation = rest;
            if (_right != null)
                Reach(_right, _frame.TransformPoint(_right.Offset));
            if (_left != null && Rod.GripLeft != null)
                Reach(_left, Rod.GripLeft.position + _frame.rotation * _left.Offset);
        }

        private void Reach(Arm arm, Vector3 wrist)
        {
            var pole = arm.Upper.position + transform.TransformDirection(arm.PoleSide);
            Solve(arm.Upper, arm.Lower, arm.Hand, wrist, pole);
            arm.Hand.rotation = _frame.rotation * arm.Turn;
        }

        /// <summary>Two-bone IK (as AnglerArms.Solve): the elbow in the pole's plane, the wrist at the target.</summary>
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

        public string Report()
        {
            string Off(Arm? arm, Vector3 want) => arm == null ? "-" : $"{Vector3.Distance(arm.Hand.position, want) * 1000f:F1} mm";
            if (_right == null && _left == null)
                return "posed arms (no bones)";
            return $"posed arms; right wrist {Off(_right, _right != null ? _frame.TransformPoint(_right.Offset) : default)} off the handle, "
                + $"left {Off(_left, _left != null ? Rod.GripLeft.position + _frame.rotation * _left.Offset : default)} off the knob";
        }
    }
}
