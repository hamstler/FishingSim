// Photographed CC0 materials of buildings and props, weathered (shaders/building.gdshaderinc): triplanar in object space
// at the set's real size (or by the mesh's UVs in metres), a large-scale colour wander, rain streaks down vertical faces,
// rising damp and splashed dirt at the foot measured from _BaseY, moss and lichen on faces turned up.
Shader "FS/Weathered"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _NormalTex ("Normal (linear)", 2D) = "bump" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Rough ("Roughness", Float) = 0.85
        _NormalStrength ("Normal strength", Float) = 1.1
        _UVScale ("1 / set size (m)", Float) = 0.5
        _Triplanar ("Triplanar", Float) = 1
        _Weathering ("Weathering", Float) = 1
        _Moss ("Moss", Float) = 0.4
        _DampHeight ("Damp band (m)", Float) = 0.7
        _BaseY ("Ground height", Float) = -10000
        _Cull ("Cull", Float) = 2
        _VertexColour ("Colour from the mesh (photo for detail)", Float) = 0
        _Mean ("Photo mean (linear)", Color) = (0.5,0.5,0.5,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf StandardSpecular vertex:vert addshadow fullforwardshadows nolightmap nodynlightmap nodirlightmap nometa exclude_path:deferred exclude_path:prepass
        #pragma target 3.5
        sampler2D _MainTex, _NormalTex;
        float4 _Tint;
        float _Rough, _NormalStrength, _UVScale, _Triplanar, _Weathering, _Moss, _DampHeight, _BaseY, _VertexColour;
        float4 _Mean;

        struct Input { float4 color : COLOR; float3 lpos; float3 lnrm; float3 wpos; float3 wN; float3 wT; float2 uv; float face : VFACE; };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            // a tangent from the normal (the built meshes carry none): the frame o.Normal is given in below
            float3 n = normalize(v.normal);
            float3 t = normalize(cross(n, abs(n.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0)));
            v.tangent = float4(t, 1);
            o.lpos = v.vertex.xyz;
            o.lnrm = n;
            o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.wN = UnityObjectToWorldNormal(n);
            o.wT = UnityObjectToWorldDir(t);
            o.uv = v.texcoord.xy;
        }

        float hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
        float noise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
        }
        float fbm(float2 p) { return noise(p) * 0.55 + noise(p * 2.13 + 7.1) * 0.3 + noise(p * 4.37 + 3.3) * 0.15; }
        float3 unpackN(float4 t) { float3 n = t.xyz * 2.0 - 1.0; n.xy *= _NormalStrength; return n; }

        void surf(Input IN, inout SurfaceOutputStandardSpecular o)
        {
            float flip = IN.face < 0 ? -1.0 : 1.0;
            float3 ln = normalize(IN.lnrm) * flip;
            float3 col;
            float3 wn;
            float3 wgeo = normalize(IN.wN) * flip;
            if (_Triplanar > 0.5)
            {
                float3 w = pow(abs(ln), 5.0);
                w /= (w.x + w.y + w.z);
                float3 p = IN.lpos * _UVScale;
                // Godot's v runs down, LoadImage's up: the same picture orientation with v negated
                float2 uvx = float2(p.z * sign(ln.x), p.y);
                float2 uvy = float2(p.x, -p.z * sign(ln.y));
                float2 uvz = float2(-p.x * sign(ln.z), p.y);
                col = 0;
                float3 nobj = 0;
                if (w.x > 0.01) { col += tex2D(_MainTex, uvx).rgb * w.x; float3 t = unpackN(tex2D(_NormalTex, uvx)); nobj += float3(t.z * sign(ln.x), t.y, t.x * sign(ln.x)) * w.x; }
                if (w.y > 0.01) { col += tex2D(_MainTex, uvy).rgb * w.y; float3 t = unpackN(tex2D(_NormalTex, uvy)); nobj += float3(t.x, t.z * sign(ln.y), -t.y * sign(ln.y)) * w.y; }
                if (w.z > 0.01) { col += tex2D(_MainTex, uvz).rgb * w.z; float3 t = unpackN(tex2D(_NormalTex, uvz)); nobj += float3(-t.x * sign(ln.z), t.y, t.z * sign(ln.z)) * w.z; }
                nobj = normalize(ln + (nobj - ln * dot(nobj, ln)));
                wn = normalize(mul((float3x3)unity_ObjectToWorld, nobj));
            }
            else
            {
                float2 uv = IN.uv * _UVScale;
                col = tex2D(_MainTex, uv).rgb;
                float3 t = unpackN(tex2D(_NormalTex, uv));
                float3 T = normalize(IN.wT - wgeo * dot(IN.wT, wgeo));
                float3 B = cross(wgeo, T);
                wn = normalize(T * t.x + B * t.y + wgeo * t.z);
            }
            if (_VertexColour > 0.5)
                col = IN.color.rgb * saturate(col / max(_Mean.rgb, 0.02) * 0.85 + 0.15);   // the mesh's colour, the photo's grain
            col *= _Tint.rgb;
            float rough = _Rough;

            // colour wander over metres
            float big = fbm(IN.wpos.xz * 0.11 + IN.wpos.y * 0.07);
            float mid = fbm(float2(IN.wpos.x + IN.wpos.z, IN.wpos.y) * 0.55);
            col *= lerp(1.0, lerp(0.86, 1.08, big) * lerp(float3(0.97, 0.99, 1.03), float3(1.04, 1.0, 0.94), mid), _Weathering);

            // rain streaks down vertical faces
            float vertical = 1.0 - abs(wgeo.y);
            float along = dot(IN.wpos.xz, normalize(float2(-wgeo.z, wgeo.x) + 1e-4));
            float streak = smoothstep(0.55, 0.85, noise(float2(along * 6.0, IN.wpos.y * 0.35)) * 0.7 + noise(float2(along * 1.3, 3.1)) * 0.5);
            col *= 1.0 - 0.16 * streak * vertical * _Weathering;

            // damp and splashed dirt at the foot
            float h = IN.wpos.y - _BaseY;
            if (h > -2.0 && h < _DampHeight * 2.5)
            {
                float edge = _DampHeight * (0.7 + 0.6 * fbm(float2(along, IN.wpos.y) * 1.3 + IN.wpos.xz * 0.2));
                float damp = (1.0 - smoothstep(edge * 0.35, edge, h)) * _Weathering * vertical;
                col = lerp(col, col * float3(0.66, 0.62, 0.55), damp * 0.85);
                float splash = (1.0 - smoothstep(0.0, 0.22, h)) * smoothstep(0.35, 0.7, noise(float2(along, IN.wpos.y) * 9.0));
                col = lerp(col, float3(0.3, 0.26, 0.2), splash * 0.45 * _Weathering * vertical);
                rough = lerp(rough, min(rough + 0.08, 1.0), damp);
            }

            // moss and lichen on faces turned up
            float up = smoothstep(0.35, 0.8, wgeo.y);
            float patch = smoothstep(0.5, 0.75, fbm(IN.wpos.xz * 0.9 + 11.0) + noise(IN.wpos.xz * 7.0) * 0.25);
            float mossy = saturate(up * patch * _Moss);
            float3 mossCol = lerp(float3(0.22, 0.27, 0.1), float3(0.36, 0.4, 0.16), noise(IN.wpos.xz * 13.0));
            col = lerp(col, mossCol * (0.75 + 0.5 * dot(col, 0.333)), mossy * 0.75);
            rough = lerp(rough, 0.95, mossy);

            // the world normal into the frame set in vert()
            float3 Tw = normalize(IN.wT - wgeo * dot(IN.wT, wgeo));
            float3 Bw = cross(wgeo, Tw);
            o.Normal = normalize(float3(dot(wn, Tw), dot(wn, Bw), dot(wn, wgeo)));
            o.Albedo = col;
            o.Specular = 0.16 * 0.35 * 0.35;     // Godot SPECULAR 0.35
            o.Smoothness = 1.0 - rough;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
