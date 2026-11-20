using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishingSim.Compat;
using FishingSim.Core;
using FishingSim.Data;
using Newtonsoft.Json;

namespace FishingSim.Game;

/// <summary>A fish kept in the keepnet.</summary>
public sealed class KeptFish
{
    public string Species { get; set; } = "";
    public string Name { get; set; } = "";
    public float Weight { get; set; }
}

/// <summary>
/// The angler's progress: money, experience, owned and equipped tackle, bait, the keepnet and records. Saved as JSON in
/// the game's user folder (FISHSIM_NO_SAVE=1 keeps it in memory).
/// </summary>
public sealed class Profile
{
    public const int StartMoney = 60;
    public const int StarterWorms = 10;

    public int Money { get; set; } = StartMoney;
    public int Xp { get; set; }
    public int Location { get; set; }
    public float Hour { get; set; } = 7f;
    public string Rod { get; set; } = "s_jesion_240";
    public string Reel { get; set; } = "mlynek1000";
    public string Line { get; set; } = "g_zylka_016_przezroczysta";
    public string Hook { get; set; } = "hak5";
    public string Bait { get; set; } = "worm";
    public HashSet<string> Owned { get; set; } = new() { "s_jesion_240", "mlynek1000", "g_zylka_016_przezroczysta", "hak5" };
    public Dictionary<string, int> Baits { get; set; } = new() { ["worm"] = StarterWorms };
    public List<KeptFish> Keepnet { get; set; } = new();
    public Dictionary<string, float> Records { get; set; } = new();
    public int Caught { get; set; }

    /// <summary>Line wear per line id (0 .. 0.6: the share of its strength lost; Core/Methods/LineWear).</summary>
    public Dictionary<string, float> LineWear { get; set; } = new();

    /// <summary>Hook dullness per hook id (0 .. 0.6; Core/Methods/HookWear).</summary>
    public Dictionary<string, float> HookDull { get; set; } = new();

    /// <summary>Twist per line id (turns put in by spinning lures; Core/Methods/LineTwist).</summary>
    public Dictionary<string, float> LineTwist { get; set; } = new();

    /// <summary>Fish kept today per species (Core/Methods/BagLimits), and which day that is.</summary>
    public Dictionary<string, int> KeptToday { get; set; } = new();
    public int KeptDay { get; set; } = -1;

    public float WearOf(string line) => LineWear.TryGetValue(line, out var w) ? w : 0f;
    public float DullOf(string hook) => HookDull.TryGetValue(hook, out var d) ? d : 0f;
    public float TwistOf(string line) => LineTwist.TryGetValue(line, out var t) ? t : 0f;

    /// <summary>How many of a species were kept today (the day from the profile's clock).</summary>
    public int KeptOf(string species)
    {
        int day = (int)(Clock / 24f);
        if (day != KeptDay)
        {
            KeptToday.Clear();
            KeptDay = day;
        }
        return KeptToday.TryGetValue(species, out var n) ? n : 0;
    }

    /// <summary>Forest pickings (Core/Camp/Foraging ids -> pieces).</summary>
    public Dictionary<string, int> Pantry { get; set; } = new();

    /// <summary>The last meal's buff and until when it lasts (Clock hours).</summary>
    public string MealId { get; set; } = "";
    public float MealUntil { get; set; }

    [JsonIgnore]
    public Core.Camp.MealBuff? ActiveMeal =>
        MealUntil > Clock ? Core.Camp.Cooking.All.FirstOrDefault(r => r.Id == MealId)?.Buff : null;

    public bool HasAccessory(string id) => Owned.Contains(id);

    /// <summary>Notices already handed in ("day:place:index").</summary>
    public HashSet<string> Notices { get; set; } = new();

    /// <summary>Cooks a dish from the keepnet (and the pantry); returns what happened.</summary>
    public string Cook(Core.Camp.Recipe r)
    {
        var fish = Keepnet.Select((f, i) => new Core.Camp.Cooking.Ingredient(i, f.Weight,
            Tackle.Species(f.Species) is { } sp ? Progression.FishValue(sp, f.Weight) : 0,
            Tackle.Species(f.Species) is { } s2 && f.Weight < s2.LegalWeight)).ToList();
        if (Core.Camp.Cooking.Missing(r, fish, HasAccessory("garnek"), Pantry) is { } why)
            return why;
        var take = Core.Camp.Cooking.Pick(r, fish)!.OrderByDescending(i => i).ToList();
        foreach (int i in take)
            Keepnet.RemoveAt(i);
        if (r.Extras != null)
            foreach (var (id, n) in r.Extras)
                Pantry[id] = Pantry.GetValueOrDefault(id) - n;
        MealId = r.Id;
        MealUntil = Clock + r.Buff.Hours;
        return $"{r.Name}: {r.Buff.Name} na {r.Buff.Hours:0} godz. (+{r.Buff.XpBonus * 100f:0}% doświadczenia, szybsze brania).";
    }

    /// <summary>Game hours played in all (the market's and the smokehouse's clock).</summary>
    public float Clock { get; set; }

    /// <summary>Fish hanging in the smokehouse and when they went in (-1 = none).</summary>
    public List<KeptFish> Smoking { get; set; } = new();
    public float SmokingSince { get; set; } = -1f;

    /// <summary>Puts every keepnet fish the smokehouse takes into the smoke; returns the fee paid, or a refusal.</summary>
    public string StartSmoking()
    {
        if (Smoking.Count > 0)
            return "W wędzarni już wisi partia ryb.";
        var take = Keepnet.Where(f => Tackle.Species(f.Species) is { } sp
            && Core.Camp.Smoking.Refusal(f.Weight, f.Weight < sp.LegalWeight, false) == null).ToList();
        if (take.Count == 0)
            return "W siatce nie ma ryb do wędzenia (od 100 g, wymiarowe).";
        int fee = Core.Camp.Smoking.Fee(take.Sum(f => f.Weight));
        if (Money < fee)
            return $"Opłata za drewno: {fee} zł. Za mało pieniędzy.";
        Money -= fee;
        foreach (var f in take)
            Keepnet.Remove(f);
        Smoking.AddRange(take);
        SmokingSince = Clock;
        return $"{take.Count} ryb w wędzarni (opłata {fee} zł). Gotowe za {Core.Camp.Smoking.Hours:0} godz. gry.";
    }

    public float SmokingHoursLeft => Smoking.Count == 0 ? 0f : Core.Camp.Smoking.HoursLeft(SmokingSince, Clock);

    public int SmokedValue() => Smoking.Sum(f => Tackle.Species(f.Species) is { } sp
        ? Core.Camp.Smoking.SmokedValue(Progression.FishValue(sp, f.Weight), 1f) : 0);

    /// <summary>Sells a ready batch to the smokehouse keeper.</summary>
    public int SellSmoked()
    {
        int total = SmokedValue();
        Money += total;
        Smoking.Clear();
        SmokingSince = -1f;
        return total;
    }

    [JsonIgnore]
    public int Level
    {
        get
        {
            int level = 1;
            while (Xp >= Progression.XpForLevel(level + 1))
                level++;
            return level;
        }
    }

    public int BaitCount(string id) => Baits.TryGetValue(id, out int n) ? n : 0;

    public void UseBait()
    {
        var bait = Tackle.Bait(Bait);
        if (bait == null || bait.Reusable)
            return;
        int left = BaitCount(Bait) - 1;
        if (left > 0)
            Baits[Bait] = left;
        else
        {
            Baits.Remove(Bait);
            Bait = Baits.Keys.FirstOrDefault() ?? "";
        }
    }

    public void AddCatch(FishSpecies sp, float weight)
    {
        Caught++;
        Xp += (int)(Progression.FishXp(sp, weight) * (1f + (ActiveMeal?.XpBonus ?? 0f)));
        if (!Records.TryGetValue(sp.SpeciesName, out float best) || weight > best)
            Records[sp.SpeciesName] = weight;
    }

    public int KeepnetValue() => Keepnet.Sum(f => Tackle.Species(f.Species) is { } sp ? Progression.FishValue(sp, f.Weight) : 0);

    public int SellKeepnet()
    {
        int total = KeepnetValue();
        Money += total;
        Keepnet.Clear();
        return total;
    }

    /// <summary>A broken item leaves the inventory; the starter piece of that kind comes back if nothing else is owned.</summary>
    public void Destroy(string id)
    {
        Owned.Remove(id);
        LineWear.Remove(id);
        HookDull.Remove(id);
        LineTwist.Remove(id);
        if (Rod == id) Rod = Owned.FirstOrDefault(o => Tackle.Rod(o) != null) ?? "";
        if (Line == id) Line = Owned.FirstOrDefault(o => Tackle.Line(o) != null) ?? "";
        if (Hook == id) Hook = Owned.FirstOrDefault(o => Tackle.Hook(o) != null) ?? "";
    }

    /// <summary>When the angler cannot fish and cannot afford to, the shop gives the starter kit back.</summary>
    public bool ClaimFreeKit()
    {
        bool missing = Tackle.Rod(Rod) == null || Tackle.Reel(Reel) == null || Tackle.Line(Line) == null
                       || Tackle.Hook(Hook) == null || BaitCount(Bait) <= 0;
        if (!missing || Money >= 40)
            return false;
        var kit = new Profile();
        foreach (var id in kit.Owned)
            Owned.Add(id);
        if (Tackle.Rod(Rod) == null) Rod = kit.Rod;
        if (Tackle.Reel(Reel) == null) Reel = kit.Reel;
        if (Tackle.Line(Line) == null) Line = kit.Line;
        if (Tackle.Hook(Hook) == null) Hook = kit.Hook;
        if (BaitCount(Bait) <= 0)
        {
            Baits["worm"] = BaitCount("worm") + StarterWorms;
            Bait = "worm";
        }
        return true;
    }

    // ------------------------------------------------------------------ saving
    public static string? SavePath { get; set; }

    public static Profile Load()
    {
        try
        {
            if (SavePath != null && File.Exists(SavePath))
                return JsonConvert.DeserializeObject<Profile>(File.ReadAllText(SavePath)) ?? new Profile();
        }
        catch (Exception)
        {
            // a damaged save starts a new game; the old file is kept beside it
            try { File.Copy(SavePath!, SavePath + ".bad", true); } catch (Exception) { }
        }
        return new Profile();
    }

    public void Save()
    {
        if (SavePath == null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
            string tmp = SavePath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented));
            if (File.Exists(SavePath))
                File.Delete(SavePath);
            File.Move(tmp, SavePath);
        }
        catch (Exception)
        {
            // the next save tries again
        }
    }
}

/// <summary>The shop's catalogue and lookups by id (records from StreamingAssets/data).</summary>
public static class Tackle
{
    private static List<RodData>? _rods;
    private static List<ReelData>? _reels;
    private static List<LineData>? _lines;
    private static List<HookData>? _hooks;
    private static List<BaitData>? _baits;
    private static List<AccessoryData>? _accessories;
    private static List<FishSpecies>? _species;

    private static readonly string[] ShopReels = { "mlynek1000", "beben_sumowy", "fjordline_freespool6000" };

    private static List<T> All<T>(string folder, Func<string, bool> keep) where T : class
    {
        var list = new List<T>();
        string dir = Path.Combine(DataStore.Root, "tackle", folder);
        if (!Directory.Exists(dir))
            return list;
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            if (keep(id) && DataStore.Load<T>($"res://data/tackle/{folder}/{id}.tres") is { } item)
                list.Add(item);
        }
        return list;
    }

    public static IReadOnlyList<RodData> Rods => _rods ??= All<RodData>("rods", id => id.StartsWith("s_"))
        .OrderBy(r => r.RequiredLevel).ThenBy(r => r.Price).ToList();
    public static IReadOnlyList<ReelData> Reels => _reels ??= All<ReelData>("reels", id => id.StartsWith("r_") || ShopReels.Contains(id))
        .OrderBy(r => r.RequiredLevel).ThenBy(r => r.Price).ToList();
    public static IReadOnlyList<LineData> Lines => _lines ??= All<LineData>("lines", id => id.StartsWith("g_"))
        .OrderBy(r => r.RequiredLevel).ThenBy(r => r.Price).ToList();
    public static IReadOnlyList<HookData> Hooks => _hooks ??= All<HookData>("hooks", _ => true)
        .OrderBy(h => h.Size).ToList();
    public static IReadOnlyList<BaitData> Baits => _baits ??= All<BaitData>("baits", _ => true)
        .OrderBy(r => r.RequiredLevel).ThenBy(r => r.Price).ToList();
    public static IReadOnlyList<AccessoryData> Accessories => _accessories ??= All<AccessoryData>("accessories", _ => true)
        .OrderBy(r => r.RequiredLevel).ThenBy(r => r.Price).ToList();
    public static IReadOnlyList<FishSpecies> AllSpecies => _species ??= SpeciesCatalog.Paths
        .Select(p => DataStore.Load<FishSpecies>(p)).Where(s => s != null).Select(s => s!).ToList();

    public static RodData? Rod(string id) => Find(Rods, id) ?? DataStore.Load<RodData>($"res://data/tackle/rods/{id}.tres");
    public static ReelData? Reel(string id) => Find(Reels, id) ?? DataStore.Load<ReelData>($"res://data/tackle/reels/{id}.tres");
    public static LineData? Line(string id) => Find(Lines, id);
    public static HookData? Hook(string id) => Find(Hooks, id);
    public static BaitData? Bait(string id) => Find(Baits, id);
    public static FishSpecies? Species(string name) => AllSpecies.FirstOrDefault(s => s.SpeciesName == name);

    private static T? Find<T>(IEnumerable<T> list, string id) where T : TackleItem =>
        string.IsNullOrEmpty(id) ? null : list.FirstOrDefault(i => i.ItemId == id);
}
