using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// How much the line's visibility matters (pure): in clear water and bright light fish see a thick or shiny line and
/// a thin fluorocarbon leader pays off; in murky water, at dusk or at night they can hardly tell one line from another.
/// The tackle's own visibility (0.65 nearly invisible .. 1 plain to see) is pulled towards an ordinary line (0.85) by
/// how well the fish can see now.
/// </summary>
public static class LineShyness
{
    /// <summary>An ordinary line's visibility.</summary>
    public const float Ordinary = 0.85f;

    /// <summary>How well the fish see the line: water clarity (metres) and light (0 night .. 1 midday).</summary>
    public static float Seeing(float clarity, float light) =>
        Math.Clamp(clarity / 4f, 0.3f, 1.5f) * (0.35f + 0.65f * Math.Clamp(light, 0f, 1f));

    /// <summary>The bite wait's multiplier for a line of this visibility now.</summary>
    public static float Effective(float visibility, float clarity, float light) =>
        MathF.Max(0.5f, Ordinary + (visibility - Ordinary) * Seeing(clarity, light));
}
