// Textured models built at run time (villagers): colour picture (alpha cut-out for hair, brows, lashes), an optional
// normal map and ambient-occlusion picture (stored as colour pictures: decoded from sRGB here), Standard lighting.
Shader "FS/LitTex"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 0
        _AOMap ("Occlusion", 2D) = "white" {}
        _AOStrength ("Occlusion strength", Float) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.3
        _Cutoff ("Alpha cut-off", Range(0,1)) = 0
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap, _AOMap;
        fixed4 _Color;
        half _BumpScale, _AOStrength, _Glossiness, _Cutoff;
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            clip(c.a - _Cutoff);
            float ao = lerp(1.0, pow(tex2D(_AOMap, IN.uv_MainTex).r, 2.2), _AOStrength);
            o.Albedo = c.rgb * ao;
            o.Occlusion = ao;
            float3 nm = tex2D(_BumpMap, IN.uv_MainTex).rgb;
            nm = pow(nm, 0.4545) * 2 - 1;
            o.Normal = normalize(lerp(float3(0, 0, 1), float3(nm.xy, max(nm.z, 0.2)), _BumpScale));
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Legacy Shaders/Transparent/Cutout/Diffuse"
}
