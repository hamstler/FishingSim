// The Godot game's environment finish (GraphicsQuality.ApplyEnvironment): sun shafts (standing in for the volumetric
// fog's light shafts: bright sky behind the trees smeared away from the sun, strongest at the golden hour),
// screen-space ambient occlusion from the
// depth buffer, glow over an HDR threshold of 1.15 blended as soft light, ACES tonemapping with white 6 (Godot's
// formula), contrast 1.12, saturation 1.1 and the Nordic colour correction (per channel through a 1D gradient: teal
// shadows, warm highlights), the time of day's exposure (SkyController: 1.9 at night .. 1.05 by day), in Godot's order (tonemap -> sRGB -> adjustments -> correction).
Shader "Hidden/FS/Post"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _Bloom, _Grade, _AO, _Shafts;
    float4 _SunScreen, _ShaftTint;
    float _ShaftStrength;
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

    // view-space point of a screen position (the camera's own projection; off-centre terms ignored)
    float3 ViewPos(float2 uv)
    {
        float d = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
        float2 ndc = uv * 2 - 1;
        return float3(ndc.x / unity_CameraProjection._m00, ndc.y / unity_CameraProjection._m11, 1) * d;
    }

    // 3: ambient occlusion from depth, oriented by the surface (Alchemy-style: a sample occludes only when it rises
    // above the point's tangent plane, so flat ground and water seen at a grazing angle stay unshaded)
    float4 Occlusion(v2f i) : SV_Target
    {
        float3 p = ViewPos(i.uv);
        float d = p.z;
        if (d > 250) return 1;
        // normal from the nearer neighbour on each axis (no halo along depth edges)
        float2 tx = float2(_MainTex_TexelSize.x * 2, 0), ty = float2(0, _MainTex_TexelSize.y * 2);
        float3 px1 = ViewPos(i.uv + tx) - p, px0 = p - ViewPos(i.uv - tx);
        float3 py1 = ViewPos(i.uv + ty) - p, py0 = p - ViewPos(i.uv - ty);
        float3 dx = abs(px1.z) < abs(px0.z) ? px1 : px0;
        float3 dy = abs(py1.z) < abs(py0.z) ? py1 : py0;
        float3 n = normalize(cross(dy, dx));
        if (dot(n, p) > 0) n = -n;
        float radius = _AORadius * unity_CameraProjection._m11 * 0.5 / d;
        float ao = 0;
        // a 4x4 interleaved rotation pattern (the blur pass averages it away; per-pixel random spin left grain)
        float2 cell = fmod(floor(i.uv * _ScreenParams.xy * 0.5), 4.0);
        float spin = (cell.x * 4 + cell.y) * (6.2831 / 16.0);
        [unroll] for (int k = 0; k < 12; k++)
        {
            float a = spin + k * 2.3999;
            float r = (k + 0.5) / 12.0;
            float2 off = float2(cos(a), sin(a)) * radius * r;
            off.x *= _ScreenParams.y / _ScreenParams.x;
            float3 v = ViewPos(i.uv + off) - p;
            float len2 = dot(v, v);
            float falloff = saturate(1 - len2 / (_AORadius * _AORadius * 4));
            ao += max(dot(n, v) - 0.02 * d - 0.03, 0) / (len2 + 0.05) * falloff;
        }
        ao = saturate(1 - ao / 12.0 * _AOStrength);
        return ao;
    }

    // 7: blur the occlusion (a 4x4 box matching the rotation pattern, depth-aware)
    float4 BlurAO(v2f i) : SV_Target
    {
        float d0 = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv));
        float sum = 0, wsum = 0;
        [unroll] for (int y = -2; y < 2; y++)
            [unroll] for (int x = -2; x < 2; x++)
            {
                float2 uv = i.uv + (float2(x, y) + 0.5) * _MainTex_TexelSize.xy;
                float d = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
                float w = saturate(1 - abs(d - d0) / (0.05 * d0 + 0.1));
                sum += tex2D(_MainTex, uv).r * w;
                wsum += w;
            }
        return sum / max(wsum, 1e-3);
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

    // the W3C soft light (Godot's glow blend): D(c) = ((16c - 12)c + 4)c below a quarter, sqrt(c) above
    float SoftLight(float c, float g)
    {
        return g <= 0.5 ? c - (1 - 2 * g) * c * (1 - c)
             : c <= 0.25 ? c + (2 * g - 1) * (((16 * c - 12) * c + 4) * c - c)
             : c + (2 * g - 1) * (sqrt(c) - c);
    }

    // 5: what can shine through: bright sky (nothing in front of it) near the sun
    float4 ShaftMask(v2f i) : SV_Target
    {
        float d = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv));
        float sky = step(0.999, d);
        float3 c = tex2D(_MainTex, i.uv).rgb;
        float2 toSun = (i.uv - _SunScreen.xy) * float2(_ScreenParams.x / _ScreenParams.y, 1);
        float near = saturate(1.0 - length(toSun) * 1.4);
        return float4(c * sky * near * near, 1);
    }
    // 6: smeared away from the sun (the light through the haze between the trees)
    float4 ShaftBlur(v2f i) : SV_Target
    {
        float2 dir = (_SunScreen.xy - i.uv) / 24.0;
        float2 uv = i.uv;
        float3 sum = 0;
        float w = 1.0;
        [unroll] for (int k = 0; k < 24; k++)
        {
            sum += tex2D(_MainTex, uv).rgb * w;
            uv += dir;
            w *= 0.95;
        }
        return float4(sum / 12.0, 1);
    }

    // 4: composite
    float4 Composite(v2f i) : SV_Target
    {
        float exposure = _SceneExposure > 0 ? _SceneExposure : 1.0;
        float3 c = tex2D(_MainTex, i.uv).rgb;
        if (_ShaftStrength > 0.001)
            c += tex2D(_Shafts, i.uv).rgb * _ShaftTint.rgb * _ShaftStrength;
        c *= exposure;
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
        // debanding (Godot's use_debanding): a triangular dither of one 8-bit step before the output is quantised, so
        // the sky's gentle gradients do not break into flat sheets with stepped edges
        float2 px = i.uv * _ScreenParams.xy;
        float n1 = frac(sin(dot(px, float2(12.9898, 78.233))) * 43758.5453);
        float n2 = frac(sin(dot(px, float2(39.3468, 11.135))) * 24634.6345);
        c += (n1 + n2 - 1.0) / 255.0;
        return float4(GammaToLinearSpace(saturate(c)), 1);
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
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment ShaftMask
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment ShaftBlur
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment BlurAO
            #pragma target 3.0
            ENDCG }
    }
}
