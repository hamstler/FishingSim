using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// Fish rising (pure): surface feeders sip insects off the film and leave spreading rings, most at dawn and in the
/// evening on calm water, hardly at midday or in a wind, less in the rain (the drops hide the food). The rings show
/// the angler where the fish are feeding near the top.
/// </summary>
public static class Rises
{
    /// <summary>Rises a minute within sight on a lake, at this hour, weather and wind (0 calm .. 1 gale).</summary>
    public static float PerMinute(float hour, Weather weather, float wind)
    {
        float h = (hour % 24f + 24f) % 24f;
        // Two evening and morning peaks, a little at night, little at midday.
        float dawn = MathF.Exp(-MathF.Pow((h - 6.5f) / 1.3f, 2f));
        float dusk = MathF.Exp(-MathF.Pow((h - 19.5f) / 1.5f, 2f));
        float rate = 0.6f + 5f * dawn + 6f * dusk + (h < 4f || h > 22f ? 0.6f : 0f);
        rate *= weather switch { Weather.Rain => 0.4f, Weather.Cloudy => 1.15f, _ => 1f };
        return rate * (1f - 0.7f * Math.Clamp(wind, 0f, 1f));
    }

    /// <summary>A fish of a species feeding at this level (FeedingHabits.LayerOf: 0 surface .. 1 bottom) rises at all.</summary>
    public static bool Riser(float layer) => layer <= 0.6f;

    /// <summary>How long (real seconds) a rise still tells where the fish is.</summary>
    public const float FreshSeconds = 60f;

    /// <summary>How far (m) from a rise a bait counts as cast to it.</summary>
    public const float CastTo = 6f;

    /// <summary>The bite weight multiplier for a species whose fish rose this far away this many seconds ago.</summary>
    public static float Boost(float distance, float secondsAgo)
    {
        if (distance >= CastTo || secondsAgo >= FreshSeconds || secondsAgo < 0f)
            return 1f;
        return 1f + 1.2f * (1f - distance / CastTo) * (1f - secondsAgo / FreshSeconds);
    }
}
