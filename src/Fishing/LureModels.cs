using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Core.Methods;
using FishingSim.Data;
using FishingSim.Graphics;
using Profile = FishingSim.Fishing.LureMesh.Profile;

namespace FishingSim.Fishing;

/// <summary>
/// The lure models, built from a lure's data like the real articles: wobblers are lofted balsa/plastic bodies (a
/// side-view profile per model) with a painted holographic skin (<see cref="LurePainter"/>), a clear coat, 3D eyes under
/// epoxy domes, a clear polycarbonate lip with the line tie on it (or none for jerks, vibs, pencils and poppers), screw
/// eyes, split rings and forged trebles; spoons and spinner blades are dished metal plates with a painted face and a
/// polished back; spinners have a wire shaft, a clevis, brass beads and body, a dressed treble; soft baits are ribbed
/// plastic on a painted jig head with the hook's point out of the back; flies are tied on bronze hooks. Nose towards +Z,
/// the line's eye at the origin. The spinner's blade turns on the node returned as <c>spin</c>; soft baits' tails and
/// the swimbait's rear segment swing on <c>tail</c>.
/// </summary>
public static class LureModels
{
    public static Node3D Build(BaitData bait, out Node3D? spin, out Node3D? tail)
    {
        spin = null;
        tail = null;
        var family = Method.FamilyOf(bait.AffinityAs);
        string shape = bait.Shape.Length > 0 ? bait.Shape : family switch
        {
            LureFamily.Spoon => "classic",
            LureFamily.Wobbler => "minnow",
            LureFamily.Softbait => "ripper",
            LureFamily.Fly => "dry",
            _ => "round",
        };
        var look = new Look(bait.LureColor, bait.AccentColor, bait.Pattern, bait.Metallic, Seed(bait.ItemId + bait.DisplayName));
        var root = new Node3D { Name = "Lure" };
        float nominal;
        switch (family)
        {
            case LureFamily.Spoon:
                nominal = shape == "jig" ? 7f : 6f;
                Spoon(root, shape, look);
                break;
            case LureFamily.Wobbler:
                nominal = shape switch { "jerk" => 7f, "pencil" => 7.5f, "swimbait" => 9f, "vib" => 5f, _ => 5.6f };
                tail = Wobbler(root, shape, look);
                break;
            case LureFamily.Softbait:
                nominal = shape switch { "worm" => 8f, "stick" => 9f, "frog" => 5.5f, _ => 6f };
                tail = Softbait(root, shape, look);
                break;
            case LureFamily.Fly:
                nominal = shape == "streamer" ? 6f : 1.8f;
                Fly(root, shape, look);
                break;
            default:
                nominal = shape switch { "spinnerbait" => 6f, "tailspinner" => 5f, _ => 5f };
                spin = Spinner(root, shape, look);
                break;
        }
        // The model is built at its family's usual length; the lure's own size scales it.
        float k = bait.SizeCm > 0f ? Math.Clamp(bait.SizeCm / nominal, 0.4f, 3f) : 1f;
        root.Scale = new Vector3(k, k, k);
        return root;
    }

    private static int Seed(string s)
    {
        uint h = 2166136261;
        foreach (char c in s)
            h = (h ^ c) * 16777619;
        return (int)(h & 0x7fffffff);
    }

    private readonly record struct Look(Color Main, Color Accent, string Pattern, bool Metal, int Seed);

    // ------------------------------------------------------------------ materials
    private static StandardMaterial3D MetalMat(Color c, float rough = 0.18f) => new()
    {
        AlbedoColor = c, Metallic = 1f, Roughness = rough, MetallicSpecular = 0.7f,
    };

    private static StandardMaterial3D PaintMat(Color c, float rough = 0.4f) => new() { AlbedoColor = c, Roughness = rough };

    private static readonly StandardMaterial3D Wire = MetalMat(new Color(0.82f, 0.83f, 0.86f), 0.15f);
    private static readonly StandardMaterial3D BlackNickel = MetalMat(new Color(0.2f, 0.2f, 0.22f), 0.25f);
    private static readonly StandardMaterial3D Bronze = MetalMat(new Color(0.48f, 0.36f, 0.2f), 0.3f);
    private static readonly StandardMaterial3D Brass = MetalMat(new Color(0.88f, 0.68f, 0.32f), 0.2f);
    private static readonly StandardMaterial3D Steel = MetalMat(new Color(0.72f, 0.73f, 0.76f), 0.2f);
    private static readonly StandardMaterial3D Black = PaintMat(new Color(0.03f, 0.03f, 0.035f), 0.25f);

    /// <summary>Clear polycarbonate (lips) and epoxy (eye domes).</summary>
    private static readonly StandardMaterial3D Clear = new()
    {
        AlbedoColor = new Color(0.9f, 0.95f, 1f, 0.28f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.04f, MetallicSpecular = 0.9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        RimEnabled = true, Rim = 0.4f, RimTint = 0.2f,
    };

    private static readonly StandardMaterial3D Epoxy = new()
    {
        AlbedoColor = new Color(1f, 1f, 1f, 0.08f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.02f, MetallicSpecular = 1f,
    };

    private static Material Skin(LurePainter.Set s, bool clearcoat = true) => new OrmMaterial3D
    {
        AlbedoTexture = s.Albedo, OrmTexture = s.Orm, NormalEnabled = true, NormalTexture = s.Normal,
        ClearcoatEnabled = clearcoat, Clearcoat = 0.85f, ClearcoatRoughness = 0.05f,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
    };

    /// <summary>Soft plastic: no clear coat, light passes through its thin parts.</summary>
    private static Material Plastic(LurePainter.Set s, Color main) => new OrmMaterial3D
    {
        AlbedoTexture = s.Albedo, OrmTexture = s.Orm, NormalEnabled = true, NormalTexture = s.Normal,
        BacklightEnabled = true, Backlight = main.Lightened(0.2f) * 0.55f,
        RimEnabled = true, Rim = 0.25f, RimTint = 0.8f,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
    };

    private static MeshInstance3D Part(Node3D parent, Mesh mesh, Material mat, Vector3 at, Vector3? rot = null, Vector3? scale = null)
    {
        var mi = new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = mat, Position = at, Rotation = rot ?? Vector3.Zero, Scale = scale ?? Vector3.One,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(mi);
        return mi;
    }

    private static Mesh Ball(int seg = 16) => new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = seg, Rings = seg / 2 };

    // ------------------------------------------------------------------ hardware
    /// <summary>A screw eye at <paramref name="at"/>, a split ring and a treble of gap <paramref name="gap"/> hanging from
    /// it (trailing back by <paramref name="trail"/> radians).</summary>
    private static void HangTreble(Node3D parent, Vector3 at, float gap, float trail = 0.25f, Material? hook = null)
    {
        var node = new Node3D { Position = at, Rotation = new Vector3(trail, 0f, 0f) };
        parent.AddChild(node);
        float ring = gap * 0.32f;
        Part(node, LureMesh.Ring(0.13f), Steel, new Vector3(0f, -ring * 0.6f, 0f), new Vector3(0f, Mathf.Pi / 2f, 0f), Vector3.One * ring);
        var t = Part(node, LureMesh.Treble(), hook ?? BlackNickel, new Vector3(0f, -ring * 1.45f, 0f), new Vector3(0f, 0.35f, 0f), Vector3.One * gap);
        t.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    /// <summary>A wire eye (the line tie or a hook hanger) standing out of a surface along <paramref name="normal"/>.</summary>
    private static void Eye(Node3D parent, Vector3 at, Vector3 normal, float radius, Material? mat = null)
    {
        var pts = LureMesh.Arc(at + normal * radius, -normal, Vector3.Forward.Cross(normal).LengthSquared() > 0.01f ? Vector3.Forward : Vector3.Right,
            radius, 0f, Mathf.Tau, 14);
        Part(parent, LureMesh.Tube(pts, _ => radius * 0.22f, 5), mat ?? Steel, Vector3.Zero);
    }

    /// <summary>An epoxy dome over a painted eye at a surface point.</summary>
    private static void EyeDome(Node3D parent, Vector3 at, Vector3 normal, float radius)
    {
        var mi = Part(parent, Ball(14), Epoxy, at + normal * radius * 0.08f);
        mi.Basis = Basis.LookingAt(normal, MathF.Abs(normal.Y) > 0.95f ? Vector3.Forward : Vector3.Up).Scaled(new Vector3(radius * 2.1f, radius * 2.1f, radius * 0.7f));
    }

    private static Vector3 SurfaceNormal(Profile p, float t, float theta, float nose)
    {
        var a = p.Point(t, theta - 0.02f, nose);
        var b = p.Point(t, theta + 0.02f, nose);
        var c = p.Point(MathF.Max(t - 0.01f, 0f), theta, nose);
        var d = p.Point(MathF.Min(t + 0.01f, 1f), theta, nose);
        var n = (b - a).Cross(d - c).Normalized();
        var centre = new Vector3(0f, (p.TopAt(t) + p.BotAt(t)) * 0.5f, a.Z);
        return n.Dot(a - centre) < 0f ? -n : n;
    }

    /// <summary>
    /// A painted hard body: its skin (pattern, foil, gill, the eye at <paramref name="eyeT"/>, <paramref name="eyeH"/>),
    /// the clear coat and the epoxy domes. Returns the material for the swimbait's other segments.
    /// </summary>
    private static Material HardBody(Node3D parent, Profile p, Look look, float nose, float eyeT, float eyeH, float eyeR,
        float gill = 0.2f, float cup = 0f, float t0 = 0f, float t1 = 1f, LurePainter.Kind kind = LurePainter.Kind.Hard)
    {
        float hh = (p.TopAt(eyeT) - p.BotAt(eyeT)) * 0.5f;
        var spot = new LurePainter.EyeSpot(eyeT, eyeH, eyeR / p.Length, eyeR / (Mathf.Tau * MathF.Max(hh, 1e-4f)));
        var set = LurePainter.Paint(new LurePainter.Spec(kind, look.Main, look.Accent, look.Pattern, look.Metal && kind == LurePainter.Kind.Head,
            spot, p.Aspect(), look.Seed % 1000, gill));
        var mat = Skin(set);
        Part(parent, LureMesh.Loft(p, t0, t1, nose: nose, startCup: cup), mat, Vector3.Zero);
        float theta = MathF.Acos(Math.Clamp(-eyeH, -1f, 1f));
        foreach (float th in new[] { theta, Mathf.Tau - theta })
            EyeDome(parent, p.Point(eyeT, th, nose), SurfaceNormal(p, eyeT, th, nose), eyeR);
        return mat;
    }

    /// <summary>A clear lip from the chin forward and down at <paramref name="angle"/> degrees below the horizontal.</summary>
    private static Node3D Lip(Node3D parent, Vector3 at, float length, float width, float angle, bool lineTie)
    {
        float a = Mathf.DegToRad(angle);
        var dir = new Vector3(0f, -MathF.Sin(a), MathF.Cos(a));
        var lip = new Node3D { Position = at };
        parent.AddChild(lip);
        lip.Basis = Basis.LookingAt(dir, Vector3.Up);
        var mesh = LureMesh.Plate(u => 0.5f * MathF.Pow(MathF.Max(1f - MathF.Pow(u, 3f), 0f), 0.35f) * (0.82f + 0.18f * u), length, width, width * 0.04f, length * 0.04f, 16, 8);
        Part(lip, mesh, Clear, Vector3.Zero);
        if (lineTie)
            Eye(lip, new Vector3(0f, length * 0.03f, -length * 0.62f), Vector3.Up, length * 0.07f);
        return lip;
    }

    // ------------------------------------------------------------------ wobblers
    private static Node3D? Wobbler(Node3D root, string shape, Look look)
    {
        if (shape == "swimbait")
            return Swimbait(root, look);
        const float nose = 0.002f;
        Profile p;
        float eyeT = 0.12f, eyeH = 0.28f, eyeR, gill = 0.21f, cup = 0f;
        switch (shape)
        {
            case "crank":
                p = new Profile(0.042f, 0f, 0f, 0.01f, -0.01f, 0.05f, 0.1f, 0.1f, -0.08f, 0.15f, 0.17f, 0.19f, -0.15f, 0.36f, 0.205f, 0.25f, -0.2f,
                    0.6f, 0.17f, 0.21f, -0.17f, 0.82f, 0.1f, 0.12f, -0.1f, 0.95f, 0.05f, 0.06f, -0.05f, 1f, 0.02f, 0.045f, -0.04f) { Exponent = 2.1f };
                eyeT = 0.13f; eyeR = 0.0034f; gill = 0.25f;
                break;
            case "jerk":
                p = new Profile(0.07f, 0f, 0f, 0.008f, -0.008f, 0.04f, 0.05f, 0.06f, -0.05f, 0.14f, 0.08f, 0.095f, -0.09f, 0.35f, 0.088f, 0.11f, -0.1f,
                    0.62f, 0.075f, 0.09f, -0.085f, 0.84f, 0.05f, 0.06f, -0.055f, 0.95f, 0.03f, 0.04f, -0.035f, 1f, 0.012f, 0.035f, -0.03f) { Exponent = 2.6f };
                eyeT = 0.09f; eyeR = 0.0036f; gill = 0.17f;
                break;
            case "shad":
                p = new Profile(0.048f, 0f, 0f, 0.01f, -0.01f, 0.05f, 0.06f, 0.1f, -0.08f, 0.18f, 0.1f, 0.2f, -0.17f, 0.4f, 0.11f, 0.25f, -0.2f,
                    0.65f, 0.09f, 0.2f, -0.16f, 0.85f, 0.055f, 0.1f, -0.08f, 0.95f, 0.035f, 0.06f, -0.05f, 1f, 0.015f, 0.05f, -0.045f) { Exponent = 2.4f };
                eyeT = 0.12f; eyeR = 0.0034f; gill = 0.22f;
                break;
            case "vib":
                p = new Profile(0.05f, 0f, 0f, 0.02f, -0.02f, 0.06f, 0.04f, 0.14f, -0.1f, 0.2f, 0.065f, 0.27f, -0.2f, 0.45f, 0.07f, 0.3f, -0.22f,
                    0.7f, 0.06f, 0.22f, -0.16f, 0.88f, 0.04f, 0.12f, -0.09f, 1f, 0.015f, 0.06f, -0.05f) { Exponent = 2.8f };
                eyeT = 0.13f; eyeH = 0.15f; eyeR = 0.0034f; gill = 0.24f;
                break;
            case "pencil":
                p = new Profile(0.075f, 0f, 0f, 0.008f, -0.008f, 0.04f, 0.05f, 0.05f, -0.05f, 0.15f, 0.08f, 0.08f, -0.08f, 0.45f, 0.085f, 0.085f, -0.085f,
                    0.75f, 0.07f, 0.07f, -0.07f, 0.92f, 0.04f, 0.04f, -0.04f, 1f, 0.005f, 0.008f, -0.008f) { Exponent = 2f };
                eyeT = 0.08f; eyeH = 0.2f; eyeR = 0.0034f; gill = 0.15f;
                break;
            case "popper":
                p = new Profile(0.045f, 0f, 0.13f, 0.13f, -0.13f, 0.1f, 0.15f, 0.15f, -0.15f, 0.35f, 0.16f, 0.17f, -0.16f, 0.65f, 0.14f, 0.15f, -0.14f,
                    0.88f, 0.08f, 0.09f, -0.08f, 1f, 0.03f, 0.04f, -0.04f) { Exponent = 2f };
                eyeT = 0.14f; eyeH = 0.3f; eyeR = 0.0036f; gill = 0.26f; cup = 0.0045f;
                break;
            default:
                p = new Profile(0.056f, 0f, 0f, 0.01f, -0.01f, 0.04f, 0.05f, 0.06f, -0.05f, 0.12f, 0.085f, 0.085f, -0.075f, 0.3f, 0.11f, 0.1f, -0.085f,
                    0.55f, 0.1f, 0.085f, -0.075f, 0.8f, 0.065f, 0.055f, -0.045f, 0.93f, 0.04f, 0.035f, -0.03f, 1f, 0.012f, 0.03f, -0.03f) { Exponent = 2.3f };
                eyeT = 0.1f; eyeR = 0.0033f; gill = 0.19f;
                break;
        }
        HardBody(root, p, look, nose, eyeT, eyeH, eyeR, gill, cup);
        float L = p.Length;
        float gap = L * 0.13f;
        // Hook hangers on the belly and a tail hanger; the line tie on the lip, on the nose or (vib) on the back.
        float belly = shape is "vib" ? 0.4f : 0.38f;
        var bellyPt = p.Point(belly, 0f, nose);
        Eye(root, bellyPt, Vector3.Down, gap * 0.14f);
        HangTreble(root, bellyPt + Vector3.Down * gap * 0.28f, gap, 0.15f);
        var tailPt = p.Point(1f, MathF.PI * 0.5f, nose) with { X = 0f };
        Eye(root, tailPt + new Vector3(0f, (p.TopAt(1f) + p.BotAt(1f)) * 0.5f - tailPt.Y, 0f), Vector3.Forward, gap * 0.14f);
        HangTreble(root, new Vector3(0f, (p.TopAt(1f) + p.BotAt(1f)) * 0.5f, -nose - L - gap * 0.28f), gap, 0.7f);
        switch (shape)
        {
            case "crank":
                Lip(root, new Vector3(0f, p.BotAt(0.08f) * 0.6f, -nose - L * 0.05f), L * 0.5f, L * 0.42f, 28f, true);
                break;
            case "shad":
                Lip(root, new Vector3(0f, p.BotAt(0.08f) * 0.6f, -nose - L * 0.05f), L * 0.36f, L * 0.3f, 38f, true);
                break;
            case "minnow":
                Lip(root, new Vector3(0f, p.BotAt(0.06f) * 0.6f, -nose - L * 0.03f), L * 0.28f, L * 0.2f, 48f, true);
                break;
            case "vib":
                Eye(root, p.Point(0.18f, MathF.PI, nose), Vector3.Up, gap * 0.15f);
                break;
            default:
                Eye(root, new Vector3(0f, (p.TopAt(0f) + p.BotAt(0f)) * 0.5f, -nose + 0.0005f), Vector3.Back, gap * 0.15f);
                break;
        }
        return null;
    }

    /// <summary>A jointed swimbait: three segments on pin hinges (the rear one swings on <c>tail</c>), a soft paddle tail.</summary>
    private static Node3D Swimbait(Node3D root, Look look)
    {
        const float nose = 0.002f;
        var p = new Profile(0.09f, 0f, 0f, 0.01f, -0.01f, 0.05f, 0.05f, 0.07f, -0.06f, 0.15f, 0.08f, 0.12f, -0.1f, 0.35f, 0.095f, 0.14f, -0.11f,
            0.6f, 0.08f, 0.12f, -0.09f, 0.8f, 0.055f, 0.08f, -0.06f, 0.92f, 0.03f, 0.045f, -0.035f, 1f, 0.016f, 0.03f, -0.03f) { Exponent = 2.3f };
        var mat = HardBody(root, p, look, nose, 0.09f, 0.28f, 0.004f, 0.18f, 0f, 0f, 0.36f);
        float L = p.Length;
        var mid = new Node3D { Position = new Vector3(0f, 0f, -nose - L * 0.37f) };
        root.AddChild(mid);
        Part(mid, LureMesh.Loft(p, 0.375f, 0.68f, nose: nose), mat, new Vector3(0f, 0f, nose + L * 0.37f));
        var back = new Node3D { Position = new Vector3(0f, 0f, -L * 0.32f) };
        mid.AddChild(back);
        Part(back, LureMesh.Loft(p, 0.695f, 1f, nose: nose), mat, new Vector3(0f, 0f, nose + L * 0.69f));
        // The hinge pins between the segments.
        foreach (var (node, z) in new[] { (root, -nose - L * 0.37f), (mid, -L * 0.32f) })
            Part(node == root ? root : mid, new CylinderMesh { TopRadius = 0.0005f, BottomRadius = 0.0005f, Height = L * 0.16f, RadialSegments = 6 }, Steel,
                new Vector3(0f, 0f, node == root ? z : z));
        // A soft paddle tail on a thin stalk.
        var paddle = LureMesh.Plate(u => 0.5f * MathF.Sqrt(MathF.Max(MathF.Sin(MathF.PI * u), 0f)), L * 0.2f, L * 0.18f, L * 0.01f, L * 0.012f, 12, 8);
        var tailNode = new Node3D { Position = new Vector3(0f, (p.TopAt(1f) + p.BotAt(1f)) * 0.5f, -L * 0.31f - nose) };
        back.AddChild(tailNode);
        var softSet = LurePainter.Paint(new LurePainter.Spec(LurePainter.Kind.Soft, look.Main, look.Accent, "plain", false, null, 1f, look.Seed % 1000));
        Part(tailNode, paddle, Plastic(softSet, look.Main), new Vector3(0f, -L * 0.1f, -L * 0.04f), new Vector3(Mathf.Pi / 2f, 0f, 0f));
        var gap = L * 0.1f;
        Lip(root, new Vector3(0f, p.BotAt(0.06f) * 0.6f, -nose - L * 0.03f), L * 0.16f, L * 0.14f, 40f, true);
        HangTreble(root, p.Point(0.22f, 0f, nose) + Vector3.Down * gap * 0.25f, gap, 0.15f);
        HangTreble(mid, new Vector3(0f, p.BotAt(0.55f), -L * 0.17f) + Vector3.Down * gap * 0.25f, gap, 0.15f);
        return back;
    }

    // ------------------------------------------------------------------ spoons
    private static void Spoon(Node3D root, string shape, Look look)
    {
        if (shape == "jig")
        {
            // A casting jig: a long lead slab, foil and paint, a red assist hook at the nose, a treble at the tail.
            var p = new Profile(0.064f, 0f, 0f, 0.02f, -0.02f, 0.06f, 0.05f, 0.09f, -0.07f, 0.3f, 0.07f, 0.11f, -0.09f, 0.7f, 0.065f, 0.1f, -0.08f,
                0.92f, 0.04f, 0.06f, -0.05f, 1f, 0.015f, 0.03f, -0.025f) { Exponent = 3.2f };
            HardBody(root, p, look with { Pattern = look.Pattern == "plain" ? "stripes" : look.Pattern }, 0.002f, 0.08f, 0.2f, 0.0028f, 0.16f);
            Eye(root, new Vector3(0f, 0f, -0.0015f), Vector3.Back, 0.0016f);
            var assist = new Node3D { Position = new Vector3(0f, 0.001f, -0.004f), Rotation = new Vector3(0.25f, 0f, 0f) };
            root.AddChild(assist);
            Part(assist, LureMesh.Tube(new[] { Vector3.Zero, new Vector3(0f, 0.004f, -0.006f), new Vector3(0f, 0.005f, -0.012f) }, _ => 0.0007f, 5),
                PaintMat(new Color(0.85f, 0.1f, 0.08f), 0.7f), Vector3.Zero);
            Part(assist, LureMesh.Single(1.6f), BlackNickel, new Vector3(0f, 0.005f, -0.012f), null, Vector3.One * 0.006f);
            HangTreble(root, new Vector3(0f, 0f, -0.068f), 0.008f, 0.7f);
            return;
        }
        Func<float, float> outline;
        float length, width, dish;
        switch (shape)
        {
            case "narrow":
                outline = u => 0.5f * MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * MathF.Pow(u, 0.85f)), 0f), 0.75f);
                length = 0.062f; width = 0.014f; dish = 0.0022f;
                break;
            case "wide":
                outline = u => 0.5f * MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * MathF.Pow(u, 0.9f)), 0f), 0.55f);
                length = 0.05f; width = 0.03f; dish = 0.0045f;
                break;
            default:
                outline = u => 0.5f * MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * MathF.Pow(u, 0.75f)), 0f), 0.65f);
                length = 0.054f; width = 0.022f; dish = 0.0035f;
                break;
        }
        var plate = new Node3D { Position = new Vector3(0f, 0f, -0.004f) };
        root.AddChild(plate);
        var face = LurePainter.Paint(new LurePainter.Spec(LurePainter.Kind.Plate, look.Main, look.Accent, look.Pattern, look.Metal, null, 1f, look.Seed % 1000));
        var mesh = LureMesh.Plate(outline, length, width, dish, 0.0009f);
        var mi = Part(plate, mesh, Skin(face, clearcoat: !look.Metal), Vector3.Zero);
        mi.SetSurfaceOverrideMaterial(0, Skin(face, clearcoat: !look.Metal));
        mi.SetSurfaceOverrideMaterial(1, look.Metal ? MetalMat(look.Main, 0.1f) : MetalMat(new Color(0.85f, 0.86f, 0.88f), 0.1f));
        mi.MaterialOverride = null;
        // Split rings through the holes at both ends; the treble at the back.
        Part(root, LureMesh.Ring(0.13f), Steel, new Vector3(0f, 0.0005f, -0.0045f), new Vector3(0f, Mathf.Pi / 2f, 0f), Vector3.One * 0.0028f);
        float gap = 0.0085f;
        var tail = new Vector3(0f, dish * 0.4f, -0.004f - length + 0.002f);
        HangTreble(root, tail + new Vector3(0f, 0f, -0.002f), gap, 1.45f);
    }

    // ------------------------------------------------------------------ spinners
    private static Node3D Blade(Node3D parent, Look look, bool willow, float length, float width)
    {
        Func<float, float> outline = willow
            ? u => 0.5f * MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * u), 0f), 0.85f)
            : u => 0.5f * MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * MathF.Pow(u, 0.8f)), 0f), 0.5f);
        var face = LurePainter.Paint(new LurePainter.Spec(LurePainter.Kind.Blade, look.Metal ? look.Main : new Color(0.88f, 0.89f, 0.92f),
            look.Accent, look.Pattern, true, null, 1f, look.Seed % 1000));
        var mi = Part(parent, LureMesh.Plate(outline, length, width, width * 0.16f, 0.0006f, 20, 10), Steel, Vector3.Zero);
        mi.MaterialOverride = null;
        mi.SetSurfaceOverrideMaterial(0, Skin(face, clearcoat: false));
        mi.SetSurfaceOverrideMaterial(1, MetalMat(look.Metal ? look.Main : new Color(0.88f, 0.89f, 0.92f), 0.08f));
        return mi;
    }

    /// <summary>The blade on a clevis: its front at the shaft, flaring out and back, its painted face outwards.</summary>
    private static Node3D SpinBlade(Node3D spin, Look look, bool willow, float length, float width, float flare = 0.42f)
    {
        var holder = new Node3D { Position = new Vector3(0.0012f, 0f, 0f) };
        spin.AddChild(holder);
        holder.Basis = new Basis(Vector3.Up, flare) * new Basis(Vector3.Back, -Mathf.Pi / 2f);
        Blade(holder, look, willow, length, width);
        // The clevis: a little U of wire through the blade's hole round the shaft.
        Part(spin, LureMesh.Tube(LureMesh.Arc(Vector3.Zero, Vector3.Right, Vector3.Up, 0.0017f, -1.4f, 1.4f, 8), _ => 0.00028f, 4), Wire,
            new Vector3(0f, 0f, -0.0008f));
        return holder;
    }

    private static Node3D Spinner(Node3D root, string shape, Look look)
    {
        if (shape == "spinnerbait")
            return Spinnerbait(root, look);
        if (shape == "tailspinner")
            return TailSpinner(root, look);
        // The wire: the line loop at the front, the shaft back to the treble's loop.
        Part(root, LureMesh.Tube(LureMesh.Arc(new Vector3(0f, 0f, 0.0018f), Vector3.Back * -1f, Vector3.Up, 0.0018f, 0f, Mathf.Tau, 12), _ => 0.0004f, 5), Wire, Vector3.Zero);
        Part(root, LureMesh.Tube(new[] { Vector3.Zero, new Vector3(0f, 0f, -0.058f) }, _ => 0.0004f, 5), Wire, Vector3.Zero);
        float zBlade = -0.004f;
        if (shape == "headed")
        {
            // A painted lead head in front of the blade: it casts far and sinks.
            var hp = new Profile(0.014f, 0f, 0f, 0.02f, -0.02f, 0.15f, 0.26f, 0.28f, -0.26f, 0.6f, 0.36f, 0.38f, -0.36f, 1f, 0.12f, 0.14f, -0.12f);
            HardBody(root, hp, look with { Pattern = "plain" }, 0.002f, 0.4f, 0.25f, 0.0018f, 0.9f, 0f, 0f, 1f, LurePainter.Kind.Head);
            zBlade = -0.017f;
        }
        // Beads, the brass body and a tail bead.
        Part(root, Ball(12), Brass, new Vector3(0f, 0f, zBlade - 0.006f), null, Vector3.One * 0.0034f);
        Part(root, Ball(12), Brass, new Vector3(0f, 0f, zBlade - 0.0095f), null, Vector3.One * 0.004f);
        var bp = new Profile(0.018f, 0f, 0.03f, 0.03f, -0.03f, 0.2f, 0.16f, 0.16f, -0.16f, 0.6f, 0.26f, 0.26f, -0.26f, 0.9f, 0.2f, 0.2f, -0.2f, 1f, 0.06f, 0.06f, -0.06f) { Exponent = 2f };
        var bodyMat = look.Metal ? MetalMat(look.Main.Lerp(new Color(0.88f, 0.68f, 0.32f), 0.4f), 0.2f) : PaintMat(look.Main, 0.3f);
        Part(root, LureMesh.Loft(bp, nose: -zBlade + 0.012f, rings: 24, sides: 20), bodyMat, Vector3.Zero);
        Part(root, Ball(12), PaintMat(look.Pattern == "dots" ? look.Accent : new Color(0.85f, 0.08f, 0.06f), 0.25f),
            new Vector3(0f, 0f, zBlade - 0.0325f), null, Vector3.One * 0.0042f);
        var spin = new Node3D { Position = new Vector3(0f, 0f, zBlade) };
        root.AddChild(spin);
        if (shape == "long")
            SpinBlade(spin, look, true, 0.027f, 0.011f, 0.32f);
        else
            SpinBlade(spin, look, false, 0.019f, 0.015f);
        // The treble's loop at the shaft's end.
        Part(root, LureMesh.Tube(LureMesh.Arc(new Vector3(0f, 0f, -0.0598f), Vector3.Back, Vector3.Up, 0.0018f, 0f, Mathf.Tau, 12), _ => 0.0004f, 5), Wire, Vector3.Zero);
        HangTreble(root, new Vector3(0f, 0f, -0.0615f), 0.0075f, 1.45f);
        if (shape == "tail")
            Dressing(root, new Vector3(0f, 0f, -0.06f), look.Accent, 0.026f);
        return spin;
    }

    /// <summary>A tuft of hair or feathers tied round the treble's shank, trailing back.</summary>
    private static void Dressing(Node3D parent, Vector3 at, Color colour, float length)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var rng = new Random(11);
        for (int i = 0; i < 40; i++)
        {
            float a = i / 40f * Mathf.Tau + (float)rng.NextDouble() * 0.2f;
            float spread = 0.0025f + (float)rng.NextDouble() * 0.002f;
            var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
            float len = length * (0.75f + 0.25f * (float)rng.NextDouble());
            var p0 = at + dir * 0.0012f;
            var p1 = at + dir * (0.0012f + spread * 0.6f) + new Vector3(0f, 0f, -len * 0.5f);
            var p2 = at + dir * (0.0012f + spread) + new Vector3(0f, -0.001f, -len);
            LureMesh.TubeInto(st, new[] { p0, p1, p2 }, f => 0.00022f * (1f - f * 0.8f), 3);
        }
        Part(parent, st.Commit(), new StandardMaterial3D
        {
            AlbedoColor = colour, Roughness = 0.75f, BacklightEnabled = true, Backlight = colour * 0.5f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        }, Vector3.Zero);
    }

    /// <summary>A spinnerbait: a bent safety-pin wire, a willow blade on a swivel at the upper arm's end, a painted lead
    /// head with an eye on the lower arm and a silicone skirt over the upturned hook.</summary>
    private static Node3D Spinnerbait(Node3D root, Look look)
    {
        var upper = new[] { Vector3.Zero, new Vector3(0f, 0.008f, -0.01f), new Vector3(0f, 0.016f, -0.022f), new Vector3(0f, 0.019f, -0.034f) };
        var lower = new[] { Vector3.Zero, new Vector3(0f, -0.003f, -0.006f), new Vector3(0f, -0.005f, -0.011f) };
        Part(root, LureMesh.Tube(upper, _ => 0.00045f, 5), Wire, Vector3.Zero);
        Part(root, LureMesh.Tube(lower, _ => 0.00045f, 5), Wire, Vector3.Zero);
        Part(root, LureMesh.Tube(LureMesh.Arc(new Vector3(0f, 0.0006f, 0.0012f), Vector3.Down, Vector3.Back * -1f, 0.0012f, 0f, Mathf.Tau, 10), _ => 0.0004f, 4), Wire, Vector3.Zero);
        // Swivel and blade.
        Part(root, new CylinderMesh { TopRadius = 0.0007f, BottomRadius = 0.0007f, Height = 0.004f, RadialSegments = 8 }, Brass,
            new Vector3(0f, 0.0195f, -0.036f), new Vector3(Mathf.Pi / 2f, 0f, 0f));
        var spin = new Node3D { Position = new Vector3(0f, 0.0195f, -0.039f) };
        root.AddChild(spin);
        SpinBlade(spin, look with { Metal = true, Main = look.Metal ? look.Main : new Color(0.92f, 0.76f, 0.38f) }, true, 0.026f, 0.01f, 0.3f);
        // The head.
        var hp = new Profile(0.013f, 0f, 0f, 0.03f, -0.03f, 0.2f, 0.25f, 0.28f, -0.3f, 0.6f, 0.36f, 0.4f, -0.42f, 1f, 0.2f, 0.22f, -0.24f);
        var head = new Node3D { Position = new Vector3(0f, -0.005f, -0.009f) };
        root.AddChild(head);
        HardBody(head, hp, look with { Pattern = "plain" }, 0f, 0.42f, 0.2f, 0.0017f, 0.9f, 0f, 0f, 1f, LurePainter.Kind.Head);
        // Hook: shank through the head, the point up at the back.
        Part(root, LureMesh.Single(3.6f), BlackNickel, new Vector3(0f, -0.0055f, -0.012f), null, Vector3.One * 0.0065f);
        // Silicone skirt.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var st2 = new SurfaceTool();
        st2.Begin(Mesh.PrimitiveType.Triangles);
        var rng = new Random(look.Seed);
        for (int i = 0; i < 46; i++)
        {
            float a = i / 46f * Mathf.Tau;
            var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
            var c0 = new Vector3(0f, -0.005f, -0.022f);
            float len = 0.028f + (float)rng.NextDouble() * 0.008f;
            var pts = new[] { c0 + dir * 0.002f, c0 + dir * 0.004f + new Vector3(0f, 0f, -len * 0.4f), c0 + dir * 0.0065f + new Vector3(0f, -0.002f, -len) };
            LureMesh.TubeInto(i % 3 == 0 ? st2 : st, pts, f => 0.00055f * (1f - f * 0.5f), 3);
        }
        Part(root, st.Commit(), new StandardMaterial3D { AlbedoColor = look.Accent, Roughness = 0.45f, CullMode = BaseMaterial3D.CullModeEnum.Disabled }, Vector3.Zero);
        Part(root, st2.Commit(), new StandardMaterial3D { AlbedoColor = look.Main, Roughness = 0.45f, CullMode = BaseMaterial3D.CullModeEnum.Disabled }, Vector3.Zero);
        Part(root, new CylinderMesh { TopRadius = 0.0028f, BottomRadius = 0.0028f, Height = 0.003f, RadialSegments = 12 }, PaintMat(look.Main.Darkened(0.4f), 0.5f),
            new Vector3(0f, -0.005f, -0.0215f), new Vector3(Mathf.Pi / 2f, 0f, 0f));
        return spin;
    }

    /// <summary>A tail spinner: a heavy painted lead body like a small fish, the blade turning behind it.</summary>
    private static Node3D TailSpinner(Node3D root, Look look)
    {
        var p = new Profile(0.034f, 0f, 0f, 0.02f, -0.02f, 0.08f, 0.1f, 0.16f, -0.12f, 0.3f, 0.15f, 0.3f, -0.18f, 0.6f, 0.13f, 0.28f, -0.14f,
            0.85f, 0.08f, 0.14f, -0.07f, 1f, 0.03f, 0.05f, -0.03f) { Exponent = 2.4f };
        HardBody(root, p, look with { Pattern = look.Metal ? "stripes" : look.Pattern }, 0.002f, 0.16f, 0.3f, 0.0026f, 0.26f);
        Eye(root, p.Point(0.2f, MathF.PI, 0.002f), Vector3.Up, 0.0012f);
        HangTreble(root, p.Point(0.45f, 0f, 0.002f) + Vector3.Down * 0.0015f, 0.0075f, 0.15f);
        Part(root, LureMesh.Tube(new[] { new Vector3(0f, 0.001f, -0.034f), new Vector3(0f, 0.001f, -0.042f) }, _ => 0.0004f, 5), Wire, Vector3.Zero);
        Part(root, Ball(10), Brass, new Vector3(0f, 0.001f, -0.04f), null, Vector3.One * 0.0025f);
        var spin = new Node3D { Position = new Vector3(0f, 0.001f, -0.042f) };
        root.AddChild(spin);
        SpinBlade(spin, look with { Metal = true, Main = look.Metal ? look.Main : new Color(0.88f, 0.89f, 0.92f), Pattern = "plain" }, true, 0.017f, 0.009f, 0.35f);
        return spin;
    }

    // ------------------------------------------------------------------ soft baits on a jig head
    /// <summary>A round painted lead jig head with an eye, the line tie on top; returns its back (where the bait sits).</summary>
    private static float JigHead(Node3D root, Look look)
    {
        var hp = new Profile(0.012f, 0f, 0f, 0.05f, -0.05f, 0.18f, 0.3f, 0.34f, -0.3f, 0.5f, 0.42f, 0.44f, -0.42f, 0.8f, 0.36f, 0.38f, -0.36f,
            1f, 0.16f, 0.18f, -0.16f) { Exponent = 2f };
        bool red = look.Pattern == "redhead";
        var headLook = look with { Main = red ? new Color(0.82f, 0.1f, 0.08f) : look.Main.Darkened(0.2f), Pattern = "plain", Metal = !red };
        HardBody(root, hp, headLook, 0f, 0.45f, 0.18f, 0.0019f, 0.95f, 0f, 0f, 1f, LurePainter.Kind.Head);
        Eye(root, hp.Point(0.35f, MathF.PI, 0f), Vector3.Up, 0.0012f);
        // The collar the plastic is pushed onto.
        Part(root, new CylinderMesh { TopRadius = 0.0018f, BottomRadius = 0.0021f, Height = 0.003f, RadialSegments = 10 }, headLook.Metal ? Steel : PaintMat(headLook.Main, 0.35f),
            new Vector3(0f, 0f, -0.0125f), new Vector3(Mathf.Pi / 2f, 0f, 0f));
        return -0.013f;
    }

    private static Node3D Softbait(Node3D root, string shape, Look look)
    {
        if (shape == "frog")
            return Frog(root, look);
        float back = JigHead(root, look);
        var set = LurePainter.Paint(new LurePainter.Spec(LurePainter.Kind.Soft, look.Main, look.Accent, look.Pattern, false, null, 0.6f, look.Seed % 1000));
        var plastic = Plastic(set, look.Main);
        float nose = -back;
        Node3D tail;
        float hookGap = 0.0055f, hookShank = 3.2f;
        switch (shape)
        {
            case "twister":
            {
                var p = new Profile(0.026f, 0f, 0.1f, 0.1f, -0.1f, 0.5f, 0.11f, 0.11f, -0.1f, 0.85f, 0.08f, 0.08f, -0.07f, 1f, 0.05f, 0.06f, -0.05f)
                    { Ribs = (t, th) => 1f - 0.07f * MathF.Pow(MathF.Abs(MathF.Sin(MathF.PI * t * 11f)), 6f) * (t < 0.8f ? 1f : 0f) };
                Part(root, LureMesh.Loft(p, nose: nose), plastic, Vector3.Zero);
                tail = new Node3D { Position = new Vector3(0f, 0f, -nose - p.Length + 0.001f) };
                root.AddChild(tail);
                Part(tail, Ribbon(0.03f, 0.008f, 1.75f), plastic, Vector3.Zero);
                hookShank = 2.6f;
                break;
            }
            case "worm":
            {
                var p = new Profile(0.075f, 0f, 0.055f, 0.055f, -0.055f, 0.3f, 0.06f, 0.06f, -0.06f, 0.75f, 0.045f, 0.045f, -0.045f, 1f, 0.006f, 0.006f, -0.006f)
                    { Ribs = (t, th) => (1f - 0.05f * MathF.Pow(MathF.Abs(MathF.Sin(MathF.PI * t * 40f)), 4f)) * (t > 0.18f && t < 0.25f ? 1.08f : 1f) };
                tail = Split(root, p, 0.6f, nose, plastic);
                break;
            }
            case "stick":
            {
                var p = new Profile(0.08f, 0f, 0.07f, 0.07f, -0.07f, 0.5f, 0.065f, 0.065f, -0.065f, 0.95f, 0.06f, 0.06f, -0.06f, 1f, 0.035f, 0.035f, -0.035f) { Exponent = 2f };
                tail = Split(root, p, 0.65f, nose, plastic);
                break;
            }
            case "craw":
            {
                // Flipper tail at the head, a segmented abdomen, the carapace, legs and the two claws trailing behind.
                var p = new Profile(0.03f, 0f, 0.14f, 0.1f, -0.1f, 0.15f, 0.12f, 0.1f, -0.08f, 0.5f, 0.18f, 0.16f, -0.1f, 0.85f, 0.2f, 0.18f, -0.1f, 1f, 0.14f, 0.12f, -0.07f)
                    { Exponent = 2.6f, Ribs = (t, th) => t < 0.5f ? 1f - 0.08f * MathF.Pow(MathF.Abs(MathF.Sin(MathF.PI * t * 12f)), 4f) : 1f };
                Part(root, LureMesh.Loft(p, nose: nose), plastic, Vector3.Zero);
                var legs = new SurfaceTool();
                legs.Begin(Mesh.PrimitiveType.Triangles);
                foreach (float side in new[] { -1f, 1f })
                {
                    for (int l = 0; l < 4; l++)
                    {
                        float z = -nose - 0.018f - l * 0.0028f;
                        LureMesh.TubeInto(legs, new[] { new Vector3(side * 0.003f, -0.001f, z), new Vector3(side * 0.007f, -0.003f, z + 0.001f), new Vector3(side * 0.009f, -0.006f, z + 0.002f) },
                            f => 0.0005f * (1f - f * 0.6f), 4);
                    }
                    LureMesh.TubeInto(legs, new[] { new Vector3(side * 0.0015f, 0.001f, -nose - 0.03f), new Vector3(side * 0.006f, 0.003f, -nose - 0.045f), new Vector3(side * 0.01f, 0.002f, -nose - 0.065f) },
                        f => 0.0003f * (1f - f * 0.7f), 3);
                }
                Part(root, legs.Commit(), plastic, Vector3.Zero);
                tail = new Node3D { Position = new Vector3(0f, 0f, -nose - 0.029f) };
                root.AddChild(tail);
                var claw = new Profile(0.022f, 0f, 0.05f, 0.04f, -0.04f, 0.3f, 0.2f, 0.07f, -0.07f, 0.7f, 0.26f, 0.06f, -0.06f, 1f, 0.02f, 0.01f, -0.01f) { Exponent = 2.4f };
                foreach (float side in new[] { -1f, 1f })
                {
                    var c = new Node3D { Position = new Vector3(side * 0.003f, 0f, 0f), Rotation = new Vector3(0f, side * 0.35f, 0f) };
                    tail.AddChild(c);
                    Part(c, LureMesh.Loft(claw, rings: 24, sides: 16), plastic, Vector3.Zero);
                }
                hookShank = 2.4f;
                break;
            }
            default:
            {
                // Ripper: a ribbed body, a thin stalk and the paddle (boot) tail that kicks.
                var p = new Profile(0.042f, 0f, 0.1f, 0.1f, -0.1f, 0.5f, 0.11f, 0.115f, -0.1f, 0.85f, 0.07f, 0.075f, -0.065f, 1f, 0.035f, 0.04f, -0.035f)
                    { Ribs = (t, th) => 1f - 0.075f * MathF.Pow(MathF.Abs(MathF.Sin(MathF.PI * t * 13f)), 6f) * (t > 0.2f && t < 0.75f ? 1f : 0f) };
                Part(root, LureMesh.Loft(p, nose: nose), plastic, Vector3.Zero);
                tail = new Node3D { Position = new Vector3(0f, 0f, -nose - p.Length + 0.001f) };
                root.AddChild(tail);
                var paddle = LureMesh.Plate(u => 0.5f * MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * u), 0f), 0.6f), 0.013f, 0.011f, 0.0012f, 0.0016f, 12, 8);
                Part(tail, paddle, plastic, new Vector3(0f, -0.0063f, -0.0052f), new Vector3(Mathf.Pi / 2f - 0.25f, 0f, 0f));
                Part(tail, LureMesh.Tube(new[] { Vector3.Zero, new Vector3(0f, 0f, -0.0035f) }, f => 0.0016f * (1f - f * 0.3f), 8), plastic, Vector3.Zero);
                break;
            }
        }
        // The jig hook: the shank inside the plastic, the bend and point out of the back.
        Part(root, LureMesh.Single(hookShank), BlackNickel, new Vector3(0f, -0.0008f, back + 0.002f), null, Vector3.One * hookGap);
        return tail;
    }

    /// <summary>
    /// A soft body in two parts that bends at <paramref name="at"/>: the rear part (returned, it swings) starts a little
    /// inside the front one and is a hair thinner there, so the joint never opens a gap when it bends.
    /// </summary>
    private static Node3D Split(Node3D root, Profile p, float at, float nose, Material plastic)
    {
        Part(root, LureMesh.Loft(p, 0f, at, nose: nose), plastic, Vector3.Zero);
        var tail = new Node3D { Position = new Vector3(0f, 0f, -nose - p.Length * at) };
        root.AddChild(tail);
        var rear = new Profile(p.Length, Rows(p)) { Exponent = p.Exponent, Ribs = (t, th) => (p.Ribs?.Invoke(t, th) ?? 1f) * (t < at ? 0.96f : 1f) };
        Part(tail, LureMesh.Loft(rear, at - 0.05f, 1f, nose: nose), plastic, new Vector3(0f, 0f, nose + p.Length * at));
        return tail;
    }

    private static float[] Rows(Profile p)
    {
        var rows = new float[p.T.Length * 4];
        for (int i = 0; i < p.T.Length; i++)
        {
            rows[i * 4] = p.T[i]; rows[i * 4 + 1] = p.W[i]; rows[i * 4 + 2] = p.Top[i]; rows[i * 4 + 3] = p.Bot[i];
        }
        return rows;
    }

    /// <summary>A curly (twister) tail: a flat crescent of plastic in the vertical plane.</summary>
    private static ArrayMesh Ribbon(float length, float width, float turns)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int n = 30;
        var pts = new List<Vector3>();
        for (int i = 0; i <= n; i++)
        {
            float s = i / (float)n;
            float a = s * turns * MathF.PI;
            float r = length / (turns * MathF.PI) * (1.2f - 0.5f * s);
            pts.Add(new Vector3(0f, r * (1f - MathF.Cos(a)), -r * MathF.Sin(a) - s * length * 0.2f));
        }
        for (int face = 0; face < 2; face++)
        {
            float x = face == 0 ? 0.0007f : -0.0007f;
            var nrm = new Vector3(face == 0 ? 1f : -1f, 0f, 0f);
            for (int i = 0; i < n; i++)
            {
                var d0 = (pts[i + 1] - pts[Math.Max(i - 1, 0)]).Normalized();
                var d1 = (pts[Math.Min(i + 2, n)] - pts[i]).Normalized();
                var o0 = new Vector3(0f, -d0.Z, d0.Y) * width * (1f - i / (float)n * 0.85f) * 0.5f;
                var o1 = new Vector3(0f, -d1.Z, d1.Y) * width * (1f - (i + 1) / (float)n * 0.85f) * 0.5f;
                var a = pts[i] + o0 + new Vector3(x, 0, 0);
                var b = pts[i] - o0 + new Vector3(x, 0, 0);
                var c = pts[i + 1] - o1 + new Vector3(x, 0, 0);
                var d = pts[i + 1] + o1 + new Vector3(x, 0, 0);
                foreach (var (p, uv) in new[] { (a, 0f), (b, 1f), (c, 1f), (a, 0f), (c, 1f), (d, 0f) })
                {
                    st.SetNormal(nrm);
                    st.SetUV(new Vector2(0.9f + 0.1f * i / n, uv * 0.5f + 0.25f));
                    st.AddVertex(p);
                }
            }
        }
        st.GenerateTangents();
        var mesh = st.Commit();
        return mesh;
    }

    /// <summary>A hollow soft frog for the lily pads: a broad body, bulging eyes, two upturned hook points along its
    /// back and legs of silicone strands (on <c>tail</c>).</summary>
    private static Node3D Frog(Node3D root, Look look)
    {
        var set = LurePainter.Paint(new LurePainter.Spec(LurePainter.Kind.Soft, look.Main, look.Accent, look.Pattern == "dots" ? "glitter" : "halves",
            false, null, 1f, look.Seed % 1000));
        var skin = Plastic(set, look.Main);
        var p = new Profile(0.036f, 0f, 0f, 0.02f, -0.02f, 0.08f, 0.17f, 0.14f, -0.08f, 0.3f, 0.3f, 0.2f, -0.12f, 0.6f, 0.34f, 0.2f, -0.13f,
            0.85f, 0.26f, 0.16f, -0.1f, 1f, 0.1f, 0.08f, -0.05f) { Exponent = 2.4f };
        Part(root, LureMesh.Loft(p, nose: 0.002f), skin, Vector3.Zero);
        Eye(root, new Vector3(0f, 0f, -0.0015f), Vector3.Back, 0.0013f);
        foreach (float x in new[] { -0.0055f, 0.0055f })
        {
            var at = new Vector3(x, 0.0055f, -0.011f);
            Part(root, Ball(14), PaintMat(new Color(0.95f, 0.85f, 0.25f), 0.3f), at, null, Vector3.One * 0.0055f);
            Part(root, Ball(10), Black, at + new Vector3(x * 0.25f, 0.0011f, 0.0012f), null, new Vector3(0.0022f, 0.0032f, 0.0022f));
            EyeDome(root, at + new Vector3(x * 0.2f, 0.0012f, 0.0006f), new Vector3(x * 120f, 0.6f, 0.4f).Normalized(), 0.0024f);
            // Hook points riding up along its back.
            var pts = new[] { new Vector3(0f, -0.001f, -0.01f), new Vector3(x * 0.5f, -0.002f, -0.024f), new Vector3(x * 1.1f, 0.0f, -0.036f),
                new Vector3(x * 1.2f, 0.004f, -0.034f), new Vector3(x * 1.05f, 0.0068f, -0.026f) };
            Part(root, LureMesh.Tube(pts, f => f < 0.8f ? 0.00055f : 0.00055f * (1f - (f - 0.8f) * 4f), 5), BlackNickel, Vector3.Zero);
        }
        var tail = new Node3D { Position = new Vector3(0f, 0f, -0.037f) };
        root.AddChild(tail);
        var legs = new SurfaceTool();
        legs.Begin(Mesh.PrimitiveType.Triangles);
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 5; i++)
                LureMesh.TubeInto(legs, new[] { new Vector3(side * 0.003f, 0f, 0f), new Vector3(side * (0.005f + i * 0.0012f), -0.0005f, -0.012f),
                    new Vector3(side * (0.006f + i * 0.0025f), -0.001f, -0.026f - i * 0.0015f) }, f => 0.00045f * (1f - f * 0.4f), 4);
        Part(tail, legs.Commit(), new StandardMaterial3D { AlbedoColor = look.Accent, Roughness = 0.5f }, Vector3.Zero);
        return tail;
    }

    // ------------------------------------------------------------------ flies
    private static void Fly(Node3D root, string shape, Look look)
    {
        var thread = new StandardMaterial3D { AlbedoColor = look.Main, Roughness = 0.9f };
        var hackleMat = new StandardMaterial3D
        {
            AlbedoColor = look.Accent, Roughness = 0.85f, BacklightEnabled = true, Backlight = look.Accent * 0.5f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        bool streamer = shape == "streamer";
        float len = streamer ? 0.026f : 0.012f;
        // The bronze hook: eye at the front, the shank along the body, the bend and point hanging below.
        var hook = Part(root, LureMesh.Single(streamer ? 4.5f : 2.4f), Bronze, new Vector3(0f, -0.0005f, -0.001f), new Vector3(0f, 0f, Mathf.Pi), Vector3.One * (len / (streamer ? 5f : 2.9f)));
        hook.Rotation = new Vector3(0f, 0f, Mathf.Pi);
        // The body: tapered thread or dubbing.
        var body = new Profile(len, 0f, 0.06f, 0.06f, -0.06f, 0.3f, 0.13f, 0.13f, -0.13f, 0.75f, 0.11f, 0.11f, -0.11f, 1f, 0.05f, 0.05f, -0.05f)
            { Ribs = shape == "nymph" ? (t, th) => 1f + 0.08f * MathF.Sin(t * 60f + th * 2f) : null };
        Part(root, LureMesh.Loft(body, nose: 0.002f, rings: 20, sides: 12), thread, Vector3.Zero);
        var fibres = new SurfaceTool();
        fibres.Begin(Mesh.PrimitiveType.Triangles);
        var rng = new Random(look.Seed);
        switch (shape)
        {
            case "nymph":
                Part(root, Ball(14), look.Pattern == "redhead" ? Brass : PaintMat(look.Accent, 0.4f), new Vector3(0f, 0f, -0.0005f), null, Vector3.One * 0.0042f);
                for (int i = 0; i < 10; i++)
                {
                    float a = MathF.PI * (0.2f + 0.6f * i / 9f);
                    var d = new Vector3(MathF.Cos(a) * 0.4f, -MathF.Sin(a), 0f);
                    LureMesh.TubeInto(fibres, new[] { new Vector3(0f, 0f, -0.003f), new Vector3(0f, 0f, -0.003f) + d * 0.004f + new Vector3(0f, 0f, -0.004f) }, f => 0.00012f, 3);
                }
                for (int i = 0; i < 6; i++)
                    LureMesh.TubeInto(fibres, new[] { new Vector3(0f, 0f, -len), new Vector3((i - 2.5f) * 0.0004f, 0.0003f, -len - 0.004f) }, f => 0.0001f, 3);
                break;
            case "wet":
                for (int i = 0; i < 14; i++)
                {
                    float a = MathF.PI * (0.15f + 0.7f * i / 13f);
                    LureMesh.TubeInto(fibres, new[] { new Vector3(0f, 0f, -0.002f), new Vector3(MathF.Cos(a) * 0.002f, -MathF.Sin(a) * 0.004f, -0.008f) }, f => 0.00012f, 3);
                }
                Part(root, LureMesh.Plate(u => 0.5f * MathF.Sqrt(MathF.Max(MathF.Sin(MathF.PI * u), 0f)), 0.012f, 0.004f, 0f, 0.0003f, 8, 4),
                    new StandardMaterial3D { AlbedoColor = look.Main.Lightened(0.3f), Roughness = 0.8f, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
                    new Vector3(0f, 0.0018f, -0.002f), new Vector3(0.22f, 0f, Mathf.Pi / 2f));
                break;
            case "streamer":
                // Long feather wings over the body, painted eyes on a cone head.
                for (int w = 0; w < 4; w++)
                    Part(root, LureMesh.Plate(u => 0.5f * MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * MathF.Pow(u, 0.6f)), 0f), 0.8f), 0.05f, 0.006f, 0.0006f, 0.0002f, 14, 4),
                        new StandardMaterial3D { AlbedoColor = (w % 2 == 0 ? look.Main : look.Main.Lightened(0.2f)), Roughness = 0.85f, BacklightEnabled = true,
                            Backlight = look.Main * 0.4f, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
                        new Vector3((w - 1.5f) * 0.0006f, 0.0025f, -0.004f), new Vector3(0.05f, 0f, Mathf.Pi / 2f + (w - 1.5f) * 0.05f));
                Part(root, Ball(14), Brass, new Vector3(0f, 0f, -0.0003f), null, new Vector3(0.0045f, 0.0045f, 0.005f));
                foreach (float x in new[] { -0.0021f, 0.0021f })
                    Part(root, Ball(10), Black, new Vector3(x, 0.0004f, 0.0002f), null, Vector3.One * 0.0013f);
                break;
            default:
                // Dry fly: a stiff hackle collar, upright wings and a tail of fibres.
                for (int i = 0; i < 22; i++)
                {
                    float a = i / 22f * Mathf.Tau + (float)rng.NextDouble() * 0.2f;
                    var d = new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
                    LureMesh.TubeInto(fibres, new[] { new Vector3(0f, 0f, -0.003f) + d * 0.0008f, new Vector3(0f, 0f, -0.0035f) + d * 0.0065f }, f => 0.0001f * (1f - f * 0.5f), 3);
                }
                for (int i = 0; i < 7; i++)
                    LureMesh.TubeInto(fibres, new[] { new Vector3(0f, 0f, -len), new Vector3((i - 3) * 0.0005f, 0.001f, -len - 0.009f) }, f => 0.00009f, 3);
                foreach (float side in new[] { -1f, 1f })
                    Part(root, LureMesh.Plate(u => 0.5f * MathF.Sqrt(MathF.Max(MathF.Sin(MathF.PI * u), 0f)), 0.009f, 0.004f, 0f, 0.0002f, 8, 4),
                        new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.84f, 0.86f, 0.75f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                            Roughness = 0.6f, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
                        new Vector3(side * 0.0006f, 0.001f, -0.0035f), new Vector3(-Mathf.Pi / 2f + 0.25f, side * 0.25f, 0f));
                break;
        }
        Part(root, fibres.Commit(), hackleMat, Vector3.Zero);
    }
}
