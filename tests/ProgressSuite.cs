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
        t.Check(q.Buy(TackleSlot.Rod, "teleskop", 120, 2) == "Wymagany poziom 2", "level gate blocks the Teleskop rod");
        q.Xp = 60; q.Level = 2;
        t.Check(q.Buy(TackleSlot.Rod, "teleskop", 120, 2) == "Za mało pieniędzy", "not enough money (60 < 120)");
        q.Money = 200;
        t.Check(q.Buy(TackleSlot.Rod, "teleskop", 120, 2) == null && q.Money == 80 && q.Owns(TackleSlot.Rod, "teleskop"), "buying works and charges 120 zł");
        t.Check(q.Buy(TackleSlot.Rod, "teleskop", 120, 2) == "Już to masz", "cannot buy the same rod twice");
        t.Check(q.Buy(TackleSlot.Bait, "worm", 10, 1, 10) == null && q.BaitCounts["worm"] == 30, "a bait pack adds 10 pieces");

        t.Section("loadout only cycles owned gear");
        var lo = new TackleLoadout();
        var pr = new Profile();
        lo.Attach(pr);
        t.Check(lo.Rod.ItemId == "junior", "starts on the Junior rod");
        lo.Cycle(TackleSlot.Rod, 1);
        t.Check(lo.Rod.ItemId == "junior", "cycling stays put with a single owned rod");
        pr.Owned["rod"].Add("karpiowka");
        lo.Cycle(TackleSlot.Rod, 1);
        t.Check(lo.Rod.ItemId == "karpiowka" && pr.Equipped["rod"] == "karpiowka", "cycling skips the unowned Teleskop and records the choice");
        lo.Cycle(TackleSlot.Bait, 1);
        t.Check(lo.Bait.ItemId == "bread", "bait cycling skips baits with no stock");
        lo.Cycle(TackleSlot.Rod, -1);
        t.Check(lo.Rod.ItemId == "junior", "cycling backwards wraps around");
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
        t.Check(bp.Destroy(TackleSlot.Rod, "junior") && !bp.Owns(TackleSlot.Rod, "junior") && bp.Equipped["rod"] == "",
            "a snapped rod leaves the inventory and is no longer equipped");
        bl.Resync();
        t.Check(!bp.HasKit && !bl.Ready && bp.MissingKit().SequenceEqual(new[] { TackleSlot.Rod }), "without a rod the kit is incomplete");
        int cost = bl.CostOfMissing();
        t.Check(cost == 40, $"a replacement rod costs {cost} gr (the cheapest one at level 1)");
        bp.Money = 60;
        t.Check(!bp.CanClaimFreeKit(cost) && bp.ClaimFreeKit(cost) != null && !bp.HasKit, "with money for a new rod the free kit is refused");
        bp.Money = 25;
        t.Check(bp.CanClaimFreeKit(cost) && bp.ClaimFreeKit(cost) == null && bp.HasKit && bp.Equipped["rod"] == "junior" && bp.Money == 25,
            "a broke angler gets the starter rod for free");
        bl.Resync();
        t.Check(bl.Ready && bl.Rod.ItemId == "junior", "the loadout picks the handed-out rod up");
        t.Check(bp.ClaimFreeKit(0) != null, "no second free kit while the kit is complete");
        bp.Owned["rod"].Add("teleskop");
        bp.Equipped["rod"] = "teleskop";
        bp.Destroy(TackleSlot.Rod, "teleskop");
        t.Check(bp.Equipped["rod"] == "junior" && bp.HasKit, "losing one of two rods falls back to the other");
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

        t.Section("save / load round trip");
        var s = new Profile();
        s.AddCatch(carp, 6.5f, new CatchContext("Jezioro", "Kukurydza", "Hak #5", 3.1f, "07:45"));
        s.AddCatch(pike, 3.1f);
        s.Money = 777;
        s.Owned["rod"].Add("teleskop");
        s.BaitCounts["boilie"] = 12;
        s.Equipped["rod"] = "teleskop";
        s.LocationIdx = 1;
        s.Hour = 19.5f;
        const string path = "/tmp/fishsim_cs_save_test.json";
        t.Check(s.Save(path), "save writes the file");
        var l = Profile.LoadFrom(path);
        t.Check(l.Money == 777 && l.Level == s.Level && l.Xp == s.Xp, "money, level and XP survive");
        t.Check(l.Owned["rod"].SequenceEqual(new[] { "junior", "teleskop" }) && l.Equipped["rod"] == "teleskop", "owned and equipped gear survive");
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
        t.Check(old.Owned["rod"].Contains("teleskop") && old.BaitCounts["worm"] == 14 && old.BaitCounts.Count == 1, "owned gear and bait stock parse");
        t.Check(old.Journal.Count == 1 && old.Journal[0].Location == null && old.Journal[0].Hour == "12:00", "journal entries without optional fields parse");
        t.Check(old.Owns(TackleSlot.Leader, TackleSlotExtensions.NoLeader) && new TackleLoadout().Leader != null,
            "an old save without leaders still has \"no leader\"");

        t.Section("lures, leaders and lines");
        var tackle = new TackleLoadout();
        t.Check(tackle.Items(TackleSlot.Rod).Count >= 10 && tackle.Items(TackleSlot.Reel).Count >= 7 && tackle.Items(TackleSlot.Line).Count >= 9
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
        return Task.CompletedTask;
    }
}
