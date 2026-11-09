using System;
using System.Collections.Generic;
using System.Linq;
using FishingSim.Data;

namespace FishingSim.Core;

/// <summary>Everything that decides which fish is interested right now.</summary>
/// <param name="BaitId">Id of the equipped bait.</param>
/// <param name="HookSize">Size of the equipped hook (1..10).</param>
/// <param name="Depth">Water depth under the bobber, in metres.</param>
/// <param name="Location">Current location (abundance per species), or null to ignore it.</param>
/// <param name="Period">Time of day, or null to ignore time and weather.</param>
/// <param name="Weather">Current weather (used together with <paramref name="Period"/>).</param>
public readonly record struct BiteContext(
    string BaitId,
    float HookSize,
    float Depth,
    LocationData? Location = null,
    DayPeriod? Period = null,
    Weather Weather = Weather.Clear);

/// <summary>One species' share of the bites.</summary>
public readonly record struct BiteChance(FishSpecies Species, float Weight, float Share);

/// <summary>
/// Bite chance = rarity x bait x hook fit x location abundance x depth fit x time of day x weather.
/// All functions are pure, so they are covered by unit tests.
/// </summary>
public static class BiteModel
{
    public const float MinWait = 0.2f;
    public const float MaxWaitDivisor = 2.0f;

    /// <summary>1 for a perfectly sized hook, falling to 0.05 for a hook 6+ sizes off.</summary>
    public static float HookFit(FishSpecies sp, float hookSize) =>
        Math.Clamp(1f - MathF.Abs(hookSize - sp.MouthSize) / 6f, 0.05f, 1f);

    /// <summary>Preferred depth range; each metre outside costs 40%.</summary>
    public static float DepthFit(FishSpecies sp, float depth)
    {
        float outside = MathF.Max(MathF.Max(sp.MinDepth - depth, depth - sp.MaxDepth), 0f);
        return Math.Clamp(1f - outside * 0.4f, 0.03f, 1f);
    }

    public static float TimeMultiplier(FishSpecies sp, DayPeriod period) => period switch
    {
        DayPeriod.Twilight => MathF.Max(sp.DayActivity, sp.NightActivity) * sp.TwilightBonus,
        DayPeriod.Night => sp.NightActivity,
        _ => sp.DayActivity,
    };

    /// <summary>Location abundance x depth x time of day x weather.</summary>
    public static float Habitat(FishSpecies sp, BiteContext ctx)
    {
        float m = 1f;
        if (ctx.Location != null)
            m *= ctx.Location.AbundanceOf(sp.SpeciesName) * DepthFit(sp, ctx.Depth);
        if (ctx.Period is DayPeriod period)
            m *= TimeMultiplier(sp, period) * sp.WeatherMultiplier(ctx.Weather.Key());
        return m;
    }

    public static float Weight(FishSpecies sp, BiteContext ctx) =>
        sp.Rarity * sp.AffinityFor(ctx.BaitId) * HookFit(sp, ctx.HookSize) * Habitat(sp, ctx);

    /// <summary>Average attractiveness over all species (1 = neutral); drives how fast bites come.</summary>
    public static float Attraction(IReadOnlyList<FishSpecies> species, BiteContext ctx)
    {
        float rarity = 0f, sum = 0f;
        foreach (var sp in species)
        {
            rarity += sp.Rarity;
            sum += Weight(sp, ctx);
        }
        return rarity > 0f ? sum / rarity : 1f;
    }

    /// <summary>Seconds until the next bite.</summary>
    public static float WaitSeconds(IRng rng, float minWait, float maxWait, float lineVisibility, float attraction) =>
        rng.Range(minWait, maxWait) * lineVisibility / Math.Clamp(attraction, MinWait, MaxWaitDivisor);

    /// <summary>Share of bites per species, sorted descending. All shares are 0 when nothing wants this bait.</summary>
    public static IReadOnlyList<BiteChance> Chances(IReadOnlyList<FishSpecies> species, BiteContext ctx)
    {
        var weights = species.Select(s => (Species: s, Weight: Weight(s, ctx))).ToList();
        float total = weights.Sum(w => w.Weight);
        return weights
            .Select(w => new BiteChance(w.Species, w.Weight, total > 1e-4f ? w.Weight / total : 0f))
            .OrderByDescending(c => c.Share)
            .ToList();
    }

    /// <summary>Picks the species that bites, or null when nothing here wants this bait/hook.</summary>
    public static FishSpecies? Pick(IReadOnlyList<FishSpecies> species, BiteContext ctx, IRng rng)
    {
        float total = 0f;
        foreach (var sp in species)
            total += Weight(sp, ctx);
        if (total <= 1e-4f)
            return null;

        float roll = rng.NextFloat() * total;
        FishSpecies? chosen = null;
        foreach (var sp in species)
        {
            float w = Weight(sp, ctx);
            if (w <= 0f)
                continue;
            chosen = sp;
            roll -= w;
            if (roll <= 0f)
                break;
        }
        return chosen;
    }

    /// <summary>Skewed towards small fish; bigger hooks flatten the curve so bigger fish show up.</summary>
    public static float RollWeight(FishSpecies sp, float hookSize, IRng rng)
    {
        float exponent = Math.Clamp(2.6f - hookSize * 0.15f, 1f, 2.6f);
        return sp.MinWeight + (sp.MaxWeight - sp.MinWeight) * MathF.Pow(rng.NextFloat(), exponent);
    }
}
