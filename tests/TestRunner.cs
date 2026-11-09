using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace FishingSim.Tests;

/// <summary>
/// Runs the test suites headless:
///   godot --headless --path . res://tests/run_tests.tscn [-- suite1 suite2]
/// Exit code is the number of failed checks (capped at 255).
/// </summary>
public partial class TestRunner : Node
{
    private static IEnumerable<ITestSuite> AllSuites() => new ITestSuite[]
    {
        new CoreSuite(),
        new AccountsSuite(),
        new ProgressSuite(),
        new WorldSuite(),
        new GameSuite(),
        new TournamentSuite(),
        new VillageSuite(),
        new GearSuite(),
    };

    public override async void _Ready()
    {
        var wanted = OS.GetCmdlineUserArgs().Select(a => a.ToLowerInvariant()).ToHashSet();
        var ctx = new TestContext(this);
        await ctx.NextFrame();   // the root is still busy setting up children during _Ready
        foreach (var suite in AllSuites())
        {
            if (wanted.Count > 0 && !wanted.Contains(suite.Name.ToLowerInvariant()))
                continue;
            GD.Print($"\n=== {suite.Name} ===");
            try
            {
                await suite.Run(ctx);
            }
            catch (Exception e)
            {
                ctx.Check(false, $"suite {suite.Name} threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
        }
        GD.Print($"\nRESULT: {ctx.Passed} passed, {ctx.Failed} failed");
        GetTree().Quit(Math.Min(ctx.Failed, 255));
    }
}
