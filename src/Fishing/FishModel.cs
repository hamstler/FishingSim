using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Data;

namespace FishingSim.Fishing;

/// <summary>Procedural fish: lofted body, fins, eyes, coloured per species. The nose points along +Z.</summary>
public partial class FishModel : Node3D
{
    private const int Rings = 16;
    private const int Segments = 10;

    private Node3D? _tail;
    private float _time;

    public float Length { get; private set; } = 0.4f;

    /// <summary>Tail beat strength (0 = still).</summary>
    public float Wiggle { get; set; } = 1f;

    public static FishModel Create(FishSpecies species, float weight)
    {
        var model = new FishModel();
        model.Build(species, weight);
        return model;
    }

    private static float Profile(float t) => 0.12f * t + 0.88f * MathF.Pow(MathF.Sin(MathF.PI * MathF.Pow(t, 0.75f)), 0.8f);

    private static float SmoothStep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private void Build(FishSpecies sp, float weight)
    {
        Length = sp.LengthRef * MathF.Pow(MathF.Max(weight, 0.05f), 0.33f);
        float depth = Length * sp.BodyDepth;
        float width = depth * 0.55f;
        float zNose = Length * 0.5f;
        float zTail = -Length * 0.38f;
        float ZAt(float t) => Mathf.Lerp(zNose, zTail, t);

        // --- body ---
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i <= Rings; i++)
        {
            float t = (float)i / Rings;
            float z = ZAt(t);
            float f = Profile(t);
            for (int j = 0; j < Segments; j++)
            {
                float ang = Mathf.Tau * j / Segments;
                float sn = MathF.Sin(ang);
                st.SetUV(new Vector2(t, 1f - (sn * 0.5f + 0.5f)));   // U nose->tail, V back->belly (no seam)
                st.AddVertex(new Vector3(MathF.Cos(ang) * width * 0.5f * f, sn * depth * 0.5f * f, z));
            }
        }
        for (int i = 0; i < Rings; i++)
        {
            for (int j = 0; j < Segments; j++)
            {
                int a = i * Segments + j;
                int b = i * Segments + (j + 1) % Segments;
                int c = (i + 1) * Segments + j;
                int d = (i + 1) * Segments + (j + 1) % Segments;
                foreach (int idx in new[] { a, c, b, b, c, d })
                    st.AddIndex(idx);
            }
        }
        st.GenerateNormals();
        // Pixel-art skin (FishSkin): species colours and markings, read with nearest filtering.
        var bodyMaterial = new StandardMaterial3D
        {
            AlbedoTexture = Graphics.FishSkin.For(sp),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Toon,
            Roughness = 0.35f,
            Metallic = 0.1f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = bodyMaterial });

        var finMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon,
            Roughness = 0.7f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        Color fin = Graphics.FishSkin.Vivid(sp.FinColor, 1.5f);

        // --- tail fin on its own pivot so it can beat ---
        _tail = new Node3D { Position = new Vector3(0, 0, zTail) };
        AddChild(_tail);
        float tl = Length * 0.26f;
        var tst = new SurfaceTool();
        tst.Begin(Mesh.PrimitiveType.Triangles);
        Tri(tst, fin, new Vector3(0, depth * 0.06f, 0), new Vector3(0, depth * 0.5f, -tl), new Vector3(0, 0, -tl * 0.65f));
        Tri(tst, fin, new Vector3(0, depth * 0.06f, 0), new Vector3(0, 0, -tl * 0.65f), new Vector3(0, -depth * 0.06f, 0));
        Tri(tst, fin, new Vector3(0, -depth * 0.06f, 0), new Vector3(0, 0, -tl * 0.65f), new Vector3(0, -depth * 0.5f, -tl));
        tst.GenerateNormals();
        _tail.AddChild(new MeshInstance3D { Mesh = tst.Commit(), MaterialOverride = finMaterial });

        // --- dorsal, anal and pectoral fins ---
        var fst = new SurfaceTool();
        fst.Begin(Mesh.PrimitiveType.Triangles);
        float dp = sp.DorsalPos;
        float t0 = dp - 0.14f, t1 = MathF.Min(dp + 0.14f, 0.95f);
        float top0 = depth * 0.5f * Profile(t0) * 0.95f;
        float top1 = depth * 0.5f * Profile(t1) * 0.95f;
        float topM = depth * 0.5f * Profile(dp);
        Tri(fst, fin, new Vector3(0, top0, ZAt(t0)), new Vector3(0, top1, ZAt(t1)),
            new Vector3(0, topM + depth * 0.38f, ZAt(Mathf.Lerp(t0, t1, 0.75f))));
        const float ta = 0.72f;
        float bot = -depth * 0.5f * Profile(ta) * 0.95f;
        Tri(fst, fin, new Vector3(0, bot, ZAt(ta - 0.08f)), new Vector3(0, bot, ZAt(ta + 0.08f)),
            new Vector3(0, bot - depth * 0.25f, ZAt(ta + 0.1f)));
        foreach (float sx in new[] { -1f, 1f })
        {
            float px = sx * width * 0.5f * Profile(0.24f) * 0.9f;
            Tri(fst, fin, new Vector3(px, -depth * 0.12f, ZAt(0.22f)), new Vector3(px, -depth * 0.16f, ZAt(0.3f)),
                new Vector3(px + sx * width * 0.5f, -depth * 0.3f, ZAt(0.36f)));
        }
        fst.GenerateNormals();
        AddChild(new MeshInstance3D { Mesh = fst.Commit(), MaterialOverride = finMaterial });

        // --- eyes: golden iris with a black pupil ---
        var irisMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.78f, 0.25f), Roughness = 0.3f, DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon };
        var pupilMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.02f), Roughness = 0.1f };
        float er = MathF.Max(depth * 0.075f, 0.004f);
        foreach (float sx in new[] { -1f, 1f })
        {
            var eyePos = new Vector3(sx * width * 0.5f * Profile(0.09f) * 0.85f, depth * 0.1f, ZAt(0.09f));
            AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = er, Height = er * 2f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = irisMaterial,
                Position = eyePos,
            });
            AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = er * 0.55f, Height = er * 1.1f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = pupilMaterial,
                Position = eyePos + new Vector3(sx * er * 0.6f, 0f, er * 0.1f),
            });
        }
    }

    private static void Tri(SurfaceTool st, Color color, Vector3 a, Vector3 b, Vector3 c)
    {
        foreach (var v in new[] { a, b, c })
        {
            st.SetColor(color);
            st.AddVertex(v);
        }
    }

    private List<StandardMaterial3D>? _underwater;
    private Color _underwaterColor = new(-1, -1, -1, -1);

    /// <summary>
    /// Switches the fish to its under-the-surface look: tinted by the water and drawn after the (transparent, non
    /// depth-writing) water surface, so it shows through it while the lake bottom and the pier still hide it.
    /// <paramref name="clarity"/> 0..1 fades it out with depth.
    /// </summary>
    public void SetUnderwater(Color tint, float clarity)
    {
        if (_underwater == null)
        {
            _underwater = new List<StandardMaterial3D>();
            foreach (var mi in FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
            {
                if (mi.MaterialOverride is not StandardMaterial3D src)
                    continue;
                var m = (StandardMaterial3D)src.Duplicate();
                m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                m.RenderPriority = 1;
                mi.MaterialOverride = m;
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                _underwater.Add(m);
            }
        }
        // Quantised: touching a material uploads it to the GPU, so only do it when the look really changes.
        float a = MathF.Round((0.25f + Math.Clamp(clarity, 0f, 1f) * 0.7f) * 20f) / 20f;
        var c = new Color(tint.R, tint.G, tint.B, a);
        if (c == _underwaterColor)
            return;
        _underwaterColor = c;
        foreach (var m in _underwater)
            m.AlbedoColor = c;
    }

    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("fishmodel");
        _time += (float)delta;
        if (_tail != null)
            _tail.Rotation = new Vector3(0f, MathF.Sin(_time * 9f) * 0.45f * Wiggle, 0f);
        Rotation = new Vector3(Rotation.X, Rotation.Y, MathF.Sin(_time * 4.5f) * 0.04f * Wiggle);
    }
}
