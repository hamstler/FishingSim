using System;

namespace FishingSim.Core.Methods;

/// <summary>
/// How disturbed the fish at the swim are (pure): a heavy splash, steps on the bank and wading near the bait, and a
/// fish fought there put them off for a while; the disturbance fades with a half-life of <see cref="HalfLife"/>
/// seconds. While it lasts the bites come slower (<see cref="Pace"/>). Shallow water carries every noise further.
/// </summary>
public sealed class Disturbance
{
    public const float HalfLife = 35f;
    public const float MaxLevel = 1.5f;

    /// <summary>0 = calm water .. 1 and above = the fish have fled.</summary>
    public float Level { get; private set; }

    public void Add(float amount) => Level = Math.Clamp(Level + MathF.Max(amount, 0f), 0f, MaxLevel);

    public void Decay(float dt) => Level *= MathF.Exp(-MathF.Max(dt, 0f) * 0.6931f / HalfLife);

    public void Clear() => Level = 0f;

    /// <summary>The bite clock's speed (1 = undisturbed, 0.25 at worst).</summary>
    public float Pace => 1f - 0.75f * Math.Clamp(Level, 0f, 1f);

    /// <summary>Disturbed enough to tell the angler.</summary>
    public bool Spooked => Level > 0.45f;

    /// <summary>
    /// The splash of the end tackle landing: a float hardly matters, a full feeder crashes in, a lure by its weight
    /// (a fly 0.1, a 25 g spoon 0.6); over shallow water up to 1.7 times as much, over deep water half.
    /// </summary>
    public static float Splash(FishingMethod method, float weightG, float depth)
    {
        float loud = method switch
        {
            FishingMethod.Bottom => 0.45f,
            FishingMethod.Spinning => 0.08f + MathF.Max(weightG, 0f) / 50f,
            _ => 0.12f,
        };
        return loud * Math.Clamp(1.7f - depth / 2.5f, 0.5f, 1.7f);
    }

    /// <summary>
    /// What the angler's steps add in <paramref name="dt"/> seconds: nothing standing still or beyond 14 m of the
    /// bait, little walking, a lot running, and two and a half times as much wading in the water.
    /// </summary>
    public static float Steps(float speed, float distanceToBait, bool wading, float dt)
    {
        if (speed < 0.5f)
            return 0f;
        float near = Math.Clamp(1f - distanceToBait / 14f, 0f, 1f);
        return (speed > 4f ? 0.22f : 0.04f) * (wading ? 2.5f : 1f) * near * dt;
    }
}
