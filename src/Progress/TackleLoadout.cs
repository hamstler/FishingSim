using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Data;

namespace FishingSim.Progress;

/// <summary>
/// What the angler has equipped plus the bait stock. With a <see cref="Profile"/> attached only owned gear can be
/// selected. Items are data resources; adding gear = adding a .tres and listing its path here.
/// </summary>
public sealed class TackleLoadout
{
    // The first items of each list keep their places (saves and tests refer to them by index); new ones follow.
    private static readonly string[] RodPaths = Paths("rods",
        "junior", "teleskop", "karpiowka", "wierzbowa_splawikowa", "cisowy_spinning", "grabowy_gruntowy",
        "trzcinowa_odleglosciowa", "jesionowy_spinning", "sumowy_kij", "wedka_mistrza");
    private static readonly string[] ReelPaths = Paths("reels",
        "mlynek1000", "mlynek3000", "freerunner", "szpula_lipowa", "orzechowy_lekki", "mosiezny_hamulec", "beben_sumowy");
    private static readonly string[] LinePaths = Paths("lines",
        "zylka016", "zylka025", "plecionka030", "jelito_cienkie", "jelito_mocne", "jedwab_skrecany",
        "plecionka_konopna", "plecionka_jedwabna", "plecionka_smolowana");
    private static readonly string[] HookPaths = Paths("hooks", "hak2", "hak5", "hak8", "hak1", "hak3", "hak6", "hak10");
    private static readonly string[] BaitPaths = Paths("baits",
        "worm", "corn", "bread", "boilie", "livebait", "maggot", "insect",
        "rosowka", "ochotka", "chrabaszcz", "martwa_rybka", "ciasto", "groch", "kasza", "konopie", "kulka_czosnkowa",
        "kulka_plywajaca", "blystka_obrotowa", "blystka_mosiezna", "rybka_kora", "rybka_drewniana", "piorko_olowiane",
        "skorka_wegorza", "mucha_kogut", "mucha_zajac", "blystka_srebrna");
    private static readonly string[] LeaderPaths = Paths("leaders",
        TackleSlotExtensions.NoLeader, "przypon_wlosie", "przypon_jelito", "przypon_mosiezny", "przypon_stalowy",
        "przypon_jedwab_drut");

    private static string[] Paths(string folder, params string[] ids) =>
        ids.Select(id => $"res://data/tackle/{folder}/{id}.tres").ToArray();

    private readonly List<TackleItem>[] _items;

    /// <summary>Selected index per slot.</summary>
    public int[] Index { get; } = { 1, 1, 1, 1, 0, 0 };

    /// <summary>Bait id -> remaining pieces (shared with the profile when attached).</summary>
    public Dictionary<string, int> BaitCounts { get; private set; } = new();

    public Profile? Profile { get; private set; }

    public TackleLoadout()
    {
        _items = new[]
        {
            Load(RodPaths), Load(ReelPaths), Load(LinePaths), Load(HookPaths), Load(BaitPaths), Load(LeaderPaths),
        };
        Refill();
    }

    private static List<TackleItem> Load(IEnumerable<string> paths) =>
        paths.Select(p => GD.Load<TackleItem>(p)).Where(i => i != null).ToList();

    public IReadOnlyList<TackleItem> Items(TackleSlot slot) => _items[(int)slot];

    public RodData Rod => (RodData)_items[(int)TackleSlot.Rod][Index[(int)TackleSlot.Rod]];
    public ReelData Reel => (ReelData)_items[(int)TackleSlot.Reel][Index[(int)TackleSlot.Reel]];
    public LineData Line => (LineData)_items[(int)TackleSlot.Line][Index[(int)TackleSlot.Line]];
    public HookData Hook => (HookData)_items[(int)TackleSlot.Hook][Index[(int)TackleSlot.Hook]];
    public BaitData Bait => (BaitData)_items[(int)TackleSlot.Bait][Index[(int)TackleSlot.Bait]];
    public LeaderData Leader => (LeaderData)_items[(int)TackleSlot.Leader][Index[(int)TackleSlot.Leader]];

    /// <summary>A lure is on: it fishes only while it moves.</summary>
    public bool LureOn => Bait.Kind == BaitKind.Lure;

    public TackleItem Selected(TackleSlot slot) => _items[(int)slot][Index[(int)slot]];

    /// <summary>Links the loadout to a profile: shares its bait stock and restores the equipped items.</summary>
    public void Attach(Profile profile)
    {
        Profile = profile;
        BaitCounts = profile.BaitCounts;
        for (int s = 0; s < TackleSlotExtensions.Count; s++)
        {
            string wanted = profile.Equipped.GetValueOrDefault(((TackleSlot)s).Key(), "");
            var list = _items[s];
            for (int i = 0; i < list.Count; i++)
                if (list[i].ItemId == wanted)
                    Index[s] = i;
        }
    }

    /// <summary>Re-reads the equipped items from the profile (after gear was destroyed or handed out).</summary>
    public void Resync()
    {
        if (Profile != null)
            Attach(Profile);
    }

    /// <summary>Rod, reel, line and hook in use are all owned: the angler can fish.</summary>
    public bool Ready =>
        Profile == null || new[] { TackleSlot.Rod, TackleSlot.Reel, TackleSlot.Line, TackleSlot.Hook }
            .All(s => Profile.Owns(s, Selected(s).ItemId));

    /// <summary>The cheapest item of each missing kind the angler may buy at their level (the cost of fishing again).</summary>
    public int CostOfMissing()
    {
        if (Profile == null)
            return 0;
        int total = 0;
        foreach (var slot in Profile.MissingKit())
        {
            var buyable = Items(slot).Where(i => i.RequiredLevel <= Profile.Level).Select(i => i.Price).ToList();
            total += buyable.Count > 0 ? buyable.Min() : 0;
        }
        return total;
    }

    public void SyncToProfile()
    {
        if (Profile == null)
            return;
        for (int s = 0; s < TackleSlotExtensions.Count; s++)
        {
            // Never mark a destroyed (no longer owned) item as equipped again.
            if ((TackleSlot)s != TackleSlot.Bait && !Profile.Owns((TackleSlot)s, _items[s][Index[s]].ItemId))
                continue;
            Profile.Equipped[((TackleSlot)s).Key()] = _items[s][Index[s]].ItemId;
        }
    }

    public bool IsOwned(TackleSlot slot, int i)
    {
        if (Profile == null)
            return true;
        var item = _items[(int)slot][i];
        if (slot == TackleSlot.Bait)
            return BaitCounts.GetValueOrDefault(item.ItemId) > 0 || i == Index[(int)slot];
        return Profile.Owns(slot, item.ItemId);
    }

    /// <summary>Moves to the next (or previous) owned item; does nothing when there is no other.</summary>
    public void Cycle(TackleSlot slot, int step)
    {
        int n = _items[(int)slot].Count;
        if (n == 0)
            return;
        for (int k = 1; k <= n; k++)
        {
            int j = ((Index[(int)slot] + step * k) % n + n) % n;
            if (IsOwned(slot, j))
            {
                Index[(int)slot] = j;
                break;
            }
        }
        SyncToProfile();
    }

    public int BaitCount => BaitCounts.GetValueOrDefault(Bait.ItemId);

    /// <summary>A bite takes a natural bait off the hook; a lure stays on.</summary>
    public void UseBait()
    {
        if (!Bait.Reusable)
            BaitCounts[Bait.ItemId] = Math.Max(BaitCount - 1, 0);
    }

    /// <summary>The line broke and the hook went with the fish: a lure on it is lost too.</summary>
    public void LoseLure()
    {
        if (Bait.Reusable)
            BaitCounts[Bait.ItemId] = Math.Max(BaitCount - 1, 0);
    }

    /// <summary>Dev helper: tops every bait up to its start amount.</summary>
    public void Refill()
    {
        foreach (var b in _items[(int)TackleSlot.Bait].Cast<BaitData>())
            BaitCounts[b.ItemId] = Math.Max(BaitCounts.GetValueOrDefault(b.ItemId), b.StartCount);
    }
}
