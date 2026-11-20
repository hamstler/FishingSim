// The Godot game's environment finish (GraphicsQuality.ApplyEnvironment): screen-space ambient occlusion from the
// depth buffer, glow over an HDR threshold of 1.15 blended as soft light, ACES tonemapping with white 6 (Godot's
// formula), contrast 1.12, saturation 1.1 and the Nordic colour correction (per channel through a 1D gradient: teal
// shadows, warm highlights), the time of day's exposure (SkyController: 1.9 at night .. 1.05 by day), in Godot's order (tonemap -> sRGB -> adjustments -> correction).
Shader "Hidden/FS/Post"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _Bloom, _Grade, _AO;
    float4 _MainTex_TexelSize;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
    float _SceneExposure, _Threshold, _GlowIntensity, _AOStrength, _AORadius, _Contrast, _Saturation, _White, _UseAO;

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

    float3 Box(float2 uv, float d)
    {
        float4 o = _MainTex_TexelSize.xyxy * float2(-d, d).xxyy;
        return (tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb + tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb) * 0.25;
    }

    // 0: bright parts over the threshold (soft knee)
    float4 Prefilter(v2f i) : SV_Target
    {
        float3 c = Box(i.uv, 1);
        float b = max(c.r, max(c.g, c.b));
        float soft = clamp(b - _Threshold + 0.5, 0, 1);
        soft = soft * soft * 0.5;
        float k = max(soft, b - _Threshold) / max(b, 1e-4);
        return float4(c * k, 1);
    }
    // 1: down
    float4 Down(v2f i) : SV_Target { return float4(Box(i.uv, 1), 1); }
    // 2: up (added onto the level below by blending)
    float4 Up(v2f i) : SV_Target { return float4(Box(i.uv, 0.5), 1); }

    // 3: ambient occlusion from depth (a disc of samples scaled by distance, range checked)
    float4 Occlusion(v2f i) : SV_Target
    {
        float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
        float d = LinearEyeDepth(raw);
        if (d > 300) return 1;
        float radius = _AORadius / d * unity_CameraProjection._m11 * 0.5;
        float ao = 0;
        float spin = frac(sin(dot(i.uv * _ScreenParams.xy, float2(12.9898, 78.233))) * 43758.5453) * 6.2831;
        [unroll] for (int k = 0; k < 12; k++)
        {
            float a = spin + k * 2.3999;
            float r = (k + 0.5) / 12.0;
            float2 off = float2(cos(a), sin(a)) * radius * r;
            off.x *= _ScreenParams.y / _ScreenParams.x;
            float s = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv + off));
            float diff = d - s;
            float range = saturate(_AORadius / max(abs(diff), 1e-3));
            ao += step(0.04 * d * 0.02 + 0.02, diff) * range * (1 - r * 0.5);
        }
        ao = saturate(1 - ao / 12.0 * _AOStrength);
        return pow(ao, 1.4);
    }

    float3 Aces(float3 c, float white)
    {
        const float bias = 1.8, A = 0.0245786, B = 0.000090537, C = 0.983729, D = 0.432951, E = 0.238081;
        c = float3(dot(c, float3(0.59719, 0.35458, 0.04823)), dot(c, float3(0.07600, 0.90834, 0.01566)), dot(c, float3(0.02840, 0.13383, 0.83777))) * bias;
        float3 t = (c * (c + A) - B) / (c * (C * c + D) + E);
        t = float3(dot(t, float3(1.60475, -0.53108, -0.07367)), dot(t, float3(-0.10208, 1.10813, -0.00605)), dot(t, float3(-0.00327, -0.07276, 1.07602)));
        white *= bias;
        float w = (white * (white + A) - B) / (white * (C * white + D) + E);
        return t / w;
    }

    float SoftLight(float c, float g)
    {
        return g <= 0.5 ? c - (1 - 2 * g) * c * (1 - c)
             : c <= 0.25 ? c + (2 * g - 1) * (4 * c * (16 * c + 4) * (c - 1))
             : c + (2 * g - 1) * (sqrt(c) - c);
    }

    // 4: composite
    float4 Composite(v2f i) : SV_Target
    {
        float exposure = _SceneExposure > 0 ? _SceneExposure : 1.0;
        float3 c = tex2D(_MainTex, i.uv).rgb * exposure;
        if (_UseAO > 0.5)
        {
            float ao = tex2D(_AO, i.uv).r;
            c *= lerp(1, ao, 0.85);
        }
        c = Aces(max(c, 0), _White);
        float3 g = saturate(Aces(tex2D(_Bloom, i.uv).rgb * _GlowIntensity * exposure, _White));
        c = saturate(LinearToGammaSpace(saturate(c)));
        g = LinearToGammaSpace(g) * 0.5 + 0.5;
        c = float3(SoftLight(c.r, g.r), SoftLight(c.g, g.g), SoftLight(c.b, g.b));
        c = lerp(0.5, c, _Contrast);
        c = lerp(dot(c, 1) / 3.0, c, _Saturation);
        c = saturate(c);
        c = float3(tex2D(_Grade, float2(c.r, 0.5)).r, tex2D(_Grade, float2(c.g, 0.5)).g, tex2D(_Grade, float2(c.b, 0.5)).b);
        return float4(GammaToLinearSpace(c), 1);
    }
    ENDCG
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment Prefilter
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment Down
            ENDCG }
        Pass { Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment Up
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment Occlusion
            #pragma target 3.5
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment Composite
            ENDCG }
    }
}
