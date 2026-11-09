using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishingSim.Core;
using FishingSim.Data;
using Godot;

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

        t.Section("data resources load with their C# types");
        t.Check(roach.SpeciesName == "Płoć" && carp.PricePerKg == 25f, "species .tres values arrive in the C# properties");
        t.Check(carp.AffinityFor("boilie") == 2f && carp.AffinityFor("nonsense") == 0f, "bait affinity lookup");
        t.Check(pike.WeatherMultiplier("rain") == 1.3f && pike.WeatherMultiplier("hail") == 1f, "weather multiplier lookup with default");
        t.Check(lake.AbundanceOf("Karp") == 0.6f && lake.AbundanceOf("Sum") == 0f, "location abundance lookup");
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
        var teleGear = new FightGear(DragMax: 5f, EffectiveLimit: 7f, RodShock: 0.2f, ReelSpeed: 3f, MaxLine: 100f);
        var juniorGear = new FightGear(DragMax: 3f, EffectiveLimit: 4f, RodShock: 0.3f, ReelSpeed: 2.2f, MaxLine: 60f);

        var (outcome, secs) = Fight(roach, 0.8f, teleGear, 0.5f, seed: 3);
        t.Check(outcome == FightOutcome.Landed && secs < 90f, $"a roach on balanced gear is landed ({secs:0} s)");

        (outcome, _) = Fight(carp, 12f, new FightGear(9f, 4f, 0.3f, 3.4f, 150f), 0.6f, seed: 4);
        t.Check(outcome == FightOutcome.LineBroke, "a drag set above the line strength breaks the line");

        (outcome, secs) = Fight(carp, 12f, new FightGear(5f, 6f, 0.2f, 3f, 80f), 0.05f, seed: 5);
        t.Check(outcome == FightOutcome.SpooledOut, "a nearly open drag lets the fish take all the line");

        (outcome, secs) = Fight(carp, 12f, teleGear, 0.5f, seed: 6);
        t.Check(outcome == FightOutcome.Landed, $"a 12 kg carp is landed with a sensible drag ({secs:0} s)");

        var model = new FightModel(FishProfile.From(carp, 12f), new FightGear(9f, 4f, 0.3f, 3.4f, 150f), new SystemRng(8), 20f, 0f, 0.6f) { Unbreakable = true };
        bool ever = false;
        for (int i = 0; i < 3000 && !ever; i++)
            ever = model.Step(1f / 60f, 0.6f, true) == FightOutcome.LineBroke;
        t.Check(!ever, "an unbreakable line never breaks (dev flag)");

        var f = new FightModel(FishProfile.From(roach, 0.5f), teleGear, new SystemRng(1), 20f, 0f, 0.5f);
        t.Near(f.Fatigue, 1f, 0.001f, "fatigue x1 at the start");
        for (int i = 0; i < 45 * 60; i++) f.Step(1f / 60f, 0.5f, false);
        t.Near(f.Fatigue, 2f, 0.05f, "fatigue x2 after 45 s");

        var a = TraceLine(carp, 12f, teleGear, seed: 11);
        var b = TraceLine(carp, 12f, teleGear, seed: 11);
        var c = TraceLine(carp, 12f, teleGear, seed: 12);
        t.Check(a == b, "the same seed gives an identical fight");
        t.Check(a != c, "a different seed gives a different fight");

        var times = Enumerable.Range(0, 12).Select(seed => Fight(carp, 12f, juniorGear, 0.6f, seed + 100)).ToList();
        t.Check(times.All(r => r.Outcome != FightOutcome.Ongoing), $"fights on weak gear end (slowest {times.Max(r => r.Seconds):0} s)");
        t.Check(times.Where(r => r.Outcome == FightOutcome.Landed).All(r => r.Seconds < 300f), "fatigue keeps the longest weak-gear fight under 5 minutes");

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
}
