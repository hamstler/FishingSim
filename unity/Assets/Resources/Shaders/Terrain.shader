// The ground (shaders/terrain.gdshader of the Godot game, ported): seven photographed 2 m layers (meadow, forest
// floor, sand, path, lake bed, rock, snow; height in the albedo's alpha, the tilt in the normal map's RG) blended by
// the weights the mesh carries (UV = sand, path; UV2 = forest floor, lake bed; the meadow is the rest), rock on steep
// or high ground (triplanar), snow on the peaks; layers meet by height, two turned readings against tiling, the map's
// colours tint them, meadows get dry and lush patches; wet band and swash film at the water, the drift line, rain
// wetness, caustics on the bottom, and from afar a dark canopy over the forest floor and heath on the meadows.
Shader "FS/Terrain"
{
    Properties
    {
        _Noise ("Noise", 2D) = "gray" {}
        _Caustics ("Caustics", 2D) = "black" {}
        _Tile ("Tile (m)", Float) = 2
        _Bump ("Bump", Float) = 1
        _SnowLine ("Snow line", Float) = 26
        _RockLine ("Rock line", Float) = 10.5
        _DarkGrassTint ("Dark grass tint", Vector) = (1,1,1,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow nolightmap nodynlightmap nodirlightmap nometa exclude_path:deferred exclude_path:prepass
        #pragma target 3.5
        #pragma require 2darray
        #include "Waves.cginc"

        UNITY_DECLARE_TEX2DARRAY(_GroundAlb);
        UNITY_DECLARE_TEX2DARRAY(_GroundNrm);
        sampler2D _Noise, _Caustics;
        float4 _LayerTint[7];
        float _LayerRough[7];
        float4 _DarkGrassTint;
        float _Tile, _Bump, _Wetness, _SnowLine, _RockLine, _Daylight;

        struct Input
        {
            float3 wpos;
            float3 wN;
            float3 wT;
            float4 wts;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.wN = UnityObjectToWorldNormal(v.normal);
            o.wT = UnityObjectToWorldDir(v.tangent.xyz);
            o.wts = float4(v.texcoord.xy, v.texcoord1.xy);
        }

        float2 turn(float2 p, float a)
        {
            float c = cos(a), s = sin(a);
            return float2(c * p.x - s * p.y, s * p.x + c * p.y);
        }

        float4 SampleA(float2 uv, float L, float2 dx, float2 dy) { return _GroundAlb.SampleGrad(sampler_GroundAlb, float3(uv, L), dx, dy); }
        float4 SampleN(float2 uv, float L, float2 dx, float2 dy) { return _GroundNrm.SampleGrad(sampler_GroundNrm, float3(uv, L), dx, dy); }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 wpos = IN.wpos;
            float3 n = normalize(IN.wN);
            float macro = tex2D(_Noise, wpos.xz * 0.013).r;
            float mid = tex2D(_Noise, wpos.xz * 0.09 + float2(0.37, 0.11)).r;
            float fine = tex2D(_Noise, wpos.xz * 0.43 + float2(0.71, 0.29)).r;
            float slope = 1.0 - n.y;

            float4 g = saturate(IN.wts);
            float finer = tex2D(_Noise, wpos.xz * 1.7 + float2(0.13, 0.57)).r;
            float wander = fine * 0.55 + finer * 0.45 - 0.5;
            g += float4(wander, mid - 0.5 + (finer - 0.5) * 0.5, wander, wander) * g * (1.0 - g) * 4.5;
            g = saturate(g);
            float rock = max(smoothstep(0.30, 0.50, slope + (mid - 0.5) * 0.3),
                smoothstep(_RockLine, _RockLine + 5.5, wpos.y + (mid - 0.5) * 8.0) * smoothstep(0.35, 0.65, fine * 0.7 + mid * 0.5));
            float snow = smoothstep(_SnowLine - 1.0, _SnowLine + 5.0, wpos.y + (mid - 0.5) * 6.0) * (1.0 - smoothstep(0.3, 0.5, slope + (fine - 0.5) * 0.2));
            float w[7];
            w[0] = max(1.0 - g.x - g.y - g.z - g.w, 0.0);
            w[1] = g.z;
            w[2] = g.x;
            w[3] = g.y;
            w[4] = g.w;
            [unroll] for (int a = 0; a < 5; a++) w[a] *= (1.0 - rock) * (1.0 - snow);
            w[5] = rock * (1.0 - snow);
            w[6] = snow;

            float2 uvA = wpos.xz / _Tile;
            float2 uvB = turn(wpos.xz, 0.83) / (_Tile * 1.37) + float2(0.31, 0.67);
            float2 dAx = ddx(uvA), dAy = ddy(uvA), dBx = ddx(uvB), dBy = ddy(uvB);
            float mixB = smoothstep(0.4, 0.6, tex2D(_Noise, wpos.xz * 0.027 + float2(0.5, 0.2)).r);
            float3 tw = pow(abs(n), 4.0);
            tw /= (tw.x + tw.y + tw.z);
            float2 uvX = wpos.zy / _Tile, uvZ = wpos.xy / _Tile;
            float2 dXx = ddx(uvX), dXy = ddy(uvX), dZx = ddx(uvZ), dZy = ddy(uvZ);

            float4 alb[7];
            float s[7];
            float top = -1.0;
            [unroll] for (int i = 0; i < 7; i++)
            {
                alb[i] = 0;
                s[i] = -1.0;
                if (w[i] < 0.002) continue;
                float L = i;
                if (i == 5)
                {
                    if (tw.y > 0.01) alb[i] += SampleA(uvA, L, dAx, dAy) * tw.y;
                    if (tw.x > 0.01) alb[i] += SampleA(uvX, L, dXx, dXy) * tw.x;
                    if (tw.z > 0.01) alb[i] += SampleA(uvZ, L, dZx, dZy) * tw.z;
                }
                else
                {
                    if (mixB < 0.999) alb[i] += SampleA(uvA, L, dAx, dAy) * (1.0 - mixB);
                    if (mixB > 0.001) alb[i] += SampleA(uvB, L, dBx, dBy) * mixB;
                }
                s[i] = w[i] + alb[i].a * 0.45;
                top = max(top, s[i]);
            }

            float3 base = 0;
            float3 tilt = 0;
            float rough = 0, total = 0, hgt = 0;
            float3 grassTint = lerp(_LayerTint[0].rgb, _DarkGrassTint.rgb, smoothstep(2.0, 8.0, wpos.y));
            [unroll] for (int j = 0; j < 7; j++)
            {
                float b = max(s[j] - top + 0.16, 0.0);
                if (b <= 0.0) continue;
                float L = j;
                float4 nm = 0;
                float3 t3 = 0;
                if (j == 5)
                {
                    if (tw.y > 0.01) { float4 q = SampleN(uvA, L, dAx, dAy); t3 += float3(q.r * 2 - 1, 0, q.g * 2 - 1) * tw.y; }
                    if (tw.x > 0.01) { float4 q = SampleN(uvX, L, dXx, dXy); t3 += float3(0, q.g * 2 - 1, q.r * 2 - 1) * tw.x; }
                    if (tw.z > 0.01) { float4 q = SampleN(uvZ, L, dZx, dZy); t3 += float3(q.r * 2 - 1, q.g * 2 - 1, 0) * tw.z; }
                }
                else
                {
                    if (mixB < 0.999) nm += SampleN(uvA, L, dAx, dAy) * (1.0 - mixB);
                    if (mixB > 0.001)
                    {
                        float4 q = SampleN(uvB, L, dBx, dBy);
                        float2 tb = turn(q.rg * 2.0 - 1.0, -0.83) * 0.5 + 0.5;
                        nm += float4(tb, 0, 0) * mixB;
                    }
                    t3 = float3(nm.r * 2 - 1, 0, nm.g * 2 - 1);
                }
                float3 tint = j == 0 ? grassTint : _LayerTint[j].rgb;
                base += alb[j].rgb * tint * b;
                hgt += alb[j].a * b;
                tilt += t3 * b;
                rough += _LayerRough[j] * b;
                total += b;
            }
            total = max(total, 1e-4);
            base /= total;
            tilt /= total;
            rough /= total;
            hgt /= total;
            base *= lerp(0.8, 1.04, smoothstep(0.1, 0.75, hgt));

            float green = saturate(w[0] + w[1]);
            base *= lerp(0.86, 1.1, macro);
            base *= lerp(1.0, lerp(float3(1.06, 1.03, 0.86), float3(0.9, 1.0, 0.95), mid), green);
            float dry = smoothstep(0.52, 0.78, macro * 0.6 + fine * 0.25 + finer * 0.15) * w[0];
            float lush = smoothstep(0.55, 0.8, (1.0 - macro) * 0.55 + mid * 0.45) * w[0];
            base = lerp(base, base * float3(1.18, 1.02, 0.66), dry * 0.55);
            base = lerp(base, base * float3(0.78, 0.9, 0.74), lush * 0.5);
            float luma = dot(base, float3(0.299, 0.587, 0.114));
            base = lerp(base, luma, 0.22 * green);
            base *= lerp(1.0, float3(1.02, 0.95, 0.84), w[0]);

            float height = wpos.y;
            float wet = 1.0 - smoothstep(0.02, 0.45 + mid * 0.2, height);
            base *= lerp(1.0, 0.6, wet);
            base = lerp(base, base * 0.3, saturate(-height / 7.0));
            rough = lerp(rough, 0.35, wet * (1.0 - step(height, -0.05)));
            float film = 0.0;
            if (abs(height) < 1.2)
            {
                float swell = water_height(wpos.xz);
                float above = height - swell;
                film = (1.0 - smoothstep(0.0, 0.05 + _WaterAmp * 0.6, above)) * step(-0.02, above);
            }
            base *= 1.0 - 0.12 * film;
            rough = lerp(rough, 0.12, film);
            float driftAt = 0.16 + 0.05 * sin(wpos.x * 0.7 + mid * 6.0);
            float drift = smoothstep(0.035, 0.0, abs(height - driftAt)) * smoothstep(0.45, 0.65, finer) * (w[2] + w[4]);
            base = lerp(base, base * float3(0.42, 0.38, 0.32), saturate(drift) * 0.8);
            float rainWet = _Wetness * step(0.0, height) * (1.0 - snow);
            base *= 1.0 - 0.28 * rainWet;
            rough = lerp(rough, max(rough * 0.45, 0.2), rainWet);

            float3 pn = normalize(n + tilt * _Bump);
            // the world-space normal back into the mesh's tangent frame (the surface shader lights in that frame)
            float3 T = normalize(IN.wT - n * dot(IN.wT, n));
            float3 B = cross(n, T);
            o.Normal = normalize(float3(dot(pn, T), dot(pn, B), dot(pn, n)));

            if (height < -0.02)
            {
                float2 cuv = wpos.xz * 0.22;
                float c1 = tex2D(_Caustics, cuv + float2(_WaterTime * 0.03, _WaterTime * 0.02)).r;
                float c2 = tex2D(_Caustics, cuv * 1.3 - float2(_WaterTime * 0.025, _WaterTime * 0.035)).r;
                float caustic = smoothstep(0.55, 0.75, min(c1, c2)) * 0.8;
                o.Emission = float3(0.9, 1.0, 0.95) * caustic * exp(height * 0.6) * _Daylight * 0.35 * base;
            }

            float faraway = smoothstep(90.0, 280.0, distance(wpos, _WorldSpaceCameraPos));
            if (faraway > 0.0)
            {
                float cover = smoothstep(0.25, 0.65, macro * 0.55 + mid * 0.45);
                float3 canopy = lerp(float3(0.034, 0.055, 0.026), float3(0.07, 0.095, 0.04), mid * 0.7 + fine * 0.3);
                base = lerp(base, canopy, w[1] * faraway * lerp(0.65, 0.95, cover));
                float3 heath = lerp(float3(0.085, 0.095, 0.045), float3(0.15, 0.135, 0.075), macro * 0.6 + fine * 0.4);
                base = lerp(base, heath, w[0] * faraway * 0.6);
                float gully = smoothstep(0.55, 0.8, tex2D(_Noise, float2(wpos.x * 0.02, wpos.y * 0.004 + wpos.z * 0.02)).r);
                float3 stone = lerp(float3(0.16, 0.16, 0.15), float3(0.24, 0.235, 0.22), macro) * (1.0 - 0.45 * gully);
                base = lerp(base, stone, w[5] * faraway * 0.7);
                float scrub = smoothstep(0.5, 0.72, macro * 0.6 + fine * 0.4) * (1.0 - smoothstep(_RockLine + 4.0, _RockLine + 22.0, wpos.y));
                base = lerp(base, heath * 0.8, w[5] * faraway * scrub * 0.85);
                rough = lerp(rough, 0.95, faraway * (w[0] + w[1]));
            }

            o.Albedo = base;
            o.Smoothness = 1.0 - rough;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
