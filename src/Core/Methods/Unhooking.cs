using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// Where the hook ends up (pure): a fish given time with a natural bait swallows it, so a late strike on the float or
/// the bottom rod often finds the hook deep in the throat. A deep hook holds in the fight, but getting it out takes
/// long out of the water and hurts the fish, much less with a disgorger ("wypychacz"). Lures are struck at the take
/// and seldom go deep.
/// </summary>
public static class Unhooking
{
    /// <summary>The chance the hook is swallowed: lateness 0 (struck at once) .. 1 (at the end of the window).</summary>
    public static float DeepChance(FishingMethod method, float lateness) => method == FishingMethod.Spinning
        ? 0.04f
        : 0.08f + 0.6f * Math.Clamp(lateness, 0f, 1f);

    /// <summary>The hook's hold gained when it is deep.</summary>
    public const float DeepHold = 0.15f;

    /// <summary>Seconds out of the water the unhooking takes.</summary>
    public static float Seconds(bool deep, bool disgorger) => !deep ? 4f : disgorger ? 9f : 28f;

    /// <summary>Condition lost to the unhooking itself (a deep hook torn out without a disgorger harms the gullet).</summary>
    public static float Harm(bool deep, bool disgorger) => deep ? disgorger ? 0.02f : 0.12f : 0f;
}
