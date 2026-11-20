using System.Collections.Generic;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The user's Meshy models (fish, buildings, rods, reels), imported by glTFast from Resources/Models (CI converts the
    /// game's .glb files there with tools/unity/convert_glb.py: WebP pictures become JPEG/PNG). Missing models return
    /// null, so the game falls back to its built shapes.
    /// </summary>
    public static class ModelLibrary
    {
        private static readonly Dictionary<string, GameObject?> Prefabs = new();
        private static readonly Dictionary<string, Bounds> BoundsCache = new();

        public static GameObject? Prefab(string id)
        {
            if (Prefabs.TryGetValue(id, out var p))
                return p;
            p = Resources.Load<GameObject>("Models/" + id);
            Prefabs[id] = p;
            return p;
        }

        public static bool Has(string id) => Prefab(id) != null;

        /// <summary>A new copy of the model, or null.</summary>
        public static GameObject? Spawn(string id, Transform? parent = null)
        {
            var prefab = Prefab(id);
            if (prefab == null)
                return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = id;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            return go;
        }

        /// <summary>The model's bounding box in its own space (all its renderers).</summary>
        public static Bounds BoundsOf(string id)
        {
            if (BoundsCache.TryGetValue(id, out var b))
                return b;
            var go = Spawn(id);
            b = new Bounds(Vector3.zero, Vector3.zero);
            if (go != null)
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                bool first = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (first) { b = r.bounds; first = false; }
                    else b.Encapsulate(r.bounds);
                }
                Object.Destroy(go);
            }
            BoundsCache[id] = b;
            return b;
        }

        /// <summary>The JSON written beside a model (reel frames, a fish's nose), or null.</summary>
        public static string? Json(string id) => Resources.Load<TextAsset>("Models/" + id)?.text;

        /// <summary>A transform anywhere under <paramref name="root"/> with this name.</summary>
        public static Transform? Find(Transform root, string name)
        {
            if (root.name == name)
                return root;
            foreach (Transform c in root)
                if (Find(c, name) is { } t)
                    return t;
            return null;
        }

        /// <summary>
        /// A fish model laid along +Z (nose forward) and +Y up, one metre long and centred, under a new holder; null
        /// without a model for the species (FishMesh is then used).
        /// </summary>
        public static GameObject? Fish(string speciesId)
        {
            var model = Spawn("fish/" + speciesId);
            if (model == null)
                return null;
            var holder = new GameObject("Fish " + speciesId);
            var b = BoundsOf("fish/" + speciesId);
            float length = Mathf.Max(b.size.z, 0.01f);
            model.transform.SetParent(holder.transform, false);
            model.transform.localScale = Vector3.one / length;
            model.transform.localPosition = -b.center / length;
            return holder;
        }
    }
}
