using System;
using System.Collections.Generic;
using Godot;

namespace FishingSim.World.Props;

/// <summary>
/// The everyday things of a village, built in code with soft edges: a washing line with linen drying (swaying in the
/// wind), a chopping block with an axe stuck in it and split wood about, a pitchfork and a rake leaning on a wall, a
/// fishing net drying on poles with its floats, a wooden rowboat pulled up on the shore with its oars, an axe to put
/// in a woodcutter's hand.
/// </summary>
public static partial class Everyday
{
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

    private static CylinderMesh Rod(float r, float h, int seg = 8) => new() { TopRadius = r, BottomRadius = r * 1.1f, Height = h, RadialSegments = seg, Rings = 1 };

    /// <summary>A pole from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static MeshInstance3D Pole(Node3D parent, Vector3 a, Vector3 b, float r, Material mat)
    {
        var mid = (a + b) / 2f;
        var mi = Part(parent, Rod(r, a.DistanceTo(b)), mat, mid);
        var dir = (b - a).Normalized();
        var axis = Vector3.Up.Cross(dir);
        if (axis.LengthSquared() > 1e-6f)
            mi.Basis = new Basis(axis.Normalized(), MathF.Acos(Math.Clamp(Vector3.Up.Dot(dir), -1f, 1f)));
        return mi;
    }

    private static readonly Color[] Linen =
    {
        new(0.86f, 0.84f, 0.78f), new(0.62f, 0.66f, 0.72f), new(0.72f, 0.5f, 0.38f), new(0.8f, 0.78f, 0.62f), new(0.52f, 0.58f, 0.44f),
    };

    /// <summary>Two posts with a sagging line and linen pegged on it (swaying), along X, posts at ±1.4 m.</summary>
    public static Node3D LaundryLine(int seed)
    {
        var root = new Node3D { Name = "Laundry" };
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 977 + 5) };
        foreach (float x in new[] { -1.4f, 1.4f })
        {
            Pole(root, new Vector3(x, 0f, 0f), new Vector3(x, 1.85f, 0f), 0.045f, CampProps.DarkWood);
            Pole(root, new Vector3(x, 1.8f, -0.25f), new Vector3(x, 1.8f, 0.25f), 0.03f, CampProps.DarkWood);
        }
        // The line sags between the posts.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.LineStrip);
        float Sag(float x) => 1.78f - 0.12f * (1f - x * x / (1.4f * 1.4f));
        for (int i = 0; i <= 16; i++)
        {
            float x = -1.4f + 2.8f * i / 16f;
            st.AddVertex(new Vector3(x, Sag(x), 0f));
        }
        root.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = CampProps.DarkWood });
        var sway = new Sway();
        root.AddChild(sway);
        float at = -1.15f;
        while (at < 1.1f)
        {
            float w = rng.RandfRange(0.35f, 0.7f), h = rng.RandfRange(0.45f, 0.95f);
            var piece = new Node3D { Position = new Vector3(at + w / 2f, Sag(at + w / 2f), 0f) };
            piece.AddChild(new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(w, h), CenterOffset = new Vector3(0f, -h / 2f, 0f) },
                MaterialOverride = BuildingFactory.Cloth(Linen[rng.RandiRange(0, Linen.Length - 1)]),
            });
            sway.AddChild(piece);
            sway.Pieces.Add(piece);
            at += w + rng.RandfRange(0.08f, 0.2f);
        }
        return root;
    }

    /// <summary>Hung things swinging gently in the wind (one node drives them all).</summary>
    public partial class Sway : Node3D
    {
        public readonly List<Node3D> Pieces = new();
        private readonly float _phase = GD.Randf() * 10f;

        public override void _Process(double delta)
        {
            float t = (float)Time.GetTicksMsec() / 1000f + _phase;
            for (int i = 0; i < Pieces.Count; i++)
                if (IsInstanceValid(Pieces[i]))
                    Pieces[i].Rotation = new Vector3(0.12f * MathF.Sin(t * 1.3f + i * 0.9f) + 0.05f * MathF.Sin(t * 3.1f + i), 0f, 0.03f * MathF.Sin(t * 0.8f + i));
        }
    }

    /// <summary>A woodcutter's axe: ash handle along +Y (grip at the bottom), iron head at the top (blade towards +X).</summary>
    public static Node3D Axe()
    {
        var root = new Node3D { Name = "Axe" };
        Part(root, Rod(0.016f, 0.72f), CampProps.Wood, new Vector3(0f, 0.36f, 0f));
        var head = new Node3D { Position = new Vector3(0.03f, 0.68f, 0f) };
        Part(head, Shapes.RoundedBox(new Vector3(0.1f, 0.06f, 0.03f), 0.008f), CampProps.Iron, new Vector3(0.02f, 0f, 0f));
        Part(head, Shapes.RoundedBox(new Vector3(0.06f, 0.13f, 0.012f), 0.004f), CampProps.Iron, new Vector3(0.085f, -0.01f, 0f));
        root.AddChild(head);
        return root;
    }

    /// <summary>A chopping block (a stump) with the axe stuck in it and split wood lying about.</summary>
    public static Node3D ChoppingBlock(int seed)
    {
        var root = new Node3D { Name = "ChoppingBlock" };
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 431 + 9) };
        Node3D? stump = PropAsset.Create("tree_stump_01", height: 0.5f);
        if (stump != null)
            root.AddChild(stump);
        else
            Part(root, new CylinderMesh { TopRadius = 0.28f, BottomRadius = 0.33f, Height = 0.5f, RadialSegments = 14 },
                CampProps.Bark, new Vector3(0f, 0.25f, 0f));
        var axe = Axe();
        axe.Position = new Vector3(0.05f, 0.48f, 0f);
        axe.RotationDegrees = new Vector3(0f, rng.RandfRange(0f, 360f), -62f);
        root.AddChild(axe);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < 7; i++)
        {
            float a = rng.RandfRange(0f, Mathf.Tau), r = rng.RandfRange(0.55f, 1.2f);
            var at = new Vector3(MathF.Cos(a) * r, 0.05f, MathF.Sin(a) * r);
            var dir = new Vector3(MathF.Cos(a + 1.3f), 0f, MathF.Sin(a + 1.3f));
            Shapes.Log(st, at, dir, rng.RandfRange(0.32f, 0.45f), rng.RandfRange(0.04f, 0.06f), seed * 10 + i);
        }
        st.GenerateNormals();
        root.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = CampProps.Bark });
        return root;
    }

    /// <summary>A pitchfork leaning back (handle along +Y from the tines at the origin).</summary>
    public static Node3D Pitchfork()
    {
        var root = new Node3D { Name = "Pitchfork" };
        Part(root, Rod(0.015f, 1.45f), CampProps.Wood, new Vector3(0f, 0.95f, 0f));
        Part(root, Rod(0.012f, 0.22f), CampProps.Iron, new Vector3(0f, 0.22f, 0f), new Vector3(0f, 0f, 90f));
        foreach (float x in new[] { -0.09f, 0f, 0.09f })
            Part(root, new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.002f, Height = 0.3f, RadialSegments = 5 }, CampProps.Iron, new Vector3(x, 0.08f, 0f));
        return root;
    }

    /// <summary>A wooden rake (handle along +Y from the head at the origin).</summary>
    public static Node3D Rake()
    {
        var root = new Node3D { Name = "Rake" };
        Part(root, Rod(0.014f, 1.5f), CampProps.Wood, new Vector3(0f, 0.8f, 0f));
        Part(root, Shapes.RoundedBox(new Vector3(0.5f, 0.04f, 0.05f), 0.01f), CampProps.Wood, new Vector3(0f, 0.04f, 0f));
        for (int i = 0; i < 9; i++)
            Part(root, new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.005f, Height = 0.08f, RadialSegments = 5 }, CampProps.Wood,
                new Vector3(-0.22f + i * 0.055f, -0.01f, 0.03f), new Vector3(70f, 0f, 0f));
        return root;
    }

    /// <summary>Tools leaning against a wall that runs along X at z = 0 (the tools stand in front of it, +Z).</summary>
    public static Node3D LeaningTools(int seed)
    {
        var root = new Node3D { Name = "Tools" };
        var tools = new List<Node3D> { Pitchfork(), Rake(), CampProps.Shovel(out _) };
        for (int i = 0; i < tools.Count; i++)
        {
            if ((seed + i) % 4 == 3)
                continue;
            var t = tools[i];
            t.Position = new Vector3(-0.5f + i * 0.5f, 0f, 0.45f);
            t.RotationDegrees = new Vector3(-17f, (seed * 37 + i * 50) % 30 - 15, 0f);
            root.AddChild(t);
        }
        return root;
    }

    /// <summary>A fishing net hung to dry over a pole between two forked posts, cork floats along its top.</summary>
    public static Node3D NetRack(int seed)
    {
        var root = new Node3D { Name = "NetRack" };
        foreach (float x in new[] { -1.6f, 1.6f })
            Pole(root, new Vector3(x, 0f, 0f), new Vector3(x, 1.75f, 0f), 0.05f, CampProps.DarkWood);
        Pole(root, new Vector3(-1.75f, 1.72f, 0f), new Vector3(1.75f, 1.72f, 0f), 0.035f, CampProps.DarkWood);
        // The net: a draped sheet in two hanging halves over the pole, sagging between the posts.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int nx = 16, ny = 8;
        Vector3 P(int i, int j, float side)
        {
            float x = -1.55f + 3.1f * i / nx, v = j / (float)ny;
            float drop = 0.15f * (1f - (x / 1.55f) * (x / 1.55f));
            return new Vector3(x, 1.7f - v * 1.25f - drop * v, side * (0.04f + v * 0.18f + 0.03f * MathF.Sin(x * 3f + seed)));
        }
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    foreach (var (a, b) in new[] { (i, j), (i + 1, j), (i + 1, j + 1), (i, j), (i + 1, j + 1), (i, j + 1) })
                    {
                        st.SetUV(new Vector2(a / (float)nx * 3f, b / (float)ny * 1.4f));
                        st.AddVertex(P(a, b, side));
                    }
        st.GenerateNormals();
        root.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = CampProps.Net });
        var cork = BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.66f, 0.48f, 0.3f), Roughness = 0.9f });
        for (int i = 0; i < 9; i++)
            Part(root, new SphereMesh { Radius = 0.045f, Height = 0.07f, RadialSegments = 8, Rings = 4 }, cork,
                new Vector3(-1.4f + i * 0.35f, 1.68f, 0f));
        return root;
    }

    /// <summary>
    /// A clinker-built rowboat (3.6 m): the hull lofted from its frames (a sheer rising to the bow, a rounded bilge, the
    /// keel curving up into a pointed bow at +Z and a pointed stern at -Z: a double-ender), six overlapping strakes a side,
    /// a tarred bottom under painted sides, keel, stem and sternpost, gunwales, two thwarts and floorboards. The keel
    /// touches y = 0. <paramref name="rowing"/>: the oars sit in their rowlocks (pivots "OarL"/"OarR" at the rowlocks,
    /// the blade out along -X/+X, turned by the rower); otherwise they lie in the boat.
    /// </summary>
    public static Node3D Rowboat(int seed, bool rowing = false)
    {
        var root = new Node3D { Name = "Rowboat" };
        const int along = 32, around = 24;
        static float Smooth(float a, float b, float x) { float t = Math.Clamp((x - a) / (b - a), 0f, 1f); return t * t * (3f - 2f * t); }
        // A double-ender (a Nordic færing): both ends pointed, the sheer and the forefoot rising to stem and sternpost.
        static float Beam(float t) => 0.74f * MathF.Pow(MathF.Max(MathF.Sin(MathF.PI * t), 0f), 0.7f);
        static float Sheer(float t) => 0.5f + 0.24f * MathF.Pow(MathF.Abs(2f * t - 1f), 3f);
        static float Keel(float t) => 0.32f * (MathF.Pow(Smooth(0.7f, 1f, t), 1.6f) + MathF.Pow(Smooth(0.7f, 1f, 1f - t), 1.6f));
        Vector3 Hull(int i, int k, float inset)
        {
            float t = i / (float)along;                       // 0 = stern, 1 = bow
            float z = -1.8f + 3.6f * t;
            float u = k / (float)around * 2f - 1f;            // -1 = left gunwale, 1 = right gunwale
            float sgn = MathF.Sign(u), sAbs = MathF.Abs(u);
            float q = sAbs * MathF.PI / 2f;
            float bottom = Keel(t), top = Sheer(t);
            // A round bilge: flat-ish across the keel, turning up into flared sides.
            float x = sgn * Beam(t) * MathF.Pow(MathF.Sin(q), 0.85f);
            float y = bottom + (top - bottom) * (1f - MathF.Cos(q));
            // Clinker: each of the six strakes laps over the one below it.
            float strake = MathF.Floor(MathF.Min(sAbs, 0.999f) * 6f);
            x += sgn * 0.012f * strake * MathF.Min(Beam(t) * 4f, 1f);
            x -= sgn * inset;
            y += inset;
            if (sgn != 0f && MathF.Sign(x) != sgn)
                x = sgn * 0.002f;
            return new Vector3(x, y, z);
        }
        // The outside (seen from outside) for a band of the frames round the hull, and the inside 2 cm in.
        ArrayMesh Shell(float inset, bool inside, int k0, int k1)
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            for (int i = 0; i < along; i++)
                for (int k = k0; k < k1; k++)
                {
                    var quad = inside
                        ? new[] { (i, k), (i + 1, k + 1), (i + 1, k), (i, k), (i, k + 1), (i + 1, k + 1) }
                        : new[] { (i, k), (i + 1, k), (i + 1, k + 1), (i, k), (i + 1, k + 1), (i, k + 1) };
                    foreach (var (ia, kb) in quad)
                    {
                        // UV in metres: u along the boat (the boards run bow to stern), v round the hull.
                        st.SetUV(new Vector2(ia / (float)along * 3.6f, kb / (float)around * 1.9f));
                        st.AddVertex(Hull(ia, kb, inset));
                    }
                }
            st.GenerateNormals();
            st.GenerateTangents();
            return st.Commit();
        }
        var paint = new[] { new Color(0.24f, 0.36f, 0.42f), new Color(0.55f, 0.24f, 0.18f), new Color(0.78f, 0.76f, 0.7f), new Color(0.3f, 0.4f, 0.28f) }[seed % 4];
        var sides = BuildingFactory.Weathered("white_planks_clean", paint * 1.4f, 0.75f, triplanar: false, weathering: 1f, moss: 0.1f)
                    ?? BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = paint, Roughness = 0.8f });
        var tar = BuildingFactory.Weathered("weathered_brown_planks", new Color(0.3f, 0.26f, 0.22f), 0.6f, triplanar: false, moss: 0.05f)
                  ?? BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.08f, 0.07f), Roughness = 0.6f });
        var bare = BuildingFactory.Weathered("weathered_brown_planks", new Color(0.78f, 0.74f, 0.68f), 0.9f, triplanar: false, moss: 0.1f)
                   ?? CampProps.Wood;
        // Tar on the bottom two strakes a side, paint above.
        int tarFrom = around / 2 - around / 6, tarTo = around / 2 + around / 6;
        root.AddChild(new MeshInstance3D { Mesh = Shell(0f, false, 0, tarFrom), MaterialOverride = sides });
        root.AddChild(new MeshInstance3D { Mesh = Shell(0f, false, tarTo, around), MaterialOverride = sides });
        root.AddChild(new MeshInstance3D { Mesh = Shell(0f, false, tarFrom, tarTo), MaterialOverride = tar });
        root.AddChild(new MeshInstance3D { Mesh = Shell(0.02f, true, 0, around), MaterialOverride = bare });

        // A piece of timber from a to b (keel, stem, gunwales).
        void Timber(Vector3 from, Vector3 to, float w, float h, Material m)
        {
            var dir = to - from;
            if (dir.LengthSquared() < 1e-6f)
                return;
            var up = MathF.Abs(dir.Normalized().Y) > 0.95f ? Vector3.Forward : Vector3.Up;
            root.AddChild(new MeshInstance3D
            {
                Mesh = Shapes.RoundedBox(new Vector3(w, h, dir.Length() + 0.012f), MathF.Min(w, h) * 0.3f),
                MaterialOverride = m,
                Transform = new Transform3D(Basis.LookingAt(dir.Normalized(), up), (from + to) / 2f),
            });
        }
        // Gunwales along both top edges, the keel under the middle, the stem up the bow.
        foreach (int k in new[] { 0, around })
            for (int i = 0; i < along; i++)
                Timber(Hull(i, k, 0.01f) + Vector3.Up * 0.015f, Hull(i + 1, k, 0.01f) + Vector3.Up * 0.015f, 0.055f, 0.045f, bare);
        for (int i = 0; i < along; i++)
            Timber(Hull(i, around / 2, 0f) + Vector3.Down * 0.025f, Hull(i + 1, around / 2, 0f) + Vector3.Down * 0.025f, 0.06f, 0.05f, tar);
        foreach (var (end, out_) in new[] { (along, 0.02f), (0, -0.02f) })
            for (int k = around / 2; k > 0; k -= 2)
                Timber(Hull(end, k, 0f) + new Vector3(0f, 0f, out_), Hull(end, k - 2, 0f) + new Vector3(0f, 0f, out_), 0.06f, 0.06f, k > tarFrom + 2 ? tar : bare);

        // Thwarts across the boat, floorboards on the bottom.
        foreach (float tz in new[] { 0.25f, 0.66f })
        {
            float z = -1.8f + 3.6f * tz;
            float half = Beam(tz) * 0.97f;
            Part(root, Shapes.RoundedBox(new Vector3(half * 2f, 0.04f, 0.22f), 0.01f), bare, new Vector3(0f, Sheer(tz) - 0.14f, z));
        }
        foreach (float x in new[] { -0.17f, 0f, 0.17f })
            Part(root, Shapes.RoundedBox(new Vector3(0.14f, 0.02f, 2.3f), 0.006f), bare, new Vector3(x, 0.05f + MathF.Abs(x) * 0.25f, 0f));

        // Rowlocks on the gunwales by the rowing thwart, and the oars: in them, or lying in the boat.
        const float lockT = 0.48f;
        float lockZ = -1.8f + 3.6f * lockT;
        foreach (float side in new[] { -1f, 1f })
        {
            var at = new Vector3(side * (Beam(lockT) + 0.012f * 5f), Sheer(lockT) + 0.06f, lockZ);
            Part(root, Rod(0.012f, 0.1f), CampProps.DarkWood, at);
            var pivot = new Node3D { Name = side < 0f ? "OarL" : "OarR", Position = at + Vector3.Up * 0.03f };
            var oar = new Node3D();
            pivot.AddChild(oar);
            // Along the pivot's X: the loom from 0.85 m inboard (the grip) to the blade 1.45 m out.
            Part(oar, Rod(0.02f, 2.3f, 10), bare, new Vector3(side * 0.3f, 0f, 0f), new Vector3(0f, 0f, 90f));
            Part(oar, Shapes.RoundedBox(new Vector3(0.5f, 0.015f, 0.13f), 0.006f), bare, new Vector3(side * 1.25f, 0f, 0f));
            Part(oar, Rod(0.026f, 0.18f, 10), CampProps.DarkWood, new Vector3(-side * 0.76f, 0f, 0f), new Vector3(0f, 0f, 90f));
            oar.AddChild(new Node3D { Name = "Grip", Position = new Vector3(-side * 0.76f, 0f, 0f) });
            if (rowing)
                root.AddChild(pivot);
            else
            {
                // Shipped: lying along the boat on the thwarts.
                var shipped = new Node3D { Position = new Vector3(side * 0.18f, Sheer(0.5f) - 0.08f, -0.2f), RotationDegrees = new Vector3(0f, side * 88f, 0f) };
                oar.Position = Vector3.Zero;
                pivot.RemoveChild(oar);
                shipped.AddChild(oar);
                pivot.Free();
                root.AddChild(shipped);
            }
        }
        return root;
    }
}
