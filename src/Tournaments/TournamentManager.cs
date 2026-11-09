using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Core;
using FishingSim.Data;
using FishingSim.Fishing;
using FishingSim.Progress;
using FishingSim.World;

namespace FishingSim.Tournaments;

/// <summary>Starts, runs and settles tournaments. Fish landed during a tournament count towards the player's score.</summary>
public partial class TournamentManager : Node
{
    public static readonly string[] TournamentPaths =
    {
        "res://data/tournaments/ploc.tres",
        "res://data/tournaments/zatoka.tres",
        "res://data/tournaments/karp.tres",
    };

    /// <summary>The hour a tournament starts at (a bright morning).</summary>
    public const float StartHour = 6.5f;

    private FishingRod _rod = null!;
    private IRng _rng = new SystemRng();

    public List<TournamentData> Tournaments { get; } = new();
    public TournamentSession? Active { get; private set; }

    /// <summary>Raised when a tournament ends: the result and the final standings.</summary>
    public event Action<TournamentRecord, IReadOnlyList<Standing>>? Finished;

    /// <summary>Raised whenever a tournament starts or is abandoned.</summary>
    public event Action? ActiveChanged;

    public void Setup(FishingRod rod, IRng? rng = null)
    {
        _rod = rod;
        if (rng != null)
            _rng = rng;
        foreach (var path in TournamentPaths)
        {
            var data = GD.Load<TournamentData>(path);
            if (data != null)
                Tournaments.Add(data);
        }
        _rod.FishLanded += OnFishLanded;
    }

    public override void _ExitTree()
    {
        if (_rod != null)
            _rod.FishLanded -= OnFishLanded;
    }

    /// <summary>Why the tournament cannot be started right now, or null when it can.</summary>
    public string? CanStart(TournamentData data)
    {
        if (Active != null)
            return "Turniej już trwa";
        if (_rod.State != RodState.Idle)
            return "Zakończ łowienie przed startem";
        if (_rod.Profile.Level < data.RequiredLevel)
            return $"Wymagany poziom {data.RequiredLevel}";
        if (_rod.Profile.Money < data.EntryFee)
            return $"Za mało pieniędzy (wpisowe {data.EntryFee} gr)";
        int location = _rod.Lake.Locations.FindIndex(l => l.DisplayName == data.LocationName);
        if (location < 0)
            return "Brak łowiska tego turnieju";
        if (_rod.Lake.Locations[location].RequiredLevel > _rod.Profile.Level)
            return "Łowisko turnieju jest jeszcze zablokowane";
        return null;
    }

    /// <summary>Pays the entry fee, travels to the venue and starts the clock. Returns an error message or null.</summary>
    public string? Start(TournamentData data)
    {
        string? error = CanStart(data);
        if (error != null)
            return error;
        var profile = _rod.Profile;
        profile.Money -= data.EntryFee;
        int location = _rod.Lake.Locations.FindIndex(l => l.DisplayName == data.LocationName);
        if (_rod.Lake.LocationIndex != location)
            _rod.Lake.SetLocation(location);
        _rod.World.Hour = StartHour;
        Active = new TournamentSession(data, _rod.Species, _rod.Lake.Location, profile.Username, _rng);
        ActiveChanged?.Invoke();
        profile.NotifyChanged();
        return null;
    }

    /// <summary>Leaves the tournament without a result (the entry fee is lost).</summary>
    public void Abandon()
    {
        if (Active == null)
            return;
        Active = null;
        ActiveChanged?.Invoke();
    }

    private void OnFishLanded(FishSpecies species, float weight) => Active?.AddPlayerCatch(weight);

    public override void _Process(double delta)
    {
        if (Active == null)
            return;
        Active.Advance((float)delta);
        if (Active.Finished)
            Complete();
    }

    /// <summary>Settles the running tournament: prizes, history and the Finished event.</summary>
    public void Complete()
    {
        if (Active == null)
            return;
        var session = Active;
        var standings = session.Standings();
        var result = session.Result();
        Active = null;
        _rod.Profile.AwardTournament(result);
        ActiveChanged?.Invoke();
        Finished?.Invoke(result, standings);
    }
}
