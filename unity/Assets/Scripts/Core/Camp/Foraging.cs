using System;
using System.Collections.Generic;
using System.Linq;

namespace FishingSim.Core.Camp;

/// <summary>What grows on the forest floor and can be picked with E.</summary>
public enum ForageKind { Porcini, Chanterelle, Blueberry }

/// <summary>
/// Picking in the forest: porcini (borowiki) under the trees, chanterelles (kurki) in the moss, blueberries (jagody) in
/// the undergrowth. A picked spot grows again after a game day. Mushrooms come up best after rain and are found in the
/// morning; the pickings go to the pantry (Profile.Pantry) for cooking, notices and the buyer.
/// </summary>
public static class Foraging
{
    public const string Porcini = "borowik", Chanterelle = "kurka", Blueberry = "jagody";

    /// <summary>A picked spot grows back after this many game hours.</summary>
    public const float RegrowHours = 24f;

    public static string IdOf(ForageKind k) => k switch
    {
        ForageKind.Porcini => Porcini,
        ForageKind.Chanterelle => Chanterelle,
        _ => Blueberry,
    };

    /// <summary>Pantry names (Polish, for counts: "kurki ×6").</summary>
    public static readonly Dictionary<string, string> Names = new()
    {
        [Porcini] = "borowiki", [Chanterelle] = "kurki", [Blueberry] = "jagody",
    };

    /// <summary>What one piece fetches at the buyer (gr).</summary>
    public static readonly Dictionary<string, int> Prices = new()
    {
        [Porcini] = 9, [Chanterelle] = 3, [Blueberry] = 1,
    };

    public static string NameOf(string id) => Names.GetValueOrDefault(id, id);

    public static string Describe(IReadOnlyDictionary<string, int> items) =>
        string.Join(", ", items.Where(i => i.Value > 0).Select(i => $"{NameOf(i.Key)} ×{i.Value}"));

    /// <summary>Which plant to put on a forest spot: porcini near trunks, chanterelles in moss, blueberries elsewhere.</summary>
    public static ForageKind KindFor(float nearTree, float moss, IRng rng)
    {
        float u = rng.NextFloat();
        if (nearTree > 0.5f && u < 0.4f)
            return ForageKind.Porcini;
        if (moss > 0.4f && u < 0.75f)
            return ForageKind.Chanterelle;
        return u < 0.12f ? ForageKind.Porcini : u < 0.45f ? ForageKind.Chanterelle : ForageKind.Blueberry;
    }

    /// <summary>
    /// What one spot gives: a porcini or two, a cluster of chanterelles, a handful of blueberries. Rain in the last day
    /// (<paramref name="wet"/>) brings more mushrooms.
    /// </summary>
    public static Haul Pick(ForageKind kind, bool wet, IRng rng)
    {
        int n = kind switch
        {
            ForageKind.Porcini => 1 + (rng.NextFloat() < (wet ? 0.6f : 0.3f) ? 1 : 0),
            ForageKind.Chanterelle => (int)MathF.Floor(rng.Range(3f, 7f) * (wet ? 1.4f : 1f)),
            _ => (int)MathF.Floor(rng.Range(8f, 18f)),
        };
        var items = new Dictionary<string, int> { [IdOf(kind)] = n };
        string verb = kind == ForageKind.Blueberry ? "Zebrano" : "Znaleziono";
        return new Haul(items, $"{verb}: {Describe(items)}");
    }

    /// <summary>True when a spot picked at <paramref name="pickedAt"/> (game hours) has grown back by <paramref name="now"/>.</summary>
    public static bool Regrown(float pickedAt, float now) => now - pickedAt >= RegrowHours;
}
