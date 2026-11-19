using System;
using System.Collections.Generic;
using System.Linq;

namespace FishingSim.Core.Camp;

/// <summary>
/// Where and how long the lift net (podrywka) was down: <paramref name="SoakSeconds"/> real seconds in the water,
/// <paramref name="Depth"/> of the water under it (m), <paramref name="Cover"/> 0..1 = water plants or reeds close by
/// (small fish hide there), <paramref name="Groundbait"/> = bread crumbled over the net when it went down.
/// </summary>
public sealed record LiftContext(float SoakSeconds, float Depth, float Hour, Weather Weather, float Cover, bool Groundbait);

/// <summary>
/// The lift net (podrywka): a square of netting on two crossed hoops, lowered from a pole to the bottom by the bank
/// and lifted after a while. Small shoal fish (bleak, roach, gudgeon, sunbleak, little perch, rudd) that swam over it
/// are caught and become live bait (żywce) for predators. They come at dawn and dusk most, less in the day, few at
/// night; a longer soak, water about a metre deep, cover nearby and crumbled bread all help.
/// </summary>
public static class LiftNetting
{
    public const string LiveBait = "livebait";

    /// <summary>The net must go into at least this much water (m): it works in the shallows, resting on the bottom.</summary>
    public const float MinDepth = 0.15f;

    /// <summary>Lifting sooner than this catches nothing: the fish have not come over the net yet (s).</summary>
    public const float MinSoak = 3f;

    /// <summary>At most this many fish come up in one lift.</summary>
    public const int MaxFish = 6;

    /// <summary>The bait fish and how common each is in a lift.</summary>
    public static readonly (string Name, float Weight)[] Fry =
    {
        ("ukleja", 0.3f), ("płotka", 0.25f), ("kiełb", 0.15f), ("słonecznica", 0.12f), ("okonek", 0.1f), ("wzdręga", 0.08f),
    };

    /// <summary>How busy the small fish are at this hour: dawn and dusk 1, the day 0.7, the night 0.25.</summary>
    public static float Activity(float hour, Weather weather)
    {
        float a = hour is >= 5f and < 9f or >= 17f and < 21f ? 1f : hour is >= 9f and < 17f ? 0.7f : 0.25f;
        return a * (weather == Weather.Rain ? 0.8f : 1f);
    }

    /// <summary>How good the depth is: the shoals keep to about forty centimetres to two metres.</summary>
    public static float DepthFactor(float depth) =>
        depth < MinDepth ? 0f : depth < 0.4f ? 0.65f : depth <= 2f ? 1f : MathF.Max(0.5f, 1f - (depth - 2f) * 0.2f);

    /// <summary>The fish expected in one lift (before chance).</summary>
    public static float Expected(LiftContext c)
    {
        if (c.SoakSeconds < MinSoak)
            return 0f;
        float soak = 1f - MathF.Exp(-(c.SoakSeconds - MinSoak) / 10f);
        return 3.4f * soak * DepthFactor(c.Depth) * Activity(c.Hour, c.Weather)
               * (1f + 0.4f * Math.Clamp(c.Cover, 0f, 1f)) * (c.Groundbait ? 1.5f : 1f);
    }

    public static Haul Lift(LiftContext c, IRng rng)
    {
        if (c.Depth < MinDepth)
            return Haul.Empty("Za płytko: podrywka musi zanurzyć się w wodzie.");
        if (c.SoakSeconds < MinSoak)
            return Haul.Empty("Za szybko poderwana: rybki nie zdążyły nad nią wpłynąć.");
        int n = Math.Min(MaxFish, (int)MathF.Floor(Expected(c) * rng.Range(0.4f, 1.4f) + rng.NextFloat()));
        if (n <= 0)
            return Haul.Empty(c.Hour is < 5f or >= 21f ? "Pusto: nocą drobnica śpi przy dnie." : "Pusto: poczekaj dłużej przed poderwaniem.");
        var kinds = new Dictionary<string, int>();
        float total = Fry.Sum(f => f.Weight);
        for (int i = 0; i < n; i++)
        {
            float u = rng.NextFloat() * total;
            string name = Fry[^1].Name;
            foreach (var (fry, w) in Fry)
            {
                if (u < w)
                {
                    name = fry;
                    break;
                }
                u -= w;
            }
            kinds[name] = kinds.GetValueOrDefault(name) + 1;
        }
        string list = string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} ×{k.Value}"));
        return new Haul(new Dictionary<string, int> { [LiveBait] = n }, $"Podrywka: {list} → żywce ×{n}");
    }
}
