using System;

namespace FishingSim.Core.Gear;

/// <summary>Stats of a landing net (podbierak).</summary>
public readonly record struct LandingNetSpec(float MaxWeight, float Reach);

public enum LandingOutcome { Landed, Escaped }

/// <summary>
/// The last metre of the fight: lifting the fish out. Small fish can be swung or lifted by hand; bigger ones need a
/// landing net big enough for them, otherwise they often slip off the hook. A fish that still has strength left
/// thrashes more. Pure functions.
/// </summary>
public static class LandingModel
{
    /// <summary>Fish up to this weight are safely landed by hand.</summary>
    public const float HandLimitKg = 1.2f;

    public static float EscapeChance(float weight, LandingNetSpec? net, float stamina)
    {
        float baseChance;
        if (net is LandingNetSpec n && weight <= n.MaxWeight)
            baseChance = 0.01f;
        else if (net is LandingNetSpec tooSmall)
            baseChance = 0.2f + 0.6f * (weight / MathF.Max(tooSmall.MaxWeight, 0.1f) - 1f);
        else if (weight <= HandLimitKg)
            baseChance = 0f;
        else
            baseChance = 0.25f + 0.15f * (weight - HandLimitKg);
        float thrash = 0.6f + 0.8f * Math.Clamp(stamina, 0f, 1f);
        return Math.Clamp(baseChance * thrash, 0f, 0.9f);
    }

    public static LandingOutcome Resolve(IRng rng, float weight, LandingNetSpec? net, float stamina) =>
        rng.NextFloat() < EscapeChance(weight, net, stamina) ? LandingOutcome.Escaped : LandingOutcome.Landed;
}
