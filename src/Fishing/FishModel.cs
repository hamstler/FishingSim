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
/// Where a real fish of the species was measured (<see cref="FishReference"/>), the body follows that fish's side view and
/// the skin its colours. It wears the species' painted skin (FishSkin). The nose points along +Z. Geometry is built at unit length and
/// cached per species, then scaled, so a lake full of fish costs one build per species. Detailed fish (on the line,
/// in the hand, in the atlas) get a fine mesh, the high-resolution skin with its normal map and a wet clear-coat.
/// </summary>
public partial class FishModel : Node3D
{
    /// <summary>Nose to tail base, as a fraction of the total length (the tail fin is the rest).</summary>
    private const float BodyFraction = 0.82f;

    private Node3D? _tail;
    private float _time;
    private Vector3 _eyeScale = Vector3.One;
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
    /// the high-resolution painted skin; fish swimming in the lake use the light version. The weight sets the size and
    /// the build: young fish are slim with big eyes, old heavy ones deep and thick (<see cref="Growth"/>).
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
        public ArrayMesh Body = null!, Tail = null!;
        public readonly Dictionary<Graphics.FinKind, ArrayMesh> FinMeshes = new();
        public ArrayMesh? Barbels;
        public SphereMesh EyeMesh = null!, PupilMesh = null!;
        public ArrayMesh IrisDisc = null!;
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
        private readonly FishReference? _ref; // measured outline of a real fish, when there is one
        private readonly float _axis;

        /// <summary>Where the body rings start: blunt heads begin with a wider ring that the nose cap rounds off.</summary>
        public float NoseStart => _ref != null ? 0.012f : 0.03f * (1f - 0.75f * MathF.Min(_b.Snout, 1.2f) / 1.2f) + 0.004f;

        public Profile(FishBody b, float depth, FishReference? reference = null)
        {
            _b = b;
            _ref = reference;
            if (reference != null)
                _axis = reference.Axis;
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
        public float Top(float t) => _ref != null ? _ref.EdgesAt(t, _axis).Top : _k * RawTop(t);

        /// <summary>Depth of the belly below the axis.</summary>
        public float Bottom(float t) => _ref != null ? _ref.EdgesAt(t, _axis).Bottom : _k * RawBottom(t);

        /// <summary>Half width of the body.</summary>
        public float HalfWidth(float t)
        {
            float broad = 1f + _b.FlatHead * 0.7f * MathF.Exp(-MathF.Pow((t - 0.1f) / 0.14f, 2f))
                + _b.HeadBroad * MathF.Exp(-MathF.Pow((t - 0.14f) / 0.14f, 2f));
            if (_ref != null)
            {
                // The measured side view sets the height; the species' width-to-height ratio the thickness.
                var (top, bottom) = _ref.EdgesAt(t, _axis);
                return _b.Width * 0.5f * (top + bottom) * broad;
            }
            return _b.Width * _k * Env(t, _ew) * (1f + 0.3f * _b.Hump * Bump(t)) * broad;
        }
    }

    private static Shape BuildShape(FishSpecies sp, bool detailed)
    {
        var b = FishBody.Of(sp);
        var shape = new Shape();
        // A species measured from a real fish is lofted to that fish's outline, with its own body-to-tail length.
        var reference = FishReference.For(sp);
        float bodyLen = reference?.BodyLength ?? BodyFraction;
        float zNose = 0.5f;
        float Z(float t) => zNose - t * bodyLen;
        var prof = new Profile(b, sp.BodyDepth, reference);
        shape.TailZ = Z(1f);

        // --- body: rings along the length, denser at the nose and the tail ---
        int rings = detailed ? 72 : 22, segs = detailed ? 40 : 14;
        float nb = 2f + 4f * b.BellyFlat;                 // flatter belly = squarer lower half of the cross-section
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        // The nose: a few rings of a smooth dome in front of the first body ring (no point, no knob).
        int capRings = detailed ? 8 : 3;
        float t0 = prof.NoseStart;
        float noseTop = prof.Top(t0), noseBottom = prof.Bottom(t0), noseHalf = prof.HalfWidth(t0);
        float noseLen = 0.6f * MathF.Max(MathF.Max(noseTop, noseBottom), noseHalf);
        int totalRings = capRings + rings;
        (float Top, float Bottom, float Half, float Z, float U) Ring(int i)
        {
            if (i < capRings)
            {
                float phi = MathF.PI * 0.5f * i / capRings;                   // 0 at the very tip
                float k = MathF.Sin(phi);
                return (noseTop * k, noseBottom * k, noseHalf * k, Z(t0) + noseLen * MathF.Cos(phi), t0 * k * k);
            }
            float tt = 0.5f - 0.5f * MathF.Cos(MathF.PI * (i - capRings) / rings);
            float t = t0 + (1f - t0) * tt;
            return (prof.Top(t), prof.Bottom(t), prof.HalfWidth(t), Z(t), t);
        }
        for (int i = 0; i <= totalRings; i++)
        {
            var (top, bottom, half, zr, t) = Ring(i);
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
                st.AddVertex(new Vector3(x, y, zr));
            }
        }
        rings = totalRings;
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
        st.GenerateTangents();          // the skin's normal map needs them
        shape.Body = st.Commit();

        // Fin vertex colours: a darker base fading to a lighter, translucent edge (tinted by the material's fin colour).
        Color white = new(0.72f, 0.72f, 0.72f), tipC = new(1f, 1f, 1f, 0.7f);
        Vector3 TopAt(float t, float lift = 0.97f) => new(0f, prof.Top(t) * lift, Z(t));
        Vector3 BottomAt(float t) => new(0f, -prof.Bottom(t) * 0.97f, Z(t));
        float depth = shape.MaxHeight;
        int steps = detailed ? 14 : 5;

        // --- fins: per kind (each gets its own painted texture on close-up fish), with UVs U along the base and V from
        // the base (0) to the edge (1); split into rows so they bend smoothly with the swimming wave ---
        var finTools = new Dictionary<Graphics.FinKind, SurfaceTool>();
        SurfaceTool FinSt(Graphics.FinKind kind)
        {
            var key = detailed ? kind : Graphics.FinKind.Dorsal;      // light fish: all fins in one mesh
            if (!finTools.TryGetValue(key, out var tool))
            {
                tool = new SurfaceTool();
                tool.Begin(Mesh.PrimitiveType.Triangles);
                finTools[key] = tool;
            }
            return tool;
        }
        int rows = detailed ? 4 : 1;
        void Vtx(SurfaceTool tool, Vector3 p, float u, float v)
        {
            tool.SetUV(new Vector2(u, v));
            tool.SetColor(white.Lerp(tipC, v));
            tool.AddVertex(p);
        }
        void Sheet(SurfaceTool tool, Func<float, Vector3> baseAt, Func<float, Vector3> tipAt, int n)
        {
            for (int k = 0; k < n; k++)
                for (int r = 0; r < rows; r++)
                {
                    float a0 = (float)k / n, a1 = (float)(k + 1) / n, v0 = (float)r / rows, v1 = (float)(r + 1) / rows;
                    Vector3 P(float a, float v) => baseAt(a).Lerp(tipAt(a), v);
                    Vtx(tool, P(a0, v0), a0, v0); Vtx(tool, P(a1, v0), a1, v0); Vtx(tool, P(a1, v1), a1, v1);
                    Vtx(tool, P(a0, v0), a0, v0); Vtx(tool, P(a1, v1), a1, v1); Vtx(tool, P(a0, v1), a0, v1);
                }
        }
        void Fan(SurfaceTool tool, Vector3 root, Func<float, Vector3> edgeAt, int n)
        {
            for (int k = 0; k < n; k++)
                for (int r = 0; r < rows; r++)
                {
                    float a0 = (float)k / n, a1 = (float)(k + 1) / n, v0 = (float)r / rows, v1 = (float)(r + 1) / rows;
                    Vector3 P(float a, float v) => root.Lerp(edgeAt(a), v);
                    Vtx(tool, P(a0, v0), a0, v0); Vtx(tool, P(a1, v1), a1, v1); Vtx(tool, P(a1, v0), a1, v0);
                    Vtx(tool, P(a0, v0), a0, v0); Vtx(tool, P(a0, v1), a0, v1); Vtx(tool, P(a1, v1), a1, v1);
                }
        }
        float dAt = b.DorsalAt >= 0f ? b.DorsalAt : Math.Clamp(sp.DorsalPos, 0.25f, 0.8f);
        float d0 = Math.Clamp(dAt - b.DorsalLength / 2f, 0.12f, 0.95f), d1 = Math.Clamp(dAt + b.DorsalLength / 2f, 0.15f, 0.99f);
        float dh = b.DorsalHeight * depth;
        Func<float, float> dorsalShape = b.Dorsal switch
        {
            DorsalKind.Spiny => a => a < 0.5f
                ? (0.75f + 0.25f * MathF.Abs(MathF.Sin(a * MathF.PI * 9f))) * MathF.Sin(MathF.PI * MathF.Pow(a / 0.5f, 0.7f))
                : 0.8f * MathF.Sin(MathF.PI * MathF.Pow((a - 0.5f) / 0.5f, 0.6f)) + 0.12f,
            DorsalKind.Sail => a => MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * MathF.Pow(a, 0.5f)), 0f), 0.45f),
            DorsalKind.Long => a => a < 0.15f ? 0.4f + 0.6f * a / 0.15f : 1f - 0.6f * (a - 0.15f) / 0.85f,
            DorsalKind.Tiny => a => MathF.Sin(MathF.PI * a),
            DorsalKind.Continuous => a => 0.85f * MathF.Sin(MathF.PI * 0.5f * MathF.Min(a * 5f, 1f)),
            _ => a => MathF.Sin(MathF.PI * MathF.Pow(a, 0.6f)),
        };
        var dorsalKind = b.Dorsal switch
        {
            DorsalKind.Spiny => Graphics.FinKind.SpinyDorsal,
            DorsalKind.Sail => Graphics.FinKind.Sail,
            DorsalKind.Continuous => Graphics.FinKind.Continuous,
            DorsalKind.Spines => Graphics.FinKind.SpinyDorsal,
            _ => Graphics.FinKind.Dorsal,
        };
        if (b.Dorsal == DorsalKind.Spines)
        {
            // Stickleback: free spines on the back, each with a little membrane.
            int count = b.DorsalLength > 0.5f ? 9 : 3;
            var tool = FinSt(dorsalKind);
            for (int k = 0; k < count; k++)
            {
                float t = Mathf.Lerp(d0, d1, count == 1 ? 0.5f : (float)k / (count - 1));
                var root = TopAt(t);
                float h = dh * (count == 3 && k == 2 ? 0.5f : 1f);
                Vtx(tool, root, 0.02f, 0f); Vtx(tool, root + new Vector3(0, 0, -0.02f), 0.06f, 0f); Vtx(tool, root + new Vector3(0, h, -0.01f), 0.04f, 1f);
            }
        }
        else
        {
            float dEnd = b.Dorsal == DorsalKind.Continuous ? 0.995f : d1;
            Sheet(FinSt(dorsalKind), a => TopAt(Mathf.Lerp(d0, dEnd, a)),
                a => TopAt(Mathf.Lerp(d0, dEnd, a)) + new Vector3(0f, dh * dorsalShape(a), -0.25f * dh * dorsalShape(a)),
                b.Dorsal is DorsalKind.Spiny or DorsalKind.Continuous ? steps * 2 : steps);
        }
        if (b.Adipose)
        {
            Sheet(FinSt(Graphics.FinKind.Adipose), a => TopAt(Mathf.Lerp(0.84f, 0.9f, a)),
                a => TopAt(Mathf.Lerp(0.84f, 0.9f, a)) + new Vector3(0f, 0.12f * depth * MathF.Sin(MathF.PI * MathF.Pow(a, 0.7f)), -0.02f * a),
                steps / 2 + 2);
        }
        // Anal fin under the rear of the body (long in bream and catfish, continuous in eels).
        float an0 = Math.Clamp(b.AnalAt - b.AnalLength / 2f, 0.4f, 0.95f);
        float an1 = b.Dorsal == DorsalKind.Continuous ? 0.995f : Math.Clamp(b.AnalAt + b.AnalLength / 2f, 0.45f, 0.98f);
        float ah = 0.24f * depth * (b.Dorsal == DorsalKind.Continuous ? 1.2f : 1f);
        Sheet(FinSt(b.Dorsal == DorsalKind.Continuous ? Graphics.FinKind.Continuous : Graphics.FinKind.Anal),
            a => BottomAt(Mathf.Lerp(an0, an1, a)),
            a => BottomAt(Mathf.Lerp(an0, an1, a)) + new Vector3(0f, -ah * MathF.Sin(MathF.PI * MathF.Pow(a, 0.55f)), -0.2f * ah * a),
            b.AnalLength > 0.2f ? steps * 2 : steps);
        // Paired fins: pelvics under the belly, pectorals behind the gill cover (huge fans on sculpins).
        int fanSteps = detailed ? 10 : 3;
        foreach (float sx in new[] { -1f, 1f })
        {
            float tp = 0.46f;
            var pb = new Vector3(sx * prof.HalfWidth(tp) * 0.45f, -prof.Bottom(tp) * 0.9f, Z(tp));
            float ps = 0.2f * depth;
            Fan(FinSt(Graphics.FinKind.Pelvic), pb,
                a => pb + new Vector3(sx * ps * (0.25f + 0.3f * a), -ps * (0.75f + 0.25f * MathF.Sin(MathF.PI * a)), -0.02f - 0.06f * a),
                fanSteps);

            float tq = Math.Clamp(b.EyeAt + 0.13f, 0.15f, 0.35f);
            float qs = 0.22f * depth * b.PectoralSize;
            var qb = new Vector3(sx * prof.HalfWidth(tq) * 0.85f, -prof.Bottom(tq) * 0.35f, Z(tq));
            Fan(FinSt(Graphics.FinKind.Pectoral), qb,
                a => qb + new Vector3(sx * qs * (0.5f + 0.4f * a), -qs * (0.6f - 0.5f * a), -qs * (0.5f + 0.9f * MathF.Sin(MathF.PI * a * 0.9f))),
                fanSteps);
        }
        foreach (var (kind, tool) in finTools)
        {
            tool.GenerateNormals();
            shape.FinMeshes[kind] = tool.Commit();
        }

        // --- tail fin: light fish beat it on a pivot; close-up fish bend it with the body (absolute z) ---
        var tst = new SurfaceTool();
        tst.Begin(Mesh.PrimitiveType.Triangles);
        float tailLen = 1f - bodyLen;
        float tailH = b.TailSize * MathF.Min(depth, 0.3f);
        float ped = prof.Top(1f);
        float zBase = detailed ? Z(1f) : 0f;
        int tSteps = detailed ? 24 : 8;
        Vector3 TailRoot(float a) => new(0f, Mathf.Lerp(ped, -prof.Bottom(1f), a) * 0.95f, zBase + 0.004f);
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
            return new Vector3(0f, y, zBase - len);
        }
        Sheet(tst, TailRoot, TailEdge, tSteps);
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
                    // A fleshy barbel: thin, drooping and curling back along its length.
                    var p0 = root;
                    int segsB = detailed ? 4 : 1;
                    for (int k = 0; k < segsB; k++)
                    {
                        float f0 = (float)k / segsB, f1 = (float)(k + 1) / segsB;
                        var bend = new Vector3(0f, -0.35f * f1 * f1, -0.45f * f1 * f1) * len;
                        var p1 = root + dir * len * f1 + bend;
                        Thread(bst, p0, p1, 0.0022f * (1f - 0.7f * f0), detailed ? 6 : 3, 1f - 0.7f * f1 / MathF.Max(1f - 0.7f * f0, 0.01f));
                        p0 = p1;
                    }
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
        // Keep the whole eye on the side of the head, so it never pokes over the crown (or shows through a narrow head).
        ey = Math.Clamp(ey, -(headBottom - 1.15f * r), headTop - 1.15f * r);
        float rel = ey >= 0f ? ey / MathF.Max(headTop, 1e-4f) : -ey / MathF.Max(headBottom, 1e-4f);
        float ex = prof.HalfWidth(te) * MathF.Sqrt(MathF.Max(1f - rel * rel, 0.05f)) * 0.96f;
        shape.EyePos = new Vector3(ex, ey, Z(te));
        shape.EyeRadius = r;
        int eyeSeg = detailed ? 20 : 8;
        shape.EyeMesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = eyeSeg, Rings = eyeSeg / 2 };
        shape.PupilMesh = new SphereMesh { Radius = r * 0.55f, Height = r * 1.1f, RadialSegments = eyeSeg, Rings = eyeSeg / 2 };
        // The iris disc (right eye; the left one is mirrored): it faces outwards and slightly up, set a touch into
        // the head, with the texture's +U pointing to the snout so the pupil's notch faces forwards.
        // Surface normal at the eye from the slope of the head: the head widens towards the tail, so it faces forwards a bit.
        float dW = (prof.HalfWidth(te + 0.01f) - prof.HalfWidth(te - 0.01f)) / (0.02f * bodyLen);
        float dH = ((ey >= 0f ? prof.Top(te + 0.01f) - prof.Top(te - 0.01f) : prof.Bottom(te - 0.01f) - prof.Bottom(te + 0.01f)) / (0.02f * bodyLen));
        var normal = new Vector3(1f, 0.5f * rel * MathF.Sign(ey), dW * 0.9f + MathF.Abs(rel) * dH * 0.2f).Normalized();
        var right = new Vector3(0f, 0f, 1f);
        right = (right - normal * right.Dot(normal)).Normalized();
        var upAxis = normal.Cross(right);
        var dst = new SurfaceTool();
        dst.Begin(Mesh.PrimitiveType.Triangles);
        int discSeg = 40;
        var centre = normal * r * 0.2f;
        for (int k = 0; k < discSeg; k++)
        {
            float a0 = Mathf.Tau * k / discSeg, a1 = Mathf.Tau * (k + 1) / discSeg;
            foreach (var (ang, isCentre) in new[] { (0f, true), (a0, false), (a1, false) })
            {
                var uv = isCentre ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f + 0.5f * MathF.Cos(ang), 0.5f - 0.5f * MathF.Sin(ang));
                var pos = isCentre ? centre + normal * r * 0.05f : centre + (right * MathF.Cos(ang) + upAxis * MathF.Sin(ang)) * r * 0.95f;
                dst.SetUV(uv);
                dst.SetNormal(normal);
                dst.AddVertex(pos);
            }
        }
        shape.IrisDisc = dst.Commit();
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
    /// <summary>
    /// How grown-up a fish of this weight is for its species (0 = the smallest catch, 1 = the record class), on a
    /// log scale of the species' weight range.
    /// </summary>
    public static float Growth(FishSpecies sp, float weight)
    {
        float lo = MathF.Log(MathF.Max(sp.MinWeight, 0.001f)), hi = MathF.Log(MathF.Max(sp.MaxWeight, sp.MinWeight * 1.5f));
        return Math.Clamp((MathF.Log(MathF.Max(weight, 0.001f)) - lo) / MathF.Max(hi - lo, 1e-3f), 0f, 1f);
    }

    /// <summary>Body depth, body width and eye size of a fish at this growth, relative to the species' build.</summary>
    public static (float Depth, float Width, float Eye) Proportions(float growth) =>
        (0.9f + 0.18f * growth, 0.86f + 0.26f * growth, 1.12f - 0.22f * growth);

    private void Build(FishSpecies sp, float weight)
    {
        _species = sp;
        float growth = Growth(sp, weight);
        var (depthK, widthK, eyeK) = Proportions(growth);
        // A deeper, thicker body holds more weight for its length: the length follows the weight and the build.
        Length = sp.LengthRef * MathF.Pow(MathF.Max(weight, 0.002f) / (depthK * widthK), 0.33f);
        // A close-up fish of a species with a hand-built model (tools/fish_model) uses it; others are lofted.
        var asset = _detailed ? FishAsset.For(sp) : null;
        var shape = asset == null ? ShapeFor(sp, _detailed) : null;
        float eyeRadius = asset?.EyeRadius ?? shape!.EyeRadius, eyeHead = asset?.HeadHeightAtEye ?? shape!.EyeHeadHeight;
        // Sideways (x) the width, upwards (y) the depth: the cached unit-length shape grows with the fish.
        var unit = new Node3D { Scale = new Vector3(Length * widthK, Length * depthK, Length) };
        AddChild(unit);
        // Eyes keep their round shape (undo the unequal scale) and are relatively bigger on young fish, never bigger
        // than the head allows.
        float eyeFit = 0.6f * eyeHead * depthK / MathF.Max(2f * eyeRadius, 1e-5f);
        _eyeScale = new Vector3(1f / widthK, 1f / depthK, 1f) * MathF.Min(eyeK, eyeFit);
        EyeDiameter = eyeRadius * 2f * Length * _eyeScale.Y * depthK;
        HeadHeightAtEye = eyeHead * Length * depthK;
        MaxHeight = (asset?.MaxHeight ?? shape!.MaxHeight) * Length * depthK;
        MaxWidth = (asset?.MaxWidth ?? shape!.MaxWidth) * Length * widthK;

        var style = Graphics.FishSkin.StyleOf(sp);
        var fin = Graphics.FishSkin.Vivid(sp.FinColor, 1.4f);
        if (asset != null)
        {
            BuildAsset(sp, asset, unit, style);
            return;
        }
        if (_detailed)
            BuildDetailed(sp, shape!, unit, style, fin);
        else
            BuildLight(sp, shape!, unit, style, fin);
        if (shape!.Barbels != null)
        {
            var barbel = new StandardMaterial3D { AlbedoColor = Graphics.FishSkin.Vivid(sp.BellyColor).Lerp(fin, 0.4f).Darkened(0.2f), Roughness = 0.45f };
            unit.AddChild(new MeshInstance3D { Mesh = shape.Barbels, MaterialOverride = barbel });
        }
    }

    /// <summary>Fish swimming in the lake: standard materials, all fins in one mesh, the tail on a beating pivot.</summary>
    private void BuildLight(FishSpecies sp, Shape shape, Node3D unit, Graphics.SkinStyle style, Color fin)
    {
        _bodyMaterial = new StandardMaterial3D
        {
            AlbedoTexture = Graphics.FishSkin.For(sp),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Burley,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
            Roughness = 0.34f,
            Metallic = 0.1f + 0.35f * style.Silver,
            MetallicSpecular = 0.6f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        unit.AddChild(new MeshInstance3D { Mesh = shape.Body, MaterialOverride = _bodyMaterial });
        var finMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoColor = fin,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Burley,
            Roughness = 0.45f,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        foreach (var mesh in shape.FinMeshes.Values)
            unit.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = finMaterial });
        _tail = new Node3D { Position = new Vector3(0f, 0f, shape.TailZ) };
        unit.AddChild(_tail);
        _tail.AddChild(new MeshInstance3D { Mesh = shape.Tail, MaterialOverride = finMaterial });

        // Eyes: golden iris and a black pupil, flattened so they sit in the head instead of bulging out of it.
        float r = shape.EyeRadius;
        foreach (float sx in new[] { -1f, 1f })
        {
            var p = shape.EyePos with { X = shape.EyePos.X * sx };
            unit.AddChild(new MeshInstance3D { Mesh = shape.EyeMesh, MaterialOverride = IrisMaterial, Position = p, Scale = new Vector3(0.45f, 1f, 1f) * _eyeScale });
            unit.AddChild(new MeshInstance3D
            {
                Mesh = shape.PupilMesh, MaterialOverride = PupilMaterial,
                Position = p + new Vector3(sx * r * 0.32f, 0f, r * 0.05f) * _eyeScale, Scale = new Vector3(0.45f, 1f, 1f) * _eyeScale,
            });
        }
    }

    /// <summary>
    /// A close-up fish from its hand-built model (FishAsset): the painted body with its normal map and the wet,
    /// silvery sheen, each fin with its own painted texture, all bending with the swimming wave; glossy eyes.
    /// </summary>
    private void BuildAsset(FishSpecies sp, FishAsset asset, Node3D unit, Graphics.SkinStyle style)
    {
        _bodyShader ??= GD.Load<Shader>("res://shaders/fish_body.gdshader");
        _finShader ??= GD.Load<Shader>("res://shaders/fish_fin.gdshader");
        _bodyShaderMaterial = new ShaderMaterial { Shader = _bodyShader };
        if (asset.BodyAlbedo != null)
            _bodyShaderMaterial.SetShaderParameter("albedo_tex", asset.BodyAlbedo);
        if (asset.BodyNormal != null)
        {
            _bodyShaderMaterial.SetShaderParameter("normal_tex", asset.BodyNormal);
            _bodyShaderMaterial.SetShaderParameter("use_normal", true);
            _bodyShaderMaterial.SetShaderParameter("normal_strength", 0.8f);
        }
        _bodyShaderMaterial.SetShaderParameter("metallic", 0.08f + 0.3f * style.Silver);
        _bodyShaderMaterial.SetShaderParameter("roughness", 0.42f);
        _bodyShaderMaterial.SetShaderParameter("clearcoat", 0.3f);
        _skinUpgraded = true;
        float phase = (float)(GD.Randf() * Mathf.Tau);
        void Swimmer(GeometryInstance3D g)
        {
            g.SetInstanceShaderParameter("swim_phase", phase);
            _swimmers.Add(g);
        }
        var body = new MeshInstance3D { Mesh = asset.Body, MaterialOverride = _bodyShaderMaterial };
        unit.AddChild(body);
        Swimmer(body);
        foreach (var (name, mesh, texture) in asset.Fins)
        {
            var m = new ShaderMaterial { Shader = _finShader };
            if (texture != null)
                m.SetShaderParameter("fin_tex", texture);
            m.SetShaderParameter("flutter", name is "FinPectoral" or "FinPelvic" ? 1f : 0f);
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = m };
            unit.AddChild(mi);
            Swimmer(mi);
        }
        var eyeMaterial = new StandardMaterial3D
        {
            AlbedoTexture = asset.Iris,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Roughness = 0.08f,
            MetallicSpecular = 0.9f,
            ClearcoatEnabled = true,
            Clearcoat = 1f,
            ClearcoatRoughness = 0.05f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        foreach (var (mesh, position) in asset.Eyes)
            unit.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = eyeMaterial, Position = position, Scale = _eyeScale });
        foreach (var (mesh, position) in asset.Corneas)
            unit.AddChild(new MeshInstance3D
            {
                Mesh = mesh, MaterialOverride = CorneaMaterial, Position = position, Scale = _eyeScale,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
    }

    private static Shader? _bodyShader, _finShader;
    private ShaderMaterial? _bodyShaderMaterial;
    private readonly List<GeometryInstance3D> _swimmers = new();
    private float _sentWiggle = -1f;

    /// <summary>
    /// Close-up fish: the body and every fin bend with one swimming wave (shaders/fish_body, fish_fin), each fin wears
    /// its painted texture (rays, translucent membrane, markings), and the eyes are an iris disc under a glossy cornea.
    /// </summary>
    private void BuildDetailed(FishSpecies sp, Shape shape, Node3D unit, Graphics.SkinStyle style, Color fin)
    {
        _bodyShader ??= GD.Load<Shader>("res://shaders/fish_body.gdshader");
        _finShader ??= GD.Load<Shader>("res://shaders/fish_fin.gdshader");
        _bodyShaderMaterial = new ShaderMaterial { Shader = _bodyShader };
        _bodyShaderMaterial.SetShaderParameter("albedo_tex", Graphics.FishSkin.For(sp));
        _bodyShaderMaterial.SetShaderParameter("metallic", 0.1f + 0.35f * style.Silver);
        float phase = (float)(GD.Randf() * Mathf.Tau);
        void Swimmer(GeometryInstance3D g)
        {
            g.SetInstanceShaderParameter("swim_phase", phase);
            _swimmers.Add(g);
        }
        var body = new MeshInstance3D { Mesh = shape.Body, MaterialOverride = _bodyShaderMaterial };
        unit.AddChild(body);
        Swimmer(body);
        Graphics.FishSkin.Prewarm(sp);
        Graphics.FinPainter.Prewarm(sp);
        TryUpgradeSkin();

        foreach (var (kind, mesh) in shape.FinMeshes)
        {
            var m = new ShaderMaterial { Shader = _finShader };
            m.SetShaderParameter("fin_tex", Graphics.FinPainter.For(sp, kind));
            m.SetShaderParameter("flutter", kind is Graphics.FinKind.Pectoral or Graphics.FinKind.Pelvic ? 1f : 0f);
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = m };
            unit.AddChild(mi);
            Swimmer(mi);
        }
        var tailMaterial = new ShaderMaterial { Shader = _finShader };
        tailMaterial.SetShaderParameter("fin_tex", Graphics.FinPainter.For(sp, Graphics.FinKind.Caudal));
        var tail = new MeshInstance3D { Mesh = shape.Tail, MaterialOverride = tailMaterial };
        unit.AddChild(tail);
        Swimmer(tail);

        // Eyes: the painted iris on a disc set into the head, under a clear, glossy cornea.
        var irisMaterial = new StandardMaterial3D
        {
            AlbedoTexture = Graphics.EyePainter.For(FishBody.IrisOf(sp)),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Roughness = 0.35f,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        foreach (float sx in new[] { -1f, 1f })
        {
            var p = shape.EyePos with { X = shape.EyePos.X * sx };
            unit.AddChild(new MeshInstance3D { Mesh = shape.IrisDisc, MaterialOverride = irisMaterial, Position = p, Scale = new Vector3(sx, 1f, 1f) * _eyeScale });
            unit.AddChild(new MeshInstance3D
            {
                Mesh = shape.EyeMesh, MaterialOverride = CorneaMaterial,
                Position = p + new Vector3(sx * shape.EyeRadius * 0.08f, 0f, 0f) * _eyeScale, Scale = new Vector3(0.5f, 1.04f, 1.04f) * _eyeScale,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    private static readonly StandardMaterial3D CorneaMaterial = new()
    {
        AlbedoColor = new Color(0.9f, 0.95f, 1f, 0.08f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.02f,
        MetallicSpecular = 1f,
        ClearcoatEnabled = true,
        Clearcoat = 1f,
        ClearcoatRoughness = 0.02f,
    };

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
    private static void Thread(SurfaceTool st, Vector3 from, Vector3 to, float radius, int sides, float taper = 0.3f)
    {
        var axis = (to - from).Normalized();
        var side = axis.Cross(Vector3.Up).LengthSquared() > 1e-4f ? axis.Cross(Vector3.Up).Normalized() : Vector3.Right;
        var up = side.Cross(axis);
        for (int k = 0; k < sides; k++)
        {
            float a0 = Mathf.Tau * k / sides, a1 = Mathf.Tau * (k + 1) / sides;
            var o0 = (side * MathF.Cos(a0) + up * MathF.Sin(a0)) * radius;
            var o1 = (side * MathF.Cos(a1) + up * MathF.Sin(a1)) * radius;
            Quad(st, from + o0, from + o1, to + o1 * taper, to + o0 * taper, Colors.White, Colors.White, Colors.White, Colors.White);
        }
    }

    /// <summary>Puts the detailed skin and its normal map on once they have been painted (in the background).</summary>
    private void TryUpgradeSkin()
    {
        if (_skinUpgraded || _species == null || (_bodyMaterial == null && _bodyShaderMaterial == null))
            return;
        var skin = Graphics.FishSkin.TryDetailed(_species);
        if (skin is not { } pair)
            return;
        _skinUpgraded = true;
        if (_bodyShaderMaterial != null)
        {
            _bodyShaderMaterial.SetShaderParameter("albedo_tex", pair.Albedo);
            _bodyShaderMaterial.SetShaderParameter("normal_tex", pair.Normal);
            _bodyShaderMaterial.SetShaderParameter("use_normal", true);
            return;
        }
        _bodyMaterial!.AlbedoTexture = pair.Albedo;
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
        if (_swimmers.Count > 0 && MathF.Abs(Wiggle - _sentWiggle) > 0.01f)
        {
            _sentWiggle = Wiggle;
            foreach (var g in _swimmers)
                g.SetInstanceShaderParameter("wiggle_amount", Wiggle);
        }
        Rotation = new Vector3(Rotation.X, Rotation.Y, MathF.Sin(_time * 4.5f) * 0.04f * Wiggle);
    }
}
