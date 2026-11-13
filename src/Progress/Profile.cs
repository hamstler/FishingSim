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

/// <summary>A fish hanging in the smokehouse since <paramref name="StartedAt"/> (game hours, <see cref="Profile.Clock"/>).</summary>
public sealed record SmokingLot(CatchRecord Fish, float StartedAt);

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

    /// <summary>The last meal's lasting effect and the game hours it has left (none when null or run out).</summary>
    public Core.Camp.MealBuff? Meal { get; set; }

    public float MealHoursLeft { get; set; }

    /// <summary>The meal working right now, or null.</summary>
    public Core.Camp.MealBuff? ActiveMeal => Meal != null && MealHoursLeft > 0f ? Meal : null;

    /// <summary>Forest pickings (Core/Camp/Foraging ids -> pieces): mushrooms and berries for cooking and notices.</summary>
    public Dictionary<string, int> Pantry { get; set; } = new();

    /// <summary>Fish hanging in the village smokehouse.</summary>
    public List<SmokingLot> Smokery { get; set; } = new();

    /// <summary>Smoked fish taken from the smokehouse; Value is what the smokehouse keeper pays for each.</summary>
    public List<CatchRecord> Smoked { get; set; } = new();

    /// <summary>The notices on the village board for <see cref="NoticesDay"/> at map <see cref="NoticesPlace"/>.</summary>
    public List<Core.Camp.VillageRequest> Notices { get; set; } = new();

    public int NoticesDay { get; set; } = -1;
    public int NoticesPlace { get; set; } = -1;

    /// <summary>Game hours since the profile began (the market's clock, advanced by <see cref="PassTime"/>).</summary>
    public float Clock => Market.Hours;

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
        Meal = null;
        MealHoursLeft = 0f;
        Pantry = new();
        Smokery = new();
        Smoked = new();
        Notices = new();
        NoticesDay = NoticesPlace = -1;
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
            ["leader"] = new() { TackleSlotExtensions.NoLeader },
        };
        BaitCounts = new() { ["worm"] = 20, ["bread"] = 10 };
        Equipped = new()
        {
            ["rod"] = "junior", ["reel"] = "mlynek1000", ["line"] = "zylka025", ["hook"] = "hak5", ["bait"] = "worm",
            ["leader"] = TackleSlotExtensions.NoLeader,
        };
        Gear = new GearInventory();
        Market = new FishMarket();
    }

    // ------------------------------------------------------------------ economy
    public int XpToNext => Progression.XpForLevel(Level + 1);

    public bool Owns(TackleSlot slot, string id) =>
        (slot == TackleSlot.Leader && id == TackleSlotExtensions.NoLeader)
        || (Owned.TryGetValue(slot.Key(), out var ids) && ids.Contains(id));

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
        if (MealHoursLeft > 0f)
        {
            MealHoursLeft = MathF.Max(0f, MealHoursLeft - hours);
            if (MealHoursLeft <= 0f)
                Changed?.Invoke();
        }
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
    /// <summary>
    /// Records a landed fish: counts, journal, records and XP. With <paramref name="toKeepnet"/> it also goes straight
    /// into the keepnet (when there is room); without it the angler decides afterwards (<see cref="Keep"/> or
    /// <see cref="Release"/>).
    /// </summary>
    public CatchResult AddCatch(FishSpecies species, float weight, CatchContext? context = null, bool toKeepnet = true)
    {
        string name = species.SpeciesName;
        int xpGain = Progression.FishXp(species, weight);
        // A well-fed angler learns more from every fish.
        if (ActiveMeal is { } meal)
            xpGain = (int)MathF.Round(xpGain * (1f + meal.XpBonus));
        int value = Progression.FishValue(species, weight);
        bool released = toKeepnet && !KeepnetFits(weight);
        if (toKeepnet && !released)
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
        if (slot != TackleSlot.Bait && !Owned.ContainsKey(slot.Key()))
            Owned[slot.Key()] = new();
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
        if (slot == TackleSlot.Bait || (slot == TackleSlot.Leader && id == TackleSlotExtensions.NoLeader)
            || !Owned.TryGetValue(slot.Key(), out var ids) || !ids.Remove(id))
            return false;
        if (Equipped.GetValueOrDefault(slot.Key()) == id)
            Equipped[slot.Key()] = slot == TackleSlot.Leader ? TackleSlotExtensions.NoLeader : ids.Count > 0 ? ids[0] : "";
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

    /// <summary>Puts a landed fish into the keepnet. Null on success, otherwise why not (a full keepnet).</summary>
    public string? Keep(FishSpecies species, float weight)
    {
        if (!KeepnetFits(weight))
            return KeepnetLimit is { } net && Keepnet.Count >= net.MaxFish
                ? "Siatka pełna: wypuść rybę albo sprzedaj połów"
                : "Ryba nie zmieści się w siatce: jest za ciężka na tę siatkę";
        Keepnet.Add(new CatchRecord(species.SpeciesName, weight, Progression.FishValue(species, weight), 1f, weight < species.LegalWeight));
        Changed?.Invoke();
        return null;
    }

    /// <summary>Adds bait the angler dug up or caught (no shop needed).</summary>
    public void AddBait(IReadOnlyDictionary<string, int> baits)
    {
        if (baits.Count == 0)
            return;
        foreach (var (id, n) in baits)
            BaitCounts[id] = BaitCounts.GetValueOrDefault(id) + n;
        Changed?.Invoke();
    }

    /// <summary>The keepnet as cooking ingredients.</summary>
    public List<Core.Camp.Cooking.Ingredient> Ingredients() =>
        Keepnet.Select((f, i) => new Core.Camp.Cooking.Ingredient(i, f.Weight, f.Value, f.Undersized)).ToList();

    /// <summary>
    /// Cooks a dish from the keepnet and eats it: the fish it takes leave the keepnet, the meal's buff starts (a new
    /// meal replaces the old one). Returns the fish used, or null with <paramref name="error"/> when it cannot be made.
    /// </summary>
    public List<CatchRecord>? Cook(Core.Camp.Recipe recipe, bool hasPot, out string? error)
    {
        var ingredients = Ingredients();
        error = Core.Camp.Cooking.Missing(recipe, ingredients, hasPot, Pantry);
        if (error != null)
            return null;
        var picked = Core.Camp.Cooking.Pick(recipe, ingredients)!;
        var used = picked.Select(i => Keepnet[i]).ToList();
        foreach (int i in picked.OrderByDescending(i => i))
            Keepnet.RemoveAt(i);
        if (recipe.Extras != null)
            foreach (var (id, n) in recipe.Extras)
                Pantry[id] = Pantry.GetValueOrDefault(id) - n;
        Meal = recipe.Buff;
        MealHoursLeft = recipe.Buff.Hours;
        Changed?.Invoke();
        return used;
    }

    // ------------------------------------------------------------------ forest, smokehouse, notices
    /// <summary>Puts forest pickings into the pantry.</summary>
    public void AddPantry(IReadOnlyDictionary<string, int> items)
    {
        if (items.Count == 0)
            return;
        foreach (var (id, n) in items)
            Pantry[id] = Pantry.GetValueOrDefault(id) + n;
        Changed?.Invoke();
    }

    /// <summary>
    /// Hangs keepnet fish in the smokehouse (indices into the keepnet) and pays the wood fee. Returns how many went in;
    /// 0 with <paramref name="error"/> when none could (refused fish are left in the keepnet).
    /// </summary>
    public int StartSmoking(IEnumerable<int> indices, out string? error)
    {
        error = null;
        var chosen = indices.Distinct().Where(i => i >= 0 && i < Keepnet.Count)
            .Where(i => Core.Camp.Smoking.Refusal(Keepnet[i].Weight, Keepnet[i].Undersized, false) == null).OrderByDescending(i => i).ToList();
        if (chosen.Count == 0)
        {
            error = Keepnet.Count == 0 ? "Siatka jest pusta." : "Żadna z tych ryb nie nadaje się do wędzenia (niewymiarowe albo za małe).";
            return 0;
        }
        int fee = Core.Camp.Smoking.Fee(chosen.Sum(i => Keepnet[i].Weight));
        if (Money < fee)
        {
            error = $"Za mało pieniędzy na drewno ({fee} gr).";
            return 0;
        }
        Money -= fee;
        foreach (int i in chosen)
        {
            Smokery.Add(new SmokingLot(Keepnet[i], Clock));
            Keepnet.RemoveAt(i);
        }
        Changed?.Invoke();
        return chosen.Count;
    }

    /// <summary>The keepnet fish the smokehouse would take (indices).</summary>
    public List<int> Smokable() =>
        Keepnet.Select((f, i) => (f, i)).Where(x => Core.Camp.Smoking.Refusal(x.f.Weight, x.f.Undersized, false) == null).Select(x => x.i).ToList();

    public int SmokeryReady => Smokery.Count(l => Core.Camp.Smoking.HoursLeft(l.StartedAt, Clock) <= 0f);

    /// <summary>Takes the finished fish from the smokehouse. Returns how many.</summary>
    public int CollectSmoked()
    {
        var ready = Smokery.Where(l => Core.Camp.Smoking.HoursLeft(l.StartedAt, Clock) <= 0f).ToList();
        if (ready.Count == 0)
            return 0;
        foreach (var lot in ready)
        {
            Smokery.Remove(lot);
            Smoked.Add(lot.Fish with { Value = Core.Camp.Smoking.SmokedValue(lot.Fish.Value, lot.Fish.Condition), Condition = 1f });
        }
        Changed?.Invoke();
        return ready.Count;
    }

    public int SmokedValue => Smoked.Sum(f => f.Value);

    /// <summary>Sells every smoked fish to the smokehouse keeper. Returns the money earned.</summary>
    public int SellSmoked()
    {
        int paid = SmokedValue;
        Money += paid;
        Smoked = new();
        Changed?.Invoke();
        return paid;
    }

    /// <summary>The day's notices at this map, made anew when the day or the map changed.</summary>
    public List<Core.Camp.VillageRequest> NoticesFor(int place, IReadOnlyList<Core.Camp.RequestFish> fish, bool smokehouse)
    {
        int day = Market.Day;
        if (day != NoticesDay || place != NoticesPlace)
        {
            Notices = Core.Camp.NoticeBoard.Daily(day, place, fish, smokehouse);
            NoticesDay = day;
            NoticesPlace = place;
        }
        return Notices;
    }

    /// <summary>What is still missing to hand in a notice, or null when it can be done.</summary>
    public string? NoticeMissing(Core.Camp.VillageRequest r)
    {
        if (r.Done)
            return "Załatwione.";
        int have = r.Kind switch
        {
            Core.Camp.RequestKind.Fish => Keepnet.Count(f => Core.Camp.NoticeBoard.FishFits(r, f.Species, f.Weight, f.Undersized)),
            Core.Camp.RequestKind.Bait => BaitCounts.GetValueOrDefault(r.Item),
            Core.Camp.RequestKind.Forage => Pantry.GetValueOrDefault(r.Item),
            _ => Smoked.Count,
        };
        return have >= r.Count ? null : $"Masz {have}/{r.Count}";
    }

    /// <summary>Hands in notice <paramref name="index"/>: takes the goods, pays the reward. Null on success, else why not.</summary>
    public string? HandIn(int index, out bool leveled)
    {
        leveled = false;
        if (index < 0 || index >= Notices.Count)
            return "Nie ma takiego ogłoszenia.";
        var r = Notices[index];
        if (NoticeMissing(r) is string missing)
            return missing;
        switch (r.Kind)
        {
            case Core.Camp.RequestKind.Fish:
                // The lightest fish that do: the big ones are worth more at the market.
                var fish = Keepnet.Select((f, i) => (f, i)).Where(x => Core.Camp.NoticeBoard.FishFits(r, x.f.Species, x.f.Weight, x.f.Undersized))
                    .OrderBy(x => x.f.Weight).Take(r.Count).Select(x => x.i).OrderByDescending(i => i).ToList();
                foreach (int i in fish)
                    Keepnet.RemoveAt(i);
                break;
            case Core.Camp.RequestKind.Bait:
                BaitCounts[r.Item] -= r.Count;
                break;
            case Core.Camp.RequestKind.Forage:
                Pantry[r.Item] -= r.Count;
                break;
            default:
                foreach (var f in Smoked.OrderBy(f => f.Value).Take(r.Count).ToList())
                    Smoked.Remove(f);
                break;
        }
        Notices[index] = r with { Done = true };
        Money += r.Reward;
        leveled = GainXp(r.Xp);
        Changed?.Invoke();
        return null;
    }

    /// <summary>Puts a landed fish back into the water (a little XP for sportsmanship).</summary>
    public void Release()
    {
        GainXp(ReleaseXp);
        Changed?.Invoke();
    }

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
