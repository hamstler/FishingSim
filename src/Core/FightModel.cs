using System;
using FishingSim.Data;
using Godot;

namespace FishingSim.Core;

public enum FightOutcome { Ongoing, Landed, LineBroke, SpooledOut }

/// <summary>Per-fish constants of a fight, derived from the species and the individual's weight.</summary>
/// <param name="PeakPull">Strongest sustained pull (kg-force) of a fresh fish.</param>
/// <param name="BurstSpeed">Top speed (m/s) of a surge; a fish on a line rarely holds it for long.</param>
/// <param name="CruiseSpeed">Speed (m/s) at which it bores and swims between runs.</param>
/// <param name="TurnRate">How fast it can change direction (rad/s): big fish turn slowly.</param>
/// <param name="Agility">How quickly it reaches a new speed (m/s²).</param>
public readonly record struct FishProfile(
    float PeakPull,
    float SizeRatio,
    float Aggression,
    float StaminaDrain,
    float RunLengthMult,
    float BurstMult,
    float BurstRate,
    float BurstSpeed,
    float CruiseSpeed,
    float TurnRate,
    float Agility)
{
    /// <summary>
    /// Even small fish pull noticeably (the +0.25 kg term): a half-kilo roach loads a light rod by about a third,
    /// which is what makes the first, light tackle feel alive. Speeds follow body length: a fish surges at a few
    /// body lengths per second, capped by its build (pike, asp and salmon are the sprinters).
    /// </summary>
    public static FishProfile From(FishSpecies sp, float weight)
    {
        float length = sp.LengthRef * MathF.Pow(MathF.Max(weight, 0.005f), 0.33f);
        float burst = Math.Clamp(length * (3f + 3f * sp.Aggression), 0.5f, 1.6f + 1.6f * sp.Aggression);
        return new FishProfile(
            PeakPull: sp.PullFactor * (MathF.Pow(weight, 0.7f) + 0.25f),
            SizeRatio: Math.Clamp(Mathf.InverseLerp(sp.MinWeight, sp.MaxWeight, weight), 0f, 1f),
            Aggression: sp.Aggression,
            StaminaDrain: sp.StaminaDrain,
            RunLengthMult: sp.RunLengthMult,
            BurstMult: sp.BurstMult,
            BurstRate: sp.BurstRate,
            BurstSpeed: burst,
            CruiseSpeed: burst * 0.35f,
            TurnRate: Math.Clamp(1.1f / MathF.Sqrt(MathF.Max(length, 0.05f)), 0.7f, 3.5f),
            Agility: Math.Clamp(2.2f / MathF.Sqrt(MathF.Max(length, 0.05f)), 1.2f, 6f));
    }
}

/// <summary>What the angler's equipment contributes to a fight.</summary>
/// <param name="DragMax">Drag limit (kg) at a 100% setting.</param>
/// <param name="EffectiveLimit">Weakest link (line or rod), in kg.</param>
/// <param name="RodShock">0..0.5, how much of a burst the rod absorbs.</param>
/// <param name="ReelSpeed">Retrieve speed in m/s.</param>
/// <param name="MaxLine">Metres of line on the spool.</param>
public readonly record struct FightGear(float DragMax, float EffectiveLimit, float RodShock, float ReelSpeed, float MaxLine);

/// <summary>
/// Simulation of one hooked fish (kg-force units, metres, seconds) on the water plane, with the angler at the origin.
/// The fish has a position, a heading and a speed limited by its size and build: it turns at a finite rate, speeds
/// up and slows down gradually, and swims in runs (away from the angler, angled to one side, with surges) and in
/// stubborn rests (boring down, shaking its head, still pulling). The line holds it at the length paid out: when its
/// pull (plus the angler's cranking) exceeds the drag setting the spool gives line and the fish moves out at a speed
/// set by how much it out-pulls the drag; otherwise the angler gains line, slowly while the fish still pulls, quickly
/// once it is tired. Sideways the fish swings on the line like a pendulum. Tension is the drag limit plus a quarter
/// of any excess, so sudden surges can overload the line. Long fights wear any fish out. No engine types are used.
/// </summary>
public sealed class FightModel
{
    public const float LandDistance = 2f;
    public const float StressLimit = 1f;
    public const float InstantBreakRatio = 1.8f;

    /// <summary>Seconds after which the fish tires twice as fast; long fights still end, but not in seconds.</summary>
    public const float FatigueSeconds = 90f;

    /// <summary>Overall pace of tiring: lower = longer, harder fights.</summary>
    public const float StaminaPace = 0.15f;

    private readonly FishProfile _fish;
    private readonly FightGear _gear;
    private readonly IRng _rng;
    private float _phaseTimer;
    private float _burstTimer;
    private float _burstLeft;
    private float _side;            // which way the current run angles: -1 left, +1 right
    private float _runOffset;       // angle between "straight away" and the run's course
    private Vector2 _velocity;

    public float Tension { get; private set; }

    /// <summary>Line between the rod tip and the fish (metres paid out).</summary>
    public float LineOut { get; private set; }

    public float Stamina { get; private set; } = 1f;
    public bool Running { get; private set; } = true;
    public float LineStress { get; private set; }

    /// <summary>The drag is giving line this step (the fish pulls harder than the drag setting).</summary>
    public bool Slipping { get; private set; }
    public float Elapsed { get; private set; }
    public float PeakPull => _fish.PeakPull;

    /// <summary>Fish position on the water relative to the angler (x, z); the angler looks along +z at 0 angle.</summary>
    public Vector2 Position { get; private set; }

    /// <summary>Direction the fish swims (radians, same convention as <see cref="FishAngle"/>).</summary>
    public float Heading { get; private set; }

    /// <summary>How hard the fish is swimming through the water (m/s), whatever the line lets it achieve.</summary>
    public float Speed => _velocity.Length();

    /// <summary>The fish's actual movement over the ground this step (m/s): what the line allows of its swimming.</summary>
    public Vector2 Motion { get; private set; }

    /// <summary>Bearing of the fish from the angler (radians: x = sin, z = cos).</summary>
    public float FishAngle => MathF.Atan2(Position.X, Position.Y);

    public float Distance => Position.Length();

    /// <summary>Dev flag: the line never breaks.</summary>
    public bool Unbreakable { get; set; }

    /// <summary>True on the step a surge begins (the game may show a jump, a swirl or a rod kick).</summary>
    public bool BurstStarted { get; private set; }

    /// <summary>A surge is under way (the fish shakes its head, thrashes or jumps).</summary>
    public bool Bursting => _burstLeft > 0f;

    public FightModel(FishProfile fish, FightGear gear, IRng rng, float startDistance, float startAngle, float dragSetting)
    {
        _fish = fish;
        _gear = gear;
        _rng = rng;
        LineOut = MathF.Max(startDistance, 4f);
        Position = new Vector2(MathF.Sin(startAngle), MathF.Cos(startAngle)) * LineOut;
        // The strike: a hooked fish bolts at once, away and to one side, with a hard first surge.
        _side = rng.NextFloat() < 0.5f ? -1f : 1f;
        _runOffset = rng.Range(0.2f, 0.8f);
        Heading = startAngle + _side * _runOffset;
        _velocity = new Vector2(MathF.Sin(Heading), MathF.Cos(Heading)) * fish.CruiseSpeed;
        Running = true;
        _phaseTimer = rng.Range(2.5f, 4.5f) * fish.RunLengthMult;
        _burstLeft = Mathf.Lerp(0.5f, 0.8f, fish.SizeRatio);
        BurstStarted = true;
        _burstTimer = NextBurstInterval();
        Tension = MathF.Min(dragSetting * gear.DragMax, fish.PeakPull);
    }

    public float Fatigue => 1f + MathF.Pow(Elapsed / FatigueSeconds, 2f);

    /// <summary>Small fish dart often, big fish surge less often but harder; species set the overall rate.</summary>
    private float NextBurstInterval() =>
        _rng.Range(0.8f, 2f) / (_fish.BurstRate * Mathf.Lerp(1.4f, 0.8f, _fish.SizeRatio));

    private void NextPhase()
    {
        float runChance = _fish.Aggression * (0.2f + 0.8f * Stamina) * 0.8f;
        Running = _rng.NextFloat() < runChance;
        if (Running)
        {
            _phaseTimer = _rng.Range(1.5f, 3.5f) * _fish.RunLengthMult * Mathf.Lerp(0.7f, 1.3f, _fish.SizeRatio);
            // A new run often swaps sides; it heads away from the angler, angled 10-60 degrees off the line.
            if (_rng.NextFloat() < 0.55f)
                _side = -_side;
            _runOffset = _rng.Range(0.15f, 1.05f);
        }
        else
        {
            _phaseTimer = _rng.Range(1f, 2.5f) * Mathf.Lerp(1.2f, 0.8f, _fish.SizeRatio);
            _runOffset = _rng.Range(1.1f, 1.7f);       // between runs it bores across the line, still pulling
        }
    }

    /// <summary>
    /// Advances the fight by <paramref name="dt"/> seconds. <paramref name="lifting"/> = the rod is raised (pumping):
    /// the fish is drawn in faster and tires sooner, but the line carries more load.
    /// </summary>
    public FightOutcome Step(float dt, float dragSetting, bool reeling, bool lifting = false)
    {
        Elapsed += dt;
        if (Elapsed > dt * 1.5f)
            BurstStarted = false;

        // --- fish AI: phase, surges, stamina, how hard it swims ---
        _phaseTimer -= dt;
        if (_phaseTimer <= 0f)
            NextPhase();
        float staminaFactor = 0.35f + 0.65f * Stamina;
        // Fighting the drag is what tires a fish: the harder the line holds it, the faster it spends itself.
        float load = 0.35f + 0.65f * Math.Clamp(Tension / MathF.Max(_fish.PeakPull, 0.01f), 0f, 1.5f);
        float effort;          // 0..~2: fraction of the fish's peak pull it puts in right now
        if (Running)
        {
            Stamina = MathF.Max(Stamina - _fish.StaminaDrain * StaminaPace * load * Mathf.Lerp(1.3f, 0.85f, _fish.SizeRatio) * Fatigue * (lifting ? 1.35f : 1f) * dt, 0f);
            _burstTimer -= dt;
            if (_burstTimer <= 0f)
            {
                _burstTimer = NextBurstInterval();
                _burstLeft = Mathf.Lerp(0.2f, 0.45f, _fish.SizeRatio);
                BurstStarted = true;
            }
            effort = staminaFactor * (1f + 0.15f * MathF.Sin(Elapsed * 7f));
        }
        else
        {
            // Between runs the fish does not give up: it bores down and shakes its head, and early in the fight it
            // even gets a little of its breath back.
            float recover = 0.012f * MathF.Max(1f - Elapsed / (2f * FatigueSeconds), 0f);
            Stamina = Math.Clamp(Stamina - _fish.StaminaDrain * StaminaPace * 0.25f * load * Fatigue * dt + recover * dt, 0f, 1f);
            float shake = MathF.Max(0f, MathF.Sin(Elapsed * (5f + 3f * _fish.Aggression))) * 0.35f * _fish.Aggression;
            effort = (0.38f + shake) * staminaFactor;
        }
        bool surging = _burstLeft > 0f;
        if (surging)
        {
            _burstLeft -= dt;
            effort *= _fish.BurstMult * Mathf.Lerp(1f, 1.25f, _fish.SizeRatio);
        }

        // --- steering: turn towards the course at a limited rate ---
        float bearing = FishAngle;
        float course = bearing + _side * _runOffset;
        if (Stamina < 0.12f)
            course = bearing + _side * 1.4f;                         // spent: it only rolls along the line
        float turn = Mathf.Wrap(course - Heading, -MathF.PI, MathF.PI);
        float maxTurn = _fish.TurnRate * (surging ? 1.4f : 1f) * dt;
        Heading += Math.Clamp(turn, -maxTurn, maxTurn);
        float wantSpeed = Running || surging
            ? Mathf.Lerp(_fish.CruiseSpeed, _fish.BurstSpeed, surging ? 1f : 0.55f) * staminaFactor
            : _fish.CruiseSpeed * 0.7f * staminaFactor;
        var dir = new Vector2(MathF.Sin(Heading), MathF.Cos(Heading));
        var desired = dir * wantSpeed;
        _velocity = MoveToward(_velocity, desired, _fish.Agility * dt);

        // --- the pull on the line: the part of the fish's effort directed away from the angler ---
        var radial = Position.LengthSquared() > 1e-6f ? Position.Normalized() : new Vector2(0f, 1f);
        float away = MathF.Max(dir.Dot(radial), 0f);
        float fishPull = _fish.PeakPull * effort * (0.35f + 0.65f * away);
        float raw = fishPull + (reeling ? 0.35f * _fish.PeakPull * staminaFactor : 0f) + (lifting ? 0.3f * _fish.PeakPull * staminaFactor : 0f);

        float dragLimit = dragSetting * _gear.DragMax;
        float slack = LineOut - Distance;
        float excess;
        if (slack > 0.3f)
        {
            // Loose line: no load beyond the line's own drag; the fish swims free and cranking takes up the slack.
            excess = 0f;
            Tension = Mathf.MoveToward(Tension, 0.05f * _fish.PeakPull, 12f * dt);
            Slipping = false;
            Position += _velocity * dt;
            if (reeling)
                LineOut = MathF.Max(LineOut - _gear.ReelSpeed * dt, Distance);
        }
        else
        {
            excess = MathF.Max(raw - dragLimit, 0f);
            float target = excess == 0f ? raw : dragLimit + 0.25f * (1f - _gear.RodShock) * excess;
            float rate = target > Tension ? 30f : 12f;
            Tension = Mathf.MoveToward(Tension, target, rate * dt);
            Slipping = excess > 0.05f;

            // Along the line: out when it beats the drag (speed grows with how much it beats it), in when the angler
            // winds or pumps — slowly while the fish is still pulling, briskly once it lets go. A fish swimming
            // towards the angler simply comes closer and leaves slack that has to be wound in.
            float radialSpeed = _velocity.Dot(radial);
            float outward = Slipping ? MathF.Max(radialSpeed, 0.15f) * MathF.Sqrt(Math.Clamp(excess / MathF.Max(raw, 0.01f), 0f, 1f)) : 0f;
            float give = 1f - Math.Clamp(fishPull / MathF.Max(dragLimit, 0.05f), 0f, 0.9f);
            float resistance = 1f - 0.8f * staminaFactor * Mathf.Lerp(0.6f, 1f, _fish.SizeRatio);
            float reelIn = reeling && !Slipping ? _gear.ReelSpeed * resistance * give : 0f;
            if (lifting && !Slipping)
                reelIn += 0.6f * give;                                // pumping gains line
            LineOut = MathF.Max(LineOut + (outward - reelIn) * dt, 0f);
            float inward = MathF.Min(radialSpeed, 0f);
            float newDistance = MathF.Max(MathF.Min(Distance + (outward + inward) * dt, LineOut), 0.5f);

            // Across the line the fish swings freely (a pendulum on the taut line).
            var across = new Vector2(radial.Y, -radial.X);
            float sideways = _velocity.Dot(across) * 0.85f;
            var p = Position + across * sideways * dt;
            var before = Position;
            Position = (p.LengthSquared() > 1e-6f ? p.Normalized() : radial) * newDistance;
            Motion = (Position - before) / MathF.Max(dt, 1e-4f);
        }
        if (slack > 0.3f)
            Motion = _velocity;

        // --- line stress ---
        float ratio = Tension / _gear.EffectiveLimit;
        if (ratio > 1f)
            LineStress += (ratio - 1f) * 4f * dt;
        else
            LineStress = MathF.Max(LineStress - 0.5f * dt, 0f);
        if (!Unbreakable && (LineStress >= StressLimit || ratio > InstantBreakRatio))
            return FightOutcome.LineBroke;

        if (LineOut >= _gear.MaxLine)
            return FightOutcome.SpooledOut;
        if (LineOut <= LandDistance)
            return FightOutcome.Landed;
        return FightOutcome.Ongoing;
    }

    private static Vector2 MoveToward(Vector2 from, Vector2 to, float delta)
    {
        var d = to - from;
        float len = d.Length();
        return len <= delta || len < 1e-6f ? to : from + d / len * delta;
    }
}
