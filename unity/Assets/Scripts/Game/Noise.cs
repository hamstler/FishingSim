using System;

namespace FishingSim.Game;

/// <summary>
/// Smooth 2D simplex noise with five octaves of fractal sum, about -1..1, standing in for the Godot game's
/// FastNoiseLite (seed 1234, frequency 0.01). Deterministic: the same map is built every time.
/// </summary>
public sealed class Noise
{
    private readonly int[] _perm = new int[512];
    private readonly float _frequency;

    public Noise(int seed = 1234, float frequency = 0.01f)
    {
        _frequency = frequency;
        var p = new int[256];
        for (int i = 0; i < 256; i++)
            p[i] = i;
        var rng = new Random(seed);
        for (int i = 255; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++)
            _perm[i] = p[i & 255];
    }

    private static readonly float[] GradX = { 1, -1, 1, -1, 1, -1, 0, 0, 0.7071f, -0.7071f, 0.7071f, -0.7071f };
    private static readonly float[] GradY = { 0, 0, 1, -1, 0.7071f, 0.7071f, -0.7071f, -0.7071f, 1, 1, -1, -1 };

    private float Simplex(float x, float y)
    {
        const float f2 = 0.36602540378f, g2 = 0.2113248654f;
        float s = (x + y) * f2;
        int i = (int)MathF.Floor(x + s), j = (int)MathF.Floor(y + s);
        float t = (i + j) * g2;
        float x0 = x - (i - t), y0 = y - (j - t);
        int i1 = x0 > y0 ? 1 : 0, j1 = 1 - i1;
        float x1 = x0 - i1 + g2, y1 = y0 - j1 + g2;
        float x2 = x0 - 1f + 2f * g2, y2 = y0 - 1f + 2f * g2;
        int ii = i & 255, jj = j & 255;
        float n = 0f;
        n += Corner(x0, y0, _perm[ii + _perm[jj]]);
        n += Corner(x1, y1, _perm[ii + i1 + _perm[jj + j1]]);
        n += Corner(x2, y2, _perm[ii + 1 + _perm[jj + 1]]);
        return 70f * n;
    }

    private static float Corner(float x, float y, int h)
    {
        float t = 0.5f - x * x - y * y;
        if (t < 0f)
            return 0f;
        int g = h % 12;
        t *= t;
        return t * t * (GradX[g] * x + GradY[g] * y);
    }

    /// <summary>Fractal sum (5 octaves, lacunarity 2, gain 0.5), normalised to about -1..1.</summary>
    public float Get(float x, float y)
    {
        x *= _frequency;
        y *= _frequency;
        float sum = 0f, amp = 1f, norm = 0f;
        for (int o = 0; o < 5; o++)
        {
            sum += Simplex(x, y) * amp;
            norm += amp;
            amp *= 0.5f;
            x *= 2f;
            y *= 2f;
        }
        return sum / norm;
    }
}
