using System;
using System.Collections.Generic;
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

        // FISHSIM_SHOT_SIZE=WxH renders a bigger shot (e.g. 2560x1440 to check the fish at QHD).
        var size = new Vector2I(1280, 720);
        var asked = System.Environment.GetEnvironmentVariable("FISHSIM_SHOT_SIZE")?.Split('x');
        if (asked is { Length: 2 } && int.TryParse(asked[0], out int shotW) && int.TryParse(asked[1], out int shotH))
            size = new Vector2I(shotW, shotH);
        GetWindow().Size = size;
        UserStore.Path = "/tmp/fishsim_shot_users.json";
        await NextFrame();

        if (mode == 17)
        {
            // Postcards of every map, day and dusk.
            var sheet = Image.CreateEmpty(FishingSim.Graphics.Postcard.Width * 2, FishingSim.Graphics.Postcard.Height * 4, false, Image.Format.Rgb8);
            for (int i = 0; i < FishingSim.World.LakeBed.LocationPaths.Length; i++)
            {
                var loc = GD.Load<LocationData>(FishingSim.World.LakeBed.LocationPaths[i]);
                sheet.BlitRect(FishingSim.Graphics.Postcard.Paint(loc, 10f), new Rect2I(0, 0, 320, 180), new Vector2I(0, i * 180));
                sheet.BlitRect(FishingSim.Graphics.Postcard.Paint(loc, 18.5f), new Rect2I(0, 0, 320, 180), new Vector2I(320, i * 180));
            }
            sheet.Resize(sheet.GetWidth() * 2, sheet.GetHeight() * 2, Image.Interpolation.Nearest);
            sheet.SavePng(output);
            GD.Print($"saved {output}");
            GetTree().Quit();
            return;
        }

        if (mode == 22)
        {
            // Studio close-up of one detailed fish (arg = catalogue index): the whole fish, then the head up close.
            var root3d = new Node3D();
            GetTree().Root.AddChild(root3d);
            var env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.12f, 0.16f, 0.18f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.55f, 0.62f, 0.7f), AmbientLightEnergy = 0.55f,
                TonemapMode = Godot.Environment.ToneMapper.Aces, SsaoEnabled = true, GlowEnabled = true,
            };
            root3d.AddChild(new WorldEnvironment { Environment = env });
            root3d.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 35, 0), LightEnergy = 1.6f, ShadowEnabled = true });
            root3d.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-15, -150, 0), LightEnergy = 0.7f, LightColor = new Color(0.65f, 0.8f, 1f) });
            var sp = GD.Load<FishSpecies>(FishingSim.Data.SpeciesCatalog.Paths[Math.Clamp(arg, 0, FishingSim.Data.SpeciesCatalog.Paths.Length - 1)]);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var fish = FishModel.Create(sp, sp.MinWeight + (sp.MaxWeight - sp.MinWeight) * 0.5f, detailed: true);
            if (System.Environment.GetEnvironmentVariable("FISHSIM_SHOT_DEBUG") == "1")
                GD.Print($"close-up model built in {watch.ElapsedMilliseconds} ms");
            float sc = 1f / fish.Length;
            fish.Scale = new Vector3(sc, sc, sc);
            fish.RotationDegrees = new Vector3(0, 90, 0);
            fish.Wiggle = 0.5f;
            root3d.AddChild(fish);
            var cam = new Camera3D { Fov = 30f, Current = true, Position = new Vector3(0.02f, 0.05f, 2.1f), RotationDegrees = new Vector3(-2, 0, 0) };
            root3d.AddChild(cam);
            for (int i = 0; i < 900 && !fish.HasDetailedSkin; i++)
                await NextFrame();
            for (int i = 0; i < 10; i++)
                await NextFrame();
            GetViewport().GetTexture().GetImage().SavePng(output);
            // The head, three-quarter view from the front.
            cam.Position = new Vector3(0.62f, 0.06f, 0.42f);
            cam.LookAt(new Vector3(0.36f, 0.0f, 0f));
            fish.Wiggle = 0f;
            for (int i = 0; i < 6; i++)
                await NextFrame();
            GetViewport().GetTexture().GetImage().SavePng(output.Replace(".png", "_head.png"));
            GD.Print($"saved {output} (detailed skin {fish.HasDetailedSkin})");
            if (System.Environment.GetEnvironmentVariable("FISHSIM_SHOT_DEBUG") == "1")
                foreach (var n in fish.FindChildren("*", "MeshInstance3D", true, false))
                    if (n is MeshInstance3D mi)
                        GD.Print($"  {mi.Name} pos {mi.Position} scale {mi.Scale} aabb {mi.GetAabb()} mat {mi.MaterialOverride?.GetType().Name}");
            GetTree().Quit();
            return;
        }
        if (mode == 21)
        {
            // Side views of 20 species (page = arg) in a small studio: body shapes, fins and eyes at a glance.
            var root3d = new Node3D();
            GetTree().Root.AddChild(root3d);
            var env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.16f, 0.2f, 0.23f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.6f, 0.65f, 0.7f), AmbientLightEnergy = 0.7f,
                TonemapMode = Godot.Environment.ToneMapper.Aces,
            };
            root3d.AddChild(new WorldEnvironment { Environment = env });
            root3d.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, 20, 0), LightEnergy = 1.5f });
            var paths = FishingSim.Data.SpeciesCatalog.Paths.Skip(arg * 20).Take(20).ToList();
            for (int i = 0; i < paths.Count; i++)
            {
                var sp = GD.Load<FishSpecies>(paths[i]);
                var fish = FishModel.Create(sp, sp.MinWeight + (sp.MaxWeight - sp.MinWeight) * 0.5f);
                fish.Wiggle = 0f;
                float sc = 1f / fish.Length;
                fish.Scale = new Vector3(sc, sc, sc);
                fish.RotationDegrees = new Vector3(0, 90, 0);
                fish.Position = new Vector3((i % 4) * 1.25f, -(i / 4) * 0.62f, 0);
                root3d.AddChild(fish);
                var label = new Label3D { Text = sp.SpeciesName, FontSize = 40, PixelSize = 0.0022f, Position = fish.Position + new Vector3(0, -0.24f, 0.3f) };
                root3d.AddChild(label);
            }
            root3d.AddChild(new Camera3D
            {
                Projection = Camera3D.ProjectionType.Orthogonal, Size = 3.3f, Current = true,
                Position = new Vector3(1.9f, -1.2f, 5f),
            });
            for (int i = 0; i < 12; i++)
                await NextFrame();
            GetViewport().GetTexture().GetImage().SavePng(output);
            GD.Print($"saved {output}");
            GetTree().Quit();
            return;
        }
        if (mode == 20)
        {
            // Detailed skins of a few species (1024×512 each), stacked: check scales, spots and bars up close.
            string[] ids = { "pstrag", "okon", "karp", "szczupak", "karp_lustrzen", "pstrag_teczowy", "sum", "jesiotr" };
            const int sw = 1024, sh = 512;
            var sheet = Image.CreateEmpty(sw * 2, sh * (ids.Length / 2), false, Image.Format.Rgb8);
            for (int i = 0; i < ids.Length; i++)
            {
                var sp = GD.Load<FishSpecies>($"res://data/fish/{ids[i]}.tres");
                var (albedo, _) = FishingSim.Graphics.FishSkin.PaintNow(sp, sw, sh);
                albedo.ClearMipmaps();
                sheet.BlitRect(albedo, new Rect2I(0, 0, sw, sh), new Vector2I((i % 2) * sw, (i / 2) * sh));
            }
            sheet.SavePng(output);
            GD.Print($"saved {output}");
            GetTree().Quit();
            return;
        }
        if (mode == 19)
        {
            // The fish atlas with one species on the turntable (arg = index in the catalogue).
            var menu = GD.Load<PackedScene>("res://menu.tscn").Instantiate<Menu>();
            GetTree().Root.AddChild(menu);
            await NextFrame();
            var atlas = menu.OpenAtlas();
            await NextFrame();
            var pick = GD.Load<FishSpecies>(FishingSim.Data.SpeciesCatalog.Paths[Math.Clamp(arg, 0, FishingSim.Data.SpeciesCatalog.Paths.Length - 1)]);
            atlas.Select(pick);
            for (int i = 0; i < 600 && !atlas.ModelReady; i++)
                await NextFrame();
            for (int i = 0; i < 20; i++)
                await NextFrame();
            GetViewport().GetTexture().GetImage().SavePng(output);
            GD.Print($"saved {output} (detailed skin: {atlas.ModelReady}) atlas {atlas.Size} menu {menu.Size} shade {atlas.GetChild<Control>(0).Size} {atlas.GetChild(0).GetType().Name}");
            GetTree().Quit();
            return;
        }
        if (mode == 18)
        {
            // Top-down previews of every map, side by side (shapes, bays, islands).
            const int w = 300, h = 240;
            var sheet = Image.CreateEmpty(w * FishingSim.World.LakeBed.LocationPaths.Length, h, false, Image.Format.Rgb8);
            for (int i = 0; i < FishingSim.World.LakeBed.LocationPaths.Length; i++)
            {
                var loc = GD.Load<LocationData>(FishingSim.World.LakeBed.LocationPaths[i]);
                var area = new Rect2(-160f, -195f, 320f, 260f);
                sheet.BlitRect(FishingSim.UI.MapPreview.Render(loc, w, h, area).GetImage(), new Rect2I(0, 0, w, h), new Vector2I(i * w, 0));
            }
            sheet.SavePng(output);
            GD.Print($"saved {output}");
            GetTree().Quit();
            return;
        }
        if (mode == 16)
        {
            // Contact sheet of every item and fish icon (no 3D needed).
            var icons = new List<ImageTexture>();
            var loadout = new FishingSim.Progress.TackleLoadout();
            foreach (FishingSim.Progress.TackleSlot slot in Enum.GetValues(typeof(FishingSim.Progress.TackleSlot)))
                foreach (var item in loadout.Items(slot))
                    icons.Add(FishingSim.Graphics.Icons.For(item));
            var gear = new FishingSim.Progress.GearCatalog();
            foreach (FishingSim.Progress.GearSlot slot in FishingSim.Progress.GearSlotExtensions.All)
                foreach (var item in gear.Items(slot))
                    icons.Add(FishingSim.Graphics.Icons.For(item));
            foreach (var path in FishingSim.Data.SpeciesCatalog.Paths)
                icons.Add(FishingSim.Graphics.Icons.Fish(GD.Load<FishSpecies>(path)));
            const int cols = 10, iconPx = FishingSim.Graphics.Icons.TextureSize, cell = iconPx + 8;
            int rows = (icons.Count + cols - 1) / cols;
            var sheet = Image.CreateEmpty(cols * cell, rows * cell, false, Image.Format.Rgba8);
            sheet.Fill(new Color(0.93f, 0.86f, 0.7f));
            for (int i = 0; i < icons.Count; i++)
            {
                var img = (Image)icons[i].GetImage().Duplicate();
                if (img.HasMipmaps())
                    img.ClearMipmaps();
                sheet.BlendRect(img, new Rect2I(0, 0, iconPx, iconPx), new Vector2I((i % cols) * cell + 4, (i / cols) * cell + 4));
            }
            sheet.SavePng(output);
            GD.Print($"saved {output} ({icons.Count} icons)");
            GetTree().Quit();
            return;
        }

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
                // FISHSIM_SHOT_WEIGHT sets the landed fish's weight (to compare a small and a big one).
                float landed = float.TryParse(System.Environment.GetEnvironmentVariable("FISHSIM_SHOT_WEIGHT"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float wanted) ? wanted : arg > 0 ? 1.2f : 4.7f;
                rod.ForceLand(GD.Load<FishSpecies>($"res://data/fish/{(arg > 0 ? FishingSim.Data.SpeciesCatalog.Paths[arg].GetFile().GetBaseName() : "karp")}.tres"), landed);
                // Wait for the detailed skin painted in the background.
                for (int i = 0; i < 600 && !(rod.HeldFishModel?.HasDetailedSkin ?? true); i++)
                    await NextFrame();
                frames = 3;
            }
        }
        else if (mode == 15)
        {
            // A full-power cast: is the float visible far out?
            rod.CastNow(1f);
            for (int i = 0; i < 240 && rod.State != RodState.Waiting; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            for (int i = 0; i < 60; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            GD.Print($"FLOAT at {main.Player.Camera.GlobalPosition.DistanceTo(rod.Bobber.GlobalPosition):0.0} m");
        }
        else if (mode == 14)
        {
            // Fish line-up: several species side by side in front of the camera (art review).
            string[] ids = { "okon", "szczupak", "pstrag", "karp", "lin", "karas", "ukleja", "wzdrega", "sandacz", "jesiotr", "sum", "palia" };
            for (int i = 0; i < ids.Length; i++)
            {
                var sp = GD.Load<FishSpecies>($"res://data/fish/{ids[i]}.tres");
                var model = FishModel.Create(sp, sp.MinWeight + (sp.MaxWeight - sp.MinWeight) * 0.3f);
                float fit = 0.42f / model.Length;
                model.Scale = Vector3.One * fit;
                model.Wiggle = 0f;
                model.Position = new Vector3(-0.75f + (i % 3) * 0.75f, 0.55f - (i / 3) * 0.37f, -1.6f);
                model.RotationDegrees = new Vector3(0, -90f, 0);
                main.Player.Camera.AddChild(model);
            }
            main.Player.Rig.Visible = false;
            main.Player.Body.Visible = false;
        }
        else if (mode == 13)
        {
            // Performance probe: walk along the shore for a while and report the slowest script frames.
            var times = new List<double>();
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = true });
            for (int i = 0; i < 90; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                times.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0 + Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0);
            }
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = false });
            times.Sort();
            GD.Print($"PERF frames={times.Count} median={times[times.Count / 2]:0.0}ms p95={times[(int)(times.Count * 0.95)]:0.0}ms max={times[^1]:0.0}ms over50={times.Count(x => x > 50)} pos={main.Player.GlobalPosition}\n{FishingSim.Graphics.Perf.Report()}");
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
            rod.Profile.Level = 3;
            var shop = main.GetChildren().OfType<Shop>().First();
            shop.Toggle();
            shop.Tabs.CurrentTab = arg;
            rod.Profile.NotifyChanged();
            await NextFrame();
            shop.SelectCard(1);
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
            {
                var shopUi = main.GetChildren().OfType<Shop>().First();
                rod.Profile.Money = 180;
                rod.Profile.AddCatch(GD.Load<FishSpecies>("res://data/fish/okon.tres"), 0.6f);
                rod.Profile.AddCatch(GD.Load<FishSpecies>("res://data/fish/karp.tres"), 4.2f);
                shopUi.OpenFor((ShopKind)arg);
                await NextFrame();
                shopUi.SelectCard(1);
            }
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
