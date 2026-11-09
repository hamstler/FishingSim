using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Data;

namespace FishingSim.Fishing;

/// <summary>
/// Procedural fish: lofted body wearing the species' painted skin (FishSkin), fan-shaped fins with a colour gradient,
/// glossy eyes. The nose points along +Z. Detailed fish (on the line, in the hand, in the atlas) get a fine mesh,
/// the high-resolution skin with its normal map and a wet clear-coat.
/// </summary>
public partial class FishModel : Node3D
{
    private int Rings => _detailed ? 48 : 18;
    private int Segments => _detailed ? 32 : 12;

    private Node3D? _tail;
    private float _time;

    public float Length { get; private set; } = 0.4f;

    /// <summary>Tail beat strength (0 = still).</summary>
    public float Wiggle { get; set; } = 1f;

    /// <summary>
    /// Builds a fish. <paramref name="detailed"/> = a close-up fish (hooked, held, in the atlas): a finer body mesh and
    /// the high-resolution painted skin; fish swimming in the lake use the light version.
    /// </summary>
    public static FishModel Create(FishSpecies species, float weight, bool detailed = false)
    {
        var model = new FishModel { _detailed = detailed };
        model.Build(species, weight);
        return model;
    }

    private bool _detailed;

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
        // Close the tail end of the body (the narrow peduncle where the tail fin sits).
        int capCentre = (Rings + 1) * Segments;
        st.SetUV(new Vector2(1f, 0.5f));
        st.AddVertex(new Vector3(0f, 0f, zTail - Length * 0.01f));
        for (int j = 0; j < Segments; j++)
        {
            int a = Rings * Segments + j, b = Rings * Segments + (j + 1) % Segments;
            st.AddIndex(a);
            st.AddIndex(capCentre);
            st.AddIndex(b);
        }
        st.GenerateNormals();
        var style = Graphics.FishSkin.StyleOf(sp);
        _species = sp;
        _bodyMaterial = new StandardMaterial3D
        {
            AlbedoTexture = Graphics.FishSkin.For(sp),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Burley,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
            Roughness = 0.34f,
            Metallic = 0.1f + 0.35f * style.Silver,
            MetallicSpecular = 0.6f,
            ClearcoatEnabled = _detailed,
            Clearcoat = 0.55f,
            ClearcoatRoughness = 0.18f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = _bodyMaterial });
        if (_detailed)
        {
            Graphics.FishSkin.Prewarm(sp);
            TryUpgradeSkin();
        }

        var finMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Burley,
            Roughness = 0.45f,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        Color fin = Graphics.FishSkin.Vivid(sp.FinColor, 1.5f);

        // --- tail fin on its own pivot so it can beat ---
        _tail = new Node3D { Position = new Vector3(0, 0, zTail) };
        AddChild(_tail);
        float tl = Length * 0.2f;
        var tst = new SurfaceTool();
        tst.Begin(Mesh.PrimitiveType.Triangles);
        var finBase = fin.Darkened(0.25f);
        var finTip = new Color(fin.Lightened(0.15f), 0.75f);
        int fanSteps = _detailed ? 14 : 6;
        for (int k = 0; k < fanSteps; k++)
        {
            // Outline of the tail from the upper lobe tip through the fork to the lower lobe tip.
            Vector3 Edge(float a)
            {
                float y = Mathf.Lerp(depth * 0.4f, -depth * 0.4f, a);
                float fork = 1f - 0.38f * MathF.Exp(-MathF.Pow((a - 0.5f) / 0.18f, 2f));
                return new Vector3(0, y, -tl * fork);
            }
            float a0 = (float)k / fanSteps, a1 = (float)(k + 1) / fanSteps;
            var root = new Vector3(0, Mathf.Lerp(depth * 0.06f, -depth * 0.06f, (a0 + a1) * 0.5f), 0);
            Tri(tst, root, Edge(a0), Edge(a1), finBase, finTip, finTip);
        }
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
        // Dorsal fin: a fan from the back up to a scalloped edge.
        int dSteps = _detailed ? 10 : 4;
        for (int k = 0; k < dSteps; k++)
        {
            float a0 = (float)k / dSteps, a1 = (float)(k + 1) / dSteps;
            Vector3 Base(float a) => new(0, depth * 0.5f * Profile(Mathf.Lerp(t0, t1, a)) * 0.95f, ZAt(Mathf.Lerp(t0, t1, a)));
            Vector3 Top(float a)
            {
                float hgt = depth * 0.38f * MathF.Sin(MathF.PI * MathF.Pow(a, 0.6f)) * (1f - 0.08f * MathF.Abs(MathF.Sin(a * 9f)));
                return Base(a) + new Vector3(0, hgt, -depth * 0.12f * a);
            }
            Tri(fst, Base(a0), Base(a1), Top(a1), finBase, finBase, finTip);
            Tri(fst, Base(a0), Top(a1), Top(a0), finBase, finTip, finTip);
        }
        const float ta = 0.72f;
        float bot = -depth * 0.5f * Profile(ta) * 0.95f;
        Tri(fst, new Vector3(0, bot, ZAt(ta - 0.08f)), new Vector3(0, bot, ZAt(ta + 0.08f)),
            new Vector3(0, bot - depth * 0.25f, ZAt(ta + 0.1f)), finBase, finBase, finTip);
        // Pelvic fins under the belly.
        float pv = -depth * 0.5f * Profile(0.45f) * 0.9f;
        foreach (float sx in new[] { -1f, 1f })
            Tri(fst, new Vector3(sx * width * 0.15f, pv, ZAt(0.42f)), new Vector3(sx * width * 0.15f, pv, ZAt(0.5f)),
                new Vector3(sx * width * 0.35f, pv - depth * 0.18f, ZAt(0.54f)), finBase, finBase, finTip);
        foreach (float sx in new[] { -1f, 1f })
        {
            float px = sx * width * 0.5f * Profile(0.24f) * 0.9f;
            Tri(fst, new Vector3(px, -depth * 0.12f, ZAt(0.22f)), new Vector3(px, -depth * 0.16f, ZAt(0.3f)),
                new Vector3(px + sx * width * 0.5f, -depth * 0.3f, ZAt(0.36f)), finBase, finBase, finTip);
        }
        fst.GenerateNormals();
        AddChild(new MeshInstance3D { Mesh = fst.Commit(), MaterialOverride = finMaterial });

        // --- eyes: golden iris with a black pupil ---
        var irisMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.78f, 0.25f), Roughness = 0.2f, Metallic = 0.3f, ClearcoatEnabled = true, Clearcoat = 1f };
        var pupilMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.01f, 0.01f, 0.01f), Roughness = 0.05f, ClearcoatEnabled = true, Clearcoat = 1f };
        int eyeSeg = _detailed ? 20 : 8;
        float er = MathF.Max(depth * 0.075f, 0.004f);
        foreach (float sx in new[] { -1f, 1f })
        {
            var eyePos = new Vector3(sx * width * 0.5f * Profile(0.09f) * 0.85f, depth * 0.1f, ZAt(0.09f));
            AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = er, Height = er * 2f, RadialSegments = eyeSeg, Rings = eyeSeg / 2 },
                MaterialOverride = irisMaterial,
                Position = eyePos,
            });
            AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = er * 0.55f, Height = er * 1.1f, RadialSegments = eyeSeg, Rings = eyeSeg / 2 },
                MaterialOverride = pupilMaterial,
                Position = eyePos + new Vector3(sx * er * 0.6f, 0f, er * 0.1f),
            });
        }
    }

    private static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
    {
        st.SetColor(ca); st.AddVertex(a);
        st.SetColor(cb); st.AddVertex(b);
        st.SetColor(cc); st.AddVertex(c);
    }

    private FishSpecies? _species;
    private StandardMaterial3D? _bodyMaterial;
    private bool _skinUpgraded;

    /// <summary>Puts the detailed skin and its normal map on once they have been painted (in the background).</summary>
    private void TryUpgradeSkin()
    {
        if (_skinUpgraded || _species == null || _bodyMaterial == null)
            return;
        var skin = Graphics.FishSkin.TryDetailed(_species);
        if (skin is not { } pair)
            return;
        _skinUpgraded = true;
        _bodyMaterial.AlbedoTexture = pair.Albedo;
        _bodyMaterial.NormalEnabled = true;
        _bodyMaterial.NormalTexture = pair.Normal;
        _bodyMaterial.NormalScale = 0.8f;
    }

    /// <summary>True once a detailed fish wears its high-resolution skin.</summary>
    public bool HasDetailedSkin => _skinUpgraded;

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
        if (_detailed && !_skinUpgraded)
            TryUpgradeSkin();
        if (_tail != null)
            _tail.Rotation = new Vector3(0f, MathF.Sin(_time * 9f) * 0.45f * Wiggle, 0f);
        Rotation = new Vector3(Rotation.X, Rotation.Y, MathF.Sin(_time * 4.5f) * 0.04f * Wiggle);
    }
}
