using System;
using Godot;
using FishingSim.Graphics;

namespace FishingSim.World;

/// <summary>Drives sky colours, sun and moon, fog, stars, clouds (as a mood), rain and wind from <see cref="WorldState"/>.</summary>
public partial class SkyController : Node
{
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
    private GpuParticles3D _rainFx = null!;
    private MeshInstance3D _stars = null!;
    private ShaderMaterial _starMaterial = null!;
    private float _cloud;
    private float _rain;
    private float _wind = 0.3f;

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
        _skyMaterial.SetShaderParameter("cloud_noise", ProceduralTextures.Noise(71, 0.008f, 512, octaves: 4));
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
        _skyMaterial.SetShaderParameter("stylize", GraphicsQuality.Style == ArtStyle.PixelArt ? 0.75f : 0f);
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
        _stars = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 900f, Height = 1800f, RadialSegments = 32, Rings = 16 },
            MaterialOverride = _starMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-1000, -1000, -1000), new Vector3(2000, 2000, 2000)),
        };
        AddChild(_stars);
    }

    private void BuildRain()
    {
        var drops = new QuadMesh
        {
            Size = new Vector2(0.008f, 0.4f),
            Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.75f, 0.82f, 0.9f, 0.45f),
                BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
            },
        };
        _rainFx = new GpuParticles3D
        {
            Amount = 7000,
            Lifetime = 0.7,
            Emitting = false,
            VisibilityAabb = new Aabb(new Vector3(-40, -20, -40), new Vector3(80, 40, 80)),
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = new Vector3(22, 0.5f, 22),
                Direction = new Vector3(0.12f, -1f, 0f),
                Spread = 2f,
                InitialVelocityMin = 17f,
                InitialVelocityMax = 19f,
                Gravity = Vector3.Zero,
            },
            DrawPass1 = drops,
        };
        AddChild(_rainFx);
    }

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
        Apply();
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

    private void Apply()
    {
        // Clouds drift through a global shader parameter: it moves the visible sky every frame without making Godot
        // re-bake the sky's lighting (radiance) cubemap, which is what caused periodic hitches.
        RenderingServer.GlobalShaderParameterSet("cloud_drift", _cloudTime);
        if (Follow != null)
        {
            _stars.GlobalPosition = Follow.GlobalPosition;
            _rainFx.GlobalPosition = new Vector3(Follow.GlobalPosition.X, 12f, Follow.GlobalPosition.Z);
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
        // Haze: more Mie scattering in rain and mist.
        _skyMaterial.SetShaderParameter("turbidity", 4f + _cloud * 6f + _rain * 20f + FogBoost * 2000f);

        // Keep the sun just above the horizon at dusk so the sky shader still gets a sunset; it fades out at night.
        _sun.RotationDegrees = new Vector3(-MathF.Max(elDeg, -4f), -30f, 0f);
        _sun.LightEnergy = 1.7f * day * (1f - _cloud * 0.7f);
        _sun.LightColor = new Color(1f, 0.5f, 0.26f).Lerp(new Color(1f, 0.95f, 0.88f), Math.Clamp(el * 2.2f, 0f, 1f));
        _sun.ShadowEnabled = _cloud < 0.75f && day > 0.02f;
        // A low sun shining straight through the haze would paint the whole sky red: let it light the fog less.
        _sun.LightVolumetricFogEnergy = Mathf.Lerp(0.08f, 1f, Math.Clamp(el * 3f, 0f, 1f));
        _sun.Visible = el > -0.12f;

        _moon.RotationDegrees = new Vector3(elDeg, 150f, 0f);
        _moon.LightEnergy = 0.25f * Math.Clamp(-el * 2.5f, 0f, 1f) * (1f - _cloud * 0.6f);

        if (Bed != null)
            Bed.Daylight = day * (1f - _cloud * 0.85f);
        _env.AmbientLightEnergy = Mathf.Lerp(0.35f, 1f, day);
        _env.AmbientLightSkyContribution = 1f;
        _env.TonemapExposure = Mathf.Lerp(1.45f, 1f, day);   // eyes adapt at night
        _env.FogLightColor = hor * 0.9f;
        // Pixel art wants clean colours: much less haze unless the weather or the map calls for it.
        float haze = Graphics.GraphicsQuality.Style == Graphics.ArtStyle.PixelArt ? 0.35f : 1f;
        _env.FogDensity = Mathf.Lerp(0.0012f * haze, 0.004f, _cloud) + _rain * 0.006f + FogBoost;
        bool illustrated = Graphics.GraphicsQuality.Style == Graphics.ArtStyle.PixelArt;
        // Distant hills fade into the sky colour (aerial perspective), like the far planes of an illustration.
        _env.FogAerialPerspective = illustrated ? 0.95f : 0.65f;
        if (illustrated)
            _env.FogLightColor = new Color(0.62f, 0.8f, 0.93f).Lerp(hor * 0.9f, Math.Clamp(1f - day + _cloud, 0f, 1f));
        _env.VolumetricFogDensity = (0.003f + _cloud * 0.004f) * haze + _rain * 0.01f + FogBoost * 2f;

        _starMaterial.SetShaderParameter("night", Math.Clamp(-el * 3f, 0f, 1f) * (1f - _cloud));
        _rainFx.Emitting = _rain > 0.02f;
        _rainFx.AmountRatio = Math.Clamp(_rain, 0.05f, 1f);
    }
}
