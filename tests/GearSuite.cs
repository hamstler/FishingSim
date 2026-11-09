using System;
using System.Linq;
using Vector2 = System.Numerics.Vector2;
using System.Threading.Tasks;
using Godot;
using FishingSim.Accounts;
using FishingSim.Core;
using FishingSim.Core.Animation;
using FishingSim.Core.FishAI;
using FishingSim.Core.Gear;
using FishingSim.Core.Market;
using FishingSim.Data;
using FishingSim.Fishing;
using FishingSim.Progress;
using FishingSim.UI;
using FishingSim.World;

namespace FishingSim.Tests;

/// <summary>Simulator expansion: fish market, keepnets, landing nets, accessories, body animation, visible fish.</summary>
public sealed class GearSuite : ITestSuite
{
    public string Name => "gear";

    private static FishSpecies Species(string id) => GD.Load<FishSpecies>($"res://data/fish/{id}.tres");

    private sealed class Bowl : IWaterVolume
    {
        public float DepthAt(float x, float z) => 4f - MathF.Sqrt(x * x + z * z) * 0.25f;
    }

    public async Task Run(TestContext t)
    {
        var carp = Species("karp");
        var roach = Species("ploc");
        var all = new[] { roach, Species("leszcz"), carp, Species("szczupak") };

        t.Section("fish market");
        var market = new FishMarket();
        var lot = new FishLot("Karp", 5f, 25f, 12f);
        int q0 = market.Quote(lot);
        t.Check(q0 > 100 && q0 < 200, $"a 5 kg carp is quoted near the buyer's 125 zł plus premium ({q0} zł)");
        market.Sell(lot);
        t.Check(market.Quote(lot) < q0, "selling floods the market: the next carp is cheaper");
        market.AdvanceHours(48f);
        t.Check(market.Saturation.Count == 0, "demand recovers after two days");
        t.Check(market.Quote(lot with { Weight = 12f }) > market.Quote(lot) * 12 / 5 * 1.3f, "trophy fish earn a bonus");
        t.Check(market.Quote(lot with { Condition = 0f }) < market.Quote(lot) * 0.35f, "dead fish are nearly worthless");
        for (int i = 0; i < 200; i++)
            market.Sell(lot);
        t.Check(market.Demand("Karp") >= market.MinDemand - 1e-5f, "demand never drops below the floor");

        t.Section("keepnet and landing net rules");
        var spec = new KeepnetSpec(10, 15f, 0.05f);
        t.Check(KeepnetModel.Fits(spec, 0, 0f, 5f) && !KeepnetModel.Fits(spec, 9, 12f, 4f) && !KeepnetModel.Fits(spec, 10, 1f, 0.1f), "keepnet capacity by count and weight");
        t.Check(KeepnetModel.Decay(1f, spec, 2f, 0f) > KeepnetModel.Decay(1f, spec, 2f, 1f), "a crowded net tires fish faster");
        t.Check(LandingModel.EscapeChance(0.5f, null, 0.5f) == 0f && LandingModel.EscapeChance(5f, null, 0.3f) > 0.4f, "small fish by hand, big fish need a net");
        t.Check(LandingModel.EscapeChance(5f, new LandingNetSpec(8f, 2f), 0.3f) < 0.02f
                && LandingModel.EscapeChance(12f, new LandingNetSpec(4f, 2f), 0.3f) > 0.5f, "the net must be big enough for the fish");

        t.Section("profile gear, keepnet and market");
        var p = new Profile();
        t.Check(p.Gear.Owns(GearSlot.Keepnet, GearInventory.StarterKeepnet) && p.Gear.EquippedId(GearSlot.LandingNet) == null,
            "a new angler gets a small keepnet but no landing net");
        var catalog = new GearCatalog();
        catalog.Attach(p);
        t.Check(catalog.Items(GearSlot.Keepnet).Count >= 3 && catalog.Items(GearSlot.LandingNet).Count >= 3 && catalog.Items(GearSlot.Accessory).Count >= 3,
            "keepnets, landing nets and accessories load from data/tackle");
        t.Check(p.KeepnetLimit is { MaxFish: 15 } && catalog.LandingNetSpec == null, "the equipped keepnet limits the profile; no net yet");
        var r1 = p.AddCatch(carp, 6f);
        var r2 = p.AddCatch(carp, 5f);
        t.Check(!r1.Released && r2.Released && p.Keepnet.Count == 1 && p.TotalCatches == 2, "a full keepnet releases the fish (XP and journal still count)");
        p.PassTime(3f);
        t.Check(p.Keepnet[0].Condition < 1f, $"fish lose condition in the net ({p.Keepnet[0].Condition:0.00})");
        int money = p.Money;
        int quote = p.MarketQuote(p.Keepnet[0], all);
        int paid = p.SellAtMarket(0, all);
        t.Check(paid == quote && p.Money == money + paid && p.Keepnet.Count == 0, $"selling one fish at the market pays the quote ({paid} zł)");

        p.Money = 1000;
        p.Level = 5;
        var big = catalog.Items(GearSlot.Keepnet).First(i => i.ItemId == "siatka_karpiowa");
        t.Check(catalog.Buy(GearSlot.Keepnet, big) == null && catalog.Buy(GearSlot.Keepnet, big) == "Już to masz", "buying a keepnet once");
        t.Check(catalog.Equip(GearSlot.Keepnet, big) && p.KeepnetLimit is { MaxKg: >= 50f }, "equipping a bigger keepnet raises the limit");
        var glasses = catalog.Items(GearSlot.Accessory).First(i => i.ItemId == "okulary_polaryzacyjne");
        t.Check(!catalog.Has(AccessoryKind.PolarizedGlasses) && catalog.Buy(GearSlot.Accessory, glasses) == null && catalog.Has(AccessoryKind.PolarizedGlasses),
            "accessories work as soon as they are bought");

        p.AddCatch(roach, 0.5f);
        p.Market.Sell(new FishLot("Płoć", 3f, 12f, 0.7f));
        var json = ProfileSerializer.ToJson(p).ToJsonString();
        var loaded = ProfileSerializer.FromJson(json);
        t.Check(loaded.Gear.Owns(GearSlot.Keepnet, "siatka_karpiowa") && loaded.Gear.EquippedId(GearSlot.Keepnet) == "siatka_karpiowa"
                && loaded.Gear.Owns(GearSlot.Accessory, "okulary_polaryzacyjne"), "gear survives save and load");
        t.Check(loaded.Market.Saturation.ContainsKey("Płoć") && loaded.Keepnet.Count == 1, "market state and keepnet survive save and load");
        var old = ProfileSerializer.FromJson("{\"money\": 50, \"keepnet\": [{\"species\": \"Płoć\", \"weight\": 0.4, \"value\": 5}]}");
        t.Check(old.Gear.Owns(GearSlot.Keepnet, GearInventory.StarterKeepnet) && old.Gear.EquippedId(GearSlot.LandingNet) == null
                && old.Keepnet[0].Condition == 1f, "old saves get the starter keepnet and fresh fish");

        t.Section("lifting the fish out");
        t.Check(LiftModel.Tension(0.5f, 0.2f, 1f) < 1.5f && LiftModel.Tension(6f, 0.3f, 1f) > 8f, "a roach lifts easily, a 6 kg carp loads the line past 8 kg");
        float lift = 0f, secs = 0f;
        while (lift < 1f && secs < 10f) { lift = LiftModel.Step(lift, true, 0.5f, 0.2f, 0.05f); secs += 0.05f; }
        t.Check(lift >= 1f && secs < 2.5f, $"a small fish reaches the hand in {secs:0.0} s");
        t.Check(LiftModel.Step(0.5f, false, 0.5f, 0.2f, 0.2f) < 0.5f, "lowering the rod lets the fish sink back");
        var net = p.Gear.EquippedId(GearSlot.LandingNet);
        p.Money += 100;
        var small = catalog.Items(GearSlot.LandingNet).First(i => i.ItemId == "podbierak_maly");
        t.Check(net == null && small.Price > 0 && catalog.Buy(GearSlot.LandingNet, small) == null && catalog.LandingNet?.ItemId == "podbierak_maly",
            "the landing net has to be bought and is used straight away");

        t.Section("body animation");
        var walk = new BodyMotion();
        for (int i = 0; i < 120; i++)
            walk.Update(1f / 60f, BodyMotion.WalkSpeed, true, BodyAction.None);
        t.Check(walk.Footsteps is >= 2 and <= 6, $"about two steps per second while walking ({walk.Footsteps} in 2 s)");
        var still = new BodyMotion();
        for (int i = 0; i < 120; i++)
            still.Update(1f / 60f, 0f, true, BodyAction.None);
        t.Check(still.Footsteps == 0 && MathF.Abs(still.Pose.HeadBobY) < 0.001f, "no steps and no bob when standing");
        var fight = new BodyMotion();
        fight.Update(0.5f, 0f, true, BodyAction.Fight);
        t.Check(fight.Pose.ArmLift > 0.5f, "the rod is raised while fighting a fish");

        t.Section("fish swimming");
        var water = new Bowl();
        var rng = new SystemRng(3);
        var swim = new FishSwimModel(water, rng);
        for (int i = 0; i < 12; i++)
            swim.Spawn("Płoć", 0.3f, new Vector2(rng.Range(-5f, 5f), rng.Range(-5f, 5f)), i % 2, 0.5f, 4f, 0.6f);
        for (int i = 0; i < 3000; i++)
            swim.Step(1f / 30f, new Vector2(100f, 100f), 0f);
        t.Check(swim.Agents.All(a => water.DepthAt(a.Position.X, a.Position.Y) >= FishSwimModel.MinWaterDepth), "fish never swim onto land");
        t.Check(swim.Agents.All(a => a.Depth <= water.DepthAt(a.Position.X, a.Position.Y)), "fish stay above the bottom");
        var threat = swim.Agents[0].Position + new Vector2(1f, 0f);
        float d0 = swim.Agents.Average(a => Vector2.Distance(a.Position, threat));
        for (int i = 0; i < 60; i++)
            swim.Step(1f / 30f, threat, 5f);
        t.Check(swim.Agents.Average(a => Vector2.Distance(a.Position, threat)) > d0, "a running angler scares the fish away");

        await InGame(t);
    }

    private static async Task InGame(TestContext t)
    {
        t.Section("in the game");
        Main.UnlockAllMaps = true;
        Session.Username = "admin";
        Session.MapIndex = 0;
        var main = GD.Load<PackedScene>("res://main.tscn").Instantiate<Main>();
        t.Host.GetTree().Root.AddChild(main);
        await t.NextFrame();
        await t.NextFrame();

        t.Check(main.Fish.Agents.Count > 5, $"the lake is stocked with visible fish ({main.Fish.Agents.Count})");
        var lake = main.GetChildren().OfType<LakeBed>().First();
        t.Check(main.Fish.Agents.All(a => lake.DepthAt(a.Position.X, a.Position.Y) > 0f), "every fish spawns in water");
        main.Fish.VisibleRange = 1000f;
        main.Fish.VisibleDepth = 99f;
        for (int i = 0; i < 3; i++)
            await t.Host.ToSignal(t.Host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        t.Check(main.Fish.VisibleCount == main.Fish.Agents.Count, "fish in range are drawn under the surface");
        var shown = main.Fish.GetChildren().OfType<FishModel>().First(f => f.Visible);
        t.Check(shown.GlobalPosition.Y < Main.WaterY, "visible fish swim below the water line");
        main.Fish.VisibleRange = 0f;
        await t.Host.ToSignal(t.Host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        t.Check(main.Fish.VisibleCount == 0, "fish out of range are hidden");

        t.Check(main.Rod.Gear != null && main.Rod.Profile.KeepnetLimit != null, "the rod knows the landing net and the keepnet limits the profile");
        t.Check(main.Player.Body != null && main.Player.Camera.Current, "first-person body and camera are in place");

        var market = main.Scenery.Interactables.FirstOrDefault(s => s.Kind == InteractKind.Market);
        t.Check(market != null && market.Prompt.Contains("Targ rybny"), "the lake village has a fish market");
        var shop = main.GetChildren().OfType<Shop>().First();
        main.Rod.Profile.AddCatch(Species("leszcz"), 1.5f);
        main.Interactions.Activate(market!);
        int money = main.Rod.Profile.Money;
        shop.SellAllAtMarket();
        t.Check(shop.Visible && main.Rod.Profile.Keepnet.Count == 0 && main.Rod.Profile.Money > money, "the market buys the whole keepnet");
        shop.Close();

        main.QueueFree();
        await t.NextFrame();
        Main.UnlockAllMaps = false;
        Session.Logout();
    }
}
