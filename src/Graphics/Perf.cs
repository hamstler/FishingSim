using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FishingSim.Graphics;

/// <summary>
/// Tiny opt-in profiler (set FISHSIM_PERF=1): <c>using var _ = Perf.Scope("name");</c> records how long a block took.
/// Off by default, then a scope costs one bool check. The in-game performance overlay (F11) turns on <see cref="Live"/>:
/// the scopes then only add up their time until the overlay takes the sums (no lists grow, nothing is allocated).
/// Scopes are timed on the main thread only.
/// </summary>
public static class Perf
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("FISHSIM_PERF") == "1";
    private static readonly Dictionary<string, List<double>> Samples = new();

    /// <summary>Set by the performance overlay while it is shown.</summary>
    public static bool Live { get; set; }

    private static readonly Dictionary<string, double> Window = new();

    /// <summary>Hands over the milliseconds each scope took since the last call (largest first) and starts again.</summary>
    public static void TakeWindow(List<(string Name, double Ms)> into)
    {
        into.Clear();
        foreach (var kv in Window)
            into.Add((kv.Key, kv.Value));
        into.Sort((a, b) => b.Ms.CompareTo(a.Ms));
        foreach (var (name, _) in into)
            Window[name] = 0.0;
    }

    public readonly struct ScopeTimer : IDisposable
    {
        private readonly string? _name;
        private readonly long _start;

        public ScopeTimer(string? name)
        {
            _name = name;
            _start = name == null ? 0 : Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (_name == null)
                return;
            double ms = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
            if (Live)
                Window[_name] = Window.GetValueOrDefault(_name) + ms;
            if (!Enabled)
                return;
            if (!Samples.TryGetValue(_name, out var list))
                Samples[_name] = list = new List<double>();
            list.Add(ms);
        }
    }

    public static ScopeTimer Scope(string name) => new(Enabled || Live ? name : null);

    public static string Report() => string.Join("\n", Samples.OrderByDescending(kv => kv.Value.Sum()).Select(kv =>
    {
        var v = kv.Value.OrderBy(x => x).ToList();
        return $"  {kv.Key,-22} n={v.Count,5} avg={v.Average():0.00}ms p95={v[(int)(v.Count * 0.95)]:0.00}ms max={v[^1]:0.00}ms";
    }));
}
