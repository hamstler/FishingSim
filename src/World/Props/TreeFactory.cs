using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Graphics;

namespace FishingSim.World.Props;

/// <summary>One kind of tree or plant: a trunk mesh and a crown mesh, both tinted per instance.</summary>
public sealed record PlantMeshes(Mesh? Trunk, Mesh Crown, Color TrunkColor, float TrunkLift, float CrownLift, bool Needles = false);

/// <summary>
/// Meshes and materials for the vegetation. Crowns are clouds of alpha-tested leaf (or needle) cards arranged in the
/// shape of the old primitive crowns, so the trees keep their silhouettes but read as foliage up close.
/// </summary>
public static class TreeFactory
{
    private static readonly Dictionary<string, PlantMeshes> Cache = new();

    public static PlantMeshes Get(string kind)
    {
        if (Cache.TryGetValue(kind, out var cached))
            return cached;
        var meshes = kind switch
        {
            "spruce" => new PlantMeshes(Trunk(0.1f, 0.17f, 1.4f), SpruceCrown(), new Color(0.28f, 0.19f, 0.11f), 0.7f, 1.1f, true),
            "oak" => new PlantMeshes(Trunk(0.12f, 0.2f, 1.8f), RoundCrown(), new Color(0.28f, 0.19f, 0.11f), 0.9f, 2.3f),
            "birch" => new PlantMeshes(Trunk(0.06f, 0.1f, 3.2f), BirchCrown(), new Color(0.86f, 0.85f, 0.8f), 1.6f, 2.6f),
            "pine" => new PlantMeshes(Trunk(0.1f, 0.18f, 5.2f), PineCrown(), new Color(0.42f, 0.24f, 0.14f), 2.6f, 4.4f, true),
            "willow" => new PlantMeshes(Trunk(0.14f, 0.24f, 2.0f), WillowCrown(), new Color(0.3f, 0.22f, 0.14f), 1.0f, 2.4f),
            "bush" => new PlantMeshes(null, BushCrown(), Colors.White, 0f, 0.4f),
            "hazel" => new PlantMeshes(null, HazelCrown(), Colors.White, 0f, 0.7f),
            _ => new PlantMeshes(null, RoundCrown(), Colors.White, 0f, 0f),
        };
        Cache[kind] = meshes;
        return meshes;
    }

    private static CylinderMesh Trunk(float top, float bottom, float height) =>
        new() { TopRadius = top, BottomRadius = bottom, Height = height, RadialSegments = 9, Rings = 3 };

    // ------------------------------------------------------------------ materials
    private static ShaderMaterial? _leafMaterial, _needleMaterial, _grassMaterial;
    private static StandardMaterial3D? _barkMaterial, _rockMaterial;

    /// <summary>Leaf-card material (shaders/foliage.gdshader); wind and time are set by <see cref="SetWind"/>.</summary>
    public static ShaderMaterial FoliageMaterial(bool needles)
    {
        ref var cached = ref needles ? ref _needleMaterial : ref _leafMaterial;
        if (cached != null)
            return cached;
        cached = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/foliage.gdshader") };
        cached.SetShaderParameter("leaf_tex", ProceduralTextures.LeafCluster(needles));
        if (needles)
            cached.SetShaderParameter("translucency", new Color(0.25f, 0.35f, 0.2f));
        return cached;
    }

    public static ShaderMaterial GrassMaterial()
    {
        if (_grassMaterial != null)
            return _grassMaterial;
        _grassMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grass.gdshader") };
        _grassMaterial.SetShaderParameter("gust_noise", ProceduralTextures.Noise(61, 0.01f, 256, octaves: 3));
        return _grassMaterial;
    }

    /// <summary>Bark: furrowed texture with a matching normal map; the instance colour tints it (birch stays white).</summary>
    public static StandardMaterial3D BarkMaterial()
    {
        if (_barkMaterial != null)
            return _barkMaterial;
        var bark = ProceduralTextures.Bark();
        _barkMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            AlbedoTexture = bark,
            NormalEnabled = true,
            NormalTexture = ProceduralTextures.NormalFrom(bark, 6f),
            Uv1Scale = new Vector3(2f, 3f, 1f),
            Roughness = 0.95f,
        };
        return _barkMaterial;
    }

    public static StandardMaterial3D RockMaterial()
    {
        if (_rockMaterial != null)
            return _rockMaterial;
        var noise = ProceduralTextures.Noise(83, 0.05f, 256);
        _rockMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            AlbedoTexture = noise,
            NormalEnabled = true,
            NormalTexture = ProceduralTextures.Noise(83, 0.05f, 256, true, 14f),
            NormalScale = 1.4f,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.9f,
        };
        return _rockMaterial;
    }

    /// <summary>Animates every foliage and grass material (called once per frame by the scenery).</summary>
    public static void SetWind(float wind, float time)
    {
        foreach (var m in new[] { _leafMaterial, _needleMaterial, _grassMaterial })
        {
            m?.SetShaderParameter("wind", wind);
            m?.SetShaderParameter("time", time);
        }
    }

    // ------------------------------------------------------------------ leaf cards
    private readonly record struct Puff(Vector3 Centre, float Radius, float Squash);

    /// <summary>A cloud of leaf cards filling the given puffs (ellipsoids). Inner cards are darker (self-shadowing).</summary>
    private static ArrayMesh LeafCards(int seed, float cardSize, float density, params Puff[] puffs)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var centre = Vector3.Zero;
        foreach (var p in puffs)
            centre += p.Centre;
        centre /= puffs.Length;
        foreach (var p in puffs)
        {
            int count = Math.Max(6, (int)(p.Radius * p.Radius * density));
            for (int i = 0; i < count; i++)
            {
                var dir = RandomUnit(rng);
                float r = p.Radius * (0.45f + 0.55f * MathF.Sqrt(rng.Randf()));
                var pos = p.Centre + new Vector3(dir.X * r, dir.Y * r * p.Squash * 0.5f, dir.Z * r);
                float size = cardSize * rng.RandfRange(0.75f, 1.25f);
                var normal = (pos - centre * 0.6f).Normalized().Lerp(Vector3.Up, 0.25f).Normalized();
                float shade = 0.5f + 0.5f * Math.Clamp(r / p.Radius, 0f, 1f) * (0.75f + 0.25f * Math.Clamp(dir.Y + 0.5f, 0f, 1f));
                Card(st, pos, RandomUnit(rng), size, normal, new Color(shade, shade, shade), rng.Randf() * Mathf.Tau);
            }
        }
        return st.Commit();
    }

    /// <summary>Needle cards on the layers of a conifer: each layer is a cone of drooping branch cards.</summary>
    private static ArrayMesh ConiferCards(int seed, params (float Y, float Radius, float Height)[] layers)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float top = 0f;
        foreach (var l in layers)
            top = MathF.Max(top, l.Y + l.Height * 0.5f);
        foreach (var (y, radius, height) in layers)
        {
            int count = (int)(radius * radius * 26f) + 10;
            for (int i = 0; i < count; i++)
            {
                float a = rng.Randf() * Mathf.Tau;
                float t = rng.Randf();                       // 0 = bottom of the layer, 1 = tip
                float ring = radius * (1f - t) * rng.RandfRange(0.55f, 1f);
                var outward = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
                var pos = new Vector3(0f, y - height * 0.5f + t * height, 0f) + outward * ring * 0.6f;
                // Branch cards point outwards and droop a little; the card spans the branch and its width.
                var along = (outward - Vector3.Up * 0.35f).Normalized();
                var side = Vector3.Up.Cross(along).Normalized();
                var plane = along.Cross(side).Normalized();
                float size = MathF.Max(radius * 0.75f, 0.45f) * rng.RandfRange(0.8f, 1.2f);
                float shade = 0.45f + 0.55f * (ring / MathF.Max(radius, 0.01f)) * (0.7f + 0.3f * (pos.Y / MathF.Max(top, 0.01f)));
                var normal = (outward + Vector3.Up * 0.5f).Normalized();
                Card(st, pos + along * size * 0.35f, plane, size, normal, new Color(shade, shade, shade), rng.RandfRange(-0.3f, 0.3f));
            }
        }
        return st.Commit();
    }

    private static Vector3 RandomUnit(RandomNumberGenerator rng)
    {
        float z = rng.RandfRange(-1f, 1f), a = rng.Randf() * Mathf.Tau, r = MathF.Sqrt(1f - z * z);
        return new Vector3(r * MathF.Cos(a), z, r * MathF.Sin(a));
    }

    /// <summary>One quad facing <paramref name="facing"/>, rotated by <paramref name="spin"/>, with a shared lighting normal.</summary>
    private static void Card(SurfaceTool st, Vector3 centre, Vector3 facing, float size, Vector3 normal, Color shade, float spin)
    {
        var f = facing.Normalized();
        var u = (MathF.Abs(f.Y) > 0.9f ? Vector3.Right : Vector3.Up).Cross(f).Normalized();
        var v = f.Cross(u).Normalized();
        float c = MathF.Cos(spin), s = MathF.Sin(spin);
        var a = (u * c + v * s) * size * 0.5f;
        var b = (v * c - u * s) * size * 0.5f;
        Vector3[] corners = { centre - a - b, centre + a - b, centre + a + b, centre - a + b };
        Vector2[] uvs = { new(0, 1), new(1, 1), new(1, 0), new(0, 0) };
        foreach (int i in new[] { 0, 1, 2, 0, 2, 3 })
        {
            st.SetNormal(normal);
            st.SetColor(shade);
            st.SetUV(uvs[i]);
            st.AddVertex(corners[i]);
        }
    }

    private static ArrayMesh Merge(params (Mesh Mesh, Transform3D Transform)[] parts)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (mesh, transform) in parts)
            st.AppendFrom(mesh, 0, transform);
        return st.Commit();
    }

    private static Transform3D At(float x, float y, float z) => new(Basis.Identity, new Vector3(x, y, z));

    private static Mesh Cone(float radius, float height, int segments = 9) =>
        new CylinderMesh { TopRadius = 0f, BottomRadius = radius, Height = height, RadialSegments = segments, Rings = 1 };

    private static Mesh Blob(float radius, int segments = 9, int rings = 5, float squash = 1.7f) =>
        new SphereMesh { Radius = radius, Height = radius * squash, RadialSegments = segments, Rings = rings };

    private static ArrayMesh SpruceCrown() => ConiferCards(1,
        (0.85f, 1.4f, 1.7f), (1.8f, 1.1f, 1.6f), (2.75f, 0.8f, 1.5f), (3.5f, 0.5f, 1.2f));

    private static ArrayMesh RoundCrown() => LeafCards(2, 0.95f, 34f,
        new Puff(new Vector3(0, 0, 0), 1.25f, 1.7f),
        new Puff(new Vector3(0.7f, -0.3f, 0.35f), 0.9f, 1.7f),
        new Puff(new Vector3(-0.6f, -0.2f, -0.5f), 0.9f, 1.7f),
        new Puff(new Vector3(0.1f, 0.8f, -0.2f), 0.8f, 1.7f));

    private static ArrayMesh BirchCrown() => LeafCards(3, 0.7f, 40f,
        new Puff(new Vector3(0, 0.2f, 0), 0.75f, 1.6f),
        new Puff(new Vector3(0.45f, -0.5f, 0.15f), 0.6f, 1.6f),
        new Puff(new Vector3(-0.4f, -0.7f, -0.2f), 0.55f, 1.6f),
        new Puff(new Vector3(0.05f, 0.9f, 0.1f), 0.5f, 1.6f));

    private static ArrayMesh PineCrown() => ConiferCards(4,
        (-1.2f, 1.2f, 1.1f), (-0.4f, 1.5f, 1.2f), (0.4f, 1.3f, 1.1f), (1.1f, 0.8f, 1.0f));

    private static ArrayMesh WillowCrown() => LeafCards(5, 0.9f, 30f,
        new Puff(new Vector3(0, 0.2f, 0), 1.6f, 1.2f),
        new Puff(new Vector3(0, -1.0f, 0), 2.0f, 1.3f),
        new Puff(new Vector3(1.1f, -0.3f, 0.4f), 1.2f, 1.1f),
        new Puff(new Vector3(-1f, -0.4f, -0.5f), 1.2f, 1.1f));

    private static ArrayMesh BushCrown() => LeafCards(6, 0.55f, 46f,
        new Puff(new Vector3(0, 0.15f, 0), 0.55f, 1.6f),
        new Puff(new Vector3(0.45f, 0f, 0.2f), 0.4f, 1.6f),
        new Puff(new Vector3(-0.4f, 0f, -0.2f), 0.4f, 1.6f));

    /// <summary>Hazel: a tall, loose shrub of several stems (the forest's understorey).</summary>
    private static ArrayMesh HazelCrown() => LeafCards(7, 0.6f, 38f,
        new Puff(new Vector3(0, 0.55f, 0), 0.6f, 1.1f),
        new Puff(new Vector3(0.5f, 0.25f, 0.3f), 0.45f, 1.3f),
        new Puff(new Vector3(-0.45f, 0.35f, -0.3f), 0.5f, 1.2f),
        new Puff(new Vector3(0.1f, 1.0f, -0.2f), 0.4f, 1.3f));

    private static ArrayMesh? _fern, _rush, _iris, _lilyPad, _lilyFlower, _yellowLily, _pondweed;

    /// <summary>A fern: fronds arching out from the crown, each a stalk with paired leaflets (UV.y = 0 root, 1 tip).</summary>
    public static ArrayMesh Fern()
    {
        if (_fern != null)
            return _fern;
        var rng = new RandomNumberGenerator { Seed = 77 };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int fronds = 9, steps = 9;
        for (int f = 0; f < fronds; f++)
        {
            float yaw = f / (float)fronds * Mathf.Tau + rng.RandfRange(-0.2f, 0.2f);
            var dir = new Vector3(MathF.Cos(yaw), 0f, MathF.Sin(yaw));
            var side = new Vector3(-dir.Z, 0f, dir.X);
            float len = rng.RandfRange(0.55f, 0.8f), rise = rng.RandfRange(0.4f, 0.6f);
            Vector3 At(float t) => dir * len * t + Vector3.Up * (rise * MathF.Sin(MathF.PI * 0.75f * t) - 0.1f * t * t);
            for (int i = 0; i < steps; i++)
            {
                float t0 = i / (float)steps, t1 = (i + 1f) / steps;
                var a = At(t0);
                var b = At(t1);
                // Leaflets on both sides, longest in the middle of the frond.
                float w = 0.16f * MathF.Sin(MathF.PI * MathF.Min(t0 * 1.1f + 0.08f, 1f));
                foreach (float sgn in new[] { -1f, 1f })
                {
                    var tip = a.Lerp(b, 0.7f) + side * sgn * w + Vector3.Up * 0.02f;
                    var normal = (b - a).Cross(side * sgn).Normalized();
                    if (normal.Y < 0f)
                        normal = -normal;
                    foreach (var (pt, tt) in new[] { (a, t0), (b, t1), (tip, (t0 + t1) * 0.5f) })
                    {
                        st.SetNormal(normal.Lerp(Vector3.Up, 0.5f).Normalized());
                        st.SetUV(new Vector2(0.5f, tt));
                        st.AddVertex(pt);
                    }
                }
            }
        }
        _fern = st.Commit();
        return _fern;
    }

    /// <summary>Rushes at the waterline: a clump of thin, straight, dark stems.</summary>
    public static ArrayMesh Rush() => _rush ??= BladeClump(21, 16, 1.0f, 0.016f, 0.14f);

    /// <summary>Yellow flag iris: a fan of broad sword leaves (its flowers are blossoms placed on top).</summary>
    public static ArrayMesh IrisLeaves() => _iris ??= BladeClump(31, 9, 0.75f, 0.055f, 0.08f);

    /// <summary>
    /// A water-lily pad: a flat disc with its V-shaped notch, a slight dome, darker and reddish at the rim
    /// (vertex colours; the instance colour tints it). For shaders/floating.gdshader.
    /// </summary>
    public static ArrayMesh LilyPad() => _lilyPad ??= Pad(new Color(0.2f, 0.4f, 0.11f), new Color(0.34f, 0.26f, 0.1f), 1f, 0.2f);

    /// <summary>Pondweed: small olive oval leaves lying on the water, scattered in a clump.</summary>
    public static ArrayMesh Pondweed()
    {
        if (_pondweed != null)
            return _pondweed;
        var rng = new RandomNumberGenerator { Seed = 55 };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int k = 0; k < 7; k++)
        {
            var c = new Vector3(rng.RandfRange(-0.3f, 0.3f), 0.005f * k, rng.RandfRange(-0.3f, 0.3f));
            float yaw = rng.Randf() * Mathf.Tau;
            var ax = new Vector3(MathF.Cos(yaw), 0f, MathF.Sin(yaw));
            var az = new Vector3(-ax.Z, 0f, ax.X);
            float l = rng.RandfRange(0.08f, 0.13f), w = l * 0.38f;
            var col = new Color(0.36f, 0.4f, 0.16f).Lerp(new Color(0.5f, 0.36f, 0.16f), rng.Randf() * 0.5f);
            const int n = 10;
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.Tau, a1 = (i + 1f) / n * Mathf.Tau;
                foreach (var v in new[] { c, c + ax * MathF.Cos(a0) * l + az * MathF.Sin(a0) * w, c + ax * MathF.Cos(a1) * l + az * MathF.Sin(a1) * w })
                {
                    st.SetNormal(Vector3.Up);
                    st.SetColor(col);
                    st.AddVertex(v);
                }
            }
        }
        _pondweed = st.Commit();
        return _pondweed;
    }

    private static ArrayMesh Pad(Color centre, Color rim, float radius, float notch)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int seg = 28;
        float[] radii = { 0f, 0.35f, 0.7f, 0.9f, 1f };     // a narrow last ring: the rim's tint stays at the edge
        Vector3 P(float r, float a) => new(MathF.Cos(a) * r * radius, 0.012f * (1f - r * r), MathF.Sin(a) * r * radius);
        Color Col(float r, int i) => centre.Lerp(rim, Math.Clamp((r - 0.9f) / 0.1f, 0f, 1f)).Lightened(i % 2 == 0 ? 0.03f : 0f);
        float span = Mathf.Tau - notch * 2f;
        for (int i = 0; i < seg; i++)
        {
            float a0 = notch + span * i / seg, a1 = notch + span * (i + 1) / seg;
            for (int j = 0; j + 1 < radii.Length; j++)
            {
                float r0 = radii[j], r1 = radii[j + 1];
                var quad = new[] { (r0, a0, i), (r1, a0, i), (r1, a1, i + 1), (r0, a0, i), (r1, a1, i + 1), (r0, a1, i + 1) };
                foreach (var (r, a, k) in quad)
                {
                    st.SetNormal(new Vector3(-MathF.Cos(a) * r * 0.15f, 1f, -MathF.Sin(a) * r * 0.15f).Normalized());
                    st.SetColor(Col(r, k));     // faint veins: every other spoke a shade lighter
                    st.AddVertex(P(r, a));
                }
            }
        }
        return st.Commit();
    }

    /// <summary>A water-lily flower: two rings of pointed petals opening around a golden centre.</summary>
    public static ArrayMesh LilyFlower() => _lilyFlower ??= Flower(new Color(0.98f, 0.97f, 0.94f), new Color(0.95f, 0.8f, 0.82f), 10, 0.075f);

    /// <summary>A yellow water-lily (brandy-bottle): a smaller, rounder yellow cup.</summary>
    public static ArrayMesh YellowLily() => _yellowLily ??= Flower(new Color(0.98f, 0.84f, 0.2f), new Color(0.9f, 0.7f, 0.12f), 5, 0.04f);

    private static ArrayMesh Flower(Color tip, Color foot, int petals, float length)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (count, open, len, turn) in new[] { (petals, 0.55f, length, 0f), (petals - 2, 1.05f, length * 0.8f, 0.3f) })
        {
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.Tau + turn;
                var dir = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
                var side = new Vector3(-dir.Z, 0f, dir.X);
                // A petal leans out from upright by its ring's opening angle.
                var spine = (dir * MathF.Sin(open) + Vector3.Up * MathF.Cos(open)).Normalized();
                var root = Vector3.Up * 0.01f;
                var mid = root + spine * len * 0.5f;
                var top = root + spine * len;
                float w = len * 0.28f;
                var normal = spine.Cross(side).Normalized();
                if (normal.Y < 0f)
                    normal = -normal;
                foreach (var (v, c) in new[] { (root, foot), (mid - side * w, tip), (top, tip), (root, foot), (top, tip), (mid + side * w, tip) })
                {
                    st.SetNormal(normal);
                    st.SetColor(c);
                    st.AddVertex(v);
                }
            }
        }
        // The golden stamens.
        for (int i = 0; i < 8; i++)
        {
            float a0 = i / 8f * Mathf.Tau, a1 = (i + 1) / 8f * Mathf.Tau;
            foreach (var v in new[] { Vector3.Up * 0.03f, new Vector3(MathF.Cos(a0) * 0.014f, 0.018f, MathF.Sin(a0) * 0.014f), new Vector3(MathF.Cos(a1) * 0.014f, 0.018f, MathF.Sin(a1) * 0.014f) })
            {
                st.SetNormal(Vector3.Up);
                st.SetColor(new Color(0.98f, 0.78f, 0.2f));
                st.AddVertex(v);
            }
        }
        return st.Commit();
    }

    private static ShaderMaterial? _floating;

    /// <summary>The material of floating plants: they ride the lake's waves (shaders/floating.gdshader).</summary>
    public static ShaderMaterial FloatingMaterial() =>
        _floating ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/floating.gdshader") };

    /// <summary>A clump of curved grass blades (UV.y = 0 at the root, 1 at the tip) for shaders/grass.gdshader.</summary>
    public static ArrayMesh Tuft()
    {
        if (_tuft != null)
            return _tuft;
        _tuft = BladeClump(9, 7, 0.55f, 0.045f, 0.22f);
        return _tuft;
    }

    /// <summary>A clump of <paramref name="blades"/> tapering, bending blades within <paramref name="spread"/> metres.</summary>
    public static ArrayMesh BladeClump(int seed, int blades, float height, float width, float spread)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int segments = 4;
        for (int b = 0; b < blades; b++)
        {
            var root = new Vector3(rng.RandfRange(-spread, spread), 0f, rng.RandfRange(-spread, spread));
            float yaw = rng.Randf() * Mathf.Tau;
            var dir = new Vector3(MathF.Cos(yaw), 0f, MathF.Sin(yaw));
            var side = new Vector3(-dir.Z, 0f, dir.X);
            float h = height * rng.RandfRange(0.6f, 1.25f);
            float lean = rng.RandfRange(0.1f, 0.45f);
            var left = new Vector3[segments + 1];
            var right = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                var spine = root + Vector3.Up * h * t + dir * lean * h * t * t;
                float w = width * (1f - t * 0.92f);
                left[i] = spine - side * w * 0.5f;
                right[i] = spine + side * w * 0.5f;
            }
            var normal = (Vector3.Up * 0.3f + dir.Cross(Vector3.Up).Cross(dir) * 0.0f + side.Cross(Vector3.Up)).Normalized();
            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments, t1 = (i + 1) / (float)segments;
                (Vector3 P, float T)[] quad = { (left[i], t0), (right[i], t0), (right[i + 1], t1), (left[i], t0), (right[i + 1], t1), (left[i + 1], t1) };
                foreach (var (p, t) in quad)
                {
                    st.SetNormal(normal);
                    st.SetUV(new Vector2(0.5f, t));
                    st.AddVertex(p);
                }
            }
        }
        return st.Commit();
    }

    private static ArrayMesh? _tuft;

    /// <summary>A small blossom on a short stalk (the stalk is tinted along with the petals).</summary>
    public static SphereMesh Blossom() => new() { Radius = 0.09f, Height = 0.14f, RadialSegments = 6, Rings = 3 };

    public static SphereMesh Rock() => new() { Radius = 1f, Height = 2f, RadialSegments = 7, Rings = 4 };

    public static CylinderMesh Reed() => new() { TopRadius = 0.004f, BottomRadius = 0.014f, Height = 1.5f, RadialSegments = 4, Rings = 1 };

    /// <summary>A bulrush head: a six-sided cigar, rounded at both ends (a capsule would cost four times the triangles).</summary>
    public static ArrayMesh Cattail()
    {
        if (_cattail != null)
            return _cattail;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int sides = 6;
        (float Y, float R)[] rows = { (-0.085f, 0f), (-0.074f, 0.012f), (-0.05f, 0.017f), (0.05f, 0.017f), (0.074f, 0.012f), (0.085f, 0f) };
        Vector3 P(int row, int side)
        {
            float a = side * Mathf.Tau / sides;
            return new Vector3(MathF.Cos(a) * rows[row].R, rows[row].Y, MathF.Sin(a) * rows[row].R);
        }
        for (int r = 0; r + 1 < rows.Length; r++)
            for (int i = 0; i < sides; i++)
            {
                var (a, b, c, d) = (P(r, i), P(r, i + 1), P(r + 1, i + 1), P(r + 1, i));
                var n = new Vector3(a.X + c.X, 0f, a.Z + c.Z).Normalized() + Vector3.Up * (rows[r + 1].Y - rows[r].Y < 0.03f ? MathF.Sign(rows[r].Y) * 0.8f : 0f);
                st.SetNormal(n.Normalized());
                foreach (var v in new[] { a, b, c, a, c, d })     // clockwise seen from outside: Godot's front face
                    st.AddVertex(v);
            }
        return _cattail = st.Commit();
    }

    private static ArrayMesh? _cattail;
}
