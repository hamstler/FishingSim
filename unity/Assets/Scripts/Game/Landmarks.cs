using System;
using System.Collections.Generic;
using UnityEngine;
using Mathf = UnityEngine.Mathf;

namespace FishingSim.Game
{
    /// <summary>
    /// World/Props/Landmarks: each map's own sights far from the village (the island ruin and the wayside chapel of
    /// Jezioro Zielone, the marsh hut on stilts, the fish-pond dike with its sluice and angling platforms, the mountain
    /// waterfall and rock islets), placed as Scenery.BuildLandmarks places them.
    /// </summary>
    public sealed class Landmarks : MonoBehaviour
    {
        public List<(Vector3 At, float Radius)> Solids { get; } = new();

        /// <summary>Where trees should stand (the dike's row), in world space.</summary>
        public List<Vector3> TreeSpots { get; } = new();

        private readonly List<GameObject> _made = new();
        private Material? _lit, _water;

        public void Build(Lake lake)
        {
            foreach (var go in _made)
                Destroy(go);
            _made.Clear();
            Solids.Clear();
            TreeSpots.Clear();
            _lit ??= Materials.Lit(0.12f, 0.45f);
            if (_water == null)
            {
                _water = Materials.Lit(0.75f, 0f);
                _water.name = "Waterfall";
            }
            var loc = lake.Location;
            foreach (var entry in loc.Landmarks)
            {
                var parts = entry.Split(new[] { ',' }, 2);
                parts[0] = parts[0].Trim();
                if (parts.Length < 2)
                    continue;
                string kind = parts[0];
                var n = Data.LocationData.Numbers(parts[1]);
                if (n.Length < 2)
                    continue;
                float x = n[0], z = n[1];
                float depth = lake.DepthAt(x, z), ground = lake.HeightAt(x, z);
                float size = n.Length >= 4 ? n[3] : depth;
                float y = kind switch
                {
                    "stilthouse" or "platforms" => 0f,
                    "rocks" => ground > 0f ? ground - 0.4f : ground * 0.3f,
                    "waterfall" => MathF.Max(ground, 0f) - 0.4f,
                    _ => ground,
                };
                float yaw = n.Length >= 3 ? n[2] : MathF.Atan2(-x, loc.OutlineCz - z) * Mathf.Rad2Deg;
                var k = new MeshKit();
                var w = new MeshKit();
                var spots = new List<Vector3>();
                switch (kind)
                {
                    case "tower": Tower(k); break;
                    case "chapel": Chapel(k); break;
                    case "stilthouse": StiltHouse(k, MathF.Max(size, 1f)); break;
                    case "dike": Dike(k, MathF.Max(size, 20f), spots); break;
                    case "waterfall": Waterfall(k, w); break;
                    case "rocks": Rocks(k); break;
                    case "platforms": Platform(k, MathF.Max(size, 0.8f)); break;
                    default: continue;
                }
                var root = new GameObject("Landmark " + kind);
                root.transform.SetParent(transform, false);
                root.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f));
                Part(root, k, _lit, true);
                if (w.Count > 0)
                    Part(root, w, _water, false);
                _made.Add(root);
                foreach (var s in spots)
                    TreeSpots.Add(root.transform.TransformPoint(s));
                if (kind is "tower" or "chapel")
                    Solids.Add((new Vector3(x, y, z), kind == "tower" ? 3.2f : 3.6f));
            }
        }

        private static void Part(GameObject root, MeshKit k, Material m, bool shadows)
        {
            var go = new GameObject("Mesh");
            go.transform.SetParent(root.transform, false);
            var mesh = k.ToMesh(root.name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Color C(float r, float g, float b) => Col.Lin(r, g, b);

        private static void Box(MeshKit k, Vector3 size, Vector3 at, Color c, Vector3 rot = default) => k.Box(at, size, Quaternion.Euler(rot), c);

        /// <summary>Godot's CylinderMesh (top radius, bottom radius, height) centred at <paramref name="at"/>.</summary>
        private static void Cyl(MeshKit k, float top, float bottom, float height, Vector3 at, Color c, int sides, Vector3 rot = default)
        {
            var q = Quaternion.Euler(rot);
            var a = at + q * Vector3.down * height * 0.5f;
            var b = at + q * Vector3.up * height * 0.5f;
            k.Tube(a, b, Mathf.Max(bottom, 0.001f), Mathf.Max(top, 0.001f), sides, c, c, true);
        }

        private static void Tower(MeshKit k)
        {
            var stone = C(0.78f, 0.74f, 0.66f);
            var dark = C(0.08f, 0.07f, 0.06f);
            var ivy = C(0.2f, 0.36f, 0.14f);
            Cyl(k, 2.5f, 2.9f, 7.5f, new Vector3(0, 3.5f, 0), stone, 14);
            Cyl(k, 3.2f, 3.4f, 0.8f, new Vector3(0, 0.1f, 0), stone, 14);
            float[] tops = { 1.8f, 0.5f, 2.6f, 0f, 1.3f, 2.2f, 0f, 1.0f, 0.4f, 1.6f };
            for (int i = 0; i < tops.Length; i++)
            {
                if (tops[i] <= 0f)
                    continue;
                float a = i * Mathf.PI * 2f / tops.Length;
                Box(k, new Vector3(1.5f, tops[i], 0.8f), new Vector3(MathF.Sin(a) * 2.2f, 7.25f + tops[i] / 2f, MathF.Cos(a) * 2.2f), stone, new Vector3(0, a * Mathf.Rad2Deg, 0));
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 2f / 4f + 0.4f;
                Box(k, new Vector3(0.22f, 1.1f, 0.3f), new Vector3(MathF.Sin(a) * 2.62f, 4.6f + (i % 2) * 1.4f, MathF.Cos(a) * 2.62f), dark, new Vector3(0, a * Mathf.Rad2Deg, 0));
            }
            Box(k, new Vector3(1.2f, 2.2f, 0.5f), new Vector3(0, 1.4f, 2.7f), dark);
            Box(k, new Vector3(1.4f, 4.5f, 0.4f), new Vector3(-1.9f, 3.2f, 1.6f), ivy, new Vector3(0, -50f, 0));
            Box(k, new Vector3(1.0f, 2.6f, 0.4f), new Vector3(2.2f, 2.0f, -1.2f), ivy, new Vector3(0, 120f, 0));
            Box(k, new Vector3(1.1f, 0.6f, 0.8f), new Vector3(3.6f, 0.3f, 1.2f), stone, new Vector3(8f, 30f, 0));
            Box(k, new Vector3(0.9f, 0.5f, 0.7f), new Vector3(-3.2f, 0.25f, 2.4f), stone, new Vector3(0, -20f, 12f));
            Box(k, new Vector3(1.3f, 0.7f, 0.9f), new Vector3(1.0f, 0.35f, -3.8f), stone, new Vector3(-6f, 70f, 0));
        }

        /// <summary>A gable roof (Godot's AddRoof): two sloping slabs over w x d at eaves height h, ridge rise r.</summary>
        private static void Roof(MeshKit k, Vector3 at, float w, float d, float h, float rise, float overhang, Color c)
        {
            float half = d / 2f + overhang;
            float slope = Mathf.Atan2(rise, d / 2f) * Mathf.Rad2Deg;
            float len = Mathf.Sqrt(half * half + rise * rise * (half / (d / 2f)) * (half / (d / 2f)));
            foreach (float s in new[] { -1f, 1f })
                Box(k, new Vector3(w + overhang * 2f, 0.14f, len), at + new Vector3(0, h + rise / 2f, s * half / 2f), c, new Vector3(s * slope, 0, 0));
            // the gable ends
            foreach (float e in new[] { -1f, 1f })
            {
                var p0 = at + new Vector3(e * w / 2f, h, -d / 2f);
                var p1 = at + new Vector3(e * w / 2f, h, d / 2f);
                var p2 = at + new Vector3(e * w / 2f, h + rise, 0f);
                var n = new Vector3(e, 0, 0);
                int i0 = k.Add(p0, n, c), i1 = k.Add(p1, n, c), i2 = k.Add(p2, n, c);
                if (e > 0) k.Tri(i0, i2, i1); else k.Tri(i0, i1, i2);
            }
        }

        private static void Chapel(MeshKit k)
        {
            const float w = 6.5f, d = 4.4f, h = 3.6f;
            var wall = C(0.95f, 0.92f, 0.85f);
            var stone = C(0.62f, 0.6f, 0.56f);
            var roof = C(0.3f, 0.22f, 0.18f);
            var iron = C(0.15f, 0.13f, 0.12f);
            Box(k, new Vector3(w + 0.3f, 0.5f, d + 0.3f), Vector3.zero, stone);
            Box(k, new Vector3(w, h, d), new Vector3(0, h / 2f, 0), wall);
            Roof(k, Vector3.zero, w, d, h, 3.0f, 0.35f, roof);
            Box(k, new Vector3(1.9f, 6.2f, 1.9f), new Vector3(w / 2f + 0.6f, 3.1f, 0), wall);
            Cyl(k, 0f, 1.45f, 2.6f, new Vector3(w / 2f + 0.6f, 7.5f, 0), roof, 4, new Vector3(0, 45f, 0));
            Box(k, new Vector3(0.1f, 1.2f, 0.1f), new Vector3(w / 2f + 0.6f, 9.3f, 0), iron);
            Box(k, new Vector3(0.1f, 0.1f, 0.6f), new Vector3(w / 2f + 0.6f, 9.5f, 0), iron);
            Box(k, new Vector3(0.5f, 0.9f, 1.95f), new Vector3(w / 2f + 0.6f, 5.3f, 0), C(0.08f, 0.07f, 0.06f));
            Cyl(k, 0.2f, 0.32f, 0.45f, new Vector3(w / 2f + 0.6f, 5.2f, 0), C(0.55f, 0.42f, 0.18f), 8);
            Box(k, new Vector3(0.1f, 2.0f, 1.0f), new Vector3(w / 2f + 1.56f, 1.0f, 0), C(0.45f, 0.3f, 0.2f));
            var glass = C(0.25f, 0.3f, 0.32f);
            foreach (float sx in new[] { -1.6f, 1.0f })
                foreach (float sz in new[] { -1f, 1f })
                    Box(k, new Vector3(0.7f, 1.3f, 0.1f), new Vector3(sx, 2.1f, sz * (d / 2f + 0.02f)), glass);
        }

        private static void StiltHouse(MeshKit k, float depth)
        {
            var timber = C(0.4f, 0.3f, 0.2f);
            var planks = C(0.62f, 0.48f, 0.34f);
            float postLen = depth + 2.2f;
            foreach (float sx in new[] { -2.2f, 0f, 2.2f })
                foreach (float sz in new[] { -1.9f, 1.9f })
                    Cyl(k, 0.13f, 0.16f, postLen, new Vector3(sx, 1.6f - postLen / 2f, sz), timber, 6);
            Box(k, new Vector3(5.6f, 0.2f, 4.6f), new Vector3(0, 1.6f, 0), planks);
            Box(k, new Vector3(3.6f, 2.3f, 3.0f), new Vector3(-0.6f, 2.85f, -0.3f), planks);
            Roof(k, new Vector3(-0.6f, 0, -0.3f), 3.6f, 3.0f, 4.0f, 1.9f, 0.4f, C(0.5f, 0.4f, 0.2f));
            Box(k, new Vector3(0.9f, 1.8f, 0.08f), new Vector3(-0.2f, 2.6f, 1.23f), C(0.12f, 0.09f, 0.07f));
            Box(k, new Vector3(0.7f, 0.6f, 0.08f), new Vector3(-1.6f, 3.1f, 1.23f), C(0.25f, 0.3f, 0.32f));
            Box(k, new Vector3(5.6f, 0.08f, 0.08f), new Vector3(0, 2.5f, 2.25f), timber);
            Box(k, new Vector3(0.08f, 0.08f, 4.6f), new Vector3(2.75f, 2.5f, 0), timber);
            foreach (float sx in new[] { 2.75f, 0.9f })
                Box(k, new Vector3(0.1f, 0.9f, 0.1f), new Vector3(sx, 2.05f, 2.25f), timber);
            foreach (float sx in new[] { -0.3f, 0.3f })
                Box(k, new Vector3(0.08f, 2.4f, 0.08f), new Vector3(1.4f + sx, 0.5f, 2.45f), timber, new Vector3(-12f, 0, 0));
            for (int i = 0; i < 5; i++)
                Box(k, new Vector3(0.6f, 0.06f, 0.06f), new Vector3(1.4f, -0.4f + i * 0.45f, 2.45f - (i - 2) * 0.1f), timber);
            Box(k, new Vector3(0.06f, 1.9f, 1.6f), new Vector3(2.5f, 2.6f, -0.9f), C(0.25f, 0.22f, 0.16f));
            Box(k, new Vector3(3.2f, 0.35f, 0.9f), new Vector3(2.8f, 0.1f, 3.4f), C(0.35f, 0.3f, 0.22f), new Vector3(0, 15f, 0));
        }

        private static void Dike(MeshKit k, float length, List<Vector3> trees)
        {
            var earth = C(0.36f, 0.5f, 0.2f);
            var path = C(0.55f, 0.45f, 0.3f);
            var concrete = C(0.7f, 0.68f, 0.62f);
            var wood = C(0.48f, 0.34f, 0.24f);
            Box(k, new Vector3(length, 1.4f, 3.2f), new Vector3(0, 0.3f, 0), earth);
            foreach (float sz in new[] { -1f, 1f })
                Box(k, new Vector3(length, 0.5f, 2.6f), new Vector3(0, 0.25f, sz * 2.4f), earth, new Vector3(sz * 18f, 0, 0));
            Box(k, new Vector3(length, 0.06f, 1.6f), new Vector3(0, 1.02f, 0), path);
            Box(k, new Vector3(1.6f, 3.6f, 1.6f), new Vector3(0, 0.4f, 4.2f), concrete);
            Box(k, new Vector3(1.2f, 2.6f, 0.12f), new Vector3(0, 0.6f, 5.02f), wood);
            for (int i = 0; i < 5; i++)
                Box(k, new Vector3(1.25f, 0.05f, 0.14f), new Vector3(0, -0.5f + i * 0.5f, 5.06f), C(0.2f, 0.15f, 0.1f));
            Box(k, new Vector3(0.9f, 0.1f, 2.6f), new Vector3(0, 1.15f, 2.4f), wood);
            Cyl(k, 0.35f, 0.35f, 0.08f, new Vector3(0, 2.35f, 4.2f), C(0.2f, 0.2f, 0.22f), 10);
            int posts = (int)(length / 6f);
            for (int i = 0; i <= posts; i++)
                Box(k, new Vector3(0.14f, 0.9f, 0.14f), new Vector3(-length / 2f + i * length / posts, 1.45f, 1.4f), wood);
            Box(k, new Vector3(length, 0.04f, 0.04f), new Vector3(0, 1.75f, 1.4f), C(0.75f, 0.68f, 0.5f));
            int n = (int)(length / 8f);
            for (int i = 0; i <= n; i++)
            {
                float x = -length / 2f + i * length / n;
                if (MathF.Abs(x) >= 4f)
                    trees.Add(new Vector3(x, 1.05f, -1.05f));
            }
        }

        private static void Platform(MeshKit k, float depth)
        {
            var wood = C(0.66f, 0.5f, 0.34f);
            Box(k, new Vector3(2.2f, 0.12f, 2.0f), new Vector3(0, 0.45f, 1.0f), wood);
            foreach (float sx in new[] { -0.95f, 0.95f })
                foreach (float sz in new[] { 0.15f, 1.85f })
                    Cyl(k, 0.08f, 0.08f, depth + 1.2f, new Vector3(sx, 0.45f - (depth + 1.2f) / 2f, sz), wood, 6);
            Box(k, new Vector3(0.5f, 0.06f, 0.3f), new Vector3(0.7f, 0.85f, 1.7f), wood);
        }

        private static void Waterfall(MeshKit k, MeshKit water)
        {
            var rock = C(0.74f, 0.72f, 0.7f);
            var darkRock = C(0.58f, 0.57f, 0.6f);
            var moss = C(0.3f, 0.45f, 0.18f);
            var rng = new System.Random(5);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int i = -5; i <= 5; i++)
            {
                float x = i * 3.4f + R(-0.6f, 0.6f);
                float h = MathF.Max(17.5f - MathF.Abs(i) * 3f + R(-1.5f, 1.5f), 3f);
                if (i == 0) h = 16.6f;
                float depth = R(6f, 9f);
                float back = -depth / 2f - (i == 0 ? 0.8f : R(0f, 2f));
                var rot = new Vector3(R(-4f, 4f), R(-12f, 12f), R(-5f, 5f));
                Box(k, new Vector3(4.2f, h, depth), new Vector3(x, h / 2f - 1f, back), (i & 1) == 0 ? rock : darkRock, rot);
                if (i != 0)
                    Box(k, new Vector3(3.8f, 0.5f, depth * 0.8f), new Vector3(x, h - 0.9f, back), moss, rot);
                if (MathF.Abs(i) is >= 1 and <= 3)
                    Box(k, new Vector3(3.2f, 2.2f, 1.6f), new Vector3(x, h * R(0.35f, 0.6f), back + depth / 2f + 0.5f), (i & 1) == 0 ? darkRock : rock, rot);
            }
            // the falling sheet, its lip and the foam in the plunge pool
            var sheet = C(0.72f, 0.84f, 0.9f);
            Box(water, new Vector3(3.2f, 16.5f, 0.05f), new Vector3(0, 8.2f, 0.4f), sheet);
            Box(water, new Vector3(3.4f, 0.4f, 1.6f), new Vector3(0, 16.4f, -0.3f), C(0.55f, 0.75f, 0.86f));
            var foam = C(0.93f, 0.96f, 1f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.1f;
                water.Blob(new Vector3(MathF.Sin(a) * 1.6f, 0.05f, 1.2f + MathF.Cos(a) * 0.8f), new Vector3(0.9f, 0.3f, 0.9f), 40 + i, 0.3f, foam, foam, true, 1);
            }
            foreach (var (pos, s) in new[] { (new Vector3(-3.2f, 0.2f, 2.2f), 1.2f), (new Vector3(3.5f, 0.1f, 1.6f), 1.5f), (new Vector3(-1.4f, 0f, 3.6f), 0.8f) })
                Box(k, new Vector3(s * 1.6f, s, s * 1.3f), pos, darkRock, new Vector3(10f, pos.x * 20f, 6f));
        }

        private static void Rocks(MeshKit k)
        {
            var rock = C(0.52f, 0.52f, 0.55f);
            (Vector3 Size, Vector3 Pos, Vector3 Rot)[] stones =
            {
                (new(4.5f, 3.2f, 3.8f), new(0, 0.6f, 0), new(8f, 20f, -6f)),
                (new(3f, 2.4f, 2.6f), new(2.6f, 0.2f, 1.4f), new(-10f, 50f, 12f)),
                (new(2.4f, 4.2f, 2.2f), new(-1.8f, 1.2f, -1.2f), new(4f, -30f, 10f)),
                (new(1.8f, 1.2f, 1.6f), new(-3.2f, -0.2f, 2.0f), new(14f, 10f, 0)),
            };
            foreach (var (size, pos, rot) in stones)
                Box(k, size, pos, rock, rot);
            Box(k, new Vector3(2.2f, 0.3f, 1.8f), new Vector3(-0.2f, 2.3f, 0.2f), C(0.3f, 0.4f, 0.2f), new Vector3(8f, 20f, -6f));
            var deadWood = C(0.55f, 0.48f, 0.4f);
            Cyl(k, 0.08f, 0.18f, 3.2f, new Vector3(-1.8f, 4.6f, -1.2f), deadWood, 6, new Vector3(0, 0, -14f));
            Box(k, new Vector3(1.4f, 0.08f, 0.08f), new Vector3(-1.4f, 5.4f, -1.2f), deadWood, new Vector3(0, 0, 20f));
        }
    }
}
