using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// Line twist (pure). A spinner's blade, and to a lesser degree a rolling spoon, turns the line with every metre it is
/// retrieved; a swivel ahead of the lure takes most of it. A twisted line springs into loops when the bail opens, and
/// past a point a cast ends in a bird's nest ("broda") that has to be cut out. Braid twists as much but hardly loops;
/// a stiff fluorocarbon loops most. A fight untwists the line a little (the fish spins it back), new line on the
/// spool none at all.
/// </summary>
public static class LineTwist
{
    /// <summary>The twist (in turns) at which the line starts to loop.</summary>
    public const float Loops = 40f;

    /// <summary>The most twist a line holds (turns).</summary>
    public const float Max = 300f;

    /// <summary>Turns put into the line per metre of retrieve by a lure of this family.</summary>
    public static float PerMetre(LureFamily family, bool swivel)
    {
        float turns = family switch
        {
            LureFamily.Spinner => 1f,
            LureFamily.Spoon => 0.45f,
            LureFamily.Softbait => 0.12f,
            LureFamily.Wobbler => 0.04f,
            _ => 0f,
        };
        return swivel ? turns * 0.12f : turns;
    }

    /// <summary>How readily a line of this kind loops (Data.LineKind as an int: 0 mono, 1 braid, 2 fluoro).</summary>
    public static float Springiness(int kind) => kind switch { 1 => 0.35f, 2 => 1.3f, _ => 1f };

    /// <summary>The chance that a cast ends in a bird's nest with this much twist on a line of this kind.</summary>
    public static float TangleChance(float twist, int kind) =>
        Math.Clamp((twist - Loops) / 160f, 0f, 0.55f) * Springiness(kind);

    /// <summary>Twist left after a bird's nest is cut out (the twisted end goes with it).</summary>
    public static float AfterTangle(float twist) => twist * 0.5f;

    /// <summary>Twist taken out by a fight of this many seconds (the fish turns the line back).</summary>
    public static float FightRelief(float seconds) => 0.6f * seconds;

    /// <summary>The HUD's word for the line's twist ("" while it is fine).</summary>
    public static string Word(float twist) => twist >= Loops * 2.5f ? "mocno skręcona" : twist >= Loops ? "skręcona" : "";
}
