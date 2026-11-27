using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// Places a Meshy model (any scale, its longest side about 1.9 units) at its real size inside a holder: turned by
    /// <c>turn</c> first, then scaled (uniformly to a length, or per axis to a box), centred over the holder's origin
    /// across x and z, with its lowest point, middle or highest point at y 0. Returns the holder (a child of the parent)
    /// or null while the model is not loaded or missing.
    /// </summary>
    public static class ModelFit
    {
        public enum Anchor { Bottom, Centre, Top }

        /// <summary>Scaled uniformly so its longest side (after the turn) is <paramref name="length"/> metres.</summary>
        public static Transform? Place(string id, Transform parent, float length, Quaternion turn, Anchor anchor = Anchor.Bottom, string name = "")
            => Fit(id, parent, new Vector3(length, length, length), turn, anchor, name, uniform: true);

        /// <summary>Scaled per axis so its box (after the turn) is <paramref name="size"/> metres (x across, y up, z along).</summary>
        public static Transform? PlaceBox(string id, Transform parent, Vector3 size, Quaternion turn, Anchor anchor = Anchor.Bottom, string name = "")
            => Fit(id, parent, size, turn, anchor, name, uniform: false);

        private static Transform? Fit(string id, Transform parent, Vector3 size, Quaternion turn, Anchor anchor, string name, bool uniform)
        {
            if (!ModelLibrary.Has(id))
                return null;
            var holder = new GameObject(name.Length > 0 ? name : "Model_" + id.Replace('/', '_')).transform;
            holder.SetParent(parent, false);
            // a pivot carrying the scale in the holder's axes, the model turned inside it
            var pivot = new GameObject("Fit").transform;
            pivot.SetParent(holder, false);
            var model = ModelLibrary.Spawn(id, pivot);
            if (model == null)
            {
                Object.Destroy(holder.gameObject);
                return null;
            }
            var t = model.transform;
            t.localRotation = turn;
            t.localScale = Vector3.one;
            t.localPosition = Vector3.zero;
            var b = Measure(pivot, t);
            Vector3 k;
            if (uniform)
            {
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float s = longest > 1e-5f ? size.x / longest : 1f;
                k = new Vector3(s, s, s);
            }
            else
                k = new Vector3(Safe(size.x, b.size.x), Safe(size.y, b.size.y), Safe(size.z, b.size.z));
            pivot.localScale = k;
            float y = anchor switch { Anchor.Centre => b.center.y, Anchor.Top => b.max.y, _ => b.min.y };
            t.localPosition = new Vector3(-b.center.x, -y, -b.center.z);
            return holder;
        }

        private static float Safe(float want, float have) => have > 1e-5f ? want / have : 1f;

        /// <summary>The meshes' bounds in <paramref name="space"/>'s local frame.</summary>
        public static Bounds Measure(Transform space, Transform root)
        {
            bool first = true;
            var b = new Bounds();
            var toSpace = space.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null)
                    continue;
                var mb = mesh.bounds;
                var m = toSpace * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }
    }
}
