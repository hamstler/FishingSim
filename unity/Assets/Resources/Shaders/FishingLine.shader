// The fishing line (Godot: shaders/fishing_line.gdshader): a camera-facing ribbon widened in the vertex shader to the
// line's real thickness but never under about a pixel (a 0.16 mm line 20 m away still reads as a hair, faded by how much
// of the pixel it really covers), unshaded colour dimmed by the daylight, and a sun glint along the line (the ribbon's
// normal is the line's own tangent: Kajiya-Kay, sin of the tangent to the half vector).
// Vertices: POSITION = the point on the line (twice), NORMAL = the line's tangent there, TEXCOORD0.x = side (-1 / +1),
// TEXCOORD0.y = metres along the line (braid bands), COLOR = the line's colour (tension tint included).
Shader "FS/FishingLine"
{
    Properties
    {
        _Diameter ("Diameter (m)", Float) = 0.00016
        _MinPixels ("Least width (px)", Float) = 1.0
        _Bands ("Braid bands (0 = one colour)", Float) = 0
        _BandColor ("Band colour", Color) = (0.1,0.1,0.1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            float _Diameter, _MinPixels, _Bands;
            float4 _BandColor;
            float _Daylight;
            float4 _SunDirection, _SunTint;

            struct a2v { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float3 tangent : TEXCOORD0;
                float3 view : TEXCOORD1;
                float2 cover : TEXCOORD2;       // x = the line's strength (how much of a pixel it covers), y = metres along
                float2 across : TEXCOORD4;      // x = -1..1 across the strip, y = the strip's width in pixels
                UNITY_FOG_COORDS(3)
            };

            v2f vert(a2v v)
            {
                v2f o;
                float3 p = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 t = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                float3 toCam = _WorldSpaceCameraPos - p;
                float dist = length(toCam);
                float3 side = cross(t, toCam / max(dist, 1e-4));
                side = dot(side, side) > 1e-8 ? normalize(side) : float3(0, 1, 0);
                // one pixel in metres at this distance (the projection's vertical scale and the screen's height)
                float pixel = dist * 2.0 / (unity_CameraProjection[1][1] * _ScreenParams.y);
                // anti-aliased: the strip is drawn at least 2.5 px wide and fades from its middle to its edges (a hard
                // one-pixel strip broke into stair-steps and dashes that crawled along the line as it moved)
                float realPx = _Diameter / pixel;
                float widthPx = max(realPx + 1.5, 2.5 * _MinPixels);
                float width = widthPx * pixel;
                p += side * v.uv.x * width * 0.5;
                o.pos = mul(UNITY_MATRIX_VP, float4(p, 1));
                o.color = v.color;
                o.tangent = t;
                o.view = toCam / max(dist, 1e-4);
                // a hair-thin line far away keeps at least 45 % of a pixel's strength (it reads as a line, not a ghost)
                o.cover = float2(saturate(0.45 + 0.55 * realPx), v.uv.y);
                o.across = float2(v.uv.x, widthPx);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 col = i.color.rgb;
                if (_Bands > 0)
                    col = lerp(col, _BandColor.rgb, step(0.5, frac(i.cover.y / _Bands)));
                // unshaded, dimmed after dark (a white line glowing at night read as a lit wire)
                col *= 0.22 + 0.78 * _Daylight;
                // the sun's glint along the line
                float3 t = normalize(i.tangent);
                float3 h = normalize(normalize(_SunDirection.xyz) + normalize(i.view));
                float th = dot(t, h);
                float glint = pow(saturate(sqrt(saturate(1.0 - th * th))), 90.0) * _Daylight;
                col += _SunTint.rgb * glint * 0.8;
                // the profile across the strip: solid over the line's own width, a one-pixel soft edge
                float d = abs(i.across.x) * i.across.y * 0.5;                 // pixels from the middle
                float core = max(0.5 * (i.across.y - 1.5), 0.5);
                float profile = saturate(core + 0.5 - d);
                float4 c = float4(col, i.color.a * i.cover.x * profile);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
