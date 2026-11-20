// Tree trunks (shaders/bark.gdshader): photographed bark tinted so its average becomes the tree's bark colour, relief
// from its normal map, deep furrows darker.
Shader "FS/Bark"
{
    Properties
    {
        _MainTex ("Bark", 2D) = "gray" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _Mean ("Bark mean (linear)", Color) = (0.3,0.3,0.3,1)
        _Relief ("Relief", Float) = 1.2
        _Rough ("Roughness", Float) = 0.88
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard vertex:vert addshadow fullforwardshadows nolightmap nodynlightmap nodirlightmap nometa exclude_path:deferred exclude_path:prepass
        #pragma multi_compile_instancing
        #pragma target 3.5
        sampler2D _MainTex, _BumpMap;
        float4 _Mean;
        float _Relief, _Rough, _FoliageWind, _FoliageTime;
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
        UNITY_INSTANCING_BUFFER_END(Props)
        struct Input { float2 uv_MainTex; };
        void vert(inout appdata_full v) { UNITY_SETUP_INSTANCE_ID(v); }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float4 a = tex2D(_MainTex, IN.uv_MainTex);
            float3 n = tex2D(_BumpMap, IN.uv_MainTex).rgb;
            n = pow(n, 0.4545) * 2 - 1;
            float3 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint).rgb;
            o.Albedo = saturate(tint * a.rgb / max(_Mean.rgb, 0.01));
            o.Normal = normalize(float3(n.xy * _Relief, max(n.z, 0.3)));
            float lum = dot(a.rgb, float3(0.3, 0.59, 0.11)) / max(dot(_Mean.rgb, float3(0.3, 0.59, 0.11)), 0.01);
            o.Occlusion = lerp(0.5, 1.0, saturate(lum));
            o.Smoothness = 1.0 - _Rough;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
