using System;
using System.Collections.Generic;

namespace FishingSim.Core.Methods;

/// <summary>A trophy for a species' best fish, by its weight against the species' biggest.</summary>
public enum TrophyTier { None, Bronze, Silver, Gold }

public static class Trophies
{
    /// <summary>Bronze from 40 % of the species' biggest weight, silver from 60 %, gold from 80 %.</summary>
    public static TrophyTier Of(float weight, float maxWeight)
    {
        if (maxWeight <= 0f || weight <= 0f)
            return TrophyTier.None;
        float k = weight / maxWeight;
        return k >= 0.8f ? TrophyTier.Gold : k >= 0.6f ? TrophyTier.Silver : k >= 0.4f ? TrophyTier.Bronze : TrophyTier.None;
    }

    public static string Name(TrophyTier t) => t switch
    {
        TrophyTier.Gold => "złote trofeum",
        TrophyTier.Silver => "srebrne trofeum",
        TrophyTier.Bronze => "brązowe trofeum",
        _ => "",
    };
}

/// <summary>What a challenge asks for.</summary>
public enum ChallengeKind { CatchCount, CatchSpecies, CatchHeavy, Method, FromBoat, Species }

/// <summary>
/// One challenge: <paramref name="Target"/> fish (or kilograms for CatchHeavy's single fish), for a species
/// (<paramref name="Subject"/>) or a method name; a reward in money and XP. Id is unique per day/week.
/// </summary>
public sealed record Challenge(string Id, ChallengeKind Kind, string Subject, float Target, int Money, int Xp, bool Weekly)
{
    public string Title => Kind switch
    {
        ChallengeKind.CatchCount => $"Złów {Target:0} ryb",
        ChallengeKind.CatchSpecies => $"Złów {Target:0} szt.: {Subject}",
        ChallengeKind.CatchHeavy => $"Złów rybę cięższą niż {Target:0.0} kg",
        ChallengeKind.Method => $"Złów {Target:0} ryb metodą: {Subject}",
        ChallengeKind.FromBoat => $"Złów {Target:0} ryb z łódki",
        ChallengeKind.Species => $"Złów {Target:0} różnych gatunków",
        _ => Id,
    };

    /// <summary>How much this catch adds to the challenge (0 when it does not count).</summary>
    public float Credit(string species, float weight, string method, bool fromBoat) => Kind switch
    {
        ChallengeKind.CatchCount => 1f,
        ChallengeKind.CatchSpecies => species == Subject ? 1f : 0f,
        ChallengeKind.CatchHeavy => weight > Target ? Target : 0f,
        ChallengeKind.Method => method == Subject ? 1f : 0f,
        ChallengeKind.FromBoat => fromBoat ? 1f : 0f,
        _ => 0f,
    };
}

/// <summary>
/// Daily (three) and weekly (one bigger) challenges, the same for a given day/week and lake (seeded), from the lake's
/// commoner species. Pure.
/// </summary>
public static class Challenges
{
    public static readonly string[] Methods = { "spławik", "grunt", "spinning" };

    public static List<Challenge> Daily(int day, int lake, IReadOnlyList<string> commonSpecies)
    {
        var rng = new SystemRng(day * 7919 + lake * 104729 + 11);
        var list = new List<Challenge>();
        string sp = commonSpecies.Count > 0 ? commonSpecies[(int)(rng.NextFloat() * commonSpecies.Count) % commonSpecies.Count] : "Płoć";
        list.Add(new Challenge($"d{day}-count", ChallengeKind.CatchCount, "", 5 + (int)(rng.NextFloat() * 4f), 60, 40, false));
        list.Add(new Challenge($"d{day}-sp", ChallengeKind.CatchSpecies, sp, 2 + (int)(rng.NextFloat() * 2f), 80, 60, false));
        list.Add(rng.NextFloat() < 0.5f
            ? new Challenge($"d{day}-method", ChallengeKind.Method, Methods[(int)(rng.NextFloat() * Methods.Length) % Methods.Length], 3, 90, 70, false)
            : new Challenge($"d{day}-boat", ChallengeKind.FromBoat, "", 3, 90, 70, false));
        return list;
    }

    public static Challenge Weekly(int week, int lake)
    {
        var rng = new SystemRng(week * 6151 + lake * 31 + 3);
        return rng.NextFloat() < 0.5f
            ? new Challenge($"w{week}-heavy", ChallengeKind.CatchHeavy, "", MathF.Round(1.5f + rng.NextFloat() * 2.5f, 1), 400, 300, true)
            : new Challenge($"w{week}-species", ChallengeKind.Species, "", 6 + (int)(rng.NextFloat() * 4f), 400, 300, true);
    }
}
