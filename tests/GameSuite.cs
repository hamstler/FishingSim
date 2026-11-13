using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FishingSim.World.Props;
using FishingSim.Accounts;
using FishingSim.Core;
using FishingSim.Data;
using FishingSim.Fishing;
using FishingSim.Core.Methods;
using FishingSim.Progress;
using FishingSim.UI;
using FishingSim.World;

namespace FishingSim.Tests;

/// <summary>Whole-game checks: login screen, map selection, pause menu, player screen, shop and the fishing loop.</summary>
public sealed class GameSuite : ITestSuite
{
    public string Name => "game";

    private static InputEventKey Key(Godot.Key code, bool shift = false) =>
        new() { Keycode = code, Pressed = true, ShiftPressed = shift };

    public async Task Run(TestContext t)
    {
        UserStore.Path = "/tmp/fishsim_cs_game_users.json";
        if (Godot.FileAccess.FileExists(UserStore.Path))
            DirAccess.RemoveAbsolute(UserStore.Path);
        Session.Logout();

        await LoginAndMaps(t);
        await InGame(t);
        Session.Logout();
    }

    private async Task LoginAndMaps(TestContext t)
    {
        t.Section("login screen");
        var menu = GD.Load<PackedScene>("res://menu.tscn").Instantiate<Menu>();
        t.Host.GetTree().Root.AddChild(menu);
        await t.NextFrame();
        t.Check(menu.LoginVisible && !menu.MapsVisible, "the menu opens on the login form");
        menu.FillCredentials("admin", "zle");
        var failed = menu.TryLogin();
        await t.NextFrame();
        t.Check(!Session.IsLoggedIn && menu.ErrorText != "" && menu.LoginLocked, "wrong password: not logged in, error shown, button locked");
        await t.Seconds(1.3);
        await failed;
        t.Check(!menu.LoginLocked, "the login button unlocks again");
        menu.FillCredentials("admin", "admin");
        await menu.TryLogin();
        t.Check(Session.Username == "admin" && menu.MapsVisible && !menu.LoginVisible, "admin / admin: logged in, map selection shown");
        await t.NextFrame();
        var buttons = menu.MapButtons.ToList();
        t.Check(buttons.Count(b => b.Text.StartsWith("Łów")) == 2 && buttons.Count(b => b.Text.StartsWith("Zablok")) == 2,
            "a level-1 player sees 2 playable maps and 2 locked");

        t.Section("fish atlas in the menu");
        var atlas = menu.OpenAtlas();
        await t.NextFrame();
        t.Check(atlas.Visible && atlas.Size == menu.Size && atlas.Listed.Count == SpeciesCatalog.Paths.Length,
            $"the atlas opens over the whole menu and lists all {atlas.Listed.Count} species");
        atlas.SetSearch("pstrąg");
        t.Check(atlas.Listed.Count == 3 && atlas.Listed.All(s => s.SpeciesName.StartsWith("Pstrąg")), "searching \"pstrąg\" finds the three trouts");
        atlas.SetSearch("");
        atlas.SetMapFilter(1 + Array.FindIndex(LakeBed.LocationPaths, p => p.EndsWith("staw.tres")));
        t.Check(atlas.Listed.Any(s => s.SpeciesName == "Karp lustrzeń") && atlas.Listed.All(s => s.SpeciesName != "Głowacica"),
            "the map filter shows only the fish of that ground");
        atlas.Select(atlas.Listed.First(s => s.SpeciesName == "Karp lustrzeń"));
        t.Check(atlas.FactsText.Contains("Cyprinus carpio") && atlas.FactsText.Contains("Staw Karpiowy") && atlas.FactsText.Contains("kg"),
            "the selected fish shows its Latin name, sizes and where it lives");
        atlas.Close();
        await t.NextFrame();
        menu.QueueFree();
        await t.NextFrame();
    }

    private async Task<Main> StartMain(TestContext t, int map)
    {
        Session.Username = "admin";
        Session.MapIndex = map;
        var main = GD.Load<PackedScene>("res://main.tscn").Instantiate<Main>();
        t.Host.GetTree().Root.AddChild(main);
        await t.NextFrame();
        await t.NextFrame();
        return main;
    }

    /// <summary>Spinning (a lure led, twitched, struck) and bottom fishing (feeder, groundbait, the rod rest).</summary>
    private static async Task SpinningAndBottom(TestContext t, Main main)
    {
        t.Section("spinning and bottom fishing");
        var rod = main.Rod;
        var loadout = rod.Loadout;
        var profile = rod.Profile;
        int RodIndex(string id) => loadout.Items(TackleSlot.Rod).ToList().FindIndex(i => i.ItemId == id);
        int BaitIndex(string id) => loadout.Items(TackleSlot.Bait).ToList().FindIndex(i => i.ItemId == id);
        int rodBefore = loadout.Index[(int)TackleSlot.Rod], baitBefore = loadout.Index[(int)TackleSlot.Bait];
        profile.Owned["rod"].Add("cisowy_spinning");
        profile.Owned["rod"].Add("grabowy_gruntowy");
        loadout.BaitCounts["blystka_obrotowa"] = 1;
        loadout.Index[(int)TackleSlot.Rod] = RodIndex("cisowy_spinning");
        loadout.Index[(int)TackleSlot.Bait] = BaitIndex("blystka_obrotowa");
        rod.ApplyTackle();
        rod.DevInstantBite = false;
        rod.DevSpeciesIndex = -1;
        t.Check(rod.Method == FishingSim.Core.Methods.FishingMethod.Spinning && loadout.Ready, "a lure on a spinning rod fishes by spinning");
        rod.CastNow(0.9f);
        await WaitForState(t, rod, RodState.Waiting, 6);
        t.Check(rod.State == RodState.Waiting && rod.Lure != null && rod.CastMethod == FishingSim.Core.Methods.FishingMethod.Spinning,
            $"the spinner lands ({rod.State})");
        await t.Seconds(1.2);
        float sank = rod.Lure?.Depth ?? 0f;
        t.Check(sank > 0.25f && rod.Bobber.GlobalPosition.Y < -0.2f, $"left alone it sinks ({sank:0.00} m, y {rod.Bobber.GlobalPosition.Y:0.00})");
        float outBefore = rod.LineOut;
        rod.ReelOverride = true;
        await t.Seconds(1.0);
        var lure = rod.Lure;
        t.Check(lure != null && rod.LineOut < outBefore - 0.3f && lure.Speed > 0f && rod.Reeling,
            $"reeling draws it in ({outBefore:0.0} -> {rod.LineOut:0.0} m, {lure?.Judgement.Title()})");
        rod.ReelOverride = false;
        await t.NextFrame();
        float beforeTwitch = rod.LineOut;
        rod.TryHook();
        // physics_frame is emitted before the nodes' physics step: wait for two, so one step has run.
        await t.Host.ToSignal(t.Host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        await t.Host.ToSignal(t.Host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        t.Check(rod.State == RodState.Waiting && rod.LineOut < beforeTwitch - 0.05f, $"RMB twitches the lure: it darts towards the angler ({beforeTwitch:0.00} -> {rod.LineOut:0.00} m, {rod.Lure?.Judgement.Title()}, banner {rod.BannerText}, depth {rod.Lure?.Depth:0.00})");
        rod.ResetToIdle();
        t.Check(rod.Lure == null && rod.State == RodState.Idle, "reset takes the lure out");

        int pikeIndex = rod.Species.ToList().FindIndex(sp => sp.SpeciesName == "Szczupak");
        rod.DevInstantBite = true;
        rod.DevSpeciesIndex = pikeIndex;
        rod.CastNow(0.9f);
        await WaitForState(t, rod, RodState.Waiting, 6);
        rod.ReelOverride = true;
        double waited = 0;
        while (rod.State == RodState.Waiting && waited < 8)
        {
            await t.Host.ToSignal(t.Host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            waited += 1.0 / Engine.PhysicsTicksPerSecond;
        }
        t.Check(rod.State is RodState.Nibble or RodState.Bite or RodState.Fight, $"a pike hits the lure being reeled ({rod.State})");
        if (rod.State is RodState.Nibble or RodState.Bite)
            rod.TryHook();
        t.Check(rod.State == RodState.Fight && !rod.Bobber.TerminalVisible, $"struck (or hooked by itself) it fights, the lure in its mouth ({rod.State})");
        rod.ReelOverride = false;
        rod.ResetToIdle();

        // Bottom: the feeder, the baited spot, the rod rest.
        loadout.BaitCounts["worm"] = 10;
        loadout.Index[(int)TackleSlot.Rod] = RodIndex("grabowy_gruntowy");
        loadout.Index[(int)TackleSlot.Bait] = BaitIndex("worm");
        rod.ApplyTackle();
        t.Check(rod.Method == FishingSim.Core.Methods.FishingMethod.Bottom, "bait on a bottom rod lies on the bottom");
        rod.DevSpeciesIndex = -1;
        rod.DevInstantBite = true;
        rod.Groundbait.Clear();
        int worms = loadout.BaitCount;
        rod.CastNow(0.8f);
        await WaitForState(t, rod, RodState.Waiting, 6);
        t.Check(rod.State == RodState.Waiting && rod.FeederLoaded && loadout.BaitCount == worms - 1,
            $"the feeder takes one worm as groundbait ({worms} -> {loadout.BaitCount})");
        bool rested = rod.PutOnRest();
        t.Check(rested && rod.OnRest && main.Player.Rig.GetParent() is RodRest && rod.NearRest,
            $"Z lays the rod on a rest in front of the angler ({rod.BannerText})");
        rod.ReelOverride = true;
        await t.NextFrame();
        await t.Host.ToSignal(t.Host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        t.Check(!rod.Reeling, "a rod on its rest is not reeled");
        rod.ReelOverride = false;
        waited = 0;
        while (!rod.RigSettled && waited < 8)
        {
            await t.Host.ToSignal(t.Host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            waited += 1.0 / Engine.PhysicsTicksPerSecond;
        }
        t.Check(rod.RigSettled && rod.Bobber.GlobalPosition.Y < -0.3f && rod.GroundbaitHeat > 0.5f,
            $"the feeder sinks to the bottom (y {rod.Bobber.GlobalPosition.Y:0.0}) and baits the spot ({rod.GroundbaitHeat:0.00})");
        await WaitForState(t, rod, RodState.Bite, 10);
        t.Check(rod.State == RodState.Bite && rod.OnRest, $"a bite shows on the rod lying on its rest ({rod.State})");
        rod.TryHook();
        t.Check(rod.State == RodState.Fight && !rod.OnRest && main.Player.Rig.GetParent() == main.Player.Camera,
            "RMB from close by picks the rod up and strikes");
        rod.ResetToIdle();
        rod.DevInstantBite = false;
        rod.DevSpeciesIndex = -1;
        loadout.Index[(int)TackleSlot.Rod] = rodBefore;
        loadout.Index[(int)TackleSlot.Bait] = baitBefore;
        rod.ApplyTackle();
    }

    private static async Task WaitForState(TestContext t, FishingRod rod, RodState wanted, double maxSeconds = 25)
    {
        double elapsed = 0;
        while (rod.State != wanted && elapsed < maxSeconds)
        {
            await t.Host.ToSignal(t.Host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            elapsed += 1.0 / Engine.PhysicsTicksPerSecond;
        }
    }

    private async Task InGame(TestContext t)
    {
        int zatoka = Array.FindIndex(LakeBed.LocationPaths, p => GD.Load<LocationData>(p).DisplayName == "Zatoka Szuwarowa");
        int staw = Array.FindIndex(LakeBed.LocationPaths, p => GD.Load<LocationData>(p).DisplayName == "Staw Karpiowy");

        t.Section("map selection reaches the game");
        var main = await StartMain(t, staw);
        var lake = main.GetChildren().OfType<LakeBed>().First();
        t.Check(lake.Location.RequiredLevel <= main.Rod.Profile.Level, "a locked map never loads");
        main.QueueFree();
        await t.NextFrame();

        main = await StartMain(t, zatoka);
        var rod = main.Rod;
        lake = main.GetChildren().OfType<LakeBed>().First();
        var pause = main.GetChildren().OfType<PauseMenu>().First();
        var ui = main.GetChildren().OfType<GameUI>().First();
        var shop = main.GetChildren().OfType<Shop>().First();
        var profile = rod.Profile;
        var loadout = rod.Loadout;
        t.Check(lake.Location.DisplayName == "Zatoka Szuwarowa", "the picked map is the one that loads");
        t.Check(profile.Username == "admin", "the game runs as the logged-in user");

        t.Section("minimap and clock");
        var minimap = main.GetChildren().OfType<Hud>().First().GetChildren().OfType<Minimap>().FirstOrDefault();
        t.Check(minimap != null && minimap.AnchorLeft == 1f && minimap.OffsetTop > 0f && minimap.OffsetTop < 40f,
            "the minimap and clock sit in the top-right corner");
        var mc = new Vector2(10f, -20f);
        t.Check(Minimap.ToMap(mc, mc) == new Vector2(Minimap.MapDiameter / 2f, Minimap.MapDiameter / 2f)
                && MathF.Abs(Minimap.ToMap(mc + new Vector2(Minimap.ViewMetres / 2f, 0f), mc).X - Minimap.MapDiameter) < 0.01f,
            $"the minimap is centred on the angler and shows {Minimap.ViewMetres:0} m across");

        t.Section("pause menu");
        pause.Open();
        t.Check(t.Host.GetTree().Paused && rod.InputBlocked && pause.Visible, "Esc menu pauses the game and blocks fishing");
        ui._UnhandledInput(Key(Godot.Key.I));
        t.Check(!ui.Visible, "the player screen does not open over the pause menu");
        pause.Resume();
        t.Check(!t.Host.GetTree().Paused && !rod.InputBlocked && !pause.Visible, "resume unpauses");

        t.Section("player screen");
        ui._UnhandledInput(Key(Godot.Key.M));
        t.Check(ui.Visible && ui.CurrentTab == 1 && rod.InputBlocked && rod.AllowHookKey, "M opens the map; mouse input is blocked but the hook key is not");
        ui._UnhandledInput(Key(Godot.Key.J));
        t.Check(ui.Visible && ui.CurrentTab == 2, "J switches to the journal");
        ui._UnhandledInput(Key(Godot.Key.J));
        t.Check(!ui.Visible && !rod.InputBlocked && !rod.AllowHookKey, "pressing J again closes the screen and restores input");

        ui.EnsureMap();
        var size = new Vector2(700, 500);
        var back = ui.PixelToWorld(ui.WorldToPixel(12.5f, -40f, size), size);
        t.Check(MathF.Abs(back.X - 12.5f) < 0.01f && MathF.Abs(back.Y + 40f) < 0.01f, "map: world -> pixel -> world round trip");
        t.Check(ui.MapArea.HasPoint(Vector2.Zero) && ui.MapArea.HasPoint(new Vector2(0, 6)), "map frames the pier");

        // Hovering the water lists the fish; the long readout must not stretch the page or the map.
        ui.Open(1);
        for (int i = 0; i < 3; i++)
            await t.NextFrame();
        var mapSize = ui.MapShownSize;
        var pageSize = ui.GetChildren().OfType<Control>().Where(c => c.Visible).Select(c => c.Size).FirstOrDefault();
        ui.HoverMap(ui.WorldToPixel(0f, LakeBed.PierEndZ - 6f, Vector2.One));
        for (int i = 0; i < 3; i++)
            await t.NextFrame();
        var pageAfter = ui.GetChildren().OfType<Control>().Where(c => c.Visible).Select(c => c.Size).FirstOrDefault();
        t.Check(ui.MapInfoText.Contains("Na obecny zestaw") && mapSize.Y > 100f && ui.MapShownSize == mapSize && pageAfter == pageSize,
            $"map: hovering the water lists the fish without resizing the map ({mapSize} -> {ui.MapShownSize}, page {pageSize} -> {pageAfter})");
        ui.Close();

        t.Section("gear keys");
        loadout.Index[(int)TackleSlot.Rod] = 0;
        profile.Owned["rod"].Add("teleskop");
        rod._UnhandledInput(Key(Godot.Key.Key1));
        t.Check(loadout.Rod.ItemId == "teleskop", "key 1 cycles to the next owned rod");
        rod._UnhandledInput(Key(Godot.Key.Key1, shift: true));
        t.Check(loadout.Rod.ItemId == "junior", "Shift+1 goes back");
        rod.CastNow(0.4f);
        rod._UnhandledInput(Key(Godot.Key.Key1));
        t.Check(loadout.Rod.ItemId == "junior", "gear cannot change while a line is out");
        rod.ResetToIdle();

        t.Section("bite chances in the game");
        var live = Array.FindIndex(loadout.Items(TackleSlot.Bait).ToArray(), i => i.ItemId == "livebait");
        loadout.Index[(int)TackleSlot.Bait] = live;
        rod.World.Hour = 19f;
        var chances = rod.BiteChances(3f);
        var takers = chances.Where(c => c.Share > 0f).ToList();
        t.Check(takers.Count > 0 && takers.All(c => c.Species.AffinityFor("livebait") > 0f) && takers.Any(c => c.Species.SpeciesName == "Szczupak"),
            $"live bait only attracts predators ({string.Join(", ", takers.Select(c => c.Species.SpeciesName))})");
        loadout.Index[(int)TackleSlot.Bait] = 0;
        loadout.Index[(int)TackleSlot.Rod] = 0;
        rod.ApplyTackle();
        float junior = rod.EstimateCastRange();
        loadout.Index[(int)TackleSlot.Rod] = 2;
        rod.ApplyTackle();
        t.Check(junior is > 15f and < 24f && rod.EstimateCastRange() > junior * 1.2f, $"cast range grows with the rod (~{junior:0} m -> ~{rod.EstimateCastRange():0} m)");
        loadout.Index[(int)TackleSlot.Rod] = 0;
        rod.ApplyTackle();

        t.Section("leaders and lures in the game");
        float lineOnly = rod.LineStrength;
        int hair = loadout.Items(TackleSlot.Leader).ToList().FindIndex(i => i.ItemId == "przypon_wlosie");
        loadout.Index[(int)TackleSlot.Leader] = hair;
        rod.ApplyTackle();
        t.Check(MathF.Abs(rod.LineStrength - MathF.Min(lineOnly, loadout.Leader.Strength)) < 0.001f && rod.LineStrength < lineOnly,
            $"a thin leader is the weakest link ({lineOnly:0.0} -> {rod.LineStrength:0.0} kg)");
        loadout.Index[(int)TackleSlot.Leader] = 0;
        rod.ApplyTackle();
        t.Check(MathF.Abs(rod.LineStrength - lineOnly) < 0.001f, "without a leader the line holds as before");
        var gearCatalog = rod.Gear!;
        var waders = gearCatalog.Items(GearSlot.Accessory).First(i => i.ItemId == "wodery");
        int cash = profile.Money;
        profile.Money = 1000;
        profile.Level = Math.Max(profile.Level, waders.RequiredLevel);
        t.Check(gearCatalog.Buy(GearSlot.Accessory, waders) == null && rod.Lake.WadeLimit > LakeBed.WadeDepth,
            $"waders let the angler wade deeper ({rod.Lake.WadeLimit:0.0} m)");
        profile.Money = cash;

        t.Section("fishing loop end to end");
        rod.DevInstantBite = true;
        rod.DevSpeciesIndex = 0;
        loadout.Refill();
        rod.World.Hour = 12f;
        rod.CastNow(0.4f);
        t.Check(rod.State == RodState.Flying, "casting puts the bobber in the air");
        await WaitForState(t, rod, RodState.Waiting, 6);
        t.Check(rod.State == RodState.Waiting && rod.Bobber.GlobalPosition.Y is < 0.1f and > -3f, $"the bobber lands in the water (y = {rod.Bobber.GlobalPosition.Y:0.00})");
        await WaitForState(t, rod, RodState.Nibble, 6);
        t.Check(rod.State == RodState.Nibble, "a nibble follows");
        await WaitForState(t, rod, RodState.Bite, 6);
        t.Check(rod.State == RodState.Bite, "then the real bite");
        int baitBefore = loadout.BaitCount;
        ui.Open(1);
        rod._UnhandledInput(Key(Godot.Key.Space));
        t.Check(rod.State == RodState.Bite, "Space no longer strikes (it is the landing net)");
        rod._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        t.Check(rod.State == RodState.Fight, "raising the rod (RMB) sets the hook, even with the map open");
        ui.Close();
        rod.DevUnbreakable = true;
        rod.ReelOverride = true;
        rod.DragSetting = 0.5f;
        int fishBefore = profile.TotalCatches;
        await WaitForState(t, rod, RodState.Fight, 1);
        for (int i = 0; i < 40; i++)
            await t.Host.ToSignal(t.Host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        t.Check(rod.State != RodState.Fight || MathF.Abs(rod.BendModel!.TotalBend()) > 0.05f, $"a fish on the line bends the rod ({rod.BendModel!.TotalBend() * 57.3f:0} deg)");
        await WaitForState(t, rod, RodState.Close, 90);
        t.Check(rod.State == RodState.Close && profile.TotalCatches == fishBefore, "a beaten fish waits at the surface: it is not caught yet");
        rod._UnhandledInput(Key(Godot.Key.Space));
        t.Check(rod.State == RodState.Close && !rod.HasNet, "without a landing net Space does nothing");
        rod.LiftOverride = true;
        await WaitForState(t, rod, RodState.Landed, 15);
        rod.LiftOverride = false;
        rod.ReelOverride = false;
        t.Check(rod.State == RodState.Landed && profile.TotalCatches == fishBefore + 1, "the fish is landed and added to the profile");
        t.Check(rod.BannerText.Contains("XP"), "the banner reports the XP gained");
        var hud = main.GetChildren().OfType<Hud>().First();
        int netBeforeChoice = profile.Keepnet.Count;
        await t.NextFrame();
        t.Check(rod.AwaitingDecision && hud.CatchPanel.Visible && profile.Keepnet.Count == netBeforeChoice,
            "a panel asks where the fish goes; it is not in the keepnet yet");
        await t.Seconds(1.5);
        t.Check(rod.State == RodState.Landed && rod.AwaitingDecision, "the fish waits in the hand until the angler decides");
        hud.CatchPanel.KeepButton.EmitSignal(BaseButton.SignalName.Pressed);
        await t.NextFrame();
        await t.NextFrame();
        t.Check(!rod.AwaitingDecision && profile.Keepnet.Count == netBeforeChoice + 1 && !hud.CatchPanel.Visible,
            $"\"Do siatki\" puts it into the keepnet and closes the panel (awaiting {rod.AwaitingDecision}, net {profile.Keepnet.Count}/{netBeforeChoice}, visible {hud.CatchPanel.Visible}, banner {rod.BannerText})");
        t.Check(profile.Journal.Count > 0 && profile.Journal[^1].Location == "Zatoka Szuwarowa" && profile.Journal[^1].Bait != null, "the journal notes where and on what it was caught");
        t.Check(baitBefore >= 0 && rod.Tension == 0f, "tension is cleared after the fight");
        rod.ResetToIdle();

        t.Section("landing and the shop");
        var carp = GD.Load<FishSpecies>("res://data/fish/karp.tres");
        int money0 = profile.Money;
        rod.ForceLand(carp, 5f);
        t.Check(rod.BannerText.Contains("rekord"), "a record fish is announced");
        rod._UnhandledInput(Key(Godot.Key.E));
        t.Check(profile.Keepnet.Count >= 1 && !rod.AwaitingDecision, "E puts the landed fish into the keepnet");
        rod.ResetToIdle();
        shop.Toggle();
        t.Check(shop.Visible && rod.InputBlocked, "B opens the shop and blocks fishing input");
        int value = profile.KeepnetValue - profile.UndersizedCount * Profile.UndersizedFine;
        shop.SellAll();
        t.Check(profile.Money == money0 + value && profile.Keepnet.Count == 0, "selling in the shop pays out");
        shop.Toggle();
        t.Check(!shop.Visible && !rod.InputBlocked, "closing the shop re-enables input");

        await SpinningAndBottom(t, main);

        t.Section("broken gear and the free kit");
        rod.DevUnbreakable = false;
        string inHand = loadout.Rod.ItemId;
        profile.Owned["rod"].RemoveAll(id => id != inHand);        // the only rod the angler has
        rod.ForceBreak(TackleSlot.Rod);
        t.Check(profile.Owned["rod"].Count == 0 && !loadout.Ready && !main.Player.Rig.Visible,
            "a snapped rod disappears from the inventory and from the hand");
        rod.ResetToIdle();
        t.Check(!rod.TryStartCharge() && rod.State == RodState.Idle && rod.BannerText.Contains("wędki"), "without a rod there is no cast, and the banner says why");
        profile.Money = 500;
        shop.OpenFor(ShopKind.Tackle);
        t.Check(shop.FreeKitButton.Visible && shop.FreeKitButton.Disabled, "with money the free kit is shown but cannot be taken");
        shop.Close();
        profile.Money = 5;
        shop.OpenFor(ShopKind.Tackle);
        t.Check(shop.FreeKitButton.Visible && !shop.FreeKitButton.Disabled, "a broke angler may take the free kit");
        shop.FreeKitButton.EmitSignal(BaseButton.SignalName.Pressed);
        t.Check(profile.Owns(TackleSlot.Rod, "junior") && loadout.Ready && main.Player.Rig.Visible && !shop.FreeKitButton.Visible,
            "the free kit puts a rod back in the hand");
        shop.Close();

        t.Section("camp life: shovel, insect net, campfire");
        rod.ResetToIdle();
        var tools = main.Tools;
        t.Check(!tools.Select(HandTool.Shovel) && tools.Current == HandTool.Rod, "without a shovel there is nothing to switch to");
        profile.Money = 1000;
        foreach (var id in new[] { "lopata", "siatka_na_owady", "garnek" })
            t.Check(main.Gear.Buy(GearSlot.Accessory, GD.Load<TackleItem>($"res://data/tackle/accessories/{id}.tres")) == null, $"the {id} can be bought");
        t.Check(tools.Available().Count == 3 && tools.Select(HandTool.Shovel) && rod.Holstered && !main.Player.Rig.Visible,
            "Q takes the shovel: the rod is put away");
        // Stand on the damp bank by the pier and dig.
        var campLake = main.Rod.Lake;
        Vector3? bank = null;
        for (float r = 6f; r < 60f && bank == null; r += 1.5f)
            for (float ang = 0f; ang < Mathf.Tau && bank == null; ang += 0.25f)
            {
                float x = MathF.Cos(ang) * r, z = MathF.Sin(ang) * r - 6f;
                var ahead = new Vector3(x, 0, z) + new Vector3(MathF.Cos(ang), 0, MathF.Sin(ang)) * 0.85f;
                if (campLake.GroundKindAt(x, z) is FishingSim.Core.Camp.GroundKind.Mud or FishingSim.Core.Camp.GroundKind.Meadow
                    && campLake.GroundKindAt(ahead.X, ahead.Z) is FishingSim.Core.Camp.GroundKind.Mud or FishingSim.Core.Camp.GroundKind.Meadow)
                {
                    bank = new Vector3(x, campLake.HeightAt(x, z) + 0.05f, z);
                    main.Player.GlobalPosition = bank.Value;
                    main.Player.Rotation = new Vector3(0f, MathF.Atan2(-MathF.Cos(ang), -MathF.Sin(ang)), 0f);
                }
            }
        await t.NextFrame();
        int baitsBefore = profile.BaitCounts.Values.Sum();
        int holesBefore = main.DigMarks.HoleCount;
        t.Check(bank != null && tools.Use() && tools.Working && main.Player.Busy, "the left button digs: the angler stands still");
        await t.Seconds(2.6);
        var marks = main.DigMarks;
        t.Check(!tools.Working && tools.LastHaul != null && marks.HoleCount == holesBefore + 1,
            $"a dig leaves a hole ({tools.LastHaul?.Message})");
        t.Check(profile.BaitCounts.Values.Sum() == baitsBefore + (tools.LastHaul?.Total ?? 0), "the bait dug up goes into the bait box");
        tools.Use();
        await t.Seconds(2.6);
        t.Check(tools.LastHaul?.Total == 0 && tools.LastHaul.Message.Contains("już"), "the same spot again gives nothing");
        t.Check(tools.Select(HandTool.Net) && tools.Use(), "the insect net swings");
        await t.Seconds(1.8);
        t.Check(!tools.Working && tools.LastHaul != null, $"a sweep of the net reports its catch ({tools.LastHaul?.Message})");
        t.Check(tools.Select(HandTool.Rod) && !rod.Holstered && main.Player.Rig.Visible, "back to the rod");

        var camp = main.Scenery.Campfires.FirstOrDefault(c => c.PlayerCamp);
        t.Check(camp != null && main.Scenery.Campfires.Count == 2, "a camp by the pier and a hearth in the village");
        var campSpot = main.Scenery.Interactables.FirstOrDefault(i => i.Kind == InteractKind.Campfire && i.Target == camp);
        t.Check(campSpot != null, "the camp can be used (E)");
        profile.Keepnet.Clear();
        profile.Keep(GD.Load<FishSpecies>("res://data/fish/ploc.tres"), 0.3f);
        profile.Keep(GD.Load<FishSpecies>("res://data/fish/ploc.tres"), 0.4f);
        main.Interactions.Activate(campSpot!);
        t.Check(main.Cooking.IsOpen && rod.InputBlocked && !camp!.Lit, "E at the camp opens the cooking panel; the fire is out");
        t.Check(!main.Cooking.Cook(FishingSim.Core.Camp.Cooking.Roast), "nothing cooks on a cold fire");
        main.Cooking.LightFire();
        t.Check(camp.Lit && main.Cooking.Cook(FishingSim.Core.Camp.Cooking.Soup) && camp.Cooking != null && !main.Cooking.IsOpen && !rod.InputBlocked,
            "lit, the soup goes into the pot and the panel closes");
        await t.Seconds(FishingSim.Core.Camp.Cooking.Soup.Seconds + 0.5);
        t.Check(camp.Cooking == null && profile.Keepnet.Count == 0 && profile.ActiveMeal?.Name == FishingSim.Core.Camp.Cooking.Soup.Buff.Name,
            $"the soup is eaten: the fish left the keepnet, the buff is on ({rod.BannerText})");
        camp.PassTime(1f);
        t.Check(camp.Lit, "the fire keeps burning a while");
        camp.PassTime(Campfire.BurnHours - 1f);
        t.Check(!camp.Lit, "then it burns down");
        t.Check(main.Scenery.Stats.GetValueOrDefault("rowboat") > 0, $"rowboats lie on the shore ({main.Scenery.Stats.GetValueOrDefault("rowboat")})");

        t.Section("camp life: lift net, forest, smokehouse, notices");
        profile.Level = Math.Max(profile.Level, 2);
        t.Check(main.Gear.Buy(GearSlot.Accessory, GD.Load<TackleItem>("res://data/tackle/accessories/podrywka.tres")) == null, "the lift net can be bought");
        // A bank with a metre of water a lift net's reach out.
        var bankSpot = TestSpots.LiftNetBank(campLake);
        Vector3? shore = bankSpot is { } bs ? bs.Stand + Vector3.Up * 0.05f : null;
        Vector3 toWater = bankSpot?.ToWater ?? Vector3.Forward;
        main.Player.GlobalPosition = shore ?? Vector3.Zero;
        main.Player.Rotation = new Vector3(0f, MathF.Atan2(-toWater.X, -toWater.Z), 0f);
        await t.NextFrame();
        int liveBefore = profile.BaitCounts.GetValueOrDefault("livebait");
        int baitTotal = profile.BaitCounts.Sum(b => b.Value);   // a slice of bread goes over the net
        bool selected = tools.Select(HandTool.LiftNet), lowered = selected && tools.Use();
        t.Check(shore != null && lowered && tools.Lift == HandTools.LiftStage.Lowering && main.Player.Busy,
            $"the lift net goes down from the bank; the angler stands still (at {shore}, selected {selected}, {rod.BannerText})");
        await t.Seconds(2.2);
        t.Check(tools.Lift == HandTools.LiftStage.Down && tools.LiftNetTop.Y - FishingSim.World.Props.CampProps.LiftDrop < 0f,
            $"it rests in the water (cord top at {tools.LiftNetTop.Y:0.00} m)");
        t.Check(!tools.Select(HandTool.Rod) && tools.Current == HandTool.LiftNet, "the net must come up before the rod is taken");
        tools.AddSoak(40f);
        t.Check(tools.Use() && tools.Lift == HandTools.LiftStage.Lifting, "the second click lifts it");
        await t.Seconds(1.6);
        var fry = tools.FryInNet;
        t.Check(tools.Lift == HandTools.LiftStage.Showing && fry.Shown == Math.Min(fry.Caught, FishingSim.Core.Camp.LiftNetting.MaxFish),
            $"the catch flaps in the lifted net ({fry.Shown} of {fry.Caught}, net top at {tools.LiftNetTop.Y:0.00} m)");
        await t.Seconds(2.6);
        t.Check(tools.Lift == HandTools.LiftStage.Up && !main.Player.Busy && tools.LastHaul != null
                && (tools.LastHaul.Message.StartsWith("Podrywka") || tools.LastHaul.Message.StartsWith("Pusto"))
                && profile.BaitCounts.GetValueOrDefault("livebait") == liveBefore + tools.LastHaul.Total
                && profile.BaitCounts.Sum(b => b.Value) == baitTotal + tools.LastHaul.Total - 1,
            $"the small fish go into the bait box as live bait ({tools.LastHaul?.Message})");
        main.Player.GlobalPosition = main.Scenery.CampPosition + Vector3.Up * 0.1f;
        await t.NextFrame();
        t.Check(!tools.Use() && tools.Lift == HandTools.LiftStage.Up, "on dry land the net is not lowered");
        tools.Select(HandTool.Rod);

        var patches = main.Scenery.Forage;
        t.Check(patches.Count >= 12 && patches.Select(p => p.Kind).Distinct().Count() == 3,
            $"porcini, chanterelles and blueberries grow in the woods ({patches.Count})");
        var patch = patches[0];
        var patchSpot = main.Scenery.Interactables.First(i => i.Target == patch);
        main.Player.GlobalPosition = patchSpot.GlobalPosition + Vector3.Up * 0.1f;
        await t.NextFrame();
        await t.NextFrame();
        string pickId = FishingSim.Core.Camp.Foraging.IdOf(patch.Kind);
        int pantryBefore = profile.Pantry.GetValueOrDefault(pickId);
        t.Check(main.Interactions.Current == patchSpot, $"E picks the {patch.Title} in front of the angler");
        main.Interactions.Activate(patchSpot);
        t.Check(profile.Pantry.GetValueOrDefault(pickId) > pantryBefore && !patch.Ripe && !patchSpot.Active, "picked: into the pantry, the spot is bare");
        await t.NextFrame();
        t.Check(main.Interactions.Current != patchSpot, "a picked spot offers nothing");
        rod.World.Advance(12f);
        rod.World.Advance(12.5f);
        t.Check(patch.Ripe && patchSpot.Active, "a day later it has grown back");

        // The smokehouse: two fish in, two smoked fish out, sold to the smoker.
        var smokeSpot = main.Scenery.Interactables.FirstOrDefault(i => i.Kind == InteractKind.Smokehouse);
        t.Check(smokeSpot != null && main.Scenery.HasSmokehouse, "the village has a smokehouse with its smoker");
        profile.Keepnet.Clear();
        profile.Keep(GD.Load<FishSpecies>("res://data/fish/ploc.tres"), 0.35f);
        profile.Keep(GD.Load<FishSpecies>("res://data/fish/okon.tres"), 0.4f);
        main.Interactions.Activate(smokeSpot!);
        t.Check(main.Smokehouse.IsOpen && rod.InputBlocked, "E at the smokehouse opens its panel");
        int moneyBefore = profile.Money;
        t.Check(main.Smokehouse.SmokeSelected() == 2 && profile.Keepnet.Count == 0 && profile.Smokery.Count == 2 && profile.Money < moneyBefore,
            "both fish hang in the smoke, the wood is paid for");
        t.Check(main.Smokehouse.Collect() == 0, "not ready at once");
        rod.World.Advance(FishingSim.Core.Camp.Smoking.Hours + 0.1f);
        t.Check(main.Smokehouse.Collect() == 2 && profile.Smoked.Count == 2, "two hours later the smoked fish are taken");
        int smokedValue = profile.SmokedValue;
        moneyBefore = profile.Money;
        t.Check(main.Smokehouse.SellSmoked() == smokedValue && profile.Money == moneyBefore + smokedValue && profile.Smoked.Count == 0,
            $"the smoker buys them ({smokedValue} gr)");
        main.Smokehouse.Close();

        // The notice board: hand in the fish notice.
        var boardSpot = main.Scenery.Interactables.FirstOrDefault(i => i.Kind == InteractKind.NoticeBoard);
        t.Check(boardSpot != null && main.Scenery.NoticeBoard != null, "a notice board stands on the square");
        main.Interactions.Activate(boardSpot!);
        t.Check(main.Board.IsOpen && profile.Notices.Count == FishingSim.Core.Camp.NoticeBoard.PerDay, "E opens the board with the day's three notices");
        var wanted = profile.Notices[0];
        var wantedSpecies = rod.Species.First(s => s.SpeciesName == wanted.Item);
        for (int i = 0; i < wanted.Count; i++)
            profile.Keep(wantedSpecies, wanted.MinWeight + 0.02f + i * 0.01f);
        moneyBefore = profile.Money;
        t.Check(main.Board.HandIn(0) && profile.Notices[0].Done && profile.Money == moneyBefore + wanted.Reward,
            $"the fish are handed in ({wanted.Wanted}, {wanted.Reward} gr)");
        t.Check(!main.Board.HandIn(0), "and only once");
        main.Board.Close();

        // A mushroom dish from the pantry.
        int berries = profile.Pantry.GetValueOrDefault(FishingSim.Core.Camp.Foraging.Blueberry);
        profile.AddPantry(new Dictionary<string, int> { [FishingSim.Core.Camp.Foraging.Blueberry] = 12 });
        main.Interactions.Activate(campSpot!);
        main.Cooking.LightFire();
        t.Check(main.Cooking.Cook(FishingSim.Core.Camp.Cooking.Compote), "blueberry compote goes on the fire");
        await t.Seconds(FishingSim.Core.Camp.Cooking.Compote.Seconds + 0.5);
        t.Check(profile.ActiveMeal?.Name == FishingSim.Core.Camp.Cooking.Compote.Buff.Name && profile.Pantry.GetValueOrDefault(FishingSim.Core.Camp.Foraging.Blueberry) == berries,
            $"the compote is drunk: the berries are used, its buff is on ({profile.ActiveMeal?.Name}, {rod.BannerText})");

        main.QueueFree();
        await t.NextFrame();
    }
}
