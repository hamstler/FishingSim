using System;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// Graphics and control settings (PlayerPrefs). The quality preset (GraphicsQuality.Set in the Godot game) fills in
    /// every graphics option from its column; each option can then be changed on its own ("Własne" when they differ).
    /// </summary>
    public static class Settings
    {
        public static readonly string[] QualityNames = { "Bardzo niska", "Niska", "Średnia", "Wysoka", "Ultra" };
        public static readonly string[] ViewNames = { "Krótki", "Średni", "Daleki" };
        public static readonly int[] FpsLimits = { 30, 60, 90, 120, 144, 0 };
        public static readonly string[] TextureNames = { "1K", "2K", "4K" };
        public static readonly string[] ShadowNames = { "wyłączone", "niskie", "średnie", "wysokie", "ultra" };
        public static readonly string[] AANames = { "wyłączone", "MSAA 2x", "MSAA 4x", "MSAA 8x" };
        public static readonly string[] PlantNames = { "bez trawy", "mało", "średnio", "dużo" };
        public static readonly string[] AONames = { "wyłączona", "normalna", "wysoka" };
        public static readonly string[] FogNames = { "wyłączona", "lekka", "realistyczna", "gęsta" };
        public static readonly string[] ReflectionNames = { "tylko niebo", "niskie", "średnie", "wysokie" };
        public static readonly string[] ColourNames = { "realistyczne", "nordyckie" };

        /// <summary>Each option's value per preset (Bardzo niska .. Ultra).</summary>
        private static readonly int[] PTextures = { 0, 0, 1, 2, 2 }, PShadows = { 0, 1, 2, 3, 4 }, PSoft = { 0, 0, 1, 1, 1 },
            PAA = { 0, 0, 1, 2, 3 }, PPlants = { 0, 1, 2, 3, 3 }, PAO = { 0, 0, 1, 1, 2 }, PBloom = { 0, 0, 1, 1, 1 },
            PShafts = { 0, 0, 1, 1, 1 }, PFog = { 1, 2, 2, 2, 2 }, PVolFog = { 0, 0, 0, 1, 1 }, PReflections = { 0, 0, 1, 2, 3 },
            PSharpen = { 0, 0, 1, 1, 1 };

        private static int _quality = 3;

        /// <summary>Setting the preset sets every graphics option from it.</summary>
        public static int Quality
        {
            get => _quality;
            set
            {
                _quality = Mathf.Clamp(value, 0, 4);
                Textures = PTextures[_quality];
                Shadows = PShadows[_quality];
                SoftShadows = PSoft[_quality] == 1;
                AntiAliasing = PAA[_quality];
                Plants = PPlants[_quality];
                AO = PAO[_quality];
                Bloom = PBloom[_quality] == 1;
                SunShafts = PShafts[_quality] == 1;
                Fog = PFog[_quality];
                VolumetricFog = PVolFog[_quality] == 1;
                Reflections = PReflections[_quality];
                Sharpen = PSharpen[_quality] == 1;
            }
        }

        public static int View { get; set; } = 2;
        public static int Fps { get; set; } = 5;
        public static bool VSync { get; set; } = true;
        public static bool Fullscreen { get; set; } = true;
        public static float Mouse { get; set; } = 2.2f;

        /// <summary>0 = 1K, 1 = 2K, 2 = 4K painted and photographed textures (applies at the next map load).</summary>
        public static int Textures { get; set; } = 2;
        public static int Shadows { get; set; } = 3;
        public static bool SoftShadows { get; set; } = true;
        public static int AntiAliasing { get; set; } = 2;
        /// <summary>Grass and small plants: how far and how dense.</summary>
        public static int Plants { get; set; } = 3;
        /// <summary>Ambient occlusion: off, half resolution, full resolution.</summary>
        public static int AO { get; set; } = 1;
        public static bool Bloom { get; set; } = true;
        public static bool SunShafts { get; set; } = true;
        /// <summary>Distance haze and mist: off, light, realistic, dense.</summary>
        public static int Fog { get; set; } = 2;
        /// <summary>Height fog lying in the hollows and over the water, lit by the sun (forward scattering).</summary>
        public static bool VolumetricFog { get; set; } = true;
        /// <summary>The water's screen-space reflections (0 = the sky only).</summary>
        public static int Reflections { get; set; } = 2;
        /// <summary>0 = realistic (neutral grade), 1 = the Nordic grade (teal shadows, warm light).</summary>
        public static int Colours { get; set; } = 0;
        public static bool Vignette { get; set; } = true;
        public static bool Sharpen { get; set; } = true;

        /// <summary>Raised by Apply: the sky, the water and the post read their options again.</summary>
        public static event Action? Changed;

        /// <summary>The options differ from the preset's column.</summary>
        public static bool Custom =>
            Textures != PTextures[_quality] || Shadows != PShadows[_quality] || SoftShadows != (PSoft[_quality] == 1) ||
            AntiAliasing != PAA[_quality] || Plants != PPlants[_quality] || AO != PAO[_quality] || Bloom != (PBloom[_quality] == 1) ||
            SunShafts != (PShafts[_quality] == 1) || Fog != PFog[_quality] || VolumetricFog != (PVolFog[_quality] == 1) ||
            Reflections != PReflections[_quality] || Sharpen != (PSharpen[_quality] == 1);

        public static float FogFactor => Fog switch { 0 => 0f, 1 => 0.55f, 2 => 1f, _ => 1.7f };
        public static float SsrSteps => Reflections switch { 0 => 0f, 1 => 12f, 2 => 20f, _ => 32f };

        public static void Load()
        {
            Quality = Mathf.Clamp(PlayerPrefs.GetInt("quality", SystemInfo.graphicsMemorySize < 3000 ? 1 : 3), 0, 4);
            // missing keys follow the preset (the setter above filled them in)
            Textures = Mathf.Clamp(PlayerPrefs.GetInt("g_textures", Textures), 0, 2);
            Shadows = Mathf.Clamp(PlayerPrefs.GetInt("g_shadows", Shadows), 0, 4);
            SoftShadows = PlayerPrefs.GetInt("g_soft", SoftShadows ? 1 : 0) == 1;
            AntiAliasing = Mathf.Clamp(PlayerPrefs.GetInt("g_aa", AntiAliasing), 0, 3);
            Plants = Mathf.Clamp(PlayerPrefs.GetInt("g_plants", Plants), 0, 3);
            AO = Mathf.Clamp(PlayerPrefs.GetInt("g_ao", AO), 0, 2);
            Bloom = PlayerPrefs.GetInt("g_bloom", Bloom ? 1 : 0) == 1;
            SunShafts = PlayerPrefs.GetInt("g_shafts", SunShafts ? 1 : 0) == 1;
            Fog = Mathf.Clamp(PlayerPrefs.GetInt("g_fog", Fog), 0, 3);
            VolumetricFog = PlayerPrefs.GetInt("g_volfog", VolumetricFog ? 1 : 0) == 1;
            Reflections = Mathf.Clamp(PlayerPrefs.GetInt("g_reflections", Reflections), 0, 3);
            Colours = Mathf.Clamp(PlayerPrefs.GetInt("g_colours", Colours), 0, 1);
            Vignette = PlayerPrefs.GetInt("g_vignette", Vignette ? 1 : 0) == 1;
            Sharpen = PlayerPrefs.GetInt("g_sharpen", Sharpen ? 1 : 0) == 1;
            View = Mathf.Clamp(PlayerPrefs.GetInt("view", 2), 0, 2);
            Fps = Mathf.Clamp(PlayerPrefs.GetInt("fps", 5), 0, FpsLimits.Length - 1);
            VSync = PlayerPrefs.GetInt("vsync", 1) == 1;
            Fullscreen = PlayerPrefs.GetInt("fullscreen", 1) == 1;
            Mouse = PlayerPrefs.GetFloat("mouse", 2.2f);
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("quality", Quality);
            PlayerPrefs.SetInt("g_textures", Textures);
            PlayerPrefs.SetInt("g_shadows", Shadows);
            PlayerPrefs.SetInt("g_soft", SoftShadows ? 1 : 0);
            PlayerPrefs.SetInt("g_aa", AntiAliasing);
            PlayerPrefs.SetInt("g_plants", Plants);
            PlayerPrefs.SetInt("g_ao", AO);
            PlayerPrefs.SetInt("g_bloom", Bloom ? 1 : 0);
            PlayerPrefs.SetInt("g_shafts", SunShafts ? 1 : 0);
            PlayerPrefs.SetInt("g_fog", Fog);
            PlayerPrefs.SetInt("g_volfog", VolumetricFog ? 1 : 0);
            PlayerPrefs.SetInt("g_reflections", Reflections);
            PlayerPrefs.SetInt("g_colours", Colours);
            PlayerPrefs.SetInt("g_vignette", Vignette ? 1 : 0);
            PlayerPrefs.SetInt("g_sharpen", Sharpen ? 1 : 0);
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
            QualitySettings.shadows = Shadows == 0 ? ShadowQuality.Disable : SoftShadows ? ShadowQuality.All : ShadowQuality.HardOnly;
            QualitySettings.shadowResolution = Shadows switch
            {
                <= 1 => ShadowResolution.Low,
                2 => ShadowResolution.Medium,
                3 => ShadowResolution.High,
                _ => ShadowResolution.VeryHigh,
            };
            QualitySettings.shadowDistance = Shadows switch { 0 => 0f, 1 => 40f, 2 => 70f, 3 => 110f, _ => 160f };
            QualitySettings.shadowCascades = Shadows >= 3 ? 4 : Shadows == 2 ? 2 : 1;
            QualitySettings.antiAliasing = AntiAliasing switch { 0 => 0, 1 => 2, 2 => 4, _ => 8 };
            QualitySettings.anisotropicFiltering = Textures >= 1 ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Enable;
            QualitySettings.pixelLightCount = Quality switch { <= 1 => 2, 2 => 4, _ => 8 };   // lamps, windows and campfires light their surroundings per pixel
            QualitySettings.softParticles = false;
            QualitySettings.lodBias = 1f + Plants * 0.33f;
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
                camera.allowMSAA = AntiAliasing > 0;
            }
            Changed?.Invoke();
        }
    }
}
