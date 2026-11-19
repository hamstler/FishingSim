using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishingSim.Data;
using FishingSim.Progress;
using Godot;

namespace FishingSim.Tests;

/// <summary>Profile economy, shop rules, ownership-aware loadout, journal and save/load.</summary>
public sealed class ProgressSuite : ITestSuite
{
    public string Name => "progress";

    private static FishSpecies Species(string id) => GD.Load<FishSpecies>($"res://data/fish/{id}.tres");

    public Task Run(TestContext t)
    {
        var carp = Species("karp");
        var roach = Species("ploc");
        var pike = Species("szczupak");

        t.Section("profile basics");
        var p = new Profile();
        t.Check(p.Money == 60 && p.Level == 1 && p.Xp == 0, "new profile: 60 zł, level 1");
        var levels = new List<int>();
        p.LevelUp += levels.Add;
        int n = 0;
        while (p.Level < 3)
        {
            var r = p.AddCatch(roach, 0.8f);
            n++;
            if (n == 1)
                t.Check(r.Record, "first fish is a species record");
        }
        t.Check(p.Level == 3 && levels.SequenceEqual(new[] { 2, 3 }), $"levelled up 1 -> 2 -> 3 after {n} roach");
        t.Check(p.Keepnet.Count == n, "keepnet holds every catch");
        var tiny = new Profile();
        tiny.AddCatch(roach, 0.11f);
        tiny.AddCatch(roach, 0.6f);
        int expectFine = tiny.KeepnetValue - Profile.UndersizedFine;
        t.Check(tiny.UndersizedCount == 1 && tiny.Keepnet[0].Undersized && !tiny.Keepnet[1].Undersized, "an 11 dag roach is undersized, a 60 dag one is not");
        t.Check(tiny.SellAll() == expectFine, $"the bailiff fines {Profile.UndersizedFine} gr for each undersized fish sold");
        var und = ProfileSerializer.FromJson(ProfileSerializer.ToJson(new Func<Profile>(() => { var q = new Profile(); q.AddCatch(roach, 0.11f); return q; })()).ToJsonString());
        t.Check(und.Keepnet[0].Undersized, "the undersized mark survives save and load");
        int before = p.Money;
        int sold = p.SellAll();
        t.Check(p.Money == before + sold && p.Keepnet.Count == 0 && sold > 0, $"SellAll pays {sold} zł and empties the net");

        t.Section("shop rules");
        var q = new Profile();
        t.Check(q.Buy(TackleSlot.Rod, "s_jesion_300", 120, 2) == "Wymagany poziom 2", "level gate blocks a level-2 rod");
        q.Xp = 60; q.Level = 2;
        t.Check(q.Buy(TackleSlot.Rod, "s_jesion_300", 120, 2) == "Za mało pieniędzy", "not enough money (60 < 120)");
        q.Money = 200;
        t.Check(q.Buy(TackleSlot.Rod, "s_jesion_300", 120, 2) == null && q.Money == 80 && q.Owns(TackleSlot.Rod, "s_jesion_300"), "buying works and charges 120 zł");
        t.Check(q.Buy(TackleSlot.Rod, "s_jesion_300", 120, 2) == "Już to masz", "cannot buy the same rod twice");
        t.Check(q.Buy(TackleSlot.Bait, "worm", 10, 1, 10) == null && q.BaitCounts["worm"] == 30, "a bait pack adds 10 pieces");

        t.Section("loadout only cycles owned gear");
        var lo = new TackleLoadout();
        var pr = new Profile();
        lo.Attach(pr);
        t.Check(lo.Rod.ItemId == "s_jesion_240", "starts on the Ostoja Jesion 2,4 m");
        // Hooks follow the real scale from #24 (smallest) to 4/0 (largest), stronger and heavier as they grow.
        var hooks = lo.Items(TackleSlot.Hook).Cast<HookData>().OrderBy(h => h.Size).ToList();
        t.Check(hooks.Count == 17 && hooks[0].Label == "#24" && hooks[^1].Label == "4/0",
            $"hooks run from {hooks[0].Label} to {hooks[^1].Label} ({hooks.Count} sizes)");
        bool rising = true;
        for (int i = 1; i < hooks.Count; i++)
            rising &= hooks[i].Strength > hooks[i - 1].Strength && hooks[i].WeightG > hooks[i - 1].WeightG
                && (hooks[i].RealSize < hooks[i - 1].RealSize);
        t.Check(rising, "a bigger hook (smaller number, then n/0) is stronger and heavier");
        // The starter kit is balanced: the line gives first, the reel's drag slips before it.
        var kitLine = GD.Load<LineData>("res://data/tackle/lines/g_zylka_016_przezroczysta.tres");
        var kitRod = GD.Load<RodData>("res://data/tackle/rods/s_jesion_240.tres");
        var kitHook = GD.Load<HookData>("res://data/tackle/hooks/hak5.tres");
        var kitReel = GD.Load<ReelData>("res://data/tackle/reels/mlynek1000.tres");
        t.Check(lo.Line.ItemId == "g_zylka_016_przezroczysta" && kitLine.Strength < kitRod.MaxLoad && kitLine.Strength < kitHook.Strength
                && kitReel.DragMax < kitLine.Strength && kitHook.Label == "#10",
            $"starter kit: line {kitLine.Strength} kg is the weakest link (rod {kitRod.MaxLoad}, hook #10 {kitHook.Strength}), drag up to {kitReel.DragMax} kg");
        lo.Cycle(TackleSlot.Rod, 1);
        t.Check(lo.Rod.ItemId == "s_jesion_240", "cycling stays put with a single owned rod");
        pr.Owned["rod"].Add("s_koi_360");
        lo.Cycle(TackleSlot.Rod, 1);
        t.Check(lo.Rod.ItemId == "s_koi_360" && pr.Equipped["rod"] == "s_koi_360", "cycling skips the unowned rods and records the choice");
        lo.Cycle(TackleSlot.Bait, 1);
        t.Check(lo.Bait.ItemId == "bread", "bait cycling skips baits with no stock");
        lo.Cycle(TackleSlot.Rod, -1);
        t.Check(lo.Rod.ItemId == "s_jesion_240", "cycling backwards wraps around");
        pr.BaitCounts["bread"] = 0;
        pr.BaitCounts["worm"] = 0;
        t.Check(lo.BaitCount == 0, "an empty bait reports 0 pieces");
        lo.Refill();
        t.Check(lo.BaitCount > 0, "dev refill restocks the equipped bait");

        t.Section("broken gear and the free kit");
        var bp = new Profile();
        var bl = new TackleLoadout();
        bl.Attach(bp);
        t.Check(bp.HasKit && bl.Ready, "a new angler has a complete kit");
        t.Check(bp.Destroy(TackleSlot.Rod, "s_jesion_240") && !bp.Owns(TackleSlot.Rod, "s_jesion_240") && bp.Equipped["rod"] == "",
            "a snapped rod leaves the inventory and is no longer equipped");
        bl.Resync();
        t.Check(!bp.HasKit && !bl.Ready && bp.MissingKit().SequenceEqual(new[] { TackleSlot.Rod }), "without a rod the kit is incomplete");
        int cost = bl.CostOfMissing();
        t.Check(cost == 60, $"a replacement rod costs {cost} gr (the cheapest one at level 1)");
        bp.Money = 60;
        t.Check(!bp.CanClaimFreeKit(cost) && bp.ClaimFreeKit(cost) != null && !bp.HasKit, "with money for a new rod the free kit is refused");
        bp.Money = 25;
        t.Check(bp.CanClaimFreeKit(cost) && bp.ClaimFreeKit(cost) == null && bp.HasKit && bp.Equipped["rod"] == "s_jesion_240" && bp.Money == 25,
            "a broke angler gets the starter rod for free");
        bl.Resync();
        t.Check(bl.Ready && bl.Rod.ItemId == "s_jesion_240", "the loadout picks the handed-out rod up");
        t.Check(bp.ClaimFreeKit(0) != null, "no second free kit while the kit is complete");
        bp.Owned["rod"].Add("s_jesion_300");
        bp.Equipped["rod"] = "s_jesion_300";
        bp.Destroy(TackleSlot.Rod, "s_jesion_300");
        t.Check(bp.Equipped["rod"] == "s_jesion_240" && bp.HasKit, "losing one of two rods falls back to the other");
        bp.Destroy(TackleSlot.Hook, "hak5");
        bp.BaitCounts.Clear();
        bp.Money = 0;
        bl.Resync();
        bp.ClaimFreeKit(bl.CostOfMissing());
        t.Check(bp.Owns(TackleSlot.Hook, "hak5") && bp.BaitCounts["worm"] == Profile.StarterWorms, "the free kit adds the missing hook and some worms");

        t.Section("journal");
        var j = new Profile();
        j.AddCatch(carp, 3.3f, new CatchContext("Staw", "Kulka", "Hak #8", 2.2f, "06:30"));
        t.Check(j.Journal.Count == 1 && j.Journal[0].Bait == "Kulka" && j.Journal[0].Depth == 2.2f, "catch is logged with bait, hook, place, depth and time");
        for (int i = 0; i < 320; i++)
            j.AddCatch(roach, 0.3f);
        t.Check(j.Journal.Count == Profile.JournalLimit, "journal is capped at 300 entries");

        t.Section("trophies and challenges");
        t.Check(FishingSim.Core.Methods.Trophies.Of(0.9f, 1f) == FishingSim.Core.Methods.TrophyTier.Gold
                && FishingSim.Core.Methods.Trophies.Of(0.65f, 1f) == FishingSim.Core.Methods.TrophyTier.Silver
                && FishingSim.Core.Methods.Trophies.Of(0.45f, 1f) == FishingSim.Core.Methods.TrophyTier.Bronze
                && FishingSim.Core.Methods.Trophies.Of(0.2f, 1f) == FishingSim.Core.Methods.TrophyTier.None,
            "trophies by the share of the species' biggest weight: bronze 40 %, silver 60 %, gold 80 %");
        var common = new List<string> { "Płoć", "Okoń" };
        var ch = new Profile();
        var today = ch.Challenges(0, common);
        t.Check(today.Count == 4 && today.Count(c => c.Challenge.Weekly) == 1 && today.All(c => !c.Done)
                && ch.Challenges(0, common).Select(c => c.Challenge.Id).SequenceEqual(today.Select(c => c.Challenge.Id)),
            "three daily challenges and a weekly one, the same all day");
        var countC = today.First(c => c.Challenge.Kind == FishingSim.Core.Methods.ChallengeKind.CatchCount).Challenge;
        int money0 = ch.Money, xp0 = ch.Xp + ch.Level * 100000;
        var completed = new List<FishingSim.Core.Methods.Challenge>();
        for (int i = 0; i < 12; i++)
            completed.AddRange(ch.CreditChallenges("Płoć", 0.3f, "spławik", false, 0, common));
        t.Check(completed.Any(c => c.Id == countC.Id) && completed.Count(c => c.Id == countC.Id) == 1 && ch.Money >= money0 + countC.Money,
            $"a completed challenge pays once ({countC.Title}: +{countC.Money} gr)");
        var back = ProfileSerializer.FromJson(ProfileSerializer.ToJson(ch).ToJsonString());
        t.Check(back.Challenges(0, common).Single(c => c.Challenge.Id == countC.Id).Done, "challenge progress is saved");

        t.Section("save / load round trip");
        var s = new Profile();
        s.AddCatch(carp, 6.5f, new CatchContext("Jezioro", "Kukurydza", "Hak #5", 3.1f, "07:45"));
        s.AddCatch(pike, 3.1f);
        s.Money = 777;
        s.Owned["rod"].Add("s_jesion_300");
        s.BaitCounts["boilie"] = 12;
        s.Equipped["rod"] = "s_jesion_300";
        s.LocationIdx = 1;
        s.Hour = 19.5f;
        const string path = "/tmp/fishsim_cs_save_test.json";
        t.Check(s.Save(path), "save writes the file");
        var l = Profile.LoadFrom(path);
        t.Check(l.Money == 777 && l.Level == s.Level && l.Xp == s.Xp, "money, level and XP survive");
        t.Check(l.Owned["rod"].SequenceEqual(new[] { "s_jesion_240", "s_jesion_300" }) && l.Equipped["rod"] == "s_jesion_300", "owned and equipped gear survive");
        t.Check(l.BaitCounts["boilie"] == 12 && l.BaitCounts["worm"] == 20, "bait stock survives");
        t.Check(l.Keepnet.Count == 2 && System.MathF.Abs(l.Keepnet[0].Weight - 6.5f) < 0.001f, "keepnet survives");
        t.Check(System.MathF.Abs(l.Records["Karp"] - 6.5f) < 0.001f && l.CatchCounts["Szczupak"] == 1, "records and counters survive");
        t.Check(l.Journal.Count == 2 && l.Journal[0].Location == "Jezioro" && l.Journal[1].Location == null, "journal (with optional fields) survives");
        t.Check(l.LocationIdx == 1 && System.MathF.Abs(l.Hour - 19.5f) < 0.001f, "location and time of day survive");
        t.Check(Profile.LoadFrom("/tmp/does_not_exist.json").Money == 60, "a missing save gives a fresh profile");

        t.Section("compatibility with saves written by the GDScript version");
        const string legacy = "{\"version\": 1, \"money\": 312, \"xp\": 130, \"level\": 2, \"keepnet\": [{\"species\": \"Karp\", \"weight\": 4.5, \"value\": 112}], " +
            "\"records\": {\"Karp\": 4.5}, \"catch_counts\": {\"Karp\": 1}, \"total_catches\": 7, \"journal\": [{\"species\": \"Karp\", \"weight\": 4.5, \"hour\": \"12:00\"}], " +
            "\"owned\": {\"rod\": [\"junior\", \"teleskop\"], \"reel\": [\"mlynek1000\"], \"line\": [\"zylka025\"], \"hook\": [\"hak5\"]}, " +
            "\"bait_counts\": {\"worm\": 14}, \"equipped\": {\"rod\": \"teleskop\", \"reel\": \"mlynek1000\", \"line\": \"zylka025\", \"hook\": \"hak5\", \"bait\": \"worm\"}, " +
            "\"location_idx\": 2, \"hour\": 7.0}";
        var old = ProfileSerializer.FromJson(legacy);
        t.Check(old.Money == 312 && old.Level == 2 && old.TotalCatches == 7 && old.LocationIdx == 2, "integers parse");
        t.Check(old.Hour == 7f && old.Records["Karp"] == 4.5f, "floats written as 7.0 parse");
        t.Check(old.Owned["rod"].SequenceEqual(new[] { "s_jesion_240", "s_jesion_300" }) && old.Equipped["rod"] == "s_jesion_300"
                && old.BaitCounts["worm"] == 14 && old.BaitCounts.Count == 1, "owned gear and bait stock parse; retired rods become their successors");
        t.Check(old.Journal.Count == 1 && old.Journal[0].Location == null && old.Journal[0].Hour == "12:00", "journal entries without optional fields parse");
        t.Check(old.Owns(TackleSlot.Leader, TackleSlotExtensions.NoLeader) && new TackleLoadout().Leader != null,
            "an old save without leaders still has \"no leader\"");

        t.Section("lures, leaders and lines");
        var tackle = new TackleLoadout();
        t.Check(tackle.Items(TackleSlot.Rod).Count >= 10 && tackle.Items(TackleSlot.Reel).Count >= 6 && tackle.Items(TackleSlot.Line).Count >= 9
                && tackle.Items(TackleSlot.Leader).Count >= 6 && tackle.Items(TackleSlot.Bait).Count >= 26,
            "the shops stock many rods, reels, lines, leaders and baits");
        t.Check(tackle.Items(TackleSlot.Bait).OfType<BaitData>().Count(b => b.Kind == BaitKind.Lure && b.Reusable) >= 8,
            "there are lures, and they are not used up");
        t.Check(tackle.Items(TackleSlot.Line).OfType<LineData>().Count(l => l.Kind == LineKind.Braid) >= 4,
            "braided lines are their own kind");
        var esox = GD.Load<FishSpecies>("res://data/fish/szczupak.tres");
        var eel = GD.Load<FishSpecies>("res://data/fish/wegorz.tres");
        var grayling = GD.Load<FishSpecies>("res://data/fish/lipien.tres");
        t.Check(esox.AffinityFor("blystka_mosiezna") > 1.5f && esox.AffinityFor("blystka_srebrna") > esox.AffinityFor("blystka_mosiezna"),
            "a pike goes for a brass spoon, more so for the silver one (family x scale)");
        t.Check(eel.AffinityFor("blystka_mosiezna") == 0f && grayling.AffinityFor("mucha_kogut") > grayling.AffinityFor("blystka_obrotowa"),
            "an eel ignores lures; a grayling prefers a fly to a spinner");
        t.Check(esox.AffinityFor("rosowka") > esox.AffinityFor("worm") * 1.1f, "a better bait of a family scores above the plain one");
        t.Check(esox.BiteOff > 0f && GD.Load<FishSpecies>("res://data/fish/karp.tres").BiteOff == 0f, "a pike has teeth, a carp has none");
        var lp = new Profile();
        var ll = new TackleLoadout();
        ll.Attach(lp);
        t.Check(ll.Leader.ItemId == TackleSlotExtensions.NoLeader && lp.Owns(TackleSlot.Leader, TackleSlotExtensions.NoLeader)
                && !lp.Destroy(TackleSlot.Leader, TackleSlotExtensions.NoLeader), "everyone fishes without a leader at first, and cannot lose it");
        lp.Level = 5; lp.Money = 1000;
        t.Check(lp.Buy(TackleSlot.Leader, "przypon_stalowy", 90, 3) == null && lp.Owns(TackleSlot.Leader, "przypon_stalowy"), "a wire leader can be bought");
        lp.BaitCounts["blystka_mosiezna"] = 2;
        ll.Index[(int)TackleSlot.Bait] = ll.Items(TackleSlot.Bait).ToList().FindIndex(i => i.ItemId == "blystka_mosiezna");
        ll.UseBait();
        t.Check(ll.LureOn && ll.BaitCount == 2, "a bite does not use a lure up");
        ll.LoseLure();
        t.Check(ll.BaitCount == 1, "a lure is lost with the hook");

        t.Section("graphics settings");
        // Key bindings (src/Bindings): defaults without a saved file, rebinding swaps a taken key, reserved keys refused.
        t.Check(Bindings.Of(Act.Interact) == Key.E && Bindings.Of(Act.QuickHelp) == Key.F1, "default keys: E interacts, F1 quick help");
        t.Check(Bindings.Rebind(Act.LooseFeed, Key.R) && Bindings.Of(Act.LooseFeed) == Key.R && Bindings.Of(Act.Release) == Key.G,
            "binding a taken key swaps it with the other action");
        t.Check(Bindings.Is(new InputEventKey { Keycode = Key.R, Pressed = true }, Act.LooseFeed), "the rebound key fires its action");
        t.Check(!Bindings.Rebind(Act.Map, Key.Escape) && !Bindings.Rebind(Act.Map, Key.Key3), "Esc and the hotbar digits cannot be bound");
        Bindings.ResetAll();
        t.Check(Bindings.Of(Act.LooseFeed) == Key.G && Bindings.Of(Act.Release) == Key.R, "reset restores the defaults");

        var gq = typeof(FishingSim.Graphics.GraphicsQuality);
        FishingSim.Graphics.GraphicsQuality.Set(FishingSim.Graphics.QualityLevel.High);
        t.Check(!FishingSim.Graphics.GraphicsQuality.Custom && FishingSim.Graphics.GraphicsQuality.TextureLimit == 1024, "the High preset: 1K textures (every preset), not custom");
        FishingSim.Graphics.GraphicsQuality.SetStep(FishingSim.Graphics.GraphicsQuality.Shadows, 0);
        t.Check(FishingSim.Graphics.GraphicsQuality.Custom, "changing one setting makes the preset custom");
        FishingSim.Graphics.GraphicsQuality.SetStep(FishingSim.Graphics.GraphicsQuality.Shadows, 99);
        t.Check(FishingSim.Graphics.GraphicsQuality.Shadows.Value == FishingSim.Graphics.GraphicsQuality.Shadows.Steps.Length - 1, "a step is clamped at the last one");
        FishingSim.Graphics.GraphicsQuality.Set(FishingSim.Graphics.QualityLevel.Ultra);
        t.Check(FishingSim.Graphics.GraphicsQuality.TextureLimit == 1024 && FishingSim.Graphics.GraphicsQuality.WaterReflectionSteps == 32 && !FishingSim.Graphics.GraphicsQuality.Custom,
            "Ultra: 1K textures too, full water reflections");
        FishingSim.Graphics.GraphicsQuality.Set(FishingSim.Graphics.QualityLevel.Low);
        t.Check(FishingSim.Graphics.GraphicsQuality.GrassRadius == 0f && FishingSim.Graphics.GraphicsQuality.TextureLimit == 1024, "Low: no grass blades, 1K textures");
        FishingSim.Graphics.GraphicsQuality.Set(FishingSim.Graphics.QualityLevel.High);
        _ = gq;
        return Task.CompletedTask;
    }
}
