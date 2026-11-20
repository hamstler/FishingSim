// A rise ring (shaders/rise_ring.gdshader): a flat quad riding the lake's Gerstner waves whose fragment draws two
// rings spreading out and fading, a thin bright crest with a faint dark trough inside each; _Age runs 0..1.
Shader "FS/RiseRing"
{
    Properties
    {
        _Age ("Age", Range(0,1)) = 1
        _Tint ("Tint", Color) = (0.7,0.78,0.84,1)
        _Light ("Light", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "Waves.cginc"
            float _Age, _Light;
            float4 _Tint;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v)
            {
                v2f o;
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                w.y += water_height(w.xz) + _WaterAmp * 0.15;
                o.pos = UnityWorldToClipPos(w);
                o.uv = v.texcoord.xy;
                return o;
            }
            float ring(float d, float r, float w) { return smoothstep(w, 0.0, abs(d - r)); }
            float4 frag(v2f i) : SV_Target
            {
                float d = length(i.uv - 0.5) * 2.0;
                float r1 = 0.12 + 0.85 * sqrt(_Age);
                float r2 = max(0.0, r1 - 0.3);
                float a = ring(d, r1, 0.035) + 0.6 * ring(d, r2, 0.03) * step(0.05, r2);
                float trough = ring(d, r1 - 0.06, 0.04) + 0.6 * ring(d, r2 - 0.05, 0.035) * step(0.1, r2);
                float fade = (1.0 - _Age) * (1.0 - _Age) * step(d, 1.0);
                float3 c = lerp(_Tint.rgb * 0.25, _Tint.rgb, a / max(a + trough, 0.001)) * _Light;
                return float4(c, saturate((a * 0.45 + trough * 0.18) * fade));
            }
            ENDCG
        }
    }
}
