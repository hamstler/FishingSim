using System;
using System.Collections.Generic;
using Godot;

namespace FishingSim.World.Props;

/// <summary>
/// Hand tools and camp things built in code with soft edges: a shovel (ash handle with a D grip, a dished iron blade),
/// an insect net (pole, iron ring, a bag of fine net), an iron pot on a bail, a tripod of poles, split logs, worms.
/// The tools are built along +Y from the working end, so they can be held and animated by their grip points.
/// </summary>
public static class CampProps
{
    private static readonly Dictionary<string, Material> Materials = new();

    private static Material Cached(string key, Func<Material> make)
    {
        lock (Materials)
        {
            if (!Materials.TryGetValue(key, out var m))
                Materials[key] = m = make();
            return m;
        }
    }

    public static Material Wood => Cached("wood", () => BuildingFactory.Pixel(new StandardMaterial3D
    {
        AlbedoColor = new Color(0.66f, 0.5f, 0.33f), Roughness = 0.55f, MetallicSpecular = 0.4f,
    }));

    public static Material DarkWood => Cached("darkwood", () => BuildingFactory.Pixel(new StandardMaterial3D
    {
        AlbedoColor = new Color(0.38f, 0.27f, 0.17f), Roughness = 0.75f,
    }));

    /// <summary>Photographed pine bark on logs and stumps (the tree shader needs the trees' own vertex data).</summary>
    public static Material Bark => Cached("bark", () =>
        BuildingFactory.Photo("pine_bark", new Color(0.85f, 0.8f, 0.74f), 0.92f, scale: 1f)
        ?? BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.27f, 0.2f), Roughness = 0.95f }));

    public static Material Iron => Cached("iron", () => BuildingFactory.Pixel(new StandardMaterial3D
    {
        AlbedoColor = new Color(0.36f, 0.36f, 0.38f), Metallic = 0.85f, Roughness = 0.42f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    }, true));

    public static Material BlackIron => Cached("blackiron", () => BuildingFactory.Pixel(new StandardMaterial3D
    {
        AlbedoColor = new Color(0.1f, 0.1f, 0.11f), Metallic = 0.7f, Roughness = 0.55f,
    }, true));

    public static Material Soil => Cached("soil", () =>
        BuildingFactory.Photo("forrest_ground_01", new Color(0.62f, 0.5f, 0.4f), 0.95f, scale: 1.5f)
        ?? BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.18f, 0.12f), Roughness = 1f }));

    public static Material WormSkin => Cached("worm", () => BuildingFactory.Pixel(new StandardMaterial3D
    {
        AlbedoColor = new Color(0.72f, 0.38f, 0.38f), Roughness = 0.3f, MetallicSpecular = 0.7f,
        SubsurfScatterEnabled = true, SubsurfScatterStrength = 0.4f,
    }, true));

    /// <summary>Fine linen net: a grid of threads with holes between them, seen from both sides.</summary>
    public static Material Net => Cached("net", () =>
    {
        const int n = 64;
        var img = Image.CreateEmpty(n, n, true, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool thread = x % 8 < 1 || y % 8 < 1;
                img.SetPixel(x, y, thread ? new Color(0.9f, 0.88f, 0.82f, 1f) : new Color(0.9f, 0.88f, 0.82f, 0f));
            }
        img.GenerateMipmaps();
        return BuildingFactory.Pixel(new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(img), Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = 0.4f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 0.9f,
            Uv1Scale = new Vector3(6f, 3f, 1f),
        });
    });

    private static MeshInstance3D Part(Node3D parent, Mesh mesh, Material mat, Vector3 pos, Vector3? rotDeg = null, Vector3? scale = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos };
        if (rotDeg is Vector3 r)
            mi.RotationDegrees = r;
        if (scale is Vector3 s)
            mi.Scale = s;
        parent.AddChild(mi);
        return mi;
    }

    private static CylinderMesh Rod(float r0, float r1, float h, int seg = 10) =>
        new() { TopRadius = r1, BottomRadius = r0, Height = h, RadialSegments = seg, Rings = 1 };

    // ------------------------------------------------------------------ shovel
    /// <summary>Where the hands hold the shovel (its own space: blade edge at the origin, handle up +Y, blade face -Z).</summary>
    public static readonly Vector3 ShovelTopGrip = new(0f, 1.2f, 0f), ShovelShaftGrip = new(0f, 0.98f, 0f);

    public const float ShovelHandleRadius = 0.017f;

    /// <summary>The soil clump that rides on the blade while digging (shown by the caller).</summary>
    public static Node3D Shovel(out Node3D bladeLoad)
    {
        var root = new Node3D { Name = "Shovel" };
        // Blade: a dished, slightly pointed iron plate from the edge (y = 0) up to the socket.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int nx = 8, ny = 6;
        Vector3 P(int i, int j)
        {
            float u = i / (float)nx * 2f - 1f, v = j / (float)ny;
            float x = u * 0.105f * (0.9f + 0.1f * v);
            float y = v * 0.28f + 0.035f * u * u * (1f - v);       // a rounded point
            float z = -0.014f * (1f - u * u);                      // dished towards the front
            return new Vector3(x, y, z);
        }
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                var a = P(i, j); var b = P(i + 1, j); var c = P(i + 1, j + 1); var d = P(i, j + 1);
                foreach (var p in new[] { a, c, b, a, d, c })
                {
                    st.SetUV(new Vector2(p.X, p.Y) * 4f);
                    st.AddVertex(p);
                }
            }
        st.GenerateNormals();
        var blade = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Iron, Name = "Blade" };
        root.AddChild(blade);
        Part(root, Rod(0.024f, 0.019f, 0.12f), Iron, new Vector3(0f, 0.32f, -0.004f));                 // socket
        Part(root, Rod(ShovelHandleRadius, ShovelHandleRadius * 0.95f, 0.8f), Wood, new Vector3(0f, 0.76f, 0f));
        // The D grip: two cheeks up from the handle and a crossbar.
        Part(root, Rod(0.012f, 0.012f, 0.13f), Wood, new Vector3(-0.035f, 1.17f, 0f), new Vector3(0f, 0f, 25f));
        Part(root, Rod(0.012f, 0.012f, 0.13f), Wood, new Vector3(0.035f, 1.17f, 0f), new Vector3(0f, 0f, -25f));
        Part(root, Rod(0.014f, 0.014f, 0.13f), Wood, ShovelTopGrip, new Vector3(0f, 0f, 90f));
        bladeLoad = new Node3D { Name = "Load", Position = new Vector3(0f, 0.12f, -0.03f), Visible = false };
        Part(bladeLoad, new SphereMesh { Radius = 0.11f, Height = 0.11f, RadialSegments = 12, Rings = 6 }, Soil, Vector3.Zero, null, new Vector3(0.95f, 0.75f, 0.55f));
        Part(bladeLoad, new SphereMesh { Radius = 0.06f, Height = 0.08f, RadialSegments = 8, Rings = 4 }, Soil, new Vector3(0.05f, 0.04f, -0.02f));
        root.AddChild(bladeLoad);
        return root;
    }

    // ------------------------------------------------------------------ insect net
    /// <summary>Where the hands hold the net (its own space: pole end at the origin, pole up +Y, ring at the top).</summary>
    public static readonly Vector3 NetEndGrip = new(0f, 0.09f, 0f), NetPoleGrip = new(0f, 0.48f, 0f);

    public const float NetPoleRadius = 0.013f, NetPoleLength = 1.3f, NetRingRadius = 0.19f;

    /// <summary>The net; <paramref name="bag"/> is the bag of netting (swung by the caller as the net moves).</summary>
    public static Node3D InsectNet(out Node3D bag)
    {
        var root = new Node3D { Name = "InsectNet" };
        Part(root, Rod(NetPoleRadius * 1.1f, NetPoleRadius, NetPoleLength), Wood, new Vector3(0f, NetPoleLength / 2f, 0f));
        var ringCentre = new Vector3(0f, NetPoleLength + NetRingRadius, 0f);
        Part(root, new TorusMesh { InnerRadius = NetRingRadius - 0.006f, OuterRadius = NetRingRadius + 0.006f, Rings = 24, RingSegments = 6 },
            Iron, ringCentre, new Vector3(90f, 0f, 0f));
        // The bag hangs from the ring towards -Z (behind the ring as it sweeps forward).
        bag = new Node3D { Name = "Bag", Position = ringCentre };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int around = 20, along = 8;
        Vector3 B(int i, int j)
        {
            float a = i / (float)around * Mathf.Tau, t = j / (float)along;
            float r = NetRingRadius * (1f - MathF.Pow(t, 1.6f) * 0.92f);
            return new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r - t * 0.06f, -t * 0.42f);
        }
        for (int i = 0; i < around; i++)
            for (int j = 0; j < along; j++)
            {
                var a = B(i, j); var b = B(i + 1, j); var c = B(i + 1, j + 1); var d = B(i, j + 1);
                foreach (var (p, uv) in new[] { (a, (i, j)), (b, (i + 1, j)), (c, (i + 1, j + 1)), (a, (i, j)), (c, (i + 1, j + 1)), (d, (i, j + 1)) })
                {
                    st.SetUV(new Vector2(uv.Item1 / (float)around, uv.Item2 / (float)along));
                    st.AddVertex(p);
                }
            }
        st.GenerateNormals();
        bag.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Net });
        root.AddChild(bag);
        return root;
    }

    // ------------------------------------------------------------------ lift net (podrywka)
    /// <summary>Where the hands hold the lift net's pole (its own space: butt at the origin, pole up +Y).</summary>
    public static readonly Vector3 LiftButtGrip = new(0f, 0.14f, 0f), LiftPoleGrip = new(0f, 0.46f, 0f);

    /// <summary>Pole length, the cord from its tip to the hoops' crossing, half the net's side, how far the hoops
    /// arch above the corners and how deep the netting sags below them (m).</summary>
    public const float LiftPoleLength = 2.6f, LiftPoleRadius = 0.019f, LiftCord = 0.55f, LiftHalfSide = 0.5f, LiftArch = 0.3f, LiftSag = 0.26f;

    /// <summary>From the cord's top (the pole's tip) down to the lowest point of the netting.</summary>
    public static float LiftDrop => LiftCord + LiftArch + LiftSag;

    /// <summary>Square-meshed green netting of the lift net.</summary>
    public static Material LiftNetting => Cached("liftnet", () =>
    {
        var m = (StandardMaterial3D)((StandardMaterial3D)Net).Duplicate();
        m.Uv1Scale = Vector3.One;
        m.AlbedoColor = new Color(0.55f, 0.62f, 0.5f);
        return m;
    });

    /// <summary>The lift net's pole: a long hazel pole, thinner to the tip, with a cord ring there.</summary>
    public static Node3D LiftNetPole()
    {
        var root = new Node3D { Name = "LiftNetPole" };
        Part(root, Rod(LiftPoleRadius * 1.15f, LiftPoleRadius * 0.6f, LiftPoleLength, 8), Wood, new Vector3(0f, LiftPoleLength / 2f, 0f));
        Part(root, new TorusMesh { InnerRadius = 0.011f, OuterRadius = 0.018f, Rings = 10, RingSegments = 5 }, BlackIron,
            new Vector3(0f, LiftPoleLength - 0.03f, 0f));
        return root;
    }

    /// <summary>
    /// The net hanging from the pole (origin at the cord's top, everything below -Y): the cord, two crossed hoops arching
    /// over a square of netting tied at their ends, the netting sagging into a bowl. <paramref name="catchHolder"/> sits
    /// in the bowl's bottom for the caught fish.
    /// </summary>
    public static Node3D LiftNet(out Node3D catchHolder)
    {
        var root = new Node3D { Name = "LiftNet" };
        var cord = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.3f, 0.24f), Roughness = 0.9f };
        Part(root, Rod(0.004f, 0.004f, LiftCord, 5), cord, new Vector3(0f, -LiftCord / 2f, 0f));
        float h = LiftHalfSide, top = -LiftCord;
        foreach (float d in new[] { 1f, -1f })
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= 16; i++)
            {
                float t = i / 8f - 1f;
                pts.Add(new Vector3(t * h, top - LiftArch * t * t, d * t * h));
            }
            root.AddChild(new MeshInstance3D { Mesh = Shapes.Tube(pts, 0.008f), MaterialOverride = Wood });
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int n = 14;
        Vector3 P(int i, int j)
        {
            float u = i / (float)n * 2f - 1f, v = j / (float)n * 2f - 1f;
            return new Vector3(u * h, top - LiftArch - LiftSag * (1f - u * u) * (1f - v * v), v * h);
        }
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                foreach (var (a, b) in new[] { (i, j), (i + 1, j), (i + 1, j + 1), (i, j), (i + 1, j + 1), (i, j + 1) })
                {
                    st.SetUV(new Vector2(a, b) / n * 3f);
                    st.AddVertex(P(a, b));
                }
        st.GenerateNormals();
        root.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = LiftNetting, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        catchHolder = new Node3D { Name = "Catch", Position = new Vector3(0f, top - LiftArch - LiftSag + 0.035f, 0f) };
        root.AddChild(catchHolder);
        return root;
    }

    // ------------------------------------------------------------------ cooking
    /// <summary>An iron pot on a bail (the bail's hook at the origin, the pot hanging below), with a lid of soup.</summary>
    public static Node3D Pot(out MeshInstance3D soup)
    {
        var root = new Node3D { Name = "Pot" };
        Part(root, new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.13f, Height = 0.22f, RadialSegments = 18, Rings = 2 },
            BlackIron, new Vector3(0f, -0.38f, 0f));
        Part(root, new SphereMesh { Radius = 0.13f, Height = 0.1f, RadialSegments = 18, Rings = 4 }, BlackIron, new Vector3(0f, -0.49f, 0f));
        Part(root, new TorusMesh { InnerRadius = 0.165f, OuterRadius = 0.182f, Rings = 24, RingSegments = 6 }, BlackIron, new Vector3(0f, -0.27f, 0f));
        // The bail: an arc of wire from rim to rim up to the hook.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.LineStrip);
        for (int i = 0; i <= 16; i++)
        {
            float a = Mathf.Pi * i / 16f;
            st.AddVertex(new Vector3(MathF.Cos(a) * 0.175f, -0.27f + MathF.Sin(a) * 0.27f, 0f));
        }
        root.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = BlackIron });
        soup = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.155f, BottomRadius = 0.155f, Height = 0.01f, RadialSegments = 18 },
            Position = new Vector3(0f, -0.3f, 0f), Visible = false,
            MaterialOverride = BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.5f, 0.3f), Roughness = 0.15f, MetallicSpecular = 0.8f }, true),
        };
        root.AddChild(soup);
        return root;
    }

    /// <summary>Three poles lashed at the top (the hook at the origin, feet on y = -height).</summary>
    public static Node3D Tripod(float height = 1.35f)
    {
        var root = new Node3D { Name = "Tripod" };
        for (int k = 0; k < 3; k++)
        {
            float a = k * Mathf.Tau / 3f + 0.3f;
            var foot = new Vector3(MathF.Cos(a) * 0.62f, -height, MathF.Sin(a) * 0.62f);
            var top = new Vector3(0f, 0.12f, 0f);
            var mid = (foot + top) / 2f;
            var pole = Part(root, Rod(0.022f, 0.018f, foot.DistanceTo(top), 8), DarkWood, mid);
            pole.LookAtFromPosition(mid, top, Vector3.Forward);
            pole.RotateObjectLocal(Vector3.Right, Mathf.Pi / 2f);
        }
        Part(root, new TorusMesh { InnerRadius = 0.03f, OuterRadius = 0.045f }, new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.45f, 0.3f) },
            new Vector3(0f, 0.08f, 0f), new Vector3(90f, 0f, 0f));
        return root;
    }

    /// <summary>A short split log to sit on (lying along X, resting on y = 0).</summary>
    public static Node3D SeatLog(float length = 1.4f, int seed = 0, float radius = 0.17f)
    {
        var root = new Node3D { Name = "SeatLog" };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Shapes.Log(st, new Vector3(0f, radius, 0f), Vector3.Right, length, radius, seed);
        st.GenerateNormals();
        root.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Bark });
        return root;
    }

    /// <summary>A worm: a thin tube winding along a sine (about 7 cm), centred on the origin.</summary>
    public static MeshInstance3D Worm(int seed)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 31 + 7) };
        float phase = rng.Randf() * 6f, len = rng.RandfRange(0.06f, 0.09f);
        const int n = 16, sides = 6;
        Vector3 C(int i) => new(i / (float)n * len - len / 2f, 0.004f, MathF.Sin(i * 0.6f + phase) * 0.012f);
        for (int i = 0; i < n; i++)
        {
            var a = C(i);
            var b = C(i + 1);
            var dir = (b - a).Normalized();
            var side = dir.Cross(Vector3.Up).Normalized();
            float r0 = 0.0035f * MathF.Sin(MathF.PI * (i + 0.5f) / n) + 0.001f, r1 = 0.0035f * MathF.Sin(MathF.PI * (i + 1.5f) / n) + 0.001f;
            for (int k = 0; k < sides; k++)
            {
                float t0 = k * Mathf.Tau / sides, t1 = (k + 1) * Mathf.Tau / sides;
                Vector3 R(float t) => side * MathF.Cos(t) + Vector3.Up * MathF.Sin(t);
                var p00 = a + R(t0) * r0; var p01 = a + R(t1) * r0; var p10 = b + R(t0) * r1; var p11 = b + R(t1) * r1;
                foreach (var (p, nrm) in new[] { (p00, R(t0)), (p10, R(t0)), (p11, R(t1)), (p00, R(t0)), (p11, R(t1)), (p01, R(t1)) })
                {
                    st.SetNormal(nrm);
                    st.AddVertex(p);
                }
            }
        }
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = WormSkin };
    }
}
