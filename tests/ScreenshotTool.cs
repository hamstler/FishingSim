using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FishingSim.Accounts;
using FishingSim.Data;
using FishingSim.Fishing;
using FishingSim.Progress;
using FishingSim.UI;
using FishingSim.World;

namespace FishingSim.Tests;

/// <summary>
/// Renders the game and saves a PNG (needs a display and GL, e.g. xvfb-run with --rendering-driver opengl3):
///   godot --rendering-driver opengl3 --path . res://tests/screenshot.tscn -- HOUR WEATHER LABEL OUT YAW PITCH MODE ARG MAP
/// MODE: 0 plain, 1 mid-fight, 2 just landed, 3 shop (ARG = tab), 4 pause menu, 5 player screen (ARG = tab),
///       6 login screen, 7 map selection, 8 tournament running (ARG = tab), 9 tournament results,
///       10 standing at the first shop door (prompt), 11 inside that shop's window (ARG = shop kind index).
/// </summary>
public partial class ScreenshotTool : Node
{
    public override async void _Ready()
    {
        var a = OS.GetCmdlineUserArgs();
        string Arg(int i, string fallback) => i < a.Length ? a[i] : fallback;
        float hour = float.Parse(Arg(0, "12"), System.Globalization.CultureInfo.InvariantCulture);
        int weather = int.Parse(Arg(1, "0"));
        string output = Arg(3, "/tmp/shot.png");
        float yaw = float.Parse(Arg(4, "0"), System.Globalization.CultureInfo.InvariantCulture);
        float pitch = float.Parse(Arg(5, "0"), System.Globalization.CultureInfo.InvariantCulture);
        int mode = int.Parse(Arg(6, "0"));
        int arg = int.Parse(Arg(7, "0"));
        int map = int.Parse(Arg(8, "-1"));
        bool hasPos = a.Length > 10;
        float px = hasPos ? float.Parse(Arg(9, "0"), System.Globalization.CultureInfo.InvariantCulture) : 0f;
        float pz = hasPos ? float.Parse(Arg(10, "0"), System.Globalization.CultureInfo.InvariantCulture) : 0f;

        GetWindow().Size = new Vector2I(1280, 720);
        UserStore.Path = "/tmp/fishsim_shot_users.json";
        await NextFrame();

        if (mode is 6 or 7)
        {
            await MenuShot(mode, output);
            GetTree().Quit();
            return;
        }

        if (map >= 0)
        {
            Main.UnlockAllMaps = true;
            Session.Username = "admin";
            Session.MapIndex = map;
        }
        var main = GD.Load<PackedScene>("res://main.tscn").Instantiate<Main>();
        GetTree().Root.AddChild(main);
        await NextFrame();
        await NextFrame();

        var world = main.GetChildren().OfType<WorldState>().First();
        world.Paused = true;
        world.Hour = hour;
        world.Weather = (Core.Weather)weather;
        if (hasPos)
        {
            var bed = main.GetChildren().OfType<LakeBed>().First();
            main.Player.GlobalPosition = new Vector3(px, bed.HeightAt(px, pz) + 0.3f, pz);
            for (int i = 0; i < 20; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        main.Player.RotationDegrees = new Vector3(0, yaw, 0);
        main.Player.Camera.RotationDegrees = new Vector3(pitch, 0, 0);
        var rod = main.Rod;
        int frames = 30;
        if (a.Length > 11 && a[11] == "1")
            main.GetChildren().OfType<Hud>().First().Visible = false;

        if (mode is 1 or 2)
        {
            rod.DevInstantBite = true;
            rod.DevSpeciesIndex = 2;
            rod.CastNow(0.35f);
            double t = 0;
            bool hooked = false;
            double stop = mode == 2 ? 4 : 9;
            while (t < 25 && !(hooked && rod.State == RodState.Fight && t > stop))
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                t += 1.0 / 60.0;
                if (rod.State == RodState.Bite && !hooked)
                {
                    hooked = true;
                    rod.TryHook();
                }
                if (rod.State == RodState.Fight)
                    rod.DragSetting = 0.3f;
            }
            if (mode == 2)
            {
                var carp = GD.Load<FishSpecies>("res://data/fish/karp.tres");
                rod.ForceLand(carp, 4.7f);
                frames = 3;
            }
        }
        else if (mode == 12)
        {
            // Lifting a beaten roach out of the water (arg = tenths of the lift to stop at, e.g. 5 = halfway).
            rod.DevInstantBite = true;
            rod.DevUnbreakable = true;
            rod.DevSpeciesIndex = 0;
            rod.CastNow(0.3f);
            double t = 0;
            while (t < 60 && rod.State != RodState.Close)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                t += 1.0 / 60.0;
                if (rod.State == RodState.Bite)
                    rod.TryHook();
                rod.ReelOverride = rod.State == RodState.Fight;
            }
            rod.ReelOverride = false;
            rod.LiftOverride = true;
            float stopAt = Math.Clamp(arg, 1, 9) / 10f;
            while (rod.State == RodState.Close && rod.Lift < stopAt)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            frames = 2;
        }
        else if (mode == 3)
        {
            var carp = GD.Load<FishSpecies>("res://data/fish/karp.tres");
            var bream = GD.Load<FishSpecies>("res://data/fish/leszcz.tres");
            rod.Profile.AddCatch(carp, 4.7f);
            rod.Profile.AddCatch(bream, 1.6f);
            rod.Profile.AddCatch(bream, 2.2f);
            rod.Profile.Money = 340;
            var shop = main.GetChildren().OfType<Shop>().First();
            shop.Toggle();
            shop.Tabs.CurrentTab = arg;
            rod.Profile.NotifyChanged();
        }
        else if (mode is 10 or 11)
        {
            var spot = main.Scenery.Interactables.First(i => i.Kind is InteractKind.Tackle or InteractKind.General);
            var building = main.Scenery.GetChildren().OfType<Building>().First(bd => bd.Spec.IsFunctional && bd.Title == spot.Title);
            var d = new Vector3(building.GlobalPosition.X - spot.GlobalPosition.X, 0, building.GlobalPosition.Z - spot.GlobalPosition.Z);
            main.Player.GlobalPosition = spot.GlobalPosition + new Vector3(0, 0.5f, 0);
            main.Player.RotationDegrees = new Vector3(0, Mathf.RadToDeg(MathF.Atan2(-d.X, -d.Z)), 0);
            main.Player.Camera.RotationDegrees = new Vector3(4, 0, 0);
            for (int i = 0; i < 30; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (mode == 11)
                main.GetChildren().OfType<Shop>().First().OpenFor((ShopKind)arg);
        }
        else if (mode is 8 or 9)
        {
            var p = rod.Profile;
            p.Level = 3;
            p.Money = 300;
            var manager = main.Tournaments;
            manager.Start(manager.Tournaments[0]);
            var session = manager.Active!;
            foreach (float w in new[] { 0.9f, 1.4f, 0.6f, 2.1f })
                session.AddPlayerCatch(w);
            session.Advance(mode == 8 ? 140f : session.Duration);
            if (mode == 8)
            {
                var ui = main.GetChildren().OfType<GameUI>().First();
                ui.Open(3);
                if (arg == 1)
                    ui.Close();
            }
            else
            {
                manager._Process(0.0);
            }
        }
        else if (mode == 4)
        {
            main.GetChildren().OfType<PauseMenu>().First().Open();
        }
        else if (mode == 5)
        {
            var roach = GD.Load<FishSpecies>("res://data/fish/ploc.tres");
            var bream = GD.Load<FishSpecies>("res://data/fish/leszcz.tres");
            var carp = GD.Load<FishSpecies>("res://data/fish/karp.tres");
            var p = rod.Profile;
            p.AddCatch(roach, 0.45f, new CatchContext("Jezioro Zielone", "Chleb", "Hak #5 (średni)", 1.8f, "07:12"));
            p.AddCatch(bream, 1.9f, new CatchContext("Jezioro Zielone", "Robak", "Hak #5 (średni)", 3.4f, "08:40"));
            p.AddCatch(carp, 5.3f, new CatchContext("Staw Karpiowy", "Kukurydza", "Hak #8 (duży)", 2.1f, "19:05"));
            p.AddCatch(roach, 0.7f, new CatchContext("Jezioro Zielone", "Chleb", "Hak #2 (mały)", 2.6f, "09:20"));
            var ui = main.GetChildren().OfType<GameUI>().First();
            ui.Open(arg);
            ui.EnsureMap();
        }

        for (int i = 0; i < frames; i++)
            await NextFrame();
        await Save(output);
        GetTree().Quit();
    }

    private async Task MenuShot(int mode, string output)
    {
        var menu = GD.Load<PackedScene>("res://menu.tscn").Instantiate<Menu>();
        GetTree().Root.AddChild(menu);
        for (int i = 0; i < 5; i++)
            await NextFrame();
        if (mode == 7)
        {
            menu.FillCredentials("admin", "admin");
            await menu.TryLogin();
            for (int i = 0; i < 8; i++)
                await NextFrame();
        }
        await Save(output);
    }

    private async Task NextFrame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Save(string output)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(output);
        GD.Print($"saved {output} {img.GetSize()}");
    }
}
