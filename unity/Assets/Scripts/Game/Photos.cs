using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The photographed CC0 sets (assets/textures/&lt;id&gt;: diff, nor, info.json with the real size), read from
    /// StreamingAssets/Textures and made into materials like BuildingFactory.Photo / Weathered in the Godot game.
    /// </summary>
    public static class Photos
    {
        private static readonly Dictionary<string, Texture2D?> Pictures = new();
        private static readonly Dictionary<string, Material?> Mats = new();

        /// <summary>The Textures setting: Low 1K, Medium 2K, High and up 4K.</summary>
        public static int Limit => 1024 << Settings.Textures;

        public static Texture2D? Picture(string id, string name, bool linear)
        {
            string key = $"{id}/{name}/{linear}/{Limit}";
            if (Pictures.TryGetValue(key, out var t))
                return t;
            // compressed on worker threads and kept in TexCache (BC1: the shader reads only the colour)
            string? file = TexCache.Find(Path.Combine(GroundLayers.Dir(id), name), ".png", ".jpg");
            t = file != null ? TexCache.Picture(file, Limit, linear, false, key, TextureWrapMode.Repeat, 16) : null;
            return Pictures[key] = t;
        }

        /// <summary>The sets' pictures made ahead over several frames (the login screen): diff and nor of each.</summary>
        public static System.Collections.IEnumerator Prewarm(params string[] ids)
        {
            foreach (var id in ids)
                foreach (var (name, linear) in new[] { ("diff", false), ("nor", true) })
                {
                    string key = $"{id}/{name}/{linear}/{Limit}";
                    if (Pictures.ContainsKey(key))
                        continue;
                    string? file = TexCache.Find(Path.Combine(GroundLayers.Dir(id), name), ".png", ".jpg");
                    if (file == null)
                    {
                        Pictures[key] = null;
                        continue;
                    }
                    Texture2D? made = null;
                    yield return TexCache.PictureAsync(file, Limit, linear, false, key, t => made = t, TextureWrapMode.Repeat, 16);
                    if (!Pictures.ContainsKey(key))
                        Pictures[key] = made;
                    else if (made != null)
                        Object.Destroy(made);
                }
        }

        /// <summary>A picture's average colour (linear), from a 16 px copy.</summary>
        public static Color Mean(Texture t)
        {
            var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(t, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var small = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var sum = Color.black;
            foreach (var p in small.GetPixels())
                sum += p.linear;
            Object.Destroy(small);
            return sum / 256f;
        }

        /// <summary>The set's real size in metres (info.json "metres"), 2 m without it.</summary>
        public static float Metres(string id)
        {
            string info = Path.Combine(GroundLayers.Dir(id), "info.json");
            if (!File.Exists(info))
                return 2f;
            try
            {
                var j = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(info));
                if (j["metres"] is Newtonsoft.Json.Linq.JArray m && m.Count > 0)
                    return Mathf.Max((float)m[0], 0.1f);
            }
            catch (System.Exception)
            {
            }
            return 2f;
        }

        /// <summary>
        /// BuildingFactory.Weathered: the set at its real size (x <paramref name="scale"/>), tinted, weathered (colour
        /// wander, rain streaks, damp foot from the object's ground, moss on faces turned up); null without the set.
        /// </summary>
        public static Material? Weathered(string id, Color tint, float roughness = 0.85f, bool triplanar = true, float scale = 1f,
            float weathering = 1f, float moss = 0.4f, bool twoSided = false, float normal = 1.1f, bool vertexColour = false)
        {
            string key = $"{id}|{tint}|{roughness}|{triplanar}|{scale}|{weathering}|{moss}|{twoSided}|{normal}|{vertexColour}|{Limit}";
            if (Mats.TryGetValue(key, out var m))
                return m;
            var albedo = Picture(id, "diff", false);
            m = null;
            if (albedo != null)
            {
                m = new Material(Shader.Find("FS/Weathered")) { name = "Weathered " + id };
                m.SetTexture("_MainTex", albedo);
                var nor = Picture(id, "nor", true);
                if (nor != null)
                    m.SetTexture("_NormalTex", nor);
                m.SetFloat("_NormalStrength", nor != null ? normal : 0f);
                m.SetColor("_Tint", tint);
                m.SetFloat("_Rough", roughness);
                m.SetFloat("_UVScale", 1f / (Metres(id) * scale));
                m.SetFloat("_Triplanar", triplanar ? 1f : 0f);
                m.SetFloat("_Weathering", weathering);
                m.SetFloat("_Moss", moss);
                m.SetFloat("_Cull", twoSided ? 0f : 2f);
                if (vertexColour)
                {
                    m.SetFloat("_VertexColour", 1f);
                    m.SetVector("_Mean", Mean(albedo));   // already linear (SetColor would linearise it again)
                }
            }
            return Mats[key] = m;
        }
    }
}
