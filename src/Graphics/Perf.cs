using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FishingSim.Graphics;

/// <summary>
/// Tiny opt-in profiler (set FISHSIM_PERF=1): <c>using var _ = Perf.Scope("name");</c> records how long a block took.
/// Off by default, then a scope costs one bool check.
/// </summary>
public static class Perf
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("FISHSIM_PERF") == "1";
    private static readonly Dictionary<string, List<double>> Samples = new();

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
            if (!Samples.TryGetValue(_name, out var list))
                Samples[_name] = list = new List<double>();
            list.Add(ms);
        }
    }

    public static ScopeTimer Scope(string name) => new(Enabled ? name : null);

    public static string Report() => string.Join("\n", Samples.OrderByDescending(kv => kv.Value.Sum()).Select(kv =>
    {
        var v = kv.Value.OrderBy(x => x).ToList();
        return $"  {kv.Key,-22} n={v.Count,5} avg={v.Average():0.00}ms p95={v[(int)(v.Count * 0.95)]:0.00}ms max={v[^1]:0.00}ms";
    }));
}
