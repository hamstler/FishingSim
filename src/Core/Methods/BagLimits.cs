using System.Collections.Generic;

namespace FishingSim.Core.Methods;

/// <summary>
/// Daily bag limits (pure), after the usual Polish fishery rules: a few of the prized predators and game fish may be
/// kept a day (two pike, two zander, one wels...), the rest freely. A fish over the limit goes back.
/// </summary>
public static class BagLimits
{
    private static readonly Dictionary<string, int> Limits = new()
    {
        ["Szczupak"] = 2, ["Sandacz"] = 2, ["Sandacz wołżański"] = 2, ["Boleń"] = 2, ["Sum"] = 1, ["Węgorz"] = 2,
        ["Pstrąg potokowy"] = 3, ["Pstrąg tęczowy"] = 3, ["Pstrąg źródlany"] = 3, ["Lipień"] = 2, ["Troć wędrowna"] = 1,
        ["Troć jeziorowa"] = 1, ["Łosoś"] = 1, ["Głowacica"] = 1, ["Palia"] = 2, ["Sieja"] = 3, ["Brzana"] = 3,
        ["Karp"] = 2, ["Karp lustrzeń"] = 2, ["Sazan"] = 2, ["Amur biały"] = 2, ["Lin"] = 3, ["Kleń"] = 5, ["Jaź"] = 5,
    };

    /// <summary>How many of a species may be kept a day, or null for no limit.</summary>
    public static int? LimitOf(string species) => Limits.TryGetValue(species, out int n) ? n : null;

    /// <summary>One more of this species may still be kept today.</summary>
    public static bool MayKeep(string species, int keptToday) => LimitOf(species) is not int n || keptToday < n;
}
