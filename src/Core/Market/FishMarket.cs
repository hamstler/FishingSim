using System;
using System.Collections.Generic;

namespace FishingSim.Core.Market;

/// <summary>One fish offered for sale. <paramref name="TrophyWeight"/> is the weight from which a fish counts as a trophy.</summary>
public readonly record struct FishLot(string Species, float Weight, float BasePricePerKg, float TrophyWeight, float Condition = 1f);

/// <summary>
/// Fish market (targ rybny) with live prices. Unlike the fixed-price buyer (skup) it pays a premium for fresh and trophy
/// fish, but every kilo of a species sold floods the market and lowers its price until demand recovers over time.
/// Prices also drift from day to day. Pure logic: no engine types, deterministic for a given seed.
/// </summary>
public sealed class FishMarket
{
    /// <summary>Price multiplier over the fixed buyer when demand is full and the day is average.</summary>
    public const float Premium = 1.25f;

    /// <summary>Demand lost per kilogram sold of one species.</summary>
    public float SaturationPerKg { get; init; } = 0.03f;

    /// <summary>Saturation that recovers per in-game hour.</summary>
    public float RecoveryPerHour { get; init; } = 0.05f;

    /// <summary>Demand never falls below this share of the full price.</summary>
    public float MinDemand { get; init; } = 0.35f;

    /// <summary>± range of the daily price drift (0.15 = ±15%).</summary>
    public float DailySwing { get; init; } = 0.15f;

    private readonly int _seed;

    /// <summary>Species -> saturation 0..(1 - MinDemand). Saved with the profile.</summary>
    public Dictionary<string, float> Saturation { get; } = new();

    /// <summary>In-game hours since the market opened; the day number drives the daily drift.</summary>
    public float Hours { get; set; }

    public int Day => (int)MathF.Floor(Hours / 24f);

    public FishMarket(int seed = 1234) => _seed = seed;

    public void AdvanceHours(float hours)
    {
        if (hours <= 0f)
            return;
        Hours += hours;
        foreach (var key in new List<string>(Saturation.Keys))
        {
            float s = Saturation[key] - RecoveryPerHour * hours;
            if (s <= 0f)
                Saturation.Remove(key);
            else
                Saturation[key] = s;
        }
    }

    /// <summary>0..1 share of the full price that buyers pay today for this species (before freshness/trophy).</summary>
    public float Demand(string species) => MathF.Max(MinDemand, 1f - Saturation.GetValueOrDefault(species));

    /// <summary>Deterministic per-day, per-species price drift in [1 - swing, 1 + swing].</summary>
    public float DailyFactor(string species)
    {
        unchecked
        {
            int h = _seed;
            foreach (char c in species)
                h = h * 31 + c;
            h = h * 31 + Day;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            float u = (h & 0xFFFF) / 65535f;   // 0..1
            return 1f + (u * 2f - 1f) * DailySwing;
        }
    }

    /// <summary>Bonus for big fish: x1.2 from 75% of the trophy weight, x1.5 for a trophy.</summary>
    public static float TrophyMultiplier(float weight, float trophyWeight)
    {
        if (trophyWeight <= 0f)
            return 1f;
        float r = weight / trophyWeight;
        return r >= 1f ? 1.5f : r >= 0.75f ? 1.2f : 1f;
    }

    /// <summary>Tired or dead fish from a poor keepnet are worth less: 30% at condition 0, 100% when fresh.</summary>
    public static float ConditionMultiplier(float condition) => 0.3f + 0.7f * Math.Clamp(condition, 0f, 1f);

    /// <summary>Price the market would pay for a fish right now (zł).</summary>
    public int Quote(FishLot lot)
    {
        float price = lot.Weight * lot.BasePricePerKg * Premium
                      * Demand(lot.Species) * DailyFactor(lot.Species)
                      * TrophyMultiplier(lot.Weight, lot.TrophyWeight) * ConditionMultiplier(lot.Condition);
        return (int)MathF.Round(MathF.Max(price, 0f), MidpointRounding.AwayFromZero);
    }

    /// <summary>Sells one fish: returns the money paid and saturates the market for that species.</summary>
    public int Sell(FishLot lot)
    {
        int paid = Quote(lot);
        float s = Saturation.GetValueOrDefault(lot.Species) + lot.Weight * SaturationPerKg;
        Saturation[lot.Species] = MathF.Min(s, 1f - MinDemand);
        return paid;
    }

    /// <summary>Sells several fish one after another (later fish get the lower, saturated price).</summary>
    public int SellAll(IEnumerable<FishLot> lots)
    {
        int total = 0;
        foreach (var lot in lots)
            total += Sell(lot);
        return total;
    }
}
