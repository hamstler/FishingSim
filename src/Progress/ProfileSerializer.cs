using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using FishingSim.Core;
using FileAccess = Godot.FileAccess;

namespace FishingSim.Progress;

/// <summary>
/// JSON persistence for <see cref="Profile"/>. The reader is lenient (numbers may be written as 60 or 60.0, missing
/// keys keep their defaults) so older saves keep working.
/// </summary>
public static class ProfileSerializer
{
    private const int Version = 1;

    public static bool Save(Profile p, string path)
    {
        DirAccess.MakeDirRecursiveAbsolute(path.GetBaseDir());
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f == null)
            return false;
        f.StoreString(JsonText.Pretty(ToJson(p)));
        return true;
    }

    public static JsonObject ToJson(Profile p)
    {
        var keepnet = new JsonArray();
        foreach (var c in p.Keepnet)
            keepnet.Add(Fish(c));
        var smoked = new JsonArray();
        foreach (var c in p.Smoked)
            smoked.Add(Fish(c));
        var smokery = new JsonArray();
        foreach (var lot in p.Smokery)
        {
            var o = Fish(lot.Fish);
            o["started_at"] = lot.StartedAt;
            smokery.Add(o);
        }
        var notices = new JsonArray();
        foreach (var r in p.Notices)
        {
            notices.Add(new JsonObject
            {
                ["kind"] = r.Kind.ToString(), ["item"] = r.Item, ["item_name"] = r.ItemName, ["count"] = r.Count, ["min_weight"] = r.MinWeight,
                ["reward"] = r.Reward, ["xp"] = r.Xp, ["from"] = r.From, ["text"] = r.Text, ["done"] = r.Done,
            });
        }

        var journal = new JsonArray();
        foreach (var e in p.Journal)
        {
            var o = new JsonObject { ["species"] = e.Species, ["weight"] = e.Weight };
            if (e.Location != null) o["location"] = e.Location;
            if (e.Bait != null) o["bait"] = e.Bait;
            if (e.Hook != null) o["hook"] = e.Hook;
            if (e.Depth != null) o["depth"] = e.Depth;
            if (e.Hour != null) o["hour"] = e.Hour;
            journal.Add(o);
        }

        var tournaments = new JsonArray();
        foreach (var t in p.TournamentHistory)
        {
            tournaments.Add(new JsonObject
            {
                ["name"] = t.Name, ["rank"] = t.Rank, ["entrants"] = t.Entrants, ["score"] = t.Score,
                ["prize_money"] = t.PrizeMoney, ["prize_xp"] = t.PrizeXp,
            });
        }

        var owned = new JsonObject();
        foreach (var (slot, ids) in p.Owned)
        {
            var arr = new JsonArray();
            foreach (var id in ids) arr.Add(id);
            owned[slot] = arr;
        }

        return new JsonObject
        {
            ["version"] = Version,
            ["money"] = p.Money,
            ["xp"] = p.Xp,
            ["level"] = p.Level,
            ["keepnet"] = keepnet,
            ["records"] = FromMap(p.Records, v => v),
            ["catch_counts"] = FromMap(p.CatchCounts, v => v),
            ["total_catches"] = p.TotalCatches,
            ["journal"] = journal,
            ["tournaments"] = tournaments,
            ["owned"] = owned,
            ["bait_counts"] = FromMap(p.BaitCounts, v => v),
            ["equipped"] = FromMap(p.Equipped, v => v),
            ["gear"] = new JsonObject
            {
                ["owned"] = FromMap(p.Gear.Owned, ids =>
                {
                    var arr = new JsonArray();
                    foreach (var id in ids) arr.Add(id);
                    return arr;
                }),
                ["equipped"] = FromMap(p.Gear.Equipped, v => v),
            },
            ["market"] = new JsonObject
            {
                ["hours"] = p.Market.Hours,
                ["saturation"] = FromMap(p.Market.Saturation, v => v),
            },
            ["pantry"] = FromMap(p.Pantry, v => v),
            ["smoked"] = smoked,
            ["smokery"] = smokery,
            ["notices"] = new JsonObject { ["day"] = p.NoticesDay, ["place"] = p.NoticesPlace, ["list"] = notices },
            ["location_idx"] = p.LocationIdx,
            ["hour"] = p.Hour,
            ["meal"] = p.ActiveMeal is { } meal
                ? new JsonObject
                {
                    ["name"] = meal.Name, ["hours"] = meal.Hours, ["xp_bonus"] = meal.XpBonus, ["bite_bonus"] = meal.BiteBonus,
                    ["left"] = p.MealHoursLeft,
                }
                : null,
        };
    }

    private static JsonObject Fish(CatchRecord c) => new()
    {
        ["species"] = c.Species, ["weight"] = c.Weight, ["value"] = c.Value, ["condition"] = c.Condition, ["undersized"] = c.Undersized,
    };

    private static CatchRecord? ReadFish(JsonNode? n) =>
        n is JsonObject o && Str(o["species"]) is string sp
            ? new CatchRecord(sp, Float(o["weight"], 0f), Int(o["value"], 0), Float(o["condition"], 1f),
                o["undersized"] is JsonValue u && u.TryGetValue<bool>(out var und) && und)
            : null;

    private static JsonObject FromMap<T>(Dictionary<string, T> map, Func<T, JsonNode?> convert)
    {
        var o = new JsonObject();
        foreach (var (k, v) in map)
            o[k] = convert(v);
        return o;
    }

    /// <summary>Loads a save file into <paramref name="p"/>; a missing or broken file leaves the defaults.</summary>
    public static void LoadInto(Profile p, string path)
    {
        if (!FileAccess.FileExists(path))
            return;
        try
        {
            if (JsonNode.Parse(FileAccess.GetFileAsString(path)) is JsonObject root)
                Apply(p, root);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            GD.PushWarning($"Save file {path} is unreadable: {e.Message}");
        }
    }

    public static Profile FromJson(string json)
    {
        var p = new Profile();
        if (JsonNode.Parse(json) is JsonObject root)
            Apply(p, root);
        return p;
    }

    private static void Apply(Profile p, JsonObject d)
    {
        p.Reset();
        p.Money = Int(d["money"], p.Money);
        p.Xp = Int(d["xp"], p.Xp);
        p.Level = Int(d["level"], p.Level);
        p.TotalCatches = Int(d["total_catches"], 0);
        p.LocationIdx = Int(d["location_idx"], 0);
        p.Hour = Float(d["hour"], p.Hour);

        if (d["keepnet"] is JsonArray net)
            foreach (var n in net)
                if (ReadFish(n) is { } c)
                    p.Keepnet.Add(c);
        if (d["smoked"] is JsonArray smoked)
            foreach (var n in smoked)
                if (ReadFish(n) is { } c)
                    p.Smoked.Add(c);
        if (d["smokery"] is JsonArray smokery)
            foreach (var n in smokery)
                if (ReadFish(n) is { } c)
                    p.Smokery.Add(new SmokingLot(c, Float(n!["started_at"], 0f)));
        ReadMap(d["pantry"], p.Pantry, n => Int(n, 0));
        if (d["notices"] is JsonObject board && board["list"] is JsonArray noticeList)
        {
            p.NoticesDay = Int(board["day"], -1);
            p.NoticesPlace = Int(board["place"], -1);
            foreach (var n in noticeList)
            {
                if (n is not JsonObject o || !Enum.TryParse<Core.Camp.RequestKind>(Str(o["kind"]), out var kind))
                    continue;
                p.Notices.Add(new Core.Camp.VillageRequest(kind, Str(o["item"]) ?? "", Str(o["item_name"]) ?? "", Int(o["count"], 1),
                    Float(o["min_weight"], 0f), Int(o["reward"], 0), Int(o["xp"], 0), Str(o["from"]) ?? "", Str(o["text"]) ?? "",
                    o["done"] is JsonValue dv && dv.TryGetValue<bool>(out var done) && done));
            }
        }
        if (d["journal"] is JsonArray journal)
        {
            foreach (var n in journal)
            {
                if (n is JsonObject o && o["species"] is JsonNode sp)
                {
                    p.Journal.Add(new JournalEntry(
                        sp.GetValue<string>(), Float(o["weight"], 0f), Str(o["location"]), Str(o["bait"]), Str(o["hook"]),
                        o["depth"] is JsonNode dep ? Float(dep, 0f) : null, Str(o["hour"])));
                }
            }
        }
        if (d["tournaments"] is JsonArray tournaments)
        {
            foreach (var n in tournaments)
            {
                if (n is JsonObject o && o["name"] is JsonNode name)
                {
                    p.TournamentHistory.Add(new TournamentRecord(
                        name.GetValue<string>(), Int(o["rank"], 0), Int(o["entrants"], 0), Float(o["score"], 0f),
                        Int(o["prize_money"], 0), Int(o["prize_xp"], 0)));
                }
            }
        }
        ReadMap(d["records"], p.Records, n => Float(n, 0f));
        ReadMap(d["catch_counts"], p.CatchCounts, n => Int(n, 0));
        if (d["owned"] is JsonObject owned)
        {
            foreach (var slot in new List<string>(p.Owned.Keys))
            {
                if (owned[slot] is JsonArray ids)
                {
                    var list = new List<string>();
                    foreach (var id in ids)
                        if (id != null) list.Add(id.GetValue<string>());
                    p.Owned[slot] = list;
                }
            }
        }
        if (d["bait_counts"] is JsonObject baits && baits.Count > 0)
        {
            p.BaitCounts.Clear();
            ReadMap(baits, p.BaitCounts, n => Int(n, 0));
        }
        if (d["equipped"] is JsonObject eq)
        {
            foreach (var (k, v) in eq)
                if (v != null) p.Equipped[k] = v.GetValue<string>();
        }
        if (d["meal"] is JsonObject m && Str(m["name"]) is string mealName)
        {
            p.Meal = new Core.Camp.MealBuff(mealName, Float(m["hours"], 1f), Float(m["xp_bonus"], 0f), Float(m["bite_bonus"], 0f));
            p.MealHoursLeft = Float(m["left"], 0f);
        }
        // Saves from before keepnets/landing nets keep the starter gear that Reset() gave them.
        if (d["gear"] is JsonObject gear)
        {
            if (gear["owned"] is JsonObject gearOwned)
            {
                foreach (var (slot, node) in gearOwned)
                {
                    if (node is not JsonArray ids)
                        continue;
                    p.Gear.Owned.TryAdd(slot, new());
                    foreach (var id in ids)
                        if (Str(id) is string s && !p.Gear.Owned[slot].Contains(s)) p.Gear.Owned[slot].Add(s);
                }
            }
            if (gear["equipped"] is JsonObject gearEq)
            {
                foreach (var (k, v) in gearEq)
                    if (Str(v) is string s) p.Gear.Equipped[k] = s;
            }
        }
        if (d["market"] is JsonObject market)
        {
            p.Market.Hours = Float(market["hours"], 0f);
            ReadMap(market["saturation"], p.Market.Saturation, n => Float(n, 0f));
        }
    }

    private static void ReadMap<T>(JsonNode? node, Dictionary<string, T> into, Func<JsonNode?, T> convert)
    {
        if (node is not JsonObject o)
            return;
        into.Clear();
        foreach (var (k, v) in o)
            into[k] = convert(v);
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static float Float(JsonNode? n, float fallback)
    {
        if (n is not JsonValue v)
            return fallback;
        if (v.TryGetValue<double>(out var d)) return (float)d;
        if (v.TryGetValue<float>(out var f)) return f;
        if (v.TryGetValue<int>(out var i)) return i;
        return fallback;
    }

    private static int Int(JsonNode? n, int fallback)
    {
        if (n is not JsonValue v)
            return fallback;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (int)Math.Round(d, MidpointRounding.AwayFromZero);
        return fallback;
    }
}
