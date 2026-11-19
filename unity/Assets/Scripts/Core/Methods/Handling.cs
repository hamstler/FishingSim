using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// How the catch is handled (pure): a long fight tires a fish, and every moment out of the water after the first
/// twenty seconds costs it more. A tired fish starts its time in the keepnet in worse condition (lower price) and a
/// released one recovers less surely.
/// </summary>
public static class Handling
{
    /// <summary>Seconds out of the water a fish takes without harm.</summary>
    public const float AirGrace = 20f;

    /// <summary>Condition lost in a fight of this length (none in the first 20 s, at most 0.25).</summary>
    public static float FightToll(float fightSeconds) => Math.Clamp((fightSeconds - 20f) * 0.0025f, 0f, 0.25f);

    /// <summary>Condition lost out of the water (none in the grace, then 0.004 a second, at most 0.35).</summary>
    public static float AirToll(float airSeconds) => Math.Clamp((airSeconds - AirGrace) * 0.004f, 0f, 0.35f);

    /// <summary>The fish's condition as it goes into the keepnet (0.4 .. 1).</summary>
    public static float Condition(float fightSeconds, float airSeconds) =>
        Math.Clamp(1f - FightToll(fightSeconds) - AirToll(airSeconds), 0.4f, 1f);
}
