using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Core;
using FishingSim.Data;
using FishingSim.Progress;

namespace FishingSim.Tournaments;

/// <summary>One line of the live or final ranking.</summary>
public sealed record Standing(int Rank, string Name, bool IsPlayer, int Fish, float Score, float BestFish);

/// <summary>
/// A running tournament: the player and AI anglers compete on the sum of their N heaviest fish. Bots catch fish on a
/// Poisson schedule that depends on their skill. No engine types are used, so the whole thing is unit-testable.
/// </summary>
public sealed class TournamentSession
{
    public const string PlayerLabel = "Ty";

    private static readonly string[] BotNames =
    {
        "Marek", "Kasia", "Jacek", "Ola", "Tomek", "Basia", "Piotr", "Ewa", "Zbyszek", "Dorota", "Wojtek", "Ania",
    };

    private sealed class Entrant
    {
        public string Name = "";
        public bool IsPlayer;
        public float Skill;
        public float NextCatchAt = float.MaxValue;
        public readonly List<float> Weights = new();
    }

    private readonly IReadOnlyList<FishSpecies> _species;
    private readonly LocationData _location;
    private readonly IRng _rng;
    private readonly List<Entrant> _entrants = new();
    private readonly Entrant _player;

    public TournamentData Data { get; }
    public float Elapsed { get; private set; }

    public TournamentSession(TournamentData data, IReadOnlyList<FishSpecies> species, LocationData location, string playerName, IRng rng)
    {
        Data = data;
        _species = species;
        _location = location;
        _rng = rng;
        _player = new Entrant { Name = playerName, IsPlayer = true };
        _entrants.Add(_player);

        int count = Math.Clamp(data.BotCount, 0, BotNames.Length);
        var names = BotNames.OrderBy(_ => rng.NextFloat()).Take(count).ToList();
        for (int i = 0; i < count; i++)
        {
            float t = count > 1 ? (float)i / (count - 1) : 0.5f;
            var bot = new Entrant { Name = names[i], Skill = Math.Clamp(data.BotSkillMin + (data.BotSkillMax - data.BotSkillMin) * t, 0f, 1f) };
            bot.NextCatchAt = NextCatchTime(bot, 0f);
            _entrants.Add(bot);
        }
    }

    public float Duration => Data.DurationSeconds;
    public float Remaining => MathF.Max(Duration - Elapsed, 0f);
    public bool Finished => Elapsed >= Duration;
    public int Entrants => _entrants.Count;

    /// <summary>Fish per minute for a bot of the given skill.</summary>
    public static float BotRatePerMinute(float skill) => Mathf.Lerp(0.6f, 1.6f, skill);

    /// <summary>Time of the bot's next catch after <paramref name="from"/> (exponentially distributed gaps).</summary>
    private float NextCatchTime(Entrant bot, float from)
    {
        float ratePerSecond = BotRatePerMinute(bot.Skill) / 60f;
        return from - MathF.Log(1f - _rng.NextFloat() * 0.999f) / ratePerSecond;
    }

    /// <summary>Advances the clock; bots land their fish on schedule.</summary>
    public void Advance(float dt)
    {
        if (Finished)
            return;
        Elapsed = MathF.Min(Elapsed + dt, Duration);
        foreach (var bot in _entrants.Where(e => !e.IsPlayer))
        {
            while (bot.NextCatchAt <= Elapsed)
            {
                float landedAt = bot.NextCatchAt;
                float weight = RollBotFish(bot);
                if (weight > 0f)
                    bot.Weights.Add(weight);
                bot.NextCatchAt = NextCatchTime(bot, landedAt);
            }
        }
    }

    private float RollBotFish(Entrant bot)
    {
        float total = 0f;
        foreach (var sp in _species)
            total += sp.Rarity * _location.AbundanceOf(sp.SpeciesName);
        if (total <= 0f)
            return 0f;
        float roll = _rng.NextFloat() * total;
        FishSpecies chosen = _species[0];
        foreach (var sp in _species)
        {
            float w = sp.Rarity * _location.AbundanceOf(sp.SpeciesName);
            if (w <= 0f)
                continue;
            chosen = sp;
            roll -= w;
            if (roll <= 0f)
                break;
        }
        // Better anglers find the bigger fish: the exponent shrinks from 2.6 (novice) to 1.4 (expert).
        float exponent = Mathf.Lerp(2.6f, 1.4f, bot.Skill);
        return chosen.MinWeight + (chosen.MaxWeight - chosen.MinWeight) * MathF.Pow(_rng.NextFloat(), exponent);
    }

    public void AddPlayerCatch(float weight)
    {
        if (!Finished)
            _player.Weights.Add(weight);
    }

    private float ScoreOf(Entrant e) => e.Weights.OrderByDescending(w => w).Take(Data.CountedFish).Sum();

    /// <summary>The ranking, best first. Ties are broken by the biggest single fish, then by name.</summary>
    public IReadOnlyList<Standing> Standings()
    {
        var ordered = _entrants
            .Select(e => (Entrant: e, Score: ScoreOf(e), Best: e.Weights.Count > 0 ? e.Weights.Max() : 0f))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Best)
            .ThenBy(x => x.Entrant.Name, StringComparer.Ordinal)
            .ToList();
        var result = new List<Standing>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            var (e, score, best) = ordered[i];
            result.Add(new Standing(i + 1, e.IsPlayer ? PlayerLabel : e.Name, e.IsPlayer, e.Weights.Count, score, best));
        }
        return result;
    }

    public Standing PlayerStanding => Standings().First(s => s.IsPlayer);

    public Standing Leader => Standings()[0];

    /// <summary>The final result (prizes by rank). Meaningful once <see cref="Finished"/>.</summary>
    public TournamentRecord Result()
    {
        var me = PlayerStanding;
        return new TournamentRecord(Data.DisplayName, me.Rank, Entrants, me.Score, Data.PrizeMoneyFor(me.Rank), Data.PrizeXpFor(me.Rank));
    }
}
