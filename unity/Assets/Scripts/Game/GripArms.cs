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
        /// <summary>The new fisherman's arms (tools/assets/untex_grip.py + export_untex_grip.py: posed by measurement, rotations
        /// only) when they are there, else the hand-posed Meshy arms.</summary>
        public const string UntexId = "characters/angler_untex";
        public const string GripId = "characters/angler_grip";
        public static string ModelId => System.IO.File.Exists(ModelLibrary.FileOf(UntexId)) ? UntexId : GripId;
        private const int ArmsLayer = 29;
        private const float CrankGripTilt = 10f;

        public Fishing Rod { get; set; } = null!;

        /// <summary>Where the reel seat (the right hand) sits in the camera's space before the shift right: low in the
        /// middle of the screen, within the arm's reach.</summary>
        public static Vector3 SeatOnScreen = new(0f, -0.197f, 0.45f);

        /// <summary>The new fisherman's arms are shorter: the seat closer, 18 degrees under the eyes, the hands in view.</summary>
        public static readonly Vector3 UntexSeat = new(0f, -0.115f, 0.36f);

        /// <summary>How far right of the screen's middle the rod stands, as a share of the screen's width (the user: 15 %).</summary>
        public const float ShiftRight = 0.15f;

        public static bool ShowAngler = true;

        private sealed class Arm
        {
            public Transform Upper = null!, Lower = null!, Hand = null!;
            public Vector3 Offset;           // the wrist from its anchor on the rod, in the rod's frame
            public Quaternion Turn;          // the hand's turn in the rod's frame
            public Vector3 UpperRestPosition, LowerRestPosition, PalmAxis;
            public bool OnKnob;
            public Vector3 PoleLocal;        // the posed elbow's side, in the body's space (rigid arms)
            public Quaternion HandInLower;   // the posed hand's turn relative to the forearm (rigid arms)
        }

        private Transform _model = null!, _frame = null!;
        private Light _fill = null!;
        private bool _rigid;
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
            // the measured arms are only turned (no bone moved or stretched), with the reach of their own proportions
            arms._rigid = ModelId == UntexId;
            if (arms._rigid)
                SeatOnScreen = UntexSeat;
            var fill = new GameObject("Forearm fill").AddComponent<Light>();
            fill.transform.SetParent(holder.transform, false);
            fill.transform.localEulerAngles = new Vector3(-24f, 155f, 0f);
            fill.type = LightType.Directional;
            fill.color = new Color(0.83f, 0.89f, 1f);
            fill.cullingMask = 1 << ArmsLayer;
            fill.shadows = LightShadows.None;
            arms._fill = fill;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                r.gameObject.layer = ArmsLayer;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer s)
                    s.updateWhenOffscreen = true;
            }
            // the grip as posed (the model still lies on the rod as in the file), then the arms onto the body
            arms._right = arms.Bind("r");
            arms._left = arms.Bind("l");
            arms.ToBody();
            if (arms._rigid)
                foreach (var a in new[] { arms._right, arms._left })
                    if (a != null)
                    {
                        var mid = (a.Upper.position + a.Hand.position) * 0.5f;
                        a.PoleLocal = holder.transform.InverseTransformDirection(a.Lower.position - mid);
                        a.HandInLower = Quaternion.Inverse(a.Lower.rotation) * a.Hand.rotation;
                    }
            FrameRod(camera, rod);
            return arms;
        }

        /// <summary>Only the rod's framing (the angler hidden: GameRoot does not even load the arms' model).</summary>
        public static void FrameOnly(Camera camera, Fishing rod)
        {
            if (rod.Frame != null)
                FrameRod(camera, rod);
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
            arm.UpperRestPosition = upper.localPosition;
            arm.LowerRestPosition = lower.localPosition;
            arm.Turn = Quaternion.Inverse(_frame.rotation) * hand.rotation;
            var knuckle = ModelLibrary.Find(_model, $"middle_01_{side}");
            var palm = knuckle != null ? knuckle.position - hand.position : hand.position - lower.position;
            arm.PalmAxis = hand.InverseTransformDirection(palm.normalized);
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
        private static void FrameRod(Camera camera, Fishing rod)
        {
            var frame = rod.Frame!;
            float k = 2f * ShiftRight * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * camera.aspect;
            var seat = new Vector3(k * SeatOnScreen.z, SeatOnScreen.y, SeatOnScreen.z);
            float reach = rod.TipRestLocal.y - 0.1f;
            float zt = seat.z + Mathf.Sqrt(Mathf.Max(reach * reach - seat.x * seat.x - seat.y * seat.y, 0.01f));
            var along = (new Vector3(0f, 0f, zt) - seat).normalized;
            var down = Vector3.ProjectOnPlane(Vector3.down, along).normalized;
            // the rod's frame: +Y along the blank, the reel on its -Z side
            var frameTurn = Quaternion.LookRotation(-down, along);
            var rootTurn = frameTurn * Quaternion.Inverse(frame.localRotation);
            var pos = seat - rootTurn * (frame.localRotation * new Vector3(0f, 0.1f, 0f));
            rod.SetFraming(pos, rootTurn);
            Debug.Log($"GRIPARMS the reel seat at {seat}, the tip {zt:0.00} m ahead on the line of sight");
        }

        private void LateUpdate()
        {
            if (Rod == null || _model == null)
                return;
            bool show = Rod.RodVisible && ShowAngler;
            _model.gameObject.SetActive(show);
            _fill.enabled = show;
            if (!show)
                return;
            _fill.intensity = Mathf.Lerp(0.02f, 0.55f, Rod.Day.Light);
            foreach (var (bone, rest) in _rest)
                bone.localRotation = rest;
            if (_rigid)
            {
                // each hand exactly as posed on the rod and the knob; the arms turned to reach them, nothing moved
                if (_right != null)
                    RigidReach(_right, _frame.TransformPoint(_right.Offset), _frame.rotation * _right.Turn);
                if (_left != null && Rod.GripLeft != null)
                    RigidReach(_left, Rod.GripLeft.position + _frame.rotation * _left.Offset, _frame.rotation * _left.Turn);
                return;
            }
            if (_right != null)
            {
                var roll = HandleGripRoll(_right);
                Reach(_right, HandleWrist(_right, roll), roll * _frame.rotation);
            }
            if (_left != null && Rod.GripLeft != null)
                Reach(_left, CrankWrist(_left), CrankGripRotation);
        }

        /// <summary>Two-bone IK by rotations only (the elbow on the posed side), the forearm rolled so the wrist keeps the
        /// posed twist (the skin's twist bone carries it along the forearm), the hand at its posed turn.</summary>
        private void RigidReach(Arm arm, Vector3 wrist, Quaternion handRot)
        {
            var shoulder = arm.Upper.position;
            float l1 = Vector3.Distance(shoulder, arm.Lower.position), l2 = Vector3.Distance(arm.Lower.position, arm.Hand.position);
            var to = wrist - shoulder;
            float d = Mathf.Clamp(to.magnitude, 0.02f, l1 + l2 - 0.001f);
            var dir = to.normalized;
            float cos = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            var bend = Vector3.ProjectOnPlane(transform.TransformDirection(arm.PoleLocal), dir);
            if (bend.sqrMagnitude < 1e-8f)
                bend = Vector3.ProjectOnPlane(-transform.up, dir);
            bend.Normalize();
            var elbow = shoulder + (dir * cos + bend * Mathf.Sqrt(1f - cos * cos)) * l1;
            arm.Upper.rotation = Quaternion.FromToRotation(arm.Lower.position - shoulder, elbow - shoulder) * arm.Upper.rotation;
            var reach = shoulder + dir * d;
            arm.Lower.rotation = Quaternion.FromToRotation(arm.Hand.position - arm.Lower.position, reach - arm.Lower.position) * arm.Lower.rotation;
            // roll the forearm about its own axis towards the posed wrist twist
            var axis = (arm.Hand.position - arm.Lower.position).normalized;
            var wantLower = handRot * Quaternion.Inverse(arm.HandInLower);
            var delta = wantLower * Quaternion.Inverse(arm.Lower.rotation);
            float along = Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), axis);
            float roll = Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(along, delta.w) * Mathf.Rad2Deg);
            arm.Lower.rotation = Quaternion.AngleAxis(roll, axis) * arm.Lower.rotation;
            arm.Hand.rotation = handRot;
        }

        private Quaternion CrankGripRotation => Quaternion.AngleAxis(CrankGripTilt, transform.forward) * _frame.rotation;

        private Vector3 CrankWrist(Arm arm) => Rod.GripLeft.position + CrankGripRotation * arm.Offset;

        private Quaternion HandleGripRoll(Arm arm)
        {
            var axis = _frame.up;
            var authored = _frame.rotation * arm.Turn * arm.PalmAxis;
            var wanted = transform.up * .85f - transform.right * .35f;
            float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(authored, axis), Vector3.ProjectOnPlane(wanted, axis), axis);
            return Quaternion.AngleAxis(angle, axis);
        }

        private Vector3 HandleWrist(Arm arm, Quaternion roll) =>
            _frame.position + roll * (_frame.TransformPoint(arm.Offset) - _frame.position);

        private void Reach(Arm arm, Vector3 wrist, Quaternion gripRotation)
        {
            var desired = gripRotation * arm.Turn;
            SeatArm(arm, wrist, desired * arm.PalmAxis);
            var axis = (arm.Hand.position - arm.Lower.position).normalized;
            var difference = desired * Quaternion.Inverse(arm.Hand.rotation);
            float along = Vector3.Dot(new Vector3(difference.x, difference.y, difference.z), axis);
            float roll = Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(along, difference.w) * Mathf.Rad2Deg);
            arm.Lower.rotation = Quaternion.AngleAxis(Mathf.Clamp(roll * .5f, -55f, 55f), axis) * arm.Lower.rotation;
            arm.Hand.rotation = desired;
        }

        private void SeatArm(Arm arm, Vector3 wrist, Vector3 palmDirection)
        {
            // Fit the visible wrist first. The upper sleeve can change length behind the camera.
            arm.Upper.localPosition = arm.UpperRestPosition;
            arm.Lower.localPosition = arm.LowerRestPosition;
            arm.Upper.position -= transform.forward * .065f;
            float length = Vector3.Distance(arm.Lower.position, arm.Hand.position);
            var forearm = (palmDirection + transform.forward * .045f).normalized;
            var elbow = wrist - forearm * length;
            arm.Upper.rotation = Quaternion.FromToRotation(arm.Lower.position - arm.Upper.position, elbow - arm.Upper.position) * arm.Upper.rotation;
            arm.Lower.position = elbow;
            arm.Lower.rotation = Quaternion.FromToRotation(arm.Hand.position - elbow, wrist - elbow) * arm.Lower.rotation;
        }

        public string Report()
        {
            string Off(Arm? arm, Vector3 want) => arm == null ? "-" : $"{Vector3.Distance(arm.Hand.position, want) * 1000f:F1} mm";
            string Angle(Arm? arm) => arm == null ? "-" : $"{Vector3.Angle(arm.Hand.position - arm.Lower.position, arm.Hand.rotation * arm.PalmAxis):F1} deg";
            if (_right == null && _left == null)
                return "posed arms (no bones)";
            if (_rigid)
                return $"measured arms (rotations only); right wrist {Off(_right, _right != null ? _frame.TransformPoint(_right.Offset) : default)} off its place on the handle, "
                    + $"left {Off(_left, _left != null ? Rod.GripLeft.position + _frame.rotation * _left.Offset : default)} off its place at the knob";
            return $"posed arms; right wrist {Off(_right, _right != null ? HandleWrist(_right, HandleGripRoll(_right)) : default)} off the handle, "
                + $"left {Off(_left, _left != null ? CrankWrist(_left) : default)} off the knob"
                + $"; palm angles right {Angle(_right)}, left {Angle(_left)}";
        }
    }
}
