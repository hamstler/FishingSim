using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// A cast is not a rifle shot (pure): the harder the swing, the wider the end tackle strays from the aim and the more
/// its distance varies; in the air the wind carries a light float or fly sideways, a heavy feeder or spoon hardly.
/// </summary>
public static class Casting
{
    /// <summary>The stray of a cast: degrees off the aim and a factor on its speed (power 0..1).</summary>
    public static (float Degrees, float Speed) Scatter(float power, IRng rng)
    {
        float spread = 1.5f + 4.5f * Math.Clamp(power, 0f, 1f);
        // Two uniform draws make a soft bell: most casts near the aim, a few wide.
        float side = (rng.NextFloat() + rng.NextFloat() - 1f) * spread;
        float speed = 1f + (rng.NextFloat() + rng.NextFloat() - 1f) * (0.03f + 0.05f * power);
        return (side, speed);
    }

    /// <summary>How strongly the wind carries the end tackle in flight (1/s): a float 0.9, a fly 1.1, a lure less the
    /// heavier it is, a feeder 0.15.</summary>
    public static float WindCatch(FishingMethod method, float weightG) => method switch
    {
        FishingMethod.Bottom => 0.15f,
        FishingMethod.Spinning => Math.Clamp(1.2f / (1f + MathF.Max(weightG, 0f) / 4f), 0.12f, 1.1f),
        _ => 0.9f,
    };
}
