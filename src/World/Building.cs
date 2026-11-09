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
        foreach (var m in _glow)
        {
            m.EmissionEnabled = night > 0.02f;
            m.EmissionEnergyMultiplier = 0.2f + night * 2.2f;
        }
        foreach (var l in _lights)
            l.LightEnergy = night * 1.1f;
    }

    public override void _Process(double delta)
    {
        foreach (var s in _spinners)
            s.RotateZ((float)delta * 0.5f);
    }
}
