using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Core;
using FishingSim.Core.Gear;
using FishingSim.Core.Market;
using FishingSim.Data;

namespace FishingSim.Progress;

/// <summary>A fish in the keepnet, waiting to be sold. <paramref name="Value"/> is the fixed buyer's price;
/// <paramref name="Condition"/> (1 = fresh, 0 = dead) drops while the fish sits in the net and lowers the market price.</summary>
public sealed record CatchRecord(string Species, float Weight, int Value, float Condition = 1f, bool Undersized = false);

/// <summary>One line of the fishing journal.</summary>
public sealed record JournalEntry(
    string Species,
    float Weight,
    string? Location = null,
    string? Bait = null,
    string? Hook = null,
    float? Depth = null,
    string? Hour = null);

/// <summary>Optional details recorded with a catch.</summary>
public sealed record CatchContext(string? Location = null, string? Bait = null, string? Hook = null, float? Depth = null, string? Hour = null);

/// <summary>A finished tournament in the player's history.</summary>
public sealed record TournamentRecord(string Name, int Rank, int Entrants, float Score, int PrizeMoney, int PrizeXp);

/// <summary>Result of landing a fish. <paramref name="Released"/>: the keepnet was full, so the fish went back to the water.</summary>
public readonly record struct CatchResult(bool Record, bool LevelUp, int Xp, int Value, bool Released = false);

/// <summary>Player progression: money, XP/level, owned tackle, bait stock, keepnet, records and journal.</summary>
public sealed class Profile
{
    public const int StartMoney = 60;
    public const int JournalLimit = 300;
    public const string SaveDir = "user://saves";
    public const string LegacySavePath = "user://save.json";

    public event Action? Changed;
    public event Action<int>? LevelUp;

    public string Username { get; set; } = "admin";
    public string Path { get; set; } = LegacySavePath;

    public int Money { get; set; }
    public int Xp { get; set; }
    public int Level { get; set; } = 1;
    public int TotalCatches { get; set; }
    public int LocationIdx { get; set; }
    public float Hour { get; set; } = 7f;

    public List<CatchRecord> Keepnet { get; set; } = new();
    public List<JournalEntry> Journal { get; set; } = new();
    public List<TournamentRecord> TournamentHistory { get; set; } = new();
    public Dictionary<string, float> Records { get; set; } = new();
    public Dictionary<string, int> CatchCounts { get; set; } = new();
    public Dictionary<string, List<string>> Owned { get; set; } = new();
    public Dictionary<string, int> BaitCounts { get; set; } = new();
    public Dictionary<string, string> Equipped { get; set; } = new();

    /// <summary>Keepnets, landing nets and accessories.</summary>
    public GearInventory Gear { get; set; } = new();

    /// <summary>Live fish-market prices (saturation per species, market clock). Saved with the profile.</summary>
    public FishMarket Market { get; set; } = new();

    /// <summary>
    /// Limits of the equipped keepnet, set by <see cref="GearCatalog"/>. Null = unlimited (plain profiles in tests
    /// and tools behave like before keepnets existed).
    /// </summary>
    public KeepnetSpec? KeepnetLimit { get; set; }

    public Profile() => Reset();

    /// <summary>Raises <see cref="Changed"/> (UI refresh, autosave).</summary>
    public void NotifyChanged() => Changed?.Invoke();

    public void Reset()
    {
        Money = StartMoney;
        Xp = 0;
        Level = 1;
        TotalCatches = 0;
        LocationIdx = 0;
        Hour = 7f;
        Keepnet = new();
        Journal = new();
        TournamentHistory = new();
        Records = new();
        CatchCounts = new();
        Owned = new()
        {
            ["rod"] = new() { "junior" },
            ["reel"] = new() { "mlynek1000" },
            ["line"] = new() { "zylka025" },
            ["hook"] = new() { "hak5" },
        };
        BaitCounts = new() { ["worm"] = 20, ["bread"] = 10 };
        Equipped = new()
        {
            ["rod"] = "junior", ["reel"] = "mlynek1000", ["line"] = "zylka025", ["hook"] = "hak5", ["bait"] = "worm",
        };
        Gear = new GearInventory();
        Market = new FishMarket();
    }

    // ------------------------------------------------------------------ economy
    public int XpToNext => Progression.XpForLevel(Level + 1);

    public bool Owns(TackleSlot slot, string id) => Owned.TryGetValue(slot.Key(), out var ids) && ids.Contains(id);

    public int KeepnetValue => Keepnet.Sum(f => f.Value);

    public float KeepnetWeight => Keepnet.Sum(f => f.Weight);

    /// <summary>True when the equipped keepnet has room for one more fish of this weight.</summary>
    public bool KeepnetFits(float weight) =>
        KeepnetLimit is not KeepnetSpec spec || KeepnetModel.Fits(spec, Keepnet.Count, KeepnetWeight, weight);

    /// <summary>Fish in the keepnet lose condition as time passes; the market recovers from earlier sales.</summary>
    public void PassTime(float hours)
    {
        if (hours <= 0f)
            return;
        Market.AdvanceHours(hours);
        if (KeepnetLimit is not KeepnetSpec spec || Keepnet.Count == 0)
            return;
        float fill = spec.MaxKg > 0f ? KeepnetWeight / spec.MaxKg : 1f;
        for (int i = 0; i < Keepnet.Count; i++)
            Keepnet[i] = Keepnet[i] with { Condition = KeepnetModel.Decay(Keepnet[i].Condition, spec, hours, fill) };
    }

    private static FishLot LotFor(CatchRecord fish, IEnumerable<FishSpecies> species)
    {
        var sp = species.FirstOrDefault(s => s.SpeciesName == fish.Species);
        float perKg = sp?.PricePerKg ?? (fish.Weight > 0f ? fish.Value / fish.Weight : 0f);
        return new FishLot(fish.Species, fish.Weight, perKg, sp?.TrophyThreshold ?? 0f, fish.Condition);
    }

    /// <summary>What the fish market would pay for one keepnet fish right now.</summary>
    public int MarketQuote(CatchRecord fish, IEnumerable<FishSpecies> species) => Market.Quote(LotFor(fish, species));

    /// <summary>Sells one keepnet fish at the market (live price). Returns the money earned, 0 for a bad index.</summary>
    public int SellAtMarket(int index, IEnumerable<FishSpecies> species)
    {
        if (index < 0 || index >= Keepnet.Count)
            return 0;
        int paid = Market.Sell(LotFor(Keepnet[index], species)) - (Keepnet[index].Undersized ? UndersizedFine : 0);
        Keepnet.RemoveAt(index);
        Money += paid;
        Changed?.Invoke();
        return paid;
    }

    /// <summary>Sells the whole keepnet at the market, biggest fish first (they get the unsaturated price).</summary>
    public int SellAllAtMarket(IEnumerable<FishSpecies> species)
    {
        var list = species.ToList();
        int paid = Market.SellAll(Keepnet.OrderByDescending(f => f.Weight).Select(f => LotFor(f, list)).ToList())
                   - UndersizedCount * UndersizedFine;
        Money += paid;
        Keepnet = new();
        Changed?.Invoke();
        return paid;
    }
    /// <summary>Adds a landed fish to the keepnet and the journal, awards XP and checks for records and level-ups.</summary>
    public CatchResult AddCatch(FishSpecies species, float weight, CatchContext? context = null)
    {
        string name = species.SpeciesName;
        int xpGain = Progression.FishXp(species, weight);
        int value = Progression.FishValue(species, weight);
        bool released = !KeepnetFits(weight);
        if (!released)
            Keepnet.Add(new CatchRecord(name, weight, value, 1f, weight < species.LegalWeight));
        CatchCounts[name] = CatchCounts.GetValueOrDefault(name) + 1;
        TotalCatches++;

        var c = context ?? new CatchContext();
        Journal.Add(new JournalEntry(name, weight, c.Location, c.Bait, c.Hook, c.Depth, c.Hour));
        if (Journal.Count > JournalLimit)
            Journal.RemoveRange(0, Journal.Count - JournalLimit);

        bool record = weight > Records.GetValueOrDefault(name);
        if (record)
            Records[name] = weight;

        bool leveled = GainXp(xpGain);
        Changed?.Invoke();
        return new CatchResult(record, leveled, xpGain, value, released);
    }

    /// <summary>Adds XP and levels up as often as needed. Returns true when at least one level was gained.</summary>
    private bool GainXp(int amount)
    {
        Xp += amount;
        bool leveled = false;
        while (Xp >= Progression.XpForLevel(Level + 1))
        {
            Level++;
            leveled = true;
            LevelUp?.Invoke(Level);
        }
        return leveled;
    }

    public int TournamentWins => TournamentHistory.Count(t => t.Rank == 1);

    /// <summary>Pays out a tournament result and records it in the history. Returns true when it caused a level-up.</summary>
    public bool AwardTournament(TournamentRecord result)
    {
        Money += result.PrizeMoney;
        bool leveled = GainXp(result.PrizeXp);
        TournamentHistory.Add(result);
        if (TournamentHistory.Count > 50)
            TournamentHistory.RemoveAt(0);
        Changed?.Invoke();
        return leveled;
    }

    /// <summary>Sells the whole keepnet and returns the money earned.</summary>
    /// <summary>Fine (gr) the bailiff takes for every undersized fish brought to a buyer.</summary>
    public const int UndersizedFine = 10;

    public int UndersizedCount => Keepnet.Count(f => f.Undersized);

    /// <summary>Sells the whole keepnet at the fixed buyer. Returns the money earned after fines for undersized fish.</summary>
    public int SellAll()
    {
        int value = KeepnetValue - UndersizedCount * UndersizedFine;
        Money += value;
        Keepnet = new();
        Changed?.Invoke();
        return value;
    }

    /// <summary>Buys an item (or a bait pack). Returns null on success or a (Polish) error message.</summary>
    public string? Buy(TackleSlot slot, string id, int price, int requiredLevel, int pack = 1)
    {
        if (Level < requiredLevel)
            return $"Wymagany poziom {requiredLevel}";
        if (slot != TackleSlot.Bait && Owns(slot, id))
            return "Już to masz";
        if (Money < price)
            return "Za mało pieniędzy";
        Money -= price;
        if (slot == TackleSlot.Bait)
            BaitCounts[id] = BaitCounts.GetValueOrDefault(id) + pack;
        else
            Owned[slot.Key()].Add(id);
        Changed?.Invoke();
        return null;
    }

    // ------------------------------------------------------------------ broken gear and the free kit
    /// <summary>The starter kit handed out free at the tackle shop to anglers who cannot afford anything else.</summary>
    public static readonly IReadOnlyDictionary<TackleSlot, string> StarterKit = new Dictionary<TackleSlot, string>
    {
        [TackleSlot.Rod] = "junior", [TackleSlot.Reel] = "mlynek1000", [TackleSlot.Line] = "zylka025", [TackleSlot.Hook] = "hak5",
    };

    /// <summary>Worms that come with the free kit when the angler has no bait at all.</summary>
    public const int StarterWorms = 10;

    private static readonly TackleSlot[] KitSlots = { TackleSlot.Rod, TackleSlot.Reel, TackleSlot.Line, TackleSlot.Hook };

    /// <summary>
    /// Destroys an item (a snapped rod, a hook left in a fish, a line pulled off the spool): it leaves the inventory,
    /// and if it was in use another owned item of that kind is equipped (or none). Returns false if it was not owned.
    /// </summary>
    public bool Destroy(TackleSlot slot, string id)
    {
        if (slot == TackleSlot.Bait || !Owned.TryGetValue(slot.Key(), out var ids) || !ids.Remove(id))
            return false;
        if (Equipped.GetValueOrDefault(slot.Key()) == id)
            Equipped[slot.Key()] = ids.Count > 0 ? ids[0] : "";
        Changed?.Invoke();
        return true;
    }

    /// <summary>Kinds of gear the angler has none of (so they cannot fish).</summary>
    public List<TackleSlot> MissingKit() =>
        KitSlots.Where(s => !Owned.TryGetValue(s.Key(), out var ids) || ids.Count == 0).ToList();

    public bool HasKit => MissingKit().Count == 0;

    /// <summary>
    /// The free kit is only for anglers who are stuck: something is missing and they cannot afford to buy it
    /// (<paramref name="costOfMissing"/> = the cheapest replacement for every missing piece).
    /// </summary>
    public bool CanClaimFreeKit(int costOfMissing) => !HasKit && Money < costOfMissing;

    /// <summary>Hands out the missing starter pieces (and worms when there is no bait). Null on success, else why not.</summary>
    public string? ClaimFreeKit(int costOfMissing)
    {
        if (HasKit)
            return "Masz już kompletny zestaw";
        if (!CanClaimFreeKit(costOfMissing))
            return $"Stać Cię na nowy sprzęt ({costOfMissing} gr): darmowy zestaw jest tylko dla tych bez pieniędzy";
        foreach (var slot in MissingKit())
        {
            string id = StarterKit[slot];
            Owned[slot.Key()].Add(id);
            Equipped[slot.Key()] = id;
        }
        if (BaitCounts.Values.All(n => n <= 0))
        {
            BaitCounts["worm"] = StarterWorms;
            Equipped["bait"] = "worm";
        }
        Changed?.Invoke();
        return null;
    }

    /// <summary>XP for putting a fish back.</summary>
    public const int ReleaseXp = 3;

    /// <summary>
    /// Puts the most recently kept fish back into the water (it must be the last one in the keepnet). Gives a little XP.
    /// Returns false when that fish is not in the keepnet.
    /// </summary>
    public bool ReleaseLast(string species, float weight)
    {
        if (Keepnet.Count == 0)
            return false;
        var last = Keepnet[^1];
        if (last.Species != species || MathF.Abs(last.Weight - weight) > 1e-4f)
            return false;
        Keepnet.RemoveAt(Keepnet.Count - 1);
        GainXp(ReleaseXp);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Buys a keepnet, landing net or accessory. Returns null on success or a (Polish) error message.</summary>
    public string? BuyGear(GearSlot slot, string id, int price, int requiredLevel)
    {
        if (Level < requiredLevel)
            return $"Wymagany poziom {requiredLevel}";
        if (Gear.Owns(slot, id))
            return "Już to masz";
        if (Money < price)
            return "Za mało pieniędzy";
        Money -= price;
        Gear.Add(slot, id);
        Changed?.Invoke();
        return null;
    }

    // ------------------------------------------------------------------ save / load
    /// <summary>Saves unless running headless or FISHSIM_NO_SAVE is set (tests, screenshots).</summary>
    public void Autosave()
    {
        if (DisplayServer.GetName() == "headless" || OS.HasEnvironment("FISHSIM_NO_SAVE"))
            return;
        Save();
    }

    public bool Save(string? path = null) => ProfileSerializer.Save(this, path ?? Path);

    public static string PathFor(string user) => $"{SaveDir}/{SafeFileName(user.ToLowerInvariant())}.json";

    /// <summary>Replaces characters that are not allowed in file names.</summary>
    public static string SafeFileName(string name)
    {
        var bad = System.IO.Path.GetInvalidFileNameChars();
        var chars = name.Select(c => Array.IndexOf(bad, c) >= 0 || c == ':' ? '_' : c).ToArray();
        return new string(chars);
    }

    public static Profile LoadFrom(string file)
    {
        var p = new Profile { Path = file };
        ProfileSerializer.LoadInto(p, file);
        return p;
    }

    /// <summary>Loads (or creates) the profile of a user. The old single save is adopted by "admin".</summary>
    public static Profile LoadFor(string user)
    {
        string target = PathFor(user);
        Profile profile;
        if (Godot.FileAccess.FileExists(target))
            profile = LoadFrom(target);
        else if (user.Equals("admin", StringComparison.OrdinalIgnoreCase) && Godot.FileAccess.FileExists(LegacySavePath))
            profile = LoadFrom(LegacySavePath);
        else
            profile = new Profile();
        profile.Username = user.ToLowerInvariant();
        profile.Path = target;
        return profile;
    }
}
