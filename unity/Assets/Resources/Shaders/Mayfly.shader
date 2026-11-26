// Mayflies dancing over the water's edge at dusk (shaders/mayfly.gdshader): one mesh per swarm of small quads, each
// an insect (uv2.x = its id); the vertex shader works out the dance (a quick climb, a slow floating fall, the swarm
// slowly circling) and faces the quad to the camera; the fragment draws the slender body, upright wings, three tails.
Shader "FS/Mayfly"
{
    Properties
    {
        _Fade ("Shown", Range(0,1)) = 1
        _Light ("Light", Float) = 1
        _Tint ("Tint", Color) = (0.95,0.9,0.78,1)
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Fade, _Light;
            float4 _Tint;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float flap : TEXCOORD1; };
            float hash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }
            v2f vert(appdata v)
            {
                v2f o;
                float id = v.uv2.x;
                float t = _Time.y;
                float ph = hash(id) * 6.2831;
                float speed = 0.28 + 0.22 * hash(id + 3.1);
                float cyc = frac(t * speed + hash(id + 7.7));
                float h = cyc < 0.28 ? smoothstep(0.0, 1.0, cyc / 0.28) : 1.0 - smoothstep(0.0, 1.0, (cyc - 0.28) / 0.72);
                float r = 0.15 + 0.85 * hash(id + 1.3);
                float a = t * (0.12 + 0.2 * hash(id + 5.0)) + ph;
                float3 off = float3(cos(a) * r, 0.8 + h * (0.7 + 0.7 * hash(id + 9.0)), sin(a) * r * 0.7);
                o.flap = cyc < 0.28 ? abs(sin(t * 55.0 + ph)) : 0.85;
                float shown = step(hash(id + 11.0), _Fade);
                float3 origin = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz + off;
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float3 w = origin + (right * (v.uv.x - 0.5) + up * (v.uv.y - 0.5)) * 0.03 * shown;
                o.pos = UnityWorldToClipPos(w);
                o.uv = v.uv;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                p.y = -p.y;
                float body = smoothstep(0.16, 0.08, abs(p.y + p.x * 0.35)) * step(-0.55, p.x) * step(p.x, 0.75);
                float wing = smoothstep(1.0, 0.9, length(float2((p.x - 0.1) * 2.3, (p.y + 0.55) * 1.3 / max(i.flap, 0.25))));
                float tails = step(abs(p.y - (p.x + 0.55) * 0.9), 0.04) + step(abs(p.y - (p.x + 0.55) * 0.2), 0.04) + step(abs(p.y + (p.x + 0.55) * 0.5), 0.04);
                tails *= step(p.x, -0.5) * step(-1.0, p.x);
                float a = max(body, max(wing * 0.7, tails * 0.8));
                clip(a - 0.35);
                float3 c = lerp(_Tint.rgb * 0.85, float3(1.0, 0.97, 0.9), wing * (1.0 - body));
                return float4(c * (0.08 + 0.92 * _Light), 1);
            }
            ENDCG
        }
    }
}
