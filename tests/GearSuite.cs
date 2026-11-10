using System;
using System.Collections.Generic;
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

        t.Section("66 freshwater species");
        var catalogue = SpeciesCatalog.Paths.Select(p => GD.Load<FishSpecies>(p)).ToList();
        var maps = FishingSim.World.LakeBed.LocationPaths.Select(p => GD.Load<LocationData>(p)).ToList();
        string[] baits = { "worm", "corn", "bread", "boilie", "livebait", "maggot", "insect" };
        t.Check(catalogue.Count >= 66 && catalogue.All(s => s != null) && catalogue.Select(s => s.SpeciesName).Distinct().Count() == catalogue.Count,
            $"{catalogue.Count} species load, all with unique names");
        var undescribed = catalogue.Where(s => s.LatinName == "" || s.Description.Length < 40).Select(s => s.SpeciesName).ToList();
        t.Check(undescribed.Count == 0, $"every species has a Latin name and an atlas description {string.Join(", ", undescribed)}");
        // Fish models: every species has its own build and eyes in proportion to the head.
        var oversizedEyes = new List<string>();
        var builds = new Dictionary<string, (float H, float W)>();
        foreach (var sp in catalogue)
        {
            foreach (bool detailed in new[] { false, true })
            {
                var m = FishingSim.Fishing.FishModel.Create(sp, sp.MinWeight, detailed);
                if (m.EyeDiameter > 0.62f * m.HeadHeightAtEye || m.EyeDiameter > 0.12f * m.Length)
                    oversizedEyes.Add($"{sp.SpeciesName} {m.EyeDiameter * 1000f:0.0} mm / head {m.HeadHeightAtEye * 1000f:0.0} mm");
                if (!detailed)
                    builds[sp.SpeciesName] = (m.MaxHeight / m.Length, m.MaxWidth / m.MaxHeight);
                m.Free();
            }
        }
        t.Check(oversizedEyes.Count == 0, $"no fish has eyes bigger than its head allows {string.Join(", ", oversizedEyes.Take(5))}");
        t.Check(catalogue.Count(FishingSim.Fishing.FishBody.HasOwnShape) == catalogue.Count, "every species has a body shape of its own");
        t.Check(builds["Leszcz"].H > builds["Szczupak"].H * 1.8f && builds["Węgorz"].W > builds["Leszcz"].W * 2.5f
                && builds["Karaś złocisty"].H > builds["Płoć"].H * 1.4f && builds["Sum"].W > builds["Karp"].W,
            $"bodies differ: a bream is deep and flat, a pike long, an eel round (H/L bream {builds["Leszcz"].H:0.00}, pike {builds["Szczupak"].H:0.00}; W/H eel {builds["Węgorz"].W:0.00}, bream {builds["Leszcz"].W:0.00})");
        // Close-up detail: painted fins with rays and a translucent membrane, the iris, a swimming shader.
        var perch = catalogue.First(sp => sp.SpeciesName == "Okoń");
        var spiny = FishingSim.Graphics.FinPainter.Paint(perch, FishingSim.Graphics.FinKind.SpinyDorsal, 256, 128);
        int clear = 0, translucent = 0, dark = 0;
        for (int y = 0; y < 128; y += 2)
            for (int x = 0; x < 256; x += 2)
            {
                var px = spiny.GetPixel(x, y);
                if (px.A < 0.05f) clear++;
                else if (px.A < 0.9f) translucent++;
                if (px.A > 0.3f && px.R + px.G + px.B < 0.4f) dark++;
            }
        t.Check(clear > 100 && translucent > 1000 && dark > 50,
            $"a perch's spiny dorsal has a ragged edge, a see-through membrane and its black spot ({clear} clear, {translucent} translucent, {dark} dark samples)");
        var iris = FishingSim.Graphics.EyePainter.For(FishingSim.Fishing.FishBody.IrisOf(catalogue.First(sp => sp.SpeciesName == "Płoć"))).GetImage();
        var pupil = iris.GetPixel(128, 128);
        var irisRing = iris.GetPixel(128 + 85, 128);
        t.Check(pupil.R < 0.05f && irisRing.R > irisRing.B * 2f, "a roach's eye: black pupil in a red iris");
        var close = FishingSim.Fishing.FishModel.Create(perch, 0.5f, detailed: true);
        int shaded = close.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().Count(m => m.MaterialOverride is ShaderMaterial);
        t.Check(shaded >= 6, $"a close-up perch swims with its body and {shaded - 1} fins bending together (shader materials)");
        close.Free();

        // Measured species: the painted model follows the side view of a real fish (FishReference) and is credited.
        var measured = catalogue.Where(FishingSim.Fishing.FishReference.Has).ToList();
        t.Check(measured.Count >= 15, $"{measured.Count} species are lofted to the outline of a real fish");
        if (measured.Count > 0)
        {
            var off = new List<string>();
            foreach (var msp in measured)
            {
                var r = FishingSim.Fishing.FishReference.For(msp)!;
                float axis = r.Axis, deepest = 0f;
                for (int i = 0; i <= 60; i++)
                {
                    var (top, bottom) = r.EdgesAt(i / 60f, axis);
                    deepest = MathF.Max(deepest, top + bottom);
                }
                float mid = MathF.Sqrt(msp.MinWeight * msp.MaxWeight);
                var mm = FishingSim.Fishing.FishModel.Create(msp, mid, false);
                float want = deepest * FishingSim.Fishing.FishModel.Proportions(FishingSim.Fishing.FishModel.Growth(msp, mid)).Depth;
                if (MathF.Abs(mm.MaxHeight / mm.Length - want) > want * 0.08f || r.Author == "" || !r.Licence.Split(';').All(l => r.Credit.Contains(l.Trim())))
                    off.Add($"{msp.SpeciesName} {mm.MaxHeight / mm.Length:0.000} vs {want:0.000}");
                mm.Free();
            }
            t.Check(off.Count == 0, $"their depth matches the measured fish, and the photographer is credited {string.Join(", ", off)}");
        }
        // Hand-built models (tools/fish_model): the close-up perch is the model made from photos of real perch.
        var perchAsset = FishingSim.Fishing.FishAsset.For(perch);
        t.Check(perchAsset != null && perchAsset.Fins.Count == 6 && perchAsset.Eyes.Count == 2 && perchAsset.BodyNormal != null,
            $"the perch has its own model: body with a normal map, {perchAsset?.Fins.Count ?? 0} fins, {perchAsset?.Eyes.Count ?? 0} eyes");
        if (perchAsset != null)
        {
            var pm = FishingSim.Fishing.FishModel.Create(perch, 0.4f, detailed: true);
            int parts = pm.FindChildren("*", nameof(MeshInstance3D), true, false).Count;
            t.Check(parts == 9 && pm.MaxHeight / pm.Length > 0.22f && pm.MaxHeight / pm.Length < 0.32f && pm.EyeDiameter < 0.62f * pm.HeadHeightAtEye,
                $"a close-up perch is built from it ({parts} parts, H/L {pm.MaxHeight / pm.Length:0.00}, eye {pm.EyeDiameter * 1000f:0} mm in a {pm.HeadHeightAtEye * 1000f:0} mm head)");
            pm.Free();
        }
        // Growth: a heavy fish is deeper and thicker for its length than a young one, which has bigger eyes.
        var growCarp = catalogue.First(sp => sp.SpeciesName == "Karp");
        var young = FishingSim.Fishing.FishModel.Create(growCarp, growCarp.MinWeight, false);
        var grown = FishingSim.Fishing.FishModel.Create(growCarp, growCarp.MaxWeight, false);
        t.Check(grown.Length > young.Length * 1.5f && grown.MaxHeight / grown.Length > young.MaxHeight / young.Length * 1.12f
                && grown.MaxWidth / grown.Length > young.MaxWidth / young.Length * 1.2f
                && young.EyeDiameter / young.Length > grown.EyeDiameter / grown.Length * 1.1f,
            $"a carp grows with its weight: {young.Length:0.00} m slim young fish, {grown.Length:0.00} m deep, thick old one (H/L {young.MaxHeight / young.Length:0.000} -> {grown.MaxHeight / grown.Length:0.000})");
        young.Free();
        grown.Free();

        var homeless = catalogue.Where(s => maps.All(m => m.AbundanceOf(s.SpeciesName) <= 0f)).Select(s => s.SpeciesName).ToList();
        t.Check(homeless.Count == 0, $"every species lives on at least one map {string.Join(", ", homeless)}");
        var picky = catalogue.Where(s => baits.All(b => s.AffinityFor(b) <= 0f)).Select(s => s.SpeciesName).ToList();
        t.Check(picky.Count == 0, $"every species takes at least one bait {string.Join(", ", picky)}");
        t.Check(catalogue.All(s => s.MinWeight > 0f && s.MaxWeight > s.MinWeight && s.MaxDepth > s.MinDepth && s.PricePerKg > 0f),
            "weights, depths and prices are sane");
        t.Check(catalogue.Take(4).Select(s => s.SpeciesName).SequenceEqual(new[] { "Płoć", "Leszcz", "Karp", "Szczupak" }), "the first four keep their order");

        // Balance: every species must be a realistic target for some setup the player can buy.
        var hooks = new[] { 2f, 5f, 8f };
        var depths = new[] { 0.5f, 1f, 2f, 3f, 5f, 8f, 12f };
        var periods = new[] { FishingSim.Core.DayPeriod.Day, FishingSim.Core.DayPeriod.Twilight, FishingSim.Core.DayPeriod.Night };
        var best = new Dictionary<string, float>();
        foreach (var map in maps)
            foreach (var baitId in baits)
                foreach (float hook in hooks)
                    foreach (float depth in depths)
                        foreach (var period in periods)
                        {
                            if (depth > map.MaxDepth + 2f)
                                continue;
                            var ctx = new FishingSim.Core.BiteContext(baitId, hook, depth, map, period, FishingSim.Core.Weather.Cloudy);
                            foreach (var c in FishingSim.Core.BiteModel.Chances(catalogue, ctx))
                                best[c.Species.SpeciesName] = MathF.Max(best.GetValueOrDefault(c.Species.SpeciesName), c.Share);
                        }
        var hard = catalogue.Where(sp => best.GetValueOrDefault(sp.SpeciesName) < 0.03f)
            .Select(sp => $"{sp.SpeciesName} {best.GetValueOrDefault(sp.SpeciesName) * 100f:0.0}%").ToList();
        t.Check(hard.Count == 0, $"every species is at least a 3% chance with the right setup {string.Join(", ", hard)}");

        // Fight balance: with the best gear on sale every species of a typical size can be landed.
        var bestGear = new FightGear(9f, 12f, 0.12f, 3.4f, 150f);
        var unlandable = new List<string>();
        foreach (var sp in catalogue)
        {
            float typical = sp.MinWeight + (sp.MaxWeight - sp.MinWeight) * 0.22f;
            int bestWins = 0;
            foreach (float drag in new[] { 0.3f, 0.5f, 0.7f, 0.9f })
            {
                int wins = 0;
                for (int seed = 0; seed < 10; seed++)
                {
                    var model = new FightModel(FishProfile.From(sp, typical), bestGear, new SystemRng(seed + 11), 25f, 0.3f, drag);
                    var o = FightOutcome.Ongoing;
                    for (int i = 0; i < 60 * 600 && o == FightOutcome.Ongoing; i++)
                        o = model.Step(1f / 60f, drag, reeling: true);
                    if (o == FightOutcome.Landed)
                        wins++;
                }
                bestWins = Math.Max(bestWins, wins);
            }
            if (bestWins < 6)
                unlandable.Add($"{sp.SpeciesName} {typical:0.0} kg ({bestWins}/10)");
        }
        t.Check(unlandable.Count == 0, $"every species of a typical size can be landed on the best gear {string.Join(", ", unlandable)}");

        string pikeTips = FishingSim.UI.GameUI.SpeciesTips(catalogue.First(sp => sp.SpeciesName == "Szczupak"));
        string eelTips = FishingSim.UI.GameUI.SpeciesTips(catalogue.First(sp => sp.SpeciesName == "Węgorz"));
        t.Check(pikeTips.Contains("żywiec") && !pikeTips.Contains("pszenica") && eelTips.Contains("nocą"),
            $"the fish atlas gives sensible tips ({pikeTips}; {eelTips})");

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

        t.Section("rod bending");
        FishingSim.Core.Physics.RodBendModel Settled(float power, float action, float load)
        {
            var m = new FishingSim.Core.Physics.RodBendModel(3f, power, action);
            for (int i = 0; i < 600; i++)
                m.Step(1f / 60f, load, 0.3f, -1f);
            return m;
        }
        t.Check(MathF.Abs(Settled(7f, 0.5f, 0f).TotalBend()) < 1e-3f, "an unloaded rod is straight");
        float b05 = MathF.Abs(Settled(7f, 0.5f, 0.5f).TotalBend()), b2 = MathF.Abs(Settled(7f, 0.5f, 2f).TotalBend()), b8 = MathF.Abs(Settled(7f, 0.5f, 8f).TotalBend());
        t.Check(b05 < b2 && b2 < b8 && b8 < 1.35f, $"more pull bends the rod more, up to lining up with the line ({b05 * 57.3f:0}/{b2 * 57.3f:0}/{b8 * 57.3f:0} deg)");
        t.Check(MathF.Abs(Settled(4f, 0.5f, 2f).TotalBend()) > MathF.Abs(Settled(12f, 0.5f, 2f).TotalBend()) * 1.3f, "a light rod bends more than a stiff one under the same load");
        var fast = Settled(7f, 0.1f, 1f);
        var slow = Settled(7f, 0.9f, 1f);
        float ShareInTip(FishingSim.Core.Physics.RodBendModel m)
        {
            float tip = 0f, all = 0f;
            for (int i = 0; i < m.Joints; i++)
            {
                all += MathF.Abs(m.Angles[i]);
                if (i >= m.Joints * 2 / 3) tip += MathF.Abs(m.Angles[i]);
            }
            return tip / all;
        }
        t.Check(ShareInTip(fast) > ShareInTip(slow) * 1.25f, $"a fast rod bends mostly at the tip, a slow one through the blank ({ShareInTip(fast):0.00} vs {ShareInTip(slow):0.00})");
        var ring = Settled(7f, 0.5f, 3f);
        int crossings = 0;
        float prevBend = ring.TotalBend();
        for (int i = 0; i < 180; i++)
        {
            ring.Step(1f / 60f, 0f, 0f, 1f);
            if (MathF.Sign(ring.TotalBend()) != MathF.Sign(prevBend)) crossings++;
            prevBend = ring.TotalBend();
        }
        for (int i = 0; i < 300; i++)
            ring.Step(1f / 60f, 0f, 0f, 1f);
        t.Check(crossings >= 2 && MathF.Abs(ring.TotalBend()) < 0.01f, $"released, the blank springs back, rings and settles ({crossings} swings)");

        t.Section("fight bar");
        t.Check(FightGauge.Reserve(0f, 6f) == 1f && FightGauge.Reserve(6f, 6f) == 0f && MathF.Abs(FightGauge.Reserve(4.5f, 6f) - 0.25f) < 1e-4f,
            "reserve = the share of the tackle's strength still unused");
        t.Check(FightGauge.Position(0f) == 0f && MathF.Abs(FightGauge.Position(0.10f) - 0.07f) < 1e-4f && MathF.Abs(FightGauge.Position(0.25f) - 0.25f) < 1e-4f
                && MathF.Abs(FightGauge.Position(0.5f) - 0.5f) < 1e-4f && FightGauge.Position(1f) == 1f,
            "segments 7/18/25/50% wide for 0-10%, 10-25%, 25-50%, 50-100% of reserve; 25% reserve sits mid-bar");
        t.Check(FightGauge.Zone(0.05f) == 0 && FightGauge.Zone(0.2f) == 1 && FightGauge.Zone(0.3f) == 2 && FightGauge.Zone(0.8f) == 3
                && FightGauge.Advice(0.05f).Contains("Poluzuj") && FightGauge.Advice(0.8f).Contains("dokręcić"),
            "red tells you to ease the drag, green that you can tighten it");

        t.Section("float visibility");
        t.Check(Bobber.VisualScale(3f) == 1f && Bobber.VisualScale(30f) > 4f && Bobber.VisualScale(500f) <= 7f,
            "the float is drawn bigger far away so it stays visible (1x near, ~5x at 30 m, capped)");

        t.Section("procedural sound");
        float Energy(FishingSim.Core.Audio.Synth syn, int n)
        {
            var buf = new float[n];
            syn.Generate(buf);
            return buf.Sum(x => x * x) / n;
        }
        var synth = new FishingSim.Core.Audio.Synth(22050f);
        t.Check(Energy(synth, 4096) == 0f, "silence when nothing is playing");
        synth.Play(FishingSim.Core.Audio.OneShot.Splash);
        float splash = Energy(synth, 2205);
        Energy(synth, 22050);
        t.Check(splash > 1e-4f && synth.ActiveVoices == 0 && Energy(synth, 2048) == 0f, "a splash is heard, then fades to silence");
        synth.Levels.Reel = 1f;
        var reel = new float[22050];
        synth.Generate(reel);
        int clicks = 0;
        for (int i = 1; i < reel.Length; i++)
            if (MathF.Abs(reel[i]) > 0.05f && MathF.Abs(reel[i - 1]) <= 0.05f)
                clicks++;
        t.Check(clicks > 10 && reel.All(x => x is >= -1f and <= 1f), $"the reel ratchet clicks while reeling ({clicks} onsets per second), samples stay in range");
        synth.Levels = default;
        synth.Levels.Wind = 1f;
        synth.Levels.Birds = 1f;
        t.Check(Energy(synth, 22050) > 1e-5f, "wind and birds make an ambience");

        t.Section("bites from the fish you see");
        var lure = new FishSwimModel(water, new SystemRng(5));
        for (int i = 0; i < 8; i++)
            lure.Spawn(i < 4 ? "Płoć" : "Szczupak", 0.4f, new Vector2(-6f + i * 0.3f, 0f), i, 0.5f, 3f, 0.6f);
        var bait = new Vector2(5f, 0f);
        lure.Attractor = bait;
        lure.Interest = a => a.Species == "Płoć" ? 1f : 0f;
        float roachBefore = lure.Agents.Where(a => a.Species == "Płoć").Average(a => Vector2.Distance(a.Position, bait));
        float pikeBefore = lure.Agents.Where(a => a.Species == "Szczupak").Average(a => Vector2.Distance(a.Position, bait));
        for (int i = 0; i < 300; i++)
            lure.Step(1f / 30f, new Vector2(100f, 100f), 0f);
        float roachAfter = lure.Agents.Where(a => a.Species == "Płoć").Average(a => Vector2.Distance(a.Position, bait));
        float pikeAfter = lure.Agents.Where(a => a.Species == "Szczupak").Average(a => Vector2.Distance(a.Position, bait));
        t.Check(roachAfter < roachBefore * 0.6f && roachAfter < pikeAfter, $"fish that like the bait swim to it (roach {roachBefore:0.0} -> {roachAfter:0.0} m, pike {pikeBefore:0.0} -> {pikeAfter:0.0} m)");
        var taker = lure.PickNear(bait, 7f, a => a.Species == "Płoć" ? 1f : 0f);
        t.Check(taker != null && taker.Species == "Płoć", "the fish that takes the bait is one of those near it");
        foreach (var a in lure.Agents)
            a.Fear = 3f;
        t.Check(lure.PickNear(bait, 7f, _ => 1f) == null, "frightened fish do not bite");
        foreach (var a in lure.Agents)
        {
            a.Fear = 0f;
            a.Hooked = true;
        }
        t.Check(lure.PickNear(bait, 7f, _ => 1f) == null, "a hooked fish cannot bite twice");

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

        t.Check(main.Lantern == null, "no lantern until one is bought");
        main.Rod.Profile.Money += 100;
        var lanternItem = main.Gear.Items(GearSlot.Accessory).First(i => i.ItemId == "latarka_czolowa");
        t.Check(main.Gear.Buy(GearSlot.Accessory, lanternItem) == null && main.Lantern != null, "a bought lantern hangs at the angler's belt");
        var worldState = main.GetChildren().OfType<WorldState>().First();
        worldState.Hour = 23f;
        await t.NextFrame();
        bool litAtNight = main.Lantern!.Visible && main.Lantern.LightEnergy > 0.5f;
        worldState.Hour = 12f;
        await t.NextFrame();
        t.Check(litAtNight && !main.Lantern.Visible, "the lantern burns at night and is out by day");
        t.Check(main.Rod.Population == main.Fish, "the rod takes its bites from the fish in the water");
        var rod = main.Rod;
        rod.DevSpeciesIndex = -1;
        rod.DevInstantBite = true;
        int splashes = 0, bells = 0;
        rod.SoundCue += (cue, _) =>
        {
            if (cue == FishingSim.Core.Audio.OneShot.Splash) splashes++;
            if (cue == FishingSim.Core.Audio.OneShot.Bell) bells++;
        };
        var pod = main.Gear.Items(GearSlot.Accessory).First(i => i.ItemId == "podporka");
        rod.Profile.Level = Math.Max(rod.Profile.Level, pod.RequiredLevel);
        rod.Profile.Money += 100;
        t.Check(main.Gear.Buy(GearSlot.Accessory, pod) == null, "the rod rest with a bell can be bought");
        rod.CastNow(0.35f);
        for (int i = 0; i < 360 && rod.State != RodState.Waiting; i++)
            await t.Host.ToSignal(t.Host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        var bob = rod.Bobber.GlobalPosition;
        var roachAgent = main.Fish.Agents.FirstOrDefault(a => a.Species == "Płoć");
        if (roachAgent != null)
        {
            foreach (var a in main.Fish.Agents.Where(a => a != roachAgent))
                a.Position = new Vector2(bob.X + 20f, bob.Z - 15f);     // everyone else out of reach of the float
            roachAgent.Position = new Vector2(bob.X + 0.5f, bob.Z);
            roachAgent.Fear = 0f;
        }
        for (int i = 0; i < 360 && rod.State == RodState.Waiting; i++)
        {
            if (roachAgent != null)
            {
                roachAgent.Position = new Vector2(bob.X + 0.5f, bob.Z);
                foreach (var a in main.Fish.Agents.Where(a => a != roachAgent))
                    a.Position = new Vector2(bob.X + 20f, bob.Z - 15f);
            }
            await t.Host.ToSignal(t.Host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        t.Check(splashes == 1, "the float lands with a splash sound");
        t.Check(bells == 1, "the bell rings when the fish touches the bait");
        t.Check(main.GetChildren().OfType<FishingSim.Audio.SoundEngine>().Any(), "the sound engine runs in the game");
        t.Check(roachAgent != null && rod.Biter == roachAgent && rod.CurrentSpecies?.SpeciesName == "Płoć" && rod.CurrentWeight == roachAgent.Weight,
            "the roach next to the float is the fish that bites (same weight too)");
        rod.ResetToIdle();
        t.Check(roachAgent != null && !roachAgent.Hooked && roachAgent.Fear > 0f && rod.Biter == null, "when the line comes in, the fish swims off in fright");
        rod.DevInstantBite = false;

        // The stool: resting while waiting makes time run faster.
        var stool = main.Gear.Items(GearSlot.Accessory).First(i => i.ItemId == "krzeselko");
        main.Rod.Profile.Money += 200;
        main.Rod.Profile.Level = Math.Max(main.Rod.Profile.Level, stool.RequiredLevel);
        t.Check(main.Gear.Buy(GearSlot.Accessory, stool) == null, "the stool can be bought");
        rod.CastNow(0.35f);
        for (int i = 0; i < 360 && rod.State != RodState.Waiting; i++)
            await t.Host.ToSignal(t.Host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        await t.Seconds(3.6);
        t.Check(rod.State != RodState.Waiting || (main.Resting && worldState.TimeScale > 1f), "sitting still on the stool while waiting speeds up time");
        rod.ResetToIdle();
        await t.NextFrame();
        await t.NextFrame();
        t.Check(!main.Resting && worldState.TimeScale == 1f, "time runs normally again when the line comes in");
        t.Check(main.Rod.Gear != null && main.Rod.Profile.KeepnetLimit != null, "the rod knows the landing net and the keepnet limits the profile");
        t.Check(main.Player.Body != null && main.Player.Camera.Current, "first-person body and camera are in place");

        // Releasing a catch with R (the catch panel's "Do wody").
        var prof = main.Rod.Profile;
        int netBefore = prof.Keepnet.Count;
        main.Rod.ForceLand(Species("ploc"), 0.2f);
        int xpBefore = prof.Xp;
        main.Rod._UnhandledInput(new InputEventKey { Keycode = Godot.Key.R, Pressed = true });
        t.Check(prof.Keepnet.Count == netBefore && prof.Xp == xpBefore + Profile.ReleaseXp && main.Rod.HeldFish == null && !main.Rod.AwaitingDecision,
            "R after landing puts the fish back into the water (never in the keepnet, a little XP)");
        main.Rod._UnhandledInput(new InputEventKey { Keycode = Godot.Key.E, Pressed = true });
        t.Check(prof.Keepnet.Count == netBefore, "once decided, the fish cannot be kept as well");
        main.Rod.ResetToIdle();

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
