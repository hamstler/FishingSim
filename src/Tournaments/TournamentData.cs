using Godot;

namespace FishingSim.Tournaments;

/// <summary>A tournament definition. Add one by adding a .tres in data/tournaments/ and listing it in <see cref="TournamentManager"/>.</summary>
[GlobalClass]
public partial class TournamentData : Resource
{
    [Export] public string DisplayName { get; set; } = "Turniej";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";

    /// <summary>Display name of the location the tournament is held at.</summary>
    [Export] public string LocationName { get; set; } = "";
    [Export] public int RequiredLevel { get; set; } = 1;
    [Export] public int EntryFee { get; set; } = 10;

    /// <summary>Length in real minutes.</summary>
    [Export] public float DurationMinutes { get; set; } = 6f;

    /// <summary>Only the N heaviest fish count towards the score.</summary>
    [Export] public int CountedFish { get; set; } = 5;
    [Export] public int BotCount { get; set; } = 6;
    [Export(PropertyHint.Range, "0,1")] public float BotSkillMin { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "0,1")] public float BotSkillMax { get; set; } = 0.7f;

    /// <summary>Prize money for 1st, 2nd, 3rd... place.</summary>
    [Export] public int[] PrizeMoney { get; set; } = { 100, 50, 25 };

    /// <summary>Bonus XP for 1st, 2nd, 3rd... place.</summary>
    [Export] public int[] PrizeXp { get; set; } = { 60, 30, 15 };

    public float DurationSeconds => DurationMinutes * 60f;

    public int PrizeMoneyFor(int rank) => rank >= 1 && rank <= PrizeMoney.Length ? PrizeMoney[rank - 1] : 0;

    public int PrizeXpFor(int rank) => rank >= 1 && rank <= PrizeXp.Length ? PrizeXp[rank - 1] : 0;
}
