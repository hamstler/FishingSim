// Campfire flames: crossed quads, additive, a flickering tongue shape from scrolling noise (vertex colour = tint,
// uv.y 0 at the base).
Shader "FS/Flame"
{
    Properties { _Color ("Tint", Color) = (1,0.6,0.2,1) _Strength ("Strength", Float) = 1 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            float _Strength;
            struct a2v { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y);
            }
            v2f vert(a2v v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o; }
            float4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.uv;
                float n = vnoise(float2(uv.x * 4 + i.color.a * 13, uv.y * 3 - t * 3.5)) * 0.6 + vnoise(float2(uv.x * 9, uv.y * 7 - t * 6)) * 0.4;
                float width = (1 - uv.y) * 0.5 + 0.05;
                float body = saturate(1 - abs(uv.x - 0.5 + (n - 0.5) * 0.25 * uv.y) / width);
                float tongue = saturate(body * 1.6 - uv.y * 1.1 - (1 - n) * 0.5);
                float3 hot = lerp(_Color.rgb, float3(1, 0.95, 0.7), saturate(tongue * 1.4 - 0.3));
                return float4(hot * tongue * _Strength * i.color.rgb, 1);
            }
            ENDCG
        }
    }
}
