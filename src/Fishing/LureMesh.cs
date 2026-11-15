using System;
using System.Collections.Generic;
using Godot;

namespace FishingSim.Fishing;

/// <summary>
/// Geometry for the lure models: lofted bodies (a side-view profile of half width, top and bottom along the length,
/// superelliptic sections, UVs along and round the body for <see cref="Graphics.LurePainter"/>), dished metal plates
/// (spoons and blades) with a painted face and a polished back, tapered wire tubes, and hardware made like the real
/// thing: treble hooks with a forged bend, a needle point and a barb, split rings, screw eyes.
/// </summary>
public static class LureMesh
{
    /// <summary>
    /// A body profile: at each station t (0 nose .. 1 tail, in body lengths) the half width and the top and bottom of
    /// the section above and below the line through the line tie, all in body lengths.
    /// </summary>
    public sealed class Profile
    {
        public readonly float[] T, W, Top, Bot;
        public float Length;
        public float Exponent = 2.2f;
        public Func<float, float, float>? Ribs;     // (t, theta) -> radial scale

        public Profile(float length, params float[] rows)
        {
            Length = length;
            int n = rows.Length / 4;
            T = new float[n]; W = new float[n]; Top = new float[n]; Bot = new float[n];
            for (int i = 0; i < n; i++)
            {
                T[i] = rows[i * 4]; W[i] = rows[i * 4 + 1]; Top[i] = rows[i * 4 + 2]; Bot[i] = rows[i * 4 + 3];
            }
        }

        private static float Cat(float[] t, float[] y, float x)
        {
            int n = t.Length;
            if (x <= t[0]) return y[0];
            if (x >= t[n - 1]) return y[n - 1];
            int i = 0;
            while (i < n - 2 && x > t[i + 1]) i++;
            float s = (x - t[i]) / (t[i + 1] - t[i]);
            float y0 = i > 0 ? y[i - 1] : y[i] - (y[i + 1] - y[i]), y1 = y[i], y2 = y[i + 1];
            float y3 = i + 2 < n ? y[i + 2] : y2 + (y2 - y1);
            // Catmull-Rom, clamped so a section never goes negative.
            float v = 0.5f * (2 * y1 + (-y0 + y2) * s + (2 * y0 - 5 * y1 + 4 * y2 - y3) * s * s + (-y0 + 3 * y1 - 3 * y2 + y3) * s * s * s);
            return v;
        }

        public float HalfWidth(float t) => MathF.Max(Cat(T, W, t), 0f) * Length;
        public float TopAt(float t) => Cat(T, Top, t) * Length;
        public float BotAt(float t) => Cat(T, Bot, t) * Length;

        /// <summary>A point on the surface: theta 0 = belly, pi/2 = right flank (+X), pi = back.</summary>
        public Vector3 Point(float t, float theta, float nose = 0f)
        {
            float w = HalfWidth(t), top = TopAt(t), bot = BotAt(t);
            float c = (top + bot) * 0.5f, hh = MathF.Max((top - bot) * 0.5f, 0f);
            float k = Ribs?.Invoke(t, theta) ?? 1f;
            float e = 2f / Exponent;
            float s = MathF.Sin(theta), co = MathF.Cos(theta);
            float sx = MathF.Sign(s) * MathF.Pow(MathF.Abs(s), e), sy = MathF.Sign(co) * MathF.Pow(MathF.Abs(co), e);
            return new Vector3(w * sx * k, c - hh * sy * k, -nose - t * Length);
        }

        /// <summary>Girth over length at the widest point (for the painter's round scales and spots).</summary>
        public float Aspect()
        {
            float best = 0f;
            for (int i = 0; i < T.Length; i++)
                best = MathF.Max(best, MathF.PI * (W[i] + (Top[i] - Bot[i]) * 0.5f));
            return best;
        }
    }

    /// <summary>The lofted body between stations t0 and t1, with closing caps where asked (a popper's cup: concave).</summary>
    public static ArrayMesh Loft(Profile p, float t0 = 0f, float t1 = 1f, int rings = 48, int sides = 32, float nose = 0f,
        bool capStart = true, bool capEnd = true, float startCup = 0f)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var pos = new Vector3[(rings + 1) * (sides + 1)];
        var uv = new Vector2[pos.Length];
        for (int r = 0; r <= rings; r++)
        {
            // Cosine spacing: dense at the rounded nose and tail.
            float a = r / (float)rings;
            float t = t0 + (t1 - t0) * (0.5f - 0.5f * MathF.Cos(MathF.PI * a));
            for (int s = 0; s <= sides; s++)
            {
                float th = s / (float)sides * Mathf.Tau;
                pos[r * (sides + 1) + s] = p.Point(t, th, nose);
                uv[r * (sides + 1) + s] = new Vector2(t, s / (float)sides);
            }
        }
        var nrm = new Vector3[pos.Length];
        for (int r = 0; r <= rings; r++)
            for (int s = 0; s <= sides; s++)
            {
                int i = r * (sides + 1) + s;
                var dth = pos[r * (sides + 1) + (s == sides ? 1 : s + 1)] - pos[r * (sides + 1) + (s == 0 ? sides - 1 : s - 1)];
                var dt = pos[Math.Min(r + 1, rings) * (sides + 1) + s] - pos[Math.Max(r - 1, 0) * (sides + 1) + s];
                var n = dth.Cross(dt);
                if (n.LengthSquared() < 1e-14f)
                    n = r < rings / 2 ? Vector3.Back : Vector3.Forward;
                n = n.Normalized();
                // Outward: away from the section's centre line.
                float t = uv[i].X;
                var centre = new Vector3(0f, (p.TopAt(t) + p.BotAt(t)) * 0.5f, pos[i].Z);
                if (n.Dot(pos[i] - centre) < 0f)
                    n = -n;
                nrm[i] = n;
            }
        for (int i = 0; i < pos.Length; i++)
        {
            st.SetNormal(nrm[i]);
            st.SetUV(uv[i]);
            st.AddVertex(pos[i]);
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = r * (sides + 1) + s, b = a + 1, c = a + sides + 1, d = c + 1;
                Tri(st, pos, nrm, a, c, b);
                Tri(st, pos, nrm, b, c, d);
            }
        int count = pos.Length;
        void Cap(int ring, float cup, bool start)
        {
            float t = uv[ring * (sides + 1)].X;
            var centre = new Vector3(0f, (p.TopAt(t) + p.BotAt(t)) * 0.5f, pos[ring * (sides + 1)].Z + (start ? -cup : cup));
            var outward = start ? Vector3.Back : Vector3.Forward;
            st.SetNormal(outward);
            st.SetUV(new Vector2(t, 0.5f));
            st.AddVertex(centre);
            int ci = count++;
            for (int s = 0; s < sides; s++)
            {
                int a = ring * (sides + 1) + s, b = a + 1;
                var n = (pos[b] - pos[a]).Cross(centre - pos[a]);
                if (n.Dot(outward) > 0f)
                {
                    st.AddIndex(a); st.AddIndex(ci); st.AddIndex(b);
                }
                else
                {
                    st.AddIndex(a); st.AddIndex(b); st.AddIndex(ci);
                }
            }
        }
        if (capStart && p.HalfWidth(t0) > 1e-5f)
            Cap(0, startCup, true);
        if (capEnd && p.HalfWidth(t1) > 1e-5f)
            Cap(rings, 0f, false);
        st.GenerateTangents();
        return st.Commit();
    }

    private static void TriOrDegenerate(SurfaceTool st, Vector3[] pos, Vector3[] nrm, int a, int b, int c)
    {
        var fn = (pos[b] - pos[a]).Cross(pos[c] - pos[a]);
        if (fn.LengthSquared() < 1e-20f)
            return;
        Tri(st, pos, nrm, a, b, c);
    }

    private static void Tri(SurfaceTool st, Vector3[] pos, Vector3[] nrm, int a, int b, int c)
    {
        // Godot's front faces are clockwise: the face normal (b-a)x(c-a) must point inwards.
        var fn = (pos[b] - pos[a]).Cross(pos[c] - pos[a]);
        var n = nrm[a] + nrm[b] + nrm[c];
        if (fn.Dot(n) > 0f)
            (b, c) = (c, b);
        st.AddIndex(a); st.AddIndex(b); st.AddIndex(c);
    }

    /// <summary>
    /// A dished plate along -Z from the origin: half width <paramref name="halfWidth"/>(u) x <paramref name="width"/>,
    /// dished by <paramref name="dish"/> (the face bulges towards +Y), thickness <paramref name="thick"/>. Surface 0 is
    /// the convex (painted) face, surface 1 the concave back and the edge.
    /// </summary>
    public static ArrayMesh Plate(Func<float, float> halfWidth, float length, float width, float dish, float thick, int nu = 28, int nv = 14)
    {
        var mesh = new ArrayMesh();
        for (int face = 0; face < 2; face++)
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            float off = face == 0 ? thick * 0.5f : -thick * 0.5f;
            Vector3 P(float u, float v)
            {
                float hw = halfWidth(u) * width;
                float x = (v * 2f - 1f) * hw;
                float across = hw > 1e-6f ? x / (width) : 0f;
                float y = dish * (MathF.Sin(MathF.PI * u) * 0.6f + 0.4f) * (1f - 4f * across * across) + off;
                return new Vector3(x, y, -u * length);
            }
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    float u = 0.5f - 0.5f * MathF.Cos(MathF.PI * i / nu), v = j / (float)nv;
                    var p = P(u, v);
                    var du = P(MathF.Min(u + 0.01f, 1f), v) - P(MathF.Max(u - 0.01f, 0f), v);
                    var dv = P(u, MathF.Min(v + 0.02f, 1f)) - P(u, MathF.Max(v - 0.02f, 0f));
                    var n = du.Cross(dv);
                    if (n.LengthSquared() < 1e-16f) n = Vector3.Up;
                    n = n.Normalized();
                    if ((face == 0) != (n.Y > 0f)) n = -n;
                    st.SetNormal(n);
                    st.SetUV(new Vector2(u, v));
                    st.AddVertex(p);
                    pos.Add(p);
                    nrm.Add(n);
                }
            var pa = pos.ToArray();
            var na = nrm.ToArray();
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    int a = i * (nv + 1) + j, b = a + 1, c = a + nv + 1, d = c + 1;
                    TriOrDegenerate(st, pa, na, a, b, c);
                    TriOrDegenerate(st, pa, na, b, d, c);
                }
            if (face == 1)
            {
                // The rim: a strip between the two faces round the outline.
                int baseIdx = (nu + 1) * (nv + 1);
                var outline = new List<(float U, float V)>();
                for (int i = 0; i <= nu; i++) outline.Add((0.5f - 0.5f * MathF.Cos(MathF.PI * i / nu), 1f));
                for (int i = nu; i >= 0; i--) outline.Add((0.5f - 0.5f * MathF.Cos(MathF.PI * i / nu), 0f));
                for (int k = 0; k < outline.Count; k++)
                {
                    var (u, v) = outline[k];
                    var p = P(u, v);
                    var n = new Vector3(v > 0.5f ? 1f : -1f, 0f, 0f);
                    st.SetNormal(n); st.SetUV(new Vector2(u, v)); st.AddVertex(p);
                    st.SetNormal(n); st.SetUV(new Vector2(u, v)); st.AddVertex(p + new Vector3(0f, thick, 0f));
                }
                for (int k = 0; k + 1 < outline.Count; k++)
                {
                    int a = baseIdx + k * 2, b = a + 1, c = a + 2, d = a + 3;
                    st.AddIndex(a); st.AddIndex(b); st.AddIndex(c);
                    st.AddIndex(b); st.AddIndex(d); st.AddIndex(c);
                    st.AddIndex(a); st.AddIndex(c); st.AddIndex(b);
                    st.AddIndex(b); st.AddIndex(c); st.AddIndex(d);
                }
            }
            st.GenerateTangents();
            st.Commit(mesh);
        }
        return mesh;
    }

    /// <summary>A tube along a polyline whose radius changes along it (a hook's point, a hair, a leg).</summary>
    public static void TubeInto(SurfaceTool st, IReadOnlyList<Vector3> pts, Func<float, float> radius, int sides = 6)
    {
        int n = pts.Count;
        var tangents = new Vector3[n];
        for (int i = 0; i < n; i++)
            tangents[i] = (pts[Math.Min(i + 1, n - 1)] - pts[Math.Max(i - 1, 0)]).Normalized();
        // Parallel-transported frame, so the tube does not twist.
        var side = tangents[0].Cross(MathF.Abs(tangents[0].Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
        var rings = new List<(Vector3 P, Vector3 S, Vector3 U, float R)>();
        for (int i = 0; i < n; i++)
        {
            var t = tangents[i];
            side = (side - t * side.Dot(t)).Normalized();
            var up = t.Cross(side);
            rings.Add((pts[i], side, up, radius(i / (float)(n - 1))));
        }
        for (int i = 0; i + 1 < n; i++)
        {
            var a = rings[i];
            var b = rings[i + 1];
            for (int k = 0; k < sides; k++)
            {
                float a0 = k / (float)sides * Mathf.Tau, a1 = (k + 1) / (float)sides * Mathf.Tau;
                var na0 = a.S * MathF.Cos(a0) + a.U * MathF.Sin(a0);
                var na1 = a.S * MathF.Cos(a1) + a.U * MathF.Sin(a1);
                var nb0 = b.S * MathF.Cos(a0) + b.U * MathF.Sin(a0);
                var nb1 = b.S * MathF.Cos(a1) + b.U * MathF.Sin(a1);
                var pa0 = a.P + na0 * a.R; var pa1 = a.P + na1 * a.R;
                var pb0 = b.P + nb0 * b.R; var pb1 = b.P + nb1 * b.R;
                Quad(st, pa0, pb0, pb1, pa1, na0, nb0, nb1, na1);
            }
        }
    }

    private static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
    {
        void V(Vector3 p, Vector3 n) { st.SetNormal(n); st.AddVertex(p); }
        var fn = (b - a).Cross(c - a);
        if (fn.Dot(na + nb + nc) > 0f)
        {
            V(a, na); V(c, nc); V(b, nb);
            V(a, na); V(d, nd); V(c, nc);
        }
        else
        {
            V(a, na); V(b, nb); V(c, nc);
            V(a, na); V(c, nc); V(d, nd);
        }
    }

    public static ArrayMesh Tube(IReadOnlyList<Vector3> pts, Func<float, float> radius, int sides = 6)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        TubeInto(st, pts, radius, sides);
        return st.Commit();
    }

    /// <summary>Points along a circular arc in the plane of <paramref name="a"/> and <paramref name="b"/> round a centre.</summary>
    public static List<Vector3> Arc(Vector3 centre, Vector3 a, Vector3 b, float radius, float from, float to, int steps)
    {
        var list = new List<Vector3>();
        for (int i = 0; i <= steps; i++)
        {
            float t = from + (to - from) * i / steps;
            list.Add(centre + (a * MathF.Cos(t) + b * MathF.Sin(t)) * radius);
        }
        return list;
    }

    private static readonly Dictionary<string, ArrayMesh> Hardware = new();

    /// <summary>
    /// A treble hook of unit gap hanging down (-Y) from its eye at the origin: a straight shank, three forged bends
    /// 120 degrees apart, needle points turned in a little, each with a barb.
    /// </summary>
    public static ArrayMesh Treble()
    {
        if (Hardware.TryGetValue("treble", out var m))
            return m;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const float wire = 0.055f, shank = 1.15f, bend = 0.33f;
        // The eye.
        TubeInto(st, Arc(new Vector3(0f, -0.12f, 0f), Vector3.Up, Vector3.Forward, 0.12f, 0f, Mathf.Tau, 14), _ => wire * 0.8f, 6);
        TubeInto(st, new[] { new Vector3(0f, -0.24f, 0f), new Vector3(0f, -shank, 0f) }, _ => wire * 1.25f, 8);
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * Mathf.Tau + 0.5f;
            var d = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
            var s = new Vector3(0f, -shank, 0f);
            var c = s + d * bend;
            var pts = Arc(c, -d, Vector3.Down, bend, 0f, MathF.PI, 12);
            var top = c + d * bend;
            pts.Add(top + Vector3.Up * 0.3f - d * 0.03f);
            pts.Add(top + Vector3.Up * 0.62f - d * 0.1f);
            int n = pts.Count;
            TubeInto(st, pts, f => f < 0.8f ? wire : wire * (1f - (f - 0.8f) / 0.2f * 0.88f), 6);
            // The barb, on the inside of the point.
            var b0 = top + Vector3.Up * 0.42f - d * 0.06f;
            TubeInto(st, new[] { b0, b0 + (Vector3.Down * 0.16f - d * 0.1f) }, f => wire * 0.7f * (1f - f * 0.9f), 4);
        }
        m = st.Commit();
        Hardware["treble"] = m;
        return m;
    }

    /// <summary>A single hook of unit gap, eye at the origin, shank along -Z, the point turned up (+Y).</summary>
    public static ArrayMesh Single(float shank = 2.2f)
    {
        string key = $"single{shank:0.0}";
        if (Hardware.TryGetValue(key, out var m))
            return m;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const float wire = 0.06f, bend = 0.5f;
        var pts = new List<Vector3> { Vector3.Zero, new(0f, 0f, -shank) };
        var c = new Vector3(0f, bend, -shank);
        pts.AddRange(Arc(c, Vector3.Down, Vector3.Back * -1f, bend, 0.15f, MathF.PI, 12));
        var top = c + Vector3.Up * bend;
        pts.Add(top + new Vector3(0f, 0.05f, 0.5f));
        pts.Add(top + new Vector3(0f, 0.12f, 0.9f));
        TubeInto(st, pts, f => f < 0.85f ? wire : wire * (1f - (f - 0.85f) / 0.15f * 0.9f), 6);
        var b0 = top + new Vector3(0f, 0.08f, 0.7f);
        TubeInto(st, new[] { b0, b0 + new Vector3(0f, -0.2f, -0.12f) }, f => wire * 0.7f * (1f - f * 0.9f), 4);
        m = st.Commit();
        Hardware[key] = m;
        return m;
    }

    /// <summary>A split ring of unit radius in the XY plane (two coils of flat wire seen as one thick ring).</summary>
    public static ArrayMesh Ring(float wire = 0.16f)
    {
        string key = $"ring{wire:0.00}";
        if (Hardware.TryGetValue(key, out var m))
            return m;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var pts = new List<Vector3>();
        for (int i = 0; i <= 40; i++)
        {
            float a = i / 40f * Mathf.Tau * 1.9f;
            pts.Add(new Vector3(MathF.Cos(a), MathF.Sin(a), (i / 40f - 0.5f) * wire * 2.2f));
        }
        TubeInto(st, pts, _ => wire, 6);
        m = st.Commit();
        Hardware[key] = m;
        return m;
    }
}
