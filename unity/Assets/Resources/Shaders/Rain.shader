// Rain (shaders/rain.gdshader): one mesh of thin quads, each a drop; the vertex shader makes them fall, wraps them in
// a box round the camera (anchored in the world) and turns each to face the camera about the vertical axis.
// uv = the quad's corner (-0.5..0.5 across, -0.5..0.5 up), uv2 = the drop's place in the box (0..1), uv2.z hides drops
// beyond _Shown (the rain's strength).
Shader "FS/Rain"
{
    Properties
    {
        _Area ("Area", Vector) = (44,14,44,0)
        _Speed ("Speed", Float) = 18
        _Slant ("Slant", Float) = 0.12
        _Tint ("Tint", Color) = (0.75,0.82,0.9,0.45)
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
            float _Speed, _Slant, _Shown;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 uv2 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert(appdata v)
            {
                v2f o;
                float3 anchor = _WorldSpaceCameraPos;
                anchor.y = 0;
                float fall = _Time.y * _Speed;
                float3 base = v.uv2.xyz * _Area.xyz;
                float3 p;
                p.x = fmod(fmod(base.x + _Slant * fall - anchor.x, _Area.x) + _Area.x, _Area.x) - _Area.x * 0.5;
                p.y = fmod(fmod(base.y - fall, _Area.y) + _Area.y, _Area.y) - 1.0;
                p.z = fmod(fmod(base.z - anchor.z, _Area.z) + _Area.z, _Area.z) - _Area.z * 0.5;
                p += anchor;
                float2 toCam = _WorldSpaceCameraPos.xz - p.xz;
                toCam = length(toCam) > 0.001 ? normalize(toCam) : float2(0, 1);
                float3 right = float3(toCam.y, 0, -toCam.x);
                float3 w = p + right * v.uv.x * 0.02 + float3(_Slant * v.uv.y * 0.45, v.uv.y * 0.45, 0);
                if (v.uv2.w > _Shown) w = float3(0, -1000, 0);
                o.pos = UnityWorldToClipPos(w);
                return o;
            }
            float4 frag(v2f i) : SV_Target { return _Tint; }
            ENDCG
        }
    }
}
