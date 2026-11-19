using System.Collections.Generic;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>Builds meshes from code: vertices with colours (alpha = how much the wind moves them), quads and shapes.</summary>
    public sealed class MeshKit
    {
        public readonly List<Vector3> V = new();
        public readonly List<Vector3> N = new();
        public readonly List<Color> C = new();
        public readonly List<int> T = new();

        public int Count => V.Count;

        public int Add(Vector3 p, Vector3 n, Color c)
        {
            V.Add(p);
            N.Add(n);
            C.Add(c);
            return V.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            T.Add(a);
            T.Add(b);
            T.Add(c);
        }

        /// <summary>A flat-shaded quad (corners counter-clockwise seen from outside; Unity's front faces are clockwise).</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            var n = Vector3.Cross(b - a, d - a).normalized;
            int i = Add(a, n, col);
            Add(b, n, col);
            Add(c, n, col);
            Add(d, n, col);
            Tri(i, i + 1, i + 2);
            Tri(i, i + 2, i + 3);
        }

        /// <summary>A box centred at c, rotated by r.</summary>
        public void Box(Vector3 c, Vector3 size, Quaternion r, Color col)
        {
            var h = size * 0.5f;
            Vector3 P(float x, float y, float z) => c + r * new Vector3(x * h.x, y * h.y, z * h.z);
            Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), col);       // top
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), col);   // bottom
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), col);       // +z
            Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), col);   // -z
            Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), col);       // +x
            Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), col);   // -x
        }

        /// <summary>A tapered tube from a to b (smooth sides), open ends unless capped.</summary>
        public void Tube(Vector3 a, Vector3 b, float ra, float rb, int sides, Color ca, Color cb, bool cap = false)
        {
            var axis = (b - a).normalized;
            var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var up = Vector3.Cross(side, axis);
            int start = Count;
            for (int i = 0; i <= sides; i++)
            {
                float t = i / (float)sides * Mathf.PI * 2f;
                var dir = side * Mathf.Cos(t) + up * Mathf.Sin(t);
                Add(a + dir * ra, dir, ca);
                Add(b + dir * rb, dir, cb);
            }
            for (int i = 0; i < sides; i++)
            {
                int k = start + i * 2;
                Tri(k, k + 1, k + 3);
                Tri(k, k + 3, k + 2);
            }
            if (cap)
            {
                int centre = Add(b, axis, cb);
                for (int i = 0; i < sides; i++)
                {
                    float t0 = i / (float)sides * Mathf.PI * 2f, t1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                    int p0 = Add(b + (side * Mathf.Cos(t0) + up * Mathf.Sin(t0)) * rb, axis, cb);
                    int p1 = Add(b + (side * Mathf.Cos(t1) + up * Mathf.Sin(t1)) * rb, axis, cb);
                    Tri(centre, p1, p0);
                }
            }
        }

        /// <summary>A lumpy ball (an icosphere pushed in and out by noise): crowns, bushes, stones.</summary>
        public void Blob(Vector3 c, Vector3 radius, int seed, float lumpiness, Color col, Color dark, bool smooth = true, int detail = 2)
        {
            var (verts, tris) = Icosphere(detail);
            int start = Count;
            var rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 100f, oz = (float)rng.NextDouble() * 100f;
            var placed = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                var d = verts[i];
                float n = Col.Noise(d.x * 1.7f + ox, d.z * 1.7f + d.y * 0.9f + oz) - 0.5f;
                float n2 = Col.Noise(d.x * 4.1f + oz, d.y * 4.3f + ox) - 0.5f;
                float k = 1f + (n * 0.9f + n2 * 0.45f) * lumpiness;
                placed[i] = c + Vector3.Scale(d, radius) * k;
            }
            if (smooth)
            {
                for (int i = 0; i < verts.Count; i++)
                {
                    var d = verts[i];
                    // darker underneath and inside: crowns read as volumes
                    float shade = Mathf.Clamp01(0.5f + d.y * 0.5f);
                    var colour = Color.Lerp(dark, col, shade);
                    colour.a = col.a;
                    Add(placed[i], Vector3.Scale(d, new Vector3(1f / radius.x, 1f / radius.y, 1f / radius.z)).normalized, colour);
                }
                foreach (var (a, b, cc) in tris)
                    Tri(start + a, start + b, start + cc);
            }
            else
            {
                foreach (var (a, b, cc) in tris)
                {
                    var pa = placed[a];
                    var pb = placed[b];
                    var pc = placed[cc];
                    var n = Vector3.Cross(pb - pa, pc - pa).normalized;
                    float shade = Mathf.Clamp01(0.55f + n.y * 0.45f);
                    var colour = Color.Lerp(dark, col, shade);
                    int i = Add(pa, n, colour);
                    Add(pb, n, colour);
                    Add(pc, n, colour);
                    Tri(i, i + 1, i + 2);
                }
            }
        }

        private static readonly Dictionary<int, (List<Vector3>, List<(int, int, int)>)> Ico = new();

        private static (List<Vector3> Verts, List<(int, int, int)> Tris) Icosphere(int subdivisions)
        {
            lock (Ico)
                if (Ico.TryGetValue(subdivisions, out var cached))
                    return cached;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
                new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            for (int i = 0; i < v.Count; i++)
                v[i] = v[i].normalized;
            var f = new List<(int, int, int)>
            {
                (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
                (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var mid = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int m))
                        return m;
                    v.Add(((v[a] + v[b]) * 0.5f).normalized);
                    mid[key] = v.Count - 1;
                    return v.Count - 1;
                }
                var next = new List<(int, int, int)>();
                foreach (var (a, b, c) in f)
                {
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.Add((a, ab, ca));
                    next.Add((b, bc, ab));
                    next.Add((c, ca, bc));
                    next.Add((ab, bc, ca));
                }
                f = next;
            }
            // Unity draws a triangle whose Cross(b - a, c - a) points at the viewer: these faces already do outwards.
            lock (Ico)
                Ico[subdivisions] = (v, f);
            return (v, f);
        }

        public Mesh ToMesh(string name = "mesh")
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000)
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V);
            m.SetNormals(N);
            m.SetColors(C);
            m.SetTriangles(T, 0);
            m.RecalculateBounds();
            return m;
        }

        public void Clear()
        {
            V.Clear();
            N.Clear();
            C.Clear();
            T.Clear();
        }
    }
}
