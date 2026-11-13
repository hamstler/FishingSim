using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Data;

namespace FishingSim.World;

/// <summary>A placed building or prop. Windows and lamp heads glow after dark.</summary>
public partial class Building : Node3D
{
    private readonly List<StandardMaterial3D> _glow = new();
    private readonly List<OmniLight3D> _lights = new();
    private readonly List<Node3D> _spinners = new();

    public BuildingSpec Spec { get; set; } = null!;

    /// <summary>The sign text, or empty for plain scenery.</summary>
    public string Title { get; set; } = "";

    /// <summary>True while the windows or lamp heads are lit (after dark).</summary>
    public bool IsGlowing => _glow.Exists(m => m.EmissionEnabled);

    public void AddGlow(StandardMaterial3D material) => _glow.Add(material);

    public void AddLight(OmniLight3D light) => _lights.Add(light);

    /// <summary>Registers a node that turns slowly (windmill blades).</summary>
    public void AddSpinner(Node3D node) => _spinners.Add(node);

    /// <summary>0 = night, 1 = full day.</summary>
    public void SetDaylight(float daylight)
    {
        float night = 1f - Math.Clamp(daylight * 1.8f, 0f, 1f);
        _night = night;
        foreach (var m in _glow)
        {
            m.EmissionEnabled = night > 0.02f;
            m.EmissionEnergyMultiplier = 0.2f + night * 2.2f;
        }
        foreach (var l in _lights)
            l.LightEnergy = night * 1.1f;
    }

    private float _night;
    private float _time, _farCheck;
    private bool _near = true;
    private readonly float _phase = GD.Randf() * 100f;

    /// <summary>Flames flicker only this close to the camera (further off nobody sees it).</summary>
    private const float FlickerRange = 70f;

    public override void _Ready()
    {
        // Most buildings have nothing that moves or glows: they take no time each frame at all.
        SetProcess(_spinners.Count > 0 || _glow.Count > 0 || _lights.Count > 0);
    }

    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("building");
        foreach (var s in _spinners)
            s.RotateZ((float)delta * 0.5f);
        if (_night <= 0.02f)
            return;
        _farCheck -= (float)delta;
        if (_farCheck <= 0f)
        {
            _farCheck = 0.5f + _phase % 0.3f;
            var cam = GetViewport()?.GetCamera3D();
            _near = cam == null || cam.GlobalPosition.DistanceSquaredTo(GlobalPosition) < FlickerRange * FlickerRange;
        }
        if (!_near)
            return;
        // Candle and lantern flames flicker.
        _time += (float)delta;
        float t = _time + _phase;
        float flicker = 0.85f + 0.1f * MathF.Sin(t * 13f) + 0.06f * MathF.Sin(t * 29f + 1.3f) + 0.04f * MathF.Sin(t * 7f);
        foreach (var m in _glow)
            m.EmissionEnergyMultiplier = (0.2f + _night * 2.2f) * flicker;
        foreach (var l in _lights)
            l.LightEnergy = _night * 1.1f * flicker;
    }
}
