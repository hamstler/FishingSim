using System;
using FishingSim.Data;
using Godot;

namespace FishingSim.Core;

public enum FightOutcome { Ongoing, Landed, LineBroke, SpooledOut }

/// <summary>Per-fish constants of a fight, derived from the species and the individual's weight.</summary>
public readonly record struct FishProfile(
    float PeakPull,
    float SizeRatio,
    float Aggression,
    float StaminaDrain,
    float RunLengthMult,
    float BurstMult,
    float BurstRate)
{
    public static FishProfile From(FishSpecies sp, float weight) => new(
        PeakPull: sp.PullFactor * MathF.Pow(weight, 0.7f),
        SizeRatio: Math.Clamp(Mathf.InverseLerp(sp.MinWeight, sp.MaxWeight, weight), 0f, 1f),
        Aggression: sp.Aggression,
        StaminaDrain: sp.StaminaDrain,
        RunLengthMult: sp.RunLengthMult,
        BurstMult: sp.BurstMult,
        BurstRate: sp.BurstRate);
}

/// <summary>What the angler's equipment contributes to a fight.</summary>
/// <param name="DragMax">Drag limit (kg) at a 100% setting.</param>
/// <param name="EffectiveLimit">Weakest link (line or rod), in kg.</param>
/// <param name="RodShock">0..0.5, how much of a burst the rod absorbs.</param>
/// <param name="ReelSpeed">Retrieve speed in m/s.</param>
/// <param name="MaxLine">Metres of line on the spool.</param>
public readonly record struct FightGear(float DragMax, float EffectiveLimit, float RodShock, float ReelSpeed, float MaxLine);

/// <summary>
/// Simulation of one hooked fish (kg-force units). The fish pulls with a raw load; the drag lets line out only when
/// the load exceeds its setting; tension is the drag limit plus a quarter of the excess, so sudden bursts can overload
/// the line. Long fights wear any fish out. No engine types are used, so it is fully testable.
/// </summary>
public sealed class FightModel
{
    public const float LandDistance = 2f;
    public const float StressLimit = 1f;
    public const float InstantBreakRatio = 1.8f;

    private readonly FishProfile _fish;
    private readonly FightGear _gear;
    private readonly IRng _rng;
    private readonly float _homeAngle;
    private float _phaseTimer;
    private float _burstTimer;
    private float _burstLeft;
    private float _targetAngle;

    public float Tension { get; private set; }
    public float LineOut { get; private set; }
    public float Stamina { get; private set; } = 1f;
    public float FishAngle { get; private set; }
    public bool Running { get; private set; } = true;
    public float LineStress { get; private set; }
    public float Elapsed { get; private set; }
    public float PeakPull => _fish.PeakPull;

    /// <summary>Dev flag: the line never breaks.</summary>
    public bool Unbreakable { get; set; }

    public FightModel(FishProfile fish, FightGear gear, IRng rng, float startDistance, float startAngle, float dragSetting)
    {
        _fish = fish;
        _gear = gear;
        _rng = rng;
        LineOut = MathF.Max(startDistance, 4f);
        FishAngle = _targetAngle = _homeAngle = startAngle;
        _phaseTimer = rng.Range(1f, 2f);
        _burstTimer = NextBurstInterval();
        Tension = dragSetting * gear.DragMax;
    }

    /// <summary>Long fights wear any fish out: drain speeds up the longer it goes on (x2 at 45 s, x5 at 90 s).</summary>
    public float Fatigue => 1f + MathF.Pow(Elapsed / 45f, 2f);

    /// <summary>Small fish dart often, big fish surge less often but harder; species set the overall rate.</summary>
    private float NextBurstInterval() =>
        _rng.Range(0.8f, 2f) / (_fish.BurstRate * Mathf.Lerp(1.4f, 0.8f, _fish.SizeRatio));

    private void NextPhase()
    {
        float runChance = _fish.Aggression * (0.2f + 0.8f * Stamina);
        Running = _rng.NextFloat() < runChance;
        if (Running)
        {
            _phaseTimer = _rng.Range(1.5f, 3.5f) * _fish.RunLengthMult * Mathf.Lerp(0.7f, 1.3f, _fish.SizeRatio);
            _targetAngle = Math.Clamp(FishAngle + _rng.Range(-1f, 1f), _homeAngle - 1.3f, _homeAngle + 1.3f);
        }
        else
        {
            _phaseTimer = _rng.Range(1f, 2.5f) * Mathf.Lerp(1.2f, 0.8f, _fish.SizeRatio);
        }
    }

    /// <summary>
    /// Advances the fight by <paramref name="dt"/> seconds. <paramref name="lifting"/> = the rod is raised (pumping):
    /// the fish is drawn in faster and tires sooner, but the line carries more load.
    /// </summary>
    public FightOutcome Step(float dt, float dragSetting, bool reeling, bool lifting = false)
    {
        Elapsed += dt;

        // --- fish AI ---
        _phaseTimer -= dt;
        if (_phaseTimer <= 0f)
            NextPhase();
        float staminaFactor = 0.35f + 0.65f * Stamina;
        float pull;
        if (Running)
        {
            Stamina = MathF.Max(Stamina - _fish.StaminaDrain * Mathf.Lerp(1.3f, 0.85f, _fish.SizeRatio) * Fatigue * (lifting ? 1.35f : 1f) * dt, 0f);
            _burstTimer -= dt;
            if (_burstTimer <= 0f)
            {
                _burstTimer = NextBurstInterval();
                _burstLeft = Mathf.Lerp(0.2f, 0.45f, _fish.SizeRatio);
            }
            pull = _fish.PeakPull * staminaFactor * (1f + 0.15f * MathF.Sin(Elapsed * 7f));
            if (_burstLeft > 0f)
            {
                _burstLeft -= dt;
                pull *= _fish.BurstMult * Mathf.Lerp(1f, 1.25f, _fish.SizeRatio);
            }
        }
        else
        {
            Stamina = MathF.Min(Stamina + 0.03f * dt / Fatigue, 1f);
            pull = _fish.PeakPull * 0.25f * staminaFactor;
        }

        // --- load, drag, tension ---
        float raw = pull + (reeling ? 0.35f * _fish.PeakPull * staminaFactor : 0f) + (lifting ? 0.3f * _fish.PeakPull * staminaFactor : 0f);
        float dragLimit = dragSetting * _gear.DragMax;
        float excess = MathF.Max(raw - dragLimit, 0f);
        float target = excess == 0f ? raw : dragLimit + 0.25f * (1f - _gear.RodShock) * excess;
        float rate = target > Tension ? 30f : 12f;
        Tension = Mathf.MoveToward(Tension, target, rate * dt);

        // --- line in / out ---
        float payout = excess * 0.5f;   // m/s pulled off the spool by a slipping drag
        float reelIn = reeling ? _gear.ReelSpeed * Math.Clamp(1f - excess / 2f, 0f, 1f) : 0f;
        if (lifting)
            reelIn += 0.6f * Math.Clamp(1f - excess / 2f, 0f, 1f);   // pumping gains line
        LineOut += (payout - reelIn) * dt;

        // --- line stress ---
        float ratio = Tension / _gear.EffectiveLimit;
        if (ratio > 1f)
            LineStress += (ratio - 1f) * 4f * dt;
        else
            LineStress = MathF.Max(LineStress - 0.5f * dt, 0f);
        if (!Unbreakable && (LineStress >= StressLimit || ratio > InstantBreakRatio))
            return FightOutcome.LineBroke;

        FishAngle = Mathf.LerpAngle(FishAngle, _targetAngle, 1.5f * dt);

        if (LineOut >= _gear.MaxLine)
            return FightOutcome.SpooledOut;
        if (LineOut <= LandDistance)
            return FightOutcome.Landed;
        return FightOutcome.Ongoing;
    }
}
