using System.Threading.Tasks;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The tileable textures the shaders read (Graphics/ProceduralTextures and Water.RippleNormals of the Godot game):
    /// a value-noise field, light caustics, foam noise and the wind-ripple slope map.
    /// </summary>
    public static class ProcTextures
    {
        private static Texture2D? _noise, _caustics, _foam, _ripples;

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393) ^ (uint)(y * 668265263) ^ (uint)(seed * 2246822519u);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        private static float TileValue(float x, float y, int period, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float H(int a, int b) => Hash(((a % period) + period) % period, ((b % period) + period) % period, seed);
            return Mathf.Lerp(Mathf.Lerp(H(ix, iy), H(ix + 1, iy), fx), Mathf.Lerp(H(ix, iy + 1), H(ix + 1, iy + 1), fx), fy);
        }

        /// <summary>Fractal value noise, tileable, 0..1 spread over the whole range.</summary>
        private static Texture2D Noise(int size, int period, int seed, string name)
        {
            var values = new float[size * size];
            Parallel.For(0, size, y =>
            {
                for (int x = 0; x < size; x++)
                {
                    float v = 0f, amp = 0.5f, sum = 0f;
                    for (int o = 0; o < 5; o++)
                    {
                        int p = period << o;
                        v += TileValue(x / (float)size * p, y / (float)size * p, p, seed + o) * amp;
                        sum += amp;
                        amp *= 0.5f;
                    }
                    values[y * size + x] = v / sum;
                }
            });
            float lo = 1f, hi = 0f;
            foreach (float v in values) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            var px = new byte[size * size];
            for (int i = 0; i < px.Length; i++)
                px[i] = (byte)Mathf.Clamp((values[i] - lo) / Mathf.Max(hi - lo, 1e-4f) * 255f, 0f, 255f);
            var tex = new Texture2D(size, size, TextureFormat.R8, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = name };
            tex.SetPixelData(px, 0);
            tex.Apply(true, true);
            return tex;
        }

        public static Texture2D NoiseField => _noise ??= Noise(512, 8, 17, "Noise");
        public static Texture2D Foam => _foam ??= Noise(512, 16, 53, "Foam");

        /// <summary>Caustics: the bright web of light focused by the waves (Voronoi cell borders, F2 - F1).</summary>
        public static Texture2D Caustics
        {
            get
            {
                if (_caustics != null)
                    return _caustics;
                const int size = 256, cells = 8;
                var px = new byte[size * size];
                Parallel.For(0, size, y =>
                {
                    for (int x = 0; x < size; x++)
                    {
                        float fx = x / (float)size * cells, fy = y / (float)size * cells;
                        int cx = Mathf.FloorToInt(fx), cy = Mathf.FloorToInt(fy);
                        float d1 = 9f, d2 = 9f;
                        for (int j = -1; j <= 1; j++)
                            for (int i = -1; i <= 1; i++)
                            {
                                int gx = cx + i, gy = cy + j;
                                int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                                float px2 = gx + Hash(wx, wy, 5), py2 = gy + Hash(wx, wy, 9);
                                float d = Mathf.Sqrt((fx - px2) * (fx - px2) + (fy - py2) * (fy - py2));
                                if (d < d1) { d2 = d1; d1 = d; }
                                else if (d < d2) d2 = d;
                            }
                        float edge = Mathf.Clamp01(1f - (d2 - d1) * 3.2f);
                        px[y * size + x] = (byte)(Mathf.Pow(edge, 3f) * 255f);
                    }
                });
                var tex = new Texture2D(size, size, TextureFormat.R8, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, name = "Caustics" };
                tex.SetPixelData(px, 0);
                tex.Apply(true, true);
                return _caustics = tex;
            }
        }

        /// <summary>Water.RippleNormals: forty short waves with whole numbers of crests per repeat; RG = slope / 2 + 0.5.</summary>
        public static Texture2D Ripples
        {
            get
            {
                if (_ripples != null)
                    return _ripples;
                const int size = 512;
                var rng = new System.Random(91);
                var waves = new (float Kx, float Kz, float A, float Phase)[40];
                for (int i = 0; i < waves.Length; i++)
                {
                    int n = 2 + rng.Next(14);
                    float angle = (float)(rng.NextDouble() * 2 - 1) * (i < 28 ? 0.8f : 3.1f);
                    int kx = Mathf.RoundToInt(n * Mathf.Cos(angle)), kz = Mathf.RoundToInt(n * Mathf.Sin(angle));
                    if (kx == 0 && kz == 0)
                        kx = n;
                    float len = Mathf.Sqrt(kx * kx + kz * kz);
                    waves[i] = (kx, kz, 0.9f / Mathf.Pow(len, 1.4f), (float)rng.NextDouble() * Mathf.PI * 2f);
                }
                var slopes = new float[size * size * 2];
                Parallel.For(0, size, y =>
                {
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size * Mathf.PI * 2f, v = y / (float)size * Mathf.PI * 2f;
                        float sx = 0f, sz = 0f;
                        foreach (var w in waves)
                        {
                            float c = Mathf.Cos(w.Kx * u + w.Kz * v + w.Phase) * w.A;
                            sx += c * w.Kx;
                            sz += c * w.Kz;
                        }
                        slopes[(y * size + x) * 2] = sx;
                        slopes[(y * size + x) * 2 + 1] = sz;
                    }
                });
                float peak = 1e-6f;
                foreach (float v in slopes)
                    peak = Mathf.Max(peak, Mathf.Abs(v));
                var data = new byte[size * size * 4];
                for (int i = 0; i < size * size; i++)
                {
                    data[i * 4] = (byte)Mathf.Clamp((slopes[i * 2] / peak * 0.5f + 0.5f) * 255f, 0f, 255f);
                    data[i * 4 + 1] = (byte)Mathf.Clamp((slopes[i * 2 + 1] / peak * 0.5f + 0.5f) * 255f, 0f, 255f);
                    data[i * 4 + 2] = 255;
                    data[i * 4 + 3] = 255;
                }
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "Ripples" };
                tex.SetPixelData(data, 0);
                tex.Apply(true, true);
                return _ripples = tex;
            }
        }
    }
}
