using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>Colour and noise helpers safe on worker threads (Unity's Color.linear and Mathf.PerlinNoise are engine calls).</summary>
    public static class Col
    {
        private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        /// <summary>An sRGB colour as the linear value the shaders work in (the project renders in linear space).</summary>
        public static Color Lin(Color c) => new(ToLinear(c.r), ToLinear(c.g), ToLinear(c.b), c.a);

        public static Color Lin(float r, float g, float b) => Lin(new Color(r, g, b));

        private static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393) ^ (uint)(y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        /// <summary>Smooth value noise, 0..1.</summary>
        public static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
    }
}
