using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Data;

namespace FishingSim.World.Props;

/// <summary>
/// Builds the village from primitives: shops, houses, a barn, a windmill, lamps, benches, boats, fences and so on.
/// Every building faces +Z locally; the caller rotates and places it. Solid buildings get a collider.
/// </summary>
public static partial class BuildingFactory
{
    private const float DoorWidth = 1.1f;
    private const float DoorHeight = 2.1f;

    public static string TitleFor(BuildingKind kind) => kind switch
    {
        BuildingKind.Tackle => "Kram wędkarski",
        BuildingKind.General => "Kram wędkarski i przynęty",
        BuildingKind.Bait => "Kram z przynętami",
        BuildingKind.FishBuyer => "Skup ryb",
        BuildingKind.Market => "Targ rybny",
        BuildingKind.Office => "Gildia wędkarzy",
        BuildingKind.BusStop => "Postój wozów",
        _ => "",
    };

    public static Building Create(BuildingSpec spec, int variant)
    {
        var b = new Building { Spec = spec, Title = TitleFor(spec.Kind) };
        switch (spec.Kind)
        {
            case BuildingKind.Tackle:
                House(b, new HouseStyle(9f, 6.5f, 3.5f, 2.5f, new Color(0.38f, 0.56f, 0.72f), true, new Color(0.62f, 0.22f, 0.16f), 3, true, true, "KRAM WĘDKARSKI"));
                Barrel(b, new Vector3(-2.6f, 0, 3.9f)); Crates(b, new Vector3(2.8f, 0, 4.0f), 2); RodRack(b, new Vector3(-4.52f, 0, 0.5f));
                break;
            case BuildingKind.General:
                House(b, new HouseStyle(8.5f, 6.5f, 3.4f, 2.4f, new Color(0.55f, 0.42f, 0.28f), false, new Color(0.30f, 0.36f, 0.42f), 3, true, true, "KRAM I PRZYNĘTY"));
                Barrel(b, new Vector3(-2.5f, 0, 3.9f)); Crates(b, new Vector3(2.7f, 0, 4.0f), 3); RodRack(b, new Vector3(-4.27f, 0, 0.5f));
                break;
            case BuildingKind.Bait:
                House(b, new HouseStyle(6.5f, 5.2f, 3.1f, 2.1f, new Color(0.40f, 0.62f, 0.36f), true, new Color(0.38f, 0.27f, 0.2f), 2, false, true, "PRZYNĘTY"));
                Barrel(b, new Vector3(-2.0f, 0, 3.3f)); Barrel(b, new Vector3(-2.7f, 0, 3.0f)); Crates(b, new Vector3(2.2f, 0, 3.3f), 2);
                break;
            case BuildingKind.Office:
                House(b, new HouseStyle(8.5f, 6.2f, 3.3f, 2.2f, new Color(0.9f, 0.9f, 0.84f), true, new Color(0.2f, 0.36f, 0.62f), 3, false, true, "GILDIA WĘDKARZY"));
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
                FishStall(b, new Color(0.18f, 0.4f, 0.55f));
                GoldenFish(b, new Vector3(0f, 5.1f, 0.2f));
                break;
            case BuildingKind.Market:
                // A bigger, red-roofed stall with stock piled around it. TODO (docs/ROZWOJ.md): market hall with NPC traders.
                FishStall(b, new Color(0.62f, 0.18f, 0.16f), "TARG RYBNY");
                GoldenFish(b, new Vector3(0f, 5.1f, 0.2f));
                Crates(b, new Vector3(3.2f, 0, 3.4f), 3);
                Barrel(b, new Vector3(-3.0f, 0, 3.4f));
                Barrel(b, new Vector3(-2.3f, 0, 3.7f));
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
    /// <summary>
    /// The village's material finish: high-resolution textures read with trilinear anisotropic filtering (sharp at
    /// QHD, no shimmer at a distance), soft banded light, no shiny highlights. (The name is historical.)
    /// </summary>
    public static StandardMaterial3D Pixel(StandardMaterial3D m, bool shiny = false)
    {
        m.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        m.DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon;
        m.SpecularMode = shiny ? BaseMaterial3D.SpecularModeEnum.Toon : BaseMaterial3D.SpecularModeEnum.Disabled;
        return m;
    }

    private static StandardMaterial3D Textured(Texture2D tex, Color tint, float roughness = 0.9f) => Pixel(new StandardMaterial3D
    {
        AlbedoTexture = tex, AlbedoColor = tint, Uv1Triplanar = true, Uv1Scale = Vector3.One * Textures.TriplanarScale, Roughness = roughness,
    });

    private static StandardMaterial3D Painted(Color color) => Textured(Textures.PlanksPainted(), color, 0.85f);

    /// <summary>Lime plaster between the timbers; the house colour only tints it lightly.</summary>
    private static StandardMaterial3D Plaster(Color tint) => Textured(Textures.Plaster(), new Color(0.95f, 0.92f, 0.85f).Lerp(tint, 0.3f));

    private static StandardMaterial3D Wood(Color tint) => Textured(Textures.Planks(), tint);

    /// <summary>Dark oak beams of the timber frame.</summary>
    private static StandardMaterial3D Timber() => Textured(Textures.Planks(), new Color(0.55f, 0.42f, 0.32f));

    /// <summary>Thatch, straw-gold with a hint of the roof colour.</summary>
    private static StandardMaterial3D Roof(Color color)
    {
        var m = Textured(Textures.Shingles(), new Color(0.86f, 0.74f, 0.5f).Lerp(color, 0.2f), 1f);
        m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        return m;
    }

    private static StandardMaterial3D Stone(Color tint) => Textured(Textures.Stone(), tint, 1f);

    private static StandardMaterial3D Flat(Color color, float roughness = 0.8f, float metallic = 0f) =>
        Pixel(new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic }, metallic > 0.3f);

    private static StandardMaterial3D Glass(Building b)
    {
        var m = Pixel(new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.14f, 0.12f),     // dark small panes by day, warm candle light at night
            Roughness = 0.6f,
            EmissionEnabled = false,
            Emission = new Color(1f, 0.72f, 0.35f),
        });
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

    /// <summary>
    /// Text painted on a sign board of <paramref name="board"/> size (m): the letters are scaled to fit inside it with a
    /// margin (at most 78% of the width and 55% of the height), whatever the length of the text.
    /// </summary>
    public static float FitPixelSize(string text, Vector2 board, int fontSize = 64)
    {
        var size = ThemeDB.FallbackFont.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
        if (size.X <= 0f || size.Y <= 0f)
            return 0.01f;
        return MathF.Min(board.X * 0.78f / size.X, board.Y * 0.55f / size.Y);
    }

    private static void Label(Node3D parent, string text, Vector3 pos, Vector2 board, int fontSize = 64, Color? color = null)
    {
        parent.AddChild(new Label3D
        {
            Text = text,
            Position = pos,
            PixelSize = FitPixelSize(text, board, fontSize),
            FontSize = fontSize,
            OutlineSize = 3,
            Modulate = color ?? new Color(0.98f, 0.93f, 0.8f),
            OutlineModulate = new Color(0.05f, 0.04f, 0.03f),
            DoubleSided = false,
            AlphaCut = Label3D.AlphaCutMode.Discard,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        });
    }

    // ------------------------------------------------------------------ the generic house
    private sealed record HouseStyle(float W, float D, float H, float RoofH, Color Wall, bool PaintedWall, Color RoofColor,
        int Windows, bool Chimney, bool Awning, string? Sign);

    private static void House(Building b, HouseStyle s)
    {
        // Medieval timber-framed house: plaster panels between dark oak beams, thatched roof, stone plinth.
        var wallMat = s.PaintedWall ? Plaster(s.Wall) : Wood(s.Wall * 1.9f);
        var trim = Timber();
        var dark = Wood(new Color(0.55f, 0.42f, 0.3f));
        var glass = Glass(b);

        // foundation, walls
        Box(b, new Vector3(s.W + 0.3f, 0.5f, s.D + 0.3f), new Vector3(0, 0.25f, 0), Stone(new Color(0.8f, 0.78f, 0.74f)));
        Box(b, new Vector3(s.W, s.H, s.D), new Vector3(0, s.H / 2f + 0.15f, 0), wallMat);
        Collider(b, new Vector3(s.W + 0.3f, s.H + 0.4f, s.D + 0.3f), new Vector3(0, (s.H + 0.4f) / 2f, 0));
        // corner posts
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                Box(b, new Vector3(0.26f, s.H, 0.26f), new Vector3(sx * s.W / 2f, s.H / 2f + 0.15f, sz * s.D / 2f), trim);
        if (s.PaintedWall)
            TimberFrame(b, s, trim);

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

        if (s.Windows >= 1)
            foreach (float x in frontX)
                Shutters(b, dark, new Vector3(x, wy, s.D / 2f + 0.06f));

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
            var board = new Vector2(Math.Min(s.Sign.Length * 0.2f + 0.8f, s.W * 0.7f), 0.7f);
            Box(b, new Vector3(board.X, board.Y, 0.12f), new Vector3(0, signY, front + 0.12f), Flat(new Color(0.16f, 0.12f, 0.09f), 0.9f));
            Label(b, s.Sign, new Vector3(0, signY, front + 0.2f), board);
        }
        var lamp = new OmniLight3D
        {
            LightColor = new Color(1f, 0.78f, 0.45f), OmniRange = 9f, LightEnergy = 0f, ShadowEnabled = false,
            Position = new Vector3(0, 2.8f, front + 1.2f),
        };
        b.AddChild(lamp);
        b.AddLight(lamp);
    }

    /// <summary>Sill, rail and plate beams on all four walls, posts beside the door, and diagonal braces on the sides.</summary>
    private static void TimberFrame(Building b, HouseStyle s, StandardMaterial3D timber)
    {
        const float t = 0.2f;
        float hw = s.W / 2f + 0.03f, hd = s.D / 2f + 0.03f;
        foreach (float y in new[] { 0.6f, 2.8f, s.H + 0.05f })
        {
            if (y > s.H + 0.1f)
                continue;
            Box(b, new Vector3(s.W, t, t), new Vector3(0, y, hd), timber);
            Box(b, new Vector3(s.W, t, t), new Vector3(0, y, -hd), timber);
            Box(b, new Vector3(t, t, s.D), new Vector3(hw, y, 0), timber);
            Box(b, new Vector3(t, t, s.D), new Vector3(-hw, y, 0), timber);
        }
        // Posts flanking the door and in the middle of the back wall.
        foreach (float x in new[] { -(DoorWidth / 2f + 0.3f), DoorWidth / 2f + 0.3f })
            Box(b, new Vector3(t, s.H, t), new Vector3(x, s.H / 2f + 0.15f, hd), timber);
        Box(b, new Vector3(t, s.H, t), new Vector3(0, s.H / 2f + 0.15f, -hd), timber);
        // Diagonal braces at the ends of the side walls (clear of the side window in the middle).
        float run = Math.Min(1.5f, s.D / 2f - 0.9f);
        float rise = 2.2f;
        float len = MathF.Sqrt(run * run + rise * rise), ang = MathF.Atan2(run, rise);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                var pos = new Vector3(sx * hw, 0.6f + rise / 2f, sz * (hd - run / 2f - 0.1f));
                Box(b, new Vector3(t, len, t), pos, timber, new Vector3(-Mathf.RadToDeg(ang) * sz, 0, 0));
            }
    }

    /// <summary>A pair of plank shutters, opened flat against the wall either side of a window.</summary>
    private static void Shutters(Building b, StandardMaterial3D wood, Vector3 pos)
    {
        foreach (float sx in new[] { -1f, 1f })
            Box(b, new Vector3(0.55f, 1.15f, 0.06f), pos + new Vector3(sx * 0.95f, 0f, 0f), wood);
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

    /// <summary>The fishmonger's emblem: a big golden carp mounted on the roof ridge, mouth open to the plaza.</summary>
    private static void GoldenFish(Building b, Vector3 at)
    {
        var carp = GD.Load<FishSpecies>("res://data/fish/karp.tres");
        if (carp == null)
            return;
        var fish = Fishing.FishModel.Create(carp, 9f);
        float scale = 2.8f / fish.Length;
        fish.Wiggle = 0f;
        fish.Scale = new Vector3(scale, scale, scale);
        fish.Position = at;
        fish.RotationDegrees = new Vector3(0f, 90f, 0f);
        b.AddChild(fish);
        Box(b, new Vector3(0.12f, 0.9f, 0.12f), at + new Vector3(0, -0.6f, 0), Timber());
    }

    private static void FishStall(Building b, Color awningColor, string sign = "SKUP RYB")
    {
        var wood = Wood(new Color(0.65f, 0.5f, 0.36f));
        var awning = Roof(awningColor);
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
        Label(b, sign, new Vector3(0, 3.55f, d / 2f + 0.38f), new Vector2(3.6f, 0.75f));
        var lamp = new OmniLight3D { LightColor = new Color(1f, 0.78f, 0.45f), OmniRange = 9f, LightEnergy = 0f, Position = new Vector3(0, 2.8f, 2.2f) };
        b.AddChild(lamp);
        b.AddLight(lamp);
    }

    /// <summary>The coaching stop: a covered wagon waiting by a signpost (travel to another fishing ground).</summary>
    private static void BusStop(Building b)
    {
        var wood = Wood(new Color(0.62f, 0.46f, 0.3f));
        var dark = Timber();
        var iron = Flat(new Color(0.14f, 0.13f, 0.12f), 0.6f, 0.5f);
        var canvas = Textured(Textures.Plaster(), new Color(0.88f, 0.84f, 0.72f));
        canvas.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

        // Wagon bed with plank sides, parked along X behind the sign.
        var wagon = new Node3D { Position = new Vector3(-0.4f, 0f, -1.2f) };
        b.AddChild(wagon);
        Box(wagon, new Vector3(2.8f, 0.14f, 1.4f), new Vector3(0, 0.95f, 0), wood);
        foreach (float sz in new[] { -0.68f, 0.68f })
            Box(wagon, new Vector3(2.8f, 0.45f, 0.08f), new Vector3(0, 1.24f, sz), wood);
        foreach (float sx in new[] { -1.38f, 1.38f })
            Box(wagon, new Vector3(0.08f, 0.45f, 1.4f), new Vector3(sx, 1.24f, 0), wood);
        // Canvas cover on hoops.
        for (int i = 0; i < 3; i++)
        {
            float x = -0.95f + i * 0.95f;
            var hoop = Cyl(wagon, 0.72f, 0.72f, 0.06f, new Vector3(x, 1.5f, 0), dark, 12);
            hoop.RotationDegrees = new Vector3(0, 0, 90f);
        }
        var cover = Cyl(wagon, 0.74f, 0.74f, 2.5f, new Vector3(0, 1.5f, 0), canvas, 12);
        cover.RotationDegrees = new Vector3(0, 0, 90f);
        cover.Scale = new Vector3(1f, 1f, 0.95f);
        // Wheels with iron tyres, axles and the shaft for the horse.
        foreach (float sx in new[] { -0.95f, 0.95f })
            foreach (float sz in new[] { -0.8f, 0.8f })
            {
                var wheel = Cyl(wagon, 0.48f, 0.48f, 0.1f, new Vector3(sx, 0.48f, sz), dark, 10);
                wheel.RotationDegrees = new Vector3(90f, 0, 0);
                var tyre = Cyl(wagon, 0.5f, 0.5f, 0.06f, new Vector3(sx, 0.48f, sz * 1.02f), iron, 10);
                tyre.RotationDegrees = new Vector3(90f, 0, 0);
            }
        foreach (float sz in new[] { -0.3f, 0.3f })
            Box(wagon, new Vector3(1.8f, 0.08f, 0.08f), new Vector3(2.2f, 0.7f, sz), dark, new Vector3(0, 0, -12f));
        Collider(wagon, new Vector3(3.0f, 1.9f, 1.6f), new Vector3(0, 1.0f, 0));

        // Signpost with the destinations.
        Box(b, new Vector3(0.18f, 3f, 0.18f), new Vector3(2.4f, 1.5f, 1.2f), dark);
        Box(b, new Vector3(1.7f, 0.45f, 0.08f), new Vector3(2.4f, 2.7f, 1.26f), wood);
        Label(b, "ŁOWISKA", new Vector3(2.4f, 2.7f, 1.32f), new Vector2(1.7f, 0.45f), 64, new Color(0.15f, 0.1f, 0.05f));
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

    /// <summary>A wooden post with an iron arm and a hanging lantern (lit at night).</summary>
    private static void Lamp(Building b)
    {
        var wood = Timber();
        var iron = Flat(new Color(0.12f, 0.11f, 0.1f), 0.6f, 0.5f);
        Box(b, new Vector3(0.22f, 3.4f, 0.22f), new Vector3(0, 1.7f, 0), wood);
        Box(b, new Vector3(0.9f, 0.1f, 0.1f), new Vector3(0.38f, 3.3f, 0), iron);
        Box(b, new Vector3(0.05f, 0.25f, 0.05f), new Vector3(0.75f, 3.15f, 0), iron);
        Box(b, new Vector3(0.34f, 0.42f, 0.34f), new Vector3(0.75f, 2.82f, 0), Glass(b));
        Box(b, new Vector3(0.42f, 0.08f, 0.42f), new Vector3(0.75f, 3.06f, 0), iron);
        Box(b, new Vector3(0.38f, 0.06f, 0.38f), new Vector3(0.75f, 2.58f, 0), iron);
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
        Label(b, "WIOSKA >", new Vector3(0.25f, 2.35f, 0.125f), new Vector2(1.7f, 0.4f), 64, new Color(0.1f, 0.08f, 0.06f));
        Box(b, new Vector3(1.7f, 0.4f, 0.07f), new Vector3(-0.25f, 1.8f, 0.08f), Painted(new Color(0.85f, 0.8f, 0.65f)), new Vector3(0, 0, -3f));
        Label(b, "< POMOST", new Vector3(-0.25f, 1.8f, 0.125f), new Vector2(1.7f, 0.4f), 64, new Color(0.1f, 0.08f, 0.06f));
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
