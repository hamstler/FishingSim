using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace FishingSim.Game
{
    /// <summary>A plant's meshes (World/Props/TreeFactory's PlantMeshes): trunk (centred), crown, bark colour, lifts.</summary>
    public sealed record PlantMeshes(Mesh? Trunk, Mesh Crown, Color TrunkColor, float TrunkLift, float CrownLift, bool Needles = false);

    /// <summary>
    /// World/Props/TreeFactory ported: trunks with a root flare, crowns of leaf cards in puffs (oak, birch, willow,
    /// bushes, hazel) or drooping needle cards in layers (spruce, pine), their far versions (a quarter of the cards at
    /// twice the size, six-sided trunks), boulders, and the photographed meadow clump cards.
    /// </summary>
    public static class Plants
    {
        private static readonly Dictionary<string, PlantMeshes> Cache = new(), FarCache = new();
        private static float _cardDensity = 1f, _cardScale = 1f;
        private static int _trunkSides = 12, _trunkRings = 8;

        public static PlantMeshes GetFar(string kind)
        {
            if (FarCache.TryGetValue(kind, out var cached))
                return cached;
            var near = Get(kind);
            Cache.Remove(kind);
            (_cardDensity, _cardScale, _trunkSides, _trunkRings) = (0.22f, 1.9f, 6, 2);
            try
            {
                return FarCache[kind] = Get(kind);
            }
            finally
            {
                (_cardDensity, _cardScale, _trunkSides, _trunkRings) = (1f, 1f, 12, 8);
                Cache[kind] = near;
            }
        }

        public static PlantMeshes Get(string kind)
        {
            if (Cache.TryGetValue(kind, out var cached))
                return cached;
            var m = kind switch
            {
                "spruce" => new PlantMeshes(Trunk(0.1f, 0.17f, 1.4f), SpruceCrown(), new Color(0.28f, 0.19f, 0.11f), 0.7f, 1.1f, true),
                "oak" => new PlantMeshes(Trunk(0.12f, 0.2f, 1.8f), RoundCrown(), new Color(0.28f, 0.19f, 0.11f), 0.9f, 2.3f),
                "birch" => new PlantMeshes(Trunk(0.06f, 0.1f, 3.2f), BirchCrown(), new Color(0.86f, 0.85f, 0.8f), 1.6f, 2.6f),
                "pine" => new PlantMeshes(Trunk(0.1f, 0.18f, 5.2f), PineCrown(), new Color(0.42f, 0.24f, 0.14f), 2.6f, 4.4f, true),
                "willow" => new PlantMeshes(Trunk(0.14f, 0.24f, 2.0f), WillowCrown(), new Color(0.3f, 0.22f, 0.14f), 1.0f, 2.4f),
                "bush" => new PlantMeshes(null, BushCrown(), Color.white, 0f, 0.4f),
                "hazel" => new PlantMeshes(null, HazelCrown(), Color.white, 0f, 0.7f),
                _ => new PlantMeshes(null, RoundCrown(), Color.white, 0f, 0f),
            };
            return Cache[kind] = m;
        }

        private static Mesh Trunk(float top, float bottom, float height)
        {
            int sides = _trunkSides, rings = _trunkRings;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            float girth = Mathf.PI * 2f * bottom;
            float Radius(float t) => Mathf.Lerp(bottom, top, t) * (1f + 0.45f * Mathf.Exp(-t * height / 0.18f));
            for (int r = 0; r <= rings; r++)
            {
                float t = Mathf.Pow(r / (float)rings, 1.5f);
                float rad = Radius(t);
                float slope = (Radius(Mathf.Max(t - 0.01f, 0f)) - Radius(Mathf.Min(t + 0.01f, 1f))) / (0.02f * height);
                for (int i = 0; i <= sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    v.Add(dir * rad + Vector3.up * (t - 0.5f) * height);
                    n.Add((dir + Vector3.up * slope).normalized);
                    uv.Add(new Vector2(i / (float)sides, t * height / girth));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int i = 0; i < sides; i++)
                {
                    int a = r * (sides + 1) + i, b = a + 1, c = a + sides + 1, d = c + 1;
                    tri.Add(a); tri.Add(c); tri.Add(b);
                    tri.Add(b); tri.Add(c); tri.Add(d);
                }
            var mesh = new Mesh { name = "Trunk" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private readonly struct Puff
        {
            public readonly Vector3 Centre;
            public readonly float Radius, Squash;
            public Puff(Vector3 c, float r, float s) { Centre = c; Radius = r; Squash = s; }
        }

        private sealed class CardBuilder
        {
            public readonly List<Vector3> V = new();
            public readonly List<Vector3> N = new();
            public readonly List<Vector2> UV = new();
            public readonly List<Color> C = new();
            public readonly List<int> T = new();

            /// <summary>One quad facing <paramref name="facing"/>, turned by <paramref name="spin"/>, sharing a lighting normal.</summary>
            public void Card(Vector3 centre, Vector3 facing, float size, Vector3 normal, float shade, float spin, int atlasCell = -1)
            {
                var f = facing.normalized;
                var u = Vector3.Cross(Mathf.Abs(f.y) > 0.9f ? Vector3.right : Vector3.up, f).normalized;
                var w = Vector3.Cross(f, u).normalized;
                float c = Mathf.Cos(spin), s = Mathf.Sin(spin);
                var a = (u * c + w * s) * size * 0.5f;
                var b = (w * c - u * s) * size * 0.5f;
                Vector3[] corners = { centre - a - b, centre + a - b, centre + a + b, centre - a + b };
                Vector2[] uvs = { new(0, 1), new(1, 1), new(1, 0), new(0, 0) };
                int start = V.Count;
                for (int i = 0; i < 4; i++)
                {
                    V.Add(corners[i]);
                    N.Add(normal);
                    C.Add(new Color(shade, shade, shade, shade));
                    var g = atlasCell < 0 ? uvs[i] : (uvs[i] + new Vector2(atlasCell % 2, atlasCell / 2)) * 0.5f;
                    UV.Add(new Vector2(g.x, 1f - g.y));   // the painted atlas is stored bottom-up
                }
                T.Add(start); T.Add(start + 1); T.Add(start + 2);
                T.Add(start); T.Add(start + 2); T.Add(start + 3);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                if (V.Count > 65000)
                    m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetUVs(0, UV);
                m.SetColors(C);
                m.SetTriangles(T, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        private static Vector3 RandomUnit(Random rng)
        {
            float z = (float)(rng.NextDouble() * 2 - 1), a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }

        private static float Rf(Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        private static Mesh LeafCards(int seed, int leaf, float cardSize, float density, params Puff[] puffs)
        {
            // Smaller clusters on the close mesh keep individual leaf silhouettes visible at the shore.
            if (_cardDensity == 1f && leaf != LeafPainter.LeafHazel)
            {
                cardSize *= 0.76f;
                density *= 1.6f;
            }
            var rng = new Random(seed);
            var cb = new CardBuilder();
            var centre = Vector3.zero;
            foreach (var p in puffs)
                centre += p.Centre;
            centre /= puffs.Length;
            foreach (var p in puffs)
            {
                int count = Math.Max(4, (int)(p.Radius * p.Radius * density * _cardDensity));
                for (int i = 0; i < count; i++)
                {
                    var dir = RandomUnit(rng);
                    float r = p.Radius * (0.45f + 0.55f * Mathf.Sqrt((float)rng.NextDouble()));
                    var pos = p.Centre + new Vector3(dir.x * r, dir.y * r * p.Squash * 0.5f, dir.z * r);
                    float size = cardSize * _cardScale * Rf(rng, 0.75f, 1.25f);
                    var normal = Vector3.Lerp((pos - centre * 0.6f).normalized, Vector3.up, 0.25f).normalized;
                    float shade = 0.5f + 0.5f * Mathf.Clamp01(r / p.Radius) * (0.75f + 0.25f * Mathf.Clamp01(dir.y + 0.5f));
                    cb.Card(pos, RandomUnit(rng), size, normal, shade, (float)rng.NextDouble() * Mathf.PI * 2f, leaf);
                }
            }
            return cb.ToMesh("Crown");
        }

        private static Mesh ConiferCards(int seed, params (float Y, float Radius, float Height)[] layers)
        {
            var rng = new Random(seed);
            var cb = new CardBuilder();
            float top = 0f;
            foreach (var l in layers)
                top = Mathf.Max(top, l.Y + l.Height * 0.5f);
            foreach (var (y, radius, height) in layers)
            {
                int count = (int)((radius * radius * 26f + 10) * _cardDensity) + 3;
                for (int i = 0; i < count; i++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float t = (float)rng.NextDouble();
                    float ring = radius * (1f - t) * Rf(rng, 0.55f, 1f);
                    var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var pos = new Vector3(0f, y - height * 0.5f + t * height, 0f) + outward * ring * 0.6f;
                    var along = (outward - Vector3.up * 0.35f).normalized;
                    var side = Vector3.Cross(Vector3.up, along).normalized;
                    var plane = Vector3.Cross(along, side).normalized;
                    float size = Mathf.Max(radius * 0.75f, 0.45f) * _cardScale * Rf(rng, 0.8f, 1.2f);
                    float shade = 0.45f + 0.55f * (ring / Mathf.Max(radius, 0.01f)) * (0.7f + 0.3f * (pos.y / Mathf.Max(top, 0.01f)));
                    var normal = (outward + Vector3.up * 0.5f).normalized;
                    cb.Card(pos + along * size * 0.35f, plane, size, normal, shade, Rf(rng, -0.3f, 0.3f));
                }
            }
            return cb.ToMesh("Conifer");
        }

        private static Mesh SpruceCrown() => ConiferCards(1, (0.85f, 1.4f, 1.7f), (1.8f, 1.1f, 1.6f), (2.75f, 0.8f, 1.5f), (3.5f, 0.5f, 1.2f));

        private static Mesh RoundCrown() => LeafCards(2, LeafPainter.LeafOak, 0.95f, 34f,
            new Puff(new Vector3(0, 0, 0), 1.25f, 1.7f), new Puff(new Vector3(0.7f, -0.3f, 0.35f), 0.9f, 1.7f),
            new Puff(new Vector3(-0.6f, -0.2f, -0.5f), 0.9f, 1.7f), new Puff(new Vector3(0.1f, 0.8f, -0.2f), 0.8f, 1.7f));

        private static Mesh BirchCrown() => LeafCards(3, LeafPainter.LeafBirch, 0.7f, 40f,
            new Puff(new Vector3(0, 0.2f, 0), 0.75f, 1.6f), new Puff(new Vector3(0.45f, -0.5f, 0.15f), 0.6f, 1.6f),
            new Puff(new Vector3(-0.4f, -0.7f, -0.2f), 0.55f, 1.6f), new Puff(new Vector3(0.05f, 0.9f, 0.1f), 0.5f, 1.6f));

        private static Mesh PineCrown() => ConiferCards(4, (-1.2f, 1.2f, 1.1f), (-0.4f, 1.5f, 1.2f), (0.4f, 1.3f, 1.1f), (1.1f, 0.8f, 1.0f));

        private static Mesh WillowCrown() => LeafCards(5, LeafPainter.LeafWillow, 0.9f, 30f,
            new Puff(new Vector3(0, 0.2f, 0), 1.6f, 1.2f), new Puff(new Vector3(0, -1.0f, 0), 2.0f, 1.3f),
            new Puff(new Vector3(1.1f, -0.3f, 0.4f), 1.2f, 1.1f), new Puff(new Vector3(-1f, -0.4f, -0.5f), 1.2f, 1.1f));

        private static Mesh BushCrown() => LeafCards(6, LeafPainter.LeafHazel, 0.55f, 46f,
            new Puff(new Vector3(0, 0.15f, 0), 0.55f, 1.6f), new Puff(new Vector3(0.45f, 0f, 0.2f), 0.4f, 1.6f), new Puff(new Vector3(-0.4f, 0f, -0.2f), 0.4f, 1.6f));

        private static Mesh HazelCrown() => LeafCards(7, LeafPainter.LeafHazel, 0.6f, 38f,
            new Puff(new Vector3(0, 0.55f, 0), 0.6f, 1.1f), new Puff(new Vector3(0.5f, 0.25f, 0.3f), 0.45f, 1.3f),
            new Puff(new Vector3(-0.45f, 0.35f, -0.3f), 0.5f, 1.2f), new Puff(new Vector3(0.1f, 1.0f, -0.2f), 0.4f, 1.3f));

        private static readonly Mesh?[] BoulderCache = new Mesh?[3];

        /// <summary>A boulder of unit radius: an icosphere pushed in and out by noise, its foot flat in the ground.</summary>
        public static Mesh Boulder(int variant)
        {
            variant = Mathf.Clamp(variant, 0, 2);
            if (BoulderCache[variant] != null)
                return BoulderCache[variant]!;
            var k = new MeshKit();
            k.Blob(Vector3.zero, Vector3.one, 300 + variant, 0.55f, Color.white, Color.white, true, 2);
            var v = k.V;
            for (int i = 0; i < v.Count; i++)
                if (v[i].y < -0.25f)
                    v[i] = new Vector3(v[i].x, -0.25f + (v[i].y + 0.25f) * 0.25f, v[i].z);
            var mesh = k.ToMesh("Boulder");
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            return BoulderCache[variant] = mesh;
        }

        /// <summary>Three crossed vertical cards (0, 60, 120 degrees), 0.72 m wide, 0.36 m high; UV v runs up.</summary>
        public static Mesh MeadowCards()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            const float half = 0.36f, height = 0.36f;
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI / 3f;
                var across = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * half;
                var facing = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                var nn = (Vector3.up * 0.8f + facing * 0.2f).normalized;
                int s = v.Count;
                v.Add(-across); v.Add(-across + Vector3.up * height); v.Add(across + Vector3.up * height); v.Add(across);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
                for (int i = 0; i < 4; i++) n.Add(nn);
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
                t.Add(s); t.Add(s + 2); t.Add(s + 3);
            }
            var m = new Mesh { name = "MeadowCards" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        // ---------------------------------------------------------------- small plants (FS/Plant: vertex colour x tint)
        private static readonly Dictionary<string, Mesh> Small = new();

        public static Mesh SmallPlant(string kind)
        {
            if (Small.TryGetValue(kind, out var m))
                return m;
            var k = new MeshKit();
            var rng = new Random(kind.GetHashCode() & 0xFFFF);
            switch (kind)
            {
                case "reed":
                    for (int i = 0; i < 3; i++)
                    {
                        var lean = new Vector3(R(rng, -0.12f, 0.12f), 0f, R(rng, -0.12f, 0.12f));
                        LeafBlade(k, new Vector3(R(rng, -0.04f, 0.04f), 0f, R(rng, -0.04f, 0.04f)), Vector3.up * R(rng, 1.3f, 1.55f) + lean, 0.014f, 0.75f);
                    }
                    break;
                case "cattail":
                    k.Tube(new Vector3(0f, 0f, 0f), new Vector3(0f, 0.13f, 0f), 0.022f, 0.02f, 6, new Color(1f, 1f, 1f, 0.15f), new Color(1f, 1f, 1f, 0.1f), true);
                    k.Tube(new Vector3(0f, 0.13f, 0f), new Vector3(0f, 0.2f, 0f), 0.004f, 0.002f, 4, new Color(1f, 1f, 1f, 0.1f), new Color(1f, 1f, 1f, 0f));
                    break;
                case "rush":
                    for (int i = 0; i < 14; i++)
                    {
                        float a = R(rng, 0f, Mathf.PI * 2f), r = R(rng, 0f, 0.09f);
                        var root = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                        var tip = root * 3f + Vector3.up * R(rng, 0.5f, 0.9f);
                        LeafBlade(k, root, tip, 0.006f, R(rng, 0.8f, 1f));
                    }
                    break;
                case "iris":
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i / 7f * Mathf.PI * 2f + R(rng, -0.2f, 0.2f);
                        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        LeafBlade(k, dir * 0.02f, dir * R(rng, 0.12f, 0.22f) + Vector3.up * R(rng, 0.55f, 0.8f), 0.022f, R(rng, 0.85f, 1f));
                    }
                    break;
                case "irisflower":
                    Flower(k, rng, 3, 0.85f, 0.07f, 0.045f);
                    break;
                case "flower":
                    Flower(k, rng, 5, 0.32f, 0.035f, 0.03f);
                    break;
                case "tuft":
                    for (int i = 0; i < 9; i++)
                    {
                        float a = R(rng, 0f, Mathf.PI * 2f), r = R(rng, 0f, 0.12f);
                        var root = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                        LeafBlade(k, root, root * 2.5f + Vector3.up * R(rng, 0.22f, 0.42f), 0.012f, R(rng, 0.75f, 1f));
                    }
                    break;
                case "fern":
                    for (int i = 0; i < 7; i++)
                        Frond(k, i / 7f * Mathf.PI * 2f + R(rng, -0.25f, 0.25f), R(rng, 0.45f, 0.7f), R(rng, 0.35f, 0.6f));
                    break;
                case "lilypad":
                    Pad(k);
                    break;
                case "pondweed":
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i / 6f * Mathf.PI * 2f + R(rng, -0.3f, 0.3f);
                        FloatLeaf(k, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * R(rng, 0.05f, 0.25f), a + R(rng, -0.5f, 0.5f), R(rng, 0.07f, 0.11f), 0.025f,
                            new Color(0.34f, 0.36f, 0.14f, 1f));
                    }
                    break;
                case "lilyflower":
                    for (int ring = 0; ring < 2; ring++)
                        for (int i = 0; i < 8; i++)
                        {
                            float a = (i + ring * 0.5f) / 8f * Mathf.PI * 2f;
                            Petal(k, a, ring == 0 ? 0.075f : 0.055f, ring == 0 ? 0.02f : 0.045f, ring == 0 ? 0.35f : 0.8f, new Color(0.97f, 0.97f, 0.94f, 1f));
                        }
                    k.Tube(Vector3.zero, new Vector3(0f, 0.04f, 0f), 0.018f, 0.012f, 6, new Color(0.95f, 0.78f, 0.15f, 1f), new Color(0.98f, 0.85f, 0.2f, 1f), true);
                    break;
                case "stem":
                    // a tapered unit branch (CylinderMesh 0.03 / 0.11, height 1, centred), no sway
                    k.Tube(new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f), 0.11f, 0.03f, 7, new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f), true);
                    break;
                case "yellowlily":
                    for (int i = 0; i < 5; i++)
                        Petal(k, i / 5f * Mathf.PI * 2f, 0.035f, 0.005f, 1.1f, new Color(0.98f, 0.82f, 0.12f, 1f));
                    break;
            }
            var mesh = k.ToMesh(kind);
            return Small[kind] = mesh;
        }

        private static float R(Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// <summary>A tapering blade from root to tip (normals leaning up), the tip free in the wind.</summary>
        private static void LeafBlade(MeshKit k, Vector3 root, Vector3 tip, float width, float shade)
        {
            var dir = tip - root;
            var side = Vector3.Cross(dir, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.01f)
                side = Vector3.right;
            var n = Vector3.Lerp(Vector3.Cross(side, dir).normalized, Vector3.up, 0.5f).normalized;
            if (n.y < 0f)
                n = -n;
            var mid = root + dir * 0.5f + Vector3.Cross(dir.normalized, side) * dir.magnitude * 0.04f;
            Color c0 = new(shade * 0.55f, shade * 0.55f, shade * 0.55f, 1f), c1 = new(shade * 0.85f, shade * 0.85f, shade * 0.85f, 0.55f), c2 = new(shade, shade, shade, 0f);
            int a = k.Add(root - side * width, n, c0);
            k.Add(root + side * width, n, c0);
            k.Add(mid + side * width * 0.8f, n, c1);
            k.Add(mid - side * width * 0.8f, n, c1);
            k.Add(tip, n, c2);
            k.Tri(a, a + 3, a + 2);
            k.Tri(a, a + 2, a + 1);
            k.Tri(a + 3, a + 4, a + 2);
        }

        private static void Flower(MeshKit k, Random rng, int heads, float height, float radius, float spread)
        {
            var stem = new Color(0.25f, 0.38f, 0.14f, 1f);
            for (int h = 0; h < heads; h++)
            {
                var top = new Vector3(R(rng, -spread, spread), height * R(rng, 0.75f, 1f), R(rng, -spread, spread));
                LeafBlade(k, Vector3.zero, top, 0.004f, 0.6f);
                for (int i = 0; i < 2; i++)
                    k.C[k.C.Count - 1 - i] = stem;
                for (int p = 0; p < 5; p++)
                {
                    float a = p / 5f * Mathf.PI * 2f + R(rng, 0f, 1f);
                    var d = new Vector3(Mathf.Cos(a), 0.25f, Mathf.Sin(a)) * radius;
                    var s = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius * 0.45f;
                    var col = new Color(1f, 1f, 1f, 0.05f);
                    int c = k.Add(top, Vector3.up, col);
                    k.Add(top + d - s, Vector3.up, col);
                    k.Add(top + d * 1.15f, Vector3.up, col);
                    k.Add(top + d + s, Vector3.up, col);
                    k.Tri(c, c + 1, c + 2);
                    k.Tri(c, c + 2, c + 3);
                }
            }
        }

        private static void Frond(MeshKit k, float yaw, float length, float rise)
        {
            var dir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            var side = new Vector3(-dir.z, 0f, dir.x);
            const int steps = 8;
            int prev = -1;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                var p = dir * length * t + Vector3.up * (rise * Mathf.Sin(t * Mathf.PI * 0.8f) - 0.1f * t * t);
                float w = 0.11f * Mathf.Sin(Mathf.Min(t * 1.2f, 1f) * Mathf.PI) + 0.01f;
                float shade = 0.6f + 0.4f * t;
                var c = new Color(shade, shade, shade, 1f - t);
                var n = Vector3.Lerp(Vector3.up, -dir, 0.3f * t).normalized;
                int a = k.Add(p - side * w, n, c);
                k.Add(p, n + Vector3.up * 0.3f, c * 1.05f);
                k.Add(p + side * w, n, c);
                if (prev >= 0)
                {
                    k.Tri(prev, a, prev + 1); k.Tri(prev + 1, a, a + 1);
                    k.Tri(prev + 1, a + 1, prev + 2); k.Tri(prev + 2, a + 1, a + 2);
                }
                prev = a;
            }
        }

        /// <summary>A round lily pad of unit radius with its notch, flat on y = 0.</summary>
        private static void Pad(MeshKit k)
        {
            const int segs = 18;
            var green = new Color(0.2f, 0.36f, 0.12f, 1f);
            var rim = new Color(0.26f, 0.4f, 0.14f, 1f);
            int centre = k.Add(Vector3.zero, Vector3.up, green);
            int first = k.Count;
            for (int i = 0; i <= segs; i++)
            {
                float a = 0.25f + i / (float)segs * (Mathf.PI * 2f - 0.5f);
                k.Add(new Vector3(Mathf.Cos(a), 0.004f, Mathf.Sin(a)), Vector3.up, rim);
            }
            for (int i = 0; i < segs; i++)
                k.Tri(centre, first + i + 1, first + i);
        }

        private static void FloatLeaf(MeshKit k, Vector3 at, float yaw, float length, float width, Color c)
        {
            var d = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * length;
            var s = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * width;
            int a = k.Add(at, Vector3.up, c);
            k.Add(at + d * 0.5f + s, Vector3.up, c);
            k.Add(at + d, Vector3.up, c);
            k.Add(at + d * 0.5f - s, Vector3.up, c);
            k.Tri(a, a + 2, a + 1);
            k.Tri(a, a + 3, a + 2);
        }

        private static void Petal(MeshKit k, float yaw, float length, float lift, float cup, Color c)
        {
            var d = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            var s = new Vector3(-d.z, 0f, d.x) * length * 0.32f;
            var tip = d * length + Vector3.up * (lift + length * cup * 0.5f);
            var mid = d * length * 0.5f + Vector3.up * (lift + length * cup * 0.15f);
            var n = (Vector3.up - d * 0.3f).normalized;
            int a = k.Add(Vector3.up * lift, n, c);
            k.Add(mid + s, n, c);
            k.Add(tip, n, c);
            k.Add(mid - s, n, c);
            k.Tri(a, a + 2, a + 1);
            k.Tri(a, a + 3, a + 2);
        }
    }
}
