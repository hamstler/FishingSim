using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// Rain muddies the water (pure): run-off and the drops stir up silt, so after a few hours of rain the water is
/// murky, and it clears again over the next day (half-life 8 game hours). Murky water hides the line and the
/// lure's colours and lets the angler see less into it.
/// </summary>
public static class Turbidity
{
    /// <summary>The murk added per hour of rain.</summary>
    public const float PerRainHour = 0.18f;

    /// <summary>Hours for the murk to halve once the rain stops.</summary>
    public const float HalfLife = 8f;

    /// <summary>The murk (0 clear .. 1 thick with silt) after this many hours in this weather.</summary>
    public static float Step(float murk, float hours, Weather weather)
    {
        if (hours <= 0f)
            return murk;
        if (weather == Weather.Rain)
            return MathF.Min(1f, murk + PerRainHour * hours);
        return murk * MathF.Pow(0.5f, hours / HalfLife);
    }

    /// <summary>The clarity multiplier for this murk (1 .. 0.45).</summary>
    public static float ClarityFactor(float murk) => 1f - 0.55f * Math.Clamp(murk, 0f, 1f);

    /// <summary>The dial's word ("" while clear).</summary>
    public static string Word(float murk) => murk >= 0.5f ? "mętna" : murk >= 0.2f ? "lekko mętna" : "";
}
