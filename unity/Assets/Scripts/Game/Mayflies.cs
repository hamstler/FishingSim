using System.Collections.Generic;
using FishingSim.Core.Camp;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>World/InsectSwarms: up to 14 swarms of mayflies over the water's edge (nearest the pier first, 24 m apart),
    /// dancing on calm evenings by Sweeping.MayflyActivity; drawn within 70 m.</summary>
    public sealed class Mayflies : MonoBehaviour
    {
        private const int PerSwarm = 45;
        private readonly List<MeshRenderer> _swarms = new();
        private Material? _mat;
        private Mesh? _mesh;
        private float _tick;
        public DayCycle? Day { get; set; }

        public void Build(Lake lake)
        {
            foreach (var r in _swarms)
                Destroy(r.gameObject);
            _swarms.Clear();
            var candidates = new List<Vector3>();
            for (float x = Lake.XMin + 6f; x < Lake.XMax - 6f; x += 5f)
                for (float z = Lake.ZMin + 6f; z < Lake.ZMax - 6f; z += 5f)
                {
                    float h = lake.HeightAt(x, z);
                    if (h > 0.08f && h < 0.55f && lake.IsWalkable(x, z))
                        candidates.Add(new Vector3(x, h, z));
                }
            candidates.Sort((a, b) => (a.x * a.x + a.z * a.z).CompareTo(b.x * b.x + b.z * b.z));
            _mat ??= new Material(Shader.Find("FS/Mayfly")) { name = "Mayfly" };
            _mesh ??= MakeMesh();
            var placed = new List<Vector3>();
            foreach (var c in candidates)
            {
                if (placed.Count >= 14)
                    break;
                bool apart = true;
                foreach (var s in placed)
                    if (new Vector2(s.x - c.x, s.z - c.z).magnitude < 24f)
                        apart = false;
                if (!apart)
                    continue;
                placed.Add(c);
                var go = new GameObject("Mayflies");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(c.x, Mathf.Max(c.y, 0f), c.z);
                go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = false;
                _swarms.Add(r);
            }
        }

        private static Mesh MakeMesh()
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var uv2 = new List<Vector2>();
            var t = new List<int>();
            for (int i = 0; i < PerSwarm; i++)
            {
                int s = v.Count;
                Vector2[] c = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
                foreach (var p in c)
                {
                    v.Add(Vector3.zero);
                    uv.Add(p);
                    uv2.Add(new Vector2(i, 0));
                }
                t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            var m = new Mesh { name = "Mayflies" };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetUVs(1, uv2);
            m.SetTriangles(t, 0);
            m.bounds = new Bounds(new Vector3(0f, 1.3f, 0f), new Vector3(2.4f, 2.6f, 2.4f));
            return m;
        }

        private void Update()
        {
            if (Day == null || _mat == null)
                return;
            _tick += Time.deltaTime;
            if (_tick < 0.25f)
                return;
            _tick = 0f;
            float activity = Sweeping.MayflyActivity(Day.Hour, Day.Weather);
            var cam = Camera.main;
            foreach (var r in _swarms)
                r.enabled = activity > 0.02f && (cam == null || (r.transform.position - cam.transform.position).sqrMagnitude < 70f * 70f);
            _mat.SetFloat("_Fade", activity);
            _mat.SetFloat("_Light", Mathf.Clamp01((1f - Day.Night) * 1.4f));
        }
    }
}
