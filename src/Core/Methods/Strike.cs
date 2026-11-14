using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// How much of the strike reaches the hook (pure): a stretchy monofilament soaks up the sweep of the rod over a long
/// distance, and any slack has to be taken up before the hook moves at all. A braid barely stretches, so it sets the
/// hook at 60 m almost as well as at 10 m.
/// </summary>
public static class Strike
{
    /// <summary>The share of the strike left when the drag is set this loose (kg): under about a kilogram the spool
    /// gives line on the sweep and the hook barely moves (0.55 at no drag, 1 from 0.9 kg).</summary>
    public static float DragFactor(float dragKg) => Math.Clamp(0.55f + 0.5f * dragKg, 0.55f, 1f);

    /// <summary>The share of the strike that sets the hook (0.45 .. 1).</summary>
    /// <param name="distance">Line out to the end tackle (m).</param>
    /// <param name="stretch">The line's stretch (LineData.Stretch: braid ~0.05, mono ~0.55).</param>
    /// <param name="slack">Loose line lying on the water (m).</param>
    public static float Efficiency(float distance, float stretch, float slack)
    {
        float far = Math.Clamp((distance - 15f) / 50f, 0f, 1f);
        float give = 1f - 0.55f * Math.Clamp(stretch, 0f, 1f) * far;
        float loose = 1f - 0.35f * Math.Clamp(slack / 2f, 0f, 1f);
        return Math.Clamp(give * loose, 0.45f, 1f);
    }
}
