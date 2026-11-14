using System;
using System.Collections.Generic;

namespace FishingSim.Core.Methods;

/// <summary>
/// The air pressure (pure): it follows the weather slowly (high in clear weather, low in rain) and what matters to
/// the fish is its trend. A slow fall before a front sets them feeding; a rise after the front puts them off; steady
/// pressure is neutral, a steady low a little worse.
/// </summary>
public sealed class Barometer
{
    /// <summary>hPa.</summary>
    public float Pressure { get; private set; } = 1016f;

    /// <summary>hPa per game hour (smoothed).</summary>
    public float Trend { get; private set; }

    public static float TargetFor(Weather weather) => weather switch
    {
        Weather.Clear => 1022f,
        Weather.Cloudy => 1012f,
        _ => 1001f,
    };

    /// <summary>Game time passes under this weather.</summary>
    public void Step(float hours, Weather weather)
    {
        if (hours <= 0f)
            return;
        float before = Pressure;
        Pressure += (TargetFor(weather) - Pressure) * (1f - MathF.Exp(-hours * 0.3f));
        float rate = (Pressure - before) / hours;
        Trend += (rate - Trend) * MathF.Min(1f, hours * 2f);
    }

    /// <summary>How the fish feed under this pressure (multiplies the bite clock, 0.7-1.2).</summary>
    public float Pace => PaceFor(Pressure, Trend);

    public static float PaceFor(float pressure, float trend)
    {
        float p = trend < -1.6f ? 1.05f          // a storm coming fast
            : trend < -0.3f ? 1.2f               // slowly falling: the best fishing
            : trend <= 0.3f ? 1f                 // steady
            : trend <= 1.6f ? 0.82f              // rising after the front
            : 0.7f;
        if (MathF.Abs(trend) <= 0.3f && pressure < 1006f)
            p *= 0.92f;                          // a long low
        return p;
    }

    /// <summary>The trend in a word for the dial.</summary>
    public string TrendWord => Trend < -0.3f ? "spada" : Trend > 0.3f ? "rośnie" : "stałe";
}

/// <summary>
/// The water's temperature and what each species makes of it (pure): a lake has its own temperature (a mountain lake
/// is cold, a pond warm), the day warms it a little towards the afternoon, clouds and rain cool it. Cold-water fish
/// (trout, grayling, whitefish, burbot) feed best around 11 degrees, warm-water fish (carp, tench, crucians, wels)
/// around 22, the rest around 17.
/// </summary>
public static class WaterTemperature
{
    private static readonly HashSet<string> Cold = new()
    {
        "Pstrąg potokowy", "Pstrąg tęczowy", "Pstrąg źródlany", "Łosoś", "Troć wędrowna", "Troć jeziorowa", "Palia", "Sieja", "Sielawa",
        "Lipień", "Miętus", "Głowacica", "Głowacz białopłetwy", "Głowacz pręgopłetwy", "Strzebla potokowa", "Stynka", "Śliz",
    };

    private static readonly HashSet<string> Warm = new()
    {
        "Karp", "Karp lustrzeń", "Sazan", "Lin", "Karaś złocisty", "Karaś srebrzysty", "Amur biały", "Tołpyga biała", "Tołpyga pstra",
        "Sum", "Sumik kanałowy", "Sumik karłowaty", "Bass wielkogębowy", "Słonecznica", "Węgorz", "Wzdręga", "Różanka", "Piskorz",
    };

    /// <summary>Degrees Celsius now: the lake's own temperature, warmest about four in the afternoon (+-1.5), cooler under clouds and rain.</summary>
    public static float At(float lakeTemperature, float hour, Weather weather) =>
        lakeTemperature + 1.5f * MathF.Cos((hour - 16f) / 24f * MathF.Tau)
        - weather switch { Weather.Rain => 0.8f, Weather.Cloudy => 0.3f, _ => 0f };

    /// <summary>The temperature a species feeds best at, and how many degrees off it still feeds well.</summary>
    public static (float Optimum, float Tolerance) Preference(string species) =>
        Cold.Contains(species) ? (11f, 5f) : Warm.Contains(species) ? (22f, 6f) : (17f, 7f);

    /// <summary>How well a species feeds at this temperature (1.1 at its optimum, 0.25 far from it).</summary>
    public static float Fit(string species, float temperature)
    {
        var (opt, tol) = Preference(species);
        float off = (temperature - opt) / tol;
        return Math.Clamp(1.1f - 0.8f * off * off, 0.25f, 1.1f);
    }
}
