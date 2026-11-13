using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Data;

namespace FishingSim.World.Props;

/// <summary>What a house's walls are made of.</summary>
public enum WallKind { Planks, Timber, Boards, Logs, Stone }

/// <summary>What covers a roof.</summary>
public enum RoofKind { Thatch, Shingles, Tiles, Turf }

/// <summary>
/// The village pieces that make each map's town its own: log cabins under turf, painted board houses under clay
/// tiles, stone cottages, a smokehouse, camping tents, market pavilions and stalls with their traders, a well, wood
/// piles, fish-drying racks, carts, villagers, and a shopkeeper at every shop. <see cref="Style"/> (the map's
/// LocationData.VillageStyle) decides what the ordinary houses and the shops are built of.
/// </summary>
public static partial class BuildingFactory
{
    /// <summary>"timber", "farm", "fishing" or "mountain": set by the scenery before the village is built.</summary>
    public static string Style { get; set; } = "timber";

    private static WallKind StyleWalls => Style switch
    {
        "farm" or "fishing" => WallKind.Boards,
        "mountain" => WallKind.Logs,
        _ => WallKind.Timber,
    };

    private static RoofKind StyleRoof => Style switch
    {
        "farm" => RoofKind.Tiles,
        "fishing" => RoofKind.Shingles,
        "mountain" => RoofKind.Turf,
        _ => RoofKind.Thatch,
    };

    /// <summary>The paint a house of this colour gets in the village's style (falu red farms, sea-weathered fishing huts).</summary>
    private static Color PaintFor(Color wall) => Style switch
    {
        "farm" => wall.Lerp(new Color(0.58f, 0.14f, 0.1f), 0.65f),
        "fishing" => wall.Lerp(new Color(0.55f, 0.6f, 0.62f), 0.55f),
        _ => wall,
    };

    private static Color LogTint(Color wall) => new Color(1f, 1f, 1f).Lerp(wall, 0.2f);

    /// <summary>Painted shutters in the village's colour: bottle green, white on the farm, sea blue, natural wood up the mountain.</summary>
    private static Color ShutterColour => Style switch
    {
        "farm" => new Color(0.92f, 0.9f, 0.85f),
        "fishing" => new Color(0.32f, 0.48f, 0.62f),
        "mountain" => new Color(0.62f, 0.46f, 0.3f),
        _ => new Color(0.24f, 0.42f, 0.3f),
    };

    /// <summary>
    /// Walls of real logs: rows along X in front and behind, rows along Z on the sides half a log higher, their ends
    /// crossing beyond the corners as in a notched cabin. One mesh for the whole house.
    /// </summary>
    private static void LogWalls(Node3D b, float w, float d, float h, Material mat)
    {
        const float radius = 0.16f, step = 0.27f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int rows = (int)MathF.Ceiling(h / step);
        int seed = 0;
        for (int r = 0; r < rows; r++)
        {
            float y = 0.3f + r * step;
            foreach (float sz in new[] { -1f, 1f })
                Shapes.Log(st, new Vector3(0f, y, sz * d / 2f), Vector3.Right, w + 0.7f, radius, seed++);
            if (y + step * 0.5f < h + 0.2f)
                foreach (float sx in new[] { -1f, 1f })
                    Shapes.Log(st, new Vector3(sx * w / 2f, y + step * 0.5f, 0f), Vector3.Back, d + 0.7f, radius, seed++);
        }
        st.GenerateTangents();
        b.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = mat });
        // The inside, seen through the gaps between the logs.
        Box(b, new Vector3(w - 0.1f, h, d - 0.1f), new Vector3(0f, h / 2f + 0.15f, 0f), Flat(new Color(0.2f, 0.15f, 0.1f)));
    }

    /// <summary>Boards along the gable edges of a roof (they hold the turf in place).</summary>
    private static void Bargeboards(Node3D b, float w, float d, float baseY, float roofH, float overhang, Material mat)
    {
        float hw = w / 2f + overhang + 0.04f, hd = d / 2f + overhang;
        float len = MathF.Sqrt(hd * hd + roofH * roofH), ang = Mathf.RadToDeg(MathF.Atan2(roofH, hd));
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                Box(b, new Vector3(0.1f, 0.32f, len), new Vector3(sx * hw, baseY + roofH * 0.5f + 0.08f, sz * hd * 0.5f), mat, new Vector3(sz * ang, 0f, 0f));
    }

    /// <summary>Builds the new kinds of village piece; false for the others (handled by <see cref="Create"/>).</summary>
    private static bool CreateVillagePiece(Building b, BuildingSpec spec, int variant)
    {
        switch (spec.Kind)
        {
            case BuildingKind.Cabin:
                House(b, new HouseStyle(7f, 5.5f, 2.7f, 2f, new Color(0.7f, 0.55f, 0.4f), false, new Color(0.3f, 0.3f, 0.3f), 2, true, false, null,
                    WallKind.Logs, variant % 3 == 1 ? RoofKind.Shingles : RoofKind.Turf));
                Woodpile(b, new Vector3(-4.6f, 0f, -0.6f), 90f);
                break;
            case BuildingKind.RedHouse:
                Color[] paints = { new(0.58f, 0.14f, 0.1f), new(0.82f, 0.6f, 0.25f), new(0.9f, 0.88f, 0.82f), new(0.42f, 0.52f, 0.58f) };
                House(b, new HouseStyle(8f, 6.5f, 3.4f, 2.6f, paints[variant % paints.Length], false, new Color(0.3f, 0.2f, 0.18f), 3, true, false, null,
                    WallKind.Boards, variant % 2 == 0 ? RoofKind.Tiles : RoofKind.Shingles));
                Porch(b, 6.5f / 2f, Painted(new Color(0.92f, 0.9f, 0.85f)));
                break;
            case BuildingKind.StoneHouse:
                House(b, new HouseStyle(7.5f, 6f, 3f, 2.4f, new Color(0.8f, 0.78f, 0.74f), false, new Color(0.35f, 0.3f, 0.25f), 2, true, false, null,
                    WallKind.Stone, variant % 2 == 0 ? RoofKind.Shingles : RoofKind.Thatch));
                break;
            case BuildingKind.Smokehouse:
                House(b, new HouseStyle(3.6f, 3f, 2.1f, 1.7f, new Color(0.6f, 0.5f, 0.4f), false, new Color(0.25f, 0.22f, 0.2f), 1, true, false, null,
                    WallKind.Logs, RoofKind.Shingles));
                DryingRack(b, new Vector3(3.4f, 0f, 0.6f), 90f);
                // Alder smoke from the chimney, and the smoker by the door (village pieces return before the keepers).
                var smoke = Props.Campfire.ChimneySmoke();
                smoke.Position = new Vector3(3.6f * 0.28f, 2.1f + 1.7f + 1.65f, -3f * 0.18f);
                b.AddChild(smoke);
                AddKeeper(b, spec.Kind, variant);
                break;
            case BuildingKind.Tent:
                Tent(b, variant);
                break;
            case BuildingKind.MarketTent:
                MarketTent(b, variant);
                break;
            case BuildingKind.Stall:
                Stall(b, variant);
                break;
            case BuildingKind.Well:
                Well(b);
                break;
            case BuildingKind.Woodpile:
                Woodpile(b, Vector3.Zero, 0f);
                break;
            case BuildingKind.DryingRack:
                DryingRack(b, Vector3.Zero, 0f);
                break;
            case BuildingKind.Cart:
                Cart(b, variant);
                break;
            case BuildingKind.Villager:
                b.AddChild(People.Create(VillagerRole(variant), variant));
                break;
            default:
                return false;
        }
        return true;
    }

    private static Role VillagerRole(int variant)
    {
        Role[] roles = Style switch
        {
            "mountain" => new[] { Role.Hunter, Role.Woman, Role.Elder, Role.Fisherman },
            "farm" => new[] { Role.Farmer, Role.Woman, Role.Elder, Role.Farmer },
            "fishing" => new[] { Role.Fisherman, Role.Woman, Role.Fisherman, Role.Elder },
            _ => new[] { Role.Fisherman, Role.Woman, Role.Elder, Role.Farmer },
        };
        return roles[variant % roles.Length];
    }

    /// <summary>The shopkeeper of a functional building: beside the door, or behind the counter of a stall.</summary>
    private static void AddKeeper(Building b, BuildingKind kind, int variant)
    {
        var (pos, yaw) = kind switch
        {
            BuildingKind.Tackle or BuildingKind.General => (new Vector3(1.5f, 0f, 4.4f), 0f),
            BuildingKind.Bait => (new Vector3(1.05f, 0f, 3.75f), 0f),
            BuildingKind.Office => (new Vector3(-1.6f, 0f, 4.2f), 0f),
            BuildingKind.FishBuyer or BuildingKind.Market => (new Vector3(-0.9f, 0f, -0.8f), 0f),
            BuildingKind.BusStop => (new Vector3(-1.6f, 0f, 0.7f), 15f),
            // The smoker stands by the drying rack beside the door.
            BuildingKind.Smokehouse => (new Vector3(2.1f, 0f, 2.5f), -20f),
            _ => (new Vector3(1.5f, 0f, 4.4f), 0f),
        };
        var keeper = People.Create(People.KeeperOf(kind), variant);
        keeper.Position = pos;
        keeper.RotationDegrees = new Vector3(0f, yaw, 0f);
        b.AddChild(keeper);
    }

    // ------------------------------------------------------------------ houses' extras
    /// <summary>A small porch roof on two posts over the front door, with steps.</summary>
    private static void Porch(Building b, float front, Material trim)
    {
        Box(b, new Vector3(2.6f, 0.12f, 1.6f), new Vector3(0f, 3.05f, front + 0.75f), trim, new Vector3(12f, 0f, 0f));
        foreach (float sx in new[] { -1.15f, 1.15f })
            Box(b, new Vector3(0.14f, 2.9f, 0.14f), new Vector3(sx, 1.6f, front + 1.4f), trim);
        Box(b, new Vector3(2.4f, 0.18f, 0.5f), new Vector3(0f, 0.32f, front + 0.4f), Wood(new Color(0.7f, 0.6f, 0.5f)));
    }

    // ------------------------------------------------------------------ camping and the market
    private static readonly Color[] TentColours =
    {
        new(0.42f, 0.46f, 0.28f), new(0.82f, 0.46f, 0.18f), new(0.24f, 0.4f, 0.6f), new(0.64f, 0.2f, 0.16f), new(0.78f, 0.74f, 0.6f),
    };

    /// <summary>An anglers' ridge tent: two canvas slopes on poles, guy ropes, the flaps open, a campfire and a stool.</summary>
    private static void Tent(Building b, int variant)
    {
        var cloth = Cloth(TentColours[variant % TentColours.Length]);
        var pole = Wood(new Color(0.6f, 0.45f, 0.3f));
        const float l = 2.6f, w = 2.3f, h = 1.55f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Tri(Vector3 a, Vector3 c, Vector3 d)
        {
            st.AddVertex(a);
            st.AddVertex(c);
            st.AddVertex(d);
        }
        foreach (float sx in new[] { -1f, 1f })
        {
            var r0 = new Vector3(0f, h, -l / 2f);
            var r1 = new Vector3(0f, h, l / 2f);
            var g0 = new Vector3(sx * w / 2f, 0.02f, -l / 2f);
            var g1 = new Vector3(sx * w / 2f, 0.02f, l / 2f);
            Tri(r0, g0, g1);
            Tri(r0, g1, r1);
        }
        // Closed back, the front flaps tied back.
        Tri(new Vector3(0f, h, -l / 2f), new Vector3(-w / 2f, 0.02f, -l / 2f), new Vector3(w / 2f, 0.02f, -l / 2f));
        foreach (float sx in new[] { -1f, 1f })
            Tri(new Vector3(0f, h, l / 2f), new Vector3(sx * w / 2f, 0.02f, l / 2f), new Vector3(sx * w * 0.72f, 0.4f, l / 2f + 0.5f));
        st.GenerateNormals();
        b.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = cloth });
        // The dark inside seen through the door.
        Box(b, new Vector3(0.05f, 1.3f, 0.05f), new Vector3(0f, 0.65f, l / 2f), pole);
        Box(b, new Vector3(0.05f, 1.3f, 0.05f), new Vector3(0f, 0.65f, -l / 2f), pole);
        Cyl(b, 0.025f, 0.025f, l + 0.2f, new Vector3(0f, h, 0f), pole, 6).RotationDegrees = new Vector3(90f, 0f, 0f);
        var rope = Flat(new Color(0.8f, 0.76f, 0.62f), 0.9f);
        foreach (float sz in new[] { -1f, 1f })
        {
            var from = new Vector3(0f, h, sz * l / 2f);
            var to = new Vector3(0f, 0.05f, sz * (l / 2f + 1.3f));
            var mid = (from + to) / 2f;
            var r = Cyl(b, 0.01f, 0.01f, from.DistanceTo(to), mid, rope, 4);
            r.RotationDegrees = new Vector3(sz * Mathf.RadToDeg(MathF.Atan2(1.3f, h)), 0f, 0f);
            Box(b, new Vector3(0.05f, 0.25f, 0.05f), to + new Vector3(0f, 0.08f, 0f), pole);
        }
        Collider(b, new Vector3(w, h, l), new Vector3(0f, h / 2f, 0f));
        Campfire(b, new Vector3(0.4f, 0f, l / 2f + 2.2f));
        Cyl(b, 0.2f, 0.17f, 0.42f, new Vector3(-1.2f, 0.21f, l / 2f + 1.9f), Wood(new Color(0.65f, 0.5f, 0.35f)), 10);
    }

    /// <summary>A ring of stones with crossed logs and embers that glow (and light the camp) after dark.</summary>
    private static void Campfire(Building b, Vector3 at)
    {
        var stone = Stone(new Color(0.75f, 0.74f, 0.72f));
        var pit = PropAsset.Create("stone_fire_pit", 0f, 0.8f);
        if (pit != null)
        {
            pit.Position = at;
            b.AddChild(pit);
        }
        for (int i = 0; i < (pit != null ? 0 : 9); i++)
        {
            float a = i * Mathf.Tau / 9f;
            var s = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.14f, Height = 0.2f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = stone,
                Position = at + new Vector3(MathF.Cos(a) * 0.5f, 0.06f, MathF.Sin(a) * 0.5f),
                Scale = new Vector3(1.1f, 0.8f, 0.9f),
            };
            b.AddChild(s);
        }
        var charred = Wood(new Color(0.3f, 0.22f, 0.16f));
        for (int i = 0; i < 3; i++)
            Cyl(b, 0.05f, 0.06f, 0.75f, at + new Vector3(0f, 0.12f, 0f), charred, 6).RotationDegrees = new Vector3(80f, i * 60f, 0f);
        var embers = Pixel(new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.1f, 0.06f), EmissionEnabled = false, Emission = new Color(1f, 0.4f, 0.1f), Roughness = 0.9f,
        });
        b.AddGlow(embers);
        Cyl(b, 0.28f, 0.3f, 0.05f, at + new Vector3(0f, 0.03f, 0f), embers, 10);
        var light = new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.25f), OmniRange = 7f, LightEnergy = 0f, Position = at + new Vector3(0f, 0.5f, 0f) };
        b.AddChild(light);
        b.AddLight(light);
    }

    private static readonly (Color Stripe, Color Cloth)[] MarketColours =
    {
        (new(0.7f, 0.16f, 0.14f), new(0.92f, 0.9f, 0.84f)), (new(0.2f, 0.36f, 0.62f), new(0.92f, 0.9f, 0.84f)),
        (new(0.24f, 0.46f, 0.26f), new(0.88f, 0.84f, 0.66f)), (new(0.82f, 0.56f, 0.14f), new(0.5f, 0.2f, 0.16f)),
    };

    /// <summary>
    /// A market pavilion: four poles under a striped canvas pyramid with a valance, a table of goods (fish, bread,
    /// apples, pots, by variant) and the trader behind it; a lantern under the peak glows at night.
    /// </summary>
    private static void MarketTent(Building b, int variant)
    {
        var (stripe, plain) = MarketColours[variant % MarketColours.Length];
        var a = Cloth(stripe);
        var c = Cloth(plain);
        var pole = Wood(new Color(0.6f, 0.46f, 0.32f));
        const float half = 1.6f, eave = 2.3f, apex = 3.5f;
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                Cyl(b, 0.05f, 0.06f, eave, new Vector3(sx * half, eave / 2f, sz * half), pole, 8);
        // Roof: each of the four faces split into stripes of the two colours.
        var top = new Vector3(0f, apex, 0f);
        Vector3[] corners = { new(-half, eave, half), new(half, eave, half), new(half, eave, -half), new(-half, eave, -half) };
        var sa = new SurfaceTool();
        var sb = new SurfaceTool();
        sa.Begin(Mesh.PrimitiveType.Triangles);
        sb.Begin(Mesh.PrimitiveType.Triangles);
        const int stripes = 6;
        for (int f = 0; f < 4; f++)
        {
            var p0 = corners[f] * new Vector3(1.12f, 1f, 1.12f) + new Vector3(0f, -0.1f, 0f);
            var p1 = corners[(f + 1) % 4] * new Vector3(1.12f, 1f, 1.12f) + new Vector3(0f, -0.1f, 0f);
            for (int i = 0; i < stripes; i++)
            {
                var st = i % 2 == 0 ? sa : sb;
                st.AddVertex(top);
                st.AddVertex(p0.Lerp(p1, i / (float)stripes));
                st.AddVertex(p0.Lerp(p1, (i + 1f) / stripes));
            }
        }
        sa.GenerateNormals();
        sb.GenerateNormals();
        b.AddChild(new MeshInstance3D { Mesh = sa.Commit(), MaterialOverride = a });
        b.AddChild(new MeshInstance3D { Mesh = sb.Commit(), MaterialOverride = c });
        // Valance: alternating flaps all round the eave.
        for (int f = 0; f < 4; f++)
            for (int i = 0; i < 4; i++)
            {
                float t = (i + 0.5f) / 4f;
                var p = corners[f].Lerp(corners[(f + 1) % 4], t) * 1.12f;
                var side = (corners[(f + 1) % 4] - corners[f]).Normalized();
                var flap = Box(b, new Vector3(half * 2f * 1.12f / 4f, 0.32f, 0.02f), new Vector3(p.X, eave - 0.24f, p.Z), i % 2 == 0 ? a : c);
                flap.Rotation = new Vector3(0f, MathF.Atan2(side.X, side.Z) - MathF.PI / 2f, 0f);
            }
        Goods(b, variant, new Vector3(0f, 0f, 0.35f), 2.6f);
        var trader = People.Create(Role.MarketTrader, variant + 5);
        trader.Position = new Vector3(0.2f, 0f, -0.75f);
        b.AddChild(trader);
        Lantern(b, new Vector3(0f, eave + 0.1f, 0f));
    }

    /// <summary>A wooden market stall: a planked counter with goods, a striped awning on two posts, the trader behind.</summary>
    private static void Stall(Building b, int variant)
    {
        var (stripe, plain) = MarketColours[(variant + 1) % MarketColours.Length];
        var wood = Wood(new Color(0.72f, 0.56f, 0.4f));
        foreach (float sx in new[] { -1.3f, 1.3f })
        {
            Box(b, new Vector3(0.12f, 2.6f, 0.12f), new Vector3(sx, 1.3f, -0.55f), wood);
            Box(b, new Vector3(0.1f, 1.95f, 0.1f), new Vector3(sx, 0.97f, 0.65f), wood);
        }
        // Awning: stripes of canvas sloping to the front.
        for (int i = 0; i < 6; i++)
            Box(b, new Vector3(2.9f / 6f, 0.04f, 1.75f), new Vector3(-1.45f + (i + 0.5f) * 2.9f / 6f, 2.25f, 0.15f), Cloth(i % 2 == 0 ? stripe : plain),
                new Vector3(18f, 0f, 0f));
        Goods(b, variant + 2, new Vector3(0f, 0f, 0.3f), 2.4f);
        var trader = People.Create(Role.MarketTrader, variant + 11);
        trader.Position = new Vector3(-0.3f, 0f, -0.75f);
        b.AddChild(trader);
    }

    /// <summary>A counter or table with the day's goods on it (by variant: fish, bread and apples, pots, cheese and jars).</summary>
    private static void Goods(Building b, int variant, Vector3 at, float width)
    {
        var top = Wood(new Color(0.78f, 0.62f, 0.45f));
        Box(b, new Vector3(width, 0.9f, 0.85f), at + new Vector3(0f, 0.45f, 0f), Wood(new Color(0.62f, 0.48f, 0.34f)));
        Box(b, new Vector3(width + 0.1f, 0.06f, 0.95f), at + new Vector3(0f, 0.92f, 0f), top);
        Collider(b, new Vector3(width, 1f, 0.9f), at + new Vector3(0f, 0.5f, 0f));
        var basket = Textured(Textures.Shingles(), new Color(0.8f, 0.66f, 0.42f));
        int kind = variant % 4;
        for (int i = 0; i < 4; i++)
        {
            float x = -width / 2f + 0.4f + i * (width - 0.8f) / 3f;
            var p = at + new Vector3(x, 0.95f, (i % 2) * 0.12f - 0.05f);
            if (PropAsset.Create("wicker_basket_01", 0f, 1.25f) is { } wicker)
            {
                wicker.Position = p;
                wicker.RotationDegrees = new Vector3(0f, i * 47f, 0f);
                b.AddChild(wicker);
            }
            else
                Cyl(b, 0.24f, 0.2f, 0.18f, p + new Vector3(0f, 0.09f, 0f), basket, 12);
            Color fill = kind switch
            {
                0 => new Color(0.62f, 0.68f, 0.72f),                          // fish
                1 => i % 2 == 0 ? new Color(0.7f, 0.12f, 0.1f) : new Color(0.62f, 0.42f, 0.2f),   // apples, bread
                2 => new Color(0.55f, 0.32f, 0.2f),                            // clay pots
                _ => i % 2 == 0 ? new Color(0.92f, 0.8f, 0.4f) : new Color(0.45f, 0.3f, 0.5f),    // cheese, plums
            };
            var mat = Flat(fill, kind == 0 ? 0.3f : 0.6f);
            for (int k = 0; k < 5; k++)
            {
                float ang = k * 1.256f;
                b.AddChild(new MeshInstance3D
                {
                    Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f, RadialSegments = 8, Rings = 4 },
                    MaterialOverride = mat,
                    Position = p + new Vector3(MathF.Cos(ang) * 0.1f, 0.2f + (k == 4 ? 0.06f : 0f), MathF.Sin(ang) * 0.1f) * (k == 4 ? 0f : 1f)
                        + new Vector3(0f, k == 4 ? 0.26f : 0f, 0f),
                    Scale = kind == 0 ? new Vector3(0.6f, 0.5f, 1.8f) : Vector3.One,
                });
            }
        }
    }

    private static void Lantern(Building b, Vector3 at)
    {
        var iron = Flat(new Color(0.14f, 0.13f, 0.12f), 0.5f, 0.6f);
        var glow = Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.8f, 0.55f), EmissionEnabled = false, Emission = new Color(1f, 0.75f, 0.4f) });
        b.AddGlow(glow);
        if (PropAsset.Create("wooden_lantern_01", 0.4f) is { } lantern)
        {
            lantern.Position = at - new Vector3(0f, 0.3f, 0f);
            b.AddChild(lantern);
            Box(b, new Vector3(0.08f, 0.12f, 0.08f), at - new Vector3(0f, 0.15f, 0f), glow);      // the flame inside
        }
        else
        {
            Box(b, new Vector3(0.18f, 0.24f, 0.18f), at, glow);
            Box(b, new Vector3(0.22f, 0.04f, 0.22f), at + new Vector3(0f, 0.14f, 0f), iron);
        }
        var light = new OmniLight3D { LightColor = new Color(1f, 0.78f, 0.45f), OmniRange = 6f, LightEnergy = 0f, Position = at };
        b.AddChild(light);
        b.AddLight(light);
    }

    // ------------------------------------------------------------------ yard pieces
    /// <summary>The village well: a fieldstone ring, a little shingled roof on two posts, the windlass and a bucket.</summary>
    private static void Well(Building b)
    {
        var stone = Stone(new Color(0.82f, 0.8f, 0.76f));
        var wood = Wood(new Color(0.6f, 0.46f, 0.32f));
        Cyl(b, 0.85f, 0.9f, 0.9f, new Vector3(0f, 0.45f, 0f), stone, 18);
        Cyl(b, 0.7f, 0.7f, 0.02f, new Vector3(0f, 0.86f, 0f), Flat(new Color(0.04f, 0.06f, 0.07f), 0.1f), 18);
        foreach (float sx in new[] { -0.8f, 0.8f })
            Box(b, new Vector3(0.14f, 2.2f, 0.14f), new Vector3(sx, 1.1f, 0f), wood);
        AddRoof(b, 2f, 1.5f, 2.1f, 0.7f, 0.15f, ShingleRoof(new Color(0.85f, 0.8f, 0.74f)));
        Cyl(b, 0.07f, 0.07f, 1.7f, new Vector3(0f, 1.65f, 0f), wood, 8).RotationDegrees = new Vector3(0f, 0f, 90f);
        Box(b, new Vector3(0.05f, 0.35f, 0.05f), new Vector3(0.9f, 1.5f, 0.1f), wood);
        Cyl(b, 0.006f, 0.006f, 0.55f, new Vector3(0f, 1.36f, 0f), Flat(new Color(0.7f, 0.66f, 0.52f)), 4);
        if (PropAsset.Create("wooden_bucket_01", 0.32f) is { } bucket)
        {
            bucket.Position = new Vector3(0f, 0.9f, 0f);
            b.AddChild(bucket);
        }
        else
            Cyl(b, 0.13f, 0.11f, 0.22f, new Vector3(0f, 1.0f, 0f), wood, 10);
        Collider(b, new Vector3(1.8f, 1f, 1.8f), new Vector3(0f, 0.5f, 0f));
    }

    /// <summary>A stack of split logs between two stakes under a board roof, and a chopping block with its axe.</summary>
    private static void Woodpile(Node3D b, Vector3 at, float yawDeg)
    {
        var holder = new Node3D { Position = at, RotationDegrees = new Vector3(0f, yawDeg, 0f) };
        b.AddChild(holder);
        var bark = Wood(new Color(0.62f, 0.48f, 0.34f));
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.11f, Height = 0.9f, RadialSegments = 7, Rings = 1 } };
        var rng = new RandomNumberGenerator { Seed = 5 };
        const int rows = 4, perRow = 9;
        mm.InstanceCount = rows * perRow;
        for (int r = 0; r < rows; r++)
            for (int i = 0; i < perRow; i++)
            {
                var basis = new Basis(Vector3.Right, MathF.PI / 2f).Rotated(Vector3.Up, rng.RandfRange(-0.05f, 0.05f));
                mm.SetInstanceTransform(r * perRow + i, new Transform3D(basis, new Vector3(-1f + i * 0.23f + (r % 2) * 0.1f, 0.12f + r * 0.2f, rng.RandfRange(-0.05f, 0.05f))));
            }
        holder.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = bark });
        foreach (float sx in new[] { -1.2f, 1.1f })
            Box(holder, new Vector3(0.1f, 1.2f, 0.1f), new Vector3(sx, 0.6f, 0f), bark);
        Box(holder, new Vector3(2.7f, 0.06f, 1.2f), new Vector3(0f, 1.05f, 0f), Wood(new Color(0.5f, 0.42f, 0.34f)), new Vector3(8f, 0f, 0f));
        Collider(holder, new Vector3(2.4f, 1f, 1f), new Vector3(0f, 0.5f, 0f));
        if (PropAsset.Create("tree_stump_01", 0f, 0.42f) is { } stump)
        {
            stump.Position = new Vector3(1.9f, 0f, 0.8f);
            holder.AddChild(stump);
        }
        else
            Cyl(holder, 0.28f, 0.3f, 0.5f, new Vector3(1.9f, 0.25f, 0.8f), bark, 12);
        Box(holder, new Vector3(0.04f, 0.6f, 0.04f), new Vector3(1.9f, 0.72f, 0.8f), Wood(new Color(0.7f, 0.56f, 0.38f)), new Vector3(0f, 0f, 25f));
        Box(holder, new Vector3(0.06f, 0.14f, 0.18f), new Vector3(2.03f, 0.98f, 0.8f), Flat(new Color(0.35f, 0.35f, 0.38f), 0.4f, 0.7f));
    }

    /// <summary>A fish-drying rack: two trestles and poles hung with split, golden-brown dried fish.</summary>
    private static void DryingRack(Node3D b, Vector3 at, float yawDeg)
    {
        var holder = new Node3D { Position = at, RotationDegrees = new Vector3(0f, yawDeg, 0f) };
        b.AddChild(holder);
        var wood = Wood(new Color(0.55f, 0.45f, 0.34f));
        foreach (float sx in new[] { -1.4f, 1.4f })
            foreach (float sz in new[] { -1f, 1f })
                Box(holder, new Vector3(0.08f, 2.3f, 0.08f), new Vector3(sx, 1.1f, sz * 0.35f), wood, new Vector3(sz * 9f, 0f, 0f));
        foreach (float y in new[] { 2.0f, 1.45f })
            Cyl(holder, 0.04f, 0.04f, 3.1f, new Vector3(0f, y, 0f), wood, 6).RotationDegrees = new Vector3(0f, 0f, 90f);
        var fish = Flat(new Color(0.62f, 0.48f, 0.26f), 0.5f);
        var mesh = new SphereMesh { Radius = 0.14f, Height = 0.6f, RadialSegments = 8, Rings = 4 };
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = 18 };
        for (int i = 0; i < 18; i++)
        {
            float y = i < 9 ? 1.72f : 1.17f;
            float x = -1.2f + (i % 9) * 0.3f;
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(0.9f, 1f, 0.3f)), new Vector3(x, y, 0f)));
        }
        holder.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = fish });
        Collider(holder, new Vector3(3f, 2f, 0.6f), new Vector3(0f, 1f, 0f));
    }

    /// <summary>A two-wheeled farm cart, shafts resting on the ground, sacks or hay in the bed.</summary>
    private static void Cart(Building b, int variant)
    {
        var wood = Wood(new Color(0.66f, 0.5f, 0.34f));
        var dark = Timber();
        var cart = new Node3D { RotationDegrees = new Vector3(-6f, 0f, 0f) };
        b.AddChild(cart);
        Box(cart, new Vector3(1.6f, 0.1f, 2.4f), new Vector3(0f, 0.95f, 0f), wood);
        foreach (float sx in new[] { -0.78f, 0.78f })
            Box(cart, new Vector3(0.06f, 0.4f, 2.4f), new Vector3(sx, 1.2f, 0f), wood);
        Box(cart, new Vector3(1.6f, 0.4f, 0.06f), new Vector3(0f, 1.2f, -1.18f), wood);
        foreach (float sx in new[] { -0.95f, 0.95f })
        {
            Cyl(cart, 0.62f, 0.62f, 0.08f, new Vector3(sx, 0.62f, 0f), dark, 16).RotationDegrees = new Vector3(0f, 0f, 90f);
            Cyl(cart, 0.5f, 0.5f, 0.1f, new Vector3(sx * 1.01f, 0.62f, 0f), Flat(new Color(0.12f, 0.1f, 0.08f)), 16).RotationDegrees = new Vector3(0f, 0f, 90f);
            Cyl(cart, 0.1f, 0.1f, 0.2f, new Vector3(sx * 1.02f, 0.62f, 0f), Flat(new Color(0.2f, 0.18f, 0.16f), 0.4f, 0.6f), 8).RotationDegrees = new Vector3(0f, 0f, 90f);
        }
        foreach (float sx in new[] { -0.55f, 0.55f })
            Box(cart, new Vector3(0.08f, 0.08f, 2.4f), new Vector3(sx, 0.62f, 2.2f), wood, new Vector3(-12f, 0f, 0f));
        if (variant % 2 == 0)
        {
            var sack = Cloth(new Color(0.78f, 0.7f, 0.52f), false);
            for (int i = 0; i < 4; i++)
                cart.AddChild(new MeshInstance3D
                {
                    Mesh = new SphereMesh { Radius = 0.3f, Height = 0.6f, RadialSegments = 10, Rings = 6 },
                    MaterialOverride = sack,
                    Position = new Vector3(-0.35f + (i % 2) * 0.7f, 1.25f, -0.6f + (i / 2) * 0.8f),
                    Scale = new Vector3(1f, 0.75f, 1.3f),
                });
        }
        else
            Box(cart, new Vector3(1.45f, 0.6f, 2.2f), new Vector3(0f, 1.3f, 0f), Roof(new Color(0.8f, 0.68f, 0.4f)));
        Collider(b, new Vector3(2f, 1.6f, 2.6f), new Vector3(0f, 0.8f, 0f));
    }
}
