using System;
using System.Collections.Generic;
using System.Linq;

namespace FishingSim.Core.Camp;

/// <summary>What a villager knows when the angler walks up: the hour, the weather and the lake's commonest fish.</summary>
public readonly record struct TalkContext(float Hour, Weather Weather, float Wind, IReadOnlyList<(string Species, float Abundance)> Fish);

/// <summary>
/// Short lines villagers say to the angler (UI banner): a greeting by the time of day, a tip that follows the
/// conditions (dawn and dusk bite best, cloud and a light ripple help, rain stirs the fish, the lake's commonest
/// species and where they keep), or village gossip. Pure: the IRng picks.
/// </summary>
public static class VillageTalk
{
    private static readonly string[] Gossip =
    {
        "Stary rybak mówi, że pod wieżą stoi sum jak kłoda. Nikt go jeszcze nie wyciągnął.",
        "W wędzarni znowu pachnie węgorzem. Jak przyniesiesz ryby, kupią od ciebie.",
        "Na tablicy na placu wiszą nowe zlecenia, zajrzyj.",
        "Po deszczu dżdżownic pod lasem jest pełno, tylko łopatę weź.",
        "Mówią, że z łódki przy trzcinach biorą największe liny.",
        "Wieczorem nad wodą tyle jętek, że siatką nałapiesz na całą zasiadkę.",
    };

    public static string Greeting(float hour) => hour switch
    {
        < 10f => "Dzień dobry!",
        < 18f => "Witaj, wędkarzu.",
        _ => "Dobry wieczór.",
    };

    /// <summary>A tip from the conditions, or null when nothing special is going on.</summary>
    public static string? Tip(TalkContext c, IRng rng)
    {
        var tips = new List<string>();
        if (c.Hour is >= 4.5f and < 8f)
            tips.Add("O świcie ryba żeruje najlepiej, nie marnuj poranka.");
        if (c.Hour is >= 18.5f and < 21.5f)
            tips.Add("Pod wieczór ryby podchodzą pod brzeg, rzucaj bliżej trzcin.");
        if (c.Hour is >= 11.5f and < 15.5f && c.Weather == Weather.Clear)
            tips.Add("W samo południe w słońcu ryba stoi głębiej, szukaj spadów dna.");
        if (c.Weather == Weather.Cloudy)
            tips.Add("Pochmurno, to i ryba śmielsza, warto posiedzieć.");
        if (c.Weather == Weather.Rain)
            tips.Add("Deszcz zmąci wodę, a zmącona woda to odważniejsze brania.");
        if (c.Wind > 0.6f)
            tips.Add("Przy takim wietrze łów na brzegu, na który wieje: tam fala niesie jedzenie.");
        var top = c.Fish.Where(f => f.Abundance > 0f).OrderByDescending(f => f.Abundance).Take(3).ToList();
        if (top.Count > 0)
        {
            var (name, _) = top[(int)(rng.NextFloat() * top.Count) % top.Count];
            tips.Add($"Tutaj najczęściej łowi się {name.ToLowerInvariant()}. {Where(name)}");
        }
        return tips.Count == 0 ? null : tips[(int)(rng.NextFloat() * tips.Count) % tips.Count];
    }

    /// <summary>One line: the greeting and either a tip (two times in three) or gossip.</summary>
    public static string Line(TalkContext c, IRng rng)
    {
        string? tip = rng.NextFloat() < 0.66f ? Tip(c, rng) : null;
        string body = tip ?? Gossip[(int)(rng.NextFloat() * Gossip.Length) % Gossip.Length];
        return $"{Greeting(c.Hour)} {body}";
    }

    /// <summary>Where the species keeps (a hint after its name).</summary>
    private static string Where(string name) => name switch
    {
        "Płoć" or "Ukleja" or "Wzdręga" => "Stoi przy roślinach, bierze na białe robaki.",
        "Okoń" => "Kręci się przy pomoście i zatopionych gałęziach.",
        "Szczupak" or "Sandacz" => "Poluje przy spadach dna, najlepiej na blachę albo żywca.",
        "Leszcz" or "Krąp" => "Trzyma się głębszej wody, na gruncie z zanętą.",
        "Lin" or "Karaś złocisty" or "Karp" => "Szukaj go przy trzcinach, rano i wieczorem.",
        "Sum" => "Nocą wychodzi na płytszą wodę.",
        _ => "Popytaj w sklepie, na co bierze.",
    };
}
