using System;
using Godot;
using FishingSim.Graphics;

namespace FishingSim.World;

/// <summary>
/// The sky and lighting controller: drives the sky (shaders/sky.gdshader), sun and moon, exposure, ambient light,
/// shadows, aerial and volumetric fog, morning mist, stars, clouds (as a mood), rain and wind from
/// <see cref="WorldState"/>. The light follows the time of day: a deep orange sun on the horizon, a golden hour with
/// long soft shadows and sun shafts through the mist, a neutral noon, a blue moonlit night.
/// </summary>
public partial class SkyController : Node
{
    /// <summary>How thick the low mist over the water gets at dawn (and, thinner, at dusk) on a calm day.</summary>
    [Export(PropertyHint.Range, "0,2")] public float MistStrength { get; set; } = 1f;

    /// <summary>How strongly the sun's rays show in the haze (volumetric fog energy at the golden hour).</summary>
    [Export(PropertyHint.Range, "0,4")] public float ShaftStrength { get; set; } = 2.2f;

    /// <summary>Height (m above the water) the morning mist reaches.</summary>
    [Export] public float MistHeight { get; set; } = 2.5f;

    // hour, sky top, sky horizon
    private static readonly (float Hour, Color Top, Color Horizon)[] Keys =
    {
        (0f, new Color(0.02f, 0.03f, 0.08f), new Color(0.05f, 0.07f, 0.15f)),
        (5f, new Color(0.04f, 0.06f, 0.16f), new Color(0.10f, 0.12f, 0.25f)),
        (6f, new Color(0.20f, 0.30f, 0.55f), new Color(0.90f, 0.55f, 0.40f)),
        (7.5f, new Color(0.30f, 0.52f, 0.85f), new Color(0.85f, 0.80f, 0.70f)),
        (12f, new Color(0.25f, 0.50f, 0.90f), new Color(0.70f, 0.82f, 0.95f)),
        (17f, new Color(0.30f, 0.50f, 0.85f), new Color(0.85f, 0.80f, 0.70f)),
        (19f, new Color(0.25f, 0.30f, 0.55f), new Color(0.95f, 0.52f, 0.32f)),
        (20.5f, new Color(0.06f, 0.08f, 0.20f), new Color(0.35f, 0.20f, 0.25f)),
        (22f, new Color(0.02f, 0.03f, 0.08f), new Color(0.05f, 0.07f, 0.15f)),
        (24f, new Color(0.02f, 0.03f, 0.08f), new Color(0.05f, 0.07f, 0.15f)),
    };

    private Godot.Environment _env = null!;
    private ShaderMaterial _skyMaterial = null!;
    private float _cloudTime;
    private DirectionalLight3D _sun = null!;
    private DirectionalLight3D _moon = null!;
    private MultiMeshInstance3D _rainFx = null!;
    private MeshInstance3D _stars = null!;
    private ShaderMaterial _starMaterial = null!;
    private float _cloud;
    private float _rain;
    private float _wind = 0.3f;
    private float _groundWet;
    private bool _wetStarted;

    /// <summary>Extra fog on top of the weather (mist over a marsh, haze in the hills); set per map.</summary>
    public float FogBoost { get; set; }

    public WorldState? World { get; set; }
    public Water? Water { get; set; }

    /// <summary>The terrain, told how much sunlight reaches the lake bottom (caustics).</summary>
    public LakeBed? Bed { get; set; }

    /// <summary>Rain and the star dome follow this node (the camera).</summary>
    public Node3D? Follow { get; set; }

    public override void _Ready()
    {
        // Physically based sky with clouds (shaders/sky.gdshader); its radiance lights and reflects the scene.
        _skyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky.gdshader") };
        _skyMaterial.SetShaderParameter("cloud_noise", ProceduralTextures.Noise(71, 0.002f, 2048, octaves: 6));
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = _skyMaterial,
                RadianceSize = Sky.RadianceSizeEnum.Size256,
                ProcessMode = Sky.ProcessModeEnum.Incremental,
            },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapExposure = 1f,
            FogEnabled = true,
            FogDensity = 0.002f,
            FogAerialPerspective = 0.65f,
            FogSkyAffect = 0f,
        };
        AddChild(new WorldEnvironment { Environment = _env });

        _sun = new DirectionalLight3D { ShadowEnabled = true };
        AddChild(_sun);
        // The moon lights the scene but is not drawn by the sky shader (its LIGHT0 is always the sun).
        _moon = new DirectionalLight3D { LightColor = new Color(0.6f, 0.7f, 1f), SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly };
        AddChild(_moon);
        ApplyQuality();
        GraphicsQuality.Changed += ApplyQuality;

        BuildRain();
        BuildStars();
        Apply();
    }

    public override void _ExitTree() => GraphicsQuality.Changed -= ApplyQuality;

    private void ApplyQuality()
    {
        _skyMaterial.SetShaderParameter("stylize", 0.35f);   // a touch of painted sky over the physical one
        _applied = (float.NaN, 0f, 0f, 0f);     // re-apply fog for the style
        GraphicsQuality.ApplyEnvironment(_env);
        GraphicsQuality.ApplySun(_sun);
        if (IsInsideTree())
            GraphicsQuality.ApplyViewport(GetViewport());
    }

    public override void _EnterTree()
    {
        if (_env != null)
            ApplyQuality();
    }

    private void BuildStars()
    {
        _starMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/stars.gdshader"), RenderPriority = -10 };
        _starMaterial.SetShaderParameter("cloud_noise", _skyMaterial.GetShaderParameter("cloud_noise"));
        _stars = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 900f, Height = 1800f, RadialSegments = 32, Rings = 16 },
            MaterialOverride = _starMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-1000, -1000, -1000), new Vector3(2000, 2000, 2000)),
        };
        AddChild(_stars);
    }

    /// <summary>Most drops at full rain; light rain shows a share of them.</summary>
    public const int RainDrops = 7000;

    /// <summary>
    /// Rain is a MultiMesh animated in shaders/rain.gdshader, not GPUParticles3D: particles drawn by two viewports
    /// (a second viewport sharing the world) with motion vectors crash Godot 4.3
    /// ("particles_get_instance_buffer_motion_vectors_offsets: Parameter particles is null").
    /// </summary>
    private void BuildRain()
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = new Vector2(0.02f, 0.45f) },
            InstanceCount = RainDrops,
        };
        var rng = new RandomNumberGenerator { Seed = 77 };
        for (int i = 0; i < RainDrops; i++)
        {
            mm.SetInstanceTransform(i, Transform3D.Identity);
            mm.SetInstanceCustomData(i, new Color(rng.Randf(), rng.Randf(), rng.Randf(), 0f));
        }
        mm.VisibleInstanceCount = 0;
        _rainFx = new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/rain.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-30, -5, -30), new Vector3(60, 25, 60)),
            Visible = false,
        };
        AddChild(_rainFx);
    }

    /// <summary>Drops currently drawn (0 when it is not raining).</summary>
    public int VisibleRainDrops => _rainFx.Visible ? _rainFx.Multimesh.VisibleInstanceCount : 0;

    // ------------------------------------------------------------------ mist banks and lightning
    private MultiMeshInstance3D? _mistBanks;
    private ShaderMaterial? _mistMaterial;
    private float _mistShown = -1f;
    private bool _mistHooked;
    private DirectionalLight3D? _lightning;
    private float _flash, _nextFlash = 25f;
    private readonly RandomNumberGenerator _stormRng = new();

    /// <summary>A lightning flash lit the sky (the thunder follows after the seconds given: Audio plays it).</summary>
    public event Action<float>? Lightning;

    /// <summary>
    /// Mist banks: thin horizontal layers over the lake (shaders/mist_bank.gdshader), faded in and out by MistAt (dawn
    /// and dusk, calm). One MultiMesh, drawn only while there is mist; land shows through where they pass over it (the
    /// layers lie under 1.5 m, below the banks' tops).
    /// </summary>
    private void BuildMistBanks()
    {
        if (Bed?.Location == null)
            return;
        if (!_mistHooked)
        {
            // A new map: new banks over its water.
            _mistHooked = true;
            Bed.Rebuilt += () =>
            {
                _mistBanks?.QueueFree();
                _mistBanks = null;
                _mistShown = -1f;
            };
        }
        _mistMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/mist_bank.gdshader") };
        _mistMaterial.SetShaderParameter("noise_tex", Graphics.ProceduralTextures.Noise(91, 0.01f, 256, octaves: 3));
        // Five layers 0.1-1.4 m over the water, as big as the map.
        float w = LakeBed.XMax - LakeBed.XMin + 120f, d = LakeBed.ZMax - LakeBed.ZMin + 120f;
        var plane = new PlaneMesh { Size = new Vector2(w, d), SubdivideWidth = 0, SubdivideDepth = 0 };
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = plane };
        mm.InstanceCount = 5;
        var centre = new Vector3((LakeBed.XMin + LakeBed.XMax) / 2f, 0f, (LakeBed.ZMin + LakeBed.ZMax) / 2f);
        for (int i = 0; i < 5; i++)
        {
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, centre + Vector3.Up * (0.1f + i * 0.32f)));
            mm.SetInstanceCustomData(i, new Color(i / 4f, 0f, 0f, 0f));
        }
        _mistBanks = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = _mistMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        AddChild(_mistBanks);
    }

    private void UpdateMistAndStorm(float dt, float hour, float elevation)
    {
        if (_mistBanks == null && Bed?.Location != null)
            BuildMistBanks();
        if (_mistBanks != null && _mistMaterial != null)
        {
            float mist = MistAt(hour, _wind, _rain) * MistStrength;
            float shown = MathF.Round(mist * 40f) / 40f;
            if (MathF.Abs(shown - _mistShown) > 0.001f)
            {
                _mistShown = shown;
                _mistBanks.Visible = shown > 0.02f;
                _mistMaterial.SetShaderParameter("density", shown * 0.9f);
                // Dawn mist glows with the low sun; at dusk it goes blue-grey.
                var tint = SunColour(Math.Clamp(elevation, 0f, 1f)).Lerp(new Color(0.75f, 0.8f, 0.86f), 0.6f) * Math.Clamp(0.18f + elevation * 2.2f, 0.18f, 0.85f);
                _mistMaterial.SetShaderParameter("mist_color", tint);
            }
        }
        // A storm: in heavy rain with a strong wind, lightning every 15-60 s: the sky and land flash for a moment.
        bool storm = _rain > 0.7f && _wind > 0.6f;
        if (_lightning == null)
        {
            _lightning = new DirectionalLight3D { LightEnergy = 0f, LightColor = new Color(0.82f, 0.86f, 1f), RotationDegrees = new Vector3(-70f, 30f, 0f), ShadowEnabled = false };
            AddChild(_lightning);
        }
        if (storm)
        {
            _nextFlash -= dt;
            if (_nextFlash <= 0f)
            {
                _flash = 1f;
                _nextFlash = _stormRng.RandfRange(15f, 60f);
                Lightning?.Invoke(_stormRng.RandfRange(0.8f, 6f));
            }
        }
        if (_flash > 0f || _lightning.LightEnergy > 0f)
        {
            _flash = Mathf.MoveToward(_flash, 0f, dt * 5f);
            // Two quick pulses, as lightning flickers.
            float pulse = _flash * (0.6f + 0.4f * MathF.Sin(_flash * 40f));
            _lightning.LightEnergy = 3.5f * pulse;
        }
    }

    /// <summary>A flash now (tests and screenshots).</summary>
    public void FlashLightning() => (_flash, _nextFlash) = (1f, _stormRng.RandfRange(15f, 60f));

    public bool Flashing => _flash > 0.05f;

    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("sky");
        if (World == null)
            return;
        float dt = (float)delta;
        _cloud = Mathf.MoveToward(_cloud, World.CloudTarget, 0.25f * dt);
        _rain = Mathf.MoveToward(_rain, World.RainTarget, 0.3f * dt);
        _wind = Mathf.MoveToward(_wind, World.WindTarget, 0.2f * dt);
        _cloudTime += dt * (1f + _wind * 4f);
        if (Water != null)
        {
            Water.Wind = _wind;
            Water.Rain = _rain;
        }
        // The ground soaks up rain in a couple of minutes and dries in about ten (a map opened in the rain starts wet).
        if (!_wetStarted)
        {
            _wetStarted = true;
            _groundWet = World.RainTarget > 0.05f ? Math.Min(1f, World.RainTarget * 1.4f) : 0f;
        }
        _groundWet = Mathf.MoveToward(_groundWet, _rain > 0.05f ? Math.Min(1f, _rain * 1.4f) : 0f, (_rain > 0.05f ? 0.01f : 0.0016f) * dt * 60f / 60f);
        if (Bed != null)
            Bed.Wetness = _groundWet;
        Apply();
        float hourNow = World.Hour;
        float elevationNow = MathF.Sin((hourNow - 6f) / 12f * MathF.PI);
        UpdateMistAndStorm(dt, hourNow, elevationNow);
    }

    private static (Color Top, Color Horizon) SampleSky(float hour)
    {
        for (int i = 0; i < Keys.Length - 1; i++)
        {
            var (ha, ta, ra) = Keys[i];
            var (hb, tb, rb) = Keys[i + 1];
            if (hour >= ha && hour <= hb)
            {
                float t = Mathf.InverseLerp(ha, hb, hour);
                return (ta.Lerp(tb, t), ra.Lerp(rb, t));
            }
        }
        return (Keys[0].Top, Keys[0].Horizon);
    }

    private (float Hour, float Cloud, float Rain, float Fog) _applied = (float.NaN, 0f, 0f, 0f);

    /// <summary>A bell curve round <paramref name="centre"/> (hours) of the given width.</summary>
    private static float Bump(float hour, float centre, float width)
    {
        float d = (hour - centre) / width;
        return MathF.Exp(-d * d);
    }

    /// <summary>The sun's colour by its elevation: deep orange on the horizon, gold low in the sky, white at noon.</summary>
    public static Color SunColour(float elevation)
    {
        var horizon = new Color(1f, 0.42f, 0.18f);
        var golden = new Color(1f, 0.74f, 0.46f);
        var noon = new Color(1f, 0.97f, 0.92f);
        float e = Math.Clamp(elevation, 0f, 1f);
        return e < 0.18f ? horizon.Lerp(golden, e / 0.18f) : golden.Lerp(noon, Math.Clamp((e - 0.18f) / 0.35f, 0f, 1f));
    }

    /// <summary>The twilight's strength by the sun's elevation (sin): 1 just after sunset, 0 at -0.18 and above 0.12.</summary>
    public static float TwilightAt(float elevation) =>
        Math.Clamp((elevation + 0.18f) / 0.16f, 0f, 1f) * (1f - Math.Clamp((elevation - 0.02f) / 0.1f, 0f, 1f));

    /// <summary>Low mist over the water: thick after a calm dawn, thinner at dusk, gone in wind and rain (0..1).</summary>
    public static float MistAt(float hour, float wind, float rain) =>
        (Bump(hour, 6.5f, 1.2f) + 0.45f * Bump(hour, 19.3f, 0.9f)) * Math.Clamp(1f - wind * 1.1f, 0f, 1f) * (1f - rain * 0.5f);

    private void Apply()
    {
        // Clouds drift through a global shader parameter: it moves the visible sky every frame without making Godot
        // re-bake the sky's lighting (radiance) cubemap, which is what caused periodic hitches.
        RenderingServer.GlobalShaderParameterSet("cloud_drift", _cloudTime);
        if (Follow != null)
        {
            _stars.GlobalPosition = Follow.GlobalPosition;
            _rainFx.GlobalPosition = new Vector3(Follow.GlobalPosition.X, 0f, Follow.GlobalPosition.Z);
        }

        // Everything else (sun, sky colours, fog) only changes in steps: 2 game minutes (0.5 degree of sun) or a
        // noticeable change of the weather. Moving the sun or touching the sky material every frame re-bakes the sky.
        float hourStep = MathF.Floor((World?.Hour ?? 12f) * 30f) / 30f;
        var state = (hourStep, MathF.Round(_cloud * 50f) / 50f, MathF.Round(_rain * 50f) / 50f, FogBoost);
        if (state.Equals(_applied))
            return;
        _applied = state;
        float hour = hourStep;
        float el = MathF.Sin((hour - 6f) / 24f * Mathf.Tau);
        float elDeg = Mathf.RadToDeg(MathF.Asin(Math.Clamp(el, -1f, 1f)));
        float day = Math.Clamp(el * 2.2f + 0.25f, 0f, 1f);

        var (_, hor) = SampleSky(hour);
        var grey = new Color(0.55f, 0.58f, 0.62f) * Mathf.Lerp(0.12f, 1f, day);
        grey.A = 1f;
        hor = hor.Lerp(grey * 1.05f, _cloud * 0.85f);
        float night = Math.Clamp(-el * 3f, 0f, 1f);
        _skyMaterial.SetShaderParameter("cloud_cover", Math.Clamp(0.22f + _cloud * 0.78f, 0f, 1f));
        _skyMaterial.SetShaderParameter("night", night);
        // Twilight: from the sun just up (el 0.06) to about 10 degrees under the horizon.
        float twilight = TwilightAt(el) * (1f - _cloud * 0.7f);
        _skyMaterial.SetShaderParameter("twilight", twilight);
        var towardSun = new Basis(Vector3.Up, Mathf.DegToRad(-30f)) * Vector3.Back;     // the sun's azimuth (its light's +Z)
        _skyMaterial.SetShaderParameter("sun_flat", new Vector3(towardSun.X, 0f, towardSun.Z));
        // Haze: more Mie scattering in rain and mist.
        _skyMaterial.SetShaderParameter("turbidity", 4f + _cloud * 6f + _rain * 20f + FogBoost * 2000f);

        // Keep the sun just above the horizon at dusk so the sky shader still gets a sunset; it fades out at night.
        _sun.RotationDegrees = new Vector3(-MathF.Max(elDeg, -4f), -30f, 0f);
        _sun.LightColor = SunColour(el);
        _sun.ShadowEnabled = _cloud < 0.75f && day > 0.02f;
        _sun.Visible = el > -0.12f;
        // For shaders that light themselves (the fishing line's glint): the sun's direction, colour and the daylight.
        RenderingServer.GlobalShaderParameterSet("sun_dir", _sun.GlobalTransform.Basis.Z.Normalized());
        RenderingServer.GlobalShaderParameterSet("sun_color", _sun.LightColor);
        RenderingServer.GlobalShaderParameterSet("daylight", Math.Clamp(day * (1f - _cloud * 0.5f) + 0.15f * Core.Methods.Moon.Illumination(World?.MoonPhase ?? 0f) * (1f - day), 0f, 1f));

        _moon.RotationDegrees = new Vector3(elDeg, 150f, 0f);
        _moon.LightEnergy = 0.25f * (0.2f + 0.8f * Core.Methods.Moon.Illumination(World.MoonPhase)) * Math.Clamp(-el * 2.5f, 0f, 1f) * (1f - _cloud * 0.6f);

        if (Bed != null)
            Bed.Daylight = day * (1f - _cloud * 0.85f);
        // Valheim-like light: deep shadows, a strong sun that throws shafts through the fog, haze tinted by the time
        // of day, and moonlit nights (part of the ambient light is a cool blue instead of the near-black night sky).
        // Golden hour: the sun low (but up) in a clear sky. Shadows grow long and soft, the shade gets bluer and a
        // little deeper so the warm light stands out, and the rays show in the haze.
        float golden = Math.Clamp(1f - MathF.Abs(el - 0.12f) / 0.16f, 0f, 1f) * (1f - _cloud * 0.8f);
        float mist = MistAt(hour, _wind, _rain) * MistStrength;
        _env.AmbientLightEnergy = Mathf.Lerp(0.75f, 0.72f, day) * (1f - 0.18f * golden);
        _env.AmbientLightColor = new Color(0.36f, 0.45f, 0.66f).Lerp(new Color(0.3f, 0.42f, 0.7f), golden);
        _env.AmbientLightSkyContribution = Mathf.Lerp(0.35f, 1f, day);
        // Exposure: the eye opens up at night and a little in the low light of dusk and dawn.
        _env.TonemapExposure = Mathf.Lerp(1.9f, 1.05f, day) + 0.12f * golden;
        // A clear day still has haze: the far shore (150-300 m) loses a fifth of its colour to the sky.
        _env.FogDensity = Mathf.Lerp(0.0016f, 0.004f, _cloud) + _rain * 0.006f + FogBoost + 0.0006f * mist;
        // The mist lies low over the water: denser below MistHeight (height fog), and in the volumetric fog too.
        _env.FogHeight = MistHeight;
        _env.FogHeightDensity = 0.025f * mist;
        _env.VolumetricFogDensity = (0.003f + _cloud * 0.004f) * 0.8f + _rain * 0.01f + FogBoost * 2f + 0.005f * mist;
        _sun.LightEnergy = 1.9f * day * (1f - _cloud * 0.65f);
        // A low sun lights the haze less, or it would paint the whole sky red; at the golden hour the rays come out.
        _sun.LightVolumetricFogEnergy = Mathf.Lerp(0.3f, 1.6f, Math.Clamp(el * 3f, 0f, 1f)) + ShaftStrength * golden * (0.4f + 0.6f * mist);
        // Long shadows of a low sun are softer; midday ones are crisp.
        _sun.ShadowBlur = Mathf.Lerp(2.4f, 1.1f, Math.Clamp(el * 2.5f, 0f, 1f));
        _env.FogAerialPerspective = 0.75f;
        // The haze is lit by the sky: bright by day, a glow at twilight, nearly black at night (a grey fog at night
        // washed the far forest out and made the mountains brighter than the sky).
        float hazeLight = Math.Clamp(day + 0.3f * twilight, 0.06f, 1f);
        _env.FogLightColor = hor.Lerp(new Color(0.55f, 0.66f, 0.74f), 0.4f).Lerp(SunColour(el) * 0.85f, 0.2f * golden) * hazeLight;
        _env.FogLightEnergy = hazeLight;
        _env.VolumetricFogAlbedo = new Color(0.9f, 0.88f, 0.84f).Lerp(hor, 0.35f);
        // Moonlight: a cold blue-white, brighter under a full moon.
        _moon.LightColor = new Color(0.58f, 0.68f, 1f);

        // Stars come out only once the twilight has faded, and hide behind the clouds (stars.gdshader reads them).
        _starMaterial.SetShaderParameter("night", Math.Clamp((-el - 0.08f) * 6f, 0f, 1f) * (1f - _cloud * 0.6f));
        _starMaterial.SetShaderParameter("cloud_cover", Math.Clamp(0.22f + _cloud * 0.78f, 0f, 1f));
        int drops = _rain > 0.02f ? (int)(RainDrops * Math.Clamp(_rain, 0.05f, 1f)) : 0;
        if (drops != _rainFx.Multimesh.VisibleInstanceCount)
        {
            _rainFx.Multimesh.VisibleInstanceCount = drops;
            _rainFx.Visible = drops > 0;
        }
    }
}
