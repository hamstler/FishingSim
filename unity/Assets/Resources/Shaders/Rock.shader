// Boulders (shaders/rock.gdshader): the terrain's photographed rock layer, triplanar in world space, tinted per stone,
// moss on the faces that look up, thicker in the hollows.
Shader "FS/Rock"
{
    Properties
    {
        _Noise ("Noise", 2D) = "gray" {}
        _RockMean ("Rock mean (linear)", Color) = (0.15,0.15,0.15,1)
        _MossTint ("Moss", Color) = (0.22,0.32,0.1,1)
        _Moss ("Moss amount", Float) = 0.7
        _Tile ("Tile", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard vertex:vert addshadow fullforwardshadows nolightmap nodynlightmap nodirlightmap nometa exclude_path:deferred exclude_path:prepass
        #pragma multi_compile_instancing
        #pragma target 3.5
        #pragma require 2darray
        UNITY_DECLARE_TEX2DARRAY(_GroundAlb);
        UNITY_DECLARE_TEX2DARRAY(_GroundNrm);
        sampler2D _Noise;
        float4 _RockMean, _MossTint;
        float _Moss, _Tile;
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
        UNITY_INSTANCING_BUFFER_END(Props)
        struct Input { float3 wp; float3 wn; float3 wt; };
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.wn = UnityObjectToWorldNormal(v.normal);
            o.wt = UnityObjectToWorldDir(v.tangent.xyz);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 n = normalize(IN.wn);
            float3 tw = pow(abs(n), 4.0);
            tw /= (tw.x + tw.y + tw.z);
            float t = _Tile * 0.8;
            float2 uy = IN.wp.xz / t, ux = IN.wp.zy / t, uz = IN.wp.xy / t;
            float4 a = UNITY_SAMPLE_TEX2DARRAY(_GroundAlb, float3(uy, 5)) * tw.y + UNITY_SAMPLE_TEX2DARRAY(_GroundAlb, float3(ux, 5)) * tw.x + UNITY_SAMPLE_TEX2DARRAY(_GroundAlb, float3(uz, 5)) * tw.z;
            float4 qy = UNITY_SAMPLE_TEX2DARRAY(_GroundNrm, float3(uy, 5)), qx = UNITY_SAMPLE_TEX2DARRAY(_GroundNrm, float3(ux, 5)), qz = UNITY_SAMPLE_TEX2DARRAY(_GroundNrm, float3(uz, 5));
            float3 tilt = float3(qy.r * 2 - 1, 0, qy.g * 2 - 1) * tw.y + float3(0, qx.g * 2 - 1, qx.r * 2 - 1) * tw.x + float3(qz.r * 2 - 1, qz.g * 2 - 1, 0) * tw.z;
            float3 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint).rgb;
            float3 col = a.rgb * tint / max(_RockMean.rgb, 0.01);
            float nz = tex2D(_Noise, IN.wp.xz * 0.35 + IN.wp.y * 0.21).r;
            float m = smoothstep(0.45, 0.8, n.y + (nz - 0.5) * 0.7 + (0.5 - a.a) * 0.4) * _Moss;
            float3 mossc = _MossTint.rgb * (0.7 + 0.6 * nz) * lerp(0.8, 1.2, a.a);
            col = lerp(col, mossc, m);
            float3 pn = normalize(n + tilt * lerp(1.2, 0.4, m));
            float3 T = normalize(IN.wt - n * dot(IN.wt, n));
            float3 B = cross(n, T);
            o.Normal = normalize(float3(dot(pn, T), dot(pn, B), dot(pn, n)));
            o.Albedo = saturate(col);
            o.Smoothness = 1.0 - lerp(0.75, 0.95, m);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
