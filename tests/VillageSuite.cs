using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FishingSim.Accounts;
using FishingSim.Data;
using FishingSim.Fishing;
using FishingSim.UI;
using FishingSim.World;

namespace FishingSim.Tests;

/// <summary>Maps as places: layouts, levelled ground, paths, biomes, roaming, doors and shops.</summary>
public sealed class VillageSuite : ITestSuite
{
    public string Name => "village";

    private static InputEventKey Key(Godot.Key code) => new() { Keycode = code, Pressed = true };

    public async Task Run(TestContext t)
    {
        Layouts(t);
        await InGame(t);
    }

    private static void Layouts(TestContext t)
    {
        var locations = LakeBed.LocationPaths.Select(p => GD.Load<LocationData>(p)).ToList();
        var bed = new LakeBed();
        foreach (var l in locations)
            bed.Locations.Add(l);

        t.Section("building specs");
        var spec = BuildingSpec.Parse("tackle,-22,24");
        t.Check(spec != null && spec.Kind == BuildingKind.Tackle && spec.Position == new Vector2(-22, 24) && spec.YawDegrees == null, "kind,x,z parses");
        var fence = BuildingSpec.Parse(" Fence , 10.5 , -4 , 90 ");
        t.Check(fence != null && fence.Kind == BuildingKind.Fence && fence.YawDegrees == 90f && fence.Position.X == 10.5f, "case, spaces and a yaw are accepted");
        t.Check(BuildingSpec.Parse("castle,1,2") == null && BuildingSpec.Parse("tackle,1") == null && BuildingSpec.Parse("tackle,a,b") == null, "garbage is rejected");
        t.Check(spec!.IsFunctional && !BuildingSpec.Parse("house,0,0")!.IsFunctional, "shops are functional, houses are scenery");
        var facing = BuildingSpec.Parse("house,10,0")!.FacingYaw(new Vector2(0, 0));
        t.Near(MathF.Sin(facing), -1f, 0.001f, "a building faces the plaza (to the west of it, it looks west)");
        var door = BuildingSpec.Parse("tackle,0,10")!.DoorPoint(new Vector2(0, 30));
        t.Check(door.Y > 10f && MathF.Abs(door.X) < 0.01f, "the door is in front of the building, towards the plaza");

        for (int i = 0; i < locations.Count; i++)
        {
            var loc = locations[i];
            bed.SelectInitial(i);
            var layout = bed.Layout;
            string name = loc.DisplayName;
            t.Section($"{name}: village");

            t.Check(layout.Specs.Count == loc.Buildings.Length && layout.Specs.Count >= 8, $"every entry parses ({layout.Specs.Count} buildings and props)");
            var kinds = layout.Specs.Select(s => s.Kind).ToHashSet();
            bool sellsGear = kinds.Contains(BuildingKind.Tackle) || kinds.Contains(BuildingKind.General);
            bool buysFish = kinds.Contains(BuildingKind.FishBuyer) || kinds.Contains(BuildingKind.General);
            bool sellsBait = kinds.Contains(BuildingKind.Bait) || kinds.Contains(BuildingKind.General);
            t.Check(sellsGear && buysFish && sellsBait, "gear, bait and a fish buyer are all available");
            t.Check(kinds.Contains(BuildingKind.BusStop), "there is a bus stop for changing maps");

            bool onLand = layout.Specs.All(s => bed.InsideMetres(loc, s.Position.X, s.Position.Y) < -2f);
            t.Check(onLand, "every building stands on land");
            bool inBounds = layout.Specs.All(s => bed.PlayDistance(s.Position.X, s.Position.Y) < -25f && s.Position.Y > 0f);
            t.Check(inBounds, "every building is inside the walkable area");

            float worstPad = 0f;
            foreach (var pad in layout.Pads)
            {
                for (int k = 0; k < 12; k++)
                {
                    float a = k / 12f * Mathf.Tau;
                    float r = pad.Radius * 0.5f;
                    worstPad = MathF.Max(worstPad, MathF.Abs(bed.HeightAt(loc, pad.Center.X + MathF.Cos(a) * r, pad.Center.Y + MathF.Sin(a) * r) - pad.Height));
                }
            }
            t.Check(worstPad < 0.12f, $"the ground under buildings is level (worst {worstPad:0.000} m)");

            var big = layout.Specs.Where(s => s.PadRadius > 0f).ToList();
            bool overlap = false;
            for (int a = 0; a < big.Count; a++)
                for (int b = a + 1; b < big.Count; b++)
                    overlap |= big[a].Position.DistanceTo(big[b].Position) < (big[a].PadRadius + big[b].PadRadius) * 0.6f;
            t.Check(!overlap, "buildings do not overlap");

            var segment = new PathSegment(MapLayout.PierRoot, loc.PlazaCenter);
            float maxSlope = 0f;
            bool walkable = true;
            for (float d = 0f; d <= segment.A.DistanceTo(segment.B); d += 2f)
            {
                var p = segment.A.Lerp(segment.B, d / segment.A.DistanceTo(segment.B));
                var q = segment.A.Lerp(segment.B, MathF.Min(d + 2f, segment.A.DistanceTo(segment.B)) / segment.A.DistanceTo(segment.B));
                walkable &= bed.IsWalkable(p.X, p.Y);
                maxSlope = MathF.Max(maxSlope, MathF.Abs(bed.HeightAt(loc, q.X, q.Y) - bed.HeightAt(loc, p.X, p.Y)) / 2f);
            }
            t.Check(walkable && maxSlope < 0.55f, $"the main path from the pier is walkable (steepest {maxSlope * 100f:0}% grade)");

            bool doorsOk = true;
            foreach (var s in layout.Specs.Where(s => s.IsFunctional))
            {
                var dp = s.DoorPoint(loc.PlazaCenter);
                doorsOk &= bed.IsWalkable(dp.X, dp.Y) && bed.InsideMetres(loc, dp.X, dp.Y) < 0f && layout.PathDistance(dp.X, dp.Y) < 1f;
            }
            t.Check(doorsOk, "every door is on land, walkable and on a path");
        }

        t.Section("maps differ");
        t.Check(locations.Select(l => l.GrassColor).Distinct().Count() == locations.Count, "each map has its own grass colour");
        t.Check(locations.Select(l => l.WaterShallowColor).Distinct().Count() == locations.Count, "each map has its own water colour");
        t.Check(locations.Select(l => string.Join(",", l.Buildings)).Distinct().Count() == locations.Count, "each map has its own village layout");
        var gory = locations.First(l => l.DisplayName == "Górskie Jezioro");
        var jez = locations.First(l => l.DisplayName == "Jezioro Zielone");
        t.Check(gory.HillScale > jez.HillScale * 1.5f && gory.TreeShare("pine") > gory.TreeShare("oak"), "the mountain map is hillier and pine-forested");

        t.Section("walking limits");
        bed.SelectInitial(0);
        t.Check(bed.IsWalkable(0f, 12f), "the shore is walkable");
        t.Check(bed.IsWalkable(0f, -3f) && bed.IsWalkable(1.2f, 4f), "the pier is walkable");
        t.Check(!bed.IsWalkable(0f, -12f), "deep water beyond the pier end is not");
        t.Check(!bed.IsWalkable(3.5f, -2f) || bed.DepthAt(3.5f, -2f) <= LakeBed.WadeDepth, "next to the pier you only get as deep as wading allows");
        t.Check(LakeBed.OnPier(0f, 0f) && !LakeBed.OnPier(3f, 0f) && !LakeBed.OnPier(0f, -6f), "pier bounds");
        bed.QueueFree();
    }

    private async Task<Main> StartMain(TestContext t, int map)
    {
        Main.UnlockAllMaps = true;
        Session.Username = "admin";
        Session.MapIndex = map;
        var main = GD.Load<PackedScene>("res://main.tscn").Instantiate<Main>();
        t.Host.GetTree().Root.AddChild(main);
        await t.NextFrame();
        await t.NextFrame();
        return main;
    }

    private async Task Physics(TestContext t, int frames)
    {
        for (int i = 0; i < frames; i++)
            await t.Host.ToSignal(t.Host.GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task InGame(TestContext t)
    {
        t.Section("the world in the game");
        var stats = new Dictionary<string, Dictionary<string, int>>();
        Main? main = null;
        for (int i = 0; i < LakeBed.LocationPaths.Length; i++)
        {
            main = await StartMain(t, i);
            var loc = main.GetChildren().OfType<LakeBed>().First().Location;
            var scenery = main.Scenery;
            stats[loc.DisplayName] = new Dictionary<string, int>(scenery.Stats);
            int functional = loc.BuildingSpecs().Count(s => s.IsFunctional);
            int sights = loc.Landmarks.Count(l => !l.StartsWith("platforms"));
            int doors = scenery.Interactables.Count(x => x.Kind != InteractKind.Campfire);
            int fires = scenery.Campfires.Count;
            t.Check(doors == functional && fires == 2 && scenery.Interactables.Count == functional + fires
                    && scenery.Markers.Count == functional + sights + fires,
                $"{loc.DisplayName}: {functional} doors with a prompt, {fires} campfires (camp and hearth), map markers for them and {sights} landmarks");
            t.Check(loc.Landmarks.Length > 0 && scenery.Stats.Where(kv => kv.Key.StartsWith("landmark_")).Sum(kv => kv.Value) == loc.Landmarks.Length,
                $"{loc.DisplayName}: its own landmarks are built ({string.Join(", ", loc.Landmarks.Select(l => l.Split(',')[0]))})");
            t.Check(scenery.Stats.Where(kv => kv.Key.StartsWith("building_")).Sum(kv => kv.Value) == loc.Buildings.Length, $"{loc.DisplayName}: every building was built");
            t.Check(scenery.Stats.GetValueOrDefault("tree_oak") + scenery.Stats.GetValueOrDefault("tree_spruce") + scenery.Stats.GetValueOrDefault("tree_pine")
                + scenery.Stats.GetValueOrDefault("tree_birch") + scenery.Stats.GetValueOrDefault("tree_willow") > 20, $"{loc.DisplayName}: there are trees");
            if (i < LakeBed.LocationPaths.Length - 1)
            {
                main.QueueFree();
                await t.NextFrame();
            }
        }
        int Get(string map, string key) => stats[map].GetValueOrDefault(key);
        t.Check(Get("Zatoka Szuwarowa", "tree_willow") > 5 && Get("Jezioro Zielone", "tree_willow") == 0, "willows grow only in the marsh");
        t.Check(Get("Górskie Jezioro", "tree_pine") > Get("Górskie Jezioro", "tree_oak") && Get("Jezioro Zielone", "tree_pine") < Get("Górskie Jezioro", "tree_pine"), "the mountains are pine country");
        t.Check(Get("Staw Karpiowy", "flower") > Get("Jezioro Zielone", "flower") * 1.5f, "the farm pond is a flowery meadow");
        t.Check(Get("Zatoka Szuwarowa", "reed") > Get("Jezioro Zielone", "reed") && Get("Górskie Jezioro", "rock") > Get("Jezioro Zielone", "rock"),
            $"reeds fill the bay and rocks fill the mountains (reeds {Get("Zatoka Szuwarowa", "reed")} vs {Get("Jezioro Zielone", "reed")}, rocks {Get("Górskie Jezioro", "rock")} vs {Get("Jezioro Zielone", "rock")})");
        t.Check(Get("Staw Karpiowy", "tree_oak") + Get("Staw Karpiowy", "tree_spruce") + Get("Staw Karpiowy", "tree_birch")
            < Get("Jezioro Zielone", "tree_oak") + Get("Jezioro Zielone", "tree_spruce") + Get("Jezioro Zielone", "tree_birch"), "the pond meadow has fewer trees than the forest lake");
        t.Check(Get("Staw Karpiowy", "building_windmill") == 1 && Get("Staw Karpiowy", "building_barn") == 1, "the farm has a windmill and a barn");
        foreach (var (map, counts) in stats)
            GD.Print($"  plants on {map}: " + string.Join(", ", new[] { "tree_", "bush", "hazel", "fern", "tuft", "flower", "reed", "rush", "iris", "lily", "lily_flower", "pondweed" }
                .Select(k => $"{k.TrimEnd('_')} {(k.EndsWith('_') ? counts.Where(kv => kv.Key.StartsWith(k)).Sum(kv => kv.Value) : counts.GetValueOrDefault(k))}")));
        t.Check(stats.Values.All(c => c.GetValueOrDefault("fern") > 50 && c.GetValueOrDefault("tuft") > 1000 && c.GetValueOrDefault("rush") > 100),
            "every map has a forest floor of ferns, grassy meadows and rushes at the water's edge");
        t.Check(Get("Zatoka Szuwarowa", "lily") > Get("Jezioro Zielone", "lily") && Get("Jezioro Zielone", "lily") > Get("Górskie Jezioro", "lily") * 3
            && Get("Jezioro Zielone", "lily_flower") > 0 && Get("Staw Karpiowy", "pondweed") > 0,
            $"water lilies cover the warm bay more than the cold mountain lake (bay {Get("Zatoka Szuwarowa", "lily")}, lake {Get("Jezioro Zielone", "lily")}, mountains {Get("Górskie Jezioro", "lily")})");
        t.Check(Get("Jezioro Zielone", "hazel") > 30 && Get("Jezioro Zielone", "iris") > 20, "hazel shrubs under the trees and yellow irises on the banks");

        // main is now the mountain map (last in the loop); go back to the first map for the rest.
        main!.QueueFree();
        await t.NextFrame();
        main = await StartMain(t, 0);
        var lake = main.GetChildren().OfType<LakeBed>().First();
        var rod = main.Rod;
        var player = main.Player;
        var shop = main.GetChildren().OfType<Shop>().First();
        var ui = main.GetChildren().OfType<GameUI>().First();
        var interactions = main.Interactions;
        var world = main.GetChildren().OfType<WorldState>().First();

        t.Section("roaming");
        t.Near(player.GlobalPosition.Z, Player.SpawnPoint.Z, 0.6f, "the player starts at the end of the pier");
        var tackle = main.Scenery.Interactables.First(s => s.Kind == InteractKind.Tackle);
        player.GlobalPosition = new Vector3(tackle.GlobalPosition.X, tackle.GlobalPosition.Y + 0.6f, tackle.GlobalPosition.Z);
        await Physics(t, 30);
        t.Check(Mathf.Abs(player.GlobalPosition.X - tackle.GlobalPosition.X) < 1f && player.IsOnFloor(), "the player can stand anywhere on the land, far from the pier");
        t.Check(interactions.Current == tackle, "standing at the tackle shop door offers the shop");
        t.Check(tackle.Prompt.Contains("Kram wędkarski"), $"the prompt names the shop ({tackle.Prompt})");

        player.GlobalPosition = new Vector3(tackle.GlobalPosition.X + 25f, tackle.GlobalPosition.Y + 0.6f, tackle.GlobalPosition.Z + 20f);
        await Physics(t, 20);
        t.Check(interactions.Current == null, "far from every door there is no prompt");

        // Stepping off the side of the pier onto the sand and walking back onto the deck.
        player.Respawn();
        player.GlobalPosition = new Vector3(2.5f, 1.3f, 3.6f);
        player.Rotation = new Vector3(0f, Mathf.Pi / 2f, 0f);        // W walks towards -X, the pier
        await Physics(t, 20);
        float sandY = player.GlobalPosition.Y;
        Input.ParseInputEvent(new InputEventKey { Keycode = Godot.Key.W, PhysicalKeycode = Godot.Key.W, Pressed = true });
        await Physics(t, 36);
        Input.ParseInputEvent(new InputEventKey { Keycode = Godot.Key.W, PhysicalKeycode = Godot.Key.W, Pressed = false });
        await Physics(t, 10);
        t.Check(sandY < 0.95f && player.GlobalPosition.Y > 1.0f && Mathf.Abs(player.GlobalPosition.X) < 1.5f,
            $"walking from the sand back onto the pier works (sand y {sandY:0.00} -> {player.GlobalPosition.Y:0.00}, x {player.GlobalPosition.X:0.00})");
        player.Rotation = Vector3.Zero;

        player.GlobalPosition = new Vector3(0f, 2f, -20f);   // deep water
        await Physics(t, 5);
        t.Check(player.GlobalPosition.Z > -6f, "deep water pushes the player back to dry ground or the pier");
        player.Respawn();
        await Physics(t, 5);

        var spots = main.Scenery.Interactables;
        t.Check(InteractionManager.Nearest(spots, spots[0].GlobalPosition + new Vector3(1f, 0f, 0f)) == spots[0], "the nearest door wins");
        t.Check(InteractionManager.Nearest(spots, new Vector3(0, 0, -100)) == null, "nothing is near in the middle of the lake");

        t.Section("signs");
        foreach (var text in new[] { "KRAM WĘDKARSKI", "GILDIA WĘDKARZY", "SKUP RYB", "Ł" })
        {
            var board = new Vector2(2.4f, 0.7f);
            float px = FishingSim.World.Props.BuildingFactory.FitPixelSize(text, board);
            var size = ThemeDB.FallbackFont.GetStringSize(text, HorizontalAlignment.Left, -1, 64) * px;
            t.Check(size.X <= board.X * 0.8f && size.Y <= board.Y * 0.6f, $"'{text}' fits its sign ({size.X:0.00} x {size.Y:0.00} m on {board.X} x {board.Y} m)");
        }

        t.Section("shops open by kind");
        int HiddenTabs() => Enumerable.Range(0, shop.Tabs.GetTabCount()).Count(i => shop.Tabs.IsTabHidden(i));
        int total = shop.Tabs.GetTabCount();
        foreach (var (kind, shown) in new[]
        {
            (InteractKind.Tackle, 8), (InteractKind.Bait, 1), (InteractKind.FishBuyer, 2), (InteractKind.Market, 2), (InteractKind.General, 11),
            (InteractKind.BusStop, 1),
        })
        {
            var spot = new Interactable { Kind = kind, Title = kind.ToString() };
            interactions.Activate(spot);
            t.Check(shop.Visible && total - HiddenTabs() == shown && !shop.Tabs.IsTabHidden(shop.Tabs.CurrentTab), $"{kind}: the shop shows {shown} tab(s)");
            shop.Close();
            spot.Free();
        }
        var office = new Interactable { Kind = InteractKind.Office, Title = "Gildia wędkarzy" };
        interactions.Activate(office);
        t.Check(ui.Visible && ui.CurrentTab == 3 && !shop.Visible, "the tournament office opens the tournaments tab");
        ui.Close();
        office.Free();

        rod.CastNow(0.3f);
        shop.OpenFor(ShopKind.General);
        t.Check(!shop.Visible, "a shop cannot be opened while a line is out");
        rod.ResetToIdle();
        shop._UnhandledInput(Key(Godot.Key.B));
        t.Check(!shop.Visible, "the old B shortcut is gone: you have to walk to a shop");

        t.Section("changing maps");
        shop.OpenFor(ShopKind.Travel);
        shop.Close();
        player.GlobalPosition = new Vector3(40f, 8f, 30f);
        lake.SetLocation(2);
        await t.NextFrame();
        t.Check(lake.LocationIndex == 2 && player.GlobalPosition.DistanceTo(Player.SpawnPoint) < 0.5f && rod.State == RodState.Idle, "a new map puts the player back on the pier");
        t.Check(main.Scenery.Interactables.Count(x => x.Kind != InteractKind.Campfire) == lake.Location.BuildingSpecs().Count(s => s.IsFunctional)
                && main.Scenery.Campfires.Count == 2, "the new map's doors and campfires replace the old ones");

        t.Section("night");
        var building = main.Scenery.GetChildren().OfType<Building>().First(b => b.Spec.IsFunctional);
        building.SetDaylight(1f);
        t.Check(!building.IsGlowing, "windows are dark by day");
        building.SetDaylight(0f);
        t.Check(building.IsGlowing, "windows light up at night");
        world.Hour = 12f;

        main.QueueFree();
        await t.NextFrame();
        Main.UnlockAllMaps = false;
        Session.Logout();
    }
}
