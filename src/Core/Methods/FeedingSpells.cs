using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// Fish feed in spells (pure): over a few game hours the bites come in waves, a busy half hour, then a lull. The waves
/// are the sum of three slow swings whose phases change with the day and the lake, so no two days are alike.
/// </summary>
public static class FeedingSpells
{
    /// <summary>The bite clock's speed at this moment (about 0.5 in a lull .. 1.5 in a feeding spell).</summary>
    public static float Pace(float totalHours, int lake)
    {
        int day = (int)MathF.Floor(totalHours / 24f);
        float p1 = Phase(day, lake, 1), p2 = Phase(day, lake, 2), p3 = Phase(day, lake, 3);
        float wave = 0.5f * MathF.Sin(totalHours * MathF.Tau / 1.7f + p1)
            + 0.3f * MathF.Sin(totalHours * MathF.Tau / 3.1f + p2)
            + 0.2f * MathF.Sin(totalHours * MathF.Tau / 0.9f + p3);
        return 1f + 0.5f * wave;
    }

    private static float Phase(int day, int lake, int k)
    {
        uint h = (uint)(day * 73856093) ^ (uint)(lake * 19349663) ^ (uint)(k * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h % 6283 / 1000f;
    }
}
