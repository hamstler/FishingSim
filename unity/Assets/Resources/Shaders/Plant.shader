// Small plants (TreeFactory's reeds, rushes, irises, ferns, wildflowers; floating.gdshader's lilies and pondweed):
// vertex colour x the instance's tint, two-sided with normals leaning up (both faces lit alike),
// swaying in the wind by height (vertex alpha = how far a vertex moves); floating plants ride the same Gerstner waves
// as the water, ~0.45 amplitudes up.
Shader "FS/Plant"
{
    Properties
    {
        _Float ("Floats on the water", Float) = 0
        _Sway ("Sway", Float) = 1
        _Gloss ("Smoothness", Float) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard vertex:vert addshadow fullforwardshadows
        #pragma multi_compile_instancing
        #pragma target 3.5
        #include "Waves.cginc"
        float _Float, _Sway, _Gloss, _FoliageWind, _FoliageTime;
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
        UNITY_INSTANCING_BUFFER_END(Props)
        struct Input { float4 color : COLOR; };

        void vert(inout appdata_full v)
        {
            UNITY_SETUP_INSTANCE_ID(v);
            float3 origin = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
            float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
            if (_Float > 0.5)
            {
                float3 d = gerstner(w.xz, 0, VERTEX_WAVES);
                w.y += d.y + 0.45 * _WaterAmp;
                w.xz += d.xz;
            }
            else
            {
                float sway = (1.0 - v.color.a) * _Sway;
                float phase = origin.x * 0.7 + origin.z * 0.53;
                float2 bend = float2(sin(_FoliageTime * 1.4 + phase), cos(_FoliageTime * 1.1 + phase * 1.3)) * (0.04 + 0.16 * _FoliageWind);
                bend += sin(_FoliageTime * 5.0 + w.y * 3.0 + phase) * 0.015 * (0.3 + _FoliageWind);
                w.xz += bend * sway;
            }
            v.vertex.xyz = mul(unity_WorldToObject, float4(w, 1)).xyz;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint).rgb;
            o.Albedo = IN.color.rgb * tint;
            o.Smoothness = _Gloss;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
