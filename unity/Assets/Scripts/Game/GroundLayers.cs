using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The seven photographed ground layers of World/Props/GroundTextures (Poly Haven CC0 sets in
    /// StreamingAssets/Textures/&lt;id&gt;: diff, nor, disp): albedo with the height map in alpha and the normal map's
    /// tilt (RG) as two texture arrays, block compressed, at the Textures setting's size; plus each layer's mean colour
    /// (linear) for the map's tints. Made once and kept in <see cref="TexCache"/> (the compression on worker threads),
    /// so later starts only read them back; <see cref="Prepare"/> runs while the login screen shows.
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
        private static bool _building;

        /// <summary>How far <see cref="Prepare"/> is (0..1), for the loading screen.</summary>
        public static float Progress { get; private set; }

        public static string Dir(string id) => System.IO.Path.Combine(Application.streamingAssetsPath, "Textures", id);

        /// <summary>The sets are there (else the terrain keeps its painted colours).</summary>
        public static bool Available()
        {
            foreach (var id in Ids)
                if (TexCache.Find(System.IO.Path.Combine(Dir(id), "diff")) == null)
                    return false;
            return true;
        }

        public static bool Ready(int size) => Albedo != null && _builtSize == size;

        /// <summary>The Textures setting: Low 1K, Medium 2K, High and up 4K (as in Godot).</summary>
        public static int SizeForQuality => Settings.Quality <= 1 ? 1024 : Settings.Quality == 2 ? 2048 : 4096;

        private static string Key(string id, int size, string part) =>
            $"ground_{id}_{size}_{part}_{TexCache.Stamp(TexCache.Find(System.IO.Path.Combine(Dir(id), "diff")) ?? "")}_{TexCache.Stamp(TexCache.Find(System.IO.Path.Combine(Dir(id), "nor")) ?? "")}_{TexCache.Stamp(TexCache.Find(System.IO.Path.Combine(Dir(id), "disp")) ?? "")}";

        /// <summary>Builds the arrays for <paramref name="size"/> over several frames (a coroutine); a second caller waits for the first.</summary>
        public static IEnumerator Prepare(int size)
        {
            while (_building)
                yield return null;
            if (Ready(size) || !Available())
            {
                Progress = 1f;
                yield break;
            }
            _building = true;
            Progress = 0f;
            LoadLog.Begin($"ground layers {size}");
            var albedo = new Texture2DArray(size, size, Count, TextureFormat.DXT5, true, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "GroundAlbedo" };
            var normal = new Texture2DArray(size, size, Count, TextureFormat.DXT5, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "GroundNormal" };
            for (int layer = 0; layer < Count; layer++)
            {
                string id = Ids[layer];
                string keyA = Key(id, size, "a"), keyN = Key(id, size, "n");
                var read = Task.Run(() => (TexCache.Read(keyA), TexCache.Read(keyN)));
                while (!read.IsCompleted)
                    yield return null;
                var (ea, en) = read.Result;
                if (ea == null || en == null || ea.Format != TextureFormat.DXT5 || en.Format != TextureFormat.DXT5 || ea.Width != size || en.Width != size)
                {
                    // not cached yet: decode the pictures here (Unity's decoder is the main thread's), the rest on workers
                    string dir = Dir(id);
                    var a = TexCache.Decode(TexCache.Find(System.IO.Path.Combine(dir, "diff"))!, size, false, out int aw, out int ah);
                    if (a == null || aw != size || ah != size)
                        a = Fit(a, aw, ah, size, false);   // a set smaller than the array: scaled up through the GPU
                    yield return null;
                    string? dispFile = TexCache.Find(System.IO.Path.Combine(dir, "disp"));
                    Color32[]? h = null;
                    if (dispFile != null)
                    {
                        h = TexCache.Decode(dispFile, size, true, out int hw, out int hh0);
                        if (h != null && (hw != size || hh0 != size))
                            h = Fit(h, hw, hh0, size, true);
                    }
                    yield return null;
                    string? norFile = TexCache.Find(System.IO.Path.Combine(dir, "nor"));
                    Color32[]? n = null;
                    if (norFile != null)
                    {
                        n = TexCache.Decode(norFile, size, true, out int nw, out int nh);
                        if (n != null && (nw != size || nh != size))
                            n = Fit(n, nw, nh, size, true);
                    }
                    yield return null;
                    var hh = h != null && h.Length == a.Length ? h : null;
                    var nn = n != null && n.Length == a.Length ? n : null;
                    var px = a;
                    var work = Task.Run(() =>
                    {
                        double r = 0, g = 0, b = 0;
                        for (int i = 0; i < px.Length; i++)
                        {
                            px[i].a = hh != null ? hh[i].r : (byte)((px[i].r + px[i].g * 2 + px[i].b) / 4);
                            r += Linear[px[i].r]; g += Linear[px[i].g]; b += Linear[px[i].b];
                        }
                        var mean = new[] { (float)(r / px.Length), (float)(g / px.Length), (float)(b / px.Length) };
                        var A = TexCache.Encode(px, size, size, false, true, mean);
                        if (nn == null)
                        {
                            nn = new Color32[size * size];
                            for (int i = 0; i < nn.Length; i++)
                                nn[i] = new Color32(128, 128, 255, 255);
                        }
                        var N = TexCache.Encode(nn, size, size, true, true);
                        TexCache.Write(keyA, A);
                        TexCache.Write(keyN, N);
                        return (A, N);
                    });
                    while (!work.IsCompleted)
                        yield return null;
                    if (work.Exception != null)
                    {
                        Debug.LogException(work.Exception);
                        _building = false;
                        yield break;
                    }
                    (ea, en) = work.Result;
                }
                Means[layer] = ea.Extra.Length >= 3 ? new Color(ea.Extra[0], ea.Extra[1], ea.Extra[2]) : Color.gray;
                TexCache.ToArray(albedo, layer, ea);
                TexCache.ToArray(normal, layer, en);
                Progress = (layer + 1f) / Count;
                yield return null;
            }
            albedo.Apply(false, true);
            normal.Apply(false, true);
            if (Albedo != null) Object.Destroy(Albedo);
            if (Normal != null) Object.Destroy(Normal);
            Albedo = albedo;
            Normal = normal;
            _builtSize = size;
            _building = false;
            Progress = 1f;
            LoadLog.End($"ground layers {size}");
        }

        /// <summary>Scales texels to size x size through the GPU (a set smaller than the array).</summary>
        private static Color32[] Fit(Color32[]? px, int w, int h, int size, bool linear)
        {
            if (px == null)
            {
                var grey = new Color32[size * size];
                for (int i = 0; i < grey.Length; i++)
                    grey[i] = new Color32(128, 128, 128, 255);
                return grey;
            }
            var src = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
            src.SetPixels32(px);
            src.Apply(false, false);
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(size, size, TextureFormat.RGBA32, false, linear);
            copy.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            copy.Apply(false, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var result = copy.GetPixels32();
            Object.Destroy(src);
            Object.Destroy(copy);
            return result;
        }

        /// <summary>sRGB byte to linear (worker threads: Unity's Color.linear is the engine's own code).</summary>
        private static readonly float[] Linear = MakeLinear();

        private static float[] MakeLinear()
        {
            var t = new float[256];
            for (int i = 0; i < 256; i++)
            {
                double c = i / 255.0;
                t[i] = (float)(c <= 0.04045 ? c / 12.92 : System.Math.Pow((c + 0.055) / 1.055, 2.4));
            }
            return t;
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
