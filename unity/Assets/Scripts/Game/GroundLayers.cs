using System.IO;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The seven photographed ground layers of World/Props/GroundTextures (Poly Haven CC0 sets in
    /// StreamingAssets/Textures/&lt;id&gt;: diff, nor, disp): albedo with the height map in alpha and the normal map's
    /// tilt (RG) as two texture arrays, block compressed, at the Textures setting's size; plus each layer's mean colour
    /// (linear) for the map's tints.
    /// </summary>
    public static class GroundLayers
    {
        public const int Count = 7;
        public static readonly string[] Ids = { "leafy_grass", "forest_leaves_02", "coast_sand_03", "forest_ground_04", "dry_river_pebbles", "rock_face_04", "snow_02" };
        public static readonly float[] Roughness = { 0.8f, 0.85f, 0.9f, 0.88f, 0.55f, 0.75f, 0.5f };
        public const int Grass = 0, Forest = 1, Sand = 2, Path = 3, Bed = 4, Rock = 5, Snow = 6;

        public static Texture2DArray? Albedo { get; private set; }
        public static Texture2DArray? Normal { get; private set; }
        public static readonly Color[] Means = new Color[Count];
        private static int _builtSize;

        public static string Dir(string id) => System.IO.Path.Combine(Application.streamingAssetsPath, "Textures", id);

        private static Texture2D? Read(string id, string name, bool linear)
        {
            foreach (var ext in new[] { ".jpg", ".png" })
            {
                string path = System.IO.Path.Combine(Dir(id), name + ext);
                if (!File.Exists(path))
                    continue;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
                t.LoadImage(File.ReadAllBytes(path), false);
                return t;
            }
            return null;
        }

        private static Color32[] Pixels(Texture2D t, int size, bool srgb = false)
        {
            if (t.width == size && t.height == size)
                return t.GetPixels32();
            // resample through the GPU
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            Graphics.Blit(t, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(size, size, TextureFormat.RGBA32, false, !srgb);
            copy.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels32();
            Object.Destroy(copy);
            return px;
        }

        /// <summary>Builds the arrays (once per size); false when the sets are missing.</summary>
        public static bool Build(int size)
        {
            if (Albedo != null && _builtSize == size)
                return true;
            foreach (var id in Ids)
                if (!File.Exists(System.IO.Path.Combine(Dir(id), "diff.jpg")))
                    return false;
            var albedo = new Texture2DArray(size, size, Count, TextureFormat.DXT5, true, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "GroundAlbedo" };
            var normal = new Texture2DArray(size, size, Count, TextureFormat.DXT5, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "GroundNormal" };
            for (int layer = 0; layer < Count; layer++)
            {
                var diff = Read(Ids[layer], "diff", false)!;
                var disp = Read(Ids[layer], "disp", true);
                var nor = Read(Ids[layer], "nor", true);
                var a = Pixels(diff, size, true);
                var h = disp != null ? Pixels(disp, size) : null;
                double r = 0, g = 0, b = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    a[i].a = h != null ? h[i].r : (byte)((a[i].r + a[i].g * 2 + a[i].b) / 4);
                    var lin = ((Color)a[i]).linear;
                    r += lin.r; g += lin.g; b += lin.b;
                }
                Means[layer] = new Color((float)(r / a.Length), (float)(g / a.Length), (float)(b / a.Length));
                var at = new Texture2D(size, size, TextureFormat.RGBA32, true, false);
                at.SetPixels32(a);
                at.Apply(true);
                at.Compress(true);
                for (int m = 0; m < at.mipmapCount && m < albedo.mipmapCount; m++)
                    Graphics.CopyTexture(at, 0, m, albedo, layer, m);
                Color32[] np;
                if (nor != null)
                    np = Pixels(nor, size);
                else
                {
                    np = new Color32[size * size];
                    for (int i = 0; i < np.Length; i++) np[i] = new Color32(128, 128, 255, 255);
                }
                var nt = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
                nt.SetPixels32(np);
                nt.Apply(true);
                nt.Compress(true);
                for (int m = 0; m < nt.mipmapCount && m < normal.mipmapCount; m++)
                    Graphics.CopyTexture(nt, 0, m, normal, layer, m);
                Object.Destroy(diff);
                if (disp != null) Object.Destroy(disp);
                if (nor != null) Object.Destroy(nor);
                Object.Destroy(at);
                Object.Destroy(nt);
            }
            albedo.Apply(false, true);
            normal.Apply(false, true);
            if (Albedo != null) Object.Destroy(Albedo);
            if (Normal != null) Object.Destroy(Normal);
            Albedo = albedo;
            Normal = normal;
            _builtSize = size;
            return true;
        }

        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>LakeBed.ApplyGroundTints' Photo(): the map's brightness in full, its hue only by <paramref name="hue"/>.</summary>
        public static Vector4 Photo(Color mapColour, int layer, float hue)
        {
            var mean = Means[layer];
            var want = mapColour.linear;
            float k = Lum(want) / Mathf.Max(Lum(mean), 0.001f);
            var full = new Vector3(want.r / Mathf.Max(mean.r, 0.001f), want.g / Mathf.Max(mean.g, 0.001f), want.b / Mathf.Max(mean.b, 0.001f));
            var t = Vector3.Lerp(Vector3.one * k, full, hue);
            return new Vector4(t.x, t.y, t.z, 0f);
        }

        /// <summary>Meadows a touch calmer than the map's colour.</summary>
        public static Color Calm(Color c)
        {
            float l = c.grayscale;
            return Color.Lerp(new Color(l, l, l), c, 0.85f) * 0.92f;
        }
    }
}
