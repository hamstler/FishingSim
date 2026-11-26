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
            // 16-bit: the shaders magnify this field a lot and threshold it with narrow smoothsteps; 8 bits showed as
            // flat terraces with hard contours (clouds, hills, water)
            var px = new ushort[size * size];
            for (int i = 0; i < px.Length; i++)
                px[i] = Mathf.FloatToHalf(Mathf.Clamp01((values[i] - lo) / Mathf.Max(hi - lo, 1e-4f)));
            var tex = new Texture2D(size, size, TextureFormat.RHalf, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = name };
            tex.SetPixelData(px, 0);
            tex.Apply(true, true);
            return tex;
        }

        public static Texture2D NoiseField => _noise ??= Noise(512, 8, 17, "Noise");

        /// <summary>
        /// The sky's cloud noise like Godot's (SkyController: FastNoiseLite Perlin fBm, frequency 0.002 over 2048 px = about
        /// 4 cycles per sheet unit, 6 octaves): tileable gradient noise, then moved onto the mean and spread measured from a
        /// dump of Godot's texture (0.473, 0.154). The value noise thresholded into square, axis-bound patches that the
        /// dome's projection sheared into zigzag cloud sheets.
        /// </summary>
        public static Texture2D CloudNoise => _cloudNoise ??= Perlin(2048, 8, 6, 71, 0.473f, 0.154f, "CloudNoise");
        private static Texture2D? _cloudNoise;

        private static float Grad(int x, int y, int period, int seed, float dx, float dy)
        {
            float a = Hash(((x % period) + period) % period, ((y % period) + period) % period, seed) * Mathf.PI * 2f;
            return Mathf.Cos(a) * dx + Mathf.Sin(a) * dy;
        }

        private static float TilePerlin(float x, float y, int period, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            float ux = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f), uy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            float a = Grad(ix, iy, period, seed, fx, fy), b = Grad(ix + 1, iy, period, seed, fx - 1f, fy);
            float c = Grad(ix, iy + 1, period, seed, fx, fy - 1f), d = Grad(ix + 1, iy + 1, period, seed, fx - 1f, fy - 1f);
            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
        }

        /// <summary>Tileable Perlin fBm (lacunarity 2, gain 0.5), scaled to the given mean and spread, 16-bit.</summary>
        private static Texture2D Perlin(int size, int period, int octaves, int seed, float mean, float spread, string name)
        {
            var values = new float[size * size];
            Parallel.For(0, size, y =>
            {
                for (int x = 0; x < size; x++)
                {
                    float v = 0f, amp = 1f;
                    for (int o = 0; o < octaves; o++)
                    {
                        int p = period << o;
                        v += TilePerlin(x / (float)size * p, y / (float)size * p, p, seed + o) * amp;
                        amp *= 0.5f;
                    }
                    values[y * size + x] = v;
                }
            });
            double sum = 0, sq = 0;
            foreach (float v in values) { sum += v; sq += (double)v * v; }
            float m = (float)(sum / values.Length), sd = Mathf.Sqrt(Mathf.Max((float)(sq / values.Length) - m * m, 1e-8f));
            var px = new ushort[size * size];
            for (int i = 0; i < px.Length; i++)
                px[i] = Mathf.FloatToHalf(Mathf.Clamp01(mean + (values[i] - m) / sd * spread));
            var tex = new Texture2D(size, size, TextureFormat.RHalf, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = name };
            tex.SetPixelData(px, 0);
            tex.Apply(true, true);
            return tex;
        }
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
