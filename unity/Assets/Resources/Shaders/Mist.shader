// Low mist over the water (shaders/mist_bank.gdshader): five horizontal layers 0.1-1.4 m over the map; each layer's
// density is drifting noise, thinner the higher it lies, fading near the camera and far away; edge-on views gather
// more of it. uv2.x = the layer (0 lowest .. 1 highest).
Shader "FS/Mist"
{
    Properties
    {
        _Noise ("Noise", 2D) = "gray" {}
        _MistColor ("Mist colour", Color) = (0.86,0.88,0.9,1)
        _Density ("Density", Float) = 0
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
            #include "UnityCG.cginc"
            sampler2D _Noise;
            float4 _MistColor;
            float _Density, _WaterTime;
            struct appdata { float4 vertex : POSITION; float2 uv2 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float layer : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(o.wpos);
                o.layer = v.uv2.x;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 drift = float2(_WaterTime * 0.015, _WaterTime * 0.005) * (1.0 + i.layer);
                float n = tex2D(_Noise, i.wpos.xz * 0.012 + drift + i.layer * 3.1).r * 0.65 + tex2D(_Noise, i.wpos.xz * 0.045 - drift * 1.6).r * 0.35;
                float sheet = smoothstep(0.38, 0.8, n) * (1.0 - i.layer * 0.65);
                float3 v = i.wpos - _WorldSpaceCameraPos;
                float d = length(v);
                float near = smoothstep(3.0, 18.0, d);
                float far = 1.0 - smoothstep(260.0, 420.0, d);
                float graze = clamp(1.0 / max(abs(v.y / d), 0.08), 1.0, 6.0);
                return float4(_MistColor.rgb, clamp(sheet * near * far * _Density * 0.055 * graze, 0.0, 0.42));
            }
            ENDCG
        }
    }
}
