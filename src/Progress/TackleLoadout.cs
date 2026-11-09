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
    private static readonly string[] RodPaths =
    {
        "res://data/tackle/rods/junior.tres", "res://data/tackle/rods/teleskop.tres", "res://data/tackle/rods/karpiowka.tres",
    };
    private static readonly string[] ReelPaths =
    {
        "res://data/tackle/reels/mlynek1000.tres", "res://data/tackle/reels/mlynek3000.tres", "res://data/tackle/reels/freerunner.tres",
    };
    private static readonly string[] LinePaths =
    {
        "res://data/tackle/lines/zylka016.tres", "res://data/tackle/lines/zylka025.tres", "res://data/tackle/lines/plecionka030.tres",
    };
    private static readonly string[] HookPaths =
    {
        "res://data/tackle/hooks/hak2.tres", "res://data/tackle/hooks/hak5.tres", "res://data/tackle/hooks/hak8.tres",
    };
    private static readonly string[] BaitPaths =
    {
        "res://data/tackle/baits/worm.tres", "res://data/tackle/baits/corn.tres", "res://data/tackle/baits/bread.tres",
        "res://data/tackle/baits/boilie.tres", "res://data/tackle/baits/livebait.tres",
    };

    private readonly List<TackleItem>[] _items;

    /// <summary>Selected index per slot.</summary>
    public int[] Index { get; } = { 1, 1, 1, 1, 0 };

    /// <summary>Bait id -> remaining pieces (shared with the profile when attached).</summary>
    public Dictionary<string, int> BaitCounts { get; private set; } = new();

    public Profile? Profile { get; private set; }

    public TackleLoadout()
    {
        _items = new[]
        {
            Load(RodPaths), Load(ReelPaths), Load(LinePaths), Load(HookPaths), Load(BaitPaths),
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

    public void SyncToProfile()
    {
        if (Profile == null)
            return;
        for (int s = 0; s < TackleSlotExtensions.Count; s++)
            Profile.Equipped[((TackleSlot)s).Key()] = _items[s][Index[s]].ItemId;
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

    public void UseBait() => BaitCounts[Bait.ItemId] = Math.Max(BaitCount - 1, 0);

    /// <summary>Dev helper: tops every bait up to its start amount.</summary>
    public void Refill()
    {
        foreach (var b in _items[(int)TackleSlot.Bait].Cast<BaitData>())
            BaitCounts[b.ItemId] = Math.Max(BaitCounts.GetValueOrDefault(b.ItemId), b.StartCount);
    }
}
