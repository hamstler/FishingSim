using System.Collections.Generic;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The angler's arms as the user posed them in Blender (characters/angler_grip.glb, written by
    /// tools/assets/export_grip.py from the grip .blend): the Meshy fisherman's arms with their modelled fists, laid in
    /// the rod's own frame, so they hang on the rod like the rod's and reel's models. The camera goes to the figure's
    /// eyes (the glb's Eyes/EyesAhead/EyesUp nodes: the rod is framed as in the Blender view from the eyes). The right
    /// hand stays on the handle as posed; the left fist keeps its offset from the crank (the glb's CrankRef, the crank as
    /// it was turned in the file), so as the crank turns the arm follows the knob by two-bone IK (shoulder kept), the hand
    /// keeping its turn on the rod (the knob spins in the fingers, as when reeling).
    /// Used instead of <see cref="AnglerArms"/> when the glb is there.
    /// </summary>
    public sealed class GripArms : MonoBehaviour
    {
        public const string ModelId = "characters/angler_grip";

        public Fishing Rod { get; set; } = null!;

        /// <summary>How far down the posed figure looks for the first-person view (degrees).</summary>
        public const float LookDown = 52f;

        private Transform _model = null!, _frame = null!;
        private Transform? _upper, _lower, _hand;
        private Vector3 _handOffset, _poleLocal;
        private Quaternion _handTurn;
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
            arms.Frame(camera);
            arms.Bind();
            return arms;
        }

        /// <summary>The camera at the figure's eyes: the rod's root turned and moved so the Eyes node sits on the camera,
        /// looking towards EyesAhead with EyesUp above.</summary>
        private void Frame(Camera camera)
        {
            var root = _frame.parent;
            var eyes = ModelLibrary.Find(_model, "Eyes");
            var ahead = ModelLibrary.Find(_model, "EyesAhead");
            var up = ModelLibrary.Find(_model, "EyesUp");
            if (eyes == null || ahead == null || up == null)
                return;
            var e = root.InverseTransformPoint(eyes.position);
            var f = root.InverseTransformPoint(ahead.position) - e;
            var u = root.InverseTransformPoint(up.position) - e;
            // the figure looks ahead with its hands at the belly, out of a first-person view: the camera's straight-ahead
            // is the figure looking down at its hands (as in the Blender view from the eyes), so the rod and hands sit
            // in the lower middle of the screen while the player looks at the water
            var look = Quaternion.LookRotation(f.normalized, u.normalized) * Quaternion.Euler(LookDown, 0f, 0f);
            var rot = Quaternion.Inverse(look);
            Rod.SetFraming(-(rot * e), rot);
            Debug.Log($"GRIPARMS eyes at {e} in the rod's root, framing {(-(rot * e))} {rot.eulerAngles}");
        }

        private void Bind()
        {
            // the hand-posed rig's bones, or the Meshy Humanoid rig's (with finger bones added in Blender)
            _upper = ModelLibrary.Find(_model, "ramie_L") ?? ModelLibrary.Find(_model, "LeftUpperArm");
            _lower = ModelLibrary.Find(_model, "przedramie_L") ?? ModelLibrary.Find(_model, "LeftLowerArm");
            _hand = ModelLibrary.Find(_model, "dlon_L") ?? ModelLibrary.Find(_model, "LeftHand");
            var crankRef = ModelLibrary.Find(_model, "CrankRef");
            if (_upper == null || _lower == null || _hand == null || crankRef == null)
            {
                Debug.LogWarning("GRIPARMS: the left arm's bones or CrankRef are missing; the arms stay as posed");
                _hand = null;
                return;
            }
            foreach (var b in new[] { _upper, _lower, _hand })
                _rest.Add((b, b.localRotation));
            // the knob keeps its place in the fist: where the knob was when the arms were posed (the crank as CrankRef has
            // it; Fishing.GripLeft is the knob in the crank's frame), and the wrist's offset from it in the rod's frame
            var knobRef = ModelLibrary.Find(_model, "KnobRef");
            var knobThen = knobRef != null ? knobRef.position : crankRef.TransformPoint(Rod.GripLeft.localPosition);
            _handOffset = Quaternion.Inverse(_frame.rotation) * (_hand.position - knobThen);
            // the hand keeps its turn on the rod (the knob spins in the fingers): only its place follows the crank
            _handTurn = Quaternion.Inverse(_frame.rotation) * _hand.rotation;
            // the elbow keeps bending the way it was posed: a pole out from the elbow, away from the shoulder-wrist line
            var s = _upper.position;
            var e = _lower.position;
            var w = _hand.position;
            var line = (w - s).normalized;
            var outward = Vector3.ProjectOnPlane(e - s, line);
            _poleLocal = _frame.InverseTransformPoint(e + (outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.down) * 0.25f);
        }

        private void LateUpdate()
        {
            if (Rod == null || _model == null)
                return;
            bool show = Rod.RodVisible;
            _model.gameObject.SetActive(show);
            if (!show || _hand == null || _upper == null || _lower == null || Rod.CrankPivot is not { } crank)
                return;
            foreach (var (bone, rest) in _rest)
                bone.localRotation = rest;
            var target = Rod.GripLeft.position + _frame.rotation * _handOffset;
            Solve(_upper, _lower, _hand, target, _frame.TransformPoint(_poleLocal));
            _hand.rotation = _frame.rotation * _handTurn;
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
            if (_hand == null || Rod.CrankPivot == null)
                return "posed arms (no crank)";
            float off = Vector3.Distance(_hand.position, Rod.GripLeft.position + _frame.rotation * _handOffset);
            return $"posed arms; left wrist {off * 1000f:F1} mm off its place by the knob";
        }
    }
}
