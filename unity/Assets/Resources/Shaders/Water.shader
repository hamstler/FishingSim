// The lake (shaders/water.gdshader of the Godot game, ported): Gerstner waves move the mesh (Waves.cginc), the short
// chop, two drifting wind-ripple maps and raindrop rings shape the light; the bottom is seen through the moving surface
// (refraction from the grabbed screen) and coloured by the water it shows through (Beer-Lambert absorption per map);
// the shore, the trees and the hills are reflected (screen-space march, then a far read), the sky by Fresnel; the sun
// glints; lacy foam laps the shore and white caps break in a strong wind; the last centimetres fade to clear.
Shader "FS/Water"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.04,0.26,0.26,1)
        _Deep ("Deep", Color) = (0.01,0.07,0.11,1)
        _Clarity ("Clarity (m)", Float) = 2.6
        _Extinction ("Extinction tint", Vector) = (2.4,1.0,0.8,0)
        _Wind ("Wind", Float) = 0.3
        _Rain ("Rain", Float) = 0
        _Detail ("Ripple detail", Float) = 1
        _SsrSteps ("Reflection steps", Float) = 20
        _MaxDepth ("Max depth", Float) = 16
        _MapRect ("Depth map rect", Vector) = (-170,-200,340,240)
        _DepthMap ("Depth map", 2D) = "black" {}
        _Ripples ("Ripple normals", 2D) = "gray" {}
        _FoamNoise ("Foam noise", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        GrabPass { "_WaterGrab" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            ZWrite On
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "Waves.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            sampler2D _WaterGrab;
            float4 _WaterGrab_TexelSize;
            sampler2D _DepthMap, _Ripples, _FoamNoise;
            float4 _Shallow, _Deep, _Extinction, _MapRect;
            float _Clarity, _Wind, _Rain, _Detail, _SsrSteps, _MaxDepth;
            float4 _SkyTop, _SkyHorizon, _SunTint;
            float3 _SunDirection;
            float _WaterDebug;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                float crest : TEXCOORD1;
                float4 grab : TEXCOORD2;
                float4 scr : TEXCOORD3;
                UNITY_FOG_COORDS(4)
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 d = gerstner(w.xz, 0, VERTEX_WAVES);
                w += d;
                o.wpos = w;
                o.crest = saturate(d.y / max(_WaterAmp * 0.9, 0.0001) * 0.5 + 0.5);
                o.pos = UnityWorldToClipPos(w);
                o.grab = ComputeGrabScreenPos(o.pos);
                o.scr = ComputeScreenPos(o.pos);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float2 ripple_map(float2 uv) { return tex2D(_Ripples, uv).rg * 2.0 - 1.0; }

            float2 rain_slope(float2 p)
            {
                float2 cell = floor(p / 0.6);
                float2 slope = 0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float2 c = cell + float2(i & 1, i >> 1) - 0.5;
                    float rnd = frac(sin(dot(c, float2(12.9898, 78.233))) * 43758.5453);
                    float phase = frac(_WaterTime * (0.9 + rnd * 0.6) + rnd * 7.0);
                    float2 centre = (c + 0.25 + 0.5 * frac(float2(rnd * 13.1, rnd * 7.7))) * 0.6;
                    float2 d = p - centre;
                    float r = length(d);
                    float ring = phase * 0.32;
                    float wave = sin((r - ring) * 60.0) * exp(-abs(r - ring) * 40.0) * (1.0 - phase);
                    slope += (r > 0.001 ? d / r : 0) * wave;
                }
                return slope * 0.25 * _Rain;
            }

            // The distance from the camera to whatever the screen shows at uv (along that pixel's ray).
            float scene_dist(float2 uv, float3 rayDir)
            {
                float eye = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0)));
                float3 fwd = -UNITY_MATRIX_V[2].xyz;
                return eye / max(dot(rayDir, fwd), 0.05);
            }

            float3 sky_colour(float3 r)
            {
                // Godot reflects its sky's radiance cubemap, which is darker and bluer than the haze at the horizon
                // (the Jezioro reference: navy water under a pale sky): the reflected horizon leans to the zenith.
                float3 sky = lerp(_SkyHorizon.rgb, _SkyTop.rgb, 0.45 + 0.55 * pow(saturate(r.y), 0.55)) * 0.6;
                return sky;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 cam = _WorldSpaceCameraPos;
                float3 toSurf = i.wpos - cam;
                float dist = length(toSurf);
                float3 V = -toSurf / dist;

                float2 rest = i.wpos.xz - gerstner(i.wpos.xz, 0, VERTEX_WAVES).xz;
                float nearw = 1.0 - smoothstep(25.0, 140.0, dist);
                float2 s = gerstner_slope(rest, 0, VERTEX_WAVES).xy + gerstner_slope(rest, VERTEX_WAVES, WAVES).xy * (0.6 + 2.4 * nearw);
                float2 wd = normalize(_WaterDir + float2(1e-5, 0));
                float2 across = float2(-wd.y, wd.x);
                float chop = (0.08 + _Wind * 1.45) * _Detail;
                float2 r1 = ripple_map(i.wpos.xz / 6.5 + wd * _WaterTime * 0.045);
                float2 r2 = ripple_map(i.wpos.xz / 2.3 - across * _WaterTime * 0.07 + wd * _WaterTime * 0.03);
                s += (r1 * 0.26 * (0.4 + 0.6 * nearw) + r2 * 0.17 * nearw) * chop;
                if (_Rain > 0.01)
                    s += rain_slope(i.wpos.xz) * (1.0 - smoothstep(6.0, 25.0, dist));
                float3 n = normalize(float3(-s.x, 1.0, -s.y));

                float cosv = saturate(dot(n, V));
                float fresnel = 0.02 + 0.98 * pow(1.0 - cosv, 5.0);

                // water behind this point along the view, and how deep below the surface it is
                float2 suv = i.scr.xy / i.scr.w;
                float2 guv = i.grab.xy / i.grab.w;
                float3 rd = -V;
                float behind = scene_dist(suv, rd);
                float thickness = max(behind - dist, 0.0);
                float below = thickness * clamp(V.y, 0.05, 1.0);

                // refraction: the bottom seen shifted through the waves (not where the shifted point is in front)
                float3 nView = mul((float3x3)UNITY_MATRIX_V, n);
                float2 shift = nView.xy * 0.045 * saturate(thickness * 0.7) / (1.0 + dist * 0.03);
                float2 border = min(suv, 1.0 - suv);
                shift *= smoothstep(0.0, 0.07, min(border.x, border.y));
                float2 ruv = suv + shift, rguv = guv + shift;
                float rbehind = scene_dist(ruv, rd);
                if (rbehind < dist) { ruv = suv; rguv = guv; }
                else thickness = max(rbehind - dist, 0.0);
                float3 refracted = tex2Dlod(_WaterGrab, float4(rguv, 0, 0)).rgb;

                float3 extinction = _Extinction.rgb * (1.4 / max(_Clarity, 0.3));
                float3 through = exp(-thickness * extinction);
                float seen = (through.r + through.g + through.b) / 3.0;
                float2 muv = (i.wpos.xz - _MapRect.xy) / _MapRect.zw;
                float bed = tex2D(_DepthMap, muv).r * _MaxDepth;
                float3 body = lerp(_Shallow.rgb, _Deep.rgb, smoothstep(0.5, 6.0, max(bed, below)));

                // screen-space reflection of what stands above the water
                float3 reflected = 0;
                float hit = 0;
                float3 R = reflect(-V, n);
                if (_SsrSteps > 0 && fresnel > 0.03)
                {
                    float t = 0.4 + dist * 0.01;
                    for (int k = 0; k < 32; k++)
                    {
                        if (k >= (int)_SsrSteps) break;
                        float3 p = i.wpos + R * t;
                        float4 clip = UnityWorldToClipPos(p);
                        float4 sp4 = ComputeScreenPos(clip);
                        float2 sp = sp4.xy / sp4.w;
                        if (sp.x < 0 || sp.x > 1 || sp.y < 0 || sp.y > 1 || clip.w < 0) break;
                        float3 pr = p - cam;
                        float pd = length(pr);
                        float sd = scene_dist(sp, pr / pd);
                        float gap = pd - sd;
                        float sceneY = cam.y + (pr / pd).y * sd;
                        if (gap > 0.0 && gap < t * 0.35 + 0.6 && sceneY > i.wpos.y - 0.03)
                        {
                            float2 edge = smoothstep(0.0, 0.08, sp) * (1.0 - smoothstep(0.92, 1.0, sp));
                            hit = edge.x * edge.y * (1.0 - k / _SsrSteps);
                            float4 g4 = ComputeGrabScreenPos(clip);
                            reflected = tex2Dlod(_WaterGrab, float4(g4.xy / g4.w, 0, 0)).rgb;
                            break;
                        }
                        t *= 1.32;
                    }
                }
                // far reflection: the mirrored ray followed to whatever the screen shows in its direction
                if (_SsrSteps > 0 && fresnel > 0.02 && hit < 0.99)
                {
                    float D = 80.0;
                    float2 sp = -1;
                    float4 clip = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        float3 p = i.wpos + R * D;
                        clip = UnityWorldToClipPos(p);
                        float4 sp4 = ComputeScreenPos(clip);
                        sp = sp4.xy / sp4.w;
                        if (sp.x < 0 || sp.x > 1 || sp.y < 0 || sp.y > 1 || clip.w < 0) { sp = -1; break; }
                        float3 pr = normalize(p - cam);
                        D = clamp(scene_dist(sp, pr), 4.0, 4000.0);
                    }
                    if (sp.x >= 0)
                    {
                        float3 pr = normalize(i.wpos + R * D - cam);
                        float sceneY = cam.y + pr.y * scene_dist(sp, pr);
                        float above = smoothstep(i.wpos.y - 0.05, i.wpos.y + 0.4, sceneY);
                        float2 edge = smoothstep(0.0, 0.06, sp) * (1.0 - smoothstep(0.94, 1.0, sp));
                        float wfar = edge.x * edge.y * above * (1.0 - hit);
                        float4 g4 = ComputeGrabScreenPos(clip);
                        float3 farCol = tex2Dlod(_WaterGrab, float4(g4.xy / g4.w, 0, 1.5)).rgb;
                        reflected = lerp(reflected, farCol, wfar / max(hit + wfar, 0.0001));
                        hit = max(hit, wfar);
                    }
                }

                // foam at the shore, white caps in a strong wind
                float swash = water_height(i.wpos.xz) / max(_WaterAmp, 0.0001);
                float lap = 0.045 + 0.03 * swash * clamp(_WaterAmp * 12.0, 0.2, 1.0);
                float foamTex = tex2D(_FoamNoise, i.wpos.xz * 0.35 + _WaterTime * 0.02).r;
                float foamFine = tex2D(_FoamNoise, i.wpos.xz * 1.6 - float2(_WaterTime * 0.03, 0)).r;
                float foam = (1.0 - smoothstep(0.0, max(lap, 0.01), below)) * smoothstep(0.5, 0.8, foamTex) * smoothstep(0.35, 0.65, foamFine) * 0.6;
                foam *= step(0.004, below);
                float caps = smoothstep(0.55, 0.95, _Wind) * smoothstep(0.72, 0.95, i.crest)
                    * smoothstep(0.62, 0.8, tex2D(_FoamNoise, i.wpos.xz * 0.21 - wd * _WaterTime * 0.05).r) * nearw;
                foam = max(foam, caps * 0.8);
                float shore = smoothstep(0.0, 0.06, below);

                // lighting: the water's own colour lit by the sun and the sky, the refraction and the reflections
                float3 L = normalize(_SunDirection);
                float ndl = saturate(dot(n, L));
                float3 ambient = ShadeSH9(float4(n, 1));
                float transmit = 1.0 - fresnel * shore;
                float3 albedo = lerp(body * (1.0 - seen) * transmit * shore, float3(0.85, 0.88, 0.88), foam);
                float3 col = albedo * (_LightColor0.rgb * ndl + ambient);
                col += (refracted * lerp(1.0, through, shore) * transmit + reflected * fresnel * hit * shore) * (1.0 - foam);
                // the sky's reflection where nothing on screen is mirrored, and the sun's glint
                col += sky_colour(R) * fresnel * (1.0 - hit) * shore * (1.0 - foam);
                float rough = lerp(0.035 + _Wind * 0.06 + _Rain * 0.08, 0.8, foam);
                float3 H = normalize(L + V);
                float a2 = rough * rough * rough * rough;
                float ndh = saturate(dot(n, H));
                float dd = ndh * ndh * (a2 - 1.0) + 1.0;
                float ggx = a2 / (3.14159 * dd * dd + 1e-5);
                col += _LightColor0.rgb * ggx * fresnel * ndl * shore * (1.0 - foam) * 0.25;
                if (_WaterDebug > 0.5)
                {
                    if (_WaterDebug < 1.5) return float4(saturate(thickness / 10.0).xxx, 1);
                    if (_WaterDebug < 2.5) return float4(refracted, 1);
                    if (_WaterDebug < 3.5) return float4(fresnel.xxx, 1);
                    return float4(saturate(LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(suv, 0, 0))) / 50.0).xxx, 1);
                }
                UNITY_APPLY_FOG(i.fogCoord, col);
                return float4(col, 1.0);
            }
            ENDCG
        }
    }
}
