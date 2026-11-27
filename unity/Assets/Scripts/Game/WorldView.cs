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
        public Material? TerrainMat { get; private set; }
        public float Progress { get; private set; }
        public bool Ready { get; private set; }

        private readonly List<GameObject> _parts = new();
        private Forest? _forest;
        private Landmarks? _landmarks;

        /// <summary>Tree trunks and boulders the angler walks round.</summary>
        public List<(Vector3 At, float Radius)> Solids { get; } = new();
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
            var weightsA = new Vector2[xs.Count * zs.Count];
            var weightsB = new Vector2[xs.Count * zs.Count];
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
                        var g = lake.GroundAt(xs[i], zs[j], h);
                        weightsA[j * xs.Count + i] = new Vector2(g.Sand, g.Path);
                        weightsB[j * xs.Count + i] = new Vector2(g.Forest, g.Bed);
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
            Progress = 0.3f;
            LoadLog.Mark("terrain heights done");
            yield return null;
            int size = GroundLayers.SizeForQuality;
            // the ground's photographs (usually ready: GameRoot prepares them while the login screen shows)
            var ground = GroundLayers.Prepare(size);
            while (ground.MoveNext())
            {
                Progress = 0.3f + 0.1f * GroundLayers.Progress;
                yield return ground.Current;
            }
            bool photos = GroundLayers.Ready(size);
            LoadLog.Begin("terrain mesh");
            MakeTerrain(xs, zs, heights, colours, weightsA, weightsB, photos ? TerrainMaterial(location) : Materials.Ground());
            LoadLog.End("terrain mesh");
            Progress = 0.45f;
            yield return null;

            // --- water
            LoadLog.Begin("water and pier");
            MakeWater(location);
            MakePier();
            PierGear();
            LoadLog.End("water and pier");
            Progress = 0.5f;
            yield return null;

            // --- plants and stones, built on worker threads per cell
            var cells = new List<Vector2Int>();
            for (int cz = Mathf.FloorToInt((Lake.ZMin - Lake.ForestMargin) / 64f); cz < Mathf.CeilToInt((Lake.ZMax + Lake.ForestMargin) / 64f); cz++)
                for (int cx = Mathf.FloorToInt((Lake.XMin - Lake.ForestMargin) / 64f); cx < Mathf.CeilToInt((Lake.XMax + Lake.ForestMargin) / 64f); cx++)
                    cells.Add(new Vector2Int(cx, cz));
            var built = new (MeshKit Solid, MeshKit Leaves, MeshKit Grass)[cells.Count];
            LoadLog.Begin("plants and stones");
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
                Progress = 0.6f + 0.2f * k / cells.Count;
                if (k % 8 == 0)
                    yield return null;
            }
            LoadLog.End("plants and stones");
            // the woods, bushes, boulders and the meadow (Scenery's placement, instanced)
            Solids.Clear();
            LoadLog.Begin("landmarks");
            _landmarks ??= new GameObject("Landmarks").AddComponent<Landmarks>();
            _landmarks.transform.SetParent(transform, false);
            _landmarks.Build(Lake);
            LoadLog.End("landmarks");
            Progress = 0.85f;
            yield return null;
            Solids.AddRange(_landmarks.Solids);
            Forest.Reserved.Clear();
            foreach (var entry in location.Landmarks)
            {
                var parts = entry.Split(',');
                if (parts.Length >= 3 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lx)
                    && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lz))
                {
                    string kind = parts[0].Trim();
                    if (kind == "dike")
                    {
                        float length = parts.Length >= 5 && float.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float l) ? Mathf.Max(l, 20f) : 20f;
                        float yaw = parts.Length >= 4 && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) ? y * Mathf.Deg2Rad : 0f;
                        var along = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
                        for (float t = -length / 2f; t <= length / 2f; t += 4f)
                            Forest.Reserved.Add((lx + along.x * t, lz + along.y * t, 6f));
                    }
                    else if (kind is not ("platforms" or "rocks" or "stilthouse"))
                        Forest.Reserved.Add((lx, lz, 8f));
                }
            }
            Forest.LandmarkTrees.Clear();
            Forest.LandmarkTrees.AddRange(_landmarks.TreeSpots);
            _forest ??= gameObject.AddComponent<Forest>();
            LoadLog.Begin("forest");
            var forest = _forest.Build(Lake, Solids);
            while (forest.MoveNext())
            {
                Progress = Mathf.Min(Progress + 0.01f, 0.99f);
                yield return forest.Current;
            }
            LoadLog.End("forest");
            Progress = 1f;
            Ready = true;
        }

        /// <summary>Grid lines every 2.5 m, coarser in the forest just outside the map.</summary>
        private static List<float> GridLines(float min, float max)
        {
            var list = new List<float>();
            float lo = min - Lake.ForestMargin, hi = max + Lake.ForestMargin;
            float x = lo;
            while (x < hi)
            {
                list.Add(x);
                float d = x < min ? min - x : x > max ? x - max : 0f;
                x += Mathf.Clamp(2.5f + d * 0.06f, 2.5f, 14f);
            }
            list.Add(hi);
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

        public static Color Gamma(Compat.Color c) => new(c.R, c.G, c.B, 1f);

        /// <summary>FS/Terrain with the photographed layers, tinted to the map's colours (LakeBed.ApplyGroundTints).</summary>
        private static Material TerrainMaterial(Data.LocationData loc)
        {
            var m = new Material(Shader.Find("FS/Terrain")) { name = "Terrain" };
            m.SetTexture("_GroundAlb", GroundLayers.Albedo);
            m.SetTexture("_GroundNrm", GroundLayers.Normal);
            m.SetTexture("_Noise", ProcTextures.NoiseField);
            m.SetTexture("_Caustics", ProcTextures.Caustics);
            // Reuse the photographed materials for gravel banks, damp leaf litter and sandy spits.
            m.SetVector("_ShoreMix", new Vector4(loc.ShoreMix.X, loc.ShoreMix.Y, 0f, 0f));
            var tints = new Vector4[GroundLayers.Count];
            tints[GroundLayers.Grass] = GroundLayers.Photo(GroundLayers.Calm(Gamma(loc.GrassColor)), GroundLayers.Grass, 0.45f);
            tints[GroundLayers.Forest] = GroundLayers.Photo(Color.Lerp(Gamma(loc.DarkGrassColor), Gamma(loc.PathColor), 0.6f) * 0.75f, GroundLayers.Forest, 0.15f);
            tints[GroundLayers.Sand] = GroundLayers.Photo(Gamma(loc.SandColor), GroundLayers.Sand, 0.3f);
            tints[GroundLayers.Path] = GroundLayers.Photo(GroundLayers.Calm(Gamma(loc.PathColor)), GroundLayers.Path, 0.2f);
            tints[GroundLayers.Bed] = GroundLayers.Photo(Gamma(loc.SandColor) * 0.7f, GroundLayers.Bed, 0.3f);
            tints[GroundLayers.Rock] = Vector4.one;
            tints[GroundLayers.Snow] = Vector4.one;
            m.SetVectorArray("_LayerTint", tints);
            m.SetFloatArray("_LayerRough", GroundLayers.Roughness);
            m.SetVector("_DarkGrassTint", GroundLayers.Photo(GroundLayers.Calm(Gamma(loc.DarkGrassColor)), GroundLayers.Grass, 0.6f));
            m.SetFloat("_SnowLine", loc.SnowLine);
            m.SetFloat("_RockLine", loc.RockLine);
            return m;
        }

        private void MakeTerrain(List<float> xs, List<float> zs, float[] heights, Color[] colours, Vector2[] uvA, Vector2[] uvB, Material material)
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
            mesh.uv = uvA;
            mesh.uv2 = uvB;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            var normals = mesh.normals;
            var tangents = new Vector4[normals.Length];
            for (int k = 0; k < normals.Length; k++)
            {
                var tan = Vector3.Cross(normals[k], Vector3.forward).normalized;
                tangents[k] = new Vector4(tan.x, tan.y, tan.z, 1f);
            }
            mesh.tangents = tangents;
            mesh.RecalculateBounds();
            var go = Part("Terrain", mesh, material, true);
            TerrainMat = material;
            go.GetComponent<MeshRenderer>().receiveShadows = true;
        }

        private void MakeWater(Data.LocationData loc)
        {
            const float step = 1f;
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
            var shallow = Gamma(loc.WaterShallowColor);
            WaterMaterial.SetColor("_Shallow", shallow);
            WaterMaterial.SetColor("_Deep", Gamma(loc.WaterDeepColor));
            WaterMaterial.SetFloat("_Clarity", loc.WaterClarity);
            float gg = Mathf.Max(shallow.g, 0.02f);
            WaterMaterial.SetVector("_Extinction", new Vector4(Mathf.Clamp(gg / Mathf.Max(shallow.r, 0.02f), 1.2f, 3.2f), 1f, Mathf.Clamp(gg / Mathf.Max(shallow.b, 0.02f), 0.6f, 2.2f), 0f));
            WaterMaterial.SetTexture("_Ripples", ProcTextures.Ripples);
            WaterMaterial.SetTexture("_FoamNoise", ProcTextures.Foam);
            WaterMaterial.SetFloat("_SsrSteps", Settings.SsrSteps);
            // the lake's depth over the map (Water.BuildDepthMap: 1.5 m cells, depth / 16 m)
            const float cell = 1.5f;
            int dw = Mathf.CeilToInt((Lake.XMax - Lake.XMin) / cell), dh = Mathf.CeilToInt((Lake.ZMax - Lake.ZMin) / cell);
            var depth = new byte[dw * dh];
            for (int j = 0; j < dh; j++)
                for (int i = 0; i < dw; i++)
                    depth[j * dw + i] = (byte)Mathf.Clamp(Lake.DepthAt(Lake.XMin + (i + 0.5f) * cell, Lake.ZMin + (j + 0.5f) * cell) / 16f * 255f, 0f, 255f);
            var dm = new Texture2D(dw, dh, TextureFormat.R8, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            dm.SetPixelData(depth, 0);
            dm.Apply(false, true);
            WaterMaterial.SetTexture("_DepthMap", dm);
            WaterMaterial.SetVector("_MapRect", new Vector4(Lake.XMin, Lake.ZMin, dw * cell, dh * cell));
            WaterMaterial.SetFloat("_MaxDepth", 16f);
            Part("Water", mesh, WaterMaterial, false).GetComponent<MeshRenderer>().receiveShadows = false;
        }

        private Material? _bulbs;
        private Light? _pierLamp;

        /// <summary>
        /// Scenery.BuildPier: old grey-brown boards weathered like the buildings (photographed weathered_brown_planks,
        /// colour wander, rain streaks, moss in patches), darker posts and rails soaked from the waterline up, festoon
        /// lights on masts behind the casting spot and a lantern post at the shore end. Painted boards without the set.
        /// </summary>
        /// <summary>
        /// The angler's gear on and by the pier (Meshy models, when present): the keepnet hung from the pier's end into
        /// the water, the landing net lying on the deck with its hoop over the water, a rod pod with a bell near the end
        /// and two hazel rod rests in the bank at the pier's root.
        /// </summary>
        private void PierGear()
        {
            float deck = PierDeck;
            // keepnet: its top ring at the deck's edge, slanting down and out into the water (glTFast mirrors the file's
            // x on import, so the turns are the mirror of what Blender shows)
            if (ModelFit.Place("tackle/keepnet", transform, 2.0f, Quaternion.Euler(0f, 0f, 55f), ModelFit.Anchor.Centre, "Keepnet") is { } keepnet)
                keepnet.position = new Vector3(2.3f, deck - 0.6f, -3.4f);
            // landing net: the handle along the deck, the hoop and bag hanging over the water past the pier's left edge
            if (ModelFit.Place("tackle/landing_net", transform, 2.5f, Quaternion.identity, ModelFit.Anchor.Top, "LandingNet") is { } net)
                net.position = new Vector3(-0.95f, deck + 0.035f, -2.6f);
            // rod pod: standing on the deck near the end, pointing out over the water
            if (ModelFit.Place("tackle/rodpod_bell", transform, 1.2f, Quaternion.Euler(0f, 90f, 0f), ModelFit.Anchor.Bottom, "RodPod") is { } pod)
                pod.position = new Vector3(0.75f, deck, -3.0f);
            // hazel rests pushed 20 cm into the bank either side of the pier's root
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -2.3f : 2.3f, z = 6.9f;
                if (ModelFit.Place("tackle/rodrest_fork", transform, 1.0f, Quaternion.Euler(0f, i * 70f + 20f, 0f), ModelFit.Anchor.Bottom, $"RodRest{i}") is { } rest)
                    rest.position = new Vector3(x, Lake.HeightAt(x, z) - 0.2f, z);
            }
        }

        private void MakePier()
        {
            var wood = Photos.Weathered("weathered_brown_planks", new Color(0.92f, 0.9f, 0.86f), 0.85f, moss: 0.22f);
            var darkWood = Photos.Weathered("weathered_brown_planks", new Color(0.5f, 0.45f, 0.4f), 0.92f, scale: 0.6f, moss: 0.12f);
            var deck = new MeshKit();
            var frame = new MeshKit();
            var woodCol = wood != null ? Color.white : Col.Lin(0.36f, 0.28f, 0.2f);
            var darkCol = darkWood != null ? Color.white : Col.Lin(0.22f, 0.17f, 0.12f);
            var rng = new System.Random(7);
            for (float z = -4.2f; z < 6.2f; z += 0.26f)
            {
                var c = wood != null ? woodCol : Color.Lerp(woodCol, darkCol, (float)rng.NextDouble() * 0.5f);
                deck.Box(new Vector3(0f, PierDeck - 0.03f, z + 0.12f), new Vector3(3.2f, 0.06f, 0.23f),
                    Quaternion.Euler(0f, (float)(rng.NextDouble() - 0.5) * 1.2f, 0f), c);
            }
            foreach (float x in new[] { -1.5f, 1.5f })
            {
                frame.Box(new Vector3(x, PierDeck - 0.12f, 1f), new Vector3(0.12f, 0.12f, 10.4f), Quaternion.identity, darkCol);
                for (float z = -4f; z <= 6.1f; z += 2.5f)
                {
                    float bed = Lake.HeightAt(x, z);
                    frame.Tube(new Vector3(x, bed - 0.3f, z), new Vector3(x, PierDeck + 0.05f, z), 0.11f, 0.1f, 10, darkCol, darkCol, true);
                }
                // railing on the far half (open at the casting end and at the shore)
                for (float z = -1f; z <= 4.1f; z += 2.5f)
                    frame.Box(new Vector3(x, PierDeck + 0.45f, z), new Vector3(0.07f, 0.9f, 0.07f), Quaternion.identity, darkCol);
                frame.Box(new Vector3(x, PierDeck + 0.9f, 1.5f), new Vector3(0.06f, 0.06f, 5.2f), Quaternion.identity, darkCol);
                frame.Box(new Vector3(x, PierDeck + 0.6f, 1.5f), new Vector3(0.05f, 0.05f, 5.2f), Quaternion.identity, darkCol);
            }
            // cross beams under the deck
            foreach (float z in new[] { -3.5f, -1f, 1.5f, 4f })
                frame.Box(new Vector3(0f, PierDeck - 0.27f, z), new Vector3(3f, 0.1f, 0.12f), Quaternion.identity, darkCol);

            // festoon lights: masts behind the casting spot and a post at the shore, ropes sagging between them
            float top = PierDeck;
            foreach (var (x, z, h) in new[] { (-1.42f, -0.4f, 4.4f), (1.42f, -0.4f, 4.4f), (-1.42f, 5.6f, 3.0f) })
                frame.Box(new Vector3(x, top + h / 2f, z), new Vector3(0.1f, h, 0.1f), Quaternion.identity, darkCol);
            frame.Box(new Vector3(1.42f, top + 0.95f, 5.6f), new Vector3(0.09f, 1.9f, 0.09f), Quaternion.identity, darkCol);
            var tops = new[] { new Vector3(-1.42f, top + 4.4f, -0.4f), new Vector3(1.42f, top + 4.4f, -0.4f), new Vector3(1.42f, top + 2.0f, 5.6f), new Vector3(-1.42f, top + 3.0f, 5.6f) };
            var rope = new MeshKit();
            var bulbs = new MeshKit();
            var ropeCol = Col.Lin(0.25f, 0.18f, 0.12f);
            foreach (var (a, b) in new[] { (tops[0], tops[1]), (tops[1], tops[2]), (tops[0], tops[3]) })
            {
                Vector3 P(float t) => Vector3.Lerp(a, b, t) + Vector3.down * (0.45f * 4f * t * (1f - t));
                const int segments = 12;
                for (int i = 0; i < segments; i++)
                    rope.Tube(P(i / (float)segments), P((i + 1) / (float)segments), 0.012f, 0.012f, 4, ropeCol, ropeCol);
                int count = (int)(Vector3.Distance(a, b) / 0.5f);
                for (int i = 1; i < count; i++)
                {
                    // a small round bulb under a dark socket on the wire (the boxes read as yellow cardboard by day)
                    var at = P(i / (float)count);
                    rope.Tube(at, at + Vector3.down * 0.045f, 0.012f, 0.011f, 6, ropeCol, ropeCol, true);
                    bulbs.Blob(at + Vector3.down * 0.075f, new Vector3(0.026f, 0.034f, 0.026f), i, 0f, Color.white, Color.white, true, 1);
                }
            }
            bulbs.Box(new Vector3(1.42f, top + 2.1f, 5.6f), new Vector3(0.22f, 0.26f, 0.22f), Quaternion.identity, Color.white);

            var deckPart = Part("Pier", deck.ToMesh("PierDeck"), wood ?? _lit, true);
            var framePart = Part("PierFrame", frame.ToMesh("PierFrame"), darkWood ?? _lit, true);
            // soaked and dirty from the waterline up (the weathering's damp foot, measured from the water)
            var block = new MaterialPropertyBlock();
            block.SetFloat("_BaseY", 0f);
            framePart.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            deckPart.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            Part("PierRope", rope.ToMesh("PierRope"), _lit, false);
            _bulbs ??= Materials.Unlit(new Color(1f, 0.86f, 0.5f));
            Part("PierBulbs", bulbs.ToMesh("PierBulbs"), _bulbs, false);
            var lamp = new GameObject("PierLamp");
            lamp.transform.SetParent(transform, false);
            lamp.transform.position = new Vector3(1.42f, top + 1.95f, 5.6f);
            _pierLamp = lamp.AddComponent<Light>();
            _pierLamp.type = LightType.Point;
            _pierLamp.color = new Color(1f, 0.75f, 0.45f);
            _pierLamp.range = 12f;
            _parts.Add(lamp);
        }

        /// <summary>The pier's bulbs dim by day and glow at night, its lantern lights the boards.</summary>
        public void SetNight(float night)
        {
            if (_bulbs != null)
                _bulbs.color = Color.Lerp(new Color(0.42f, 0.4f, 0.34f), new Color(2.4f, 1.75f, 0.8f), night);   // unlit glass by day
            if (_pierLamp != null)
            {
                _pierLamp.intensity = 1.2f * night;
                _pierLamp.enabled = night > 0.02f;
            }
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

                }
            return (solid, leaves, grass);
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
