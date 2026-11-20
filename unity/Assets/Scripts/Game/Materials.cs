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

    /// <summary>
    /// The lake's Gerstner waves in C#, exactly as Waves.cginc moves the water (World/Water.HeightAt): the float, the
    /// boat and the line ride them. Push() sets the shaders' globals once a frame.
    /// </summary>
    public static class Waves
    {
        private static readonly Vector4[] Table =
        {
            new(0f, 9f, 0.40f, 0f), new(0.49f, 6.2f, 0.26f, 1.7f), new(-0.58f, 4.4f, 0.18f, 4.1f), new(1.08f, 3.1f, 0.10f, 2.6f),
            new(-0.26f, 1.9f, 0.05f, 5.3f), new(0.79f, 1.25f, 0.035f, 0.9f), new(-1.22f, 0.8f, 0.025f, 3.7f),
        };

        public const int VertexWaves = 4;
        public static float Wind = 0.3f;
        public static float Amplitude => Mathf.Lerp(0.015f, 0.1f, Wind);
        public static Vector2 Direction = new(0.8f, 0.6f);
        public static float Chop = 1.4f;
        public static float Time;

        private static readonly int TimeId = Shader.PropertyToID("_WaterTime"), AmpId = Shader.PropertyToID("_WaterAmp"),
            DirId = Shader.PropertyToID("_WaterDir"), ChopId = Shader.PropertyToID("_WaterChop");

        public static void Push(float dt)
        {
            Time += dt;
            Shader.SetGlobalFloat(TimeId, Time);
            Shader.SetGlobalFloat(AmpId, Amplitude);
            Shader.SetGlobalVector(DirId, new Vector4(Direction.normalized.x, Direction.normalized.y, 0f, 0f));
            Shader.SetGlobalFloat(ChopId, Chop);
        }

        private static Vector2 Dir(int i)
        {
            var w = Direction.normalized;
            float a = Table[i].x;
            return new Vector2(w.x * Mathf.Cos(a) - w.y * Mathf.Sin(a), w.x * Mathf.Sin(a) + w.y * Mathf.Cos(a));
        }

        private static Vector3 Gerstner(Vector2 p)
        {
            var d = Vector3.zero;
            float amp = Amplitude;
            for (int i = 0; i < VertexWaves; i++)
            {
                float k = 2f * Mathf.PI / Table[i].y;
                float omega = Mathf.Sqrt(9.81f * k);
                var dir = Dir(i);
                float f = k * Vector2.Dot(dir, p) - omega * Time + Table[i].w;
                float a = amp * Table[i].z;
                float q = Chop * a;
                d.x += dir.x * q * Mathf.Cos(f);
                d.z += dir.y * q * Mathf.Cos(f);
                d.y += a * Mathf.Sin(f);
            }
            return d;
        }

        /// <summary>The water's height over the world point (x, z).</summary>
        public static float HeightAt(float x, float z, float unusedTime = 0f)
        {
            var p = new Vector2(x, z);
            var g = Gerstner(p);
            var rest = p - new Vector2(g.x, g.z);
            return Gerstner(rest).y;
        }
    }
}
