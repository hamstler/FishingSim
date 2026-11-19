using System;

namespace FishingSim.Core;

/// <summary>
/// How fast game time runs: the day (06:00-18:00) lasts 40 real minutes and the night (18:00-06:00) 20, so a full
/// cycle takes one hour. Pure maths, used by the world clock and the HUD.
/// </summary>
public static class DayClock
{
    public const float DayStart = 6f, NightStart = 18f;
    public const float DayRealMinutes = 40f, NightRealMinutes = 20f;

    public static bool IsDaytime(float hour) => hour >= DayStart && hour < NightStart;

    /// <summary>Game minutes that pass per real second at this hour (before any speed-up such as resting).</summary>
    public static float GameMinutesPerSecond(float hour)
    {
        float span = (NightStart - DayStart) * 60f;                      // 720 game minutes of day
        float night = (24f - (NightStart - DayStart)) * 60f;             // 720 of night
        return IsDaytime(hour) ? span / (DayRealMinutes * 60f) : night / (NightRealMinutes * 60f);
    }

    /// <summary>Advances <paramref name="hour"/> by <paramref name="realSeconds"/>, crossing dawn and dusk exactly.</summary>
    public static float Advance(float hour, float realSeconds, float speed = 1f)
    {
        float left = realSeconds * speed;
        for (int guard = 0; left > 1e-6f && guard < 8; guard++)
        {
            float rate = GameMinutesPerSecond(hour) / 60f;              // game hours per real second
            float boundary = IsDaytime(hour) ? NightStart : hour >= NightStart ? 24f + DayStart : DayStart;
            float toBoundary = (boundary - hour) / rate;
            if (left < toBoundary)
                return Wrap(hour + left * rate);
            hour = Wrap(boundary);
            left -= toBoundary;
        }
        return hour;
    }

    /// <summary>Real seconds until the given hour comes round.</summary>
    public static float RealSecondsUntil(float from, float to)
    {
        float seconds = 0f, hour = from;
        for (int i = 0; i < 4000 && MathF.Abs(Delta(hour, to)) > 1e-3f; i++)
        {
            float step = MathF.Min(Delta(hour, to), 0.25f);
            seconds += step / (GameMinutesPerSecond(hour) / 60f);
            hour = Wrap(hour + step);
        }
        return seconds;
    }

    private static float Delta(float from, float to) => ((to - from) % 24f + 24f) % 24f;

    private static float Wrap(float h) => (h % 24f + 24f) % 24f;
}
