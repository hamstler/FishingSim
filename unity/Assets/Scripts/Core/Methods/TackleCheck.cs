using System;

namespace FishingSim.Core.Methods;

/// <summary>How the drag sits against the tackle's weakest link (pure).</summary>
public enum TackleVerdict
{
    /// <summary>The drag gives line well before anything breaks, and holds hard enough to set the hook.</summary>
    Good,
    /// <summary>The drag holds close to the breaking load: a sudden run can break the line or the rod.</summary>
    Tight,
    /// <summary>The drag holds more than the line or the rod can take: they break before the drag slips.</summary>
    TooTight,
    /// <summary>The drag is so loose that the strike barely sets the hook and every fish takes line.</summary>
    Loose,
}

/// <summary>
/// The check shown before a cast: does the drag slip before the weakest link (line with its leader, or the rod) breaks?
/// In the fight (FightModel) the tension stays at the drag's hold while the fish takes line, plus a share of the excess
/// the rod cannot absorb, so the drag has to hold clearly below the breaking load: <see cref="TightShare"/> of it.
/// </summary>
public static class TackleCheck
{
    /// <summary>Above this share of the breaking load a run's spike can break the tackle.</summary>
    public const float TightShare = 0.8f;

    /// <summary>Below this hold (kg) the strike loses force (see <see cref="Strike.DragFactor"/>, full at 0.9 kg).</summary>
    public const float LooseKg = 0.9f;

    /// <summary>The drag's hold (kg) as a share of the breaking load (0 .. any).</summary>
    public static float Share(float dragKg, float lineKg, float rodKg, float hookKg = float.MaxValue) =>
        dragKg / MathF.Max(Limit(lineKg, rodKg, hookKg), 0.01f);

    /// <summary>The weakest link's breaking load (kg).</summary>
    public static float Limit(float lineKg, float rodKg, float hookKg = float.MaxValue) => MathF.Min(MathF.Min(lineKg, rodKg), hookKg);

    public static TackleVerdict Judge(float dragKg, float lineKg, float rodKg, float hookKg = float.MaxValue)
    {
        float share = Share(dragKg, lineKg, rodKg, hookKg);
        if (share > 1f)
            return TackleVerdict.TooTight;
        if (share > TightShare)
            return TackleVerdict.Tight;
        if (dragKg < LooseKg && share < 0.3f)
            return TackleVerdict.Loose;
        return TackleVerdict.Good;
    }

    /// <summary>The drag setting (0..1 of the reel's maximum) at which the drag holds as much as the tackle takes.</summary>
    public static float BreakSetting(float dragMaxKg, float lineKg, float rodKg, float hookKg = float.MaxValue) =>
        Math.Clamp(Limit(lineKg, rodKg, hookKg) / MathF.Max(dragMaxKg, 0.01f), 0f, 1f);

    /// <summary>One line for the angler, e.g. "Hamulec za mocny: trzyma 3,4 kg, linka pęka przy 3,0 kg".</summary>
    public static string Describe(float dragKg, float lineKg, float rodKg, float hookKg = float.MaxValue)
    {
        float limit = Limit(lineKg, rodKg, hookKg);
        string weak = limit == rodKg ? "wędka" : limit == hookKg ? "haczyk" : "linka";
        string note = rodKg < lineKg * 0.8f ? " (wędka słabsza od linki)" : hookKg < lineKg * 0.8f ? " (haczyk słabszy od linki)" : "";
        return Judge(dragKg, lineKg, rodKg, hookKg) switch
        {
            TackleVerdict.TooTight => $"Hamulec za mocny: trzyma {dragKg:0.0} kg, {weak} pęka przy {limit:0.0} kg. Poluzuj!",
            TackleVerdict.Tight => $"Hamulec na granicy: {dragKg:0.0} z {limit:0.0} kg. Mocny odjazd może zerwać zestaw{note}",
            TackleVerdict.Loose => $"Hamulec bardzo luźny: {dragKg:0.0} kg. Słabe zacięcie, ryba łatwo bierze linkę",
            _ => $"Zestaw dobrze ustawiony: hamulec {dragKg:0.0} kg, {weak} wytrzyma {limit:0.0} kg",
        };
    }
}
