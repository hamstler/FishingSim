using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The angler's own arms in first person (Fishing/FirstPersonBody + ArmIK): the player model's "Arms" mesh
    /// (assets/characters/player_angler.glb: MakeHuman hands and flannel sleeves on the Quaternius skeleton) under the
    /// camera, each arm solved by two-bone IK onto the rod: the right hand on the handle above the reel seat, the left
    /// on the reel's crank knob (or the butt without a reel model), elbows down and out.
    /// </summary>
    public sealed class AnglerArms : MonoBehaviour
    {
        public Fishing Rod { get; set; } = null!;

        private Transform? _upR, _lowR, _handR, _upL, _lowL, _handL, _model;

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
            arms._upR = ModelLibrary.Find(t, "upperarm_r");
            arms._lowR = ModelLibrary.Find(t, "lowerarm_r");
            arms._handR = ModelLibrary.Find(t, "hand_r");
            arms._upL = ModelLibrary.Find(t, "upperarm_l");
            arms._lowL = ModelLibrary.Find(t, "lowerarm_l");
            arms._handL = ModelLibrary.Find(t, "hand_l");
            // eyes (about 12 cm over the neck joint, 9 cm ahead of it) a little behind the camera
            if (ModelLibrary.Find(t, "neck_01") is { } neck)
            {
                var eyes = holder.transform.InverseTransformPoint(neck.position) + new Vector3(0f, 0.12f, 0.09f);
                model.transform.localPosition = -eyes + new Vector3(0f, 0f, -0.04f);
            }
            return arms;
        }

        private void LateUpdate()
        {
            if (_model == null || Rod == null)
                return;
            bool show = Rod.RodVisible;
            _model.gameObject.SetActive(show);
            if (!show)
                return;
            var cam = transform.parent;
            var down = -cam.up;
            if (_upR != null && _lowR != null && _handR != null && Rod.GripRight != null)
                Solve(_upR, _lowR, _handR, Rod.GripRight.position, _upR.position + down * 0.5f + cam.right * 0.35f);
            if (_upL != null && _lowL != null && _handL != null && Rod.GripLeft != null)
                Solve(_upL, _lowL, _handL, Rod.GripLeft.position, _upL.position + down * 0.5f - cam.right * 0.35f);
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
    }
}
