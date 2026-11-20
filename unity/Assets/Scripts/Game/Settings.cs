using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>Graphics and control settings (PlayerPrefs): quality preset, view distance, frame limit, v-sync, mouse.</summary>
    public static class Settings
    {
        public static readonly string[] QualityNames = { "Bardzo niska", "Niska", "Średnia", "Wysoka", "Ultra" };
        public static readonly string[] ViewNames = { "Krótki", "Średni", "Daleki" };
        public static readonly int[] FpsLimits = { 30, 60, 90, 120, 144, 0 };

        public static int Quality { get; set; } = 3;
        public static int View { get; set; } = 2;
        public static int Fps { get; set; } = 5;
        public static bool VSync { get; set; } = true;
        public static bool Fullscreen { get; set; } = true;
        public static float Mouse { get; set; } = 2.2f;

        public static void Load()
        {
            Quality = Mathf.Clamp(PlayerPrefs.GetInt("quality", SystemInfo.graphicsMemorySize < 3000 ? 1 : 3), 0, 4);
            View = Mathf.Clamp(PlayerPrefs.GetInt("view", 2), 0, 2);
            Fps = Mathf.Clamp(PlayerPrefs.GetInt("fps", 5), 0, FpsLimits.Length - 1);
            VSync = PlayerPrefs.GetInt("vsync", 1) == 1;
            Fullscreen = PlayerPrefs.GetInt("fullscreen", 1) == 1;
            Mouse = PlayerPrefs.GetFloat("mouse", 2.2f);
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("quality", Quality);
            PlayerPrefs.SetInt("view", View);
            PlayerPrefs.SetInt("fps", Fps);
            PlayerPrefs.SetInt("vsync", VSync ? 1 : 0);
            PlayerPrefs.SetInt("fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetFloat("mouse", Mouse);
            PlayerPrefs.Save();
        }

        public static void Apply(Camera? camera)
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FpsLimits[Fps] == 0 ? -1 : FpsLimits[Fps];
            QualitySettings.shadows = Quality == 0 ? ShadowQuality.Disable : ShadowQuality.All;
            QualitySettings.shadowResolution = Quality switch
            {
                <= 1 => ShadowResolution.Low,
                2 => ShadowResolution.Medium,
                3 => ShadowResolution.High,
                _ => ShadowResolution.VeryHigh,
            };
            QualitySettings.shadowDistance = Quality switch { 0 => 0f, 1 => 40f, 2 => 70f, 3 => 110f, _ => 160f };
            QualitySettings.shadowCascades = Quality >= 3 ? 4 : Quality == 2 ? 2 : 1;
            QualitySettings.antiAliasing = Quality switch { <= 1 => 0, 2 => 2, 3 => 4, _ => 8 };
            QualitySettings.anisotropicFiltering = Quality >= 2 ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Enable;
            QualitySettings.pixelLightCount = Quality switch { <= 1 => 2, 2 => 4, _ => 8 };   // lamps, windows and campfires light their surroundings per pixel
            QualitySettings.softParticles = false;
            QualitySettings.lodBias = 1f + Quality * 0.25f;
            if (Screen.fullScreen != Fullscreen)
            {
                Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                Screen.fullScreen = Fullscreen;
            }
            if (camera != null)
            {
                float far = View switch { 0 => 350f, 1 => 700f, _ => 1400f };
                camera.farClipPlane = far;
                // grass and reeds end sooner than the trees
                var distances = new float[32];
                camera.layerCullDistances = distances;
                camera.allowMSAA = Quality >= 2;
            }
        }
    }
}
