using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Mathf = UnityEngine.Mathf;
using Random = System.Random;

namespace FishingSim.Game
{
    /// <summary>
    /// The map's plants and stones as Scenery places them in the Godot game: the woods inside the map and the dense rim
    /// forest (five kinds, near and far versions per 64 m cell), bushes, hazel round the trees, boulders by the shore and
    /// on the mountains, and the photographed meadow clumps following the angler (GrassField). Everything is drawn
    /// instanced from prebuilt matrix arrays (no per-frame allocation); placement runs on a worker thread.
    /// </summary>
    public sealed class Forest : MonoBehaviour
    {
        private const float Cell = 64f, TreeNearRange = 130f;
        private static readonly string[] Kinds = { "spruce", "oak", "birch", "pine", "willow" };

        /// <summary>The small plants: mesh, drawn up to (m, x FoliageRange), floating on the water.</summary>
        private static readonly (string Kind, float Range, bool Floating)[] SmallKinds =
        {
            ("reed", 260f, false), ("cattail", 200f, false), ("rush", 140f, false), ("iris", 120f, false), ("irisflower", 120f, false),
            ("flower", 80f, false), ("tuft", 90f, false), ("fern", 90f, false),
            ("lilypad", 160f, true), ("pondweed", 120f, true), ("lilyflower", 160f, true), ("yellowlily", 160f, true),
        };
        private const int Reed = 15, Cattail = 16, Rush = 17, Iris = 18, IrisFlower = 19, Flower = 20, Tuft = 21, Fern = 22,
            LilyPad = 23, Pondweed = 24, LilyFlower = 25, YellowLily = 26;

        private sealed class Batch
        {
            public Mesh Near = null!;
            public Mesh? Far;
            public Material Mat = null!;
            public Bounds Box;
            public float Range, MaxRange;
            public bool Shadows;
            public readonly List<Matrix4x4[]> Chunks = new();
            public readonly List<int> Counts = new();
            public readonly List<MaterialPropertyBlock> Props = new();
        }

        private readonly List<Batch> _batches = new();
        private readonly Plane[] _planes = new Plane[6];
        private Lake _lake = null!;
        private Material? _foliage, _needles, _rock, _grassMat;
        private readonly Dictionary<string, Material> _bark = new();

        // the meadow field
        private Batch? _grass;
        private Task<(List<Matrix4x4> M, List<Vector4> C)>? _grassJob;
        private Vector2 _grassCentre = new(1e9f, 1e9f);
        private Mesh? _cards;

        public bool Ready { get; private set; }

        /// <summary>The lily and pondweed patches (x, z, radius): cover for the fish.</summary>
        public static List<(float X, float Z, float Radius)> WeedBeds { get; } = new();

        public static float FoliageRange => Settings.Quality switch { 0 => 0.45f, 1 => 0.65f, 2 => 0.85f, _ => 1f };
        public static float GrassRadius => Settings.Quality switch { 0 => 0f, 1 => 18f, 2 => 28f, _ => 40f } * (Settings.View switch { 0 => 0.75f, 1 => 1f, _ => 1.2f });
        public static float ViewFactor => Settings.View switch { 0 => 0.6f, 1 => 0.85f, _ => 1.1f };

        // ---------------------------------------------------------------- building
        private readonly struct Item
        {
            public readonly int Group;
            public readonly Matrix4x4 M;
            public readonly Vector4 Tint;
            public Item(int group, Matrix4x4 m, Vector4 tint) { Group = group; M = m; Tint = tint; }
        }

        public System.Collections.IEnumerator Build(Lake lake, List<(Vector3 At, float Radius)> solids)
        {
            Ready = false;
            Clear();
            _lake = lake;
            foreach (var kind in Kinds)
                Plants.Get(kind);   // meshes are made on the main thread; the worker only reads their lifts
            WeedBeds.Clear();
            foreach (var (kind, _, _) in SmallKinds)
                Plants.SmallPlant(kind);
            var job = Task.Run(() => Place(lake));
            while (!job.IsCompleted)
                yield return null;
            if (job.Exception != null)
                throw job.Exception;
            var (items, trunkSolids) = job.Result;
            solids.AddRange(trunkSolids);

            // groups: 0..4 trunks, 5..9 crowns, 10 bush, 11 hazel, 12..14 boulders
            var groups = new List<Item>[15 + SmallKinds.Length];
            for (int g = 0; g < groups.Length; g++)
                groups[g] = new List<Item>();
            foreach (var it in items)
                groups[it.Group].Add(it);
            for (int k = 0; k < Kinds.Length; k++)
            {
                var near = Plants.Get(Kinds[k]);
                var far = Plants.GetFar(Kinds[k]);
                float range = TreeNearRange * FoliageRange * ViewFactor;
                if (near.Trunk != null)
                    AddCells(groups[k], near.Trunk, far.Trunk, BarkMaterial(Kinds[k]), range, float.MaxValue, true);
                AddCells(groups[5 + k], near.Crown, far.Crown, near.Needles ? NeedleMaterial() : LeafMaterial(), range, float.MaxValue, true);
                yield return null;
            }
            AddCells(groups[10], Plants.Get("bush").Crown, null, LeafMaterial(), 220f * FoliageRange, 0f, true);
            AddCells(groups[11], Plants.Get("hazel").Crown, null, LeafMaterial(), 220f * FoliageRange, 0f, true);
            for (int v = 0; v < 3; v++)
                AddCells(groups[12 + v], Plants.Boulder(v), null, RockMaterial(), 900f, 0f, true);
            for (int k = 0; k < SmallKinds.Length; k++)
            {
                var (kind, range, floating) = SmallKinds[k];
                AddCells(groups[15 + k], Plants.SmallPlant(kind), null, PlantMaterial(floating), range * FoliageRange, 0f, false, 48f);
            }
            Ready = true;
        }

        private void Clear()
        {
            _batches.Clear();
            _grass = null;
            _grassCentre = new Vector2(1e9f, 1e9f);
        }

        private void AddCells(List<Item> items, Mesh near, Mesh? far, Material mat, float range, float maxRange, bool shadows, float cellSize = Cell)
        {
            float Cell = cellSize;
            var cells = new Dictionary<(int, int), List<Item>>();
            foreach (var it in items)
            {
                var p = it.M.GetColumn(3);
                var key = (Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
                if (!cells.TryGetValue(key, out var list))
                    cells[key] = list = new List<Item>();
                list.Add(it);
            }
            foreach (var list in cells.Values)
            {
                var b = new Batch { Near = near, Far = far, Mat = mat, Range = range, MaxRange = far == null ? range : maxRange, Shadows = shadows };
                Fill(b, list);
                _batches.Add(b);
            }
        }

        private static void Fill(Batch b, List<Item> list)
        {
            bool first = true;
            float reach = Mathf.Max(b.Near.bounds.extents.magnitude, b.Far != null ? b.Far.bounds.extents.magnitude : 0f);
            for (int start = 0; start < list.Count; start += 1023)
            {
                int n = Mathf.Min(1023, list.Count - start);
                var m = new Matrix4x4[n];
                var tints = new Vector4[n];
                for (int i = 0; i < n; i++)
                {
                    var it = list[start + i];
                    m[i] = it.M;
                    tints[i] = it.Tint;
                    var p = (Vector3)it.M.GetColumn(3);
                    float s = it.M.lossyScale.magnitude * 0.6f * reach;
                    var bb = new Bounds(p, Vector3.one * s * 2f);
                    if (first) { b.Box = bb; first = false; } else b.Box.Encapsulate(bb);
                }
                var props = new MaterialPropertyBlock();
                props.SetVectorArray("_Tint", tints);
                b.Chunks.Add(m);
                b.Counts.Add(n);
                b.Props.Add(props);
            }
        }

        private static Vector4 Tint(Color c, float t) => new(c.r + t, c.g + t, c.b + t, 1f);

        private static Color CrownColor(string kind) => kind switch
        {
            "spruce" => new Color(0.17f, 0.3f, 0.2f),
            "oak" => new Color(0.22f, 0.35f, 0.12f),
            "birch" => new Color(0.36f, 0.5f, 0.2f),
            "pine" => new Color(0.2f, 0.33f, 0.19f),
            "willow" => new Color(0.33f, 0.44f, 0.2f),
            _ => new Color(0.19f, 0.34f, 0.13f),
        };

        private static float TrunkRadius(string kind) => kind switch
        {
            "spruce" => 0.17f, "oak" => 0.2f, "birch" => 0.1f, "pine" => 0.18f, "willow" => 0.24f, _ => 0.15f,
        };

        private static float R(Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// <summary>Ground kept free by the landmarks (x, z, radius), and trees they ask for (Building.TreeSpots).</summary>
        public static List<(float X, float Z, float R)> Reserved { get; } = new();
        public static List<Vector3> LandmarkTrees { get; } = new();

        private static bool Clear(Lake lake, float x, float z, float pad = 3f, float path = 1.8f)
        {
            if (Lake.OnPier(x, z) || lake.Built(x, z, pad) || lake.PathDistance(x, z) <= path)
                return false;
            foreach (var (rx, rz, r) in Reserved)
                if ((x - rx) * (x - rx) + (z - rz) * (z - rz) < r * r)
                    return false;
            return true;
        }

        /// <summary>Scenery.ScatterTrees, ScatterGroundCover (bushes, hazel) and ScatterRocks.</summary>
        private static (List<Item>, List<(Vector3, float)>) Place(Lake lake)
        {
            var loc = lake.Location;
            var rng = new Random(lake.Seed * 7 + 11);
            var items = new List<Item>(20000);
            var solids = new List<(Vector3, float)>();
            var spots = new List<(float X, float Z, string Kind)>();

            void Plant(int k, float x, float z, float h, float s, float yaw, float tint, bool understorey)
            {
                string kind = Kinds[k];
                var meshes = Plants.Get(kind);
                var rot = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
                var pos = new Vector3(x, h - 0.1f, z);
                var scale = Vector3.one * s;
                if (meshes.Trunk != null)
                {
                    var bark = Col.Lin(meshes.TrunkColor + new Color(tint, tint, tint) * 0.5f);
                    items.Add(new Item(k, Matrix4x4.TRS(pos + Vector3.up * meshes.TrunkLift * s, rot, scale), Tint(bark, 0f)));
                }
                items.Add(new Item(5 + k, Matrix4x4.TRS(pos + Vector3.up * meshes.CrownLift * s, rot, scale), Tint(CrownColor(kind), tint)));
                if (understorey)
                    spots.Add((x, z, kind));
                if (lake.PlayDistance(x, z) < 2f)
                    solids.Add((new Vector3(x, h, z), TrunkRadius(kind) * s * 1.1f));
            }

            int Choose(float[] w, float pick)
            {
                float total = 0f;
                foreach (var v in w) total += v;
                float roll = pick * total;
                for (int k = 0; k < w.Length; k++)
                {
                    roll -= w[k];
                    if (roll <= 0f)
                        return k;
                }
                return 0;
            }

            // the woods inside the map
            float threshold = -0.08f + (1f - loc.TreeDensity) * 0.5f;
            int cap = (int)(1550 * Math.Min(loc.TreeDensity, 1.2f)), placed = 0;
            var weights = new float[Kinds.Length];
            for (int i = 0; i < 20000 && placed < cap; i++)
            {
                float x = R(rng, Lake.XMin, Lake.XMax), z = R(rng, Lake.ZMin, Lake.ZMax);
                float s = R(rng, 0.7f, 1.7f), tint = R(rng, -0.05f, 0.05f), pick = (float)rng.NextDouble(), yaw = (float)rng.NextDouble() * Mathf.PI * 2f;
                if (lake.ForestDensity(x, z) < threshold || lake.PlayDistance(x, z) > -34f)
                    continue;
                float h = lake.HeightAt(x, z);
                if (h < 1.2f || !Clear(lake, x, z))
                    continue;
                float inside = lake.InsideMetres(x, z);
                float sum = 0f;
                for (int k = 0; k < Kinds.Length; k++)
                {
                    float w = loc.TreeShare(Kinds[k]);
                    w *= Kinds[k] switch
                    {
                        "willow" => inside > -16f && inside < -1.5f ? 3f : 0f,
                        "pine" => h > 3f ? 1.6f : 0.5f,
                        "oak" => h < 8f ? 1f : 0.3f,
                        "spruce" => h > 5f ? 1.4f : 1f,
                        _ => 1f,
                    };
                    weights[k] = w;
                    sum += w;
                }
                if (sum <= 0f)
                    continue;
                Plant(Choose(weights, pick), x, z, h, s, yaw, tint, true);
                placed++;
            }

            // trees the landmarks ask for (the dike's row): tall birches and oaks
            foreach (var foot in LandmarkTrees)
            {
                float pick = (float)rng.NextDouble();
                Plant(pick < 0.65f ? 2 : 1, foot.x, foot.z, foot.y + 0.1f, R(rng, 1.15f, 1.45f), (float)rng.NextDouble() * Mathf.PI * 2f, R(rng, -0.1f, 0.1f), false);
            }

            // the rim forest: dense at the edge, thinning up the slopes to the tree line
            float treeLine = MathF.Min(loc.SnowLine - 3f, loc.RimHeight * 0.75f);
            var rimWeights = new float[Kinds.Length];
            for (int k = 0; k < Kinds.Length; k++)
                rimWeights[k] = Kinds[k] switch
                {
                    "spruce" => loc.TreeShare("spruce") + 0.35f,
                    "pine" => loc.TreeShare("pine") + 0.2f,
                    "birch" => loc.TreeShare("birch") + 0.1f,
                    "oak" => loc.TreeShare("oak") * 0.6f + 0.04f,
                    _ => 0f,
                };
            const float cell = 3.4f;
            for (float gz = Lake.ZMin - 140f; gz <= Lake.ZMax + 140f; gz += cell)
                for (float gx = Lake.XMin - 140f; gx <= Lake.XMax + 140f; gx += cell)
                {
                    float x = gx + (float)rng.NextDouble() * cell, z = gz + (float)rng.NextDouble() * cell;
                    float s = R(rng, 0.9f, 2f), yaw = (float)rng.NextDouble() * Mathf.PI * 2f, tint = R(rng, -0.06f, 0.05f), pick = (float)rng.NextDouble();
                    float keep = (float)rng.NextDouble();
                    float d = lake.PlayDistance(x, z);
                    if (d < -34f || d > 140f || (d > 2f && keep > 0.3f) || (d < -20f && keep > 0.6f))
                        continue;
                    float h = lake.HeightAt(x, z);
                    if (h < 1.2f || h > treeLine - keep * 8f || lake.InsideMetres(x, z) > -3f || (d < 0f && !Clear(lake, x, z)))
                        continue;
                    float slope = MathF.Abs(lake.HeightAt(x + 1.5f, z) - h) + MathF.Abs(lake.HeightAt(x, z + 1.5f) - h);
                    if (slope > 1.4f)
                        continue;
                    Plant(Choose(rimWeights, pick), x, z, h, s, yaw, tint, d < 0f && keep < 0.25f);
                }

            // bushes over the map, thick at its forest edge
            int maxBushes = (int)(900 * loc.BushDensity), bushes = 0;
            float highest = MathF.Min(loc.SnowLine - 2f, 40f);
            for (int i = 0; i < 30000 && bushes < maxBushes; i++)
            {
                float x = R(rng, Lake.XMin, Lake.XMax), z = R(rng, Lake.ZMin, Lake.ZMax);
                float roll = (float)rng.NextDouble(), s = R(rng, 0.7f, 1.4f), yaw = (float)rng.NextDouble() * 360f;
                float edge = lake.PlayDistance(x, z);
                if (edge > 3f)
                    continue;
                if (edge > -30f)
                    roll *= 0.4f;
                if (roll >= 0.12f)
                    continue;
                float h = lake.HeightAt(x, z);
                if (h < 1.0f || h > highest || !Clear(lake, x, z, 1.5f, 1.6f))
                    continue;
                items.Add(new Item(10, Matrix4x4.TRS(new Vector3(x, h, z), Quaternion.Euler(0, yaw, 0), Vector3.one * s), Tint(CrownColor("bush"), R(rng, -0.05f, 0.05f))));
                bushes++;
            }
            // hazel in the shade of the trees
            foreach (var (tx, tz, kind) in spots)
            {
                if (kind == "willow" || rng.NextDouble() >= 0.35f * loc.BushDensity)
                    continue;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, dd = R(rng, 2.2f, 4.5f);
                float x = tx + MathF.Cos(a) * dd, z = tz + MathF.Sin(a) * dd;
                float h = lake.HeightAt(x, z);
                if (h <= 1.1f || !Clear(lake, x, z, 1.5f, 1.6f))
                    continue;
                float sc = R(rng, 0.8f, 1.5f);
                items.Add(new Item(11, Matrix4x4.TRS(new Vector3(x, h + 0.7f * sc - 0.15f, z), Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0),
                    new Vector3(sc, sc * R(rng, 0.9f, 1.3f), sc)), Tint(new Color(0.26f, 0.42f, 0.13f), R(rng, -0.05f, 0.05f))));
            }

            // ---- the small plants (Scenery.ScatterGroundCover's flowers and tufts, ferns round the trees, ScatterReeds,
            // ScatterShoreline, ScatterWaterPlants); colours are the Godot ones, made linear for FS/Plant
            Vector4 Lin(Color c) { var l = Col.Lin(c); return new Vector4(l.r, l.g, l.b, 1f); }
            Vector4 Tn(Color c, float t) => Lin(new Color(c.r + t, c.g + t, c.b + t));
            Matrix4x4 Yaw(Vector3 at, Vector3 scale) => Matrix4x4.TRS(at, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), scale);
            Color[] palette = { new(0.97f, 0.97f, 0.92f), new(0.98f, 0.85f, 0.2f), new(0.92f, 0.4f, 0.6f), new(0.6f, 0.4f, 0.85f), new(0.9f, 0.25f, 0.2f) };
            int maxFlowers = (int)(3400 * loc.FlowerDensity), maxTufts = (int)(7000 * Math.Max(loc.BushDensity, 0.7f)), flowers = 0, tufts = 0;
            for (int i = 0; i < 90000 && (flowers < maxFlowers || tufts < maxTufts); i++)
            {
                float x = R(rng, Lake.XMin, Lake.XMax), z = R(rng, Lake.ZMin, Lake.ZMax);
                float roll = (float)rng.NextDouble(), s = R(rng, 0.7f, 1.4f);
                float edge = lake.PlayDistance(x, z);
                if (edge > 3f)
                    continue;
                if (edge > -30f)
                    roll *= 0.4f;
                if (roll < 0.12f)
                    continue;   // the bushes' share (placed above)
                float h = lake.HeightAt(x, z);
                if (h < 1.0f || h > highest || !Clear(lake, x, z, 1.5f, 1.6f))
                    continue;
                float patch = lake.NoiseAt(x * 3f + 40f, z * 3f - 70f);
                if (roll < 0.4f && patch > 0f && flowers < maxFlowers)
                {
                    items.Add(new Item(Flower, Yaw(new Vector3(x, h - 0.03f, z), new Vector3(s, s * R(rng, 0.7f, 1.3f), s)), Lin(palette[rng.Next(palette.Length)])));
                    flowers++;
                }
                else if (tufts < maxTufts && patch > -0.35f)
                {
                    var g = new Color(loc.GrassColor.R, loc.GrassColor.G, loc.GrassColor.B);
                    items.Add(new Item(Tuft, Yaw(new Vector3(x, h - 0.05f, z), new Vector3(s, s * 1.2f, s)), Tn(Color.Lerp(g, new Color(0.5f, 0.65f, 0.25f), 0.35f), R(rng, -0.06f, 0.06f))));
                    tufts++;
                }
            }
            foreach (var (tx, tz, kind) in spots)
            {
                if (kind == "willow")
                    continue;
                int nf = kind == "pine" ? 1 : rng.Next(1, 5);
                for (int f = 0; f < nf; f++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, dd = R(rng, 0.8f, 3.5f);
                    float x = tx + MathF.Cos(a) * dd, z = tz + MathF.Sin(a) * dd;
                    float h = lake.HeightAt(x, z);
                    if (h < 1.1f || !Clear(lake, x, z, 1.5f, 1.6f))
                        continue;
                    float sc = R(rng, 0.7f, 1.3f);
                    items.Add(new Item(Fern, Yaw(new Vector3(x, h - 0.03f, z), Vector3.one * sc), Tn(new Color(0.28f, 0.48f, 0.15f), R(rng, -0.06f, 0.04f))));
                }
            }

            // shorelines: the lake, its bays, its islands
            var shores = new List<(float X, float Z, float Rx, float Rz, bool Island)> { (0f, loc.OutlineCz, loc.OutlineRx, loc.OutlineRz, false) };
            foreach (var entry in loc.Lobes)
                if (Data.LocationData.Numbers(entry) is { Length: >= 4 } l)
                    shores.Add((l[0], l[1], l[2], l[3], false));
            foreach (var entry in loc.Islands)
                if (Data.LocationData.Numbers(entry) is { Length: >= 3 } il)
                    shores.Add((il[0], il[1], il[2], il[2], true));
            (float X, float Z, float Rx, float Rz, bool Island) Shore(float main) =>
                shores.Count == 1 || rng.NextDouble() < main ? shores[0] : shores[rng.Next(1, shores.Count)];
            static bool NearPier(float x, float z) => MathF.Abs(x) < 12f && z > -18f;

            int maxReeds = (int)(4200 * Math.Min(loc.ReedDensity, 2f)), reeds = 0;
            for (int tries = 0; reeds < maxReeds && tries < 90000; tries++)
            {
                var sh = Shore(0.5f);
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float shrink = sh.Island ? R(rng, 1f, 1.6f) : R(rng, 0.85f, 1.04f);
                float x = sh.X + sh.Rx * shrink * MathF.Cos(angle), z = sh.Z + sh.Rz * shrink * MathF.Sin(angle);
                if (MathF.Abs(x) < 9f && z > -14f)
                    continue;
                float d = lake.DepthAt(x, z);
                if (d < 0.15f || d > 0.9f || lake.ForestDensity(x * 1.7f, z * 1.7f) < 0f)
                    continue;
                for (int k = 0; k < 5; k++)
                {
                    float rx = x + R(rng, -0.8f, 0.8f), rz = z + R(rng, -0.8f, 0.8f);
                    float hh = R(rng, 0.9f, 1.5f) / 1.42f;
                    var rot = Quaternion.Euler(R(rng, -7f, 7f), (float)rng.NextDouble() * 360f, R(rng, -7f, 7f));
                    var origin = new Vector3(rx, -lake.DepthAt(rx, rz), rz);
                    items.Add(new Item(Reed, Matrix4x4.TRS(origin, rot, new Vector3(1f, hh, 1f)), Tn(new Color(0.42f, 0.5f, 0.2f), R(rng, -0.05f, 0.05f))));
                    if (rng.NextDouble() < 0.35f)
                        items.Add(new Item(Cattail, Matrix4x4.TRS(origin + rot * (Vector3.up * 1.3f * hh), rot, Vector3.one), Lin(new Color(0.3f, 0.17f, 0.08f))));
                    reeds++;
                }
            }

            int maxRushes = (int)(1400 * Math.Clamp(loc.ReedDensity, 0.3f, 2f)), maxIrises = (int)(320 * Math.Max(loc.ReedDensity, 0.2f)), rushes = 0, irises = 0;
            for (int tries = 0; (rushes < maxRushes || irises < maxIrises) && tries < 60000; tries++)
            {
                var sh = Shore(0.6f);
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float shrink = sh.Island ? R(rng, 0.95f, 1.25f) : R(rng, 0.93f, 1.08f);
                float x = sh.X + sh.Rx * shrink * MathF.Cos(angle), z = sh.Z + sh.Rz * shrink * MathF.Sin(angle);
                if (NearPier(x, z))
                    continue;
                float inside = lake.InsideMetres(x, z), depth = lake.DepthAt(x, z);
                if (inside > 0f && depth > 0f && depth < 0.35f && rushes < maxRushes)
                {
                    float sc = R(rng, 0.7f, 1.3f);
                    items.Add(new Item(Rush, Yaw(new Vector3(x, -depth, z), new Vector3(sc, sc * R(rng, 0.9f, 1.4f), sc)), Tn(new Color(0.22f, 0.34f, 0.13f), R(rng, -0.04f, 0.04f))));
                    rushes++;
                }
                else if (inside > -2.5f && inside < 0.4f && irises < maxIrises && lake.NoiseAt(x * 2f - 30f, z * 2f + 11f) > 0.1f && Clear(lake, x, z, 1.5f, 1.6f))
                {
                    float h = MathF.Max(lake.HeightAt(x, z), -depth), sc = R(rng, 0.8f, 1.2f);
                    items.Add(new Item(Iris, Yaw(new Vector3(x, h - 0.02f, z), Vector3.one * sc), Tn(new Color(0.3f, 0.45f, 0.18f), R(rng, -0.04f, 0.04f))));
                    if (rng.NextDouble() < 0.7f)
                        items.Add(new Item(IrisFlower, Yaw(new Vector3(x + 0.05f, h - 0.02f, z), Vector3.one * sc), Lin(new Color(0.98f, 0.84f, 0.15f))));
                    irises++;
                }
            }

            int patches = (int)MathF.Round(30 * loc.LilyDensity), weedPatches = (int)MathF.Round(22 * loc.LilyDensity), made = 0, madeWeeds = 0;
            for (int tries = 0; (made < patches || madeWeeds < weedPatches) && tries < 20000; tries++)
            {
                bool weed = made >= patches || (madeWeeds < weedPatches && rng.NextDouble() < 0.4f);
                var sh = Shore(0.45f);
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float shrink = sh.Island ? R(rng, 1.1f, 1.8f) : R(rng, 0.72f, 0.97f);
                float cx = sh.X + sh.Rx * shrink * MathF.Cos(angle), cz = sh.Z + sh.Rz * shrink * MathF.Sin(angle);
                if (NearPier(cx, cz))
                    continue;
                float depth = lake.DepthAt(cx, cz);
                if (weed ? depth < 0.9f || depth > 3.2f : depth < 0.4f || depth > 2.2f)
                    continue;
                if (weed) madeWeeds++; else made++;
                float radius = R(rng, 1.6f, weed ? 3.5f : 5f);
                WeedBeds.Add((cx, cz, radius));
                int count = (int)(radius * radius * R(rng, weed ? 0.9f : 2.2f, weed ? 1.6f : 3.6f));
                bool yellowPatch = !weed && rng.NextDouble() < 0.3f;
                for (int k = 0; k < count; k++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = radius * MathF.Sqrt((float)rng.NextDouble());
                    float x = cx + MathF.Cos(a) * d, z = cz + MathF.Sin(a) * d;
                    float dd = lake.DepthAt(x, z);
                    if (dd < 0.25f || dd > 3.4f || NearPier(x, z))
                        continue;
                    if (weed)
                    {
                        float sc = R(rng, 0.8f, 1.3f);
                        items.Add(new Item(Pondweed, Yaw(new Vector3(x, 0.002f, z), new Vector3(sc, 1f, sc)), Tn(Color.white, R(rng, -0.12f, 0.02f))));
                        continue;
                    }
                    float r = R(rng, 0.15f, yellowPatch ? 0.26f : 0.34f);
                    items.Add(new Item(LilyPad, Yaw(new Vector3(x, 0.004f * (k % 4), z), new Vector3(r, 1f, r)), Tn(Color.white, R(rng, -0.2f, 0f))));
                    if (rng.NextDouble() < (yellowPatch ? 0.12f : 0.14f))
                    {
                        var at = new Vector3(x + R(rng, -0.05f, 0.05f), 0.018f, z + R(rng, -0.05f, 0.05f));
                        var sc = Vector3.one * R(rng, 0.85f, 1.25f);
                        if (yellowPatch)
                            items.Add(new Item(YellowLily, Yaw(at + Vector3.up * 0.03f, sc), Vector4.one));
                        else
                            items.Add(new Item(LilyFlower, Yaw(at, sc), rng.NextDouble() < 0.25f ? Lin(new Color(1f, 0.82f, 0.88f)) : Vector4.one));
                    }
                }
            }

            // boulders by the shore and on the hills
            int maxRocks = (int)(160 * loc.RockDensity), rocks = 0;
            float size = loc.RockDensity > 1.5f ? 1.7f : 1f;
            for (int tries = 0; rocks < maxRocks && tries < 30000; tries++)
            {
                float x = R(rng, Lake.XMin, Lake.XMax), z = R(rng, Lake.ZMin, Lake.ZMax);
                if (lake.PlayDistance(x, z) > 0f || (MathF.Abs(x) < 6f && z > -6f && z < 12f))
                    continue;
                float inside = lake.InsideMetres(x, z), h = lake.HeightAt(x, z);
                bool nearShore = inside > -6f && inside < 4f;
                bool hills = h > 5f && rng.NextDouble() < 0.1f * loc.RockDensity;
                if (!(nearShore || hills) || !Clear(lake, x, z, 1f, 1f))
                    continue;
                if (lake.ForestDensity(x * 2f + 7f, z * 2f) < 0.1f && nearShore)
                    continue;
                var scale = new Vector3(R(rng, 0.3f, 1.1f), R(rng, 0.2f, 0.6f), R(rng, 0.3f, 1f)) * size;
                float gr = R(rng, 0.38f, 0.55f);
                items.Add(new Item(12 + rocks % 3, Matrix4x4.TRS(new Vector3(x, h, z), Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), scale), new Vector4(gr, gr * 0.98f, gr * 0.93f, 1f)));
                solids.Add((new Vector3(x, h, z), (scale.x + scale.z) * 0.5f * 0.85f));
                rocks++;
            }
            int mountain = 0;
            for (int tries = 0; mountain < 60 + 50 * loc.RockDensity && tries < 20000; tries++)
            {
                float x = R(rng, Lake.XMin - 110f, Lake.XMax + 110f), z = R(rng, Lake.ZMin - 110f, Lake.ZMax + 110f);
                float d = lake.PlayDistance(x, z);
                if (d < -6f || d > 100f)
                    continue;
                float h = lake.HeightAt(x, z);
                if (h < 3f || h > loc.SnowLine - 3f || lake.NoiseAt(x * 2.5f + 11f, z * 2.5f) < 0.05f)
                    continue;
                float sz = R(rng, 1.2f, 3.6f) * (0.8f + 0.4f * loc.RockDensity);
                var scale = new Vector3(R(rng, 0.8f, 1.3f), R(rng, 0.45f, 0.8f), R(rng, 0.8f, 1.2f)) * sz;
                float gr = R(rng, 0.4f, 0.56f);
                items.Add(new Item(12 + (rocks + mountain) % 3, Matrix4x4.TRS(new Vector3(x, h - sz * 0.15f, z), Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), scale), new Vector4(gr, gr * 0.98f, gr * 0.94f, 1f)));
                mountain++;
            }
            return (items, solids);
        }

        // ---------------------------------------------------------------- materials
        private static Shader FindShader(string name) => Shader.Find(name) ?? Shader.Find("Standard");

        private Material LeafMaterial() => _foliage ??= new Material(FindShader("FS/Foliage")) { name = "Leaves", enableInstancing = true, mainTexture = LeafPainter.Leaves };
        private Material NeedleMaterial() => _needles ??= new Material(FindShader("FS/Foliage")) { name = "Needles", enableInstancing = true, mainTexture = LeafPainter.Needles };

        private readonly Material?[] _plant = new Material?[2];

        private Material PlantMaterial(bool floating)
        {
            int i = floating ? 1 : 0;
            if (_plant[i] != null)
                return _plant[i]!;
            var m = new Material(FindShader("FS/Plant")) { name = floating ? "Floating" : "Plant", enableInstancing = true };
            m.SetFloat("_Float", floating ? 1f : 0f);
            m.SetFloat("_Gloss", floating ? 0.45f : 0.2f);
            return _plant[i] = m;
        }

        private Material RockMaterial()
        {
            if (_rock != null)
                return _rock;
            _rock = new Material(FindShader("FS/Rock")) { name = "Rock", enableInstancing = true };
            _rock.SetTexture("_Noise", ProcTextures.NoiseField);
            if (GroundLayers.Albedo != null)
            {
                _rock.SetTexture("_GroundAlb", GroundLayers.Albedo);
                _rock.SetTexture("_GroundNrm", GroundLayers.Normal);
                _rock.SetColor("_RockMean", GroundLayers.Means[5]);
            }
            return _rock;
        }

        private Material BarkMaterial(string kind)
        {
            if (_bark.TryGetValue(kind, out var m))
                return m;
            string id = kind is "pine" or "spruce" ? "pine_bark" : "bark_brown_02";
            m = new Material(FindShader("FS/Bark")) { name = "Bark_" + kind, enableInstancing = true };
            Texture2D? diff = null, nor = null;
            if (kind == "birch")
                (diff, nor) = (BirchBark(), null);
            else
            {
                diff = Load(id, "diff");
                nor = Load(id, "nor");
            }
            if (diff != null)
            {
                m.SetTexture("_MainTex", diff);
                m.SetColor("_Mean", Mean(diff));
                m.mainTextureScale = kind == "birch" ? Vector2.one : new Vector2(1f, 1f);
            }
            if (nor != null)
                m.SetTexture("_BumpMap", nor);
            else
                m.SetFloat("_Relief", 0f);
            m.SetFloat("_Rough", 0.88f);
            return _bark[kind] = m;
        }

        private static readonly Dictionary<string, Texture2D?> Loaded = new();

        private static Texture2D? Load(string id, string name)
        {
            string key = id + "/" + name;
            if (Loaded.TryGetValue(key, out var t))
                return t;
            t = null;
            foreach (var ext in new[] { ".jpg", ".png" })
            {
                string path = Path.Combine(GroundLayers.Dir(id), name + ext);
                if (!File.Exists(path))
                    continue;
                t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = key, anisoLevel = 8, wrapMode = TextureWrapMode.Repeat };
                t.LoadImage(File.ReadAllBytes(path), false);
                int limit = Settings.Quality <= 1 ? 1024 : Settings.Quality == 2 ? 2048 : 4096;   // the Textures setting: Low 1K, Medium 2K, High+ 4K (as in Godot)
                if (t.width > limit)
                {
                    var rt = RenderTexture.GetTemporary(limit, limit * t.height / t.width, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(t, rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var small = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, true) { name = key, anisoLevel = 8, wrapMode = TextureWrapMode.Repeat };
                    small.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    small.Apply(true);
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    Destroy(t);
                    t = small;
                }
                t.Compress(true);
                t.Apply(true, true);
                break;
            }
            return Loaded[key] = t;
        }

        /// <summary>The average colour of a picture (linear), from a small copy.</summary>
        private static Color Mean(Texture2D t)
        {
            var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(t, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var small = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var sum = Color.black;
            foreach (var p in small.GetPixels())
                sum += p.linear;
            Destroy(small);
            return sum / 256f;
        }

        private static Texture2D? _birch;

        /// <summary>Painted birch bark (GroundTextures.Bark birch): chalk white with dark lenticels and black patches.</summary>
        private static Texture2D BirchBark()
        {
            if (_birch != null)
                return _birch;
            const int w = 256, h = 512;
            var px = new Color32[w * h];
            var rng = new Random(91);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float n = Col.Noise(x * 0.05f, y * 0.012f) * 0.5f + 0.5f;
                    float v = 0.86f + 0.08f * n;
                    px[y * w + x] = new Color(v, v * 0.985f, v * 0.94f);
                }
            // lenticels: thin horizontal dashes
            for (int k = 0; k < 420; k++)
            {
                int cx = rng.Next(w), cy = rng.Next(h), len = 6 + rng.Next(26);
                float dark = 0.18f + (float)rng.NextDouble() * 0.25f;
                for (int dx = 0; dx < len; dx++)
                    for (int dy = 0; dy < 2; dy++)
                        px[((cy + dy) % h) * w + (cx + dx) % w] = new Color(dark, dark * 0.95f, dark * 0.9f);
            }
            // black patches round old branches
            for (int k = 0; k < 18; k++)
            {
                int cx = rng.Next(w), cy = rng.Next(h), rx = 6 + rng.Next(18), ry = 3 + rng.Next(8);
                for (int dy = -ry; dy <= ry; dy++)
                    for (int dx = -rx; dx <= rx; dx++)
                    {
                        float e = dx * dx / (float)(rx * rx) + dy * dy / (float)(ry * ry);
                        if (e > 1f || Col.Noise((cx + dx) * 0.3f, (cy + dy) * 0.3f) > 0.4f + e * 0.3f)
                            continue;
                        px[((cy + dy + h) % h) * w + (cx + dx + w) % w] = new Color(0.08f, 0.07f, 0.06f);
                    }
            }
            _birch = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "BirchBark", wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
            _birch.SetPixels32(px);
            _birch.Apply(true);
            _birch.Compress(true);
            _birch.Apply(true, true);
            return _birch;
        }

        private Material? GrassMaterial()
        {
            if (_grassMat != null)
                return _grassMat;
            var cards = Load("grass_medium_01", "cards");
            if (cards == null)
                return null;
            _grassMat = new Material(FindShader("FS/GrassCard")) { name = "GrassCards", enableInstancing = true, mainTexture = cards };
            _grassMat.SetTexture("_Noise", ProcTextures.NoiseField);
            _grassMat.SetColor("_CardMean", Mean(cards));
            string info = Path.Combine(GroundLayers.Dir("grass_medium_01"), "info.json");
            if (File.Exists(info))
            {
                var j = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(info));
                if (j["cells"] is Newtonsoft.Json.Linq.JArray cells)
                {
                    var hgt = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
                    var wid = Vector4.one;
                    for (int i = 0; i < Math.Min(4, cells.Count); i++)
                    {
                        hgt[i] = (float?)cells[i]["height"] ?? 0.5f;
                        wid[i] = (float?)cells[i]["width"] ?? 1f;
                    }
                    _grassMat.SetVector("_CellHeight", hgt);
                    _grassMat.SetVector("_CellWidth", wid);
                }
            }
            return _grassMat;
        }

        // ---------------------------------------------------------------- the meadow round the angler
        private static float Hash(int x, int z, int k)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + z * 668265263 + k * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        private static (List<Matrix4x4>, List<Vector4>) GrassField(Lake lake, Vector2 centre, float radius, float spacing)
        {
            var m = new List<Matrix4x4>(8192);
            var c = new List<Vector4>(8192);
            int x0 = Mathf.FloorToInt((centre.x - radius) / spacing), x1 = Mathf.CeilToInt((centre.x + radius) / spacing);
            int z0 = Mathf.FloorToInt((centre.y - radius) / spacing), z1 = Mathf.CeilToInt((centre.y + radius) / spacing);
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    float x = (ix + Hash(ix, iz, 1)) * spacing, z = (iz + Hash(ix, iz, 2)) * spacing;
                    float d = new Vector2(x - centre.x, z - centre.y).magnitude;
                    if (d > radius)
                        continue;
                    float edge = Mathf.Clamp01((d - radius * 0.55f) / (radius * 0.45f));
                    if (Hash(ix, iz, 3) < edge * 0.75f)
                        continue;
                    float h = lake.HeightAt(x, z);
                    if (h < 0.6f || Lake.OnPier(x, z) || lake.PathDistance(x, z) < 1.1f || lake.PadDistance(x, z) < 0.5f)
                        continue;
                    float slope = MathF.Abs(lake.HeightAt(x + 0.6f, z) - h) + MathF.Abs(lake.HeightAt(x, z + 0.6f) - h);
                    if (slope > 0.55f)
                        continue;
                    var col = lake.ColorAt(x, z, h);
                    if (col.G < col.R * 1.02f || col.G < col.B)
                        continue;
                    var g = lake.GroundAt(x, z, h);
                    if (1f - g.Sand - g.Path - g.Forest - g.Bed < 0.5f && (g.Forest < 0.4f || Hash(ix, iz, 8) > 0.2f))
                        continue;
                    float s = (0.7f + 0.6f * Hash(ix, iz, 4)) * (1f - edge * 0.5f);
                    m.Add(Matrix4x4.TRS(new Vector3(x, h - 0.02f, z), Quaternion.Euler(0f, Hash(ix, iz, 5) * 360f, 0f), new Vector3(s, s * (0.8f + 0.5f * Hash(ix, iz, 6)), s)));
                    float tone = (Hash(ix, iz, 7) - 0.5f) * 0.12f;
                    var lin = Col.Lin(new Color(col.R * 1.05f + tone, col.G * 1.08f + tone, col.B + tone * 0.5f));
                    c.Add(new Vector4(lin.r, lin.g, lin.b, 1f));
                }
            return (m, c);
        }

        private void UpdateGrass(Vector3 eye)
        {
            float radius = GrassRadius;
            if (radius <= 0f || _lake == null)
                return;
            var mat = GrassMaterial();
            if (mat == null)
                return;
            if (_grassJob != null)
            {
                if (!_grassJob.IsCompleted)
                    return;
                if (_grassJob.Exception == null)
                {
                    var (m, c) = _grassJob.Result;
                    _cards ??= Plants.MeadowCards();
                    var b = new Batch { Near = _cards, Mat = mat, Range = radius + 4f, MaxRange = radius + 4f, Shadows = false };
                    var list = new List<Item>(m.Count);
                    for (int i = 0; i < m.Count; i++)
                        list.Add(new Item(0, m[i], c[i]));
                    Fill(b, list);
                    _grass = b;
                }
                _grassJob = null;
            }
            var here = new Vector2(eye.x, eye.z);
            if ((here - _grassCentre).magnitude < radius * 0.25f)
                return;
            _grassCentre = here;
            var lake = _lake;
            float spacing = (Settings.Quality >= 3 ? 0.42f : 0.5f) * 0.78f;
            _grassJob = Task.Run(() => GrassField(lake, here, radius, spacing));
        }

        // ---------------------------------------------------------------- drawing
        private void LateUpdate()
        {
            if (!Ready)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            var eye = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            UpdateGrass(eye);
            foreach (var b in _batches)
                Draw(b, eye);
            if (_grass != null)
                Draw(_grass, eye);
        }

        private void Draw(Batch b, Vector3 eye)
        {
            if (!GeometryUtility.TestPlanesAABB(_planes, b.Box))
                return;
            float d = Mathf.Sqrt(b.Box.SqrDistance(eye));
            bool near = d < b.Range;
            if (!near && (b.Far == null || d > b.MaxRange))
                return;
            var mesh = near ? b.Near : b.Far!;
            var shadows = near && b.Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            for (int i = 0; i < b.Chunks.Count; i++)
                Graphics.DrawMeshInstanced(mesh, 0, b.Mat, b.Chunks[i], b.Counts[i], b.Props[i], shadows, true, 0);
        }
    }
}
