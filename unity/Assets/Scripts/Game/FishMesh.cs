using System.Collections.Generic;
using UnityEngine;
using FishingSim.Data;

namespace FishingSim.Game
{
    /// <summary>
    /// A fish built from its species' proportions (FishSpecies: body depth, dorsal position, back/belly/fin colours):
    /// a lofted body, tail, dorsal, anal and paired fins, eyes. Unit length along +Z (head forward), cached per species.
    /// </summary>
    public static class FishMesh
    {
        private static readonly Dictionary<string, Mesh> Cache = new();

        public static Mesh For(FishSpecies sp)
        {
            if (Cache.TryGetValue(sp.SpeciesName, out var m))
                return m;
            m = Build(sp);
            Cache[sp.SpeciesName] = m;
            return m;
        }

        private static Color C(FishingSim.Compat.Color c) => Col.Lin(c.R, c.G, c.B);

        private static Mesh Build(FishSpecies sp)
        {
            var k = new MeshKit();
            var back = C(sp.BackColor);
            var belly = C(sp.BellyColor);
            var fin = C(sp.FinColor);
            float depth = Mathf.Clamp(sp.BodyDepth, 0.08f, 0.45f);
            float width = depth * 0.42f;
            const int rings = 22, sides = 16;
            // body stations from the snout (z = 0.5) to the tail stalk (z = -0.36)
            float Profile(float t)
            {
                // t 0 snout .. 1 stalk: rises fast, deepest at ~0.35, narrows to the stalk
                float rise = Mathf.Sqrt(Mathf.Clamp01(t / 0.32f));
                float fall = 1f - Mathf.Pow(Mathf.Clamp01((t - 0.32f) / 0.68f), 1.4f) * 0.82f;
                return t < 0.32f ? Mathf.Lerp(0.12f, 1f, rise) : fall;
            }
            int start = k.Count;
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;
                float z = Mathf.Lerp(0.5f, -0.36f, t);
                float p = Profile(t);
                float hy = depth * 0.5f * p, hx = width * 0.5f * p;
                float centreY = (t - 0.4f) * depth * 0.06f;
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    float cy = Mathf.Cos(a), sx = Mathf.Sin(a);
                    var pos = new Vector3(sx * hx, centreY + cy * hy * (cy > 0 ? 1f : 0.9f), z);
                    var n = new Vector3(sx / Mathf.Max(hx, 0.001f), cy / Mathf.Max(hy, 0.001f), (t < 0.05f ? 1f : 0.15f)).normalized;
                    // countershading: dark back, pale belly, a soft lateral band
                    float up = cy * 0.5f + 0.5f;
                    var col = Color.Lerp(belly, back, Mathf.SmoothStep(0.25f, 0.8f, up));
                    float stripe = Mathf.Exp(-Mathf.Pow((cy - 0.05f) / 0.12f, 2f)) * 0.15f;
                    col = Color.Lerp(col, back * 0.8f, stripe);
                    k.Add(pos, n, col);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < sides; s++)
                {
                    int a = start + r * (sides + 1) + s, b = a + 1, c = a + sides + 1, d = c + 1;
                    k.Tri(a, b, c);
                    k.Tri(b, d, c);
                }
            // snout cap
            int tip = k.Add(new Vector3(0f, -depth * 0.02f, 0.515f), Vector3.forward, back);
            for (int s = 0; s < sides; s++)
                k.Tri(tip, start + s + 1, start + s);
            // tail fin: two lobes from the stalk
            float stalk = depth * 0.5f * Profile(1f);
            Fin(k, new Vector3(0, stalk, -0.36f), new Vector3(0, -stalk, -0.36f),
                new Vector3(0, -depth * 0.62f, -0.52f), new Vector3(0, depth * 0.62f, -0.52f), new Vector3(0, 0, -0.45f), fin);
            // dorsal fin
            float dz = Mathf.Lerp(0.15f, -0.15f, Mathf.Clamp01(sp.DorsalPos));
            float topY = depth * 0.5f;
            Fin(k, new Vector3(0, topY * 0.95f, dz + 0.12f), new Vector3(0, topY * 0.75f, dz - 0.1f),
                new Vector3(0, topY * 0.75f + depth * 0.35f, dz - 0.12f), new Vector3(0, topY + depth * 0.38f, dz + 0.06f), null, fin);
            // anal fin
            Fin(k, new Vector3(0, -topY * 0.7f, -0.12f), new Vector3(0, -topY * 0.55f, -0.24f),
                new Vector3(0, -topY * 0.55f - depth * 0.25f, -0.27f), new Vector3(0, -topY * 0.7f - depth * 0.25f, -0.16f), null, fin);
            // pectoral and pelvic fins
            foreach (float side in new[] { -1f, 1f })
            {
                Fin(k, new Vector3(side * width * 0.45f, -depth * 0.1f, 0.3f), new Vector3(side * width * 0.45f, -depth * 0.18f, 0.27f),
                    new Vector3(side * width * 1.4f, -depth * 0.42f, 0.14f), new Vector3(side * width * 1.1f, -depth * 0.3f, 0.16f), null, fin);
                Fin(k, new Vector3(side * width * 0.3f, -depth * 0.42f, 0.05f), new Vector3(side * width * 0.3f, -depth * 0.42f, 0.0f),
                    new Vector3(side * width * 0.9f, -depth * 0.62f, -0.08f), new Vector3(side * width * 0.8f, -depth * 0.58f, -0.03f), null, fin);
                // eye: a dark disc with a gold rim
                var eye = new Vector3(side * width * 0.36f, depth * 0.08f, 0.42f);
                k.Blob(eye, new Vector3(0.012f, depth * 0.075f, depth * 0.075f), 3, 0f, new Color(0.02f, 0.02f, 0.02f), Col.Lin(0.6f, 0.45f, 0.15f), true, 1);
            }
            var mesh = k.ToMesh(sp.SpeciesName);
            return mesh;
        }

        /// <summary>A two-sided fin quad (or a forked pentagon when <paramref name="fork"/> is given).</summary>
        private static void Fin(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3? fork, Color col)
        {
            var edge = col * 0.75f;
            edge.a = 1f;
            var n = Vector3.Cross(b - a, d - a).normalized;
            for (int side = 0; side < 2; side++)
            {
                var nn = side == 0 ? n : -n;
                int i = k.Add(a, nn, col);
                k.Add(b, nn, col);
                k.Add(c, nn, edge);
                k.Add(d, nn, edge);
                if (fork is { } f)
                {
                    int m = k.Add(f, nn, col);
                    if (side == 0) { k.Tri(i, i + 1, i + 2); k.Tri(i, m, i + 3); k.Tri(i, i + 2, m); }
                    else { k.Tri(i, i + 2, i + 1); k.Tri(i, i + 3, m); k.Tri(i, m, i + 2); }
                }
                else if (side == 0)
                {
                    k.Tri(i, i + 1, i + 2);
                    k.Tri(i, i + 2, i + 3);
                }
                else
                {
                    k.Tri(i, i + 2, i + 1);
                    k.Tri(i, i + 3, i + 2);
                }
            }
        }
    }
}
