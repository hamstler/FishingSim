using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Data;

namespace FishingSim.Fishing;

/// <summary>
/// Procedural fish built from the species' <see cref="FishBody"/>: a body lofted from separate back, belly and width
/// profiles (a deep, humped carp, a flat bream, a long pike with a duck-bill snout, a round eel, a broad-headed
/// catfish, a sturgeon with its rostrum), its own fins (soft, spiny, sail, long carp dorsal, adipose, continuous eel
/// fins, stickleback spines), a forked, notched, rounded or shark-like tail, barbels and eyes sized to the head.
/// It wears the species' painted skin (FishSkin). The nose points along +Z. Geometry is built at unit length and
/// cached per species, then scaled, so a lake full of fish costs one build per species. Detailed fish (on the line,
/// in the hand, in the atlas) get a fine mesh, the high-resolution skin with its normal map and a wet clear-coat.
/// </summary>
public partial class FishModel : Node3D
{
    /// <summary>Nose to tail base, as a fraction of the total length (the tail fin is the rest).</summary>
    private const float BodyFraction = 0.82f;

    private Node3D? _tail;
    private float _time;
    private bool _detailed;
    private FishSpecies? _species;
    private StandardMaterial3D? _bodyMaterial;
    private bool _skinUpgraded;

    public float Length { get; private set; } = 0.4f;

    /// <summary>Tail beat strength (0 = still).</summary>
    public float Wiggle { get; set; } = 1f;

    /// <summary>Eye diameter in metres (for tests: eyes must stay in proportion to the head).</summary>
    public float EyeDiameter { get; private set; }

    /// <summary>Body height at the eye in metres.</summary>
    public float HeadHeightAtEye { get; private set; }

    /// <summary>Deepest body height in metres.</summary>
    public float MaxHeight { get; private set; }

    /// <summary>Widest body width in metres.</summary>
    public float MaxWidth { get; private set; }

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

    // ================================================================== shape (unit length: total length = 1)
    /// <summary>The lofted shape of a species at unit length, shared by every fish of that species.</summary>
    private sealed class Shape
    {
        public ArrayMesh Body = null!, Fins = null!, Tail = null!;
        public ArrayMesh? Barbels;
        public SphereMesh EyeMesh = null!, PupilMesh = null!;
        public float TailZ;
        public Vector3 EyePos;
        public float EyeRadius, EyeHeadHeight, MaxHeight, MaxWidth;
    }

    private static readonly Dictionary<(string, bool), Shape> Shapes = new();

    private sealed class Profile
    {
        private readonly FishBody _b;
        private readonly float _k;           // scales the envelopes to the species' body depth
        private readonly float _eh, _ew;

        public Profile(FishBody b, float depth)
        {
            _b = b;
            _eh = 0.62f + 0.6f * b.Snout;
            _ew = 0.55f + 0.6f * MathF.Min(b.Snout, 1f) * (1f - 0.55f * b.FlatHead);
            float max = 0f;
            for (int i = 0; i <= 200; i++)
            {
                float t = i / 200f;
                max = MathF.Max(max, RawTop(t) + RawBottom(t));
            }
            _k = depth / MathF.Max(max, 1e-4f);
        }

        private float Env(float t, float exp)
        {
            float p = _b.PeakAt;
            if (t <= p)
                return MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * 0.5f * t / p), 0f), exp);
            float ped = _b.Peduncle;
            if (t > 0.94f)
                return ped * (1f + (t - 0.94f) / 0.06f * 0.22f);       // the tail base flares a little
            float u = Math.Clamp((t - p) / (0.94f - p), 0f, 1f);
            return ped + (1f - ped) * MathF.Pow(MathF.Max(MathF.Cos(MathF.PI * 0.5f * u), 0f), 1.25f);
        }

        private float Bump(float t) => MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * Math.Clamp(t / 0.9f, 0f, 1f)), 0f), 1.5f);

        private float HeadFlat(float t) => 1f - _b.FlatHead * 0.45f * MathF.Exp(-MathF.Pow((t - 0.07f) / 0.13f, 2f));

        private float RawTop(float t) => Env(t, _eh) * (1f + _b.Hump * Bump(t)) * HeadFlat(t);

        private float RawBottom(float t) => Env(t, _eh) * MathF.Max(1f - 0.35f * _b.Hump * Bump(t), 0.2f) * HeadFlat(t);

        /// <summary>Height of the back above the body axis (unit length).</summary>
        public float Top(float t) => _k * RawTop(t);

        /// <summary>Depth of the belly below the axis.</summary>
        public float Bottom(float t) => _k * RawBottom(t);

        /// <summary>Half width of the body.</summary>
        public float HalfWidth(float t)
        {
            float broad = 1f + _b.FlatHead * 0.7f * MathF.Exp(-MathF.Pow((t - 0.1f) / 0.14f, 2f))
                + _b.HeadBroad * MathF.Exp(-MathF.Pow((t - 0.14f) / 0.14f, 2f));
            return _b.Width * _k * Env(t, _ew) * (1f + 0.3f * _b.Hump * Bump(t)) * broad;
        }
    }

    private static Shape BuildShape(FishSpecies sp, bool detailed)
    {
        var b = FishBody.Of(sp);
        var shape = new Shape();
        float bodyLen = BodyFraction;
        float zNose = 0.5f;
        float Z(float t) => zNose - t * bodyLen;
        var prof = new Profile(b, sp.BodyDepth);
        shape.TailZ = Z(1f);

        // --- body: rings along the length, denser at the nose and the tail ---
        int rings = detailed ? 72 : 22, segs = detailed ? 40 : 14;
        float nb = 2f + 4f * b.BellyFlat;                 // flatter belly = squarer lower half of the cross-section
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i <= rings; i++)
        {
            float t = 0.5f - 0.5f * MathF.Cos(MathF.PI * i / rings);
            float top = prof.Top(t), bottom = prof.Bottom(t), half = prof.HalfWidth(t);
            shape.MaxHeight = MathF.Max(shape.MaxHeight, top + bottom);
            shape.MaxWidth = MathF.Max(shape.MaxWidth, 2f * half);
            for (int j = 0; j < segs; j++)
            {
                float a = Mathf.Tau * j / segs;
                float s = MathF.Sin(a), c = MathF.Cos(a);
                float n = s >= 0f ? 2f : nb;
                float x = half * MathF.Sign(c) * MathF.Pow(MathF.Abs(c), 2f / n);
                float y = (s >= 0f ? top : bottom) * MathF.Sign(s) * MathF.Pow(MathF.Abs(s), 2f / n);
                st.SetUV(new Vector2(t, 1f - (s * 0.5f + 0.5f)));   // U nose->tail, V back->belly (both flanks share it)
                st.AddVertex(new Vector3(x, y, Z(t)));
            }
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < segs; j++)
            {
                int a = i * segs + j, bb = i * segs + (j + 1) % segs, c = (i + 1) * segs + j, d = (i + 1) * segs + (j + 1) % segs;
                st.AddIndex(a); st.AddIndex(c); st.AddIndex(bb);
                st.AddIndex(bb); st.AddIndex(c); st.AddIndex(d);
            }
        int cap = (rings + 1) * segs;
        st.SetUV(new Vector2(1f, 0.5f));
        st.AddVertex(new Vector3(0f, 0f, Z(1f) - 0.004f));
        for (int j = 0; j < segs; j++)
        {
            st.AddIndex(rings * segs + j);
            st.AddIndex(cap);
            st.AddIndex(rings * segs + (j + 1) % segs);
        }
        st.GenerateNormals();
        shape.Body = st.Commit();

        // Fin vertex colours: a darker base fading to a lighter, translucent edge (tinted by the material's fin colour).
        Color white = new(0.72f, 0.72f, 0.72f), tipC = new(1f, 1f, 1f, 0.7f);
        Vector3 TopAt(float t, float lift = 0.97f) => new(0f, prof.Top(t) * lift, Z(t));
        Vector3 BottomAt(float t) => new(0f, -prof.Bottom(t) * 0.97f, Z(t));
        float depth = shape.MaxHeight;
        int steps = detailed ? 14 : 5;

        // --- fins (vertex colour = gradient from base to tip; the material tints them with the species' fin colour) ---
        var fst = new SurfaceTool();
        fst.Begin(Mesh.PrimitiveType.Triangles);
        void Strip(Func<float, Vector3> baseAt, Func<float, Vector3> tipAt, int n)
        {
            for (int k = 0; k < n; k++)
            {
                float a0 = (float)k / n, a1 = (float)(k + 1) / n;
                Quad(fst, baseAt(a0), baseAt(a1), tipAt(a1), tipAt(a0), white, white, tipC, tipC);
            }
        }
        float dAt = b.DorsalAt >= 0f ? b.DorsalAt : Math.Clamp(sp.DorsalPos, 0.25f, 0.8f);
        float d0 = Math.Clamp(dAt - b.DorsalLength / 2f, 0.12f, 0.95f), d1 = Math.Clamp(dAt + b.DorsalLength / 2f, 0.15f, 0.99f);
        float dh = b.DorsalHeight * depth;
        Func<float, float> dorsalShape = b.Dorsal switch
        {
            DorsalKind.Spiny => a => a < 0.5f
                ? (0.7f + 0.3f * MathF.Abs(MathF.Sin(a * MathF.PI * 9f))) * MathF.Sin(MathF.PI * MathF.Pow(a / 0.5f, 0.7f))
                : 0.8f * MathF.Sin(MathF.PI * MathF.Pow((a - 0.5f) / 0.5f, 0.6f)) + 0.12f,
            DorsalKind.Sail => a => MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * MathF.Pow(a, 0.5f)), 0f), 0.45f),
            DorsalKind.Long => a => a < 0.15f ? 0.4f + 0.6f * a / 0.15f : 1f - 0.6f * (a - 0.15f) / 0.85f,
            DorsalKind.Tiny => a => MathF.Sin(MathF.PI * a),
            DorsalKind.Continuous => a => 0.85f * MathF.Sin(MathF.PI * 0.5f * MathF.Min(a * 5f, 1f)),
            _ => a => MathF.Sin(MathF.PI * MathF.Pow(a, 0.6f)),
        };
        if (b.Dorsal == DorsalKind.Spines)
        {
            // Stickleback: free spines on the back, each with a little membrane.
            int count = b.DorsalLength > 0.5f ? 9 : 3;
            for (int k = 0; k < count; k++)
            {
                float t = Mathf.Lerp(d0, d1, count == 1 ? 0.5f : (float)k / (count - 1));
                var root = TopAt(t);
                float h = dh * (count == 3 && k == 2 ? 0.5f : 1f);
                Tri(fst, root, root + new Vector3(0, 0, -0.02f), root + new Vector3(0, h, -0.01f), white, white, white);
            }
        }
        else
        {
            float dEnd = b.Dorsal == DorsalKind.Continuous ? 0.995f : d1;
            Strip(a => TopAt(Mathf.Lerp(d0, dEnd, a)),
                a => TopAt(Mathf.Lerp(d0, dEnd, a)) + new Vector3(0f, dh * dorsalShape(a), -0.25f * dh * dorsalShape(a)),
                b.Dorsal is DorsalKind.Spiny or DorsalKind.Continuous ? steps * 2 : steps);
        }
        if (b.Adipose)
        {
            Strip(a => TopAt(Mathf.Lerp(0.84f, 0.9f, a)),
                a => TopAt(Mathf.Lerp(0.84f, 0.9f, a)) + new Vector3(0f, 0.12f * depth * MathF.Sin(MathF.PI * MathF.Pow(a, 0.7f)), -0.02f * a),
                steps / 2 + 2);
        }
        // Anal fin under the rear of the body (long in bream and catfish, continuous in eels).
        float an0 = Math.Clamp(b.AnalAt - b.AnalLength / 2f, 0.4f, 0.95f);
        float an1 = b.Dorsal == DorsalKind.Continuous ? 0.995f : Math.Clamp(b.AnalAt + b.AnalLength / 2f, 0.45f, 0.98f);
        float ah = 0.24f * depth * (b.Dorsal == DorsalKind.Continuous ? 1.2f : 1f);
        Strip(a => BottomAt(Mathf.Lerp(an0, an1, a)),
            a => BottomAt(Mathf.Lerp(an0, an1, a)) + new Vector3(0f, -ah * MathF.Sin(MathF.PI * MathF.Pow(a, 0.55f)), -0.2f * ah * a),
            b.AnalLength > 0.2f ? steps * 2 : steps);
        // Paired fins: pelvics under the belly, pectorals behind the gill cover (huge fans on sculpins).
        foreach (float sx in new[] { -1f, 1f })
        {
            float tp = 0.46f;
            var pb = new Vector3(sx * prof.HalfWidth(tp) * 0.45f, -prof.Bottom(tp) * 0.9f, Z(tp));
            float ps = 0.2f * depth;
            Tri(fst, pb, pb + new Vector3(0, 0, -0.06f), pb + new Vector3(sx * ps * 0.5f, -ps, -0.07f), white, white, tipC);
            Tri(fst, pb, pb + new Vector3(sx * ps * 0.5f, -ps, -0.07f), pb + new Vector3(sx * ps * 0.2f, -ps * 0.7f, 0.0f), white, tipC, tipC);

            float tq = Math.Clamp(b.EyeAt + 0.13f, 0.15f, 0.35f);
            float qs = 0.22f * depth * b.PectoralSize;
            var qb = new Vector3(sx * prof.HalfWidth(tq) * 0.85f, -prof.Bottom(tq) * 0.35f, Z(tq));
            for (int k = 0; k < 4; k++)
            {
                float a0 = k / 4f, a1 = (k + 1) / 4f;
                Vector3 Edge(float a) => qb + new Vector3(sx * qs * (0.5f + 0.4f * a), -qs * (0.6f - 0.5f * a), -qs * (0.5f + 0.9f * MathF.Sin(MathF.PI * a * 0.9f)));
                Tri(fst, qb, Edge(a0), Edge(a1), white, tipC, tipC);
            }
        }
        fst.GenerateNormals();
        shape.Fins = fst.Commit();

        // --- tail fin (on its own pivot so it can beat) ---
        var tst = new SurfaceTool();
        tst.Begin(Mesh.PrimitiveType.Triangles);
        float tailLen = 1f - BodyFraction;
        float tailH = b.TailSize * MathF.Min(depth, 0.3f);
        float ped = prof.Top(1f);
        int tSteps = detailed ? 20 : 8;
        Vector3 TailRoot(float a) => new(0f, Mathf.Lerp(ped, -prof.Bottom(1f), a) * 0.95f, 0.004f);
        Vector3 TailEdge(float a)
        {
            float fork = b.TailFork;
            float len = tailLen * ((1f - fork) * (0.82f + 0.18f * MathF.Sin(MathF.PI * a))
                + fork * (1f - 0.72f * MathF.Exp(-MathF.Pow((a - 0.5f) / 0.17f, 2f))));
            float y = Mathf.Lerp(tailH * 0.5f, -tailH * 0.5f, a);
            if (a < 0.5f && b.TailUpper > 1f)
            {
                float up = Mathf.Lerp(b.TailUpper, 1f, a * 2f);
                len *= up;
                y *= Mathf.Lerp(1f, 0.75f, (up - 1f) / MathF.Max(b.TailUpper - 1f, 0.01f));
            }
            return new Vector3(0f, y, -len);
        }
        for (int k = 0; k < tSteps; k++)
        {
            float a0 = (float)k / tSteps, a1 = (float)(k + 1) / tSteps;
            Quad(tst, TailRoot(a0), TailRoot(a1), TailEdge(a1), TailEdge(a0), white, white, tipC, tipC);
        }
        tst.GenerateNormals();
        shape.Tail = tst.Commit();

        // --- barbels around the mouth ---
        if (b.BarbelPairs > 0)
        {
            var bst = new SurfaceTool();
            bst.Begin(Mesh.PrimitiveType.Triangles);
            for (int pair = 0; pair < b.BarbelPairs; pair++)
                foreach (float sx in new[] { -1f, 1f })
                {
                    float t = 0.015f + 0.012f * pair;
                    var root = new Vector3(sx * prof.HalfWidth(t) * (0.5f + 0.25f * pair), -prof.Bottom(t) * (0.3f + 0.25f * pair), Z(t));
                    float len = b.BarbelLength * (pair == 0 ? 1f : 0.55f);
                    var dir = new Vector3(sx * (0.35f + 0.2f * pair), -0.7f, 0.25f - 0.35f * pair).Normalized();
                    Thread(bst, root, root + dir * len + new Vector3(0, -len * 0.25f, 0), 0.0035f, detailed ? 6 : 3);
                }
            bst.GenerateNormals();
            shape.Barbels = bst.Commit();
        }

        // --- eye: sized to the species and never bigger than the head allows ---
        float te = b.EyeAt;
        float headTop = prof.Top(te), headBottom = prof.Bottom(te);
        float r = b.Eye * BodyFraction * 0.5f;
        r = MathF.Min(r, 0.3f * (headTop + headBottom));
        r = MathF.Min(r, 0.6f * prof.HalfWidth(te));
        float ey = b.EyeHigh >= 0f ? headTop * Math.Clamp(b.EyeHigh, 0f, 0.85f) : -headBottom * Math.Clamp(-b.EyeHigh, 0f, 0.6f);
        float rel = ey >= 0f ? ey / MathF.Max(headTop, 1e-4f) : -ey / MathF.Max(headBottom, 1e-4f);
        float ex = prof.HalfWidth(te) * MathF.Sqrt(MathF.Max(1f - rel * rel, 0.05f)) * 0.96f;
        shape.EyePos = new Vector3(ex, ey, Z(te));
        shape.EyeRadius = r;
        int eyeSeg = detailed ? 20 : 8;
        shape.EyeMesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = eyeSeg, Rings = eyeSeg / 2 };
        shape.PupilMesh = new SphereMesh { Radius = r * 0.55f, Height = r * 1.1f, RadialSegments = eyeSeg, Rings = eyeSeg / 2 };
        shape.EyeHeadHeight = headTop + headBottom;
        return shape;
    }

    private static Shape ShapeFor(FishSpecies sp, bool detailed)
    {
        var key = (sp.SpeciesName, detailed);
        if (!Shapes.TryGetValue(key, out var s))
        {
            s = BuildShape(sp, detailed);
            Shapes[key] = s;
        }
        return s;
    }

    // ================================================================== the node
    private void Build(FishSpecies sp, float weight)
    {
        _species = sp;
        Length = sp.LengthRef * MathF.Pow(MathF.Max(weight, 0.002f), 0.33f);
        var shape = ShapeFor(sp, _detailed);
        var unit = new Node3D { Scale = new Vector3(Length, Length, Length) };
        AddChild(unit);
        EyeDiameter = shape.EyeRadius * 2f * Length;
        HeadHeightAtEye = shape.EyeHeadHeight * Length;
        MaxHeight = shape.MaxHeight * Length;
        MaxWidth = shape.MaxWidth * Length;

        var style = Graphics.FishSkin.StyleOf(sp);
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
        unit.AddChild(new MeshInstance3D { Mesh = shape.Body, MaterialOverride = _bodyMaterial });
        if (_detailed)
        {
            Graphics.FishSkin.Prewarm(sp);
            TryUpgradeSkin();
        }

        var fin = Graphics.FishSkin.Vivid(sp.FinColor, 1.4f);
        var finMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoColor = fin,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Burley,
            Roughness = 0.45f,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        unit.AddChild(new MeshInstance3D { Mesh = shape.Fins, MaterialOverride = finMaterial });
        _tail = new Node3D { Position = new Vector3(0f, 0f, shape.TailZ) };
        unit.AddChild(_tail);
        _tail.AddChild(new MeshInstance3D { Mesh = shape.Tail, MaterialOverride = finMaterial });
        if (shape.Barbels != null)
        {
            var barbel = new StandardMaterial3D { AlbedoColor = Graphics.FishSkin.Vivid(sp.BellyColor).Lerp(fin, 0.4f).Darkened(0.2f), Roughness = 0.5f };
            unit.AddChild(new MeshInstance3D { Mesh = shape.Barbels, MaterialOverride = barbel });
        }

        // Eyes: golden iris and a black pupil, flattened so they sit in the head instead of bulging out of it.
        var irisMaterial = IrisMaterial;
        var pupilMaterial = PupilMaterial;
        float r = shape.EyeRadius;
        var eyeMesh = shape.EyeMesh;
        var pupilMesh = shape.PupilMesh;
        foreach (float sx in new[] { -1f, 1f })
        {
            var p = shape.EyePos with { X = shape.EyePos.X * sx };
            unit.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = irisMaterial, Position = p, Scale = new Vector3(0.45f, 1f, 1f) });
            unit.AddChild(new MeshInstance3D
            {
                Mesh = pupilMesh, MaterialOverride = pupilMaterial,
                Position = p + new Vector3(sx * r * 0.32f, 0f, r * 0.05f), Scale = new Vector3(0.45f, 1f, 1f),
            });
        }
    }

    private static readonly StandardMaterial3D IrisMaterial = new()
    {
        AlbedoColor = new Color(0.9f, 0.72f, 0.25f), Roughness = 0.2f, Metallic = 0.3f, ClearcoatEnabled = true, Clearcoat = 1f,
    };

    private static readonly StandardMaterial3D PupilMaterial = new()
    {
        AlbedoColor = new Color(0.01f, 0.01f, 0.01f), Roughness = 0.05f, ClearcoatEnabled = true, Clearcoat = 1f,
    };

    private static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
    {
        st.SetColor(ca); st.AddVertex(a);
        st.SetColor(cb); st.AddVertex(b);
        st.SetColor(cc); st.AddVertex(c);
    }

    private static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd)
    {
        Tri(st, a, b, c, ca, cb, cc);
        Tri(st, a, c, d, ca, cc, cd);
    }

    /// <summary>A thin tapering tube (a barbel).</summary>
    private static void Thread(SurfaceTool st, Vector3 from, Vector3 to, float radius, int sides)
    {
        var axis = (to - from).Normalized();
        var side = axis.Cross(Vector3.Up).LengthSquared() > 1e-4f ? axis.Cross(Vector3.Up).Normalized() : Vector3.Right;
        var up = side.Cross(axis);
        for (int k = 0; k < sides; k++)
        {
            float a0 = Mathf.Tau * k / sides, a1 = Mathf.Tau * (k + 1) / sides;
            var o0 = (side * MathF.Cos(a0) + up * MathF.Sin(a0)) * radius;
            var o1 = (side * MathF.Cos(a1) + up * MathF.Sin(a1)) * radius;
            Quad(st, from + o0, from + o1, to + o1 * 0.3f, to + o0 * 0.3f, Colors.White, Colors.White, Colors.White, Colors.White);
        }
    }

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
        if (!Visible)
            return;                     // hidden fish in the lake cost nothing
        _time += (float)delta;
        if (_detailed && !_skinUpgraded)
            TryUpgradeSkin();
        if (_tail != null)
            _tail.Rotation = new Vector3(0f, MathF.Sin(_time * 9f) * 0.45f * Wiggle, 0f);
        Rotation = new Vector3(Rotation.X, Rotation.Y, MathF.Sin(_time * 4.5f) * 0.04f * Wiggle);
    }
}
