using System;

namespace FishingSim.Core.Gear;

/// <summary>
/// Lifting a fish out of the water with the rod (right mouse button) until it reaches the angler's hand.
/// <c>lift</c> runs from 0 (fish at the surface) to 1 (fish touches the angler = caught). A light, tired fish comes up
/// quickly; a heavy or fresh one loads the line far more and is slow to raise, so big fish need a landing net.
/// Pure functions, kg-force like <see cref="FightModel"/>.
/// </summary>
public static class LiftModel
{
    /// <summary>How fast the fish drops back when the rod is lowered (lift per second).</summary>
    public const float DropRate = 1.4f;

    /// <summary>Line load while the fish hangs at <paramref name="lift"/>: its weight leaving the water plus its thrashing.</summary>
    public static float Tension(float weight, float stamina, float lift)
    {
        float outOfWater = 0.45f + 0.75f * Math.Clamp(lift, 0f, 1f);
        float thrash = 1f + 1.3f * Math.Clamp(stamina, 0f, 1f);
        return weight * outOfWater * thrash;
    }

    /// <summary>Lift gained per second while the rod is held up.</summary>
    public static float Rate(float weight, float stamina) =>
        0.9f / (0.55f + MathF.Max(weight, 0.05f) * 0.35f) * (1f - 0.45f * Math.Clamp(stamina, 0f, 1f));

    /// <summary>One step of lifting (or letting the fish sink back). Returns the new lift value.</summary>
    public static float Step(float lift, bool lifting, float weight, float stamina, float dt) =>
        Math.Clamp(lifting ? lift + Rate(weight, stamina) * dt : lift - DropRate * dt, 0f, 1f);
}
