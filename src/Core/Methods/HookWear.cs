using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// A hook's point wears (pure): every fish's bony mouth and every snag on stones and wood blunts it a little, and a blunt
/// hook slides over the lip on the strike instead of going in. A hone puts the point back (H, with the "Osełka" owned);
/// a new hook of the same kind from the shop does too.
/// </summary>
public static class HookWear
{
    /// <summary>The bluntest a hook gets.</summary>
    public const float Max = 0.6f;

    /// <summary>The blunting from snags (each time the hook catches on the bottom).</summary>
    public const float PerSnag = 0.1f;

    /// <summary>The blunting from landing a fish of this weight (bigger, bonier mouths blunt more).</summary>
    public static float PerFish(float weightKg) => 0.025f + 0.01f * Math.Clamp(weightKg, 0f, 5f);

    /// <summary>The strike's chance and the hook's hold multiplier for a hook this blunt (1 sharp .. 0.64 at the worst).</summary>
    public static float SetFactor(float dullness) => 1f - 0.6f * Math.Clamp(dullness, 0f, Max);

    /// <summary>Blunt enough to be worth sharpening or replacing.</summary>
    public static bool Blunt(float dullness) => dullness >= 0.15f;

    /// <summary>The HUD's word ("" while sharp).</summary>
    public static string Word(float dullness) => dullness >= 0.35f ? "tępy" : dullness >= 0.15f ? "stępiony" : "";
}
