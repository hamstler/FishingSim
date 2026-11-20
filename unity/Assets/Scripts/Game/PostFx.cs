using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The camera's finish (GraphicsQuality.ApplyEnvironment in the Godot game): AO from Medium up, glow from Medium up,
    /// ACES tonemapping, contrast, saturation and the Nordic grade always. The camera renders in HDR.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PostFx : MonoBehaviour
    {
        [SerializeField] public float Threshold = 1.15f, GlowIntensity = 0.7f, Contrast = 1.12f, Saturation = 1.1f, White = 6f;
        [SerializeField] public float AORadius = 1.8f, AOStrength = 2.4f;

        private Material? _mat;
        private Texture2D? _grade;
        private const int Levels = 6;
        private readonly RenderTexture?[] _chain = new RenderTexture?[Levels];

        private void OnEnable()
        {
            var cam = GetComponent<Camera>();
            cam.allowHDR = true;
            cam.depthTextureMode |= DepthTextureMode.Depth;
        }

        /// <summary>The Nordic grade (luminance in, colour out), sampled per channel like Godot's 1D colour correction.</summary>
        private static Texture2D Grade()
        {
            float[] offsets = { 0f, 0.18f, 0.5f, 0.8f, 1f };
            Color[] colours =
            {
                new(0.02f, 0.04f, 0.065f), new(0.16f, 0.2f, 0.23f), new(0.5f, 0.5f, 0.48f), new(0.83f, 0.8f, 0.72f), new(1f, 0.98f, 0.93f),
            };
            var t = new Texture2D(256, 1, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "NordicGrade" };
            for (int x = 0; x < 256; x++)
            {
                float u = x / 255f;
                int k = 0;
                while (k < offsets.Length - 2 && u > offsets[k + 1])
                    k++;
                float f = Mathf.InverseLerp(offsets[k], offsets[k + 1], u);
                t.SetPixel(x, 0, Color.Lerp(colours[k], colours[k + 1], f));
            }
            t.Apply(false, true);
            return t;
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (_mat == null)
            {
                var shader = Shader.Find("Hidden/FS/Post");
                if (shader == null || !shader.isSupported)
                {
                    Graphics.Blit(src, dst);
                    return;
                }
                _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _grade = Grade();
            }
            int q = Settings.Quality;
            _mat.SetFloat("_Threshold", Threshold);
            _mat.SetFloat("_GlowIntensity", q >= 2 ? GlowIntensity : 0f);
            _mat.SetFloat("_Contrast", Contrast);
            _mat.SetFloat("_Saturation", Saturation);
            _mat.SetFloat("_White", White);
            _mat.SetTexture("_Grade", _grade);

            // glow: a mip chain of the bright parts, then back up
            var fmt = RenderTextureFormat.DefaultHDR;
            int w = src.width / 2, h = src.height / 2;
            RenderTexture? bloom = null;
            int levels = 0;
            if (q >= 2)
            {
                for (int i = 0; i < Levels && w >= 8 && h >= 8; i++, w /= 2, h /= 2)
                {
                    _chain[i] = RenderTexture.GetTemporary(w, h, 0, fmt);
                    Graphics.Blit(i == 0 ? src : _chain[i - 1], _chain[i], _mat, i == 0 ? 0 : 1);
                    levels++;
                }
                for (int i = levels - 1; i > 0; i--)
                    Graphics.Blit(_chain[i], _chain[i - 1], _mat, 2);
                bloom = _chain[0];
            }
            _mat.SetTexture("_Bloom", bloom != null ? bloom : Texture2D.blackTexture);

            RenderTexture? ao = null;
            if (q >= 2)
            {
                ao = RenderTexture.GetTemporary(src.width / 2, src.height / 2, 0, RenderTextureFormat.R8);
                _mat.SetFloat("_AORadius", AORadius);
                _mat.SetFloat("_AOStrength", AOStrength);
                Graphics.Blit(src, ao, _mat, 3);
            }
            _mat.SetFloat("_UseAO", ao != null ? 1f : 0f);
            _mat.SetTexture("_AO", ao != null ? ao : Texture2D.whiteTexture);

            Graphics.Blit(src, dst, _mat, 4);

            for (int i = 0; i < levels; i++)
            {
                RenderTexture.ReleaseTemporary(_chain[i]);
                _chain[i] = null;
            }
            if (ao != null)
                RenderTexture.ReleaseTemporary(ao);
        }
    }
}
