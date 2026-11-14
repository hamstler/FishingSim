using System;
using System.Collections.Generic;

namespace FishingSim.Core.Methods;

/// <summary>
/// The moon (pure): its phase runs a 29.5-day cycle of game days, and on a clear night its light decides how dark the
/// water is. The night hunters (catfish, eel, zander, burbot) hunt best in the dark of a new moon and are shy under a
/// full one; the other fish feed a little more on bright nights. By day the moon changes nothing.
/// </summary>
public static class Moon
{
    /// <summary>Days from new moon to new moon.</summary>
    public const float Cycle = 29.53f;

    /// <summary>The first game day starts a few days before the full moon.</summary>
    private const float StartPhase = 0.35f;

    private static readonly HashSet<string> NightHunters = new()
    {
        "Sum", "Sumik kanałowy", "Sumik karłowaty", "Węgorz", "Miętus", "Sandacz", "Sandacz wołżański",
    };

    /// <summary>The phase (0 new moon, 0.5 full moon) after this many game hours.</summary>
    public static float Phase(float totalHours)
    {
        float p = StartPhase + totalHours / 24f / Cycle;
        return p - MathF.Floor(p);
    }

    /// <summary>The lit share of the moon's disc (0 new .. 1 full).</summary>
    public static float Illumination(float phase) => 0.5f * (1f - MathF.Cos(phase * MathF.Tau));

    /// <summary>How much moonlight reaches the water (0 .. 1): the disc's light through the clouds, only at night.</summary>
    public static float Light(float phase, float night, Weather weather) =>
        Illumination(phase) * Math.Clamp(night, 0f, 1f) * weather switch { Weather.Rain => 0.15f, Weather.Cloudy => 0.45f, _ => 1f };

    /// <summary>How a species bites under this much moonlight (night hunters 1.3 in the dark .. 0.7 at a clear full moon).</summary>
    public static float Fit(string species, float light)
    {
        light = Math.Clamp(light, 0f, 1f);
        return NightHunters.Contains(species) ? 1.3f - 0.6f * light : 0.92f + 0.16f * light;
    }

    /// <summary>The phase's Polish name for the dial.</summary>
    public static string Name(float phase)
    {
        int eighth = (int)MathF.Floor((phase * 8f + 0.5f) % 8f);
        return eighth switch
        {
            0 => "nów",
            1 => "młody księżyc",
            2 => "pierwsza kwadra",
            3 => "przybywa",
            4 => "pełnia",
            5 => "ubywa",
            6 => "ostatnia kwadra",
            _ => "stary księżyc",
        };
    }
}
