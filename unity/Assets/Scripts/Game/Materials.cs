using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>The game's materials, made from the shaders in Resources/Shaders (included in every build).</summary>
    public static class Materials
    {
        private static Texture2D? _grain;

        private static Shader Find(string name) =>
            Shader.Find(name) ?? throw new System.InvalidOperationException($"Shader {name} missing from the build");

        /// <summary>A tileable grain picture (soil, bark, stone texture under the vertex colours), made once.</summary>
        public static Texture2D Grain
        {
            get
            {
                if (_grain != null)
                    return _grain;
                const int size = 256;
                var tex = new Texture2D(size, size, TextureFormat.R8, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "Grain" };
                var px = new byte[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float v = 0f, amp = 0.5f, sum = 0f;
                        for (int o = 0; o < 5; o++)
                        {
                            int period = 8 << o;
                            float fx = x / (float)size * period, fy = y / (float)size * period;
                            v += TileNoise(fx, fy, period, o) * amp;
                            sum += amp;
                            amp *= 0.55f;
                        }
                        px[y * size + x] = (byte)Mathf.Clamp(v / sum * 255f, 0f, 255f);
                    }
                tex.SetPixelData(px, 0);
                tex.Apply(true, true);
                return _grain = tex;
            }
        }

        private static float TileNoise(float x, float y, int period, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float H(int a, int b)
            {
                a = ((a % period) + period) % period;
                b = ((b % period) + period) % period;
                unchecked
                {
                    uint h = (uint)(a * 374761393) ^ (uint)(b * 668265263) ^ (uint)(seed * 2246822519u);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
                }
            }
            return Mathf.Lerp(Mathf.Lerp(H(ix, iy), H(ix + 1, iy), fx), Mathf.Lerp(H(ix, iy + 1), H(ix + 1, iy + 1), fx), fy);
        }

        public static Material Lit(float smoothness = 0.1f, float grain = 0.3f)
        {
            var m = new Material(Find("FS/Lit")) { name = "Lit" };
            m.SetTexture("_Grain", Grain);
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_GrainStrength", grain);
            m.SetFloat("_GrainScale", 0.8f);
            m.enableInstancing = true;
            return m;
        }

        public static Material Ground()
        {
            var m = Lit(0.08f, 0.45f);
            m.name = "Ground";
            m.SetFloat("_GrainScale", 0.45f);
            return m;
        }

        public static Material Foliage(bool twoSided)
        {
            var m = Lit(0.15f, 0.25f);
            m.name = twoSided ? "Grass" : "Foliage";
            m.SetFloat("_Wind", 1f);
            m.SetFloat("_GrainScale", 1.6f);
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            return m;
        }

        public static Material Flame() => new(Find("FS/Flame")) { name = "Flame" };
        public static Material Water() => new(Find("FS/Water")) { name = "Water" };
        public static Material Sky() => new(Find("FS/Sky")) { name = "Sky" };
        public static Material Unlit(Color c) => new(Find("FS/Unlit")) { name = "Unlit", color = c };
    }

    /// <summary>The lake's waves in C#: the same four sines as Water.shader, for the float and the boat.</summary>
    public static class Waves
    {
        public static float Amplitude = 0.06f;

        public static float HeightAt(float x, float z, float t)
        {
            float a = Amplitude;
            return a * 1.0f * Mathf.Sin(Dot(1f, 0.3f, x, z) * 0.7f + 1.2f * t)
                 + a * 0.6f * Mathf.Sin(Dot(0.6f, 1f, x, z) * 1.1f + 1.5f * t)
                 + a * 0.3f * Mathf.Sin(Dot(-0.4f, 1f, x, z) * 1.9f + 2.0f * t)
                 + a * 0.15f * Mathf.Sin(Dot(1f, -0.7f, x, z) * 3.1f + 2.6f * t);
        }

        private static float Dot(float dx, float dz, float x, float z)
        {
            float l = Mathf.Sqrt(dx * dx + dz * dz);
            return (dx * x + dz * z) / l;
        }
    }
}
