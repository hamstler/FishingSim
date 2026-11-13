using System;
using Godot;
using FishingSim.Core.Methods;
using FishingSim.Data;

namespace FishingSim.Fishing;

/// <summary>
/// The lure models, built from a lure's data: its family and model (<see cref="BaitData.Shape"/>), main and accent
/// colours, pattern (dots, stripes, bars, halves, glitter, red head), metal or paint, and size. Nose towards +Z, the
/// line's eye at the origin. Spinners: round, long, with a hair tail, with a lead head (the blade turns on a node
/// returned as <c>spin</c>); spoons: classic, narrow, wide; wobblers: minnow, crank (big lip), jerk (no lip), popper
/// (cupped face); soft baits on a jig head: ripper, twister, worm, craw (the tail wags on <c>tail</c>); flies: dry,
/// nymph, wet, streamer.
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
        var look = new Look(bait.LureColor, bait.AccentColor, bait.Pattern, bait.Metallic);
        var root = new Node3D { Name = "Lure" };
        float nominal;
        switch (family)
        {
            case LureFamily.Spoon:
                nominal = 6f;
                Spoon(root, shape, look);
                break;
            case LureFamily.Wobbler:
                nominal = shape == "jerk" ? 7f : 5.6f;
                Wobbler(root, shape, look);
                break;
            case LureFamily.Softbait:
                nominal = shape == "worm" ? 8f : 6f;
                tail = Softbait(root, shape, look);
                break;
            case LureFamily.Fly:
                nominal = shape == "streamer" ? 6f : 1.8f;
                Fly(root, shape, look);
                break;
            default:
                nominal = 5f;
                spin = Spinner(root, shape, look);
                break;
        }
        // The model is built at its family's usual length; the lure's own size scales it.
        float k = bait.SizeCm > 0f ? Math.Clamp(bait.SizeCm / nominal, 0.4f, 3f) : 1f;
        root.Scale = new Vector3(k, k, k);
        return root;
    }

    private readonly record struct Look(Color Main, Color Accent, string Pattern, bool Metal)
    {
        public Material Body => Metal ? Metallic(Main) : Paint(Main);
        public Material AccentPaint => Paint(Accent);
        public Color Dark => Main.Darkened(0.55f);
    }

    private static StandardMaterial3D Metallic(Color c, float rough = 0.2f) => new() { AlbedoColor = c, Metallic = 0.95f, Roughness = rough };

    private static StandardMaterial3D Paint(Color c, float rough = 0.4f) => new() { AlbedoColor = c, Roughness = rough };

    private static readonly Material Wire = Metallic(new Color(0.78f, 0.78f, 0.8f), 0.2f);
    private static readonly Material Steel = Metallic(new Color(0.3f, 0.3f, 0.32f), 0.35f);
    private static readonly Material Lead = Metallic(new Color(0.42f, 0.42f, 0.44f), 0.6f);
    private static readonly Material EyeBlack = Paint(new Color(0.05f, 0.04f, 0.03f), 0.1f);
    private static readonly Material EyeYellow = Paint(new Color(0.95f, 0.8f, 0.15f), 0.2f);

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

    private static Mesh Ball => new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 8 };

    private static Mesh Rod(float r, float length, int sides = 6) =>
        new CylinderMesh { TopRadius = r, BottomRadius = r, Height = length, RadialSegments = sides, Rings = 1 };

    private static readonly Vector3 AlongZ = new(Mathf.Pi / 2f, 0f, 0f);

    /// <summary>A small treble hook hanging from a point (three bent points round a shank).</summary>
    private static void Treble(Node3D parent, Vector3 at, float size)
    {
        var hook = new Node3D { Position = at };
        parent.AddChild(hook);
        Part(hook, new TorusMesh { InnerRadius = size * 0.12f, OuterRadius = size * 0.2f, Rings = 8, RingSegments = 4 }, Steel,
            Vector3.Zero, new Vector3(0f, 0f, Mathf.Pi / 2f));
        Part(hook, Rod(size * 0.04f, size * 0.8f, 4), Steel, new Vector3(0f, 0f, -size * 0.45f), AlongZ);
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * Mathf.Tau;
            var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
            Part(hook, new TorusMesh { InnerRadius = size * 0.26f, OuterRadius = size * 0.32f, Rings = 10, RingSegments = 4 }, Steel,
                new Vector3(0f, 0f, -size * 0.85f) + dir * size * 0.28f, new Vector3(0f, Mathf.Pi / 2f, a), new Vector3(1f, 1f, 0.6f));
        }
    }

    /// <summary>Spots of the accent colour over a body (on a flat face: <paramref name="flat"/> squashes them).</summary>
    private static void Dots(Node3D parent, Look look, Vector3 centre, Vector3 half, int count, int seed, float size)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919 + 3) };
        var mat = look.AccentPaint;
        for (int i = 0; i < count; i++)
        {
            var p = centre + new Vector3(rng.RandfRange(-half.X, half.X), rng.RandfRange(-half.Y, half.Y), rng.RandfRange(-half.Z, half.Z));
            Part(parent, Ball, mat, p, null, new Vector3(size, size, size));
        }
    }

    // ------------------------------------------------------------------ spinners
    private static Node3D Spinner(Node3D root, string shape, Look look)
    {
        // The wire shaft, beads and the body, a treble at the back.
        Part(root, Rod(0.0007f, 0.06f, 4), Wire, new Vector3(0f, 0f, -0.03f), AlongZ);
        var bodyMat = look.Metal ? look.Body : Metallic(new Color(0.85f, 0.66f, 0.3f));
        if (shape == "headed")
        {
            // A lead head in front of the blade: it casts far and sinks.
            Part(root, Ball, Lead, new Vector3(0f, 0f, -0.006f), null, new Vector3(0.011f, 0.011f, 0.014f));
            Part(root, Ball, EyeYellow, new Vector3(0.0045f, 0.0015f, -0.004f), null, new Vector3(0.003f, 0.003f, 0.003f));
        }
        Part(root, Ball, bodyMat, new Vector3(0f, 0f, -0.024f), null, new Vector3(0.007f, 0.007f, 0.007f));
        Part(root, Ball, bodyMat, new Vector3(0f, 0f, -0.035f), null, new Vector3(0.009f, 0.009f, 0.018f));
        Part(root, Ball, Paint(look.Pattern == "dots" ? look.Accent : new Color(0.85f, 0.1f, 0.08f)), new Vector3(0f, 0f, -0.046f), null,
            new Vector3(0.005f, 0.005f, 0.005f));
        var spin = new Node3D { Position = new Vector3(0f, 0f, shape == "headed" ? -0.016f : -0.008f) };
        root.AddChild(spin);
        var bladeSize = shape == "long" ? new Vector3(0.0085f, 0.0018f, 0.026f) : new Vector3(0.012f, 0.0018f, 0.017f);
        var blade = new Node3D { Position = new Vector3(bladeSize.X * 0.6f, 0f, -bladeSize.Z * 0.6f), Rotation = new Vector3(0f, 0.3f, 0f) };
        spin.AddChild(blade);
        Part(blade, Ball, look.Body, Vector3.Zero, null, bladeSize);
        if (look.Pattern == "dots")
            for (int i = 0; i < 3; i++)
                Part(blade, Ball, look.AccentPaint, new Vector3((i - 1) * bladeSize.X * 0.25f, bladeSize.Y * 0.45f, (i % 2 - 0.5f) * bladeSize.Z * 0.3f), null,
                    new Vector3(0.003f, 0.0008f, 0.003f));
        else if (look.Pattern == "bars")
            for (int i = 0; i < 2; i++)
                Part(blade, new BoxMesh { Size = new Vector3(bladeSize.X * 0.8f, 0.0006f, 0.0018f) }, Paint(new Color(0.06f, 0.06f, 0.06f)),
                    new Vector3(0f, bladeSize.Y * 0.45f, (i - 0.5f) * bladeSize.Z * 0.35f));
        Treble(root, new Vector3(0f, 0f, -0.062f), 0.012f);
        if (shape == "tail")
        {
            // A tuft of hair over the treble, in the accent colour.
            var hair = Paint(look.Accent, 0.9f);
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.Tau;
                Part(root, new CylinderMesh { TopRadius = 0.0003f, BottomRadius = 0.0012f, Height = 0.024f, RadialSegments = 3 }, hair,
                    new Vector3(MathF.Cos(a) * 0.002f, MathF.Sin(a) * 0.002f, -0.066f), new Vector3(Mathf.Pi / 2f + MathF.Sin(a) * 0.15f, 0f, MathF.Cos(a) * 0.15f));
            }
        }
        return spin;
    }

    // ------------------------------------------------------------------ spoons
    private static void Spoon(Node3D root, string shape, Look look)
    {
        var size = shape switch
        {
            "narrow" => new Vector3(0.012f, 0.005f, 0.06f),
            "wide" => new Vector3(0.026f, 0.006f, 0.048f),
            _ => new Vector3(0.019f, 0.006f, 0.052f),
        };
        var plate = new Node3D { Position = new Vector3(0f, 0f, -0.004f - size.Z / 2f) };
        root.AddChild(plate);
        Part(plate, new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 18, Rings = 8, IsHemisphere = true }, look.Body,
            Vector3.Zero, new Vector3(Mathf.Pi, 0f, 0f), size);
        // The painted side: half of it, stripes, bars or a red dot.
        var accent = look.AccentPaint;
        switch (look.Pattern)
        {
            case "halves":
                Part(plate, new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 18, Rings = 8, IsHemisphere = true }, accent,
                    new Vector3(0f, 0.0004f, -size.Z * 0.22f), new Vector3(Mathf.Pi, 0f, 0f), new Vector3(size.X * 0.98f, size.Y * 0.9f, size.Z * 0.5f));
                break;
            case "stripes":
                for (int i = 0; i < 2; i++)
                    Part(plate, new BoxMesh { Size = new Vector3(size.X * 0.85f, 0.0008f, size.Z * 0.08f) }, accent, new Vector3(0f, 0.0008f, (i - 0.5f) * size.Z * 0.35f));
                break;
            case "bars":
                for (int i = 0; i < 3; i++)
                    Part(plate, new BoxMesh { Size = new Vector3(size.X * 0.8f, 0.0008f, size.Z * 0.07f) }, Paint(look.Dark), new Vector3(0f, 0.0008f, (i - 1f) * size.Z * 0.28f));
                Part(plate, Ball, accent, new Vector3(0f, 0.0006f, size.Z * 0.3f), null, new Vector3(size.X * 0.5f, 0.0008f, size.Z * 0.18f));
                break;
            default:
                Part(plate, Ball, Paint(new Color(0.85f, 0.1f, 0.08f)), new Vector3(0f, 0.0006f, -size.Z * 0.15f), null, new Vector3(0.006f, 0.0008f, 0.006f));
                break;
        }
        Part(root, new TorusMesh { InnerRadius = 0.002f, OuterRadius = 0.003f, Rings = 8, RingSegments = 4 }, Wire, new Vector3(0f, 0f, -0.002f), new Vector3(0f, 0f, Mathf.Pi / 2f));
        Treble(root, new Vector3(0f, 0f, -0.006f - size.Z), 0.012f);
    }

    // ------------------------------------------------------------------ wobblers
    private static void Wobbler(Node3D root, string shape, Look look)
    {
        var body = shape switch
        {
            "crank" => new Vector3(0.017f, 0.022f, 0.042f),
            "jerk" => new Vector3(0.014f, 0.02f, 0.07f),
            "popper" => new Vector3(0.016f, 0.017f, 0.045f),
            _ => new Vector3(0.013f, 0.017f, 0.056f),
        };
        float mid = -body.Z / 2f - 0.002f;
        // The back in the main colour, the flanks and belly in the accent, a darker line along the top.
        Part(root, Ball, Paint(look.Main, 0.3f), new Vector3(0f, 0f, mid), null, body);
        Part(root, Ball, Paint(look.Accent, 0.3f), new Vector3(0f, -body.Y * 0.18f, mid), null, new Vector3(body.X * 1.04f, body.Y * 0.7f, body.Z * 0.9f));
        Part(root, Ball, Paint(look.Dark, 0.3f), new Vector3(0f, body.Y * 0.36f, mid), null, new Vector3(body.X * 0.5f, body.Y * 0.32f, body.Z * 0.8f));
        switch (look.Pattern)
        {
            case "bars":
                for (int i = 0; i < 4; i++)
                    Part(root, new BoxMesh { Size = new Vector3(body.X * 1.06f, body.Y * 0.5f, body.Z * 0.045f) }, Paint(look.Dark, 0.3f),
                        new Vector3(0f, body.Y * 0.12f, mid + (i - 1.5f) * body.Z * 0.18f));
                break;
            case "dots":
                Dots(root, look with { Accent = look.Main.Lightened(0.45f) }, new Vector3(0f, body.Y * 0.1f, mid), new Vector3(body.X * 0.5f, body.Y * 0.25f, body.Z * 0.35f), 10, 5, 0.0028f);
                break;
            case "redhead":
                Part(root, Ball, Paint(look.Accent, 0.3f), new Vector3(0f, 0f, -0.006f), null, new Vector3(body.X * 1.05f, body.Y * 1.02f, body.Z * 0.28f));
                break;
        }
        // Eyes.
        foreach (float x in new[] { -body.X * 0.47f, body.X * 0.47f })
        {
            Part(root, Ball, EyeYellow, new Vector3(x, body.Y * 0.15f, -0.002f - body.Z * 0.16f), null, new Vector3(0.0042f, 0.0042f, 0.0042f));
            Part(root, Ball, EyeBlack, new Vector3(x * 1.06f, body.Y * 0.15f, -0.002f - body.Z * 0.16f), null, new Vector3(0.0024f, 0.0024f, 0.0024f));
        }
        var clear = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.9f, 0.95f, 1f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f,
        };
        switch (shape)
        {
            case "crank":
                Part(root, new BoxMesh { Size = new Vector3(0.016f, 0.0015f, 0.024f) }, clear, new Vector3(0f, -0.008f, 0.008f), new Vector3(0.7f, 0f, 0f));
                break;
            case "popper":
                // The cupped face that splashes.
                Part(root, new CylinderMesh { TopRadius = body.X * 0.48f, BottomRadius = body.X * 0.48f, Height = 0.002f, RadialSegments = 14 }, EyeBlack,
                    new Vector3(0f, 0f, -0.0025f), AlongZ);
                break;
            case "jerk":
                break;
            default:
                Part(root, new BoxMesh { Size = new Vector3(0.012f, 0.0012f, 0.016f) }, clear, new Vector3(0f, -0.006f, 0.004f), new Vector3(0.6f, 0f, 0f));
                break;
        }
        Treble(root, new Vector3(0f, -body.Y * 0.55f, mid + body.Z * 0.15f), 0.009f);
        Treble(root, new Vector3(0f, -body.Y * 0.1f, -0.004f - body.Z), 0.009f);
    }

    // ------------------------------------------------------------------ soft baits on a jig head
    private static Node3D Softbait(Node3D root, string shape, Look look)
    {
        // The jig head: lead, or red with a red-head pattern; an eye; the hook's point standing up out of the back.
        Part(root, Ball, look.Pattern == "redhead" ? Paint(new Color(0.8f, 0.1f, 0.08f), 0.4f) : Lead, new Vector3(0f, 0f, -0.005f), null,
            new Vector3(0.0136f, 0.0136f, 0.0136f));
        Part(root, Ball, EyeYellow, new Vector3(0.0055f, 0.002f, -0.003f), null, new Vector3(0.0036f, 0.0036f, 0.0036f));
        var soft = Paint(look.Main, 0.6f);
        Part(root, Rod(0.0006f, 0.012f, 4), Steel, new Vector3(0f, 0.006f, -0.03f), new Vector3(0.25f, 0f, 0f));
        Node3D tail;
        switch (shape)
        {
            case "twister":
            {
                Part(root, new CylinderMesh { TopRadius = 0.0045f, BottomRadius = 0.0035f, Height = 0.03f, RadialSegments = 10, Rings = 3 }, soft,
                    new Vector3(0f, 0f, -0.025f), new Vector3(-Mathf.Pi / 2f, 0f, 0f));
                tail = new Node3D { Position = new Vector3(0f, 0f, -0.04f) };
                root.AddChild(tail);
                // The curly tail: a flat ribbon bent round in a sickle.
                for (int i = 0; i < 7; i++)
                {
                    float a = i / 6f * 2.6f;
                    Part(tail, new BoxMesh { Size = new Vector3(0.0012f, 0.006f - i * 0.0004f, 0.007f) }, soft,
                        new Vector3(0f, (1f - MathF.Cos(a)) * 0.008f, -MathF.Sin(a) * 0.012f), new Vector3(a, 0f, 0f));
                }
                break;
            }
            case "worm":
            {
                for (int i = 0; i < 6; i++)
                    Part(root, new CylinderMesh { TopRadius = 0.0034f - i * 0.0002f, BottomRadius = 0.0032f - i * 0.0002f, Height = 0.011f, RadialSegments = 8 }, soft,
                        new Vector3(0f, 0f, -0.016f - i * 0.0105f), new Vector3(-Mathf.Pi / 2f, 0f, 0f));
                tail = new Node3D { Position = new Vector3(0f, 0f, -0.078f) };
                root.AddChild(tail);
                Part(tail, new CylinderMesh { TopRadius = 0.0018f, BottomRadius = 0.0004f, Height = 0.016f, RadialSegments = 6 }, soft,
                    new Vector3(0f, 0f, -0.008f), new Vector3(-Mathf.Pi / 2f, 0f, 0f));
                break;
            }
            case "craw":
            {
                Part(root, Ball, soft, new Vector3(0f, 0f, -0.024f), null, new Vector3(0.011f, 0.008f, 0.03f));
                foreach (float side in new[] { -1f, 1f })
                {
                    Part(root, Ball, soft, new Vector3(side * 0.008f, 0f, -0.005f), new Vector3(0f, side * 0.5f, 0f), new Vector3(0.006f, 0.003f, 0.016f));
                    for (int l = 0; l < 3; l++)
                        Part(root, Rod(0.0005f, 0.008f, 3), soft, new Vector3(side * 0.007f, -0.003f, -0.018f - l * 0.006f), new Vector3(0f, 0f, side * 1.2f));
                }
                tail = new Node3D { Position = new Vector3(0f, 0f, -0.04f) };
                root.AddChild(tail);
                Part(tail, Ball, soft, new Vector3(0f, 0f, -0.006f), null, new Vector3(0.012f, 0.003f, 0.012f));
                break;
            }
            default:
            {
                // Ripper: a ribbed body and a paddle tail.
                Part(root, new CylinderMesh { TopRadius = 0.005f, BottomRadius = 0.0035f, Height = 0.045f, RadialSegments = 10, Rings = 4 }, soft,
                    new Vector3(0f, 0f, -0.033f), new Vector3(-Mathf.Pi / 2f, 0f, 0f));
                tail = new Node3D { Position = new Vector3(0f, 0f, -0.055f) };
                root.AddChild(tail);
                Part(tail, new CylinderMesh { TopRadius = 0.0035f, BottomRadius = 0.0022f, Height = 0.008f, RadialSegments = 8 }, soft,
                    new Vector3(0f, 0f, -0.004f), new Vector3(-Mathf.Pi / 2f, 0f, 0f));
                Part(tail, new BoxMesh { Size = new Vector3(0.0025f, 0.012f, 0.009f) }, soft, new Vector3(0f, 0f, -0.011f));
                break;
            }
        }
        if (look.Pattern == "glitter")
            Dots(root, look, new Vector3(0f, 0f, -0.032f), new Vector3(0.004f, 0.004f, 0.018f), 14, 9, 0.0012f);
        return tail;
    }

    // ------------------------------------------------------------------ flies
    private static void Fly(Node3D root, string shape, Look look)
    {
        var body = Paint(look.Main, 0.8f);
        var hackle = Paint(look.Accent, 0.9f);
        // The hook under the body.
        Part(root, new TorusMesh { InnerRadius = 0.003f, OuterRadius = 0.0036f, Rings = 10, RingSegments = 4 }, Steel,
            new Vector3(0f, -0.004f, shape == "streamer" ? -0.03f : -0.014f), new Vector3(0f, Mathf.Pi / 2f, 0f), new Vector3(1f, 1f, 0.5f));
        switch (shape)
        {
            case "nymph":
                Part(root, Ball, body, new Vector3(0f, 0f, -0.009f), null, new Vector3(0.0055f, 0.0055f, 0.016f));
                Part(root, Ball, look.Pattern == "redhead" ? Metallic(new Color(0.95f, 0.75f, 0.25f)) : Paint(look.Accent, 0.5f),
                    new Vector3(0f, 0f, -0.001f), null, new Vector3(0.0045f, 0.0045f, 0.0045f));
                for (int i = 0; i < 4; i++)
                    Part(root, Rod(0.0003f, 0.006f, 3), hackle, new Vector3(0f, -0.003f, -0.006f - i * 0.002f), new Vector3(0.4f, 0f, 0f));
                break;
            case "wet":
                Part(root, Rod(0.002f, 0.014f, 8), body, new Vector3(0f, 0f, -0.008f), AlongZ);
                Part(root, new BoxMesh { Size = new Vector3(0.002f, 0.006f, 0.014f) }, hackle, new Vector3(0f, 0.004f, -0.014f), new Vector3(0.35f, 0f, 0f));
                break;
            case "streamer":
                Part(root, Rod(0.0022f, 0.022f, 8), Paint(look.Accent, 0.7f), new Vector3(0f, 0f, -0.012f), AlongZ);
                for (int i = 0; i < 4; i++)
                    Part(root, new BoxMesh { Size = new Vector3(0.0012f, 0.006f, 0.05f) }, body,
                        new Vector3((i - 1.5f) * 0.0012f, 0.003f, -0.035f), new Vector3(0.06f * (i - 1.5f), 0f, 0f));
                Part(root, Ball, EyeYellow, new Vector3(0.0025f, 0.001f, -0.002f), null, new Vector3(0.003f, 0.003f, 0.003f));
                break;
            default:
                // Dry fly: body, a ruff of hackle, upright wings, a tail.
                Part(root, Rod(0.002f, 0.014f, 8), body, new Vector3(0f, 0f, -0.008f), AlongZ);
                for (int i = 0; i < 10; i++)
                {
                    float a = i / 10f * Mathf.Tau;
                    Part(root, new CylinderMesh { TopRadius = 0.0002f, BottomRadius = 0.0006f, Height = 0.012f, RadialSegments = 3 }, hackle,
                        new Vector3(MathF.Cos(a) * 0.005f, MathF.Sin(a) * 0.005f, -0.001f), new Vector3(0f, 0f, a - Mathf.Pi / 2f));
                }
                Part(root, new BoxMesh { Size = new Vector3(0.0008f, 0.01f, 0.006f) }, Paint(look.Main.Lightened(0.4f), 0.8f), new Vector3(0f, 0.007f, -0.004f));
                Part(root, new CylinderMesh { TopRadius = 0.0003f, BottomRadius = 0.0012f, Height = 0.012f, RadialSegments = 4 }, hackle,
                    new Vector3(0f, 0.001f, -0.02f), new Vector3(Mathf.Pi / 2f + 0.2f, 0f, 0f));
                break;
        }
    }
}
