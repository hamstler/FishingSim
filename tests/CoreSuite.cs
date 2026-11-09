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
}
