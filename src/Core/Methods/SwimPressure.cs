using System;
using System.Collections.Generic;

namespace FishingSim.Core.Methods;

/// <summary>
/// A swim fished hard (pure): every fish taken out of one place leaves fewer there and makes the rest wary, so the
/// bites at that spot slow down; a released fish warns the shoal too, though less. The swim recovers over a few game
/// hours as fish move back in. Moving a few dozen metres finds fresh fish.
/// </summary>
public sealed class SwimPressure
{
    /// <summary>How far (m) a catch is felt.</summary>
    public const float Radius = 15f;

    /// <summary>The time (game hours) over which a catch's weight fades by e (about 6 h).</summary>
    public const float RecoverHours = 6f;

    /// <summary>A released fish counts this much of a kept one.</summary>
    public const float Released = 0.5f;

    private readonly List<(float X, float Z, float Amount, float At)> _catches = new();

    /// <summary>A fish caught at (x, z) at this game hour (Profile.Clock); <paramref name="amount"/> 1 kept, 0.5 released.</summary>
    public void Add(float x, float z, float at, float amount = 1f)
    {
        _catches.Add((x, z, amount, at));
        // Old catches long forgotten are dropped.
        _catches.RemoveAll(c => at - c.At > RecoverHours * 5f);
    }

    /// <summary>Forget every catch (a new map).</summary>
    public void Clear() => _catches.Clear();

    /// <summary>How much fishing a spot has felt (number of recent nearby catches, faded by time and distance).</summary>
    public float Load(float x, float z, float now)
    {
        float load = 0f;
        foreach (var c in _catches)
        {
            float dx = c.X - x, dz = c.Z - z;
            float d = MathF.Sqrt(dx * dx + dz * dz);
            if (d >= Radius)
                continue;
            float near = 1f - d / Radius;
            load += c.Amount * near * near * MathF.Exp(-MathF.Max(0f, now - c.At) / RecoverHours);
        }
        return load;
    }

    /// <summary>The bite clock's speed at a spot (1 fresh .. 0.4 fished out).</summary>
    public static float Pace(float load) => MathF.Max(0.4f, 1f / (1f + 0.18f * MathF.Max(0f, load)));

    /// <summary>The swim is fished hard enough to tell the angler.</summary>
    public static bool FishedOut(float load) => Pace(load) < 0.7f;
}
