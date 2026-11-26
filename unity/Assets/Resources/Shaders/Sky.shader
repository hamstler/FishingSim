// The Godot game's sky (shaders/sky.gdshader): Rayleigh + Mie scattering (adapted from Godot's MIT-licensed
// PhysicalSkyMaterial) with the dusk blue, the twilight glow, overcast greying, a drifting cumulus sheet lit through
// three steps towards the sun with a silver lining, a touch of the painted sky (stylize 0.35), and stars.gdshader's
// stars behind the clouds at night.
Shader "FS/Sky"
{
    Properties
    {
        _CloudNoise ("Cloud noise", 2D) = "gray" {}
        _CloudCover ("Cloud cover", Range(0,1)) = 0.3
        _Night ("Night", Range(0,1)) = 0
        _Stars ("Stars", Range(0,1)) = 0
        _Twilight ("Twilight", Range(0,1)) = 0
        _SunFlat ("Sun azimuth", Vector) = (0,0,-1,0)
        _Turbidity ("Turbidity", Float) = 4
        _SunDir ("Sun direction", Vector) = (0,0.5,0.8,0)
        _SunColor ("Sun colour", Color) = (1,0.97,0.92,1)
        _SunEnergy ("Sun energy", Float) = 1.9
        _SunOn ("Sun in the sky", Float) = 1
        _Stylize ("Stylize", Float) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            sampler2D _CloudNoise;
            float _CloudCover, _Night, _Stars, _Twilight, _Turbidity, _SunEnergy, _SunOn, _Stylize, _CloudDrift;
            float _SkyFogBlend, _SkyFogHeight;   // DayCycle: how much and how high the horizon melts into the fog's colour
            float4 _SunFlat, _SunDir, _SunColor;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o; }

            static const float3 UP = float3(0, 1, 0);
            static const float SUN_ENERGY = 1000.0, RAYLEIGH_ZENITH_SIZE = 8.4e3, MIE_ZENITH_SIZE = 1.25e3, PI_ = 3.14159265;
            static const float rayleigh = 2.0, mie = 0.004, mie_eccentricity = 0.8, saturation = 1.7, exposure = 0.1;
            static const float3 rayleigh_color = float3(0.18, 0.4, 0.85);
            static const float3 mie_color = float3(0.69, 0.729, 0.812);
            static const float3 ground_color = float3(0.1, 0.07, 0.034);
            static const float3 night_color = float3(0.004, 0.006, 0.014);

            float hg(float c, float g) { return 0.0795774715459 * (1.0 - g * g) / pow(1.0 + g * g - 2.0 * g * c, 1.5); }
            // ProcTextures.CloudNoise has 8 base cycles per tile, read at half scale: about Godot's 4 per sheet unit; it is
            // Perlin fBm with Godot's mean and spread already (a clear sky about 14 % cloud).
            // Read with mipmaps like Godot's texture(): the cloud sheet's screen derivatives (static, taken once before the
            // branch) scaled by each octave's factor k. Reading the top level only (tex2Dlod 0) aliased towards the
            // horizon and in the fine octaves into jagged, zigzag cloud edges.
            static float2 _uvDx, _uvDy;
            float N(float2 uv, float k)
            {
                return tex2Dgrad(_CloudNoise, uv * 0.5, _uvDx * (0.5 * k), _uvDy * (0.5 * k)).r;
            }
            float cloud_shape(float2 uv) { return N(uv, 1.0) * 0.55 + N(uv * 2.03 + float2(0.31, 0.17), 2.03) * 0.28 + N(uv * 4.11 + float2(0.71, 0.53), 4.11) * 0.17; }
            float clouds(float2 uv, bool detailed)
            {
                float threshold = lerp(0.72, 0.18, _CloudCover);
                float d = smoothstep(threshold, threshold + 0.24, cloud_shape(uv));
                if (detailed)
                {
                    float billow = N(uv * 8.7 + float2(0.11, 0.83), 8.7) * 0.6 + N(uv * 17.3 + float2(0.47, 0.29), 17.3) * 0.4;
                    d = saturate(d - (1.0 - d) * (billow - 0.25) * 1.1);
                }
                return d;
            }
            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }

            float4 frag(v2f i) : SV_Target
            {
                float3 eye = normalize(i.dir);
                float3 sun_dir = normalize(_SunDir.xyz);
                float cos_theta = dot(eye, sun_dir);
                float3 color = 0;
                float sun_up = 0;
                float3 lightCol = _SunColor.rgb;
                if (_SunOn > 0.5)
                {
                    float zenith_angle = clamp(dot(UP, sun_dir), -1.0, 1.0);
                    float sun_energy = max(0.0, 1.0 - exp(-((PI_ * 0.5) - acos(zenith_angle)))) * SUN_ENERGY * max(_SunEnergy, 0.05);
                    float sun_fade = 1.0 - saturate(1.0 - exp(sun_dir.y));
                    sun_up = saturate(sun_dir.y * 4.0 + 0.3);
                    float rc = rayleigh - (1.0 - sun_fade);
                    float3 rb = rc * rayleigh_color * 0.0001;
                    float3 mb = _Turbidity * mie * mie_color * 0.000434;
                    float zenith = acos(max(0.0, dot(UP, eye)));
                    float optical_mass = 1.0 / (cos(zenith) + 0.15 * pow(max(93.885 - degrees(zenith), 1e-3), -1.253));
                    float3 extinction = exp(-(rb * RAYLEIGH_ZENITH_SIZE * optical_mass + mb * MIE_ZENITH_SIZE * optical_mass));
                    float rp = (3.0 / (16.0 * PI_)) * (1.0 + pow(cos_theta * 0.5 + 0.5, 2.0));
                    float3 brt = rb * rp, bmt = mb * hg(cos_theta, mie_eccentricity);
                    float3 lin = pow(max(sun_energy * ((brt + bmt) / (rb + mb)) * (1.0 - extinction), 0), 1.5);
                    lin *= lerp(1.0, pow(max(sun_energy * ((brt + bmt) / (rb + mb)) * extinction, 0), 0.5), saturate(pow(1.0 - zenith_angle, 5.0)));
                    lin *= lerp(ground_color, 1.0, smoothstep(-0.1, 0.1, dot(UP, eye)));
                    float size = 0.0093;
                    float sundisk = smoothstep(cos(size), cos(size * 0.5), cos_theta) * (1.0 - _CloudCover * 0.9);
                    float3 l0 = (sun_energy * 1900.0 * extinction) * sundisk * lightCol + 0.1 * extinction;
                    color = (lin + l0) * 0.04;
                    color = pow(max(color, 0), 1.0 / (1.2 + 1.2 * sun_fade));
                    color *= exposure;
                    float l = dot(color, float3(0.3, 0.59, 0.11));
                    float sat = lerp(1.0, saturation, smoothstep(0.02, 0.4, sun_dir.y));
                    color = max(lerp(l, color, sat), 0);
                    float dusk = 1.0 - smoothstep(0.05, 0.35, sun_dir.y);
                    float high = smoothstep(0.05, 0.6, eye.y);
                    float glow = pow(max(cos_theta, 0.0), 6.0);
                    float3 twilight_blue = float3(0.035, 0.06, 0.15) + l * float3(0.4, 0.5, 0.9);
                    color = lerp(color, twilight_blue, dusk * high * (1.0 - glow) * 0.8);
                }
                color += night_color * _Night;

                float2 flat_eye = normalize(eye.xz + 0.0001);
                float2 sflat = normalize(_SunFlat.xz + 0.0001);
                if (_Twilight > 0.001)
                {
                    float up_tw = max(eye.y, 0.0);
                    float toward = max(dot(flat_eye, sflat), 0.0);
                    float band = exp(-up_tw * 7.0);
                    float3 horizon_col = lerp(float3(0.34, 0.2, 0.36), float3(0.85, 0.42, 0.2), toward * toward);
                    float3 tw = lerp(float3(0.05, 0.08, 0.2), horizon_col, band) * (0.55 + 0.45 * toward * band);
                    color = max(color, tw * _Twilight);
                }
                {
                    float up = saturate(eye.y);
                    float3 drawn = lerp(float3(0.42, 0.70, 0.95), float3(0.16, 0.42, 0.82), pow(up, 0.55));
                    float lum_sky = dot(color, float3(0.3, 0.59, 0.11));
                    float3 tinted = drawn * clamp(lum_sky / dot(drawn, float3(0.3, 0.59, 0.11)), 0.0, 0.8);
                    color = lerp(color, tinted, _Stylize * smoothstep(0.0, 0.35, sun_dir.y) * _SunOn);
                }
                float lum = dot(color, float3(0.3, 0.59, 0.11));
                float3 overcast = float3(0.62, 0.65, 0.7) * max(lum * 1.1, 0.002 + 0.25 * sun_up);
                color = lerp(color, overcast, _CloudCover * _CloudCover * 0.85);

                float density = 0;
                float2 sheetUv = eye.xz / (max(eye.y, 0.0) + 0.08) * 0.08 + float2(_CloudDrift * 0.004, _CloudDrift * 0.0017);
                _uvDx = ddx(sheetUv);
                _uvDy = ddy(sheetUv);
                if (eye.y > 0.0)
                {
                    float2 uv = sheetUv;
                    density = clouds(uv, true);
                    float depth = 0;
                    [unroll] for (int k = 1; k <= 3; k++)
                        depth += clouds(uv + sun_dir.xz * 0.011 * k, false);
                    float through = exp(-depth * 0.9);
                    float powder = 1.0 - exp(-density * 3.5);
                    float3 sun_lit = float3(1.0, 0.97, 0.93) * lerp(0.3, 1.05, sun_up) * lightCol;
                    float3 ambient = lerp(float3(0.42, 0.5, 0.62), float3(0.6, 0.62, 0.66), _CloudCover) * lerp(0.18, 0.7, sun_up);
                    float3 base = lerp(ambient, sun_lit, through * lerp(0.7, 1.0, powder));
                    base += lightCol * pow(max(cos_theta, 0.0), 8.0) * (1.0 - density) * 1.6 * sun_up;
                    base = lerp(base, overcast * 0.9, _CloudCover * 0.6);
                    float toward_c = max(dot(flat_eye, sflat), 0.0);
                    float3 tw_cloud = lerp(float3(0.4, 0.24, 0.36), float3(0.95, 0.5, 0.28), toward_c) * 0.7;
                    base = lerp(base, tw_cloud * (0.15 + 0.85 * _Twilight), smoothstep(0.0, 0.25, _Twilight) * (1.0 - sun_up));
                    base = lerp(base, float3(0.05, 0.06, 0.09), _Night * (1.0 - _Twilight));
                    float3 drawn_cloud = lerp(float3(0.68, 0.76, 0.88), float3(1.0, 0.99, 0.97), through) * lerp(0.35, 1.0, sun_up);
                    base = lerp(base, drawn_cloud, _Stylize * 0.6 * smoothstep(0.0, 0.35, sun_dir.y) * (1.0 - _CloudCover * 0.6) * _SunOn);
                    float fade = smoothstep(0.06, 0.32, eye.y);
                    color = lerp(color, base * 0.9, density * fade * 0.92);

                    // stars (stars.gdshader): behind the clouds, out once the twilight has faded
                    float3 p = eye * 120.0;
                    float h = hash(floor(p));
                    float star = step(0.985, h) * smoothstep(0.3, 0.0, length(frac(p) - 0.5));
                    float twinkle = 0.7 + 0.3 * sin(_Time.y * 3.0 + h * 40.0);
                    float threshold = lerp(0.72, 0.18, _CloudCover);
                    float cl = smoothstep(threshold - 0.05, threshold + 0.12, cloud_shape(uv));
                    color += float3(0.9, 0.95, 1.0) * star * twinkle * smoothstep(0.02, 0.25, eye.y) * _Stars * 2.0 * (1.0 - cl);
                }
                // the horizon melts into the fog's colour, so the far land fogged by Unity meets the sky without a seam
                // (under a grey sky and in rain the hills fade into it as they do; on clear days only a thin haze band)
                float low = 1.0 - smoothstep(-0.04, max(_SkyFogHeight, 0.02), eye.y);
                color = lerp(color, unity_FogColor.rgb, saturate(low * _SkyFogBlend));
                // an overcast sky is a neutral, slightly cool grey: the sunlit clouds' warmth fades with the cover
                float lumc = dot(color, float3(0.3, 0.59, 0.11));
                color = lerp(color, lumc * float3(0.95, 0.98, 1.03), _CloudCover * _CloudCover * 0.9);
                return float4(color, 1);
            }
            ENDCG
        }
    }
}
