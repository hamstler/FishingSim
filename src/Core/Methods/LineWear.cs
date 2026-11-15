using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// A line wears with use (pure): fights that load it past half its strength, a snag jerked or pulled on, and every
/// fish landed fray it a little. A worn line breaks sooner (up to 60 % weaker); new line on the spool (the shop)
/// makes it whole again.
/// </summary>
public static class LineWear
{
    public const float Max = 0.6f;
    public const float PerFish = 0.004f;
    public const float PerSnagJerk = 0.03f;
    public const float PerSnagPullSecond = 0.012f;

    /// <summary>Wear from <paramref name="dt"/> seconds at this tension (only past half the line's strength).</summary>
    public static float FromLoad(float tension, float strength, float dt)
    {
        float share = strength > 0f ? tension / strength : 0f;
        return share > 0.5f ? (share - 0.5f) * 0.02f * MathF.Max(dt, 0f) : 0f;
    }

    /// <summary>What a line of this strength holds with this much wear.</summary>
    public static float Strength(float strength, float wear) => strength * (1f - Math.Clamp(wear, 0f, Max));

    /// <summary>Worth putting new line on the spool.</summary>
    public static bool Worn(float wear) => wear >= 0.05f;

    public static string Word(float wear) =>
        wear < 0.05f ? "" : wear < 0.2f ? "lekko przetarta" : wear < 0.4f ? "przetarta" : "mocno przetarta";
}
