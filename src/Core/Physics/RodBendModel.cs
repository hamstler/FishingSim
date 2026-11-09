using System;

namespace FishingSim.Core.Physics;

/// <summary>
/// The blank of a fishing rod as a chain of springy joints bending in one plane (2D: X along the unloaded rod,
/// Y towards the line). The line pulls the tip with the tension (kg-force) in a given direction; every joint bends
/// by the moment of that pull divided by its stiffness. Stiffness falls from the butt to the tip according to the
/// rod's action (fast = only the tip bends, slow/parabolic = the whole blank works) and grows with the rod's power.
/// Joints are spring-damper systems, so the rod lags, overshoots and vibrates (bites, casts, a fish's lunges).
/// Pure maths, used by <c>Fishing/RodRig</c>.
/// </summary>
public sealed class RodBendModel
{
    public const int DefaultJoints = 10;
    private const float Gravity = 9.81f;

    private readonly float[] _angle;
    private readonly float[] _velocity;
    private readonly float[] _stiffness;
    private readonly float[] _rest;       // equilibrium shape for the current load (solved each step, warm-started)
    private readonly float _segment;

    public int Joints => _angle.Length;

    /// <summary>Length (m) of the bending blank.</summary>
    public float Length { get; }

    /// <summary>Relative bend of each joint (radians, positive = towards the line).</summary>
    public ReadOnlySpan<float> Angles => _angle;

    /// <summary>Natural frequency (rad/s) of the blank's sway and its damping ratio.</summary>
    public float Omega { get; set; } = 26f;
    public float Damping { get; set; } = 0.22f;

    /// <param name="power">Load (kg) at which the rod is fully bent, around its rated maximum.</param>
    /// <param name="action">0 = fast (tip action), 1 = slow (parabolic).</param>
    public RodBendModel(float length, float power, float action, int joints = DefaultJoints)
    {
        Length = length;
        _segment = length / joints;
        _angle = new float[joints];
        _velocity = new float[joints];
        _stiffness = new float[joints];
        _rest = new float[joints];
        float a = Math.Clamp(action, 0f, 1f);
        for (int i = 0; i < joints; i++)
        {
            float t = (float)i / (joints - 1);                       // 0 butt, 1 tip
            // Taper: a fast rod's butt is very stiff and its tip soft; a slow rod is more even.
            float floor = Lerp(0.05f, 0.4f, a);
            float taper = floor + (1f - floor) * MathF.Pow(1f - t, Lerp(3.5f, 0.6f, a));
            // Scaled so that the rated load bends the blank to roughly a right angle in total.
            _stiffness[i] = MathF.Max(power, 0.3f) * Gravity * length * 1.6f * taper;
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Tip position in the bend plane for the current angles (base at the origin, unloaded rod along +X).</summary>
    public (float X, float Y) Tip()
    {
        float x = 0f, y = 0f, phi = 0f;
        for (int i = 0; i < _angle.Length; i++)
        {
            phi += _angle[i];
            x += MathF.Cos(phi) * _segment;
            y += MathF.Sin(phi) * _segment;
        }
        return (x, y);
    }

    /// <summary>Total bend of the blank (radians between butt and tip direction).</summary>
    public float TotalBend()
    {
        float s = 0f;
        foreach (var a in _angle)
            s += a;
        return s;
    }

    /// <summary>Adds a sudden kick to the blank (a bite's twitch, the snap of a cast); positive = towards the line.</summary>
    public void Impulse(float strength)
    {
        for (int i = 0; i < _angle.Length; i++)
            _velocity[i] += strength * (i + 1f) / _angle.Length;
    }

    /// <summary>
    /// Advances the blank. <paramref name="tension"/> is the line pull in kg-force; (<paramref name="dirX"/>,
    /// <paramref name="dirY"/>) its direction in the bend plane (X along the unloaded rod, Y towards the line).
    /// </summary>
    public void Step(float dt, float tension, float dirX, float dirY)
    {
        if (dt <= 0f)
            return;
        float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (len < 1e-5f)
        {
            dirX = 0f;
            dirY = 1f;
        }
        else
        {
            dirX /= len;
            dirY /= len;
        }
        float fx = MathF.Max(tension, 0f) * Gravity * dirX;
        float fy = MathF.Max(tension, 0f) * Gravity * dirY;

        // 1) Equilibrium: relax the shape until every joint's bend balances the line's moment about it. Solving it
        //    on its own (rather than from the swinging shape) keeps the blank stable under any load.
        Span<float> px = stackalloc float[_angle.Length + 1];
        Span<float> py = stackalloc float[_angle.Length + 1];
        for (int iter = 0; iter < 24; iter++)
        {
            float phi = 0f;
            for (int i = 0; i < _rest.Length; i++)
            {
                phi += _rest[i];
                px[i + 1] = px[i] + MathF.Cos(phi) * _segment;
                py[i + 1] = py[i] + MathF.Sin(phi) * _segment;
            }
            float tipX = px[_rest.Length], tipY = py[_rest.Length];
            for (int i = 0; i < _rest.Length; i++)
            {
                float rx = tipX - px[i], ry = tipY - py[i];
                float moment = rx * fy - ry * fx;
                float target = Math.Clamp(moment / _stiffness[i], -0.5f, 0.5f);
                _rest[i] += (target - _rest[i]) * 0.12f;   // small steps: all joints move at once, larger ones overshoot
            }
        }

        // 2) Dynamics: each joint swings towards its equilibrium bend like a damped spring (lag, overshoot, ringing).
        int sub = Math.Clamp((int)MathF.Ceiling(dt / 0.004f), 1, 16);
        float h = dt / sub;
        for (int i = 0; i < _angle.Length; i++)
        {
            for (int k = 0; k < sub; k++)
            {
                float acc = (_rest[i] - _angle[i]) * Omega * Omega - 2f * Damping * Omega * _velocity[i];
                _velocity[i] += acc * h;
                _angle[i] += _velocity[i] * h;
            }
            _angle[i] = Math.Clamp(_angle[i], -0.7f, 0.7f);
        }
    }
}

