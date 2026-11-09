using System;
using Godot;

namespace FishingSim.Graphics;

public enum QualityLevel { Low, Medium, High, Ultra }

/// <summary>
/// Graphics presets: post-processing (SSAO, SSIL, SSR, SDFGI, volumetric fog, bloom), anti-aliasing, shadow range and
/// grass density. Chosen in the pause menu and remembered in user://graphics.cfg. Effects the Compatibility renderer
/// does not support are simply ignored by Godot.
/// </summary>
public static class GraphicsQuality
{
    private const string ConfigPath = "user://graphics.cfg";
    private static bool _loaded;
    private static QualityLevel _level = QualityLevel.High;

    public static event Action? Changed;

    public static QualityLevel Level
    {
        get
        {
            if (!_loaded)
                Load();
            return _level;
        }
    }

    public static string Title(QualityLevel level) => level switch
    {
        QualityLevel.Low => "Niska",
        QualityLevel.Medium => "Średnia",
        QualityLevel.High => "Wysoka",
        _ => "Ultra",
    };

    public static void Set(QualityLevel level)
    {
        _loaded = true;
        _level = level;
        if (!Renderer.IsHeadless)
        {
            var cfg = new ConfigFile();
            cfg.SetValue("graphics", "quality", (int)level);
            cfg.Save(ConfigPath);
        }
        Changed?.Invoke();
    }

    public static void Cycle() => Set((QualityLevel)(((int)Level + 1) % 4));

    private static void Load()
    {
        _loaded = true;
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) == Error.Ok)
            _level = (QualityLevel)Math.Clamp(cfg.GetValue("graphics", "quality", (int)QualityLevel.High).AsInt32(), 0, 3);
    }

    /// <summary>Radius (m) of the dense grass around the player; 0 = no grass blades.</summary>
    public static float GrassRadius => Level switch
    {
        QualityLevel.Low => 0f,
        QualityLevel.Medium => 18f,
        QualityLevel.High => 28f,
        _ => 40f,
    };

    /// <summary>Screen pixels per world-image pixel (the pixel-art scale): bigger = chunkier and faster.</summary>
    public static int PixelSize => Level switch
    {
        QualityLevel.Low => 4,
        QualityLevel.Ultra => 2,
        _ => 3,
    };

    /// <summary>Distance (m) between grass clumps.</summary>
    public static float GrassSpacing => Level >= QualityLevel.Ultra ? 0.42f : 0.5f;

    public static void ApplyEnvironment(Godot.Environment env)
    {
        var q = Level;
        env.TonemapMode = Godot.Environment.ToneMapper.Aces;
        env.TonemapWhite = 6f;

        env.SsaoEnabled = q >= QualityLevel.Medium;
        env.SsaoRadius = 1.2f;
        env.SsaoIntensity = 1.6f;
        env.SsaoDetail = 0.6f;
        env.SsilEnabled = q >= QualityLevel.High;
        env.SsilRadius = 4f;
        env.SsilIntensity = 0.8f;
        env.SsrEnabled = q >= QualityLevel.High;
        env.SsrMaxSteps = 48;
        env.SdfgiEnabled = q >= QualityLevel.Ultra;
        env.SdfgiUseOcclusion = true;
        env.SdfgiCascades = 4;

        env.GlowEnabled = q >= QualityLevel.Medium;
        env.GlowIntensity = 0.6f;
        env.GlowBloom = 0.04f;
        env.GlowHdrThreshold = 1.1f;
        env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight;

        // Light shafts and depth haze; the plain distance fog (SkyController) stays on every level.
        env.VolumetricFogEnabled = q >= QualityLevel.High;
        env.VolumetricFogDensity = 0.004f;
        env.VolumetricFogAnisotropy = 0.7f;
        env.VolumetricFogLength = 90f;
        env.VolumetricFogAlbedo = new Color(0.9f, 0.93f, 1f);
        env.VolumetricFogGIInject = 0.6f;

        env.AdjustmentEnabled = true;
        env.AdjustmentContrast = 1.04f;
        env.AdjustmentSaturation = 1.06f;
    }

    public static void ApplyViewport(Viewport vp)
    {
        var q = Level;
        vp.Msaa3D = q switch
        {
            QualityLevel.Ultra => Viewport.Msaa.Msaa4X,
            QualityLevel.High => Viewport.Msaa.Msaa2X,
            _ => Viewport.Msaa.Disabled,
        };
        vp.UseTaa = q >= QualityLevel.High;
        vp.ScreenSpaceAA = q <= QualityLevel.Medium ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        vp.UseDebanding = q >= QualityLevel.Medium;
        vp.PositionalShadowAtlasSize = q >= QualityLevel.High ? 4096 : 2048;
    }

    public static void ApplySun(DirectionalLight3D sun)
    {
        var q = Level;
        sun.DirectionalShadowMode = q >= QualityLevel.High
            ? DirectionalLight3D.ShadowMode.Parallel4Splits
            : DirectionalLight3D.ShadowMode.Parallel2Splits;
        sun.DirectionalShadowMaxDistance = q switch
        {
            QualityLevel.Low => 50f,
            QualityLevel.Medium => 90f,
            QualityLevel.High => 140f,
            _ => 220f,
        };
        sun.ShadowBlur = 1.2f;
        sun.ShadowNormalBias = 1.2f;
        sun.LightAngularDistance = q >= QualityLevel.Ultra ? 0.5f : 0f;   // contact-hardening shadows on Ultra
    }
}
