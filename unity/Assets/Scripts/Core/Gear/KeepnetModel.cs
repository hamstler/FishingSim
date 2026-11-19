using System;

namespace FishingSim.Core.Gear;

/// <summary>Stats of a keepnet (siatka): how many fish / kilos it holds and how fast fish in it lose condition.</summary>
public readonly record struct KeepnetSpec(int MaxFish, float MaxKg, float ConditionLossPerHour);

/// <summary>Keepnet rules. Pure functions.</summary>
public static class KeepnetModel
{
    /// <summary>True when one more fish of <paramref name="weight"/> kg still fits.</summary>
    public static bool Fits(KeepnetSpec spec, int count, float kg, float weight) =>
        count + 1 <= spec.MaxFish && kg + weight <= spec.MaxKg + 1e-4f;

    /// <summary>
    /// Condition after <paramref name="hours"/> in the net. A crowded net (by weight) and warm water make fish
    /// tire faster. Condition 1 = fresh, 0 = dead.
    /// </summary>
    public static float Decay(float condition, KeepnetSpec spec, float hours, float fillRatio, float warmth = 1f)
    {
        if (hours <= 0f)
            return condition;
        float crowding = 1f + 1.5f * Math.Clamp(fillRatio, 0f, 1f);
        return Math.Clamp(condition - spec.ConditionLossPerHour * crowding * MathF.Max(warmth, 0f) * hours, 0f, 1f);
    }
}
