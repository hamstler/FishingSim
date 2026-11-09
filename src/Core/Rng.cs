using System;

namespace FishingSim.Core;

/// <summary>Source of randomness. Injected so simulations and tests can be deterministic.</summary>
public interface IRng
{
    /// <summary>A uniformly distributed value in [0, 1).</summary>
    float NextFloat();
}

public static class RngExtensions
{
    /// <summary>A uniformly distributed value in [min, max).</summary>
    public static float Range(this IRng rng, float min, float max) => min + rng.NextFloat() * (max - min);
}

/// <summary>Default <see cref="IRng"/> backed by <see cref="System.Random"/>; pass a seed for repeatable runs.</summary>
public sealed class SystemRng : IRng
{
    private readonly Random _random;

    public SystemRng(int? seed = null) => _random = seed is int s ? new Random(s) : new Random();

    public float NextFloat() => (float)_random.NextDouble();
}
