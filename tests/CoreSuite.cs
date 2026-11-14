using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishingSim.Core;
using FishingSim.Data;
using Godot;
using FishingSim.Core.Camp;
using FishingSim.Progress;
using FishingSim.Core.Methods;

namespace FishingSim.Tests;

/// <summary>Bite model, fight model and progression maths (pure logic, no scene needed).</summary>
public sealed class CoreSuite : ITestSuite
{
    public string Name => "core";

    private static FishSpecies Species(string id) => GD.Load<FishSpecies>($"res://data/fish/{id}.tres");

    public Task Run(TestContext t)
    {
        var roach = Species("ploc");
        var bream = Species("leszcz");
        var carp = Species("karp");
        var pike = Species("szczupak");
        var all = new[] { roach, bream, carp, pike };
        var lake = GD.Load<LocationData>("res://data/locations/jezioro.tres");
        var pond = GD.Load<LocationData>("res://data/locations/staw.tres");

        t.Section("progression");
        t.Check(Progression.XpForLevel(1) == 0 && Progression.XpForLevel(2) == 60 && Progression.XpForLevel(3) == 240, "XP thresholds: 0, 60, 240");
        t.Check(Progression.FishValue(roach, 0.8f) == 10, "0.8 kg roach sells for 10 zł");
        t.Check(Progression.FishValue(carp, 5f) == 125, "5 kg carp sells for 125 zł");
        t.Check(Progression.FishXp(carp, 14f) > Progression.FishXp(roach, 0.4f) * 5, "a big carp is worth far more XP than a small roach");

        CampLife(t, roach, carp);

        t.Section("data resources load with their C# types");
        t.Check(roach.SpeciesName == "Płoć" && carp.PricePerKg == 25f, "species .tres values arrive in the C# properties");
        t.Check(carp.AffinityFor("boilie") == 2f && carp.AffinityFor("nonsense") == 0f, "bait affinity lookup");
        t.Check(pike.WeatherMultiplier("rain") == 1.3f && pike.WeatherMultiplier("hail") == 1f, "weather multiplier lookup with default");
        t.Check(lake.AbundanceOf("Karp") == 0.6f && lake.AbundanceOf("Pstrąg potokowy") == 0f, "location abundance lookup");
        t.Check(lake.OutlineRx == 120f && lake.HolePos == new Vector2(12, -40), "location numbers and vectors");
        var teleskop = GD.Load<RodData>("res://data/tackle/rods/teleskop.tres");
        t.Check(teleskop.ItemId == "teleskop" && teleskop.Price == 120 && teleskop.RequiredLevel == 2, "tackle id comes from the file name");

        t.Section("bite model");
        var worm = new BiteContext("worm", 5f, 3f, lake, DayPeriod.Day, Weather.Clear);
        t.Near(BiteModel.HookFit(carp, 7f), 1f, 0.001f, "a perfectly sized hook fits 100%");
        t.Near(BiteModel.HookFit(roach, 8f), 0.05f, 0.001f, "a hook 6 sizes too big fits 5%");
        t.Near(BiteModel.DepthFit(carp, 3f), 1f, 0.001f, "inside the depth range");
        t.Near(BiteModel.DepthFit(roach, 6f), 0.2f, 0.001f, "2 m too deep costs 80% (roach, max 4 m)");
        t.Near(BiteModel.TimeMultiplier(pike, DayPeriod.Night), 0.2f, 0.001f, "pike are sluggish at night");
        t.Check(BiteModel.TimeMultiplier(pike, DayPeriod.Twilight) > BiteModel.TimeMultiplier(pike, DayPeriod.Day), "pike peak at dawn and dusk");
        t.Check(BiteModel.Weight(pike, worm with { BaitId = "corn" }) == 0f, "corn never attracts pike");

        var live = new BiteContext("livebait", 8f, 3f, lake, DayPeriod.Day, Weather.Clear);
        var chances = BiteModel.Chances(all, live);
        t.Check(chances[0].Species == pike && chances[0].Share > 0.99f, "live bait only attracts pike");
        var chances2 = BiteModel.Chances(all, worm);
        t.Near(chances2.Sum(c => c.Share), 1f, 0.001f, "shares add up to 100%");
        t.Check(chances2.Zip(chances2.Skip(1), (a, b) => a.Share >= b.Share).All(x => x), "shares are sorted descending");
        t.Check(BiteModel.Chances(all, worm with { BaitId = "nothing" }).All(c => c.Share == 0f), "unknown bait: nothing is interested");

        float carpLake = BiteModel.Weight(carp, new BiteContext("corn", 7f, 3f, lake, DayPeriod.Day));
        float carpPond = BiteModel.Weight(carp, new BiteContext("corn", 7f, 3f, pond, DayPeriod.Day));
        t.Check(carpPond > carpLake * 3f, "the carp pond has far more carp than the lake");
        float night = BiteModel.Weight(bream, new BiteContext("worm", 4f, 3f, lake, DayPeriod.Night));
        float day = BiteModel.Weight(bream, new BiteContext("worm", 4f, 3f, lake, DayPeriod.Day));
        t.Check(night > day, "bream bite better at night");
        t.Check(BiteModel.Attraction(all, worm) > BiteModel.Attraction(all, worm with { BaitId = "boilie" }), "worms are more generally attractive than boilies");
        var rng = new SystemRng(1);
        float fast = Enumerable.Range(0, 200).Average(_ => BiteModel.WaitSeconds(rng, 4f, 12f, 1f, 2f));
        float slow = Enumerable.Range(0, 200).Average(_ => BiteModel.WaitSeconds(rng, 4f, 12f, 1f, 0.2f));
        t.Check(slow > fast * 5f, "a hopeless bait makes bites ~10x slower");

        var picks = new Dictionary<string, int>();
        var prng = new SystemRng(7);
        for (int i = 0; i < 3000; i++)
        {
            var p = BiteModel.Pick(all, new BiteContext("worm", 5f, 2.5f, lake, DayPeriod.Day), prng);
            picks[p!.SpeciesName] = picks.GetValueOrDefault(p.SpeciesName) + 1;
        }
        t.Check(picks["Płoć"] > picks["Leszcz"] && picks["Leszcz"] > picks["Karp"] && picks.GetValueOrDefault("Szczupak") < picks["Karp"], "pick frequencies follow the weights (roach > bream > carp > pike)");
        t.Check(BiteModel.Pick(all, worm with { BaitId = "nothing" }, prng) == null, "Pick returns null when nothing is interested");

        float smallHook = Enumerable.Range(0, 3000).Average(_ => BiteModel.RollWeight(carp, 2f, prng));
        float largeHook = Enumerable.Range(0, 3000).Average(_ => BiteModel.RollWeight(carp, 9f, prng));
        t.Check(largeHook > smallHook * 1.2f && smallHook >= carp.MinWeight && largeHook <= carp.MaxWeight, $"bigger hooks bring bigger fish ({smallHook:0.0} kg vs {largeHook:0.0} kg)");

        t.Section("fight model");
        if (OS.HasEnvironment("FISHSIM_TRACE"))
        {
            var cg = new FightGear(9f, 12f, 0.12f, 3.4f, 150f);
            var tr = new FightModel(FishProfile.From(carp, 12f), cg, new SystemRng(6), 25f, 0.3f, 0.5f);
            for (int i = 0; i < 60 * 200; i++)
            {
                var o = tr.Step(1f / 60f, 0.5f, true);
                if (i % 60 == 0 || o != FightOutcome.Ongoing)
                    GD.Print($"t={tr.Elapsed:0} line={tr.LineOut:0.0} dist={tr.Distance:0.0} st={tr.Stamina:0.00} run={tr.Running} T={tr.Tension:0.0} slip={tr.Slipping} spd={tr.Speed:0.0} head={tr.Heading:0.0} ang={tr.FishAngle:0.0}");
                if (o != FightOutcome.Ongoing) break;
            }
        }
        var teleGear = new FightGear(DragMax: 4f, EffectiveLimit: 5f, RodShock: 0.2f, ReelSpeed: 3f, MaxLine: 100f);
        var juniorGear = new FightGear(DragMax: 2f, EffectiveLimit: 2.4f, RodShock: 0.3f, ReelSpeed: 1.9f, MaxLine: 60f);

        var (outcome, secs) = Fight(roach, 0.8f, teleGear, 0.5f, seed: 3);
        t.Check(outcome == FightOutcome.Landed && secs is > 12f and < 90f, $"a roach on balanced gear puts up a fight but is landed ({secs:0} s)");

        (outcome, _) = Fight(carp, 12f, new FightGear(9f, 4f, 0.3f, 3.4f, 150f), 0.6f, seed: 4);
        t.Check(outcome == FightOutcome.LineBroke, "a drag set above the line strength breaks the line");

        (outcome, secs) = Fight(carp, 12f, new FightGear(5f, 6f, 0.2f, 3f, 80f), 0.05f, seed: 5);
        t.Check(outcome == FightOutcome.SpooledOut, "a nearly open drag lets the fish take all the line");

        var carpGear = new FightGear(DragMax: 9f, EffectiveLimit: 12f, RodShock: 0.12f, ReelSpeed: 3.4f, MaxLine: 150f);
        (outcome, secs) = Fight(carp, 12f, carpGear, 0.5f, seed: 6);
        t.Check(outcome == FightOutcome.Landed && secs > 55f, $"a 12 kg carp is a long fight but is landed on carp tackle with a sensible drag ({secs:0} s)");
        var tele = Enumerable.Range(0, 8).Select(i => Fight(carp, 12f, teleGear, 0.5f, seed: 40 + i)).ToList();
        float teleLanded = tele.Count(r => r.Outcome == FightOutcome.Landed) / 8f;
        float teleTime = tele.Where(r => r.Outcome == FightOutcome.Landed).Select(r => r.Seconds).DefaultIfEmpty(999f).Average();
        t.Check(teleLanded < 0.7f || teleTime > secs * 1.5f,
            $"the same carp is too much for a telescope rod (landed {teleLanded * 100f:0}%, avg {teleTime:0} s vs {secs:0} s on carp tackle)");

        var model = new FightModel(FishProfile.From(carp, 12f), new FightGear(9f, 4f, 0.3f, 3.4f, 150f), new SystemRng(8), 20f, 0f, 0.6f) { Unbreakable = true };
        bool ever = false;
        for (int i = 0; i < 3000 && !ever; i++)
            ever = model.Step(1f / 60f, 0.6f, true) == FightOutcome.LineBroke;
        t.Check(!ever, "an unbreakable line never breaks (dev flag)");

        var f = new FightModel(FishProfile.From(roach, 0.5f), teleGear, new SystemRng(1), 20f, 0f, 0.5f);
        t.Near(f.Fatigue, 1f, 0.001f, "fatigue x1 at the start");
        for (int i = 0; i < (int)(FightModel.FatigueSeconds * 60); i++) f.Step(1f / 60f, 0.5f, false);
        t.Near(f.Fatigue, 2f, 0.05f, $"fatigue x2 after {FightModel.FatigueSeconds:0} s");

        var a = TraceLine(carp, 12f, teleGear, seed: 11);
        var b = TraceLine(carp, 12f, teleGear, seed: 11);
        var c = TraceLine(carp, 12f, teleGear, seed: 12);
        t.Check(a == b, "the same seed gives an identical fight");
        t.Check(a != c, "a different seed gives a different fight");

        var times = Enumerable.Range(0, 12).Select(seed => Fight(carp, 12f, juniorGear, 0.6f, seed + 100)).ToList();
        t.Check(times.All(r => r.Outcome != FightOutcome.Ongoing), $"fights on weak gear end (slowest {times.Max(r => r.Seconds):0} s)");
        t.Check(times.Where(r => r.Outcome == FightOutcome.Landed).All(r => r.Seconds < 480f), "fatigue keeps the longest weak-gear fight under 8 minutes");

        // Realistic swimming: limited speed and turning, held on the line, facing away while it runs.
        var pikeProfile = FishProfile.From(pike, 4f);
        var swim = new FightModel(pikeProfile, teleGear, new SystemRng(31), 20f, 0.2f, 0.5f) { Unbreakable = true };
        float fastest = 0f, widest = 0f, offLine = 0f, awayWhileRunning = 0f;
        int runningSteps = 0;
        var lastPos = swim.Position;
        float lastHeading = swim.Heading, sharpest = 0f;
        for (int i = 0; i < 60 * 60; i++)
        {
            if (swim.Step(1f / 60f, 0.5f, reeling: i % 240 < 150) != FightOutcome.Ongoing)
                break;
            fastest = MathF.Max(fastest, (swim.Position - lastPos).Length() * 60f);
            var radial = swim.Position.Normalized();
            widest = MathF.Max(widest, MathF.Abs((swim.Position - lastPos).Dot(new Vector2(radial.Y, -radial.X))) * 60f);
            offLine = MathF.Max(offLine, swim.Distance - swim.LineOut);
            sharpest = MathF.Max(sharpest, MathF.Abs(Mathf.Wrap(swim.Heading - lastHeading, -MathF.PI, MathF.PI)) * 60f);
            if (swim.Running)
            {
                runningSteps++;
                awayWhileRunning += MathF.Cos(swim.Heading - swim.FishAngle);
            }
            lastPos = swim.Position;
            lastHeading = swim.Heading;
        }
        t.Check(fastest <= pikeProfile.BurstSpeed * 1.25f && pikeProfile.BurstSpeed is > 1.5f and < 4.5f,
            $"a 4 kg pike never moves faster than its burst speed ({fastest:0.0} m/s, burst {pikeProfile.BurstSpeed:0.0})");
        t.Check(widest <= pikeProfile.BurstSpeed, $"it swings across the line no faster than it can swim ({widest:0.0} m/s), no jumping from place to place");
        t.Check(sharpest <= pikeProfile.TurnRate * 1.45f, $"it turns at a limited rate ({sharpest:0.0} rad/s)");
        t.Check(offLine < 0.05f, "the line holds it: it is never further than the line paid out");
        t.Check(runningSteps > 0 && awayWhileRunning / runningSteps > 0.3f, "while it runs it heads away from the angler");
        float bigBurst = FishProfile.From(carp, 12f).BurstSpeed, smallBurst = FishProfile.From(roach, 0.3f).BurstSpeed;
        t.Check(bigBurst > smallBurst * 1.8f && FishProfile.From(carp, 12f).TurnRate < FishProfile.From(roach, 0.3f).TurnRate,
            $"a big fish is faster but turns slower than a small one ({bigBurst:0.0} vs {smallBurst:0.0} m/s)");

        // The fight starts at the strike: the fish bolts at once and loads light starter tackle hard.
        var strike = new FightModel(FishProfile.From(bream, 1.5f), juniorGear, new SystemRng(21), 15f, 0f, 0.5f);
        float peakLoad = 0f, startLine = strike.LineOut;
        for (int i = 0; i < 120; i++)
        {
            strike.Step(1f / 60f, 0.5f, reeling: true);
            peakLoad = MathF.Max(peakLoad, strike.Tension / juniorGear.EffectiveLimit);
        }
        t.Check(strike.Running && strike.LineOut > startLine + 0.5f && peakLoad > 0.4f,
            $"a 1.5 kg bream on starter tackle bolts at the strike and takes line ({strike.LineOut - startLine:0.0} m, load {peakLoad * 100f:0}%)");
        var tiny = new FightModel(FishProfile.From(roach, 0.4f), juniorGear, new SystemRng(22), 15f, 0f, 0.5f);
        float tinyLoad = 0f;
        for (int i = 0; i < 180; i++)
        {
            tiny.Step(1f / 60f, 0.5f, reeling: true);
            tinyLoad = MathF.Max(tinyLoad, tiny.Tension / juniorGear.EffectiveLimit);
        }
        t.Check(tinyLoad > 0.3f, $"even a 0.4 kg roach loads the starter rod noticeably ({tinyLoad * 100f:0}%)");

        var small = Enumerable.Range(0, 10).Select(s => Fight(pike, 1.2f, teleGear, 0.5f, s + 200).Seconds).Average();
        var big = Enumerable.Range(0, 10).Select(s => Fight(pike, 7.5f, teleGear, 0.5f, s + 300).Seconds).Average();
        t.Check(big > small * 1.5f, $"big fish fight longer ({small:0} s vs {big:0} s for pike)");
        return Task.CompletedTask;
    }

    private static (FightOutcome Outcome, float Seconds) Fight(FishSpecies sp, float weight, FightGear gear, float drag, int seed)
    {
        var model = new FightModel(FishProfile.From(sp, weight), gear, new SystemRng(seed), 25f, 0.3f, drag);
        const float dt = 1f / 60f;
        for (int i = 0; i < 60 * 600; i++)
        {
            var o = model.Step(dt, drag, reeling: true);
            if (o != FightOutcome.Ongoing)
                return (o, model.Elapsed);
        }
        return (FightOutcome.Ongoing, model.Elapsed);
    }

    private static string TraceLine(FishSpecies sp, float weight, FightGear gear, int seed)
    {
        var model = new FightModel(FishProfile.From(sp, weight), gear, new SystemRng(seed), 25f, 0.3f, 0.5f);
        for (int i = 0; i < 600; i++)
            model.Step(1f / 60f, 0.5f, true);
        return $"{model.LineOut:0.0000}/{model.Tension:0.0000}/{model.Stamina:0.0000}";
    }

    /// <summary>Digging, the insect net, cooking and meals (Core/Camp).</summary>
    private static void CampLife(TestContext t, FishSpecies roach, FishSpecies carp)
    {
        t.Section("camp life: digging, insects, cooking");
        var rng = new FishingSim.Core.SystemRng(7);
        int Total(GroundKind g, float hour, Weather w, string id)
        {
            int n = 0;
            for (int i = 0; i < 300; i++)
                n += Digging.Dig(g, hour, w, false, rng).Baits.GetValueOrDefault(id);
            return n;
        }
        t.Check(Digging.Dig(GroundKind.Path, 12f, Weather.Clear, false, rng).Total == 0
                && Digging.Dig(GroundKind.Rock, 12f, Weather.Clear, false, rng).Total == 0,
            "a trodden path or rock gives no bait");
        t.Check(Enumerable.Range(0, 50).All(_ => Digging.Dig(GroundKind.Mud, 2f, Weather.Rain, true, rng).Total == 0),
            "a spot already dug gives nothing again");
        int mud = Total(GroundKind.Mud, 12f, Weather.Clear, Digging.Worm), sand = Total(GroundKind.Sand, 12f, Weather.Clear, Digging.Worm);
        int dry = Total(GroundKind.Meadow, 12f, Weather.Clear, Digging.Worm), wet = Total(GroundKind.Meadow, 12f, Weather.Rain, Digging.Worm);
        t.Check(mud > sand * 4 && wet > dry, $"worms: a damp bank beats sand ({mud} vs {sand}), rain brings them up ({wet} vs {dry})");
        t.Check(Total(GroundKind.Meadow, 12f, Weather.Clear, Digging.NightCrawler) == 0 && Total(GroundKind.Meadow, 23f, Weather.Rain, Digging.NightCrawler) > 50,
            "night crawlers only at night or after rain");
        t.Check(Total(GroundKind.Forest, 12f, Weather.Clear, Digging.Grub) > 60 && Total(GroundKind.Meadow, 12f, Weather.Clear, Digging.Grub) == 0,
            "grubs come from the forest floor");

        t.Check(Sweeping.MayflyActivity(12f, Weather.Clear) == 0f && Sweeping.MayflyActivity(19f, Weather.Clear) > 0.9f
                && Sweeping.MayflyActivity(19f, Weather.Rain) == 0f, "mayflies dance over the water at dusk, not at noon or in rain");
        int flies = 0, hoppers = 0;
        for (int i = 0; i < 200; i++)
        {
            flies += Sweeping.Sweep(new SweepContext(GroundKind.Sand, true, false, 19f, Weather.Clear, 1f), rng).Baits.GetValueOrDefault(Sweeping.Mayfly);
            hoppers += Sweeping.Sweep(new SweepContext(GroundKind.Meadow, false, false, 13f, Weather.Clear, 0f), rng).Baits.GetValueOrDefault(Sweeping.Grasshopper);
        }
        t.Check(flies > 300 && hoppers > 100, $"a swarm fills the net with mayflies ({flies}), a warm meadow gives grasshoppers ({hoppers})");
        t.Check(Sweeping.Sweep(new SweepContext(GroundKind.Meadow, true, true, 19f, Weather.Rain, 1f), rng).Total == 0, "rain grounds every insect");
        foreach (var id in new[] { "jetka", "pedrak", Digging.NightCrawler, Sweeping.Grasshopper, Sweeping.Beetle })
            t.Check(GD.Load<BaitData>($"res://data/tackle/baits/{id}.tres") != null, $"bait {id} exists");

        var fish = new List<Cooking.Ingredient>
        {
            new(0, 2.4f, 60, false), new(1, 0.3f, 4, false), new(2, 0.08f, 1, false), new(3, 0.5f, 6, true),
        };
        t.Check(Cooking.Pick(Cooking.Roast, fish) is [3], "a roast takes the cheapest fish big enough to eat, undersized first");
        t.Check(Cooking.Missing(Cooking.Soup, fish, hasPot: false) != null && Cooking.Pick(Cooking.Soup, fish) is { Count: >= 2 } soup
                && !soup.Contains(0), "soup needs the pot and two or more fish, sparing the valuable one");
        t.Check(Cooking.Missing(Cooking.Roast, new List<Cooking.Ingredient>(), true) != null, "nothing to cook from an empty keepnet");

        var p = new Profile();
        p.Keepnet.Add(new CatchRecord("Płoć", 0.3f, 4));
        p.Keepnet.Add(new CatchRecord("Karp", 3f, 75));
        int xpPlain = p.AddCatch(roach, 0.4f, toKeepnet: false).Xp;
        var used = p.Cook(Cooking.Roast, hasPot: false, out var error);
        t.Check(error == null && used is { Count: 1 } && used[0].Species == "Płoć" && p.Keepnet.Count == 1 && p.ActiveMeal?.Name == "Syty",
            "cooking takes the roach from the keepnet and the meal starts");
        int xpFed = p.AddCatch(roach, 0.4f, toKeepnet: false).Xp;
        t.Check(xpFed > xpPlain, $"a fed angler gains more XP ({xpFed} vs {xpPlain})");
        var json = ProfileSerializer.ToJson(p).ToJsonString();
        t.Check(ProfileSerializer.FromJson(json).ActiveMeal?.Name == "Syty", "the meal survives a save");
        p.PassTime(Cooking.Roast.Buff.Hours + 0.1f);
        t.Check(p.ActiveMeal == null, "the meal wears off after its hours");
        p.AddBait(new Dictionary<string, int> { ["jetka"] = 3 });
        t.Check(p.BaitCounts.GetValueOrDefault("jetka") == 3, "gathered bait goes into the bait box");

        LiftNetForestSmoke(t, roach);
        FishingMethods(t, Species("szczupak"), Species("leszcz"));
        TackleCatalogue(t);
        LineRopePhysics(t);
        Noise(t);
        Feeding(t, GD.Load<LocationData>("res://data/locations/jezioro.tres"));
    }

    /// <summary>The lift net, mushrooms and berries, the smokehouse and the notice board (Core/Camp).</summary>
    private static void LiftNetForestSmoke(TestContext t, FishSpecies roach)
    {
        t.Section("camp life: lift net, forest, smokehouse, notices");
        var rng = new FishingSim.Core.SystemRng(11);
        int Lifts(LiftContext c)
        {
            int n = 0;
            for (int i = 0; i < 200; i++)
                n += LiftNetting.Lift(c, rng).Baits.GetValueOrDefault(LiftNetting.LiveBait);
            return n;
        }
        var dusk = new LiftContext(20f, 1f, 19f, Weather.Clear, 0.5f, false);
        int good = Lifts(dusk), quick = Lifts(dusk with { SoakSeconds = 2f }), dry = Lifts(dusk with { Depth = 0.08f });
        int night = Lifts(dusk with { Hour = 2f }), baited = Lifts(dusk with { Groundbait = true });
        t.Check(good > 300 && quick == 0 && dry == 0, $"a soaked lift net at dusk brings live bait ({good}); lifted at once or on dry land none");
        t.Check(night < good / 2 && baited > good, $"few fish at night ({night}), more over crumbled bread ({baited})");
        t.Check(Enumerable.Range(0, 100).All(_ => LiftNetting.Lift(dusk with { Groundbait = true, Cover = 1f }, rng).Total <= LiftNetting.MaxFish),
            "a lift holds a handful of fish at most");
        t.Check(LiftNetting.Lift(dusk, rng) is { Total: > 0 } h && h.Message.Contains("żywce"), "the message names the fish and the live bait");

        int porcini = 0, chant = 0, berries = 0;
        for (int i = 0; i < 100; i++)
        {
            porcini += Foraging.Pick(ForageKind.Porcini, false, rng).Baits[Foraging.Porcini];
            chant += Foraging.Pick(ForageKind.Chanterelle, false, rng).Baits[Foraging.Chanterelle];
            berries += Foraging.Pick(ForageKind.Blueberry, false, rng).Baits[Foraging.Blueberry];
        }
        t.Check(porcini is >= 100 and < 200 && chant > porcini * 2 && berries > chant, $"a porcini or two ({porcini}), chanterelles in clusters ({chant}), handfuls of berries ({berries})");
        t.Check(!Foraging.Regrown(10f, 30f) && Foraging.Regrown(10f, 34.5f), "a picked spot grows back after a day");
        var kinds = Enumerable.Range(0, 300).Select(_ => Foraging.KindFor(0.8f, 0.2f, rng)).ToList();
        t.Check(kinds.Count(k => k == ForageKind.Porcini) > kinds.Count(k => k == ForageKind.Chanterelle), "porcini grow by the trunks");

        var pantry = new Dictionary<string, int> { [Foraging.Chanterelle] = 2 };
        var fish = new List<Cooking.Ingredient> { new(0, 0.4f, 5, false), new(1, 0.5f, 6, false) };
        t.Check(Cooking.Missing(Cooking.Chanterelles, fish, false, pantry) is string m && m.Contains("kurki 2/4"),
            "a dish with chanterelles says how many are missing");
        pantry[Foraging.Chanterelle] = 5;
        t.Check(Cooking.Missing(Cooking.Chanterelles, fish, false, pantry) == null, "with four chanterelles the dish can be made");
        t.Check(Cooking.Pick(Cooking.Compote, fish) is { Count: 0 } && Cooking.Missing(Cooking.Compote, fish, true, new Dictionary<string, int> { [Foraging.Blueberry] = 12 }) == null,
            "the compote takes no fish");
        t.Check(Cooking.MushroomSoup.Buff.XpBonus > Cooking.Soup.Buff.XpBonus && Cooking.Chanterelles.Buff.XpBonus > Cooking.Roast.Buff.XpBonus,
            "mushroom dishes feed better than plain ones");

        t.Check(Smoking.Refusal(0.5f, true, false) != null && Smoking.Refusal(0.05f, false, false) != null && Smoking.Refusal(0.5f, false, false) == null,
            "the smokehouse refuses undersized and tiny fish");
        t.Check(Smoking.SmokedValue(100, 1f) == 180 && Smoking.SmokedValue(100, 0f) < 100 + 1 && Smoking.Fee(0.2f) == Smoking.MinFee,
            "a smoked fresh fish fetches 1.8x, a dead one about the raw price; a small batch pays the least fee");

        var p = new Profile { Money = 100 };
        p.Keepnet.Add(new CatchRecord("Płoć", 0.4f, 6));
        p.Keepnet.Add(new CatchRecord("Płoć", 0.05f, 1, 1f, true));
        p.Keepnet.Add(new CatchRecord("Karp", 2.5f, 62, 0.5f));
        int went = p.StartSmoking(new[] { 0, 1, 2 }, out var err);
        t.Check(went == 2 && err == null && p.Keepnet.Count == 1 && p.Keepnet[0].Undersized && p.Money == 100 - Smoking.Fee(2.9f),
            "two fish go into the smoke, the undersized one stays, the wood is paid");
        t.Check(p.CollectSmoked() == 0, "nothing is ready at once");
        p.PassTime(Smoking.Hours + 0.1f);
        t.Check(p.CollectSmoked() == 2 && p.Smoked.Sum(f => f.Value) == Smoking.SmokedValue(6, 1f) + Smoking.SmokedValue(62, 0.5f),
            "after two hours both are smoked, priced by their condition");

        var lake = new List<RequestFish> { new("Płoć", 0.1f, 8f, 3f), new("Okoń", 0.15f, 14f, 1.5f), new("Szczupak", 1f, 22f, 0.3f) };
        var day1 = NoticeBoard.Daily(5, 0, lake, true);
        t.Check(day1.Count == NoticeBoard.PerDay && day1[0].Kind == RequestKind.Fish && day1.Select(r => r.From).Distinct().Count() == day1.Count,
            "three notices a day from three villagers, the first for fish");
        t.Check(NoticeBoard.Daily(5, 0, lake, true).Select(r => r.Text).SequenceEqual(day1.Select(r => r.Text))
                && !NoticeBoard.Daily(6, 0, lake, true).Select(r => r.Text).SequenceEqual(day1.Select(r => r.Text)),
            "the same day gives the same notices, the next day new ones");
        t.Check(Enumerable.Range(0, 60).SelectMany(d => NoticeBoard.Daily(d, 1, lake, false)).All(r => r.Kind != RequestKind.Smoked && r.Reward > 0 && r.Xp > 0),
            "no smoked fish asked for without a smokehouse; every notice pays");

        p.Notices = new List<VillageRequest>
        {
            new(RequestKind.Fish, "Płoć", "Płoć", 1, 0.2f, 40, 30, "Babcia Stasia", ""),
            new(RequestKind.Forage, Foraging.Porcini, "borowiki", 2, 0f, 45, 22, "Leśniczy Jerzy", ""),
            new(RequestKind.Smoked, "", "wędzona ryba", 1, 0f, 70, 40, "Rybak Edek", ""),
        };
        p.NoticesDay = p.Market.Day;
        p.NoticesPlace = 0;
        t.Check(p.NoticeMissing(p.Notices[0]) != null, "an undersized roach does not do for a dinner notice");
        p.AddCatch(roach, 0.3f);
        int money = p.Money;
        t.Check(p.HandIn(0, out _) == null && p.Money == money + 40 && p.Notices[0].Done && p.Keepnet.All(f => f.Weight < 0.2f),
            "the roach is handed in and paid for");
        t.Check(p.HandIn(0, out _) != null, "a notice is done only once");
        p.AddPantry(new Dictionary<string, int> { [Foraging.Porcini] = 3 });
        t.Check(p.HandIn(1, out _) == null && p.Pantry[Foraging.Porcini] == 1, "porcini from the pantry");
        t.Check(p.HandIn(2, out _) == null && p.Smoked.Count == 1, "a smoked fish, the cheapest one");
        var saved = ProfileSerializer.FromJson(ProfileSerializer.ToJson(p).ToJsonString());
        t.Check(saved.Pantry.GetValueOrDefault(Foraging.Porcini) == 1 && saved.Smoked.Count == 1 && saved.Notices.Count == 3 && saved.Notices.All(r => r.Done)
                && saved.NoticesDay == p.NoticesDay, "pantry, smoked fish and notices survive a save");
        p.Keepnet.Add(new CatchRecord("Okoń", 0.3f, 4));
        p.StartSmoking(new[] { p.Keepnet.Count - 1 }, out _);
        saved = ProfileSerializer.FromJson(ProfileSerializer.ToJson(p).ToJsonString());
        t.Check(saved.Smokery.Count == 1 && MathF.Abs(saved.Smokery[0].StartedAt - p.Clock) < 1e-3f, "a batch in the smoke survives a save");
        var fresh = p.NoticesFor(0, lake, true);
        t.Check(fresh.Count == 3 && fresh.All(r => r.Done), "the same day keeps the handed-in notices");
        p.PassTime(24f);
        t.Check(p.NoticesFor(0, lake, true).Any(r => !r.Done), "a new day brings new notices");
    }

    /// <summary>Spinning lures and bottom groundbait (Core/Methods).</summary>
    private static void FishingMethods(TestContext t, FishSpecies pike, FishSpecies bream)
    {
        t.Section("fishing methods: spinning and bottom");
        t.Check(Method.For(RodKind.Spinning, BaitKind.Lure) == FishingMethod.Spinning && Method.For(RodKind.Float, BaitKind.Lure) == FishingMethod.Spinning
                && Method.For(RodKind.Bottom, BaitKind.Natural) == FishingMethod.Bottom && Method.For(RodKind.Carp, BaitKind.Carp) == FishingMethod.Bottom
                && Method.For(RodKind.Float, BaitKind.Natural) == FishingMethod.Float && Method.For(RodKind.Spinning, BaitKind.Natural) == FishingMethod.Float,
            "a lure means spinning, bait on a bottom or carp rod lies on the bottom, otherwise the float");
        var rng = new FishingSim.Core.SystemRng(5);
        LureMotion Run(LureFamily f, float seconds, float retrieve, float bottom = 4f, LureMotion? lure = null)
        {
            lure ??= new LureMotion(f);
            for (float s = 0f; s < seconds; s += 0.05f)
                lure.Step(0.05f, retrieve, bottom, new FixedRng(0.99f));
            return lure;
        }
        var spinner = Run(LureFamily.Spinner, 3f, 0f);
        float sunk = spinner.Depth;
        Run(LureFamily.Spinner, 4f, 1.6f, lure: spinner);
        t.Check(sunk > 1.2f && spinner.Depth < 0.6f, $"a spinner sinks when the reel stands ({sunk:0.0} m) and rises on a fast retrieve ({spinner.Depth:0.0} m)");
        var wobbler = Run(LureFamily.Wobbler, 5f, 1.8f);
        float dived = wobbler.Depth;
        Run(LureFamily.Wobbler, 4f, 0f, lure: wobbler);
        t.Check(dived > 1.4f && wobbler.Depth < dived - 0.8f, $"a wobbler dives on a fast retrieve ({dived:0.0} m) and floats up when stopped ({wobbler.Depth:0.0} m)");
        var jig = Run(LureFamily.Softbait, 4f, 0f);
        t.Check(jig.OnBottom && jig.Depth > 3.8f, $"a jig drops to the bottom ({jig.Depth:0.0} m)");
        t.Check(Run(LureFamily.Fly, 3f, 0.4f).Depth == 0f, "a fly rides the surface");
        var good = Run(LureFamily.Spinner, 1f, 0.7f);
        var fast = Run(LureFamily.Spinner, 1f, 2f);
        var still = Run(LureFamily.Spinner, 10f, 0f);
        t.Check(good.Judgement == RetrieveJudgement.Good && fast.Judgement == RetrieveJudgement.TooFast && good.BitePace > fast.BitePace * 2f
                && still.BitePace < good.BitePace * 0.1f, $"a well led lure brings bites ({good.BitePace:0.00}), too fast less ({fast.BitePace:0.00}), lying still hardly ({still.BitePace:0.00})");
        var falling = Run(LureFamily.Softbait, 0.5f, 0f);
        t.Check(falling.Falling && falling.BitePace > 1f, $"a falling jig is taken ({falling.BitePace:0.00})");
        var lifted = Run(LureFamily.Softbait, 4f, 0f);
        lifted.Step(0.05f, 0.5f, 4f, new FixedRng(0.99f));
        t.Check(lifted.BitePace > 1.5f, $"a jig lifted off the bottom after the fall is taken best ({lifted.BitePace:0.00})");
        float before = good.BitePace;
        good.Twitch();
        t.Check(good.BitePace > before * 1.2f, "a twitch flashes the lure");

        int Snags(LureFamily f)
        {
            int n = 0;
            for (int i = 0; i < 200; i++)
            {
                var l = new LureMotion(f);
                for (int k = 0; k < 120; k++)
                    l.Step(0.05f, k < 80 ? 0f : 0.5f, 0.6f, rng);   // falls to a shallow bottom, then dragged along it
                n += l.Snagged ? 1 : 0;
            }
            return n;
        }
        int wobblerSnags = Snags(LureFamily.Wobbler), jigSnags = Snags(LureFamily.Softbait);
        t.Check(Snags(LureFamily.Spoon) > 20 && jigSnags < Snags(LureFamily.Spoon), $"dragged along the bottom lures catch (spoon), a jig less ({jigSnags})");
        var snag = new LureMotion(LureFamily.Spoon);
        for (int k = 0; k < 400 && !snag.Snagged; k++)
            snag.Step(0.05f, k < 40 ? 0f : 0.5f, 1f, rng);
        t.Check(snag.Snagged && snag.BitePace == 0f && snag.Judgement == RetrieveJudgement.Snagged, "a snagged lure fishes no more");
        var pulled = new LureMotion(LureFamily.Spoon);
        for (int k = 0; k < 400 && !pulled.Snagged; k++)
            pulled.Step(0.05f, k < 40 ? 0f : 0.5f, 1f, rng);
        var hold = new FixedRng(0.99f);
        SnagResult r = SnagResult.Stuck;
        for (int k = 0; k < 100 && r == SnagResult.Stuck; k++)
            r = pulled.Pull(hold, 0.05f);
        t.Check(r == SnagResult.Broke, "pulling a snag for four seconds breaks the line");
        int freed = 0;
        for (int i = 0; i < 300; i++)
        {
            var l = new LureMotion(LureFamily.Spoon);
            for (int k = 0; k < 400 && !l.Snagged; k++)
                l.Step(0.05f, k < 40 ? 0f : 0.5f, 1f, rng);
            freed += l.Jerk(rng) == SnagResult.Freed ? 1 : 0;
        }
        t.Check(freed is > 70 and < 140, $"a jerk frees a snag about a third of the time ({freed}/300)");
        t.Check(Method.SelfHookChance(pike.Aggression, true) > Method.SelfHookChance(bream.Aggression, true) * 3f
                && Method.SelfHookChance(pike.Aggression, false) == 0f, "a pike hooks itself on a moving lure far more often than a bream");
        t.Check(Method.HookWindow(FishingMethod.Bottom) > Method.HookWindow(FishingMethod.Float) && Method.HookWindow(FishingMethod.Spinning) < Method.HookWindow(FishingMethod.Float),
            "a fish on the bottom gives more time to strike, a hit on a lure less");

        var spots = new GroundbaitSpots();
        spots.Feed(10f, -30f);
        spots.Feed(11f, -31f);
        spots.Feed(40f, -30f);
        t.Check(spots.Spots.Count == 2 && spots.HeatAt(10.5f, -30.5f) > 1.5f && spots.HeatAt(25f, -30f) == 0f,
            $"feeders emptied close together make one baited spot ({spots.HeatAt(10.5f, -30.5f):0.0}), nothing between spots");
        for (int i = 0; i < 10; i++)
            spots.Feed(10f, -30f);
        t.Check(spots.HeatAt(10f, -30f) <= GroundbaitSpots.MaxStrength + 1e-3f && GroundbaitSpots.PaceFor(spots.HeatAt(10f, -30f)) > 2.5f
                && GroundbaitSpots.PaceFor(0f) == 1f, "a well baited spot brings bites up to almost three times faster");
        spots.Decay(1.5f);
        float faded = spots.HeatAt(10f, -30f);
        spots.Decay(6f);
        t.Check(faded is > 0.9f and < 1.3f && spots.Spots.Count == 0, $"groundbait fades over game hours ({faded:0.00} after 1.5 h, gone after 7.5)");
    }

    /// <summary>The lure and line catalogue: every lure's run, colours, size and weight; every line's diameter.</summary>
    private static void TackleCatalogue(TestContext t)
    {
        t.Section("tackle catalogue: lures and lines");
        var loadout = new TackleLoadout();
        var lures = loadout.Items(TackleSlot.Bait).OfType<BaitData>().Where(b => b.Kind == BaitKind.Lure).ToList();
        var lines = loadout.Items(TackleSlot.Line).OfType<LineData>().ToList();
        var families = lures.GroupBy(b => b.AffinityAs).ToDictionary(g => g.Key, g => g.Count());
        t.Check(lures.Count >= 120 && families.Count == 5 && families.Values.All(n => n >= 10),
            $"a big lure catalogue ({lures.Count}: {string.Join(", ", families.Select(kv => $"{kv.Key} {kv.Value}"))})");
        t.Check(lures.All(b => b.Reusable && b.Price > 0 && b.Description.Length > 10 && b.Shape.Length > 0 && b.SizeCm > 0f && b.WeightG > 0f),
            "every lure has a model, a size, a weight, a price and a description");
        t.Check(lures.Select(b => b.LureColor).Distinct().Count() >= 25, $"in many colours ({lures.Select(b => b.LureColor).Distinct().Count()})");
        var kinds = lines.GroupBy(l => l.Kind).ToDictionary(g => g.Key, g => g.Count());
        t.Check(lines.Count >= 50 && kinds.Count == 3 && lines.All(l => l.Diameter > 0f && l.Strength > 0f),
            $"many lines: monofilament, braid and fluorocarbon ({lines.Count}: {string.Join(", ", kinds.Select(kv => $"{kv.Key} {kv.Value}"))})");
        t.Check(lines.Select(l => l.Diameter).Distinct().Count() >= 15 && lines.Select(l => l.LineColor).Distinct().Count() >= 8,
            "in many diameters and colours");
        bool Thicker(LineKind kind) => lines.Where(l => l.Kind == kind && l.ItemId.StartsWith("g_")).GroupBy(l => l.LineColor)
            .All(g => g.OrderBy(l => l.Diameter).Zip(g.OrderBy(l => l.Diameter).Skip(1)).All(p => p.Second.Strength > p.First.Strength));
        t.Check(Thicker(LineKind.Mono) && Thicker(LineKind.Braid) && Thicker(LineKind.Fluoro), "a thicker line holds more");
        var mono = lines.First(l => l.ItemId == "g_zylka_020_przezroczysta");
        var braid = lines.First(l => l.ItemId == "g_plecionka_020_zielona");
        t.Check(braid.Strength > mono.Strength * 3f && braid.Stretch < mono.Stretch, "a braid is far stronger than a monofilament of its diameter and does not stretch");

        // How the lures run.
        LureMotion Still(BaitData b, float seconds)
        {
            var l = new LureMotion(Method.FamilyOf(b.AffinityAs), LureTuning.From(b));
            for (float s = 0f; s < seconds; s += 0.05f)
                l.Step(0.05f, 0f, 5f, new FixedRng(0.99f));
            return l;
        }
        var popper = lures.First(b => b.Shape == "popper");
        var nymph = lures.First(b => b.Shape == "nymph");
        var dry = lures.First(b => b.Shape == "dry");
        var jerk = lures.First(b => b.Shape == "jerk");
        var minnow = lures.First(b => b.Shape == "minnow" && b.ItemId.StartsWith("l_"));
        t.Check(Still(popper, 3f) is { Surface: true, Depth: 0f } && Still(dry, 3f).Surface, "a popper and a dry fly stay on the surface");
        t.Check(Still(nymph, 3f) is { Surface: false } n && n.Depth > 0.4f, "a nymph sinks under the surface");
        t.Check(Still(jerk, 3f).Depth > 0.6f && Still(minnow, 3f).Depth < 0.1f, "a sinking jerk goes down where a floating minnow comes up");
        var deep = lures.First(b => b.Shape == "crank");
        var drawn = new LureMotion(LureFamily.Wobbler, LureTuning.From(deep));
        var shallow = new LureMotion(LureFamily.Wobbler, LureTuning.From(lures.First(b => b.Shape == "minnow" && b.DepthScale < 0.7f)));
        for (int i = 0; i < 100; i++)
        {
            drawn.Step(0.05f, 1f, 6f, new FixedRng(0.99f));
            shallow.Step(0.05f, 1f, 6f, new FixedRng(0.99f));
        }
        t.Check(drawn.Depth > shallow.Depth * 2.5f, $"a crank with a big lip runs deep ({drawn.Depth:0.0} m), a shallow minnow high ({shallow.Depth:0.0} m)");

        // Colours, size, weight and line.
        var chartreuse = new Color(0.75f, 1f, 0.15f);
        var natural = new Color(0.42f, 0.55f, 0.18f);
        var black = new Color(0.07f, 0.07f, 0.08f);
        var silver = new Color(0.86f, 0.87f, 0.9f);
        float dusk = Method.LightLevel(19f, Weather.Cloudy), noon = Method.LightLevel(12f, Weather.Clear);
        t.Check(noon > 0.95f && dusk < 0.4f && Method.LightLevel(1f, Weather.Clear) < 0.1f && Method.LightLevel(12f, Weather.Rain) < noon,
            "daylight: full at noon, dim at dusk, dark at night, less in the rain");
        t.Check(Method.ColourAppeal(chartreuse, chartreuse, false, dusk, 1.2f) > Method.ColourAppeal(natural, natural, false, dusk, 1.2f) * 1.2f,
            "a fluorescent lure wins in murky water at dusk");
        t.Check(Method.ColourAppeal(natural, natural, false, noon, 5f) > Method.ColourAppeal(chartreuse, chartreuse, false, noon, 5f),
            "a natural colour wins in clear water by day");
        t.Check(Method.ColourAppeal(silver, silver, true, noon, 5f) > Method.ColourAppeal(silver, silver, true, 0.08f, 5f)
                && Method.ColourAppeal(black, black, false, 0.08f, 3f) > Method.ColourAppeal(black, black, false, noon, 3f),
            "polished metal flashes in the sun, black shows at night");
        t.Check(Method.SizeHookShift(14f) > 2f && Method.SizeHookShift(3f) < 0f && Method.SizePace(3f) > Method.SizePace(14f),
            "a big lure acts as a big hook and is taken less often");
        t.Check(Method.CastFactor(25f, 0.2f, true) > Method.CastFactor(3f, 0.2f, true) * 1.2f && Method.CastFactor(0f, 0.12f, false) > Method.CastFactor(0f, 0.4f, false) * 1.2f,
            "a heavy lure and a thin line cast further");
    }

    /// <summary>The fishing line as a rope (Core/Physics/LineRope).</summary>
    private static void LineRopePhysics(TestContext t)
    {
        t.Section("line physics");
        var tip = new Vector3(0f, 3f, 0f);
        var end = new Vector3(0f, 0f, -20f);
        FishingSim.Core.Physics.LineRope Run(float length, float seconds, Vector3 wind, int kind = 0, float bottom = -50f)
        {
            var rope = new FishingSim.Core.Physics.LineRope { WaterY = 0f, Length = length, Wind = wind, BottomY = (_, _) => bottom };
            var (sink, floats) = FishingSim.Core.Physics.LineRope.Behaviour(kind);
            rope.SinkSpeed = sink;
            rope.Floats = floats;
            rope.Reset(tip, end);
            for (float s = 0f; s < seconds; s += 1f / 60f)
                rope.Step(1f / 60f, tip, end);
            return rope;
        }
        var slack = Run(24f, 4f, Vector3.Zero);
        var pts = slack.Positions.ToArray();
        int onWater = pts.Count(p => MathF.Abs(p.Y) < 0.02f);
        t.Check(onWater > pts.Length / 3 && pts.All(p => p.Y >= -0.001f), $"a slack monofilament lies on the water ({onWater}/{pts.Length} points on the surface, none under it)");
        float seg = 24f / (FishingSim.Core.Physics.LineRope.Points - 1);
        bool stretched = false;
        for (int i = 0; i < pts.Length - 1; i++)
            stretched |= pts[i].DistanceTo(pts[i + 1]) > seg * 1.05f;
        t.Check(!stretched, "the line never stretches beyond its length");
        var windy = Run(24f, 6f, new Vector3(3f, 0f, 0f)).Positions.ToArray();
        float bow = windy.Max(p => p.X);
        t.Check(bow > 0.5f, $"the wind blows the slack into a bow ({bow:0.0} m downwind)");
        var taut = Run(tip.DistanceTo(end), 1f, Vector3.Zero).Positions.ToArray();
        t.Check(taut.All(p => (p - tip.Lerp(end, (p.Z - tip.Z) / (end.Z - tip.Z))).Length() < 0.01f), "a tight line runs straight");
        var fluoro = Run(24f, 8f, Vector3.Zero, kind: 2, bottom: -1.5f).Positions.ToArray();
        float deepest = fluoro.Min(p => p.Y);
        t.Check(deepest < -0.5f && deepest >= -1.5f + 0.005f, $"a fluorocarbon sinks under the surface ({deepest:0.00} m), down to the bottom and no further");
        var air = new FishingSim.Core.Physics.LineRope { WaterY = -10f, Length = 22f };
        air.Reset(tip, new Vector3(0f, 3f, -20f));
        for (int i = 0; i < 60; i++)
            air.Step(1f / 60f, tip, new Vector3(0f, 3f, -20f));
        t.Check(air.Positions[FishingSim.Core.Physics.LineRope.Points / 2].Y < 2f, "in the air the slack line hangs under the chord");
        t.Check(slack.Slack(tip, end) > 3f && taut.Length > 0, "the slack is the line paid out beyond the straight distance");
    }

    /// <summary>Feeding levels (the float's depth setting, a lure's depth) and bite styles (Core/Methods/FeedingHabits).</summary>
    private static void Feeding(TestContext t, LocationData lake)
    {
        t.Section("feeding levels and bite styles");
        var bream = Species("leszcz");
        var bleak = Species("ukleja");
        var onBottom = new BiteContext("maggot", 3f, 3f, lake, null, Weather.Clear, 1f);
        var high = onBottom with { BaitLayer = 0.15f };
        t.Check(BiteModel.Weight(bream, onBottom) > BiteModel.Weight(bream, high) * 4f, "a bream finds a bait on the bottom, hardly one hung near the surface");
        var shallow = new BiteContext("maggot", 2f, 1.5f, lake, null, Weather.Clear, 1f);
        t.Check(BiteModel.Weight(bleak, shallow with { BaitLayer = 0.15f }) > BiteModel.Weight(bleak, shallow) * 1.5f, "a bleak takes high in the water");
        t.Check(BiteModel.Weight(bream, onBottom with { BaitLayer = null }) >= BiteModel.Weight(bream, onBottom), "without a level the bait's depth is not judged");
        t.Check(FeedingHabits.BaitLayer(1f, 4f) == 0.25f && FeedingHabits.BaitLayer(5f, 2f) == 1f, "a float set deeper than the water puts the bait on the bottom");
        t.Check(FeedingHabits.LayerFit("Leszcz", 0.6f, 0.8f) > FeedingHabits.LayerFit("Leszcz", 0.6f, 5f), "in shallow water the level matters less (it is fewer metres off)");
        t.Check(FeedingHabits.StyleOf("Leszcz") == BiteStyle.Lift && FeedingHabits.StyleOf("Karp") == BiteStyle.Run
                && FeedingHabits.StyleOf("Ukleja") == BiteStyle.Pluck && FeedingHabits.StyleOf("Okoń") == BiteStyle.Dive,
            "a bream lifts the float, a carp runs with it, a bleak plucks, a perch pulls it under");
        var names = SpeciesCatalog.Paths.Select(path => GD.Load<FishSpecies>(path).SpeciesName).ToHashSet();
        t.Check(new[] { "Leszcz", "Lin", "Karp", "Szczupak", "Ukleja", "Płoć", "Wzdręga", "Boleń", "Sandacz" }.All(names.Contains),
            "the habits name real species");
    }

    /// <summary>Noise at the swim (Core/Methods/Disturbance).</summary>
    private static void Noise(TestContext t)
    {
        t.Section("disturbing the fish");
        float fly = Disturbance.Splash(FishingMethod.Spinning, 0.3f, 2f), spoon = Disturbance.Splash(FishingMethod.Spinning, 25f, 2f);
        t.Check(spoon > fly * 4f && Disturbance.Splash(FishingMethod.Bottom, 0f, 0.8f) > Disturbance.Splash(FishingMethod.Bottom, 0f, 5f) * 2f
                && Disturbance.Splash(FishingMethod.Float, 0f, 2f) < 0.2f,
            $"a heavy lure splashes louder than a fly ({spoon:0.00} vs {fly:0.00}), a feeder louder in shallow water, a float hardly");
        var d = new Disturbance();
        d.Add(0.9f);
        t.Check(d.Spooked && d.Pace < 0.4f, $"spooked fish bite slower (x{d.Pace:0.00})");
        d.Decay(Disturbance.HalfLife);
        float half = d.Level;
        d.Decay(Disturbance.HalfLife * 5f);
        t.Check(MathF.Abs(half - 0.45f) < 0.01f && d.Level < 0.03f && d.Pace > 0.97f, "the water calms down with time");
        t.Check(Disturbance.Steps(5.8f, 3f, false, 1f) > Disturbance.Steps(2.5f, 3f, false, 1f) * 4f
                && Disturbance.Steps(2.5f, 3f, true, 1f) > Disturbance.Steps(2.5f, 3f, false, 1f) * 2f
                && Disturbance.Steps(5.8f, 20f, false, 1f) == 0f && Disturbance.Steps(0f, 1f, true, 1f) == 0f,
            "running near the bait scares more than walking, wading more still; far away or standing still nothing");
        for (int i = 0; i < 50; i++)
            d.Add(1f);
        t.Check(d.Level <= Disturbance.MaxLevel && d.Pace >= 0.25f, "bites never stop altogether");
    }

    /// <summary>Always returns the same value (deterministic checks).</summary>
    private sealed class FixedRng : FishingSim.Core.IRng
    {
        private readonly float _v;
        public FixedRng(float v) => _v = v;
        public float NextFloat() => _v;
    }
}
