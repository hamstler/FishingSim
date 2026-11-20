// Tree crowns and bushes from leaf cards (shaders/foliage.gdshader): alpha-tested sprays of painted leaves (an atlas
// of four kinds, or needles), lit as one soft volume (the crown's outward normal), darker deep inside (vertex alpha),
// light shining through the leaves, wind sway and flutter; far crowns darker and greyer.
Shader "FS/Foliage"
{
    Properties
    {
        _MainTex ("Leaves", 2D) = "white" {}
        _Translucency ("Translucency", Color) = (0.55,0.65,0.25,1)
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        CGPROGRAM
        #pragma surface surf Foliage vertex:vert addshadow fullforwardshadows
        #pragma multi_compile_instancing
        #pragma target 3.5
        #include "UnityPBSLighting.cginc"
        sampler2D _MainTex;
        float4 _Translucency;
        float _FoliageWind, _FoliageTime;
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
        UNITY_INSTANCING_BUFFER_END(Props)

        struct Input { float2 uv_MainTex; float4 color : COLOR; float3 worldPos; };
        struct SurfaceOutputFoliage
        {
            fixed3 Albedo; fixed3 Normal; half3 Emission; half Metallic; half Smoothness; half Occlusion; fixed Alpha;
            half3 Backlight;
        };

        void vert(inout appdata_full v)
        {
            UNITY_SETUP_INSTANCE_ID(v);
            float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
            float3 origin = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
            float phase = origin.x * 0.31 + origin.z * 0.27;
            float h = clamp(v.vertex.y + 2.0, 0.0, 6.0);
            float gust = 0.6 + 0.4 * sin(_FoliageTime * 0.35 + phase * 0.2);
            float2 sway = float2(sin(_FoliageTime * 1.1 + phase), cos(_FoliageTime * 0.9 + phase * 1.3)) * 0.025 * h * _FoliageWind * gust;
            float flutter = sin(_FoliageTime * 8.0 + dot(world, float3(3.1, 4.7, 2.3))) * 0.02 * _FoliageWind;
            v.vertex.xz += sway + flutter;
            v.vertex.y += flutter * 0.5;
        }

        half4 LightingFoliage(SurfaceOutputFoliage s, half3 viewDir, UnityGI gi)
        {
            SurfaceOutputStandard st;
            st.Albedo = s.Albedo; st.Normal = s.Normal; st.Emission = s.Emission; st.Metallic = s.Metallic;
            st.Smoothness = s.Smoothness; st.Occlusion = s.Occlusion; st.Alpha = s.Alpha;
            half4 c = LightingStandard(st, viewDir, gi);
            // light through the leaves from behind
            c.rgb += s.Backlight * gi.light.color * saturate(dot(-s.Normal, gi.light.dir));
            return c;
        }

        void LightingFoliage_GI(SurfaceOutputFoliage s, UnityGIInput data, inout UnityGI gi)
        {
            SurfaceOutputStandard st;
            st.Albedo = s.Albedo; st.Normal = s.Normal; st.Emission = s.Emission; st.Metallic = s.Metallic;
            st.Smoothness = s.Smoothness; st.Occlusion = s.Occlusion; st.Alpha = s.Alpha;
            LightingStandard_GI(st, data, gi);
        }

        void surf(Input IN, inout SurfaceOutputFoliage o)
        {
            float4 leaf = tex2D(_MainTex, IN.uv_MainTex);
            float dist = distance(IN.worldPos, _WorldSpaceCameraPos);
            clip(leaf.a - lerp(0.5, 0.12, smoothstep(25.0, 110.0, dist)));
            float shade = IN.color.a;
            float3 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint).rgb;
            float3 albedo = leaf.rgb * tint * lerp(0.72, 1.0, shade);
            float far = smoothstep(30.0, 220.0, dist);
            float grey = dot(albedo, float3(0.3, 0.59, 0.11));
            albedo = lerp(albedo, lerp(grey, albedo, 0.68) * 0.66, far);
            o.Albedo = albedo;
            o.Occlusion = lerp(0.4, 1.0, saturate(shade * 1.4 - 0.4)) * (1.0 - 0.3 * far);
            o.Backlight = _Translucency.rgb * tint * 1.6 * (1.0 - 0.75 * far) * 0.35;
            o.Smoothness = 0.38;
            o.Metallic = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
}
