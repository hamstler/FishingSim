// The lake's wind waves (shaders/water_waves.gdshaderinc of the Godot game, unchanged): Gerstner waves in a fan round
// the wind, 0..3 move the water mesh, 4..6 only shape the light. Game/Waves.cs mirrors it (the float rides these).
#ifndef FS_WAVES_INCLUDED
#define FS_WAVES_INCLUDED

float _WaterTime;
float _WaterAmp;
float2 _WaterDir;
float _WaterChop;

#define WAVES 7
#define VERTEX_WAVES 4

static const float4 WAVE[7] = {
    float4(0.0, 9.0, 0.40, 0.0),
    float4(0.49, 6.2, 0.26, 1.7),
    float4(-0.58, 4.4, 0.18, 4.1),
    float4(1.08, 3.1, 0.10, 2.6),
    float4(-0.26, 1.9, 0.05, 5.3),
    float4(0.79, 1.25, 0.035, 0.9),
    float4(-1.22, 0.8, 0.025, 3.7)
};

float2 wave_direction(int i)
{
    float a = WAVE[i].x;
    float2 w = normalize(_WaterDir + float2(1e-5, 0));
    return float2(w.x * cos(a) - w.y * sin(a), w.x * sin(a) + w.y * cos(a));
}

float3 gerstner(float2 p, int first, int last)
{
    float3 d = 0;
    [unroll] for (int i = 0; i < WAVES; i++)
    {
        if (i < first || i >= last) continue;
        float k = 6.2831853 / WAVE[i].y;
        float omega = sqrt(9.81 * k);
        float2 dir = wave_direction(i);
        float f = k * dot(dir, p) - omega * _WaterTime + WAVE[i].w;
        float a = _WaterAmp * WAVE[i].z;
        float q = _WaterChop * a;
        d.xz += dir * q * cos(f);
        d.y += a * sin(f);
    }
    return d;
}

float3 gerstner_slope(float2 p, int first, int last)
{
    float2 s = 0;
    float squeeze = 1.0;
    [unroll] for (int i = 0; i < WAVES; i++)
    {
        if (i < first || i >= last) continue;
        float k = 6.2831853 / WAVE[i].y;
        float omega = sqrt(9.81 * k);
        float2 dir = wave_direction(i);
        float f = k * dot(dir, p) - omega * _WaterTime + WAVE[i].w;
        float ka = k * _WaterAmp * WAVE[i].z;
        s += dir * ka * cos(f);
        squeeze -= _WaterChop * ka * sin(f);
    }
    return float3(s / max(squeeze, 0.2), squeeze);
}

float water_height(float2 p)
{
    float2 rest = p - gerstner(p, 0, VERTEX_WAVES).xz;
    return gerstner(rest, 0, VERTEX_WAVES).y;
}

#endif
