using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Data = FishingSim.Data;

namespace FishingSim.Game
{
    /// <summary>
    /// Builds a map from <see cref="Lake"/>: the terrain (one mesh, fine over the map, coarse out to the mountains), the
    /// water, trees, bushes, stones, reeds and grass (merged per 64 m cell), the pier. Heights are computed on worker
    /// threads; only the meshes are made on the main thread.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        public const float PierDeck = 0.62f;
        public Lake Lake { get; private set; } = null!;
        public Material WaterMaterial { get; private set; } = null!;
        public float Progress { get; private set; }
        public bool Ready { get; private set; }

        private readonly List<GameObject> _parts = new();
        private Material _lit = null!, _foliage = null!, _foliageTwoSided = null!;

        public static Color Lin(Compat.Color c) => Col.Lin(c.R, c.G, c.B);

        public IEnumerator Build(Data.LocationData location)
        {
            Ready = false;
            Progress = 0f;
            foreach (var p in _parts)
                Destroy(p);
            _parts.Clear();
            Lake = new Lake(location);
            _lit = Materials.Lit();
            _foliage = Materials.Foliage(false);
            _foliageTwoSided = Materials.Foliage(true);

            // --- terrain (heights and colours on a worker thread)
            var xs = GridLines(Lake.XMin, Lake.XMax);
            var zs = GridLines(Lake.ZMin, Lake.ZMax);
            var heights = new float[xs.Count * zs.Count];
            var colours = new Color[xs.Count * zs.Count];
            var lake = Lake;
            var task = Task.Run(() =>
            {
                Parallel.For(0, zs.Count, j =>
                {
                    for (int i = 0; i < xs.Count; i++)
                    {
                        float h = lake.HeightAt(xs[i], zs[j]);
                        heights[j * xs.Count + i] = h;
                        colours[j * xs.Count + i] = Lin(lake.ColorAt(xs[i], zs[j], h));
                    }
                });
            });
            while (!task.IsCompleted)
            {
                Progress = 0.05f;
                yield return null;
            }
            if (task.Exception != null)
                throw task.Exception;
            Progress = 0.35f;
            yield return null;
            MakeTerrain(xs, zs, heights, colours);
            Progress = 0.45f;
            yield return null;

            // --- water
            MakeWater(location);
            MakePier();
            Progress = 0.5f;
            yield return null;

            // --- plants and stones, built on worker threads per cell
            var cells = new List<Vector2Int>();
            for (int cz = Mathf.FloorToInt(-Lake.Outer / 64f); cz < Mathf.CeilToInt(Lake.Outer / 64f); cz++)
                for (int cx = Mathf.FloorToInt(-Lake.Outer / 64f); cx < Mathf.CeilToInt(Lake.Outer / 64f); cx++)
                    cells.Add(new Vector2Int(cx, cz));
            var built = new (MeshKit Solid, MeshKit Leaves, MeshKit Grass)[cells.Count];
            var plants = Task.Run(() => Parallel.For(0, cells.Count, k => built[k] = Scatter(lake, cells[k])));
            while (!plants.IsCompleted)
            {
                Progress = 0.55f;
                yield return null;
            }
            if (plants.Exception != null)
                throw plants.Exception;
            for (int k = 0; k < cells.Count; k++)
            {
                var (solid, leaves, grass) = built[k];
                if (solid.Count > 0)
                    Part($"Cell{cells[k]}", solid.ToMesh(), _lit, true);
                if (leaves.Count > 0)
                    Part($"Leaves{cells[k]}", leaves.ToMesh(), _foliage, true);
                if (grass.Count > 0)
                    Part($"Grass{cells[k]}", grass.ToMesh(), _foliageTwoSided, false);
                Progress = 0.6f + 0.4f * k / cells.Count;
                if (k % 8 == 0)
                    yield return null;
            }
            Progress = 1f;
            Ready = true;
        }

        /// <summary>Grid lines every 2.5 m over the map and 30 m around it, widening to 14 m out to the mountains.</summary>
        private static List<float> GridLines(float min, float max)
        {
            var list = new List<float>();
            float lo = min - 30f, hi = max + 30f;
            float x = -Lake.Outer;
            while (x < Lake.Outer)
            {
                list.Add(x);
                float d = x < lo ? lo - x : x > hi ? x - hi : 0f;
                x += Mathf.Clamp(2.5f + d * 0.06f, 2.5f, 14f);
            }
            list.Add(Lake.Outer);
            return list;
        }

        private GameObject Part(string name, Mesh mesh, Material mat, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            _parts.Add(go);
            return go;
        }

        private void MakeTerrain(List<float> xs, List<float> zs, float[] heights, Color[] colours)
        {
            int nx = xs.Count, nz = zs.Count;
            var verts = new Vector3[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                    verts[j * nx + i] = new Vector3(xs[i], heights[j * nx + i], zs[j]);
            var tris = new int[(nx - 1) * (nz - 1) * 6];
            int t = 0;
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    // clockwise seen from above
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            var mesh = new Mesh { name = "Terrain", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.colors = colours;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = Part("Terrain", mesh, Materials.Ground(), true);
            go.GetComponent<MeshRenderer>().receiveShadows = true;
        }

        private void MakeWater(Data.LocationData loc)
        {
            const float step = 2f;
            float x0 = Lake.XMin - 40f, x1 = Lake.XMax + 40f, z0 = Lake.ZMin - 40f, z1 = Lake.ZMax + 40f;
            int nx = Mathf.CeilToInt((x1 - x0) / step) + 1, nz = Mathf.CeilToInt((z1 - z0) / step) + 1;
            var verts = new Vector3[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                    verts[j * nx + i] = new Vector3(x0 + i * step, 0f, z0 + j * step);
            var tris = new List<int>();
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    // only cells near or over the water
                    float cx = x0 + (i + 0.5f) * step, cz = z0 + (j + 0.5f) * step;
                    if (Lake.InsideMetres(cx, cz) < -6f)
                        continue;
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            var mesh = new Mesh { name = "Water", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.SetTriangles(tris, 0);
            var normals = new Vector3[verts.Length];
            for (int k = 0; k < normals.Length; k++)
                normals[k] = Vector3.up;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.up * 2f);
            WaterMaterial = Materials.Water();
            WaterMaterial.SetColor("_Shallow", Lin(loc.WaterShallowColor));
            WaterMaterial.SetColor("_Deep", Lin(loc.WaterDeepColor));
            WaterMaterial.SetFloat("_Clarity", Mathf.Max(loc.WaterClarity * 0.8f, 0.6f));
            WaterMaterial.SetFloat("_Amp", Waves.Amplitude);
            Part("Water", mesh, WaterMaterial, false).GetComponent<MeshRenderer>().receiveShadows = false;
        }

        private void MakePier()
        {
            var k = new MeshKit();
            var wood = Col.Lin(0.36f, 0.28f, 0.2f);
            var dark = Col.Lin(0.22f, 0.17f, 0.12f);
            var rng = new System.Random(7);
            for (float z = -4.2f; z < 6.2f; z += 0.26f)
            {
                var c = Color.Lerp(wood, dark, (float)rng.NextDouble() * 0.5f);
                k.Box(new Vector3(0f, PierDeck - 0.03f, z + 0.12f), new Vector3(3.2f, 0.06f, 0.23f),
                    Quaternion.Euler(0f, (float)(rng.NextDouble() - 0.5) * 1.2f, 0f), c);
            }
            foreach (float x in new[] { -1.5f, 1.5f })
            {
                k.Box(new Vector3(x, PierDeck - 0.12f, 1f), new Vector3(0.12f, 0.12f, 10.4f), Quaternion.identity, dark);
                for (float z = -4f; z <= 6.1f; z += 2.5f)
                {
                    float bed = Lake.HeightAt(x, z);
                    k.Tube(new Vector3(x, bed - 0.3f, z), new Vector3(x, PierDeck + 0.05f, z), 0.11f, 0.1f, 8, dark, wood, true);
                }
                // hand rail on the far half
                for (float z = -4f; z <= 1.1f; z += 2.5f)
                    k.Tube(new Vector3(x, PierDeck, z), new Vector3(x, PierDeck + 0.95f, z), 0.05f, 0.045f, 6, dark, wood, true);
                k.Box(new Vector3(x, PierDeck + 0.95f, -1.5f), new Vector3(0.08f, 0.06f, 5.2f), Quaternion.identity, wood);
            }
            Part("Pier", k.ToMesh("Pier"), _lit, true);
        }

        // ------------------------------------------------------------------ plants
        private static float Hash(int x, int z, int k)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(k * 83492791);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        /// <summary>Trees, bushes and stones (solid parts and leaves separately), reeds and grass for one 64 m cell.</summary>
        private static (MeshKit, MeshKit, MeshKit) Scatter(Lake lake, Vector2Int cell)
        {
            var solid = new MeshKit();
            var leaves = new MeshKit();
            var grass = new MeshKit();
            float x0 = cell.x * 64f, z0 = cell.y * 64f;
            var loc = lake.Location;
            // trees on a jittered grid: dense in the forest belt, sparse on meadows
            const float step = 4.2f;
            for (int j = 0; j < 16; j++)
                for (int i = 0; i < 16; i++)
                {
                    int gx = Mathf.RoundToInt(x0 / step) + i, gz = Mathf.RoundToInt(z0 / step) + j;
                    float x = gx * step + (Hash(gx, gz, 1) - 0.5f) * step * 0.9f;
                    float z = gz * step + (Hash(gx, gz, 2) - 0.5f) * step * 0.9f;
                    if (x < x0 || x >= x0 + 64f || z < z0 || z >= z0 + 64f || Mathf.Abs(x) > Lake.Outer - 4f || Mathf.Abs(z) > Lake.Outer - 4f)
                        continue;
                    float h = lake.HeightAt(x, z);
                    float play = lake.PlayDistance(x, z);
                    bool forest = lake.IsForest(x, z, h) || (play > -25f && h > 1.5f && h < loc.RockLine + 8f);
                    float r = Hash(gx, gz, 3);
                    if (Lake.OnPier(x, z) || lake.Built(x, z))
                        continue;
                    if (forest && r < (play > -25f ? 0.85f : 0.62f) * Mathf.Clamp(loc.TreeDensity, 0.3f, 1.4f))
                    {
                        if (play > 60f && r > 0.5f)
                            continue;   // thinner up the mountains
                        float size = 0.75f + Hash(gx, gz, 4) * 0.7f;
                        bool pine = Hash(gx, gz, 5) < 0.55f + Mathf.Clamp01((h - 6f) / 20f) * 0.4f;
                        if (pine)
                            Pine(solid, leaves, new Vector3(x, h, z), size, gx * 31 + gz);
                        else
                            Broadleaf(solid, leaves, new Vector3(x, h, z), size, gx * 31 + gz, Hash(gx, gz, 6) < 0.3f);
                    }
                    else if (h > 1.2f && h < loc.RockLine && r > 0.985f && play < -10f)
                        Broadleaf(solid, leaves, new Vector3(x, h, z), 0.9f + Hash(gx, gz, 4) * 0.5f, gx * 31 + gz, false);
                    else if (h > 0.8f && r < 0.06f && play < 0f)
                        Bush(leaves, new Vector3(x, h, z), 0.6f + Hash(gx, gz, 7), gx * 13 + gz);
                    // stones on the beach, rocky slopes
                    if (Hash(gx, gz, 8) < (h > loc.RockLine ? 0.25f : h > -0.4f && h < 1f ? 0.08f : 0.012f))
                    {
                        var p = new Vector3(x + 1.3f, 0f, z - 0.9f);
                        p.y = lake.HeightAt(p.x, p.z);
                        float s = 0.25f + Hash(gx, gz, 9) * (h > loc.RockLine ? 2.2f : 0.7f);
                        var stone = Col.Lin(Color.Lerp(new Color(0.42f, 0.41f, 0.38f), new Color(0.3f, 0.31f, 0.29f), Hash(gx, gz, 10)));
                        solid.Blob(p + Vector3.up * s * 0.25f, new Vector3(s, s * 0.6f, s * 0.85f), gx * 7 + gz, 0.5f, stone, stone * 0.6f, false, 1);
                    }
                }
            // reeds along the shallow shore and grass tufts on the meadows near the lake
            const float gstep = 1.6f;
            for (int j = 0; j < 40; j++)
                for (int i = 0; i < 40; i++)
                {
                    int gx = Mathf.RoundToInt(x0 / gstep) + i, gz = Mathf.RoundToInt(z0 / gstep) + j;
                    float x = gx * gstep + (Hash(gx, gz, 11) - 0.5f) * gstep;
                    float z = gz * gstep + (Hash(gx, gz, 12) - 0.5f) * gstep;
                    if (x < x0 || x >= x0 + 64f || z < z0 || z >= z0 + 64f || lake.PlayDistance(x, z) > 10f || Lake.OnPier(x, z) || lake.Built(x, z, 0.5f))
                        continue;
                    float h = lake.HeightAt(x, z);
                    float r = Hash(gx, gz, 13);
                    if (h < -0.05f && h > -0.9f && r < 0.5f && Mathf.Abs(x) > 3f)
                        Reeds(grass, new Vector3(x, h, z), gx * 17 + gz);
                    else if (h > 0.9f && h < loc.RockLine && r < 0.55f && !lake.IsForest(x, z, h))
                        Tuft(grass, new Vector3(x, h, z), gx * 19 + gz, WorldView.Lin(loc.GrassColor));
                }
            return (solid, leaves, grass);
        }

        private static readonly Color Bark = Col.Lin(0.27f, 0.2f, 0.15f);
        private static readonly Color BirchBark = Col.Lin(0.78f, 0.76f, 0.7f);

        private static void Pine(MeshKit solid, MeshKit leaves, Vector3 at, float size, int seed)
        {
            var rng = new System.Random(seed);
            float height = 11f * size;
            solid.Tube(at - Vector3.up * 0.3f, at + Vector3.up * height * 0.92f, 0.22f * size, 0.04f, 6, Bark, Bark, false);
            var needle = Col.Lin(Color.Lerp(new Color(0.08f, 0.16f, 0.09f), new Color(0.12f, 0.22f, 0.1f), (float)rng.NextDouble()));
            var dark = needle * 0.55f;
            int tiers = 6;
            for (int t = 0; t < tiers; t++)
            {
                float f = t / (float)(tiers - 1);
                float y = height * Mathf.Lerp(0.22f, 0.86f, f);
                float radius = Mathf.Lerp(2.3f, 0.6f, f) * size * (0.85f + (float)rng.NextDouble() * 0.3f);
                float rise = Mathf.Lerp(2.1f, 1.4f, f) * size;
                // a drooping skirt of branches: a ring of jittered points under an apex
                int n = 9;
                var c = at + Vector3.up * y;
                int apex = leaves.Add(c + Vector3.up * rise, Vector3.up, new Color(needle.r, needle.g, needle.b, 0.75f));
                int first = leaves.Count;
                for (int k = 0; k < n; k++)
                {
                    float a = k / (float)n * Mathf.PI * 2f + (float)rng.NextDouble() * 0.3f;
                    float rr = radius * (0.8f + (float)rng.NextDouble() * 0.35f);
                    var p = c + new Vector3(Mathf.Cos(a) * rr, -0.35f * size - (float)rng.NextDouble() * 0.3f, Mathf.Sin(a) * rr);
                    var nrm = new Vector3(Mathf.Cos(a), 0.6f, Mathf.Sin(a)).normalized;
                    leaves.Add(p, nrm, new Color(dark.r, dark.g, dark.b, 0.55f));
                }
                for (int k = 0; k < n; k++)
                    leaves.Tri(apex, first + (k + 1) % n, first + k);
                // underside so it is not see-through from below
                int under = leaves.Add(c - Vector3.up * 0.2f, Vector3.down, new Color(dark.r * 0.6f, dark.g * 0.6f, dark.b * 0.6f, 0.8f));
                for (int k = 0; k < n; k++)
                    leaves.Tri(under, first + k, first + (k + 1) % n);
            }
            leaves.Add(at, Vector3.up, needle); // keeps the bounds' floor
        }

        private static void Broadleaf(MeshKit solid, MeshKit leaves, Vector3 at, float size, int seed, bool birch)
        {
            var rng = new System.Random(seed);
            float height = (birch ? 9f : 7.5f) * size;
            var bark = birch ? BirchBark : Bark;
            solid.Tube(at - Vector3.up * 0.3f, at + Vector3.up * height * 0.75f, (birch ? 0.14f : 0.26f) * size, 0.08f * size, 7, bark, bark, false);
            var leaf = Col.Lin(birch
                ? Color.Lerp(new Color(0.3f, 0.42f, 0.14f), new Color(0.38f, 0.48f, 0.16f), (float)rng.NextDouble())
                : Color.Lerp(new Color(0.17f, 0.3f, 0.1f), new Color(0.26f, 0.36f, 0.12f), (float)rng.NextDouble()));
            leaf.a = 0.6f;
            var dark = leaf * 0.45f;
            dark.a = 0.8f;
            int blobs = 3 + rng.Next(3);
            for (int b = 0; b < blobs; b++)
            {
                var off = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() * 0.6f, (float)rng.NextDouble() - 0.5f) * 2.6f * size;
                float r = (1.4f + (float)rng.NextDouble() * 0.9f) * size * (birch ? 0.8f : 1f);
                // branch to the blob
                solid.Tube(at + Vector3.up * height * 0.5f, at + Vector3.up * height * 0.72f + off * 0.7f, 0.07f * size, 0.04f * size, 5, bark, bark);
                leaves.Blob(at + Vector3.up * height * 0.78f + off, new Vector3(r, r * 0.8f, r), seed * 7 + b, 0.55f, leaf, dark, true, 2);
            }
        }

        private static void Bush(MeshKit leaves, Vector3 at, float size, int seed)
        {
            var leaf = Col.Lin(0.16f, 0.27f, 0.1f);
            leaf.a = 0.7f;
            var dark = leaf * 0.5f;
            dark.a = 1f;
            leaves.Blob(at + Vector3.up * 0.45f * size, new Vector3(1.1f, 0.75f, 1f) * size, seed, 0.6f, leaf, dark, true, 1);
        }

        private static void Reeds(MeshKit k, Vector3 at, int seed)
        {
            var rng = new System.Random(seed);
            var green = Col.Lin(0.32f, 0.38f, 0.16f);
            var tip = Col.Lin(0.55f, 0.5f, 0.28f);
            for (int s = 0; s < 7; s++)
            {
                var p = at + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.8f;
                float h = 1.2f + (float)rng.NextDouble() * 1.1f;
                var lean = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.35f;
                Blade(k, p, p + Vector3.up * h + lean, 0.025f, green, tip);
            }
        }

        private static void Tuft(MeshKit k, Vector3 at, int seed, Color grass)
        {
            var rng = new System.Random(seed);
            for (int s = 0; s < 5; s++)
            {
                var p = at + new Vector3((float)rng.NextDouble() - 0.5f, -0.02f, (float)rng.NextDouble() - 0.5f) * 0.5f;
                float h = 0.25f + (float)rng.NextDouble() * 0.35f;
                var lean = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.25f;
                var c = Color.Lerp(grass * 0.6f, grass * 1.15f, (float)rng.NextDouble());
                Blade(k, p, p + Vector3.up * h + lean, 0.035f, c * 0.55f, c);
            }
        }

        /// <summary>A thin tapering blade (two-sided material), its tip swaying in the wind.</summary>
        private static void Blade(MeshKit k, Vector3 root, Vector3 tip, float width, Color baseColour, Color tipColour)
        {
            var dir = tip - root;
            var side = Vector3.Cross(dir, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.01f)
                side = Vector3.right;
            side *= width;
            var n = Vector3.Cross(side, dir).normalized;
            if (n.y < 0f)
                n = -n;
            baseColour.a = 1f;
            tipColour.a = 0.2f;
            var mid = Color.Lerp(baseColour, tipColour, 0.5f);
            mid.a = 0.6f;
            int a = k.Add(root - side, n, baseColour);
            k.Add(root + side, n, baseColour);
            k.Add(root + dir * 0.55f + side * 0.6f, n, mid);
            k.Add(root + dir * 0.55f - side * 0.6f, n, mid);
            k.Add(tip, n, tipColour);
            k.Tri(a, a + 3, a + 2);
            k.Tri(a, a + 2, a + 1);
            k.Tri(a + 3, a + 4, a + 2);
        }
    }
}
