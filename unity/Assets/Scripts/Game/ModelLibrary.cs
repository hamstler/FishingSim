using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishingSim.Game
{
    /// <summary>
    /// The user's Meshy models (fish, buildings, rods, reels), the props and the people, read at run time by glTFast
    /// from StreamingAssets/Models (CI converts the game's .glb files there with tools/unity/convert_glb.py: WebP
    /// pictures become JPEG/PNG). Each model is loaded once into a hidden template (Preload, a coroutine) and copied
    /// from it (Spawn). Missing models give null, so the game falls back to its built shapes.
    /// </summary>
    public static class ModelLibrary
    {
        private static readonly Dictionary<string, GameObject?> Templates = new();
        private static readonly Dictionary<string, Task> Loading = new();
        private static readonly Dictionary<string, Bounds> BoundsCache = new();
        private static readonly List<GltfImport> Imports = new();
        private static Transform? _cache;

        public static string Root => Path.Combine(Application.streamingAssetsPath, "Models");

        private static Transform Cache
        {
            get
            {
                if (_cache != null)
                    return _cache;
                var go = new GameObject("ModelCache");
                go.SetActive(false);
                Object.DontDestroyOnLoad(go);
                return _cache = go.transform;
            }
        }

        public static string FileOf(string id) => Path.Combine(Root, id + ".glb");

        /// <summary>Every model id in a folder of StreamingAssets/Models ("fish", "rods"...).</summary>
        public static List<string> IdsIn(string folder)
        {
            var list = new List<string>();
            string dir = Path.Combine(Root, folder);
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.glb"))
                    list.Add(folder + "/" + Path.GetFileNameWithoutExtension(f));
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>Loads the models not loaded yet (a coroutine: the game keeps drawing meanwhile).</summary>
        public static IEnumerator Preload(IEnumerable<string> ids)
        {
            var waiting = new List<Task>();
            foreach (var id in ids)
                if (Start(id) is { } t)
                    waiting.Add(t);
            foreach (var t in waiting)
                while (!t.IsCompleted)
                    yield return null;
        }

        /// <summary>Starts loading one model (null when it is loaded, missing or loading already).</summary>
        public static Task? Start(string id)
        {
            if (Templates.ContainsKey(id))
                return null;
            if (Loading.TryGetValue(id, out var running))
                return running;
            if (!File.Exists(FileOf(id)))
            {
                Templates[id] = null;
                return null;
            }
            var task = Load(id);
            Loading[id] = task;
            return task;
        }

        private static async Task Load(string id)
        {
            GameObject? template = null;
            try
            {
                var bytes = await Task.Run(() => File.ReadAllBytes(FileOf(id)));
                var gltf = new GltfImport();
                var settings = new ImportSettings { AnimationMethod = AnimationMethod.Legacy, GenerateMipMaps = true, AnisotropicFilterLevel = 8 };
                if (await gltf.LoadGltfBinary(bytes, new Uri(FileOf(id)), settings))
                {
                    template = new GameObject(id);
                    template.transform.SetParent(Cache, false);
                    if (!await gltf.InstantiateMainSceneAsync(template.transform))
                    {
                        Object.Destroy(template);
                        template = null;
                    }
                    else
                        Imports.Add(gltf);   // keeps the meshes, pictures and materials alive
                }
                else
                    Debug.LogWarning($"Model {id} could not be read");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Model {id}: {e.Message}");
            }
            Templates[id] = template;
            Loading.Remove(id);
        }

        public static bool Has(string id) => Templates.TryGetValue(id, out var t) && t != null;

        /// <summary>A copy of a loaded model, or null (not loaded or missing).</summary>
        public static GameObject? Spawn(string id, Transform? parent = null)
        {
            if (!Templates.TryGetValue(id, out var template) || template == null)
                return null;
            var go = Object.Instantiate(template, parent, false);
            go.name = id;
            go.SetActive(true);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            return go;
        }

        /// <summary>The template itself (for reading its animation clips).</summary>
        public static GameObject? Template(string id) => Templates.TryGetValue(id, out var t) ? t : null;

        /// <summary>The model's bounding box in its own space (all its renderers).</summary>
        public static Bounds BoundsOf(string id)
        {
            if (BoundsCache.TryGetValue(id, out var b))
                return b;
            var go = Spawn(id);
            b = new Bounds(Vector3.zero, Vector3.zero);
            if (go != null)
            {
                go.transform.SetParent(null, false);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                bool first = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var rb = r is SkinnedMeshRenderer s ? s.bounds : r.bounds;
                    if (first) { b = rb; first = false; }
                    else b.Encapsulate(rb);
                }
                Object.Destroy(go);
                BoundsCache[id] = b;
            }
            return b;
        }

        /// <summary>The JSON written beside a model (reel frames, a fish's nose, a character's pictures), or null.</summary>
        public static string? Json(string id)
        {
            string path = Path.Combine(Root, id + ".json");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private static readonly Dictionary<string, Texture2D?> Pictures = new();

        /// <summary>A loose picture from StreamingAssets/Models (png or jpg), or null.</summary>
        public static Texture2D? Picture(string pathWithoutExtension)
        {
            if (Pictures.TryGetValue(pathWithoutExtension, out var t))
                return t;
            t = null;
            foreach (var ext in new[] { ".png", ".jpg" })
            {
                string path = Path.Combine(Root, pathWithoutExtension + ext);
                if (!File.Exists(path))
                    continue;
                t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = pathWithoutExtension, anisoLevel = 4 };
                t.LoadImage(File.ReadAllBytes(path), false);
                t.Compress(true);
                t.Apply(true, true);
                break;
            }
            Pictures[pathWithoutExtension] = t;
            return t;
        }

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
        /// without a loaded model for the species (FishMesh is then used).
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
