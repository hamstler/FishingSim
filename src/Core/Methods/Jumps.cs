using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// A fish leaping in the fight (pure): in the air it shakes its head against the line, and a rod held high on a tight
/// line gives it the purchase to tear the hook out. The angler "bows" to a jumping fish: drops the rod so the line
/// goes soft for that moment. A deeply set hook holds either way more often.
/// </summary>
public static class Jumps
{
    /// <summary>The chance that one leap throws the hook.</summary>
    /// <param name="lifting">The rod is held up (RMB) as the fish leaps.</param>
    /// <param name="hold">How well the hook is set (FightModel.HookHold, 0.2 .. 1).</param>
    /// <param name="load">The line's tension as a share of the fish's peak pull (0 .. 1.5).</param>
    public static float ThrowChance(bool lifting, float hold, float load) =>
        (lifting ? 0.45f : 0.07f) * Math.Clamp(1.25f - hold, 0.1f, 1f) * (0.5f + 0.5f * Math.Clamp(load, 0f, 1.5f));
}
