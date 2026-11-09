using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Data;

namespace FishingSim.World.Props;

/// <summary>
/// Builds the village from primitives: shops, houses, a barn, a windmill, lamps, benches, boats, fences and so on.
/// Every building faces +Z locally; the caller rotates and places it. Solid buildings get a collider.
/// </summary>
public static class BuildingFactory
{
    private const float DoorWidth = 1.1f;
    private const float DoorHeight = 2.1f;

    public static string TitleFor(BuildingKind kind) => kind switch
    {
        BuildingKind.Tackle => "Sklep wędkarski",
        BuildingKind.General => "Sklep wędkarski i przynęty",
        BuildingKind.Bait => "Sklep z przynętami",
        BuildingKind.FishBuyer => "Skup ryb",
        BuildingKind.Office => "Biuro zawodów",
        BuildingKind.BusStop => "Przystanek",
        _ => "",
    };

    public static Building Create(BuildingSpec spec, int variant)
    {
        var b = new Building { Spec = spec, Title = TitleFor(spec.Kind) };
        switch (spec.Kind)
        {
            case BuildingKind.Tackle:
                House(b, new HouseStyle(9f, 6.5f, 3.5f, 2.5f, new Color(0.38f, 0.56f, 0.72f), true, new Color(0.62f, 0.22f, 0.16f), 3, true, true, "SKLEP WĘDKARSKI"));
                Barrel(b, new Vector3(-2.6f, 0, 3.9f)); Crates(b, new Vector3(2.8f, 0, 4.0f), 2); RodRack(b, new Vector3(-4.52f, 0, 0.5f));
                break;
            case BuildingKind.General:
                House(b, new HouseStyle(8.5f, 6.5f, 3.4f, 2.4f, new Color(0.55f, 0.42f, 0.28f), false, new Color(0.30f, 0.36f, 0.42f), 3, true, true, "SKLEP WĘDKARSKI"));
                Barrel(b, new Vector3(-2.5f, 0, 3.9f)); Crates(b, new Vector3(2.7f, 0, 4.0f), 3); RodRack(b, new Vector3(-4.27f, 0, 0.5f));
                break;
            case BuildingKind.Bait:
                House(b, new HouseStyle(6.5f, 5.2f, 3.1f, 2.1f, new Color(0.40f, 0.62f, 0.36f), true, new Color(0.38f, 0.27f, 0.2f), 2, false, true, "PRZYNĘTY"));
                Barrel(b, new Vector3(-2.0f, 0, 3.3f)); Barrel(b, new Vector3(-2.7f, 0, 3.0f)); Crates(b, new Vector3(2.2f, 0, 3.3f), 2);
                break;
            case BuildingKind.Office:
                House(b, new HouseStyle(8.5f, 6.2f, 3.3f, 2.2f, new Color(0.9f, 0.9f, 0.84f), true, new Color(0.2f, 0.36f, 0.62f), 3, false, true, "BIURO ZAWODÓW"));
                Flag(b, new Vector3(4.3f, 0, 3.6f));
                break;
            case BuildingKind.House:
                Color[] walls = { new(0.86f, 0.8f, 0.66f), new(0.78f, 0.66f, 0.55f), new(0.7f, 0.78f, 0.7f), new(0.82f, 0.72f, 0.72f) };
                House(b, new HouseStyle(7f, 6f, 3f, 2.2f, walls[variant % walls.Length], true, new Color(0.5f, 0.2f, 0.16f), 2, true, false, null));
                break;
            case BuildingKind.Barn:
                Barn(b);
                break;
            case BuildingKind.Boathouse:
                House(b, new HouseStyle(6.5f, 5f, 2.8f, 1.9f, new Color(0.45f, 0.34f, 0.24f), false, new Color(0.24f, 0.21f, 0.19f), 1, false, false, null));
                Crates(b, new Vector3(2.4f, 0, 3.4f), 2);
                break;
            case BuildingKind.FishBuyer:
                FishStall(b);
                break;
            case BuildingKind.BusStop:
                BusStop(b);
                break;
            case BuildingKind.Windmill:
                Windmill(b);
                break;
            case BuildingKind.Haystack:
                Haystack(b);
                break;
            case BuildingKind.Lamp:
                Lamp(b);
                break;
            case BuildingKind.Bench:
                Bench(b);
                break;
            case BuildingKind.Boat:
                Boat(b);
                break;
            case BuildingKind.Crates:
                Crates(b, Vector3.Zero, 3);
                Barrel(b, new Vector3(1.3f, 0, 0.2f));
                break;
            case BuildingKind.Signpost:
                Signpost(b);
                break;
            case BuildingKind.Fence:
                Fence(b, 12f);
                break;
        }
        return b;
    }

    // ------------------------------------------------------------------ materials
    private static StandardMaterial3D Painted(Color color) => new()
    {
        AlbedoTexture = Textures.PlanksPainted(), AlbedoColor = color, Uv1Triplanar = true, Uv1Scale = Vector3.One * 0.9f, Roughness = 0.85f,
    };

    private static StandardMaterial3D Wood(Color tint) => new()
    {
        AlbedoTexture = Textures.Planks(), AlbedoColor = tint, Uv1Triplanar = true, Uv1Scale = Vector3.One, Roughness = 0.9f,
    };

    private static StandardMaterial3D Roof(Color color) => new()
    {
        AlbedoTexture = Textures.Shingles(), AlbedoColor = color, Uv1Triplanar = true, Uv1Scale = Vector3.One * 0.7f,
        Roughness = 0.85f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static StandardMaterial3D Stone(Color tint) => new()
    {
        AlbedoTexture = Textures.Stone(), AlbedoColor = tint, Uv1Triplanar = true, Uv1Scale = Vector3.One * 0.6f, Roughness = 1f,
    };

    private static StandardMaterial3D Flat(Color color, float roughness = 0.8f, float metallic = 0f) => new()
    {
        AlbedoColor = color, Roughness = roughness, Metallic = metallic,
    };

    private static StandardMaterial3D Glass(Building b)
    {
        var m = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.72f, 0.82f),
            Roughness = 0.2f,
            EmissionEnabled = false,
            Emission = new Color(1f, 0.78f, 0.4f),
        };
        b.AddGlow(m);
        return m;
    }

    // ------------------------------------------------------------------ primitives
    private static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Material mat, Vector3? rotDeg = null)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat, Position = pos };
        if (rotDeg is Vector3 r)
            mi.RotationDegrees = r;
        parent.AddChild(mi);
        return mi;
    }

    private static MeshInstance3D Cyl(Node3D parent, float top, float bottom, float height, Vector3 pos, Material mat, int segments = 10)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = top, BottomRadius = bottom, Height = height, RadialSegments = segments, Rings = 1 },
            MaterialOverride = mat,
            Position = pos,
        };
        parent.AddChild(mi);
        return mi;
    }

    private static void Collider(Node3D parent, Vector3 size, Vector3 pos)
    {
        var body = new StaticBody3D { CollisionLayer = 1, Position = pos };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
    }

    private static void Label(Node3D parent, string text, Vector3 pos, float pixelSize = 0.011f, int fontSize = 64, Color? color = null)
    {
        parent.AddChild(new Label3D
        {
            Text = text,
            Position = pos,
            PixelSize = pixelSize,
            FontSize = fontSize,
            OutlineSize = 6,
            Modulate = color ?? new Color(0.98f, 0.93f, 0.8f),
            OutlineModulate = new Color(0.05f, 0.04f, 0.03f),
            DoubleSided = false,
            AlphaCut = Label3D.AlphaCutMode.Discard,
        });
    }

    // ------------------------------------------------------------------ the generic house
    private sealed record HouseStyle(float W, float D, float H, float RoofH, Color Wall, bool PaintedWall, Color RoofColor,
        int Windows, bool Chimney, bool Awning, string? Sign);

    private static void House(Building b, HouseStyle s)
    {
        var wallMat = s.PaintedWall ? Painted(s.Wall) : Wood(s.Wall * 1.9f);
        var trim = Flat(new Color(0.93f, 0.9f, 0.82f), 0.7f);
        var dark = Wood(new Color(0.55f, 0.42f, 0.3f));
        var glass = Glass(b);

        // foundation, walls
        Box(b, new Vector3(s.W + 0.3f, 0.5f, s.D + 0.3f), new Vector3(0, 0.25f, 0), Stone(new Color(0.8f, 0.78f, 0.74f)));
        Box(b, new Vector3(s.W, s.H, s.D), new Vector3(0, s.H / 2f + 0.15f, 0), wallMat);
        Collider(b, new Vector3(s.W + 0.3f, s.H + 0.4f, s.D + 0.3f), new Vector3(0, (s.H + 0.4f) / 2f, 0));
        // corner trim
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                Box(b, new Vector3(0.22f, s.H, 0.22f), new Vector3(sx * s.W / 2f, s.H / 2f + 0.15f, sz * s.D / 2f), trim);

        AddRoof(b, s.W, s.D, s.H + 0.15f, s.RoofH, 0.6f, Roof(s.RoofColor));

        // door with frame, step and knob
        float front = s.D / 2f;
        Box(b, new Vector3(DoorWidth + 0.3f, DoorHeight + 0.2f, 0.1f), new Vector3(0, DoorHeight / 2f + 0.5f, front + 0.02f), trim);
        Box(b, new Vector3(DoorWidth, DoorHeight, 0.14f), new Vector3(0, DoorHeight / 2f + 0.45f, front + 0.05f), dark);
        Box(b, new Vector3(0.08f, 0.08f, 0.1f), new Vector3(0.38f, 1.5f, front + 0.14f), Flat(new Color(0.8f, 0.7f, 0.3f), 0.3f, 0.8f));
        Box(b, new Vector3(DoorWidth + 0.7f, 0.2f, 0.9f), new Vector3(0, 0.1f, front + 0.55f), Stone(new Color(0.85f, 0.83f, 0.8f)));

        // windows: front pair around the door, one on each side
        float wy = 1.9f;
        float[] frontX = s.Windows >= 3 ? new[] { -s.W * 0.31f, s.W * 0.31f } : s.Windows == 2 ? new[] { -s.W * 0.3f, s.W * 0.3f } : new[] { s.W * 0.3f };
        foreach (float x in frontX)
            Window(b, glass, trim, new Vector3(x, wy, front + 0.02f), 0f);
        if (s.Windows >= 2)
        {
            Window(b, glass, trim, new Vector3(s.W / 2f + 0.02f, wy, 0f), 90f);
            Window(b, glass, trim, new Vector3(-s.W / 2f - 0.02f, wy, 0f), -90f);
        }

        if (s.Chimney)
        {
            Box(b, new Vector3(0.8f, s.RoofH + 1.6f, 0.8f), new Vector3(s.W * 0.28f, s.H + s.RoofH * 0.35f + 0.6f, -s.D * 0.18f), Stone(new Color(0.7f, 0.5f, 0.45f)));
            Box(b, new Vector3(1.0f, 0.15f, 1.0f), new Vector3(s.W * 0.28f, s.H + s.RoofH + 1.5f, -s.D * 0.18f), Flat(new Color(0.25f, 0.22f, 0.2f)));
        }

        if (s.Awning)
        {
            Box(b, new Vector3(3.4f, 0.12f, 1.8f), new Vector3(0, 3.05f, front + 0.85f), Roof(s.RoofColor), new Vector3(14f, 0, 0));
            foreach (float sx in new[] { -1.55f, 1.55f })
                Cyl(b, 0.07f, 0.07f, 2.9f, new Vector3(sx, 1.6f, front + 1.6f), dark, 8);
        }
        if (s.Sign != null)
        {
            float signY = s.Awning ? 3.55f : 3.05f;
            Box(b, new Vector3(s.Sign.Length * 0.22f + 0.8f, 0.7f, 0.12f), new Vector3(0, signY, front + 0.12f), Flat(new Color(0.16f, 0.12f, 0.09f), 0.9f));
            Label(b, s.Sign, new Vector3(0, signY, front + 0.2f), 0.0095f, 60);
        }
        var lamp = new OmniLight3D
        {
            LightColor = new Color(1f, 0.78f, 0.45f), OmniRange = 9f, LightEnergy = 0f, ShadowEnabled = false,
            Position = new Vector3(0, 2.8f, front + 1.2f),
        };
        b.AddChild(lamp);
        b.AddLight(lamp);
    }

    private static void Window(Building b, StandardMaterial3D glass, StandardMaterial3D trim, Vector3 pos, float yawDeg)
    {
        var holder = new Node3D { Position = pos, RotationDegrees = new Vector3(0, yawDeg, 0) };
        b.AddChild(holder);
        Box(holder, new Vector3(1.25f, 1.25f, 0.08f), Vector3.Zero, trim);
        Box(holder, new Vector3(1.0f, 1.0f, 0.1f), new Vector3(0, 0, 0.02f), glass);
        Box(holder, new Vector3(0.06f, 1.0f, 0.12f), new Vector3(0, 0, 0.04f), trim);
        Box(holder, new Vector3(1.0f, 0.06f, 0.12f), new Vector3(0, 0, 0.04f), trim);
        Box(holder, new Vector3(1.5f, 0.1f, 0.25f), new Vector3(0, -0.68f, 0.1f), trim);
    }

    /// <summary>Gabled roof along X with overhang: two slopes plus two gable triangles.</summary>
    private static void AddRoof(Node3D parent, float w, float d, float baseY, float roofH, float overhang, Material mat)
    {
        float hw = w / 2f + overhang, hd = d / 2f + overhang;
        float ridge = baseY + roofH;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(Vector3 a, Vector3 b2, Vector3 c, Vector3 d2)
        {
            st.AddVertex(a); st.AddVertex(b2); st.AddVertex(c);
            st.AddVertex(a); st.AddVertex(c); st.AddVertex(d2);
        }
        Quad(new Vector3(-hw, baseY - 0.1f, hd), new Vector3(hw, baseY - 0.1f, hd), new Vector3(hw, ridge, 0), new Vector3(-hw, ridge, 0));
        Quad(new Vector3(hw, baseY - 0.1f, -hd), new Vector3(-hw, baseY - 0.1f, -hd), new Vector3(-hw, ridge, 0), new Vector3(hw, ridge, 0));
        float apex = baseY + roofH * (d / 2f) / hd;
        foreach (float sx in new[] { -1f, 1f })
        {
            float x = sx * w / 2f;
            st.AddVertex(new Vector3(x, baseY, -d / 2f)); st.AddVertex(new Vector3(x, baseY, d / 2f)); st.AddVertex(new Vector3(x, apex, 0));
        }
        st.GenerateNormals();
        parent.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = mat });
        // ridge cap
        Box(parent, new Vector3(w + overhang * 2f + 0.1f, 0.14f, 0.3f), new Vector3(0, ridge + 0.05f, 0), Flat(new Color(0.2f, 0.17f, 0.15f)));
    }

    // ------------------------------------------------------------------ shop dressing
    private static void Barrel(Node3D parent, Vector3 pos)
    {
        var wood = Wood(new Color(0.7f, 0.5f, 0.35f));
        Cyl(parent, 0.4f, 0.4f, 0.95f, pos + new Vector3(0, 0.475f, 0), wood, 12);
        foreach (float y in new[] { 0.2f, 0.75f })
            Cyl(parent, 0.43f, 0.43f, 0.06f, pos + new Vector3(0, y, 0), Flat(new Color(0.18f, 0.17f, 0.16f), 0.5f, 0.6f), 12);
        Collider(parent, new Vector3(0.85f, 0.95f, 0.85f), pos + new Vector3(0, 0.475f, 0));
    }

    private static void Crates(Node3D parent, Vector3 pos, int count)
    {
        var wood = Wood(new Color(0.8f, 0.62f, 0.42f));
        for (int i = 0; i < count; i++)
        {
            float yaw = 12f * (i + 1) - 10f;
            var offset = i == 2 ? new Vector3(0.1f, 1.1f, 0.05f) : new Vector3(i * 1.05f, 0.5f, i * 0.1f);
            Box(parent, new Vector3(0.95f, 0.95f, 0.95f), pos + offset, wood, new Vector3(0, yaw, 0));
        }
        Collider(parent, new Vector3(count * 1.0f, 1.0f, 1.0f), pos + new Vector3(count * 0.45f, 0.5f, 0));
    }

    private static void RodRack(Node3D parent, Vector3 pos)
    {
        var graphite = Flat(new Color(0.1f, 0.1f, 0.12f), 0.4f);
        for (int i = 0; i < 4; i++)
        {
            var rod = Cyl(parent, 0.008f, 0.02f, 3.6f, pos + new Vector3(-0.12f * (i % 2), 1.85f, -0.9f + i * 0.55f), graphite, 6);
            rod.RotationDegrees = new Vector3(0, 0, -3f - i);
        }
    }

    private static void Flag(Building b, Vector3 pos)
    {
        Cyl(b, 0.05f, 0.07f, 5f, pos + new Vector3(0, 2.5f, 0), Flat(new Color(0.8f, 0.8f, 0.82f), 0.4f, 0.7f), 8);
        Box(b, new Vector3(1.5f, 0.9f, 0.04f), pos + new Vector3(0.78f, 4.4f, 0), Flat(new Color(0.85f, 0.15f, 0.15f), 0.7f));
        Box(b, new Vector3(0.5f, 0.5f, 0.05f), pos + new Vector3(0.32f, 4.4f, 0), Flat(new Color(0.95f, 0.95f, 0.95f), 0.7f));
    }

    private static void Barn(Building b)
    {
        const float w = 12f, d = 9f, h = 4.6f;
        var red = Painted(new Color(0.62f, 0.17f, 0.13f));
        var white = Flat(new Color(0.92f, 0.9f, 0.84f), 0.7f);
        Box(b, new Vector3(w, h, d), new Vector3(0, h / 2f, 0), red);
        Collider(b, new Vector3(w, h + 1f, d), new Vector3(0, (h + 1f) / 2f, 0));
        AddRoof(b, w, d, h, 3.6f, 0.5f, Roof(new Color(0.34f, 0.34f, 0.36f)));
        // big double door with white cross bracing
        Box(b, new Vector3(4.4f, 3.4f, 0.12f), new Vector3(0, 1.7f, d / 2f + 0.04f), Wood(new Color(0.5f, 0.32f, 0.22f)));
        foreach (float rot in new[] { 38f, -38f })
            foreach (float sx in new[] { -1.1f, 1.1f })
                Box(b, new Vector3(0.14f, 2.9f, 0.08f), new Vector3(sx, 1.7f, d / 2f + 0.12f), white, new Vector3(0, 0, rot));
        Box(b, new Vector3(4.6f, 0.16f, 0.1f), new Vector3(0, 3.45f, d / 2f + 0.1f), white);
        Box(b, new Vector3(0.16f, 3.5f, 0.1f), new Vector3(0, 1.7f, d / 2f + 0.14f), white);
        Box(b, new Vector3(1.2f, 1.1f, 0.1f), new Vector3(0, h + 1.2f, d / 2f + 0.1f), Flat(new Color(0.1f, 0.08f, 0.07f)));
    }

    private static void FishStall(Building b)
    {
        var wood = Wood(new Color(0.65f, 0.5f, 0.36f));
        var awning = Roof(new Color(0.18f, 0.4f, 0.55f));
        const float w = 8f, d = 5f;
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                Cyl(b, 0.1f, 0.1f, 3.2f, new Vector3(sx * (w / 2f - 0.2f), 1.6f, sz * (d / 2f - 0.2f)), wood, 8);
                Collider(b, new Vector3(0.3f, 3.2f, 0.3f), new Vector3(sx * (w / 2f - 0.2f), 1.6f, sz * (d / 2f - 0.2f)));
            }
        AddRoof(b, w, d, 3.2f, 1.2f, 0.7f, awning);
        // counter, crates of fish, scale
        Box(b, new Vector3(w - 1.5f, 1f, 1.2f), new Vector3(0, 0.5f, 0.3f), wood);
        Collider(b, new Vector3(w - 1.5f, 1.2f, 1.3f), new Vector3(0, 0.6f, 0.3f));
        Box(b, new Vector3(w - 1.3f, 0.1f, 1.4f), new Vector3(0, 1.02f, 0.3f), Wood(new Color(0.85f, 0.7f, 0.5f)));
        var silver = Flat(new Color(0.62f, 0.7f, 0.76f), 0.3f, 0.5f);
        for (int i = 0; i < 4; i++)
        {
            var fish = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.3f, Height = 0.4f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = silver,
                Position = new Vector3(-1.9f + i * 0.5f, 1.2f, 0.3f + (i % 2) * 0.2f),
                Scale = new Vector3(0.45f, 0.3f, 1f),
                RotationDegrees = new Vector3(0, 90f + i * 12f, 0),
            };
            b.AddChild(fish);
        }
        Box(b, new Vector3(1.0f, 0.1f, 0.7f), new Vector3(2.4f, 1.12f, 0.3f), Flat(new Color(0.3f, 0.3f, 0.32f), 0.4f, 0.7f));
        Cyl(b, 0.06f, 0.12f, 0.45f, new Vector3(2.4f, 1.4f, 0.3f), Flat(new Color(0.3f, 0.3f, 0.32f), 0.4f, 0.7f), 8);
        // drying rack with hanging fish
        Box(b, new Vector3(0.12f, 2.4f, 0.12f), new Vector3(-w / 2f - 1.6f, 1.2f, 0.5f), wood);
        Box(b, new Vector3(0.12f, 2.4f, 0.12f), new Vector3(-w / 2f - 1.6f, 1.2f, 2.5f), wood);
        Box(b, new Vector3(0.1f, 0.1f, 2.4f), new Vector3(-w / 2f - 1.6f, 2.35f, 1.5f), wood);
        for (int i = 0; i < 5; i++)
            b.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.25f, Height = 0.4f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = silver,
                Position = new Vector3(-w / 2f - 1.6f, 2.0f, 0.65f + i * 0.4f),
                Scale = new Vector3(0.3f, 1f, 0.5f),
            });
        // sign on the front beam
        Box(b, new Vector3(3.6f, 0.75f, 0.12f), new Vector3(0, 3.55f, d / 2f + 0.3f), Flat(new Color(0.16f, 0.12f, 0.09f), 0.9f));
        Label(b, "SKUP RYB", new Vector3(0, 3.55f, d / 2f + 0.38f), 0.011f, 64);
        var lamp = new OmniLight3D { LightColor = new Color(1f, 0.78f, 0.45f), OmniRange = 9f, LightEnergy = 0f, Position = new Vector3(0, 2.8f, 2.2f) };
        b.AddChild(lamp);
        b.AddLight(lamp);
    }

    private static void BusStop(Building b)
    {
        var wood = Wood(new Color(0.6f, 0.45f, 0.32f));
        var roof = Roof(new Color(0.3f, 0.34f, 0.4f));
        foreach (float sx in new[] { -1.5f, 1.5f })
        {
            Cyl(b, 0.08f, 0.08f, 2.6f, new Vector3(sx, 1.3f, -0.9f), wood, 8);
            Cyl(b, 0.08f, 0.08f, 2.6f, new Vector3(sx, 1.3f, 0.9f), wood, 8);
        }
        Box(b, new Vector3(3.6f, 0.14f, 2.4f), new Vector3(0, 2.65f, 0), roof, new Vector3(-8f, 0, 0));
        Box(b, new Vector3(3.0f, 1.4f, 0.1f), new Vector3(0, 1.4f, -1.0f), Painted(new Color(0.45f, 0.55f, 0.62f)));
        Box(b, new Vector3(2.6f, 0.1f, 0.6f), new Vector3(0, 0.55f, -0.65f), wood);
        Collider(b, new Vector3(3.2f, 2.6f, 0.4f), new Vector3(0, 1.3f, -1.0f));
        Cyl(b, 0.05f, 0.05f, 3.4f, new Vector3(2.4f, 1.7f, 1.4f), Flat(new Color(0.25f, 0.27f, 0.3f), 0.4f, 0.6f), 8);
        Box(b, new Vector3(1.6f, 0.5f, 0.06f), new Vector3(2.4f, 3.2f, 1.4f), Flat(new Color(0.15f, 0.35f, 0.7f), 0.5f));
        Label(b, "ŁOWISKA", new Vector3(2.4f, 3.2f, 1.44f), 0.007f, 56, Colors.White);
    }

    private static void Windmill(Building b)
    {
        var white = Painted(new Color(0.9f, 0.88f, 0.82f));
        Cyl(b, 1.3f, 2.2f, 7.2f, new Vector3(0, 3.6f, 0), white, 12);
        b.AddChild(new StaticBody3D { CollisionLayer = 1, Position = new Vector3(0, 3.6f, 0) });
        var body = (StaticBody3D)b.GetChild(b.GetChildCount() - 1);
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 2.0f, Height = 7.2f } });
        Cyl(b, 0f, 1.9f, 2.0f, new Vector3(0, 8.2f, 0), Roof(new Color(0.4f, 0.22f, 0.15f)), 12);
        Box(b, new Vector3(1.1f, 2.0f, 0.2f), new Vector3(0, 1.0f, 1.95f), Wood(new Color(0.55f, 0.4f, 0.3f)));
        var hub = new Node3D { Position = new Vector3(0, 7.2f, 1.7f) };
        b.AddChild(hub);
        var sail = Flat(new Color(0.9f, 0.85f, 0.75f), 0.9f);
        var frame = Wood(new Color(0.55f, 0.4f, 0.28f));
        for (int i = 0; i < 4; i++)
        {
            var arm = new Node3D { RotationDegrees = new Vector3(0, 0, i * 90f) };
            hub.AddChild(arm);
            Box(arm, new Vector3(0.22f, 6.2f, 0.12f), new Vector3(0, 3.2f, 0), frame);
            Box(arm, new Vector3(1.1f, 4.6f, 0.05f), new Vector3(0.65f, 3.6f, 0.04f), sail);
        }
        Cyl(hub, 0.25f, 0.25f, 0.5f, Vector3.Zero, frame, 8).RotationDegrees = new Vector3(90, 0, 0);
        b.AddSpinner(hub);
    }

    private static void Haystack(Building b)
    {
        var straw = Flat(new Color(0.82f, 0.68f, 0.28f), 1f);
        Cyl(b, 1.5f, 1.65f, 1.3f, new Vector3(0, 0.65f, 0), straw, 10);
        Cyl(b, 0f, 1.55f, 1.7f, new Vector3(0, 2.1f, 0), straw, 10);
        var body = new StaticBody3D { CollisionLayer = 1, Position = new Vector3(0, 1f, 0) };
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 1.6f, Height = 2f } });
        b.AddChild(body);
    }

    private static void Lamp(Building b)
    {
        var metal = Flat(new Color(0.15f, 0.16f, 0.18f), 0.45f, 0.7f);
        Cyl(b, 0.06f, 0.1f, 3.6f, new Vector3(0, 1.8f, 0), metal, 8);
        Box(b, new Vector3(0.5f, 0.1f, 0.5f), new Vector3(0, 3.65f, 0), metal);
        Box(b, new Vector3(0.36f, 0.42f, 0.36f), new Vector3(0, 3.9f, 0), Glass(b));
        Cyl(b, 0f, 0.36f, 0.25f, new Vector3(0, 4.22f, 0), metal, 4);
    }

    private static void Bench(Building b)
    {
        var wood = Wood(new Color(0.75f, 0.55f, 0.38f));
        var iron = Flat(new Color(0.12f, 0.12f, 0.14f), 0.5f, 0.6f);
        Box(b, new Vector3(1.9f, 0.08f, 0.5f), new Vector3(0, 0.5f, 0), wood);
        Box(b, new Vector3(1.9f, 0.4f, 0.07f), new Vector3(0, 0.85f, -0.25f), wood, new Vector3(-8f, 0, 0));
        foreach (float sx in new[] { -0.8f, 0.8f })
            Box(b, new Vector3(0.07f, 0.5f, 0.5f), new Vector3(sx, 0.25f, 0), iron);
    }

    private static void Boat(Building b)
    {
        var hullMat = Painted(new Color(0.22f, 0.4f, 0.55f));
        var inner = Wood(new Color(0.75f, 0.6f, 0.45f));
        var hull = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.62f, Height = 3.6f, RadialSegments = 12, Rings = 4 },
            MaterialOverride = hullMat,
            Position = new Vector3(0, 0.38f, 0),
            Scale = new Vector3(1f, 1f, 0.55f),
            RotationDegrees = new Vector3(0, 0, 90f),
        };
        b.AddChild(hull);
        Box(b, new Vector3(2.3f, 0.06f, 0.68f), new Vector3(0, 0.62f, 0), inner);
        Box(b, new Vector3(0.12f, 0.1f, 0.7f), new Vector3(0.1f, 0.7f, 0), inner);
        var oar = Cyl(b, 0.03f, 0.03f, 2.2f, new Vector3(-0.5f, 0.85f, 0.2f), inner, 6);
        oar.RotationDegrees = new Vector3(0, 0, 70f);
        var body = new StaticBody3D { CollisionLayer = 1, Position = new Vector3(0, 0.5f, 0) };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3.4f, 0.8f, 1.2f) } });
        b.AddChild(body);
    }

    private static void Signpost(Building b)
    {
        var wood = Wood(new Color(0.6f, 0.45f, 0.32f));
        Cyl(b, 0.09f, 0.11f, 2.8f, new Vector3(0, 1.4f, 0), wood, 8);
        Box(b, new Vector3(1.7f, 0.4f, 0.07f), new Vector3(0.25f, 2.35f, 0.08f), Painted(new Color(0.85f, 0.8f, 0.65f)), new Vector3(0, 0, 4f));
        Label(b, "WIOSKA >", new Vector3(0.25f, 2.35f, 0.125f), 0.0085f, 48, new Color(0.1f, 0.08f, 0.06f));
        Box(b, new Vector3(1.7f, 0.4f, 0.07f), new Vector3(-0.25f, 1.8f, 0.08f), Painted(new Color(0.85f, 0.8f, 0.65f)), new Vector3(0, 0, -3f));
        Label(b, "< POMOST", new Vector3(-0.25f, 1.8f, 0.125f), 0.0085f, 48, new Color(0.1f, 0.08f, 0.06f));
    }

    private static void Fence(Building b, float length)
    {
        var wood = Wood(new Color(0.7f, 0.55f, 0.4f));
        int posts = (int)(length / 2f) + 1;
        for (int i = 0; i < posts; i++)
            Box(b, new Vector3(0.14f, 1.15f, 0.14f), new Vector3(i * 2f, 0.575f, 0), wood);
        foreach (float y in new[] { 0.45f, 0.9f })
            Box(b, new Vector3(length + 0.1f, 0.1f, 0.07f), new Vector3(length / 2f, y, 0.05f), wood);
        Collider(b, new Vector3(length + 0.2f, 1.1f, 0.3f), new Vector3(length / 2f, 0.55f, 0));
    }
}
