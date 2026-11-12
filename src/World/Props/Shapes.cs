using System;
using System.Collections.Generic;
using Godot;

namespace FishingSim.World.Props;

/// <summary>
/// Meshes for the village with soft edges: boxes with rounded edges (no razor-sharp corners anywhere; the light rolls
/// over every edge), logs, and UVs in metres so a texture's real size can be set on the material.
/// </summary>
public static class Shapes
{
    private static readonly Dictionary<(int, int, int, int), ArrayMesh> Boxes = new();

    /// <summary>
    /// A box of <paramref name="size"/> whose edges are rounded with a radius of <paramref name="round"/> (at most a
    /// fifth of its thinnest side). Each face is cut into a flat middle and two-step bevels; UVs in metres.
    /// </summary>
    public static ArrayMesh RoundedBox(Vector3 size, float round = 0.035f)
    {
        float r = MathF.Min(round, MathF.Min(size.X, MathF.Min(size.Y, size.Z)) * 0.2f);
        var key = ((int)(size.X * 1000), (int)(size.Y * 1000), (int)(size.Z * 1000), (int)(r * 1000));
        lock (Boxes)
            if (Boxes.TryGetValue(key, out var cached))
                return cached;
        var half = size / 2f;
        var inner = half - new Vector3(r, r, r);
        float[] Steps(float h) => new[] { -h, -h + r * 0.3f, -h + r, h - r, h - r * 0.3f, h };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        // Six faces: (normal axis, u axis, v axis).
        (Vector3 N, Vector3 U, Vector3 V)[] faces =
        {
            (Vector3.Right, Vector3.Back, Vector3.Up), (Vector3.Left, Vector3.Forward, Vector3.Up),
            (Vector3.Up, Vector3.Right, Vector3.Back), (Vector3.Down, Vector3.Right, Vector3.Forward),
            (Vector3.Back, Vector3.Left, Vector3.Up), (Vector3.Forward, Vector3.Right, Vector3.Up),
        };
        foreach (var (n, u, v) in faces)
        {
            float hn = MathF.Abs(n.Dot(half)), hu = MathF.Abs(u.Dot(half)), hv = MathF.Abs(v.Dot(half));
            var us = Steps(hu);
            var vs = Steps(hv);
            var grid = new Vector3[us.Length, vs.Length];
            var norms = new Vector3[us.Length, vs.Length];
            for (int i = 0; i < us.Length; i++)
                for (int j = 0; j < vs.Length; j++)
                {
                    var p = n * hn + u * us[i] + v * vs[j];
                    var clamped = new Vector3(Math.Clamp(p.X, -inner.X, inner.X), Math.Clamp(p.Y, -inner.Y, inner.Y), Math.Clamp(p.Z, -inner.Z, inner.Z));
                    var d = p - clamped;
                    var dir = d.LengthSquared() > 1e-10f ? d.Normalized() : n;
                    grid[i, j] = clamped + dir * r;
                    norms[i, j] = dir;
                }
            for (int i = 0; i + 1 < us.Length; i++)
                for (int j = 0; j + 1 < vs.Length; j++)
                {
                    (int I, int J)[] quad = { (i, j), (i + 1, j), (i + 1, j + 1), (i, j), (i + 1, j + 1), (i, j + 1) };
                    // Godot's front faces turn clockwise seen from outside: order by the face's handedness.
                    bool flip = u.Cross(v).Dot(n) > 0f;
                    if (flip)
                        quad = new[] { quad[0], quad[2], quad[1], quad[3], quad[5], quad[4] };
                    foreach (var (qi, qj) in quad)
                    {
                        st.SetNormal(norms[qi, qj]);
                        st.SetUV(new Vector2(us[qi] + hu, hv - vs[qj]));
                        st.AddVertex(grid[qi, qj]);
                    }
                }
        }
        st.GenerateTangents();
        var mesh = st.Commit();
        lock (Boxes)
            Boxes[key] = mesh;
        return mesh;
    }

    /// <summary>Adds a log (a slightly uneven cylinder with flat ends) to <paramref name="st"/>, along <paramref name="axis"/>.</summary>
    public static void Log(SurfaceTool st, Vector3 centre, Vector3 axis, float length, float radius, int seed)
    {
        const int sides = 12;
        var a = axis.Normalized();
        var side = (MathF.Abs(a.Y) > 0.9f ? Vector3.Right : Vector3.Up).Cross(a).Normalized();
        var up = a.Cross(side).Normalized();
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919 + 13) };
        float wobble = rng.RandfRange(0.92f, 1.06f);
        Vector3 Ring(int i, float t, out Vector3 n)
        {
            float ang = i * Mathf.Tau / sides;
            float rr = radius * wobble * (1f + 0.04f * MathF.Sin(ang * 3f + seed)) * (1f - 0.06f * t);
            n = (side * MathF.Cos(ang) + up * MathF.Sin(ang)).Normalized();
            return centre + a * (length * (t - 0.5f)) + n * rr;
        }
        for (int i = 0; i < sides; i++)
        {
            var p00 = Ring(i, 0f, out var n0);
            var p10 = Ring(i + 1, 0f, out var n1);
            var p01 = Ring(i, 1f, out _);
            var p11 = Ring(i + 1, 1f, out _);
            foreach (var (p, n) in new[] { (p00, n0), (p11, n1), (p10, n1), (p00, n0), (p01, n0), (p11, n1) })
            {
                st.SetNormal(n);
                st.SetUV(new Vector2(0f, 0f));
                st.AddVertex(p);
            }
            // The cut ends.
            foreach (float t in new[] { 0f, 1f })
            {
                var c = centre + a * (length * (t - 0.5f));
                var q0 = Ring(i, t, out _);
                var q1 = Ring(i + 1, t, out _);
                var en = t > 0.5f ? a : -a;
                var tri = t > 0.5f ? new[] { c, q0, q1 } : new[] { c, q1, q0 };
                foreach (var p in tri)
                {
                    st.SetNormal(en);
                    st.SetUV(new Vector2(0f, 0f));
                    st.AddVertex(p);
                }
            }
        }
    }
}
