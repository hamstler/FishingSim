// The fishing line, the float's paint, glows: flat vertex colour x tint.
Shader "FS/Unlit"
{
    Properties { _Color ("Colour", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            float4 _Color;
            struct a2v { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; UNITY_FOG_COORDS(0) };
            v2f vert(a2v v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color * _Color; UNITY_TRANSFER_FOG(o, o.pos); return o; }
            float4 frag(v2f i) : SV_Target { float4 c = i.color; UNITY_APPLY_FOG(i.fogCoord, c); return c; }
            ENDCG
        }
    }
}
