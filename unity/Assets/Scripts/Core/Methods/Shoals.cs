using System;
using System.Collections.Generic;

namespace FishingSim.Core.Methods;

/// <summary>
/// Shoals (pure): roach, bream, bleak, rudd, silver bream, crucian and young perch travel in shoals, so where one bit
/// its companions are close by. For the next hour or so after such a catch the same species bites more readily at
/// that spot. Solitary fish (pike, catfish, eel, carp...) leave no such promise.
/// </summary>
public sealed class Shoals
{
    /// <summary>How far (m) the shoal reaches.</summary>
    public const float Radius = 12f;

    /// <summary>How long (game hours, e-folding) a shoal stays at the spot.</summary>
    public const float StayHours = 0.75f;

    /// <summary>The strongest the shoal makes its species (x the bite weight).</summary>
    public const float MaxBoost = 2.5f;

    private static readonly HashSet<string> Shoaling = new()
    {
        "Płoć", "Leszcz", "Krąp", "Ukleja", "Wzdręga", "Okoń", "Karaś srebrzysty", "Karaś złocisty", "Jaź", "Kleń", "Świnka",
        "Certa", "Rozpiór", "Sielawa", "Sieja", "Stynka", "Słonecznica", "Kiełb", "Jelec", "Strzebla potokowa",
    };

    private readonly List<(string Species, float X, float Z, float At)> _catches = new();

    /// <summary>The species lives in shoals.</summary>
    public static bool IsShoaling(string species) => Shoaling.Contains(species);

    /// <summary>A fish of this species was caught at (x, z) at this game hour.</summary>
    public void Add(string species, float x, float z, float at)
    {
        if (!Shoaling.Contains(species))
            return;
        _catches.Add((species, x, z, at));
        _catches.RemoveAll(c => at - c.At > StayHours * 5f);
    }

    /// <summary>Forget them all (a new map).</summary>
    public void Clear() => _catches.Clear();

    /// <summary>There is a recent shoaling catch at all (cheap test before building a map of boosts).</summary>
    public bool Any => _catches.Count > 0;

    /// <summary>The bite weight multiplier for a species at a spot now (1 = no shoal known there).</summary>
    public float Boost(string species, float x, float z, float now)
    {
        float boost = 1f;
        foreach (var c in _catches)
        {
            if (c.Species != species)
                continue;
            float dx = c.X - x, dz = c.Z - z;
            float d = MathF.Sqrt(dx * dx + dz * dz);
            if (d >= Radius)
                continue;
            boost += 0.9f * (1f - d / Radius) * MathF.Exp(-MathF.Max(0f, now - c.At) / StayHours);
        }
        return MathF.Min(boost, MaxBoost);
    }
}
