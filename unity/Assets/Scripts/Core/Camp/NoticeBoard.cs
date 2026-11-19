using System;
using System.Collections.Generic;
using System.Linq;

namespace FishingSim.Core.Camp;

/// <summary>What a villager's notice asks for.</summary>
public enum RequestKind { Fish, Bait, Forage, Smoked }

/// <summary>
/// One notice on the village board: <paramref name="Item"/> is a species name (Fish), a bait id (Bait), a pantry id
/// (Forage) or empty (Smoked: any smoked fish). Fish must weigh at least <paramref name="MinWeight"/>.
/// </summary>
public sealed record VillageRequest(RequestKind Kind, string Item, string ItemName, int Count, float MinWeight, int Reward, int Xp,
    string From, string Text, bool Done = false)
{
    /// <summary>What is asked for, short: "Okoń ×2 (od 0,25 kg)".</summary>
    public string Wanted => Kind switch
    {
        RequestKind.Fish => $"{ItemName} ×{Count} (od {MinWeight:0.00} kg)",
        RequestKind.Smoked => $"wędzona ryba ×{Count}",
        _ => $"{ItemName} ×{Count}",
    };
}

/// <summary>A species the notices may ask for at this lake: how common it is there (abundance × rarity), its legal
/// weight and price.</summary>
public readonly record struct RequestFish(string Name, float LegalWeight, float PricePerKg, float Commonness);

/// <summary>
/// The notice board (tablica ogłoszeń) on the village square: every game day three villagers pin up what they need,
/// fish of a species and size for dinner, bait for a grandson, mushrooms or berries, a smoked fish for a name day. The
/// day's notices follow from the day and the place (the same for everyone), and pay in money and XP when handed in.
/// </summary>
public static class NoticeBoard
{
    public const int PerDay = 3;

    public static readonly string[] Folk =
    {
        "Sołtys Bronisław", "Pani Halina z karczmy", "Dziadek Wacław", "Kowal Zbyszek", "Babcia Stasia", "Rybak Edek",
        "Leśniczy Jerzy", "Gospodyni Krysia", "Młynarz Antoni", "Pani Zosia", "Proboszcz", "Nauczycielka Ewa",
    };

    // Bait the angler can gather himself: id, name, count range, reward per piece.
    private static readonly (string Id, string Name, int Min, int Max, int Per)[] Baits =
    {
        ("worm", "robaki", 10, 20, 2), (Digging.NightCrawler, "rosówki", 4, 8, 5), (Sweeping.Mayfly, "jętki", 5, 10, 4),
        (LiftNetting.LiveBait, "żywce", 4, 8, 7), (Digging.Grub, "pędraki", 2, 4, 6), (Sweeping.Grasshopper, "koniki polne", 3, 6, 4),
    };

    private static readonly (string Id, int Min, int Max, int Per)[] Pickings =
    {
        (Foraging.Porcini, 2, 4, 18), (Foraging.Chanterelle, 8, 14, 6), (Foraging.Blueberry, 25, 40, 2),
    };

    private static readonly string[] FishLines =
    {
        "Szukam ryby na niedzielny obiad.", "Goście przyjeżdżają, a w spiżarni pusto.", "Kot nie chce jeść nic innego.",
        "Na smażenie, z masłem i koperkiem.", "Na galaretę dla teściowej.",
    };

    private static readonly string[] BaitLines =
    {
        "Wnuk jedzie na ryby, a nie ma na co łowić.", "Mój kręgosłup już nie dla łopaty.", "Na jutrzejsze zawody w sąsiedniej wsi.",
    };

    private static readonly string[] ForageLines =
    {
        "Na suszenie na zimę.", "Do pierogów.", "Na sos do ziemniaków.", "Na przetwory, póki sezon.",
    };

    private static readonly string[] SmokedLines =
    {
        "Na imieniny sąsiada.", "Na wesele kuzyna: zakąska musi być.", "Do piwa na wieczór przy ognisku.",
    };

    /// <summary>The day's notices at a place (<paramref name="place"/> = the map's index). Smoked fish is asked for only
    /// where there is a smokehouse.</summary>
    public static List<VillageRequest> Daily(int day, int place, IReadOnlyList<RequestFish> fish, bool smokehouse)
    {
        var rng = new SystemRng(Seed(day, place));
        var kinds = new List<RequestKind> { RequestKind.Fish };
        var pool = new List<RequestKind> { RequestKind.Bait, RequestKind.Forage, RequestKind.Fish, RequestKind.Bait, RequestKind.Forage };
        if (smokehouse)
            pool.Add(RequestKind.Smoked);
        while (kinds.Count < PerDay)
        {
            var k = pool[(int)(rng.NextFloat() * pool.Count) % pool.Count];
            pool.Remove(k);
            kinds.Add(k);
        }
        var folk = Folk.OrderBy(_ => rng.NextFloat()).ToList();
        var list = new List<VillageRequest>();
        var usedFish = new HashSet<string>();
        for (int i = 0; i < kinds.Count; i++)
        {
            var r = Make(kinds[i], folk[i], fish, usedFish, rng);
            if (r != null)
                list.Add(r);
        }
        return list;
    }

    private static VillageRequest? Make(RequestKind kind, string from, IReadOnlyList<RequestFish> fish, HashSet<string> usedFish, IRng rng)
    {
        string Line(string[] lines) => lines[(int)(rng.NextFloat() * lines.Length) % lines.Length];
        switch (kind)
        {
            case RequestKind.Fish:
            {
                // Common fish of this lake, weighted by how often they are caught.
                var candidates = fish.Where(f => f.Commonness > 0f && !usedFish.Contains(f.Name)).ToList();
                if (candidates.Count == 0)
                    return Make(RequestKind.Bait, from, fish, usedFish, rng);
                float total = candidates.Sum(f => f.Commonness);
                float u = rng.NextFloat() * total;
                var pick = candidates[^1];
                foreach (var f in candidates)
                {
                    if (u < f.Commonness)
                    {
                        pick = f;
                        break;
                    }
                    u -= f.Commonness;
                }
                usedFish.Add(pick.Name);
                float share = pick.Commonness / total;
                int count = share > 0.15f ? 1 + (int)(rng.NextFloat() * 3f) : share > 0.05f ? 1 + (int)(rng.NextFloat() * 2f) : 1;
                float min = MathF.Round(MathF.Max(pick.LegalWeight, 0.12f) * rng.Range(1f, 1.5f) / 0.05f) * 0.05f;
                int reward = RoundTo5(count * min * pick.PricePerKg * 2.2f + 20f);
                return new VillageRequest(kind, pick.Name, pick.Name, count, min, reward, 20 + 12 * count, from,
                    $"{Line(FishLines)} Potrzebuję: {pick.Name} ×{count}, każda od {min:0.00} kg.");
            }
            case RequestKind.Bait:
            {
                var b = Baits[(int)(rng.NextFloat() * Baits.Length) % Baits.Length];
                int count = b.Min + (int)(rng.NextFloat() * (b.Max - b.Min + 1));
                return new VillageRequest(kind, b.Id, b.Name, count, 0f, RoundTo5(count * b.Per + 10), 15 + count, from,
                    $"{Line(BaitLines)} Kupię {b.Name} ×{count}.");
            }
            case RequestKind.Forage:
            {
                var p = Pickings[(int)(rng.NextFloat() * Pickings.Length) % Pickings.Length];
                int count = p.Min + (int)(rng.NextFloat() * (p.Max - p.Min + 1));
                string name = Foraging.NameOf(p.Id);
                return new VillageRequest(kind, p.Id, name, count, 0f, RoundTo5(count * p.Per + 10), 20 + count, from,
                    $"{Line(ForageLines)} Przynieście {name} ×{count} z lasu.");
            }
            default:
            {
                int count = 1 + (rng.NextFloat() < 0.3f ? 1 : 0);
                return new VillageRequest(kind, "", "wędzona ryba", count, 0f, 70 * count, 40 * count, from,
                    $"{Line(SmokedLines)} Kupię wędzoną rybę ×{count}, jaka będzie.");
            }
        }
    }

    private static int RoundTo5(float x) => Math.Max(5, (int)MathF.Round(x / 5f) * 5);

    private static int Seed(int day, int place)
    {
        unchecked
        {
            int h = 0x2545F491;
            h = h * 31 + day;
            h = h * 31 + place;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            return h ^ (h >> 15);
        }
    }

    /// <summary>True when this fish from the keepnet can be handed in for a fish notice.</summary>
    public static bool FishFits(VillageRequest r, string species, float weight, bool undersized) =>
        r.Kind == RequestKind.Fish && species == r.Item && weight >= r.MinWeight && !undersized;
}
