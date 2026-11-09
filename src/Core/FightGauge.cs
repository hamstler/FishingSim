using System;

namespace FishingSim.Core;

/// <summary>
/// The fight bar: how much of the tackle's strength is still in reserve, shown on four segments that cover
/// 0-10%, 10-25%, 25-50% and 50-100% of reserve and take 7%, 18%, 25% and 50% of the bar's width. Left (red) = the fish pulls almost to breaking point, ease the drag;
/// right (green) = plenty in reserve, the drag can be tightened; the middle (light orange, ~25% reserve) is the sweet
/// spot where the fish tires without risking the gear. Pure functions.
/// </summary>
public static class FightGauge
{
    /// <summary>Upper edges of the segments, as reserve fractions.</summary>
    public static readonly float[] Edges = { 0.10f, 0.25f, 0.50f, 1.00f };

    /// <summary>Share of the strength still unused: 1 = no load, 0 = at the breaking limit.</summary>
    public static float Reserve(float tension, float limit) =>
        limit <= 0f ? 0f : Math.Clamp(1f - tension / limit, 0f, 1f);

    /// <summary>Width of each segment as a share of the bar (red is narrow, green takes half).</summary>
    public static readonly float[] Widths = { 0.07f, 0.18f, 0.25f, 0.50f };

    /// <summary>Left edge of segment <paramref name="i"/> on the bar (0..1).</summary>
    public static float SegmentStart(int i)
    {
        float x = 0f;
        for (int k = 0; k < i && k < Widths.Length; k++)
            x += Widths[k];
        return x;
    }

    /// <summary>Where the needle sits (0 = left edge, 1 = right edge), linear inside each segment.</summary>
    public static float Position(float reserve)
    {
        reserve = Math.Clamp(reserve, 0f, 1f);
        float lo = 0f;
        for (int i = 0; i < Edges.Length; i++)
        {
            if (reserve <= Edges[i] || i == Edges.Length - 1)
                return SegmentStart(i) + Widths[i] * (reserve - lo) / (Edges[i] - lo);
            lo = Edges[i];
        }
        return 1f;
    }

    /// <summary>0 = red (danger), 1 = orange, 2 = light orange (ideal), 3 = green (slack).</summary>
    public static int Zone(float reserve)
    {
        for (int i = 0; i < Edges.Length; i++)
            if (reserve <= Edges[i])
                return i;
        return Edges.Length - 1;
    }

    public static string Advice(float reserve) => Zone(reserve) switch
    {
        0 => "Poluzuj hamulec! Zestaw zaraz pęknie",
        1 => "Mocno, uważaj na zrywy",
        2 => "Dobrze: ryba się męczy",
        _ => "Luźno, możesz dokręcić hamulec",
    };
}
