// The sky dome: gradient from the horizon to the zenith, the sun's disc and glow, drifting clouds, stars at night.
Shader "FS/Sky"
{
    Properties
    {
        _Top ("Zenith", Color) = (0.25,0.45,0.8,1)
        _Horizon ("Horizon", Color) = (0.75,0.82,0.9,1)
        _Ground ("Below", Color) = (0.3,0.32,0.3,1)
        _SunDir ("Sun direction", Vector) = (0,0.5,0.8,0)
        _SunColor ("Sun colour", Color) = (1,0.95,0.85,1)
        _Cloud ("Cloud cover", Range(0,1)) = 0.4
        _CloudColor ("Cloud colour", Color) = (1,1,1,1)
        _Night ("Night", Range(0,1)) = 0
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
            #include "UnityCG.cginc"
            float4 _Top, _Horizon, _Ground, _SunDir, _SunColor, _CloudColor;
            float _Cloud, _Night;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o; }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y);
            }
            float fbm(float2 p) { float s = 0, a = 0.5; for (int k = 0; k < 5; k++) { s += vnoise(p) * a; p *= 2.03; a *= 0.5; } return s; }
            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;
                float3 col = up > 0 ? lerp(_Horizon.rgb, _Top.rgb, pow(saturate(up), 0.55)) : lerp(_Horizon.rgb, _Ground.rgb, saturate(-up * 4));
                float3 s = normalize(_SunDir.xyz);
                float sd = saturate(dot(d, s));
                col += _SunColor.rgb * (pow(sd, 900) * 30 + pow(sd, 12) * 0.25 * (1 - _Night));
                if (up > 0.0)
                {
                    float2 uv = d.xz / (up + 0.12) * 1.4 + _Time.y * float2(0.004, 0.0015);
                    float c = fbm(uv * 1.6);
                    float cover = smoothstep(1 - _Cloud * 0.8 - 0.25, 1 - _Cloud * 0.8 + 0.25, c);
                    float lit = 0.75 + 0.25 * fbm(uv * 1.6 + s.xz * 0.15);
                    float3 cc = _CloudColor.rgb * lit + _SunColor.rgb * pow(sd, 6) * 0.4;
                    col = lerp(col, cc, cover * smoothstep(0.0, 0.15, up));
                    float star = step(0.9965, hash(floor(d.xz / (up + 0.3) * 260))) * _Night * (1 - cover);
                    col += star * 0.9;
                }
                return float4(col, 1);
            }
            ENDCG
        }
    }
}
