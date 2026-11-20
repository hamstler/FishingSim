// Ground, trees, rocks, the pier and the fish: vertex colour x a world-space grain, Standard lighting with shadows.
// Vertex alpha < 1 marks swaying foliage (1 - alpha = how far it moves in the wind).
Shader "FS/Lit"
{
    Properties
    {
        _Color ("Colour", Color) = (1,1,1,1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.12
        _Metallic ("Metallic", Range(0,1)) = 0
        _Grain ("Grain texture", 2D) = "gray" {}
        _GrainScale ("Grain scale (1/m)", Float) = 0.35
        _GrainStrength ("Grain strength", Range(0,1)) = 0.35
        _Wind ("Wind sway", Float) = 0
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow nolightmap nodynlightmap nodirlightmap nometa exclude_path:deferred exclude_path:prepass
        #pragma target 3.0
        sampler2D _Grain;
        fixed4 _Color;
        half _Glossiness, _Metallic, _GrainScale, _GrainStrength, _Wind;
        struct Input { float4 color : COLOR; float3 worldPos; float3 worldNormal; };
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float sway = (1.0 - v.color.a) * _Wind;
            float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
            float t = _Time.y;
            float3 off = float3(sin(t * 1.3 + w.x * 0.21 + w.z * 0.13), 0, sin(t * 1.1 + w.z * 0.19)) * sway * 0.12;
            off += float3(sin(t * 4.7 + w.y * 3.1 + w.x), 0, cos(t * 5.3 + w.y * 2.7)) * sway * 0.02;
            v.vertex.xyz += mul((float3x3)unity_WorldToObject, off);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 n = abs(IN.worldNormal);
            n /= (n.x + n.y + n.z);
            float3 p = IN.worldPos * _GrainScale;
            float g = tex2D(_Grain, p.xz).r * n.y + tex2D(_Grain, p.xy).r * n.z + tex2D(_Grain, p.zy).r * n.x;
            float g2 = tex2D(_Grain, p.xz * 0.13 + 0.37).r;
            float grain = lerp(1.0, (0.55 + g * 0.9) * (0.8 + g2 * 0.4), _GrainStrength);
            o.Albedo = IN.color.rgb * _Color.rgb * grain;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
