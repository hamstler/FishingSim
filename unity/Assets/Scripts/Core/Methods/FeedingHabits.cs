using System;
using System.Collections.Generic;

namespace FishingSim.Core.Methods;

/// <summary>How a species takes a bait under a float.</summary>
public enum BiteStyle
{
    /// <summary>The float bobs, then goes under (most fish).</summary>
    Dive,
    /// <summary>Quick little dips of small fish, a short pull under.</summary>
    Pluck,
    /// <summary>The fish picks the bait up off the bottom: the float rises and lies flat (bream, tench, crucians).</summary>
    Lift,
    /// <summary>The float slides away across the water before it goes under (carp, predators with a live bait).</summary>
    Run,
}

/// <summary>
/// Where in the water column each species feeds (1 = on the bottom, 0 = at the surface) and how it takes a bait under
/// a float. A bait hanging at the wrong level is found by few fish: the float's depth setting ("grunt"), a lure's
/// running depth and a bait lying on the bottom are judged by <see cref="LayerFit"/>.
/// </summary>
public static class FeedingHabits
{
    private static readonly Dictionary<string, float> Layers = new()
    {
        // The surface and just under it.
        ["Ukleja"] = 0.1f, ["Słonecznica"] = 0.15f, ["Wzdręga"] = 0.2f, ["Boleń"] = 0.2f, ["Ciosa"] = 0.2f, ["Koza"] = 0.15f,
        ["Tołpyga biała"] = 0.25f, ["Tołpyga pstra"] = 0.35f, ["Orfa"] = 0.3f, ["Jelec"] = 0.35f, ["Kleń"] = 0.35f,
        ["Pstrąg tęczowy"] = 0.4f, ["Pstrąg potokowy"] = 0.5f, ["Pstrąg źródlany"] = 0.5f, ["Lipień"] = 0.55f, ["Amur biały"] = 0.4f,
        // Mid water.
        ["Płoć"] = 0.6f, ["Jaź"] = 0.5f, ["Okoń"] = 0.65f, ["Szczupak"] = 0.55f, ["Bass wielkogębowy"] = 0.5f, ["Sielawa"] = 0.5f,
        ["Sieja"] = 0.6f, ["Stynka"] = 0.6f, ["Rozpiór"] = 0.7f, ["Krąp"] = 0.8f, ["Łosoś"] = 0.6f, ["Troć wędrowna"] = 0.6f,
        ["Troć jeziorowa"] = 0.65f, ["Palia"] = 0.7f, ["Głowacica"] = 0.7f, ["Różanka"] = 0.6f, ["Ciernik"] = 0.5f,
        ["Cierniczek"] = 0.5f, ["Strzebla potokowa"] = 0.5f, ["Strzebla błotna"] = 0.6f, ["Certa"] = 0.8f, ["Świnka"] = 0.85f,
        ["Sandacz"] = 0.85f, ["Sandacz wołżański"] = 0.85f,
    };

    private static readonly Dictionary<string, BiteStyle> Styles = new()
    {
        ["Leszcz"] = BiteStyle.Lift, ["Lin"] = BiteStyle.Lift, ["Karaś złocisty"] = BiteStyle.Lift, ["Karaś srebrzysty"] = BiteStyle.Lift,
        ["Krąp"] = BiteStyle.Lift, ["Sapa"] = BiteStyle.Lift, ["Rozpiór"] = BiteStyle.Lift,
        ["Karp"] = BiteStyle.Run, ["Karp lustrzeń"] = BiteStyle.Run, ["Sazan"] = BiteStyle.Run, ["Amur biały"] = BiteStyle.Run,
        ["Szczupak"] = BiteStyle.Run, ["Sandacz"] = BiteStyle.Run, ["Sandacz wołżański"] = BiteStyle.Run, ["Sum"] = BiteStyle.Run,
        ["Węgorz"] = BiteStyle.Run, ["Miętus"] = BiteStyle.Run, ["Boleń"] = BiteStyle.Run, ["Brzana"] = BiteStyle.Run,
        ["Ukleja"] = BiteStyle.Pluck, ["Słonecznica"] = BiteStyle.Pluck, ["Kiełb"] = BiteStyle.Pluck, ["Kiełb białopłetwy"] = BiteStyle.Pluck,
        ["Jazgarz"] = BiteStyle.Pluck, ["Różanka"] = BiteStyle.Pluck, ["Ciernik"] = BiteStyle.Pluck, ["Cierniczek"] = BiteStyle.Pluck,
        ["Strzebla potokowa"] = BiteStyle.Pluck, ["Strzebla błotna"] = BiteStyle.Pluck, ["Śliz"] = BiteStyle.Pluck, ["Piskorz"] = BiteStyle.Pluck,
        ["Babka bycza"] = BiteStyle.Pluck, ["Koza"] = BiteStyle.Pluck, ["Stynka"] = BiteStyle.Pluck, ["Płoć"] = BiteStyle.Pluck,
    };

    /// <summary>The level a species feeds at: 1 = on the bottom (the default: most fish), 0 = at the surface.</summary>
    public static float LayerOf(string species) => Layers.GetValueOrDefault(species, 1f);

    public static BiteStyle StyleOf(string species) => Styles.GetValueOrDefault(species, BiteStyle.Dive);

    /// <summary>
    /// The bait's level for a depth setting: its depth as a share of the water's (1 = on the bottom, also when the
    /// setting is deeper than the water: the bait lies on the bottom and the float lies flat).
    /// </summary>
    public static float BaitLayer(float baitDepth, float waterDepth) =>
        waterDepth <= 0.05f ? 1f : Math.Clamp(baitDepth / waterDepth, 0f, 1f);

    /// <summary>
    /// How well a bait at this level is found by the species: 1 at its own level, falling with the distance in the
    /// water column (in metres too: half a metre off hardly matters in shallow water), never below 0.12.
    /// </summary>
    public static float LayerFit(string species, float baitLayer, float waterDepth)
    {
        float offMetres = MathF.Abs(baitLayer - LayerOf(species)) * MathF.Max(waterDepth, 0.3f);
        return Math.Clamp(1.1f - offMetres * 0.75f, 0.12f, 1f);
    }

    /// <summary>The share of strikes that hold with the float set deeper than the water (the line lies slack on the bottom).</summary>
    public const float OverDepthHookFactor = 0.75f;
}
