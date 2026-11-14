using System;
using System.Collections.Generic;

namespace FishingSim.Core.Methods;

/// <summary>
/// A predator grabs the hooked little fish (pure): a roach or a bleak struggling on the line is the easiest meal in
/// the lake, and now and then a pike, a perch, a zander or an asp takes it on the way in. The angler then plays the
/// predator, which holds the little fish rather than the hook, so it may let go.
/// </summary>
public static class PreyAttack
{
    private static readonly HashSet<string> Prey = new()
    {
        "Płoć", "Ukleja", "Krąp", "Wzdręga", "Kiełb", "Jazgarz", "Słonecznica", "Stynka", "Okoń", "Leszcz", "Karaś srebrzysty",
        "Różanka", "Strzebla potokowa", "Ciernik", "Słonecznica", "Kleń", "Jelec", "Sielawa",
    };

    /// <summary>The predators that take a struggling fish, with how often each does (x its abundance).</summary>
    public static readonly IReadOnlyDictionary<string, float> Predators = new Dictionary<string, float>
    {
        ["Szczupak"] = 1f, ["Okoń"] = 0.6f, ["Sandacz"] = 0.6f, ["Boleń"] = 0.5f, ["Sum"] = 0.3f, ["Pstrąg potokowy"] = 0.3f,
        ["Bass wielkogębowy"] = 0.6f, ["Troć jeziorowa"] = 0.2f,
    };

    /// <summary>Little fish up to this weight (kg) tempt a predator.</summary>
    public const float MaxPreyKg = 0.3f;

    /// <summary>A fish of this species and weight on the line is prey.</summary>
    public static bool IsPrey(string species, float weightKg) => weightKg <= MaxPreyKg && Prey.Contains(species);

    /// <summary>The chance per second that a predator grabs the hooked prey, given how many predators the water holds
    /// (the sum of their abundance x tendency, about 1 in a pike lake).</summary>
    public static float ChancePerSecond(float predatorPresence) => 0.008f * Math.Clamp(predatorPresence, 0f, 3f);

    /// <summary>How well the predator holds on (it has the fish, not the hook).</summary>
    public const float Hold = 0.45f;
}
