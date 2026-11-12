using System;
using Godot;

namespace FishingSim.World;

/// <summary>
/// Animated water surface. Shape and colour come from the lake's depth map; waves grow with the wind.
/// <see cref="HeightAt"/> mirrors the shader's wave function so floating things ride the same waves.
/// </summary>
public partial class Water : MeshInstance3D
{
    public const float MaxDepth = 16f;
    public const float MapCell = 1.5f;

    private static readonly Vector2 D1 = new(0.8f, 0.6f);
    private static readonly Vector2 D2 = new(-0.5f, 0.866f);
    private static readonly Vector2 D3 = new(0.9f, -0.436f);

    private ShaderMaterial _material = null!;
    private LakeBed? _bed;
    private float _waveTime;

    /// <summary>0..1, eased by the sky controller.</summary>
    public float Wind { get; set; } = 0.3f;

    /// <summary>0..1, eased by the sky controller.</summary>
    public float Rain { get; set; }

    public override void _Ready()
    {
        Mesh = new PlaneMesh { Size = new Vector2(300, 300), SubdivideWidth = 150, SubdivideDepth = 150 };
        Position = new Vector3(0, 0, -80);
        CustomAabb = new Aabb(new Vector3(-160, -1, -160), new Vector3(320, 2, 320));
        CastShadow = ShadowCastingSetting.Off;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
        _material.SetShaderParameter("foam_noise", Graphics.ProceduralTextures.Noise(53, 0.02f, 256));
        MaterialOverride = _material;
        PushParameters();
    }

    public float Amplitude => Mathf.Lerp(0.012f, 0.07f, Wind);

    public float HeightAt(float x, float z)
    {
        var p = new Vector2(x, z);
        float w = 0.5f * MathF.Sin(p.Dot(D1) * 0.9f + _waveTime * 1.1f)
                + 0.3f * MathF.Sin(p.Dot(D2) * 1.7f + _waveTime * 1.6f)
                + 0.2f * MathF.Sin(p.Dot(D3) * 3.1f + _waveTime * 2.3f);
        return Amplitude * w;
    }

    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("water");
        _waveTime += (float)delta;
        PushParameters();
    }

    private void PushParameters()
    {
        _material.SetShaderParameter("wave_time", _waveTime);
        _material.SetShaderParameter("amp", Amplitude);
        // Floating plants (lily pads, pondweed) ride the same waves: shaders/floating.gdshader reads these.
        RenderingServer.GlobalShaderParameterSet("water_time", _waveTime);
        RenderingServer.GlobalShaderParameterSet("water_amp", Amplitude);
        _material.SetShaderParameter("rain", Rain);
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
    }
}
