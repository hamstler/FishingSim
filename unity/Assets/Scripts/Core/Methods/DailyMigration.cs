using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// The daily move between deep and shallow water (pure): in low light (dawn, dusk, night, heavy cloud) fish come up
/// into the shallows to feed, where the food is; under a bright midday sun they drop off to deeper, darker water.
/// The bank angler feels it as better shallow swims early and late and the drop-offs paying at noon.
/// </summary>
public static class DailyMigration
{
    /// <summary>The bite weight multiplier for bait over water this deep (m) in this light (0 night .. 1 bright midday).</summary>
    public static float Fit(float depth, float light)
    {
        light = Math.Clamp(light, 0f, 1f);
        float shallow = Math.Clamp(1f - depth / 4f, 0f, 1f);
        float deep = Math.Clamp((depth - 3f) / 4f, 0f, 1f);
        return (1f + shallow * (0.35f - 0.65f * light)) * (1f + deep * (0.3f * light - 0.1f));
    }
}
