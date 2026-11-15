using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// A natural bait on the hook wears in the water (pure): a worm or a maggot weakens and stops wriggling, bread and
/// dough soften and fall off, corn and boilies last long. A fish that took the bait but was missed usually leaves
/// the hook bare. Lures do not wear.
/// </summary>
public static class BaitWear
{
    /// <summary>Real minutes in the water until a bait is spent.</summary>
    public static float LifeMinutes(string id) => id switch
    {
        "bread" => 3f,
        "ciasto" => 4f,
        "kasza" or "konopie" => 6f,
        "maggot" or "ochotka" => 6f,
        "jetka" or "insect" or "chrabaszcz" or "pedrak" => 5f,
        "worm" or "rosowka" => 9f,
        "livebait" => 12f,
        "martwa_rybka" => 15f,
        "corn" or "groch" => 20f,
        "boilie" or "kulka_czosnkowa" or "kulka_plywajaca" => 30f,
        _ => 10f,
    };

    /// <summary>Soft baits fall off the hook once spent; the others stay on, just no longer attractive.</summary>
    public static bool Dissolves(string id) => id is "bread" or "ciasto" or "kasza";

    /// <summary>The bait's freshness after <paramref name="seconds"/> more in the water (1 fresh .. 0 spent).</summary>
    public static float Step(float freshness, string id, float seconds) =>
        MathF.Max(0f, freshness - MathF.Max(seconds, 0f) / (LifeMinutes(id) * 60f));

    /// <summary>How a bait this fresh draws the fish (multiplies the bite clock): 1 fresh, 0.35 spent.</summary>
    public static float Appeal(float freshness) => 0.35f + 0.65f * Math.Clamp(freshness, 0f, 1f);

    /// <summary>The chance a missed fish leaves the bait on the hook.</summary>
    public const float KeptAfterMiss = 0.35f;

    public static string Word(bool onHook, float freshness) =>
        !onHook ? "pusty haczyk: zwiń i załóż przynętę" : freshness < 0.35f ? "przynęta zużyta" : freshness < 0.7f ? "przynęta słabnie" : "";
}
