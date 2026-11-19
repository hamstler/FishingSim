// The lake: waves moved in the vertex shader, colour by the water's thickness above the bed (camera depth texture),
// sky reflection by Fresnel, the sun's glint, fog.
Shader "FS/Water"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.08,0.28,0.26,1)
        _Deep ("Deep", Color) = (0.01,0.07,0.1,1)
        _SkyTop ("Sky top", Color) = (0.35,0.55,0.85,1)
        _SkyHorizon ("Sky horizon", Color) = (0.75,0.82,0.9,1)
        _Amp ("Wave height", Float) = 0.06
        _Clarity ("Clarity (m)", Float) = 2.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _Shallow, _Deep, _SkyTop, _SkyHorizon;
            float _Amp, _Clarity;
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float4 scr : TEXCOORD1; UNITY_FOG_COORDS(2) };

            // Four travelling waves (height and slope); the wind direction is fixed per map.
            float WaveH(float2 p, float t, out float2 slope)
            {
                float2 d0 = normalize(float2(1, 0.3)), d1 = normalize(float2(0.6, 1)), d2 = normalize(float2(-0.4, 1)), d3 = normalize(float2(1, -0.7));
                float4 k = float4(0.7, 1.1, 1.9, 3.1);
                float4 a = float4(1.0, 0.6, 0.3, 0.15) * _Amp;
                float4 s = float4(1.2, 1.5, 2.0, 2.6);
                float4 ph = float4(dot(d0, p), dot(d1, p), dot(d2, p), dot(d3, p)) * k + s * t;
                float4 sn = sin(ph), cs = cos(ph);
                slope = d0 * a.x * k.x * cs.x + d1 * a.y * k.y * cs.y + d2 * a.z * k.z * cs.z + d3 * a.w * k.w * cs.w;
                return dot(a, sn);
            }
            v2f vert(appdata_base v)
            {
                v2f o;
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                float2 sl;
                w.y += WaveH(w.xz, _Time.y, sl);
                o.wpos = w;
                o.pos = UnityWorldToClipPos(w);
                o.scr = ComputeScreenPos(o.pos);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 sl;
                WaveH(i.wpos.xz, _Time.y, sl);
                // fine ripples on top
                float t = _Time.y;
                float2 p = i.wpos.xz;
                sl += float2(cos(p.x * 7.3 + t * 3.1) + cos(p.x * 5.1 - p.y * 4.3 + t * 2.3), cos(p.y * 6.7 + t * 2.7) + cos(p.y * 9.1 + p.x * 3.3 - t * 3.7)) * 0.012;
                sl *= 0.6;
                float3 n = normalize(float3(-sl.x, 1, -sl.y));
                float3 v = normalize(_WorldSpaceCameraPos - i.wpos);
                float fres = 0.02 + 0.6 * pow(1 - saturate(dot(n, v)), 5);
                float3 r = reflect(-v, n);
                float3 sky = lerp(_SkyHorizon.rgb, _SkyTop.rgb, saturate(r.y * 2.5)) * 0.75;
                float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.scr)));
                float thick = max(sceneZ - i.scr.w, 0);
                float deep = 1 - exp(-thick / max(_Clarity, 0.1));
                float3 body = lerp(_Shallow.rgb, _Deep.rgb, deep) * (0.35 + 0.65 * _LightColor0.rgb);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float spec = pow(saturate(dot(reflect(-L, n), v)), 280) * 6 + pow(saturate(dot(reflect(-L, n), v)), 40) * 0.25;
                float3 col = lerp(body, sky, fres) + _LightColor0.rgb * spec;
                float alpha = saturate(max(saturate(thick * 2.2) * (0.55 + 0.45 * deep), fres));
                alpha = max(alpha, saturate(thick * 0.6));
                UNITY_APPLY_FOG(i.fogCoord, col);
                return float4(col, alpha);
            }
            ENDCG
        }
    }
}
