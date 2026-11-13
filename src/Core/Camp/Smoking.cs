using System;

namespace FishingSim.Core.Camp;

/// <summary>
/// The village smokehouse (wędzarnia): fish from the keepnet are hung over alder smoke for a few game hours. A smoked
/// fish no longer loses condition and the smokehouse keeper buys it for much more than the fixed buyer pays for a raw
/// one (a fresh fish keeps its full premium; a tired one less). The keeper takes a small fee per kilo for the wood.
/// Undersized fish are refused, and so are very small ones.
/// </summary>
public static class Smoking
{
    /// <summary>How long a batch hangs in the smoke (game hours).</summary>
    public const float Hours = 2f;

    /// <summary>Price of a smoked fish over the buyer's raw price, for a fresh fish.</summary>
    public const float ValueFactor = 1.8f;

    /// <summary>The smallest fish worth smoking (kg).</summary>
    public const float MinWeight = 0.1f;

    public const int FeePerKg = 6, MinFee = 4;

    /// <summary>The fee for the wood for a batch of this weight (gr).</summary>
    public static int Fee(float kg) => kg <= 0f ? 0 : Math.Max(MinFee, (int)MathF.Ceiling(kg * FeePerKg));

    /// <summary>Why this fish cannot be smoked, or null when it can.</summary>
    public static string? Refusal(float weight, bool undersized, bool smoked) =>
        smoked ? "Już uwędzona."
        : undersized ? "Niewymiarowej ryby wędzarz nie przyjmie."
        : weight < MinWeight ? $"Za mała: wędzi się ryby od {MinWeight * 1000f:0} g."
        : null;

    /// <summary>What the smokehouse keeper pays for the smoked fish (gr): the raw price × <see cref="ValueFactor"/>,
    /// less for a fish that was tired when it went into the smoke.</summary>
    public static int SmokedValue(int rawValue, float condition) =>
        (int)MathF.Round(rawValue * ValueFactor * (0.55f + 0.45f * Math.Clamp(condition, 0f, 1f)), MidpointRounding.AwayFromZero);

    /// <summary>Game hours left until a batch put in at <paramref name="startedAt"/> is ready (0 when it is).</summary>
    public static float HoursLeft(float startedAt, float now) => MathF.Max(0f, startedAt + Hours - now);
}
