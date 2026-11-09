using System;
using Godot;

namespace FishingSim.World.Props;

/// <summary>
/// One-of-a-kind landmarks that give each fishing ground its character, placed by <see cref="Scenery"/> from
/// <c>LocationData.Landmarks</c>: a ruined tower on an island, a wayside chapel, a hut on stilts in the marsh,
/// a fish-pond dike with its sluice tower, a waterfall in a rock face, and rock islets. Local +Z faces the water.
/// </summary>
public static partial class BuildingFactory
{
    public static string LandmarkTitle(string kind) => kind switch
    {
        "tower" => "Ruiny wieży",
        "chapel" => "Kapliczka",
        "stilthouse" => "Chata na palach",
        "dike" => "Grobla z mnichem",
        "waterfall" => "Wodospad",
        "rocks" => "Skały",
        "platforms" => "Stanowiska",
        _ => "",
    };

    /// <summary>Builds a landmark. <paramref name="size"/> is the length of a dike or the depth below a stilt hut.</summary>
    public static Building Landmark(string kind, float size = 0f)
    {
        var b = new Building { Name = "landmark_" + kind, Title = LandmarkTitle(kind) };
        switch (kind)
        {
            case "tower": RuinedTower(b); break;
            case "chapel": Chapel(b); break;
            case "stilthouse": StiltHouse(b, MathF.Max(size, 1f)); break;
            case "dike": Dike(b, MathF.Max(size, 20f)); break;
            case "waterfall": Waterfall(b); break;
            case "rocks": RockIslet(b); break;
            case "platforms": Platform(b, MathF.Max(size, 0.8f)); break;
        }
        return b;
    }

    // ------------------------------------------------------------------ the island ruin (Jezioro Zielone)
    private static void RuinedTower(Building b)
    {
        var stone = Stone(new Color(0.78f, 0.74f, 0.66f));
        var dark = Flat(new Color(0.08f, 0.07f, 0.06f));
        var ivy = Flat(new Color(0.2f, 0.36f, 0.14f), 1f);
        Cyl(b, 2.5f, 2.9f, 7.5f, new Vector3(0, 3.5f, 0), stone, 14);
        Cyl(b, 3.2f, 3.4f, 0.8f, new Vector3(0, 0.1f, 0), stone, 14);                  // footing
        // Broken crown: merlons of different heights, some missing.
        float[] tops = { 1.8f, 0.5f, 2.6f, 0f, 1.3f, 2.2f, 0f, 1.0f, 0.4f, 1.6f };
        for (int i = 0; i < tops.Length; i++)
        {
            if (tops[i] <= 0f)
                continue;
            float a = i * Mathf.Tau / tops.Length;
            Box(b, new Vector3(1.5f, tops[i], 0.8f), new Vector3(MathF.Sin(a) * 2.2f, 7.25f + tops[i] / 2f, MathF.Cos(a) * 2.2f), stone,
                new Vector3(0, Mathf.RadToDeg(a), 0));
        }
        // Arrow slits and the dark doorway facing the water.
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Tau / 4f + 0.4f;
            Box(b, new Vector3(0.22f, 1.1f, 0.3f), new Vector3(MathF.Sin(a) * 2.62f, 4.6f + (i % 2) * 1.4f, MathF.Cos(a) * 2.62f), dark,
                new Vector3(0, Mathf.RadToDeg(a), 0));
        }
        Box(b, new Vector3(1.2f, 2.2f, 0.5f), new Vector3(0, 1.4f, 2.7f), dark);
        // Ivy climbing the side and fallen blocks around the foot.
        Box(b, new Vector3(1.4f, 4.5f, 0.4f), new Vector3(-1.9f, 3.2f, 1.6f), ivy, new Vector3(0, -50f, 0));
        Box(b, new Vector3(1.0f, 2.6f, 0.4f), new Vector3(2.2f, 2.0f, -1.2f), ivy, new Vector3(0, 120f, 0));
        Box(b, new Vector3(1.1f, 0.6f, 0.8f), new Vector3(3.6f, 0.3f, 1.2f), stone, new Vector3(8f, 30f, 0));
        Box(b, new Vector3(0.9f, 0.5f, 0.7f), new Vector3(-3.2f, 0.25f, 2.4f), stone, new Vector3(0, -20f, 12f));
        Box(b, new Vector3(1.3f, 0.7f, 0.9f), new Vector3(1.0f, 0.35f, -3.8f), stone, new Vector3(-6f, 70f, 0));
        Collider(b, new Vector3(5.6f, 9f, 5.6f), new Vector3(0, 4.5f, 0));
    }

    // ------------------------------------------------------------------ the wayside chapel (Jezioro Zielone)
    private static void Chapel(Building b)
    {
        const float w = 6.5f, d = 4.4f, h = 3.6f;
        var wall = Plaster(new Color(0.95f, 0.92f, 0.85f));
        var stone = Stone(new Color(0.62f, 0.6f, 0.56f));
        var roof = Roof(new Color(0.3f, 0.22f, 0.18f));
        var iron = Flat(new Color(0.15f, 0.13f, 0.12f), 0.5f, 0.5f);
        Box(b, new Vector3(w + 0.3f, 0.5f, d + 0.3f), new Vector3(0, 0.0f, 0), stone);
        Box(b, new Vector3(w, h, d), new Vector3(0, h / 2f, 0), wall);
        AddRoof(b, w, d, h, 3.0f, 0.35f, roof);
        // Bell tower over the door end (+X), with a small spire and an iron cross.
        Box(b, new Vector3(1.9f, 6.2f, 1.9f), new Vector3(w / 2f + 0.6f, 3.1f, 0), wall);
        Cyl(b, 0f, 1.45f, 2.6f, new Vector3(w / 2f + 0.6f, 7.5f, 0), roof, 4).RotationDegrees = new Vector3(0, 45f, 0);
        Box(b, new Vector3(0.1f, 1.2f, 0.1f), new Vector3(w / 2f + 0.6f, 9.3f, 0), iron);
        Box(b, new Vector3(0.1f, 0.1f, 0.6f), new Vector3(w / 2f + 0.6f, 9.5f, 0), iron);
        Box(b, new Vector3(0.5f, 0.9f, 1.95f), new Vector3(w / 2f + 0.6f, 5.3f, 0), Flat(new Color(0.08f, 0.07f, 0.06f)));
        Cyl(b, 0.2f, 0.32f, 0.45f, new Vector3(w / 2f + 0.6f, 5.2f, 0), Flat(new Color(0.55f, 0.42f, 0.18f), 0.4f, 0.8f), 8);
        Box(b, new Vector3(0.1f, 2.0f, 1.0f), new Vector3(w / 2f + 1.56f, 1.0f, 0), Wood(new Color(0.45f, 0.3f, 0.2f)));
        // Arched windows that glow with candles at night.
        var glass = Glass(b);
        foreach (float sx in new[] { -1.6f, 1.0f })
            foreach (float sz in new[] { -1f, 1f })
                Box(b, new Vector3(0.7f, 1.3f, 0.1f), new Vector3(sx, 2.1f, sz * (d / 2f + 0.02f)), glass);
        Collider(b, new Vector3(w + 2.5f, h + 1f, d), new Vector3(0.5f, (h + 1f) / 2f, 0));
    }

    // ------------------------------------------------------------------ the marsh hut on stilts (Zatoka Szuwarowa)
    private static void StiltHouse(Building b, float depth)
    {
        // The node stands on the water surface (y = 0); posts go down to the bottom.
        var timber = Timber();
        var planks = Wood(new Color(0.62f, 0.48f, 0.34f));
        float postLen = depth + 2.2f;
        foreach (float sx in new[] { -2.2f, 0f, 2.2f })
            foreach (float sz in new[] { -1.9f, 1.9f })
                Cyl(b, 0.13f, 0.16f, postLen, new Vector3(sx, 1.6f - postLen / 2f, sz), timber, 6);
        Box(b, new Vector3(5.6f, 0.2f, 4.6f), new Vector3(0, 1.6f, 0), planks);                 // deck
        Box(b, new Vector3(3.6f, 2.3f, 3.0f), new Vector3(-0.6f, 2.85f, -0.3f), planks);         // hut
        var hut = new Node3D { Position = new Vector3(-0.6f, 0, -0.3f) };
        b.AddChild(hut);
        AddRoof(hut, 3.6f, 3.0f, 4.0f, 1.9f, 0.4f, Roof(new Color(0.5f, 0.4f, 0.2f)));
        Box(b, new Vector3(0.9f, 1.8f, 0.08f), new Vector3(-0.2f, 2.6f, 1.23f), Flat(new Color(0.12f, 0.09f, 0.07f)));
        Box(b, new Vector3(0.7f, 0.6f, 0.08f), new Vector3(-1.6f, 3.1f, 1.23f), Glass(b));
        // Railing, a ladder into the water and a net drying on poles.
        Box(b, new Vector3(5.6f, 0.08f, 0.08f), new Vector3(0, 2.5f, 2.25f), timber);
        Box(b, new Vector3(0.08f, 0.08f, 4.6f), new Vector3(2.75f, 2.5f, 0), timber);
        foreach (float sx in new[] { 2.75f, 0.9f })
            Box(b, new Vector3(0.1f, 0.9f, 0.1f), new Vector3(sx, 2.05f, 2.25f), timber);
        foreach (float sx in new[] { -0.3f, 0.3f })
            Box(b, new Vector3(0.08f, 2.4f, 0.08f), new Vector3(1.4f + sx, 0.5f, 2.45f), timber, new Vector3(-12f, 0, 0));
        for (int i = 0; i < 5; i++)
            Box(b, new Vector3(0.6f, 0.06f, 0.06f), new Vector3(1.4f, -0.4f + i * 0.45f, 2.45f - (i - 2) * 0.1f), timber);
        Box(b, new Vector3(0.06f, 1.9f, 1.6f), new Vector3(2.5f, 2.6f, -0.9f), Flat(new Color(0.25f, 0.22f, 0.16f), 1f));
        // A flat-bottomed punt tied to the ladder.
        Box(b, new Vector3(3.2f, 0.35f, 0.9f), new Vector3(2.8f, 0.1f, 3.4f), Painted(new Color(0.35f, 0.3f, 0.22f)), new Vector3(0, 15f, 0));
        Collider(b, new Vector3(5.6f, 0.4f, 4.6f), new Vector3(0, 1.6f, 0));
    }

    // ------------------------------------------------------------------ the fish-pond dike and its sluice (Staw Karpiowy)
    private static void Dike(Building b, float length)
    {
        var earth = Flat(new Color(0.36f, 0.5f, 0.2f), 1f);
        var path = Flat(new Color(0.55f, 0.45f, 0.3f), 1f);
        var concrete = Stone(new Color(0.7f, 0.68f, 0.62f));
        var wood = Wood(new Color(0.48f, 0.34f, 0.24f));
        // A long earth bank with sloping sides and a cart track on the crown.
        Box(b, new Vector3(length, 1.4f, 3.2f), new Vector3(0, 0.3f, 0), earth);
        foreach (float sz in new[] { -1f, 1f })
            Box(b, new Vector3(length, 0.5f, 2.6f), new Vector3(0, 0.25f, sz * 2.4f), earth, new Vector3(sz * 18f, 0, 0));
        Box(b, new Vector3(length, 0.06f, 1.6f), new Vector3(0, 1.02f, 0), path);
        // The "monk": a sluice tower in the water with boards that hold the level, and a plank bridge to it.
        Box(b, new Vector3(1.6f, 3.6f, 1.6f), new Vector3(0, 0.4f, 4.2f), concrete);
        Box(b, new Vector3(1.2f, 2.6f, 0.12f), new Vector3(0, 0.6f, 5.02f), wood);
        for (int i = 0; i < 5; i++)
            Box(b, new Vector3(1.25f, 0.05f, 0.14f), new Vector3(0, -0.5f + i * 0.5f, 5.06f), Flat(new Color(0.2f, 0.15f, 0.1f)));
        Box(b, new Vector3(0.9f, 0.1f, 2.6f), new Vector3(0, 1.15f, 2.4f), wood);
        Cyl(b, 0.35f, 0.35f, 0.08f, new Vector3(0, 2.35f, 4.2f), Flat(new Color(0.2f, 0.2f, 0.22f), 0.5f, 0.6f), 10);
        // Posts with a rope along the water side.
        int posts = (int)(length / 6f);
        for (int i = 0; i <= posts; i++)
            Box(b, new Vector3(0.14f, 0.9f, 0.14f), new Vector3(-length / 2f + i * length / posts, 1.45f, 1.4f), wood);
        Box(b, new Vector3(length, 0.04f, 0.04f), new Vector3(0, 1.75f, 1.4f), Flat(new Color(0.75f, 0.68f, 0.5f)));
        // A row of tall poplars along the crown, the mark of an old fish-pond dike seen from across the water.
        var bark = Flat(new Color(0.42f, 0.36f, 0.3f), 1f);
        var crown = Flat(new Color(0.2f, 0.38f, 0.14f), 1f);
        var crownLight = Flat(new Color(0.32f, 0.5f, 0.18f), 1f);
        int trees = (int)(length / 8f);
        for (int i = 0; i <= trees; i++)
        {
            float x = -length / 2f + i * length / trees;
            if (MathF.Abs(x) < 4f)
                continue;   // leave the sluice in the open
            float h = 9f + (i * 37 % 7) * 0.5f;
            Cyl(b, 0.12f, 0.2f, 2.4f, new Vector3(x, 2.2f, -0.9f), bark, 6);
            Cyl(b, 0.25f, 1.25f, h * 0.55f, new Vector3(x, 3.2f + h * 0.275f, -0.9f), crown, 7);
            Cyl(b, 0f, 0.95f, h * 0.5f, new Vector3(x, 3.2f + h * 0.55f + h * 0.2f, -0.9f), i % 2 == 0 ? crownLight : crown, 7);
        }
        Collider(b, new Vector3(1.6f, 3.6f, 1.6f), new Vector3(0, 0.4f, 4.2f));
    }

    /// <summary>Small wooden angling platforms along the straight banks of the fish pond.</summary>
    private static void Platform(Building b, float depth)
    {
        var wood = Wood(new Color(0.66f, 0.5f, 0.34f));
        Box(b, new Vector3(2.2f, 0.12f, 2.0f), new Vector3(0, 0.45f, 1.0f), wood);
        foreach (float sx in new[] { -0.95f, 0.95f })
            foreach (float sz in new[] { 0.15f, 1.85f })
                Cyl(b, 0.08f, 0.08f, depth + 1.2f, new Vector3(sx, 0.45f - (depth + 1.2f) / 2f, sz), wood, 6);
        Box(b, new Vector3(0.5f, 0.06f, 0.3f), new Vector3(0.7f, 0.85f, 1.7f), wood);   // rod rest
    }

    // ------------------------------------------------------------------ the waterfall (Górskie Jezioro)
    private static void Waterfall(Building b)
    {
        var rock = Stone(new Color(0.74f, 0.72f, 0.7f));
        var darkRock = Stone(new Color(0.58f, 0.57f, 0.6f));
        // A ridge of rock that rises to the lip of the fall in the middle and sinks into the slope at the sides,
        // with moss and grass on the ledges.
        var moss = Flat(new Color(0.3f, 0.45f, 0.18f), 1f);
        var rng = new RandomNumberGenerator { Seed = 5 };
        for (int i = -5; i <= 5; i++)
        {
            float x = i * 3.4f + rng.RandfRange(-0.6f, 0.6f);
            float h = MathF.Max(17.5f - MathF.Abs(i) * 3f + rng.RandfRange(-1.5f, 1.5f), 3f);
            if (i == 0) h = 16.6f;
            float depth = rng.RandfRange(6f, 9f);
            float back = -depth / 2f - (i == 0 ? 0.8f : rng.RandfRange(0f, 2f));
            var rot = new Vector3(rng.RandfRange(-4f, 4f), rng.RandfRange(-12f, 12f), rng.RandfRange(-5f, 5f));
            Box(b, new Vector3(4.2f, h, depth), new Vector3(x, h / 2f - 1f, back), (i & 1) == 0 ? rock : darkRock, rot);
            if (i != 0)
                Box(b, new Vector3(3.8f, 0.5f, depth * 0.8f), new Vector3(x, h - 0.9f, back), moss, rot);
            // A ledge half-way up breaks the straight face.
            if (MathF.Abs(i) is >= 1 and <= 3)
                Box(b, new Vector3(3.2f, 2.2f, 1.6f), new Vector3(x, h * rng.RandfRange(0.35f, 0.6f), back + depth / 2f + 0.5f), (i & 1) == 0 ? darkRock : rock, rot);
        }
        // The falling sheet: a lip at the top and the curtain down to the plunge pool.
        var sheet = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/waterfall.gdshader") };
        var curtain = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(3.2f, 16.5f) },
            MaterialOverride = sheet,
            Position = new Vector3(0, 8.2f, 0.4f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        b.AddChild(curtain);
        Box(b, new Vector3(3.4f, 0.4f, 1.6f), new Vector3(0, 16.4f, -0.3f), Flat(new Color(0.55f, 0.75f, 0.86f), 0.4f));
        // Foam and boulders in the plunge pool.
        var foam = Flat(new Color(0.93f, 0.96f, 1f), 0.9f);
        for (int i = 0; i < 6; i++)
        {
            float a = i * 1.1f;
            b.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.9f, Height = 0.6f, RadialSegments = 8, Rings = 3 },
                MaterialOverride = foam,
                Position = new Vector3(MathF.Sin(a) * 1.6f, 0.05f, 1.2f + MathF.Cos(a) * 0.8f),
            });
        }
        foreach (var (pos, s) in new[] { (new Vector3(-3.2f, 0.2f, 2.2f), 1.2f), (new Vector3(3.5f, 0.1f, 1.6f), 1.5f), (new Vector3(-1.4f, 0f, 3.6f), 0.8f) })
            Box(b, new Vector3(s * 1.6f, s, s * 1.3f), pos, darkRock, new Vector3(10f, pos.X * 20f, 6f));
        Collider(b, new Vector3(34f, 16f, 7f), new Vector3(0, 8f, -4.5f));
    }

    // ------------------------------------------------------------------ rock islets (Górskie Jezioro)
    private static void RockIslet(Building b)
    {
        var rock = Stone(new Color(0.52f, 0.52f, 0.55f));
        var moss = Flat(new Color(0.3f, 0.4f, 0.2f), 1f);
        (Vector3 Size, Vector3 Pos, Vector3 Rot)[] stones =
        {
            (new(4.5f, 3.2f, 3.8f), new(0, 0.6f, 0), new(8f, 20f, -6f)),
            (new(3f, 2.4f, 2.6f), new(2.6f, 0.2f, 1.4f), new(-10f, 50f, 12f)),
            (new(2.4f, 4.2f, 2.2f), new(-1.8f, 1.2f, -1.2f), new(4f, -30f, 10f)),
            (new(1.8f, 1.2f, 1.6f), new(-3.2f, -0.2f, 2.0f), new(14f, 10f, 0)),
        };
        foreach (var (size, pos, rot) in stones)
            Box(b, size, pos, rock, rot);
        Box(b, new Vector3(2.2f, 0.3f, 1.8f), new Vector3(-0.2f, 2.3f, 0.2f), moss, new Vector3(8f, 20f, -6f));
        // A wind-bent dead pine clinging to the top.
        var trunk = Cyl(b, 0.08f, 0.18f, 3.2f, new Vector3(-1.8f, 4.6f, -1.2f), Wood(new Color(0.55f, 0.48f, 0.4f)), 6);
        trunk.RotationDegrees = new Vector3(0, 0, -14f);
        Box(b, new Vector3(1.4f, 0.08f, 0.08f), new Vector3(-1.4f, 5.4f, -1.2f), Wood(new Color(0.55f, 0.48f, 0.4f)), new Vector3(0, 0, 20f));
    }
}
