// Rain (shaders/rain.gdshader): one mesh of thin quads, each a drop; the vertex shader makes them fall, wraps them in
// a box round the camera (anchored in the world) and turns each to face the camera about the vertical axis.
// uv = the quad's corner (-0.5..0.5 across, -0.5..0.5 up), uv2 = the drop's place in the box (0..1), uv2.w hides drops
// beyond _Shown (the rain's strength).
// A drop is a streak (its fall during a camera's exposure) a few millimetres thick: never thinner than about a pixel,
// a far drop is wider but fainter by how little of the pixel it covers; drops right at the camera fade out (they
// covered the screen with white bars), and the streaks take the colour of the sky's light (the fog's colour, brighter),
// so they are grey under a grey sky and hardly seen at night.
Shader "FS/Rain"
{
    Properties
    {
        _Area ("Area", Vector) = (40,14,40,0)
        _Speed ("Speed", Float) = 9
        _Slant ("Slant", Float) = 0.12
        _Length ("Streak length", Float) = 0.32
        _Tint ("Tint", Color) = (1,1,1,0.32)
        _Shown ("Shown", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Area, _Tint;
            float _Speed, _Slant, _Shown, _Length;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 uv2 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float fade : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                float3 anchor = _WorldSpaceCameraPos;
                anchor.y = 0;
                // each drop falls at its own speed (8-10 m/s)
                float fall = _Time.y * _Speed * (0.88 + 0.24 * frac(v.uv2.w * 7.13));
                float3 base = v.uv2.xyz * _Area.xyz;
                float3 p;
                p.x = fmod(fmod(base.x + _Slant * fall - anchor.x, _Area.x) + _Area.x, _Area.x) - _Area.x * 0.5;
                p.y = fmod(fmod(base.y - fall, _Area.y) + _Area.y, _Area.y) - 1.0;
                p.z = fmod(fmod(base.z - anchor.z, _Area.z) + _Area.z, _Area.z) - _Area.z * 0.5;
                p += anchor;
                float2 toCam = _WorldSpaceCameraPos.xz - p.xz;
                toCam = length(toCam) > 0.001 ? normalize(toCam) : float2(0, 1);
                float3 right = float3(toCam.y, 0, -toCam.x);
                float dist = distance(p, _WorldSpaceCameraPos);
                float pixel = 2.0 * dist / (max(unity_CameraProjection._m11, 0.1) * _ScreenParams.y);
                float width = max(0.003, pixel * 1.15);
                float coverage = 0.003 / width;
                float3 w = p + right * v.uv.x * width + float3(_Slant * v.uv.y * _Length, v.uv.y * _Length, 0);
                o.fade = smoothstep(0.7, 2.6, dist) * (1.0 - smoothstep(13.0, 20.0, dist)) * lerp(0.3, 1.0, coverage);
                if (v.uv2.w > _Shown)
                    w = float3(0, -1000, 0);
                o.pos = UnityWorldToClipPos(w);
                o.uv = v.uv;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float across = saturate(1.0 - abs(i.uv.x * 2.0));
                float along = smoothstep(0.5, 0.12, abs(i.uv.y));
                float3 light = unity_FogColor.rgb * 1.7 + 0.03;
                return float4(light * _Tint.rgb, _Tint.a * i.fade * across * along);
            }
            ENDCG
        }
    }
}
