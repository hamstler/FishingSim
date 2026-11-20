// Meadow grass from photographed clumps (shaders/grass_card.gdshader): three crossed cards per clump, each instance one
// of the atlas's four cells (by place), cut out by alpha, tinted part way to the ground with the meadow's dry and lush
// patches, light through the blades, bending in the gusts.
Shader "FS/GrassCard"
{
    Properties
    {
        _MainTex ("Cards", 2D) = "white" {}
        _CellHeight ("Cell heights", Vector) = (0.5,0.5,0.5,0.5)
        _CellWidth ("Cell widths", Vector) = (1,1,1,1)
        _CardMean ("Atlas mean (linear)", Color) = (0.1,0.12,0.05,1)
        _Noise ("Noise", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard vertex:vert addshadow nolightmap nodynlightmap nodirlightmap nometa exclude_path:deferred exclude_path:prepass
        #pragma multi_compile_instancing
        #pragma target 3.5
        sampler2D _MainTex, _Noise;
        float4 _MainTex_TexelSize;
        float4 _CellHeight, _CellWidth, _CardMean;
        float _FoliageWind, _FoliageTime;
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
        UNITY_INSTANCING_BUFFER_END(Props)
        struct Input { float2 cellUv; float4 cellBox; float tip; float3 tintK; };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 origin = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
            float pick = frac(sin(dot(floor(origin.xz * 7.0), float2(12.9898, 78.233))) * 43758.5453);
            int cell = (int)min(pick * 4.0, 3.0);
            float h = _CellHeight[cell], w = _CellWidth[cell];
            // cells 0,1 in the atlas's upper half, 2,3 below (Unity's v runs up); the clump stands on its cell's bottom
            float2 corner = float2((cell % 2) * 0.5, 0.5 - (cell / 2) * 0.5);
            v.vertex.y *= h / 0.5;
            v.vertex.xz *= w;
            o.cellBox = float4(corner + float2(0.5 * (1.0 - w) * 0.5, 0.0), 0.5 * w, 0.5 * h);
            o.cellUv = v.texcoord.xy;
            o.tip = v.texcoord.y;
            float g = tex2Dlod(_Noise, float4(origin.xz * 0.02 + float2(_FoliageTime * 0.04, _FoliageTime * 0.015), 0, 0)).r;
            float bend = (0.12 + _FoliageWind * 0.8) * (0.3 + g) * o.tip * o.tip;
            float flutter = sin(_FoliageTime * 3.2 + origin.x * 1.7 + origin.z * 1.3 + v.vertex.x * 7.0) * 0.035 * o.tip * (0.3 + _FoliageWind);
            v.vertex.x += bend * 0.3 + flutter;
            v.vertex.z += bend * 0.18;
            v.vertex.y -= bend * 0.06;
            float3 base = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint).rgb;
            float macro = tex2Dlod(_Noise, float4(origin.xz * 0.013, 0, 0)).r;
            float mid = tex2Dlod(_Noise, float4(origin.xz * 0.09 + float2(0.37, 0.11), 0, 0)).r;
            float fine = tex2Dlod(_Noise, float4(origin.xz * 0.43 + float2(0.71, 0.29), 0, 0)).r;
            float finer = tex2Dlod(_Noise, float4(origin.xz * 1.7 + float2(0.13, 0.57), 0, 0)).r;
            float dry = smoothstep(0.52, 0.78, macro * 0.6 + fine * 0.25 + finer * 0.15);
            float lush = smoothstep(0.55, 0.8, (1.0 - macro) * 0.55 + mid * 0.45);
            float3 k = lerp(1.0, base / max(_CardMean.rgb, 0.002), 0.3);
            k *= lerp(0.88, 1.08, macro);
            k = lerp(k, k * float3(1.16, 1.02, 0.7), dry * 0.55);
            k = lerp(k, k * float3(0.8, 0.9, 0.76), lush * 0.45);
            o.tintK = clamp(k, 0.3, 2.2);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = IN.cellBox.xy + IN.cellUv * IN.cellBox.zw;
            float4 c = tex2D(_MainTex, uv);
            float2 dx = ddx(uv * _MainTex_TexelSize.zw), dy = ddy(uv * _MainTex_TexelSize.zw);
            float lod = max(0.5 * log2(max(dot(dx, dx), dot(dy, dy))), 0.0);
            clip(saturate(c.a * (1.0 + lod * 0.22)) - 0.5);
            float t = smoothstep(0.0, 1.0, IN.tip);
            o.Albedo = c.rgb * IN.tintK;
            o.Emission = c.rgb * IN.tintK * 0.06 * t;
            o.Occlusion = lerp(0.55, 1.0, t);
            o.Smoothness = 0.4;
        }
        ENDCG
    }
}
