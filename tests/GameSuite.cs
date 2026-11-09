using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FishingSim.Accounts;
using FishingSim.Core;
using FishingSim.Data;
using FishingSim.Fishing;
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
        await WaitForState(t, rod, RodState.Close, 90);
        t.Check(rod.State == RodState.Close && profile.TotalCatches == fishBefore, "a beaten fish waits at the surface: it is not caught yet");
        rod._UnhandledInput(Key(Godot.Key.Space));
        t.Check(rod.State == RodState.Close && !rod.HasNet, "without a landing net Space does nothing");
        rod.LiftOverride = true;
        await WaitForState(t, rod, RodState.Landed, 15);
        rod.LiftOverride = false;
        rod.ReelOverride = false;
        t.Check(rod.State == RodState.Landed && profile.TotalCatches == fishBefore + 1, "the fish is landed and added to the profile");
        t.Check(profile.Journal.Count > 0 && profile.Journal[^1].Location == "Zatoka Szuwarowa" && profile.Journal[^1].Bait != null, "the journal notes where and on what it was caught");
        t.Check(rod.BannerText.Contains("XP"), "the banner reports the XP gained");
        t.Check(baitBefore >= 0 && rod.Tension == 0f, "tension is cleared after the fight");
        rod.ResetToIdle();

        t.Section("landing and the shop");
        var carp = GD.Load<FishSpecies>("res://data/fish/karp.tres");
        int money0 = profile.Money;
        rod.ForceLand(carp, 5f);
        t.Check(rod.BannerText.Contains("rekord") && profile.Keepnet.Count >= 1, "a record fish is announced");
        rod.ResetToIdle();
        shop.Toggle();
        t.Check(shop.Visible && rod.InputBlocked, "B opens the shop and blocks fishing input");
        int value = profile.KeepnetValue;
        shop.SellAll();
        t.Check(profile.Money == money0 + value && profile.Keepnet.Count == 0, "selling in the shop pays out");
        shop.Toggle();
        t.Check(!shop.Visible && !rod.InputBlocked, "closing the shop re-enables input");

        main.QueueFree();
        await t.NextFrame();
    }
}
