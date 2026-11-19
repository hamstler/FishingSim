using System;
using System.Collections.Generic;

namespace FishingSim.Core.Methods;

/// <summary>A patch of water plants (lilies or underwater weed) on the lake: its middle and radius (m).</summary>
public readonly record struct WeedBed(float X, float Z, float Radius);

/// <summary>A sunken tree or branch off a wooded bank: its middle and radius (m). It hides fish and snags lures.</summary>
public readonly record struct SunkenWood(float X, float Z, float Radius);

/// <summary>
/// Where the fish hold (pure): pike, tench, perch, crucians, rudd and the like lurk in and by the plants; zander,
/// wels, burbot, ruffe and perch hunt along the drop-offs where the bottom falls away. A bait in such a place draws
/// them better (<see cref="Fit"/>); elsewhere they bite less. A fish fought into a weed bed tangles the line unless
/// the angler lifts it out.
/// </summary>
public static class Structure
{
    private static readonly HashSet<string> CoverLovers = new()
    {
        "Szczupak", "Lin", "Okoń", "Wzdręga", "Karaś złocisty", "Karaś srebrzysty", "Słonecznica", "Bass wielkogębowy",
        "Różanka", "Piskorz", "Karp", "Karp lustrzeń", "Amur biały",
    };

    private static readonly HashSet<string> EdgeHunters = new()
    {
        "Sandacz", "Sandacz wołżański", "Okoń", "Sum", "Miętus", "Jazgarz", "Węgorz", "Leszcz", "Brzana",
    };

    /// <summary>How much plant cover there is at a point: 1 inside a bed, fading to 0 four metres beyond its edge.</summary>
    public static float CoverAt(IReadOnlyList<WeedBed> beds, float x, float z)
    {
        float best = 0f;
        for (int i = 0; i < beds.Count; i++)
        {
            var b = beds[i];
            float d = MathF.Sqrt((b.X - x) * (b.X - x) + (b.Z - z) * (b.Z - z)) - b.Radius;
            float c = d <= 0f ? 1f : MathF.Max(0f, 1f - d / 4f);
            if (c > best)
                best = c;
        }
        return best;
    }

    /// <summary>How close to sunken wood a point is: 1 among the branches, fading to 0 three metres beyond them.</summary>
    public static float WoodAt(IReadOnlyList<SunkenWood> wood, float x, float z)
    {
        float best = 0f;
        for (int i = 0; i < wood.Count; i++)
        {
            var w = wood[i];
            float d = MathF.Sqrt((w.X - x) * (w.X - x) + (w.Z - z) * (w.Z - z)) - w.Radius;
            float c = d <= 0f ? 1f : MathF.Max(0f, 1f - d / 3f);
            if (c > best)
                best = c;
        }
        return best;
    }

    /// <summary>How much more often a lure dragged here snags (1 open bottom .. 7 among the branches).</summary>
    public static float SnagFactor(float wood) => 1f + 6f * Math.Clamp(wood, 0f, 1f);

    /// <summary>How steep the bottom is (0 flat .. 1 a steep drop-off) from the depth's change per metre.</summary>
    public static float EdgeFrom(float depthChangePerMetre) => Math.Clamp(MathF.Abs(depthChangePerMetre) / 0.45f, 0f, 1f);

    /// <summary>How the place suits the species (multiplies the bite weight, 0.7-1.3).</summary>
    public static float Fit(string species, float cover, float edge)
    {
        float f = 1f;
        if (CoverLovers.Contains(species))
            f *= 0.7f + 0.6f * Math.Clamp(cover, 0f, 1f);
        if (EdgeHunters.Contains(species))
            f *= 0.8f + 0.45f * Math.Clamp(edge, 0f, 1f);
        return f;
    }

    public static bool LovesCover(string species) => CoverLovers.Contains(species);

    /// <summary>Seconds a fish may stay in a weed bed while not lifted before it tangles the line.</summary>
    public const float TangleSeconds = 2.5f;
}
