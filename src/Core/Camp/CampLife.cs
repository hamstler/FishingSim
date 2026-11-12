using System;
using System.Collections.Generic;
using System.Linq;

namespace FishingSim.Core.Camp;

/// <summary>What lies under the angler's feet, as far as digging and insects care.</summary>
public enum GroundKind { Meadow, Forest, Mud, Sand, Path, Rock, Water }

/// <summary>What a dig or a sweep of the net brought: bait id -> pieces, and a line for the HUD.</summary>
public sealed record Haul(IReadOnlyDictionary<string, int> Baits, string Message)
{
    public int Total => Baits.Values.Sum();

    public static Haul Empty(string message) => new(new Dictionary<string, int>(), message);
}

/// <summary>
/// Digging for bait with a shovel. Earthworms live in moist, rich soil: a damp bank by the water and a meadow are best,
/// the forest floor gives fewer worms but chafer grubs among the roots, sand hardly anything, a trodden path or rock
/// nothing. Night crawlers (rosówki) come up at night and after rain, and rain makes every worm easier to find. A spot
/// that was dug already gives nothing again.
/// </summary>
public static class Digging
{
    public const string Worm = "worm", NightCrawler = "rosowka", Grub = "pedrak";

    /// <summary>How close to an earlier hole (m) a new one counts as the same spot.</summary>
    public const float SpotRadius = 1.2f;

    public static bool CanDig(GroundKind g) => g is GroundKind.Meadow or GroundKind.Forest or GroundKind.Mud or GroundKind.Sand;

    public static Haul Dig(GroundKind ground, float hour, Weather weather, bool spotUsed, IRng rng)
    {
        if (!CanDig(ground))
            return Haul.Empty(ground switch
            {
                GroundKind.Path => "Ubita ścieżka: łopata nie wchodzi.",
                GroundKind.Rock => "Same kamienie.",
                _ => "Tu nie da się kopać.",
            });
        if (spotUsed)
            return Haul.Empty("Tu już kopałeś: spróbuj obok.");
        bool night = hour < 5f || hour >= 21f;
        bool wet = weather == Weather.Rain;
        float richness = ground switch
        {
            GroundKind.Mud => 1.25f,
            GroundKind.Meadow => 1f,
            GroundKind.Forest => 0.6f,
            _ => 0.3f,
        } * (wet ? 1.5f : 1f) * (night ? 1.2f : 1f);
        var baits = new Dictionary<string, int>();
        // A few worms in a good spade of soil; a bad one turns up nothing.
        int worms = (int)MathF.Floor(rng.Range(0f, 4.2f) * richness);
        if (worms > 0)
            baits[Worm] = worms;
        if ((night || wet) && ground is GroundKind.Meadow or GroundKind.Mud && rng.NextFloat() < (night && wet ? 0.7f : 0.4f))
            baits[NightCrawler] = 1 + (rng.NextFloat() < 0.3f ? 1 : 0);
        if (ground == GroundKind.Forest && rng.NextFloat() < 0.45f)
            baits[Grub] = 1 + (rng.NextFloat() < 0.25f ? 1 : 0);
        return new Haul(baits, baits.Count == 0 ? "Pusta ziemia: tylko korzonki." : "Wykopano: " + Describe(baits));
    }

    public static string Describe(IReadOnlyDictionary<string, int> baits) =>
        string.Join(", ", baits.Select(b => $"{Names.GetValueOrDefault(b.Key, b.Key)} ×{b.Value}"));

    public static readonly Dictionary<string, string> Names = new()
    {
        [Worm] = "robak", [NightCrawler] = "rosówka", [Grub] = "pędrak", [Sweeping.Mayfly] = "jętka",
        [Sweeping.Grasshopper] = "konik polny", [Sweeping.Beetle] = "chrabąszcz",
    };
}

/// <summary>Where a sweep of the insect net goes.</summary>
public sealed record SweepContext(GroundKind Ground, bool NearWater, bool NearTrees, float Hour, Weather Weather, float Swarm);

/// <summary>
/// Catching insects with a net: mayflies (jętki) dance over the water's edge from late afternoon to dusk in calm
/// weather (Swarm says how thick the swarm is right there, 0..1), grasshoppers jump in the meadow grass on warm days,
/// chafers buzz round the trees at dusk. Rain grounds them all.
/// </summary>
public static class Sweeping
{
    public const string Mayfly = "jetka", Grasshopper = "insect", Beetle = "chrabaszcz";

    /// <summary>The mayflies' hours: from five in the afternoon until it is dark.</summary>
    public static float MayflyActivity(float hour, Weather weather)
    {
        if (weather == Weather.Rain)
            return 0f;
        float rise = Smooth(16f, 18f, hour), fall = 1f - Smooth(20.5f, 22f, hour);
        return rise * fall * (weather == Weather.Cloudy ? 0.8f : 1f);
    }

    public static Haul Sweep(SweepContext c, IRng rng)
    {
        if (c.Weather == Weather.Rain)
            return Haul.Empty("W deszczu owady siedzą schowane.");
        var baits = new Dictionary<string, int>();
        if (c.NearWater && c.Swarm > 0.05f)
        {
            int n = (int)MathF.Round(rng.Range(0.5f, 4.5f) * c.Swarm);
            if (n > 0)
                baits[Mayfly] = n;
        }
        bool warm = c.Hour >= 10f && c.Hour < 18.5f && c.Weather == Weather.Clear;
        if (c.Ground == GroundKind.Meadow && (warm || c.Hour >= 9f && c.Hour < 19f) && rng.NextFloat() < (warm ? 0.65f : 0.3f))
            baits[Grasshopper] = 1 + (rng.NextFloat() < 0.35f ? 1 : 0);
        bool dusk = c.Hour >= 19.5f && c.Hour < 22f;
        if (c.NearTrees && dusk && rng.NextFloat() < 0.4f)
            baits[Beetle] = 1;
        string empty = c.NearWater && MayflyActivity(c.Hour, c.Weather) <= 0f
            ? "Pusto. Jętki tańczą nad wodą wieczorem."
            : "Siatka pusta.";
        return new Haul(baits, baits.Count == 0 ? empty : "Złapano: " + Digging.Describe(baits));
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

/// <summary>A meal's lasting effect: more XP per fish and a quicker bite, for some hours of game time.</summary>
public sealed record MealBuff(string Name, float Hours, float XpBonus, float BiteBonus);

/// <summary>A dish cooked at a campfire from fish in the keepnet.</summary>
public sealed record Recipe(string Id, string Name, string Description, bool NeedsPot, int MinFish, float MinKg, float Seconds, MealBuff Buff);

/// <summary>
/// Cooking fish over a campfire: a fish roasted on a stick (any campfire), a fish soup in a pot (the pot from the
/// shop, two fish or more). The cook takes the least valuable fish that make the dish (undersized ones first: they
/// cannot be sold anyway), and the meal leaves a buff: the well-fed angler learns more from each fish and waits a
/// little less for a bite.
/// </summary>
public static class Cooking
{
    public static readonly Recipe Roast = new("roast", "Ryba z ogniska",
        "Jedna ryba na kiju nad żarem, z solą.", false, 1, 0.12f, 8f,
        new MealBuff("Syty", 2f, 0.10f, 0.05f));

    public static readonly Recipe Soup = new("soup", "Zupa rybna",
        "Dwie ryby lub więcej w garnku, z cebulą i zielem.", true, 2, 0.6f, 12f,
        new MealBuff("Rozgrzany zupą", 5f, 0.15f, 0.12f));

    public static IReadOnlyList<Recipe> All { get; } = new[] { Roast, Soup };

    /// <summary>A fish in the keepnet: index, weight, value, undersized.</summary>
    public readonly record struct Ingredient(int Index, float Weight, int Value, bool Undersized);

    /// <summary>Why the dish cannot be made, or null when it can.</summary>
    public static string? Missing(Recipe r, IReadOnlyList<Ingredient> fish, bool hasPot)
    {
        if (r.NeedsPot && !hasPot)
            return "Potrzebny garnek (kram).";
        if (Pick(r, fish) == null)
            return fish.Count == 0 ? "Siatka jest pusta." : r.MinFish > 1 ? $"Za mało ryb: {r.MinFish} lub więcej, razem {r.MinKg:0.0} kg." : $"Potrzebna ryba od {r.MinKg * 1000f:0} g.";
        return null;
    }

    /// <summary>The fish the dish takes (indices into the keepnet), or null when there are not enough.</summary>
    public static List<int>? Pick(Recipe r, IReadOnlyList<Ingredient> fish)
    {
        var order = fish.OrderByDescending(f => f.Undersized).ThenBy(f => f.Value).ThenBy(f => f.Weight).ToList();
        var chosen = new List<int>();
        float kg = 0f;
        if (r.MinFish == 1)
        {
            // One fish big enough to eat: the cheapest of those.
            foreach (var f in order)
                if (f.Weight >= r.MinKg)
                    return new List<int> { f.Index };
            return null;
        }
        foreach (var f in order)
        {
            chosen.Add(f.Index);
            kg += f.Weight;
            if (chosen.Count >= r.MinFish && kg >= r.MinKg)
                return chosen;
        }
        return null;
    }
}
