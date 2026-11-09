using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FishingSim.Accounts;
using FishingSim.Core;
using FishingSim.Data;
using FishingSim.Fishing;
using FishingSim.Progress;
using FishingSim.Tournaments;
using FishingSim.UI;
using FishingSim.World;

namespace FishingSim.Tests;

/// <summary>Tournament scoring, bots, prizes, history, and the manager running inside the game.</summary>
public sealed class TournamentSuite : ITestSuite
{
    public string Name => "tournaments";

    private static FishSpecies Species(string id) => GD.Load<FishSpecies>($"res://data/fish/{id}.tres");

    private static TournamentData Data(string id) => GD.Load<TournamentData>($"res://data/tournaments/{id}.tres");

    public async Task Run(TestContext t)
    {
        Core(t);
        await InGame(t);
    }

    private static void Core(TestContext t)
    {
        var species = new[] { "ploc", "leszcz", "karp", "szczupak" }.Select(Species).ToList();
        var lake = GD.Load<LocationData>("res://data/locations/jezioro.tres");
        var cup = Data("ploc");

        t.Section("tournament data");
        t.Check(cup.DisplayName == "Poranny Puchar Płoci" && cup.EntryFee == 10 && cup.CountedFish == 5, "the .tres loads into the C# resource");
        t.Check(cup.PrizeMoneyFor(1) == 120 && cup.PrizeMoneyFor(3) == 30 && cup.PrizeMoneyFor(4) == 0 && cup.PrizeMoneyFor(0) == 0, "prize money by rank (0 below the podium)");
        t.Check(cup.PrizeXpFor(2) == 30 && cup.PrizeXpFor(9) == 0, "bonus XP by rank");
        t.Check(Math.Abs(cup.DurationSeconds - 360f) < 0.01f, "duration is in real seconds");

        t.Section("session basics");
        var session = new TournamentSession(cup, species, lake, "admin", new SystemRng(5));
        t.Check(session.Entrants == cup.BotCount + 1, "the player plus the bots");
        t.Check(!session.Finished && session.Remaining > 359f && session.Elapsed == 0f, "starts with the full time left");
        t.Check(session.Standings().All(s => s.Score == 0f && s.Fish == 0), "nobody has caught anything yet");
        t.Check(session.Standings().Select(s => s.Rank).SequenceEqual(Enumerable.Range(1, session.Entrants)), "ranks run 1..N");

        session.AddPlayerCatch(1.5f);
        session.AddPlayerCatch(0.5f);
        var me = session.PlayerStanding;
        t.Check(me.Fish == 2 && Math.Abs(me.Score - 2.0f) < 0.001f && me.Name == "Ty", "the player's catches add up");
        foreach (float w in new[] { 3f, 2f, 1f, 4f, 0.2f })
            session.AddPlayerCatch(w);
        t.Near(session.PlayerStanding.Score, 4f + 3f + 2f + 1.5f + 1f, 0.001f, "only the 5 heaviest fish count");
        t.Near(session.PlayerStanding.BestFish, 4f, 0.001f, "best single fish is tracked");

        t.Section("bots");
        float lastLeader = 0f;
        bool monotonic = true;
        while (!session.Finished)
        {
            session.Advance(5f);
            float leader = session.Standings().Where(s => !s.IsPlayer).Max(s => s.Score);
            monotonic &= leader >= lastLeader - 1e-4f;
            lastLeader = leader;
        }
        t.Check(monotonic, "a bot's score never goes down");
        t.Check(session.Finished && session.Remaining == 0f && Math.Abs(session.Elapsed - session.Duration) < 0.01f, "the session ends exactly at the duration");
        var botStandings = session.Standings().Where(s => !s.IsPlayer).ToList();
        t.Check(botStandings.All(s => s.Fish >= 1), "every bot caught something in 6 minutes");
        float before = session.Standings().Sum(s => s.Score);
        session.Advance(100f);
        session.AddPlayerCatch(50f);
        t.Near(session.Standings().Sum(s => s.Score), before, 0.0001f, "nothing changes after the final whistle");

        var sorted = session.Standings();
        t.Check(sorted.Zip(sorted.Skip(1), (a, b) => a.Score >= b.Score).All(x => x), "the ranking is sorted by score");

        t.Section("bot behaviour over many tournaments");
        var counts = new List<int>();
        for (int seed = 0; seed < 30; seed++)
        {
            var s = new TournamentSession(cup, species, lake, "admin", new SystemRng(seed));
            s.Advance(s.Duration);
            counts.AddRange(s.Standings().Where(x => !x.IsPlayer).Select(x => x.Fish));
        }
        float meanFish = (float)counts.Average();
        t.Check(meanFish is > 3f and < 12f, $"bots land {meanFish:0.0} fish on average in a 6-minute cup");

        float Mean(float skill)
        {
            var data = new TournamentData { BotCount = 4, BotSkillMin = skill, BotSkillMax = skill, CountedFish = 5, DurationMinutes = 6f };
            return (float)Enumerable.Range(0, 40).Average(seed =>
            {
                var s = new TournamentSession(data, species, lake, "x", new SystemRng(seed + 1000));
                s.Advance(s.Duration);
                return s.Standings().Where(x => !x.IsPlayer).Average(x => x.Score);
            });
        }
        float novice = Mean(0.1f), expert = Mean(0.9f);
        t.Check(expert > novice * 1.5f, $"experts score more than novices ({novice:0.0} kg vs {expert:0.0} kg)");
        t.Check(TournamentSession.BotRatePerMinute(0.9f) > TournamentSession.BotRatePerMinute(0.1f), "skilled bots catch faster");

        var big = new TournamentData { BotCount = 50 };
        t.Check(new TournamentSession(big, species, lake, "x", new SystemRng(1)).Entrants <= 13, "the bot count is capped by the available names");

        var a = new TournamentSession(cup, species, lake, "x", new SystemRng(77));
        var b = new TournamentSession(cup, species, lake, "x", new SystemRng(77));
        a.Advance(200f); b.Advance(200f);
        t.Check(a.Standings().SequenceEqual(b.Standings()), "the same seed gives the same tournament");

        t.Section("results and prizes");
        var win = new TournamentSession(cup, species, lake, "admin", new SystemRng(9));
        for (int i = 0; i < 5; i++)
            win.AddPlayerCatch(30f);
        win.Advance(win.Duration);
        var result = win.Result();
        t.Check(result.Rank == 1 && result.PrizeMoney == 120 && result.PrizeXp == 60, "a huge haul wins first prize");
        t.Check(result.Entrants == win.Entrants && result.Name == cup.DisplayName, "the record names the tournament and field size");

        var lose = new TournamentSession(cup, species, lake, "admin", new SystemRng(9));
        lose.Advance(lose.Duration);
        var lost = lose.Result();
        t.Check(lost.Rank > 3 && lost.PrizeMoney == 0 && lost.Score == 0f, "no fish means no prize");

        t.Section("profile history");
        var p = new Profile();
        int levelBefore = p.Level;
        bool leveled = p.AwardTournament(new TournamentRecord("Cup", 1, 7, 12.5f, 200, 100));
        t.Check(p.Money == Profile.StartMoney + 200 && p.Xp == 100 && leveled && p.Level > levelBefore, "prize money and XP are paid, and can level up");
        p.AwardTournament(new TournamentRecord("Cup", 4, 7, 5f, 0, 0));
        t.Check(p.TournamentHistory.Count == 2 && p.TournamentWins == 1, "history and win counter");
        for (int i = 0; i < 60; i++)
            p.AwardTournament(new TournamentRecord("Cup", 2, 7, 1f, 0, 0));
        t.Check(p.TournamentHistory.Count == 50, "history keeps the last 50");
        p.TournamentHistory.Clear();
        p.AwardTournament(new TournamentRecord("Zawody", 2, 8, 9.25f, 60, 30));
        const string path = "/tmp/fishsim_cs_tournament_save.json";
        p.Save(path);
        var loaded = Profile.LoadFrom(path);
        t.Check(loaded.TournamentHistory.Count == 1 && loaded.TournamentHistory[0] == p.TournamentHistory[0], "tournament history survives save/load");
    }

    private static InputEventKey Key(Godot.Key code) => new() { Keycode = code, Pressed = true };

    private async Task InGame(TestContext t)
    {
        t.Section("manager inside the game");
        UserStore.Path = "/tmp/fishsim_cs_tournament_users.json";
        Session.Username = "admin";
        Session.MapIndex = 0;
        var main = GD.Load<PackedScene>("res://main.tscn").Instantiate<Main>();
        t.Host.GetTree().Root.AddChild(main);
        await t.NextFrame();
        await t.NextFrame();
        var rod = main.Rod;
        var manager = main.Tournaments;
        var profile = rod.Profile;
        var lake = rod.Lake;
        var cup = manager.Tournaments.First(x => x.DisplayName == "Poranny Puchar Płoci");
        var bay = manager.Tournaments.First(x => x.DisplayName == "Zatoka Szczupaka");
        var results = main.GetChildren().OfType<TournamentResultsScreen>().First();
        var ui = main.GetChildren().OfType<GameUI>().First();

        t.Check(manager.Tournaments.Count == 3, "all three tournaments are listed");
        t.Check(manager.CanStart(bay)?.Contains("poziom") == true, $"a level-2 tournament is locked at level 1 ({manager.CanStart(bay)})");
        profile.Money = 5;
        t.Check(manager.CanStart(cup)?.Contains("pieniędzy") == true, "not enough money for the entry fee");
        profile.Money = 100;
        rod.CastNow(0.3f);
        t.Check(manager.CanStart(cup) != null, "cannot start while a line is out");
        rod.ResetToIdle();
        t.Check(manager.CanStart(cup) == null, "a level-1 cup is open to a new player with enough money");

        bool finishedFired = false;
        manager.Finished += (_, _) => finishedFired = true;
        t.Check(manager.Start(cup) == null && profile.Money == 90, "starting charges the 10 zł entry fee");
        t.Check(manager.Active != null && rod.World.Hour == TournamentManager.StartHour && lake.Location.DisplayName == cup.LocationName, "the tournament moves you to its venue at the start hour");
        t.Check(manager.Start(cup) == "Turniej już trwa", "only one tournament at a time");

        rod.ForceLand(Species("ploc"), 1.2f);
        rod.ResetToIdle();
        t.Check(manager.Active!.PlayerStanding.Fish == 1 && Math.Abs(manager.Active.PlayerStanding.Score - 1.2f) < 0.001f, "a landed fish counts for the player");

        ui._UnhandledInput(Key(Godot.Key.T));
        t.Check(ui.Visible && ui.CurrentTab == 3, "T opens the tournaments tab");
        ui._UnhandledInput(Key(Godot.Key.T));
        t.Check(!ui.Visible, "T again closes it");

        manager.Active.Advance(manager.Active.Duration + 1f);
        manager._Process(0.0);
        t.Check(manager.Active == null && finishedFired, "when time runs out the tournament is settled automatically");
        t.Check(profile.TournamentHistory.Count == 1 && profile.TournamentHistory[0].Name == cup.DisplayName, "the result is in the history");
        t.Check(results.Visible && rod.InputBlocked, "the results window opens and blocks fishing input");
        results.Close();
        t.Check(!results.Visible && !rod.InputBlocked, "closing it re-enables input");
        int moneyAfter = profile.Money;
        t.Check(moneyAfter >= 90, "prize money is added to the entry-fee-reduced balance");

        profile.Money = 100;
        t.Check(manager.Start(cup) == null, "a second run can start");
        var pause = main.GetChildren().OfType<PauseMenu>().First();
        int historyBefore = profile.TournamentHistory.Count;
        manager.Abandon();
        t.Check(manager.Active == null && profile.TournamentHistory.Count == historyBefore, "abandoning leaves no result (the fee is lost)");
        t.Check(profile.Money == 90, "the entry fee is not refunded");
        pause.Open();
        t.Check(pause.Visible, "the pause menu opens (shows a warning only during a tournament)");
        pause.Resume();

        main.QueueFree();
        await t.NextFrame();
        Session.Logout();
    }
}
