using System;
using Godot;

namespace FishingSim.Graphics;

public enum QualityLevel { UltraLow, Low, Medium, High, Ultra }

/// <summary>How far the world is drawn in detail (a separate choice from the effects' quality).</summary>
public enum ViewDistance { Short, Medium, Long, Extreme }

/// <summary>
/// Graphics presets: post-processing (SSAO, SSIL, SSR, SDFGI, volumetric fog, bloom), anti-aliasing, shadow range and
/// grass density. Chosen in the pause menu and remembered in user://graphics.cfg. Effects the Compatibility renderer
/// does not support are simply ignored by Godot.
/// </summary>
public static class GraphicsQuality
{
    private const string ConfigPath = "user://graphics.cfg";

    /// <summary>Bump when the presets change: older saved effect settings then follow the new preset.</summary>
    private const int SettingsVersion = 4;     // 4: Ultra Low added in front (saved levels move up one)
    private static bool _loaded;
    private static QualityLevel _level = QualityLevel.High;
    private static ViewDistance _view = ViewDistance.Long;
    private static int _scale = 100;

    /// <summary>The 3D resolution choices (per cent of the screen's pixels; below 100 upscaled by AMD FSR).</summary>
    public static readonly int[] RenderScales = { 100, 85, 70, 50 };

    public static event Action? Changed;

    // ------------------------------------------------------------------ the individual settings
    /// <summary>
    /// One setting of the graphics screen: its name, a hint, the names of its steps and the step in use. A quality
    /// preset sets them all; changing one afterwards makes the preset "Własne" (custom).
    /// </summary>
    public sealed class Setting
    {
        public readonly string Key, Name, Hint;
        public readonly string[] Steps;
        public readonly int[] Preset;          // the step for Ultra Low, Low, Medium, High, Ultra
        public readonly bool Display;          // a window/display setting (not part of the presets)
        public int Value;

        public Setting(string key, string name, string hint, string[] steps, int[] preset, bool display = false)
        {
            Key = key; Name = name; Hint = hint; Steps = steps; Preset = preset; Display = display;
            Value = preset[(int)QualityLevel.High];
        }
    }

    // The largest texture side: 1K on Ultra Low and Low (memory and the first start on weak PCs) .. 8K on Ultra.
    public static readonly Setting Textures = new("textures", "Tekstury",
        "Największa rozdzielczość tekstur: 1K (słabe komputery) .. 8K (dużo pamięci karty). Pełna zmiana po ponownym uruchomieniu gry.",
        new[] { "1K", "2K", "4K", "8K" }, new[] { 0, 0, 1, 2, 3 });
    public static readonly Setting Shadows = new("shadows", "Cienie", "Zasięg i ostrość cieni od słońca.",
        new[] { "Wyłączone", "Niskie", "Średnie", "Wysokie", "Ultra" }, new[] { 0, 1, 2, 3, 4 });
    // No MSAA: in Godot 4.3 it leaves the depth texture the water reads empty (no refraction, no bottom, no shore).
    public static readonly Setting AntiAliasing = new("aa", "Wygładzanie krawędzi", "TAA najlepiej wygładza trawę, liście i linkę; FXAA jest najtańsze.",
        new[] { "Wyłączone", "FXAA", "TAA" }, new[] { 1, 1, 1, 2, 2 });
    public static readonly Setting Occlusion = new("ssao", "Cieniowanie otoczenia (SSAO)", "Miękkie cienie w zakamarkach.",
        new[] { "Wyłączone", "Włączone" }, new[] { 0, 0, 1, 1, 1 });
    public static readonly Setting IndirectLight = new("ssil", "Światło odbite (SSIL)", "Kolorowe odbicia światła od otoczenia. Kosztowne.",
        new[] { "Wyłączone", "Włączone" }, new[] { 0, 0, 0, 0, 1 });
    public static readonly Setting Reflections = new("reflections", "Odbicia w wodzie", "Odbicia brzegu i nieba w wodzie (SSR).",
        new[] { "Tylko niebo", "Niskie", "Średnie", "Wysokie" }, new[] { 0, 0, 1, 2, 3 });
    public static readonly Setting Fog = new("fog", "Mgła wolumetryczna", "Mgła z promieniami słońca.",
        new[] { "Wyłączona", "Bliska", "Daleka" }, new[] { 0, 0, 1, 2, 2 });
    public static readonly Setting Glow = new("glow", "Poświata (bloom)", "Blask jasnych miejsc: słońca na wodzie, latarni.",
        new[] { "Wyłączona", "Włączona" }, new[] { 0, 0, 1, 1, 1 });
    public static readonly Setting Plants = new("plants", "Trawa i rośliny", "Gęstość i zasięg trawy, kwiatów i trzcin.",
        new[] { "Niska", "Średnia", "Wysoka", "Ultra" }, new[] { 0, 0, 1, 2, 3 });
    public static readonly Setting WindowMode = new("window", "Tryb okna", "",
        new[] { "Okno", "Okno zmaksymalizowane", "Pełny ekran", "Pełny ekran (wyłączny)" }, new[] { 1, 1, 1, 1, 1 }, display: true);
    public static readonly Setting VSync = new("vsync", "Synchronizacja pionowa", "Bez rozrywania obrazu; ogranicza FPS do odświeżania monitora.",
        new[] { "Wyłączona", "Włączona", "Adaptacyjna" }, new[] { 1, 1, 1, 1, 1 }, display: true);
    public static readonly int[] FpsSteps = { 0, 30, 60, 75, 90, 120, 144, 165, 240 };
    public static readonly Setting FpsLimit = new("fps", "Limit klatek", "Górna granica FPS (mniej ciepła i hałasu karty).",
        Array.ConvertAll(FpsSteps, f => f == 0 ? "Bez limitu" : $"{f} FPS"), new[] { 0, 0, 0, 0, 0 }, display: true);

    public static readonly float[] UiScales = { 1f, 1.25f, 1.5f, 1.75f, 2f };
    public static readonly Setting UiScale = new("ui_scale", "Skala interfejsu", "Wielkość napisów i paneli przy 1920x1080 (obraz 3D zawsze w pełnej rozdzielczości).",
        new[] { "Mała", "Średnia", "Normalna", "Duża", "Bardzo duża" }, new[] { 2, 2, 2, 2, 2 }, display: true);
    public static readonly Setting FpsCounter = new("fps_counter", "Licznik FPS", "Liczba klatek na sekundę w rogu ekranu; szczegóły = pełny panel wydajności (F11).",
        new[] { "Wyłączony", "FPS", "FPS + szczegóły" }, new[] { 0, 0, 0, 0, 0 }, display: true);
    public static readonly Setting FloatPreview = new("float_preview", "Podgląd spławika", "Okienko z bliskim widokiem spławika, gdy trzymasz w ręce wędkę z zarzuconym spławikiem.",
        new[] { "Wyłączony", "Włączony" }, new[] { 0, 0, 0, 0, 0 }, display: true);

    /// <summary>Every setting of the graphics screen, in its order (the view distance and 3D resolution have their own rows).</summary>
    public static readonly Setting[] Settings = { Textures, Shadows, AntiAliasing, Occlusion, IndirectLight, Reflections, Fog, Glow, Plants };
    public static readonly Setting[] DisplaySettings = { WindowMode, VSync, FpsLimit };
    public static readonly Setting[] InterfaceSettings = { UiScale, FpsCounter, FloatPreview };

    /// <summary>Raised when a display or interface setting changes (the FPS counter and the float preview follow it).</summary>
    public static event Action? DisplayChanged;

    /// <summary>The settings no longer match the last preset.</summary>
    public static bool Custom
    {
        get
        {
            if (!_loaded)
                Load();
            foreach (var st in Settings)
                if (st.Value != st.Preset[(int)_level])
                    return true;
            return false;
        }
    }

    /// <summary>Sets one setting's step (clamped) and applies it.</summary>
    public static void SetStep(Setting setting, int step)
    {
        if (!_loaded)
            Load();
        setting.Value = Math.Clamp(step, 0, setting.Steps.Length - 1);
        SaveConfig();
        if (setting.Display)
            ApplyDisplay();
        Changed?.Invoke();
    }

    private static int V(Setting s)
    {
        if (!_loaded)
            Load();
        return s.Value;
    }

    // ------------------------------------------------------------------ presets
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
        QualityLevel.UltraLow => "Ultra niska",
        QualityLevel.Low => "Niska",
        QualityLevel.Medium => "Średnia",
        QualityLevel.High => "Wysoka",
        _ => "Ultra",
    };

    /// <summary>Picks a preset: every setting goes to its step for that preset.</summary>
    public static void Set(QualityLevel level)
    {
        _loaded = true;
        _level = level;
        foreach (var st in Settings)
            st.Value = st.Preset[(int)level];
        SaveConfig();
        Changed?.Invoke();
    }

    public static void Cycle() => Set((QualityLevel)(((int)Level + 1) % LevelCount));

    public const int LevelCount = 5;

    public static ViewDistance View
    {
        get
        {
            if (!_loaded)
                Load();
            return _view;
        }
    }

    public static string ViewTitle(ViewDistance v) => v switch
    {
        ViewDistance.Short => "Bliski",
        ViewDistance.Medium => "Średni",
        ViewDistance.Long => "Daleki",
        _ => "Bardzo daleki",
    };

    public static void SetView(ViewDistance v)
    {
        if (!_loaded)
            Load();
        _view = v;
        SaveConfig();
        Changed?.Invoke();
    }

    public static void CycleView() => SetView((ViewDistance)(((int)View + 1) % 4));

    /// <summary>The 3D image's resolution in per cent of the screen's (the interface stays sharp at full resolution).
    /// The graphics card's work grows with the pixels: 70 % draws half of them.</summary>
    public static int RenderScale
    {
        get
        {
            if (!_loaded)
                Load();
            return _scale;
        }
    }

    public static string RenderScaleTitle(int percent) => percent >= 100 ? "100% (natywna)" : $"{percent}% (FSR)";

    public static void SetRenderScale(int percent)
    {
        if (!_loaded)
            Load();
        _scale = Math.Clamp(percent, 50, 100);
        SaveConfig();
        Changed?.Invoke();
    }

    public static void CycleRenderScale()
    {
        int i = Array.IndexOf(RenderScales, RenderScale);
        SetRenderScale(RenderScales[(i + 1) % RenderScales.Length]);
    }

    /// <summary>How far (m) the distant forest is drawn; 0 = all the way to the horizon.</summary>
    public static float ViewRange => View switch
    {
        ViewDistance.Short => 320f,
        ViewDistance.Medium => 520f,
        ViewDistance.Long => 850f,
        _ => 0f,
    };

    /// <summary>How far the near detail reaches, as a share of its normal range: detailed trees, plants, buildings and
    /// props, villagers (drawn and animated), sun shadows, lamp light.</summary>
    public static float ViewFactor => View switch
    {
        ViewDistance.Short => 0.6f,
        ViewDistance.Medium => 0.8f,
        ViewDistance.Long => 1f,
        _ => 1.3f,
    };

    /// <summary>Point lights (campfires, the head lamp) cast shadows only from the High shadow step up: each one
    /// re-renders the scene round it whenever anything there moves.</summary>
    public static bool PointShadows => V(Shadows) >= 3;

    /// <summary>The largest texture side by the Textures setting: 1024, 2048, 4096 or 8192.</summary>
    public static int TextureLimit => 1024 << V(Textures);

    /// <summary>
    /// The side the game paints and decodes its own textures at (ground, bark, buildings, leaves, photographed sets),
    /// fixed when they are first painted in a run: the Textures setting, at most 4096 (the photographed sets are 4K;
    /// a painted 8K layer would need ~1 GB of working memory).
    /// </summary>
    public static int PaintSize => Math.Min(TextureLimit, 4096);

    /// <summary>Screen-space reflection steps of the water by the Reflections setting (0 = the sky only).</summary>
    public static int WaterReflectionSteps => V(Reflections) switch { 0 => 0, 1 => 12, 2 => 20, _ => 32 };

    private static void SaveConfig()
    {
        if (Renderer.IsHeadless || OS.HasEnvironment("FISHSIM_NO_SAVE"))
            return;
        var cfg = new ConfigFile();
        cfg.SetValue("graphics", "version", SettingsVersion);
        cfg.SetValue("graphics", "quality", (int)_level);
        cfg.SetValue("graphics", "view", (int)_view);
        cfg.SetValue("graphics", "scale", _scale);
        foreach (var st in Settings)
            cfg.SetValue("graphics", st.Key, st.Value);
        foreach (var st in DisplaySettings)
            cfg.SetValue("display", st.Key, st.Value);
        foreach (var st in InterfaceSettings)
            cfg.SetValue("interface", st.Key, st.Value);
        cfg.Save(ConfigPath);
    }

    /// <summary>True when the first start found integrated graphics (no saved settings) and picked light ones.</summary>
    public static bool PickedForIntegratedGpu { get; private set; }

    private static void Load()
    {
        _loaded = true;
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) != Error.Ok)
        {
            // The first start on a laptop's integrated graphics: light effects, a short view and a 70 % 3D image
            // upscaled by FSR (the angler can raise them on the graphics screen). Dedicated cards keep High.
            if (!Renderer.IsHeadless && RenderingServer.GetVideoAdapterType() == RenderingDevice.DeviceType.IntegratedGpu)
            {
                _level = QualityLevel.Low;
                _view = ViewDistance.Short;
                _scale = 70;
                PickedForIntegratedGpu = true;
            }
            foreach (var st in Settings)
                st.Value = st.Preset[(int)_level];
            SaveConfig();
            return;
        }
        int savedVersion = cfg.GetValue("graphics", "version", 0).AsInt32();
        int savedLevel = cfg.GetValue("graphics", "quality", (int)QualityLevel.High).AsInt32();
        if (savedVersion < 4 && cfg.HasSectionKey("graphics", "quality"))
            savedLevel++;      // saved before Ultra Low was put in front
        _level = (QualityLevel)Math.Clamp(savedLevel, 0, LevelCount - 1);
        _view = (ViewDistance)Math.Clamp(cfg.GetValue("graphics", "view", (int)ViewDistance.Long).AsInt32(), 0, 3);
        _scale = Math.Clamp(cfg.GetValue("graphics", "scale", 100).AsInt32(), 50, 100);
        // Settings saved by older versions (missing, or from presets that have changed since) follow the preset.
        bool current = savedVersion >= SettingsVersion;
        foreach (var st in Settings)
            st.Value = current
                ? Math.Clamp(cfg.GetValue("graphics", st.Key, st.Preset[(int)_level]).AsInt32(), 0, st.Steps.Length - 1)
                : st.Preset[(int)_level];
        foreach (var st in DisplaySettings)
            st.Value = Math.Clamp(cfg.GetValue("display", st.Key, st.Preset[0]).AsInt32(), 0, st.Steps.Length - 1);
        foreach (var st in InterfaceSettings)
            st.Value = Math.Clamp(cfg.GetValue("interface", st.Key, st.Preset[0]).AsInt32(), 0, st.Steps.Length - 1);
    }

    /// <summary>The window mode, vertical sync and frame limit (at start and when changed).</summary>
    public static void ApplyDisplay()
    {
        if (Renderer.IsHeadless)
            return;
        if (!_loaded)
            Load();
        var mode = WindowMode.Value switch
        {
            0 => DisplayServer.WindowMode.Windowed,
            1 => DisplayServer.WindowMode.Maximized,
            2 => DisplayServer.WindowMode.Fullscreen,
            _ => DisplayServer.WindowMode.ExclusiveFullscreen,
        };
        if (DisplayServer.WindowGetMode() != mode)
            DisplayServer.WindowSetMode(mode);
        DisplayServer.WindowSetVsyncMode(VSync.Value switch
        {
            0 => DisplayServer.VSyncMode.Disabled,
            2 => DisplayServer.VSyncMode.Adaptive,
            _ => DisplayServer.VSyncMode.Enabled,
        });
        Engine.MaxFps = FpsSteps[Math.Clamp(FpsLimit.Value, 0, FpsSteps.Length - 1)];
        if (Engine.GetMainLoop() is SceneTree tree)
            tree.Root.ContentScaleFactor = UiScales[Math.Clamp(UiScale.Value, 0, UiScales.Length - 1)];
        UI.FpsCounter.Sync();
        DisplayChanged?.Invoke();
    }

    /// <summary>Radius (m) of the dense grass around the player; 0 = no grass blades.</summary>
    public static float GrassRadius => V(Plants) switch
    {
        0 => 0f,
        1 => 18f,
        2 => 28f,
        _ => 40f,
    } * Math.Clamp(ViewFactor, 0.75f, 1.2f);

    /// <summary>How far the scattered plants (grass tufts, flowers, ferns, reeds, lilies) are drawn, as a share of their full range.</summary>
    public static float FoliageRange => V(Plants) switch
    {
        0 => 0.45f,
        1 => 0.7f,
        2 => 1f,
        _ => 1.3f,
    };

    /// <summary>Distance (m) between grass clumps.</summary>
    public static float GrassSpacing => V(Plants) >= 3 ? 0.42f : 0.5f;

    /// <summary>
    /// The Nordic (Valheim-like) finish, the game's only look: light-catching fog with sun shafts, bloom on bright
    /// water and sky, ambient occlusion, and a grade that pushes shadows towards teal and highlights towards warm gold.
    /// Costly screen-space effects are kept for Ultra so the default (High) runs smoothly at QHD:
    /// Low = AO off, no fog volume; Medium = AO + volumetric fog; High = + more fog detail;
    /// Ultra = + indirect light (SSIL) and screen-space reflections.
    /// </summary>
    public static void ApplyEnvironment(Godot.Environment env)
    {
        env.TonemapMode = Godot.Environment.ToneMapper.Aces;
        env.TonemapWhite = 6f;

        env.SsaoEnabled = V(Occlusion) > 0;
        env.SsaoRadius = 1.8f;
        env.SsaoIntensity = 2.4f;
        env.SsaoPower = 1.4f;
        env.SsaoDetail = 0.5f;
        env.SsilEnabled = V(IndirectLight) > 0;
        env.SsilRadius = 4f;
        env.SsilIntensity = 1.1f;
        env.SsrEnabled = V(Reflections) >= 3;
        env.SsrMaxSteps = 40;
        env.SdfgiEnabled = false;                       // far too expensive for what it adds outdoors

        env.GlowEnabled = V(Glow) > 0;     // the blur passes cost integrated graphics dearly
        env.GlowIntensity = 0.7f;
        env.GlowStrength = 1f;
        env.GlowBloom = 0.03f;
        env.GlowHdrThreshold = 1.15f;
        env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight;

        env.VolumetricFogEnabled = V(Fog) > 0;
        env.VolumetricFogAnisotropy = 0.82f;
        env.VolumetricFogLength = V(Fog) >= 2 ? 140f : 90f;
        env.VolumetricFogDetailSpread = 1.5f;
        env.VolumetricFogAlbedo = new Color(0.86f, 0.9f, 0.96f);
        env.VolumetricFogGIInject = 1f;

        env.AdjustmentEnabled = true;
        env.AdjustmentContrast = 1.12f;
        env.AdjustmentSaturation = 1.1f;
        env.AdjustmentColorCorrection = NordicGrade();
    }

    private static GradientTexture1D? _nordicGrade;

    /// <summary>Colour grade: luminance in, colour out (dark teal shadows, neutral mids, warm highlights).</summary>
    private static GradientTexture1D NordicGrade()
    {
        if (_nordicGrade != null)
            return _nordicGrade;
        var g = new Gradient();
        g.Offsets = new[] { 0f, 0.18f, 0.5f, 0.8f, 1f };
        g.Colors = new[]
        {
            new Color(0.02f, 0.04f, 0.065f), new Color(0.16f, 0.2f, 0.23f), new Color(0.5f, 0.5f, 0.48f),
            new Color(0.83f, 0.8f, 0.72f), new Color(1f, 0.98f, 0.93f),
        };
        _nordicGrade = new GradientTexture1D { Gradient = g, Width = 256 };
        return _nordicGrade;
    }

    public static void ApplyViewport(Viewport vp)
    {
        int aa = V(AntiAliasing);
        vp.Msaa3D = Viewport.Msaa.Disabled;
        vp.UseTaa = aa >= 2;
        vp.ScreenSpaceAA = aa == 1 ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        vp.UseDebanding = V(Glow) > 0 || V(Fog) > 0;
        vp.PositionalShadowAtlasSize = V(Shadows) >= 3 ? 4096 : 2048;
        // A lower 3D resolution upscaled by FSR 1 (the interface is drawn at the screen's own resolution on top).
        float scale = RenderScale / 100f;
        vp.Scaling3DMode = scale < 0.999f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        vp.Scaling3DScale = scale;
        // Weak GPUs (the Low preset): no sharpening mip bias (project.godot's -0.5 costs texture bandwidth on every
        // surface; the 16x anisotropy is a project setting Godot 4.3 cannot change per viewport), meshes drop to
        // their LODs sooner, the softest shadow filters off.
        bool low = Level <= QualityLevel.Low;
        vp.TextureMipmapBias = low ? 0f : -0.5f;
        vp.MeshLodThreshold = low ? 3f : 1f;
        var filter = low ? RenderingServer.ShadowQuality.Hard : V(Shadows) >= 3 ? RenderingServer.ShadowQuality.SoftMedium : RenderingServer.ShadowQuality.SoftLow;
        RenderingServer.DirectionalSoftShadowFilterSetQuality(filter);
        RenderingServer.PositionalSoftShadowFilterSetQuality(filter);
    }

    /// <summary>Fish under the water cast shadows except on Ultra Low and Low (they are barely seen from above the surface).</summary>
    public static bool FishShadows => Level > QualityLevel.Low;

    public static void ApplySun(DirectionalLight3D sun)
    {
        int q = V(Shadows);
        sun.ShadowEnabled = q > 0;
        // The sun's shadow map: 2048 px on the low steps (a quarter of the memory), 4096 px otherwise.
        RenderingServer.DirectionalShadowAtlasSetSize(q <= 1 ? 2048 : 4096, true);
        sun.DirectionalShadowMode = q >= 4
            ? DirectionalLight3D.ShadowMode.Parallel4Splits
            : DirectionalLight3D.ShadowMode.Parallel2Splits;
        sun.DirectionalShadowMaxDistance = q switch
        {
            <= 1 => 50f,
            2 => 80f,
            3 => 120f,
            _ => 200f,
        } * Math.Clamp(ViewFactor, 0.7f, 1.2f);
        sun.ShadowBlur = 1.2f;
        sun.ShadowNormalBias = 1.2f;
        sun.LightAngularDistance = q >= 4 ? 0.5f : 0f;   // contact-hardening shadows on the top step
    }
}
