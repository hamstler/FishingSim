using System;
using Godot;

namespace FishingSim.Core.Physics;

/// <summary>
/// The fishing line as a rope of points between the rod tip and the end tackle (Verlet integration with one-sided
/// length constraints: it can go slack but not stretch). In the air it hangs under its own weight, held back by the air
/// and pushed by the wind; on the water a floating line (monofilament, braid) lies on the surface film and drifts with
/// the wind into a bow; under the water it settles slowly, a fluorocarbon sinking, the others hanging just under the
/// surface, and never through the bottom. Its length is the line paid out: when it is longer than the straight
/// distance the slack lies on the water; reeling shortens it, so the slack is taken up before the end moves (see
/// <see cref="Slack"/>). The endpoints are pinned to the tip and the end.
/// </summary>
public sealed class LineRope
{
    public const int Points = 40;
    private const int Iterations = 14;

    private readonly Vector3[] _p = new Vector3[Points];
    private readonly Vector3[] _prev = new Vector3[Points];
    private readonly float[] _bed = new float[Points];
    private bool _started;

    /// <summary>The rope's points from the tip (0) to the end (last).</summary>
    public ReadOnlySpan<Vector3> Positions => _p;

    /// <summary>The line paid out between the tip and the end (m).</summary>
    public float Length { get; set; } = 1f;

    /// <summary>The water's surface height.</summary>
    public float WaterY { get; set; }

    /// <summary>The height of the bottom at (x, z), or null for no bottom.</summary>
    public Func<float, float, float>? BottomY { get; set; }

    /// <summary>Settling speed under the water (m/s): positive sinks (fluorocarbon), zero hangs, negative rises to the surface.</summary>
    public float SinkSpeed { get; set; }

    /// <summary>The line floats on the surface film (monofilament, braid); a fluorocarbon breaks through it.</summary>
    public bool Floats { get; set; } = true;

    /// <summary>The wind's velocity (m/s, horizontal) pushing the line in the air and on the water.</summary>
    public Vector3 Wind { get; set; }

    /// <summary>How much line is not stretched between the ends: the paid-out length less the straight distance.</summary>
    public float Slack(Vector3 tip, Vector3 end) => MathF.Max(0f, Length - tip.DistanceTo(end));

    /// <summary>The line lies straight from the tip to the end.</summary>
    public void Reset(Vector3 tip, Vector3 end)
    {
        for (int i = 0; i < Points; i++)
            _p[i] = _prev[i] = tip.Lerp(end, i / (float)(Points - 1));
        _started = true;
    }

    /// <summary>One step of the line's motion with the ends where they are now.</summary>
    public void Step(float dt, Vector3 tip, Vector3 end)
    {
        if (!_started)
            Reset(tip, end);
        dt = Math.Clamp(dt, 1e-4f, 1f / 30f);
        float straight = tip.DistanceTo(end);
        // Taut (or nearly): the line runs straight between the ends.
        if (Length <= straight * 1.002f)
        {
            for (int i = 0; i < Points; i++)
            {
                var q = tip.Lerp(end, i / (float)(Points - 1));
                _prev[i] = _p[i] = q;
            }
            return;
        }
        float seg = Length / (Points - 1);
        var gravity = new Vector3(0f, -9.8f, 0f);
        // The bottom under each point, read once a step.
        for (int i = 1; i < Points - 1; i++)
            _bed[i] = BottomY != null ? BottomY(_p[i].X, _p[i].Z) + 0.01f : float.NegativeInfinity;
        for (int i = 1; i < Points - 1; i++)
        {
            var p = _p[i];
            var v = (p - _prev[i]) / dt;
            bool under = p.Y < WaterY - 0.005f;
            bool onSurface = !under && p.Y <= WaterY + 0.01f;
            if (under)
            {
                // In the water: heavy drag, slow settling towards the line's own sinking speed, a little drift.
                v = v * MathF.Exp(-6f * dt);
                v.Y += (-SinkSpeed - v.Y) * MathF.Min(1f, 4f * dt);
                v += new Vector3(Wind.X, 0f, Wind.Z) * 0.05f * dt;
            }
            else if (onSurface && Floats)
            {
                // On the surface film: it lies flat and drifts slowly downwind.
                v = v * MathF.Exp(-5f * dt);
                v.Y = MathF.Max(v.Y, 0f) * 0.5f;
                v += (new Vector3(Wind.X, 0f, Wind.Z) * 0.12f - new Vector3(v.X, 0f, v.Z)) * MathF.Min(1f, 1.5f * dt);
            }
            else
            {
                // In the air: it falls, the air holds it back, the wind pushes it.
                v += gravity * dt;
                v += (Wind - v) * MathF.Min(1f, 1.2f * dt);
            }
            _prev[i] = p;
            _p[i] = p + v * dt;
        }
        _p[0] = _prev[0] = tip;
        _p[Points - 1] = _prev[Points - 1] = end;
        for (int k = 0; k < Iterations; k++)
        {
            // Rope constraints: a segment can be shorter than its share of the line, never longer.
            for (int i = 0; i < Points - 1; i++)
            {
                var d = _p[i + 1] - _p[i];
                float len = d.Length();
                if (len <= seg || len < 1e-6f)
                    continue;
                var corr = d * ((len - seg) / len);
                if (i == 0)
                    _p[i + 1] -= corr;
                else if (i + 1 == Points - 1)
                    _p[i] += corr;
                else
                {
                    _p[i] += corr * 0.5f;
                    _p[i + 1] -= corr * 0.5f;
                }
            }
            for (int i = 1; i < Points - 1; i++)
                _p[i] = Contain(_p[i], _bed[i]);
        }
    }

    /// <summary>Keeps a point on or above the floating line's surface film and above the bottom.</summary>
    private Vector3 Contain(Vector3 p, float bed)
    {
        if (Floats && p.Y < WaterY && SinkSpeed <= 0f)
            p.Y = WaterY;
        if (p.Y < bed)
            p.Y = bed;
        return p;
    }

    /// <summary>The settling speed and floating of a line of this kind (Data.LineKind as an int: 0 mono, 1 braid, 2 fluoro).</summary>
    public static (float Sink, bool Floats) Behaviour(int kind) => kind switch
    {
        2 => (0.12f, false),      // fluorocarbon sinks
        1 => (-0.02f, true),      // a braid floats
        _ => (0f, true),          // a monofilament lies in the film, hangs where it is pulled under
    };
}
