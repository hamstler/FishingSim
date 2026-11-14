using System.Collections.Generic;

namespace FishingSim.Core.Methods;

/// <summary>
/// How big a natural bait is (pure): a big mouthful (a live fish, a night crawler, a boilie) is ignored by the small
/// fry and taken by the bigger fish; a pinch of maggots or bloodworm is taken by everything, mostly by the many
/// small ones. It shifts the effective hook size the bite model and the weight roll see (as a lure's size does).
/// </summary>
public static class BaitSize
{
    private static readonly Dictionary<string, float> Shift = new()
    {
        ["ochotka"] = -1.2f, ["maggot"] = -0.8f, ["konopie"] = -0.8f, ["kasza"] = -0.5f, ["jetka"] = -0.4f,
        ["bread"] = -0.2f, ["insect"] = 0f, ["worm"] = 0f, ["groch"] = 0.2f, ["corn"] = 0.3f, ["ciasto"] = 0.3f,
        ["pedrak"] = 0.6f, ["chrabaszcz"] = 0.6f, ["rosowka"] = 1.2f, ["martwa_rybka"] = 1.6f, ["livebait"] = 2.2f,
        ["boilie"] = 1.8f, ["kulka_czosnkowa"] = 1.8f, ["kulka_plywajaca"] = 1.8f,
    };

    /// <summary>The shift of the effective hook size for a natural bait (0 for unknown ids and lures).</summary>
    public static float HookShift(string baitId) => Shift.GetValueOrDefault(baitId);
}
