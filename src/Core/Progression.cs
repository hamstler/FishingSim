using System;
using FishingSim.Data;

namespace FishingSim.Core;

/// <summary>Levels, XP and fish value. Pure functions, no engine state.</summary>
public static class Progression
{
    /// <summary>Total XP needed to reach <paramref name="level"/> (level 1 needs 0).</summary>
    public static int XpForLevel(int level) => 60 * (level - 1) * (level - 1);

    public static int FishValue(float pricePerKg, float weight) => (int)MathF.Round(weight * pricePerKg, MidpointRounding.AwayFromZero);

    public static int FishXp(float rarity, float weight) =>
        (int)MathF.Round(8f + 10f * MathF.Pow(weight, 0.8f) * (1f + 0.3f / MathF.Max(rarity, 0.3f)), MidpointRounding.AwayFromZero);

    public static int FishValue(FishSpecies species, float weight) => FishValue(species.PricePerKg, weight);

    public static int FishXp(FishSpecies species, float weight) => FishXp(species.Rarity, weight);
}
