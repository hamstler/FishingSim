using System.Collections.Generic;
using System.Linq;
using FishingSim.Data;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FishingSim.Game
{
    /// <summary>
    /// One villager: a MakeHuman character (assets/characters/mh_*.glb, the Quaternius skeleton) dressed in its
    /// pictures (the model's .json: albedo, normal, ambient occlusion, cut-out hair), playing the shared animations
    /// (anims_male/anims_female.glb: idle, talk, fold arms, farm work, chopping, sitting, walking).
    /// </summary>
    public sealed class Person : MonoBehaviour
    {
        private static readonly Dictionary<string, Dictionary<string, AnimationClip>> Clips = new();
        private static readonly Dictionary<string, Material> Mats = new();

        public string Sex { get; private set; } = "M";
        private Animation? _legacy;
        private Animator? _animator;
        private PlayableGraph _graph;
        private AnimationClipPlayable _playable;
        private AnimationClip? _clip;

        [System.Serializable]
        private sealed class Look
        {
            public string sex = "M";
            public float pelvis_scale = 1f;
        }

        public static Person? Create(string model, Transform parent, Vector3 at, float yaw, string anim)
        {
            var go = ModelLibrary.Spawn("characters/" + model);
            if (go == null)
                return null;
            var holder = new GameObject("Villager " + model);
            holder.transform.SetParent(parent, false);
            holder.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            go.transform.SetParent(holder.transform, false);
            var p = holder.AddComponent<Person>();
            var json = ModelLibrary.Json("characters/" + model);
            var look = json != null ? JsonUtility.FromJson<Look>(json) : new Look();
            p.Sex = look.sex;
            Dress(go, model, json);
            p.Setup(go);
            p.Play(anim);
            return p;
        }

        private static AnimationClip? ClipFor(string sex, string name)
        {
            string file = sex == "F" ? "anims_female" : "anims_male";
            if (!Clips.TryGetValue(file, out var clips))
            {
                clips = new Dictionary<string, AnimationClip>();
                // the clips glTFast made (legacy) on the loaded animations file
                if (ModelLibrary.Template("characters/" + file) is { } t)
                    foreach (var anim in t.GetComponentsInChildren<Animation>(true))
                        foreach (AnimationState state in anim)
                            if (state.clip != null)
                                clips[state.clip.name] = state.clip;
                if (clips.Count > 0)
                    Clips[file] = clips;
            }
            return clips.TryGetValue(name, out var clip) ? clip : clips.Values.FirstOrDefault(c => c.name.Contains(name));
        }

        private void Setup(GameObject model)
        {
            var probe = ClipFor(Sex, "Idle_Loop");
            if (probe != null && probe.legacy)
            {
                // on the same node as in the animations file (the clips' paths start there)
                var host = model.transform;
                if (ModelLibrary.Template(Sex == "F" ? "characters/anims_female" : "characters/anims_male") is { } t
                    && t.GetComponentInChildren<Animation>(true) is { } src && src.transform != t.transform
                    && ModelLibrary.Find(model.transform, src.name) is { } same)
                    host = same;
                _legacy = host.GetComponent<Animation>() ?? host.gameObject.AddComponent<Animation>();
                _legacy.cullingType = AnimationCullingType.BasedOnRenderers;
            }
            else
            {
                _animator = model.AddComponent<Animator>();
                _animator.cullingMode = AnimatorCullingMode.CullCompletely;
                _graph = PlayableGraph.Create("Villager");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            }
        }

        public void Play(string name)
        {
            var clip = ClipFor(Sex, name) ?? ClipFor(Sex, "Idle_Loop");
            if (clip == null || clip == _clip)
                return;
            _clip = clip;
            if (_legacy != null)
            {
                if (_legacy.GetClip(clip.name) == null)
                    _legacy.AddClip(clip, clip.name);
                _legacy.wrapMode = WrapMode.Loop;
                _legacy.CrossFade(clip.name, 0.3f);
                return;
            }
            if (_animator == null)
                return;
            if (_playable.IsValid())
                _playable.Destroy();
            _playable = AnimationClipPlayable.Create(_graph, clip);
            var output = AnimationPlayableOutput.Create(_graph, "Anim", _animator);
            output.SetSourcePlayable(_playable);
            _playable.SetTime(Random.value * clip.length);
            _graph.Play();
        }

        private void Update()
        {
            // the playables path does not loop by itself
            if (_clip != null && _playable.IsValid() && _playable.GetTime() > _clip.length)
                _playable.SetTime(_playable.GetTime() % Mathf.Max(_clip.length, 0.01f));
        }

        private void OnDestroy()
        {
            if (_graph.IsValid())
                _graph.Destroy();
        }

        // ------------------------------------------------------------------ clothes and skin
        [System.Serializable]
        private sealed class Slot
        {
            public float roughness_value = 0.7f;
            public string[] albedo = System.Array.Empty<string>();
            public string normal = "";
            public string ao = "";
            public bool alpha;
        }

        private static Texture2D? Picture(string name) => string.IsNullOrEmpty(name) ? null : ModelLibrary.Picture("characters/textures/" + name);

        public static void DressModel(GameObject go, string model, string? json) => Dress(go, model, json);

        /// <summary>Gives each surface its material from the model's json (slots: skin, eye, hair1, outfit, shoes, hat...).</summary>
        private static void Dress(GameObject go, string model, string? json)
        {
            if (json == null)
                return;
            var slots = Newtonsoft.Json.Linq.JObject.Parse(json)["materials"] as Newtonsoft.Json.Linq.JObject;
            if (slots == null)
                return;
            var shader = Shader.Find("FS/LitTex");
            if (shader == null)
                return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string slotName = mats[i] != null ? mats[i].name.Replace(" (Instance)", "").Trim() : "";
                    if (slots[slotName] is not { } token)
                        continue;
                    string key = model + "/" + slotName;
                    if (!Mats.TryGetValue(key, out var m))
                    {
                        var slot = token.ToObject<Slot>() ?? new Slot();
                        m = new Material(shader) { name = key };
                        if (Picture(slot.albedo.FirstOrDefault() ?? "") is { } a)
                            m.SetTexture("_MainTex", a);
                        if (Picture(slot.normal) is { } n)
                        {
                            m.SetTexture("_BumpMap", n);
                            m.SetFloat("_BumpScale", slotName == "skin" ? 0.6f : 1f);
                        }
                        if (Picture(slot.ao) is { } o)
                        {
                            m.SetTexture("_AOMap", o);
                            m.SetFloat("_AOStrength", slotName == "skin" ? 0.7f : 0.45f);
                        }
                        m.SetFloat("_Glossiness", Mathf.Clamp01(1f - slot.roughness_value));
                        if (slot.alpha)
                        {
                            m.SetFloat("_Cutoff", 0.4f);
                            m.SetFloat("_Cull", 0f);
                        }
                        if (slotName == "outfit" || slotName == "hat")
                            m.SetFloat("_Cull", 0f);
                        Mats[key] = m;
                    }
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }
    }

    /// <summary>
    /// The village's people (World/Props/People + Wanderer): a keeper in front of every trade, villagers at their
    /// spots (farm work, chopping, talking), two by the village fire, and walkers going between the square, the well and
    /// the houses by day (home and hidden at night).
    /// </summary>
    public sealed class People : MonoBehaviour
    {
        private static readonly string[] Men = { "mh_male_worker", "mh_male_casual", "mh_male_denim", "mh_male_old" };
        private static readonly string[] Women = { "mh_female_casual", "mh_female_middle" };

        private sealed class Walker
        {
            public Person P = null!;
            public List<Vector3> Stops = new();
            public int Next;
            public float Wait;
        }

        private readonly List<Walker> _walkers = new();
        private Lake _lake = null!;

        public void Build(Lake lake, Village village, Camp camp)
        {
            foreach (Transform c in transform)
                Destroy(c.gameObject);
            _walkers.Clear();
            _lake = lake;
            var rng = new System.Random(lake.Location.DisplayName.GetHashCode() & 0xffff);
            string Pick(bool woman) => woman ? Women[rng.Next(Women.Length)] : Men[rng.Next(Men.Length)];
            var plaza = new Vector3(lake.Location.PlazaCenter.X, 0f, lake.Location.PlazaCenter.Y);
            // keepers in front of the trades, facing out of the door
            foreach (var d in village.Doors)
            {
                var toPlaza = plaza - d.At;
                toPlaza.y = 0f;
                float yaw = Quaternion.LookRotation(toPlaza.sqrMagnitude > 0.1f ? toPlaza : Vector3.forward).eulerAngles.y;
                var at = d.At;
                at.y = lake.HeightAt(at.x, at.z);
                Person.Create(Pick(rng.NextDouble() < 0.3), transform, at, yaw, rng.NextDouble() < 0.5 ? "Idle_Talking_Loop" : "Idle_FoldArms_Loop");
            }
            // villagers at their spots
            string[] work = { "Farm_Watering", "TreeChopping_Loop", "Idle_Loop", "Farm_Harvest", "Idle_Talking_Loop" };
            foreach (var spec in lake.Specs.Where(s => s.Kind == BuildingKind.Villager))
            {
                var at = new Vector3(spec.Position.X, lake.HeightAt(spec.Position.X, spec.Position.Y), spec.Position.Y);
                Person.Create(Pick(rng.NextDouble() < 0.4), transform, at, (float)rng.NextDouble() * 360f, work[rng.Next(work.Length)]);
            }
            // two sit by the village fire
            if (camp.Fires.Count > 1)
            {
                var fire = camp.Fires[1].transform.position;
                foreach (float a in new[] { 0.6f, 2.5f })
                {
                    var at = fire + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.7f;
                    float yaw = Quaternion.LookRotation(fire - at).eulerAngles.y;
                    Person.Create(Pick(a > 1f), transform, at, yaw, "Sitting_Idle_Loop");
                }
                camp.Fires[1].BurnUntil = float.MaxValue;   // the village keeps its hearth going
            }
            // walkers: square, doors, back
            var doors = village.Doors.Select(d => d.At).ToList();
            for (int i = 0; i < 4 && doors.Count > 0; i++)
            {
                var stops = new List<Vector3> { plaza };
                for (int k = 0; k < 3; k++)
                    stops.Add(doors[rng.Next(doors.Count)]);
                var start = stops[0];
                start.y = lake.HeightAt(start.x, start.z);
                var person = Person.Create(Pick(rng.NextDouble() < 0.45), transform, start, 0f, "Walk_Loop");
                if (person != null)
                    _walkers.Add(new Walker { P = person, Stops = stops, Next = 1, Wait = (float)rng.NextDouble() * 10f });
            }
        }

        public void Tick(float dt, float hour)
        {
            bool day = hour > 6.5f && hour < 20.5f;
            foreach (var w in _walkers)
            {
                w.P.gameObject.SetActive(day);
                if (!day)
                    continue;
                if (w.Wait > 0f)
                {
                    w.Wait -= dt;
                    w.P.Play("Idle_Loop");
                    continue;
                }
                var t = w.P.transform;
                var target = w.Stops[w.Next];
                var to = target - t.position;
                to.y = 0f;
                if (to.magnitude < 0.5f)
                {
                    w.Next = (w.Next + 1) % w.Stops.Count;
                    w.Wait = 6f + Random.value * 24f;
                    continue;
                }
                w.P.Play("Walk_Loop");
                var step = to.normalized * Mathf.Min(1.15f * dt, to.magnitude);
                var p = t.position + step;
                p.y = _lake.HeightAt(p.x, p.z);
                t.SetPositionAndRotation(p, Quaternion.Slerp(t.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-dt * 6f)));
            }
        }
    }
}
