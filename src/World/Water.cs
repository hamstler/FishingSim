using System;
using Godot;

namespace FishingSim.World;

/// <summary>
/// The lake's surface (shaders/water.gdshader, the "water controller"): Gerstner wind waves in a fan of directions round
/// the wind (shaders/water_waves.gdshaderinc), two drifting ripple normal maps, the bottom and the fish seen through it
/// with the water's absorption and refraction, the sky and the shore reflected by Fresnel (screen-space reflections by
/// the Reflections setting), sun glints, foam lapping the shore and white caps in a strong wind, rain rings.
/// <see cref="HeightAt"/> mirrors the shader's waves exactly so the float and everything floating rides them.
/// </summary>
public partial class Water : MeshInstance3D
{
    public const float MaxDepth = 16f;
    public const float MapCell = 1.5f;

    /// <summary>The wave table of shaders/water_waves.gdshaderinc: angle from the wind (rad), wavelength (m), share of
    /// the amplitude, phase. Waves 0..VertexWaves-1 move the surface; the rest only shape the light.</summary>
    private static readonly Vector4[] Waves =
    {
        new(0f, 9f, 0.40f, 0f), new(0.49f, 6.2f, 0.26f, 1.7f), new(-0.58f, 4.4f, 0.18f, 4.1f), new(1.08f, 3.1f, 0.10f, 2.6f),
        new(-0.26f, 1.9f, 0.05f, 5.3f), new(0.79f, 1.25f, 0.035f, 0.9f), new(-1.22f, 0.8f, 0.025f, 3.7f),
    };

    public const int VertexWaves = 4;

    private ShaderMaterial _material = null!;
    private LakeBed? _bed;
    private float _waveTime;

    /// <summary>0..1, eased by the sky controller.</summary>
    [Export] public float Wind { get; set; } = 0.3f;

    /// <summary>Where the wind blows to (world XZ): the waves fan out round it and the ripples drift along it.</summary>
    [Export] public Vector2 WindDirection { get; set; } = new(0.8f, 0.6f);

    /// <summary>How far the water swings round in its orbits (Gerstner steepness): sharper crests, flatter troughs.</summary>
    [Export(PropertyHint.Range, "0,3")] public float Choppiness { get; set; } = 1.4f;

    /// <summary>Strength of the fine wind ripples (the drifting normal maps).</summary>
    [Export(PropertyHint.Range, "0,2")] public float Detail { get; set; } = 1f;

    /// <summary>0..1, eased by the sky controller.</summary>
    public float Rain { get; set; }

    public override void _Ready()
    {
        // A vertex every metre: the shortest moving wave (3.1 m) still gets three per wavelength.
        Mesh = new PlaneMesh { Size = new Vector2(300, 300), SubdivideWidth = 300, SubdivideDepth = 300 };
        Position = new Vector3(0, 0, -80);
        CustomAabb = new Aabb(new Vector3(-160, -1, -160), new Vector3(320, 2, 320));
        CastShadow = ShadowCastingSetting.Off;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
        _material.SetShaderParameter("foam_noise", Graphics.ProceduralTextures.Noise(53, 0.005f, 1024));
        _material.SetShaderParameter("ripple_normals", RippleNormals());
        MaterialOverride = _material;
        PushParameters();
        ApplyQuality();
        Graphics.GraphicsQuality.Changed += ApplyQuality;
    }

    /// <summary>Screen-space reflection steps by the graphics quality (Low: the sky's reflection only).</summary>
    private void ApplyQuality() => _material.SetShaderParameter("ssr_steps", Graphics.GraphicsQuality.WaterReflectionSteps);

    /// <summary>How high the swells run: a calm lake a centimetre or two, a windy one about ten.</summary>
    public float Amplitude => Mathf.Lerp(0.015f, 0.1f, Wind);

    /// <summary>The water's height over the world point (x, z): the long Gerstner waves, exactly as the shader moves
    /// the mesh (the rest point that sways over (x, z) is found by one step back, as in water_height()).</summary>
    public float HeightAt(float x, float z)
    {
        var p = new Vector2(x, z);
        var rest = p - Sway(p);
        return Gerstner(rest).Y;
    }

    private Vector2 Direction(int i)
    {
        var w = WindDirection.LengthSquared() > 1e-6f ? WindDirection.Normalized() : new Vector2(0.8f, 0.6f);
        float a = Waves[i].X;
        return new Vector2(w.X * MathF.Cos(a) - w.Y * MathF.Sin(a), w.X * MathF.Sin(a) + w.Y * MathF.Cos(a));
    }

    /// <summary>Sway (x, z) and height (y) of the rest point p over the moving waves (gerstner() in the shader).</summary>
    private Vector3 Gerstner(Vector2 p)
    {
        var d = Vector3.Zero;
        float amp = Amplitude;
        for (int i = 0; i < VertexWaves; i++)
        {
            float k = Mathf.Tau / Waves[i].Y;
            float omega = MathF.Sqrt(9.81f * k);
            var dir = Direction(i);
            float f = k * dir.Dot(p) - omega * _waveTime + Waves[i].W;
            float a = amp * Waves[i].Z;
            float q = Choppiness * a;
            d.X += dir.X * q * MathF.Cos(f);
            d.Z += dir.Y * q * MathF.Cos(f);
            d.Y += a * MathF.Sin(f);
        }
        return d;
    }

    private Vector2 Sway(Vector2 p)
    {
        var d = Gerstner(p);
        return new Vector2(d.X, d.Z);
    }

    /// <summary>
    /// A tileable map of wind ripples (512 px over one repeat): forty short waves with whole numbers of crests per
    /// repeat, mostly downwind (the map is laid along +X and turned to the wind by its drift), each as long as it is
    /// strong; RG holds the slope (dh/dx, dh/dz) / 2 + 0.5, read twice at two scales and drifts by the shader.
    /// </summary>
    private static ImageTexture RippleNormals()
    {
        const int size = 512;
        var rng = new Random(91);
        var waves = new (float Kx, float Kz, float A, float Phase)[40];
        for (int i = 0; i < waves.Length; i++)
        {
            int n = 2 + rng.Next(14);                                   // crests per repeat
            float angle = (float)(rng.NextDouble() * 2 - 1) * (i < 28 ? 0.8f : 3.1f);
            int kx = (int)MathF.Round(n * MathF.Cos(angle)), kz = (int)MathF.Round(n * MathF.Sin(angle));
            if (kx == 0 && kz == 0)
                kx = n;
            float len = MathF.Sqrt(kx * kx + kz * kz);
            waves[i] = (kx, kz, 0.9f / MathF.Pow(len, 1.4f), (float)rng.NextDouble() * Mathf.Tau);
        }
        var slopes = new float[size * size * 2];
        System.Threading.Tasks.Parallel.For(0, size, FishingSim.Graphics.Workers.Options, y =>
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size * Mathf.Tau, v = y / (float)size * Mathf.Tau;
                float sx = 0f, sz = 0f;
                foreach (var w in waves)
                {
                    float c = MathF.Cos(w.Kx * u + w.Kz * v + w.Phase) * w.A;
                    sx += c * w.Kx;
                    sz += c * w.Kz;
                }
                slopes[(y * size + x) * 2] = sx;
                slopes[(y * size + x) * 2 + 1] = sz;
            }
        });
        // Normalised so the steepest ripple fills the byte range (the shader scales it by wind and Detail).
        float peak = 1e-6f;
        foreach (float v in slopes)
            peak = MathF.Max(peak, MathF.Abs(v));
        var data = new byte[size * size * 4];
        for (int i = 0; i < size * size; i++)
        {
            data[i * 4] = (byte)Math.Clamp((int)((slopes[i * 2] / peak * 0.5f + 0.5f) * 255f), 0, 255);
            data[i * 4 + 1] = (byte)Math.Clamp((int)((slopes[i * 2 + 1] / peak * 0.5f + 0.5f) * 255f), 0, 255);
            data[i * 4 + 2] = 255;
            data[i * 4 + 3] = 255;
        }
        var img = Image.CreateFromData(size, size, false, Image.Format.Rgba8, data);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("water");
        _waveTime += (float)delta;
        PushParameters();
    }

    private Vector2 _pushedDirection;
    private float _pushedChop = -1f, _pushedRain = -1f, _pushedWind = -1f, _pushedDetail = -1f;

    private void PushParameters()
    {
        // The waves are global shader parameters: the water, the floating plants and the rise rings read the same.
        RenderingServer.GlobalShaderParameterSet("water_time", _waveTime);
        RenderingServer.GlobalShaderParameterSet("water_amp", Amplitude);
        if (WindDirection != _pushedDirection)
        {
            _pushedDirection = WindDirection;
            RenderingServer.GlobalShaderParameterSet("water_dir", WindDirection.LengthSquared() > 1e-6f ? WindDirection.Normalized() : new Vector2(0.8f, 0.6f));
        }
        if (Choppiness != _pushedChop)
        {
            _pushedChop = Choppiness;
            RenderingServer.GlobalShaderParameterSet("water_chop", Choppiness);
        }
        // Material values only when they change (the sky controller eases them slowly).
        if (MathF.Abs(Rain - _pushedRain) > 0.002f)
        {
            _pushedRain = Rain;
            _material.SetShaderParameter("rain", Rain);
        }
        if (MathF.Abs(Wind - _pushedWind) > 0.002f)
        {
            _pushedWind = Wind;
            _material.SetShaderParameter("wind", Wind);
        }
        if (Detail != _pushedDetail)
        {
            _pushedDetail = Detail;
            _material.SetShaderParameter("detail", Detail);
        }
    }

    /// <summary>0..1: how much surface glare is removed (polarized glasses make fish easier to see).</summary>
    public void SetGlareCut(float amount) => _material.SetShaderParameter("glare_cut", Math.Clamp(amount, 0f, 1f));

    /// <summary>How clear the lake is (metres; bigger = you see deeper). Set per map.</summary>
    public void SetClarity(float metres) => _material.SetShaderParameter("clarity", Math.Max(metres, 0.3f));

    /// <summary>Colours of the water over the shallows and over the deep parts (set per map).</summary>
    public void SetTint(Color shallow, Color deep)
    {
        _material.SetShaderParameter("shallow_color", shallow);
        _material.SetShaderParameter("deep_color", deep);
        // The light that reaches the bottom and back is coloured like the water itself: each channel fades as fast as
        // the shallows' colour lacks it (green = 1; red at most 3.2 times as fast).
        float g = Math.Max(shallow.G, 0.02f);
        _material.SetShaderParameter("extinction_tint", new Vector3(
            Math.Clamp(g / Math.Max(shallow.R, 0.02f), 1.2f, 3.2f), 1f, Math.Clamp(g / Math.Max(shallow.B, 0.02f), 0.6f, 2.2f)));
    }

    public void SetBed(LakeBed bed)
    {
        if (_bed != null)
            _bed.Rebuilt -= BuildDepthMap;
        _bed = bed;
        bed.Rebuilt += BuildDepthMap;
        BuildDepthMap();
    }

    private void BuildDepthMap()
    {
        if (_bed == null)
            return;
        int w = (int)MathF.Ceiling((LakeBed.XMax - LakeBed.XMin) / MapCell);
        int h = (int)MathF.Ceiling((LakeBed.ZMax - LakeBed.ZMin) / MapCell);
        var img = Image.CreateEmpty(w, h, false, Image.Format.L8);
        for (int iy = 0; iy < h; iy++)
        {
            for (int ix = 0; ix < w; ix++)
            {
                float d = _bed.DepthAt(LakeBed.XMin + (ix + 0.5f) * MapCell, LakeBed.ZMin + (iy + 0.5f) * MapCell);
                float v = Math.Clamp(d / MaxDepth, 0f, 1f);
                img.SetPixel(ix, iy, new Color(v, v, v));
            }
        }
        _material.SetShaderParameter("depth_map", ImageTexture.CreateFromImage(img));
        _material.SetShaderParameter("map_rect", new Vector4(LakeBed.XMin, LakeBed.ZMin, w * MapCell, h * MapCell));
        _material.SetShaderParameter("max_depth", MaxDepth);
    }

    public override void _ExitTree()
    {
        if (_bed != null)
            _bed.Rebuilt -= BuildDepthMap;
        Graphics.GraphicsQuality.Changed -= ApplyQuality;
    }
}
