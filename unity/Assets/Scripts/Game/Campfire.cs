using System.Collections.Generic;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// A campfire (World/Props/Campfire): the stone fire pit (Poly Haven's CC0 scan when present), seat logs, flames
    /// (crossed quads of FS/Flame), a flickering warm light; lit with E it burns some game hours; cooking at it
    /// (Core/Camp/Cooking) leaves a meal buff.
    /// </summary>
    public sealed class Campfire : MonoBehaviour
    {
        public const float BurnHours = 3f;
        public static readonly List<Campfire> All = new();

        public bool Lit => BurnUntil > ClockNow;
        public float BurnUntil { get; set; } = -1f;
        public static float ClockNow { get; set; }
        public string Name { get; set; } = "Ognisko";

        private Light _light = null!;
        private GameObject _flames = null!;
        private Material _flameMat = null!;
        private float _flicker;

        public static float NearestHeat(Vector3 p)
        {
            float best = 0f;
            foreach (var f in All)
                if (f != null && f.Lit)
                    best = Mathf.Max(best, Mathf.Clamp01(1f - (Vector3.Distance(f.transform.position, p) - 2f) / 14f));
            return best;
        }

        public static Campfire Create(Transform parent, Vector3 at, string name, bool seats)
        {
            var go = new GameObject("Campfire");
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var fire = go.AddComponent<Campfire>();
            fire.Name = name;
            fire.Build(seats);
            return fire;
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Build(bool seats)
        {
            var lit = Materials.Lit(0.1f, 0.4f);
            var pit = ModelLibrary.Spawn("stone_fire_pit", transform);
            var k = new MeshKit();
            if (pit == null)
            {
                var stone = Col.Lin(0.42f, 0.41f, 0.39f);
                for (int i = 0; i < 10; i++)
                {
                    float a = i / 10f * Mathf.PI * 2f;
                    k.Blob(new Vector3(Mathf.Cos(a) * 0.55f, 0.08f, Mathf.Sin(a) * 0.55f), new Vector3(0.16f, 0.12f, 0.14f), i, 0.4f, stone, stone * 0.6f, false, 1);
                }
            }
            else
            {
                var b = ModelLibrary.BoundsOf("stone_fire_pit");
                float s = 1.3f / Mathf.Max(b.size.x, b.size.z, 0.01f);
                pit.transform.localScale = Vector3.one * s;
                pit.transform.localPosition = new Vector3(-b.center.x * s, -b.min.y * s, -b.center.z * s);
            }
            // logs crossed in the pit
            var wood = Col.Lin(0.32f, 0.22f, 0.14f);
            var charred = Col.Lin(0.08f, 0.07f, 0.06f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 0.8f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.35f;
                k.Tube(-d + Vector3.up * 0.08f, d + Vector3.up * 0.18f, 0.05f, 0.045f, 7, wood, charred, true);
            }
            if (seats)
                foreach (float a in new[] { 0.6f, 2.5f })
                {
                    var c = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.7f;
                    var side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.8f;
                    k.Tube(c - side + Vector3.up * 0.2f, c + side + Vector3.up * 0.2f, 0.2f, 0.19f, 12, Col.Lin(0.3f, 0.24f, 0.18f), Col.Lin(0.36f, 0.29f, 0.2f), true);
                }
            var logs = new GameObject("Logs");
            logs.transform.SetParent(transform, false);
            logs.AddComponent<MeshFilter>().sharedMesh = k.ToMesh("Campfire");
            logs.AddComponent<MeshRenderer>().sharedMaterial = lit;

            // flames: four crossed quads, uv.y up
            var f = new Mesh { name = "Flames" };
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var col = new List<Color>();
            var t = new List<int>();
            for (int q = 0; q < 4; q++)
            {
                float a = q * Mathf.PI / 4f;
                var side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.38f;
                int b = v.Count;
                v.Add(-side + Vector3.up * 0.1f); v.Add(side + Vector3.up * 0.1f); v.Add(side + Vector3.up * 1.15f); v.Add(-side + Vector3.up * 1.15f);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                var c = new Color(1f, 1f, 1f, q * 0.25f);
                col.Add(c); col.Add(c); col.Add(c); col.Add(c);
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2);
            }
            f.SetVertices(v);
            f.SetUVs(0, uv);
            f.SetColors(col);
            f.SetTriangles(t, 0);
            f.RecalculateBounds();
            _flames = new GameObject("Flames");
            _flames.transform.SetParent(transform, false);
            _flames.AddComponent<MeshFilter>().sharedMesh = f;
            var r = _flames.AddComponent<MeshRenderer>();
            _flameMat = Materials.Flame();
            r.sharedMaterial = _flameMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = Vector3.up * 0.8f;
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.6f, 0.3f);
            _light.range = 12f;
            _light.shadows = LightShadows.None;
        }

        private void Update()
        {
            bool lit = Lit;
            _flames.SetActive(lit);
            _light.enabled = lit;
            if (!lit)
                return;
            _flicker = Mathf.Lerp(_flicker, Random.Range(0.75f, 1.25f), Time.deltaTime * 12f);
            _light.intensity = 2.2f * _flicker;
            _flameMat.SetFloat("_Strength", 0.9f + 0.2f * _flicker);
        }
    }
}
