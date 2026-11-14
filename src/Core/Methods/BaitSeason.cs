using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// Which kind of bait suits the water's temperature (pure): in cold water fish feed little and want a small animal
/// bait (a worm, maggots); plant baits (corn, bread, groats) come into their own in warm water, when the carp family
/// feeds hard; boilies, made for carp, want it warm too. Lures are judged by their own rules.
/// </summary>
public static class BaitSeason
{
    /// <summary>The bite clock's speed for a bait of this kind (Data.BaitKind as an int: 0 natural, 1 plant, 2 carp,
    /// 3 lure) at this temperature (degrees, at the bait).</summary>
    public static float Pace(int kind, float temperature) => kind switch
    {
        0 => Math.Clamp(1.15f - 0.015f * (temperature - 10f), 0.95f, 1.15f),
        1 => Math.Clamp(0.55f + 0.04f * (temperature - 10f), 0.55f, 1.15f),
        2 => Math.Clamp(0.45f + 0.05f * (temperature - 10f), 0.45f, 1.2f),
        _ => 1f,
    };
}
