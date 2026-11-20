// Textured models built at run time (villagers, the angler's arms), with the Godot game's character materials
// (World/Props/CharacterAsset.MaterialFor, StandardMaterial3D): colour picture x tint (skin tint, hair colour), alpha
// cut-out for hair, brows and lashes, a normal map (linear), baked ambient occlusion that darkens the sky's light fully
// and the sun's by _AOLight (Godot's AOLightAffect), Godot's specular amount (MetallicSpecular: 0.5 = F0 0.04), its rim
// (pow(1 - N.V, (1 - roughness) * 16) x rim x mix(white, albedo, rim tint) x the light) and a clear coat for wet eyes.
Shader "FS/LitTex"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BumpMap ("Normal (linear)", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 0
        _AOMap ("Occlusion", 2D) = "white" {}
        _AOStrength ("Occlusion strength", Float) = 0
        _AOLight ("Occlusion in direct light", Float) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.3
        _Spec ("Specular (Godot)", Range(0,1)) = 0.5
        _Rim ("Rim", Range(0,1)) = 0
        _RimTint ("Rim tint", Range(0,1)) = 0.5
        _Clearcoat ("Clear coat", Range(0,1)) = 0
        _Cutoff ("Alpha cut-off", Range(0,1)) = 0
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf Char fullforwardshadows addshadow
        #pragma target 3.5
        #include "UnityPBSLighting.cginc"
        sampler2D _MainTex, _BumpMap, _AOMap;
        fixed4 _Color;
        half _BumpScale, _AOStrength, _AOLight, _Glossiness, _Spec, _Rim, _RimTint, _Clearcoat, _Cutoff;
        struct Input { float2 uv_MainTex; float face : VFACE; };

        struct SurfaceOutputChar
        {
            fixed3 Albedo; fixed3 Normal; half3 Emission; half Smoothness; half Occlusion; fixed Alpha;
            half Spec; half Rim; half RimTint; half AOLight; half Clearcoat;
        };

        SurfaceOutputStandardSpecular ToStd(SurfaceOutputChar s)
        {
            SurfaceOutputStandardSpecular o;
            o.Albedo = s.Albedo; o.Normal = s.Normal; o.Emission = s.Emission; o.Smoothness = s.Smoothness;
            o.Occlusion = s.Occlusion; o.Alpha = s.Alpha;
            o.Specular = 0.16 * s.Spec * s.Spec;      // Godot: F0 = 0.16 x specular^2 (0.5 -> 0.04)
            return o;
        }

        half4 LightingChar(SurfaceOutputChar s, half3 viewDir, UnityGI gi)
        {
            // the baked shade darkens the direct light too, by AOLight
            gi.light.color *= lerp(1.0, s.Occlusion, s.AOLight);
            half4 c = LightingStandardSpecular(ToStd(s), viewDir, gi);
            half nl = saturate(dot(s.Normal, gi.light.dir));
            half nv = saturate(dot(s.Normal, viewDir));
            if (s.Rim > 0)
            {
                half rough = 1.0 - s.Smoothness;
                half rim = pow(max(1e-4, 1.0 - nv), max(0.0, (1.0 - rough) * 16.0));
                c.rgb += rim * s.Rim * lerp(1.0, s.Albedo, s.RimTint) * gi.light.color * nl;
            }
            if (s.Clearcoat > 0)
            {
                half3 h = normalize(gi.light.dir + viewDir);
                half nh = saturate(dot(s.Normal, h));
                half a2 = 0.04 * 0.04;
                half d = nh * nh * (a2 - 1.0) + 1.0;
                half ggx = a2 / (3.14159 * d * d + 1e-5);
                half f = 0.04 + 0.96 * pow(1.0 - saturate(dot(h, viewDir)), 5.0);
                c.rgb += gi.light.color * ggx * f * nl * s.Clearcoat * 0.25;
            }
            return c;
        }

        void LightingChar_GI(SurfaceOutputChar s, UnityGIInput data, inout UnityGI gi)
        {
            LightingStandardSpecular_GI(ToStd(s), data, gi);
        }

        void surf(Input IN, inout SurfaceOutputChar o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            clip(c.a - _Cutoff);
            float ao = lerp(1.0, tex2D(_AOMap, IN.uv_MainTex).r, _AOStrength);
            o.Albedo = c.rgb;
            o.Occlusion = ao;
            float3 nm = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            nm.xy *= _BumpScale;
            nm = normalize(float3(nm.xy, max(nm.z, 0.2)));
            if (IN.face < 0) nm.z = -nm.z;     // two-sided cloth and hair: the inner side lit as its own face
            o.Normal = nm;
            o.Smoothness = _Glossiness;
            o.Spec = _Spec;
            o.Rim = _Rim;
            o.RimTint = _RimTint;
            o.AOLight = _AOLight;
            o.Clearcoat = _Clearcoat;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Legacy Shaders/Transparent/Cutout/Diffuse"
}
