using System;
using System.Threading.Tasks;
using Godot;

namespace FishingSim.Tests;

/// <summary>Tiny assertion helper that prints a line per check and counts failures.</summary>
public sealed class TestContext
{
    public Node Host { get; }
    public int Passed { get; private set; }
    public int Failed { get; private set; }

    public TestContext(Node host) => Host = host;

    public void Check(bool condition, string label)
    {
        if (condition)
            Passed++;
        else
            Failed++;
        GD.Print($"{(condition ? "ok  " : "FAIL")}  {label}");
    }

    public void Near(float actual, float expected, float tolerance, string label) =>
        Check(MathF.Abs(actual - expected) <= tolerance, $"{label} (got {actual:0.###}, expected {expected:0.###})");

    public void Section(string title) => GD.Print($"--- {title} ---");

    /// <summary>Waits for one rendered frame of the host's scene tree.</summary>
    public async Task NextFrame() => await Host.ToSignal(Host.GetTree(), SceneTree.SignalName.ProcessFrame);

    public async Task Seconds(double seconds) =>
        await Host.ToSignal(Host.GetTree().CreateTimer(seconds), Godot.Timer.SignalName.Timeout);
}

public interface ITestSuite
{
    string Name { get; }
    Task Run(TestContext t);
}
