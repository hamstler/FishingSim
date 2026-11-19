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

    /// <summary>Where real trees should grow on this landmark (local positions of their feet; Scenery plants them with
    /// the forest's own trees, so they look like every other tree).</summary>
    public List<Vector3> TreeSpots { get; } = new();

    /// <summary>The sign text, or empty for plain scenery.</summary>
    public string Title { get; set; } = "";

    /// <summary>True while the windows or lamp heads are lit (after dark).</summary>
    public bool IsGlowing => _glow.Exists(m => m.EmissionEnabled);

    public void AddGlow(StandardMaterial3D material) => _glow.Add(material);

    public void AddLight(OmniLight3D light, float weight = 1f)
    {
        _lights.Add(light);
        _weights.Add(weight);
    }

    private readonly List<float> _weights = new();

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
        for (int i = 0; i < _lights.Count; i++)
        {
            _lights[i].LightEnergy = night * 1.1f * _weights[i];
            // By day the lights are out of the scene (Forward+ still binned and looped over lights with no energy).
            _lights[i].Visible = night > 0.02f;
        }
    }

    private float _night;
    private float _time, _farCheck;
    private bool _near = true;
    private readonly float _phase = GD.Randf() * 100f;

    /// <summary>Flames flicker only this close to the camera (further off nobody sees it).</summary>
    private const float FlickerRange = 70f;

    /// <summary>
    /// Joins the building's static parts (walls, logs, beams, shutters, shingles, signs' boards, barrels...) into one
    /// mesh with a surface per material: a house was dozens of separate MeshInstances, one draw call each in every
    /// pass. Left alone: what moves (spinners), people (skeletons) and anything with a script, Label3D lettering, and
    /// parts whose shader is not a plain StandardMaterial3D or the building shaders (those might read object space).
    /// </summary>
    private void MergeStatic()
    {
        var inv = GlobalTransform.AffineInverse();
        var groups = new Dictionary<(Material Mat, GeometryInstance3D.ShadowCastingSetting Shadow, int Format), List<(Mesh Mesh, int Surface, Transform3D At)>>();
        var merged = new List<MeshInstance3D>();
        foreach (var node in FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)node;
            if (mi.Mesh == null || mi.Skin != null || !Mergeable(mi))
                continue;
            var at = inv * mi.GlobalTransform;
            bool ok = true;
            var parts = new List<(Material, int, int)>();
            for (int s = 0; s < mi.Mesh.GetSurfaceCount() && ok; s++)
            {
                var mat = mi.GetActiveMaterial(s);
                ok = mat != null && (mat is StandardMaterial3D || mat is ShaderMaterial { Shader: { } sh } && sh.ResourcePath.StartsWith("res://shaders/building"))
                     && (mi.Mesh is PrimitiveMesh || mi.Mesh is ArrayMesh am && am.SurfaceGetPrimitiveType(s) == Mesh.PrimitiveType.Triangles);
                if (ok)
                    parts.Add((mat!, s, FormatOf(mi.Mesh, s)));
            }
            if (!ok || parts.Count == 0)
                continue;
            foreach (var (mat, s, format) in parts)
            {
                var key = (mat, mi.CastShadow, format);
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new();
                list.Add((mi.Mesh, s, at));
            }
            merged.Add(mi);
        }
        if (merged.Count < 2)
            return;
        // One mesh per shadow setting, a surface per material (and vertex format).
        var meshes = new Dictionary<GeometryInstance3D.ShadowCastingSetting, ArrayMesh>();
        foreach (var ((mat, shadow, _), list) in groups)
        {
            using var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            foreach (var (mesh, s, at) in list)
                st.AppendFrom(mesh, s, at);
            st.SetMaterial(mat);
            if (!meshes.TryGetValue(shadow, out var am))
                meshes[shadow] = am = new ArrayMesh();
            st.Commit(am);
        }
        foreach (var (shadow, am) in meshes)
            AddChild(new MeshInstance3D { Name = $"Merged{shadow}", Mesh = am, CastShadow = shadow });
        MergedParts += merged.Count;
        MergedInto += meshes.Count;
        // The parts go (one that holds children, a collider say, stays as an empty node).
        foreach (var mi in merged)
        {
            if (mi.GetChildCount() > 0)
                mi.Mesh = null;
            else
            {
                mi.GetParent().RemoveChild(mi);
                mi.QueueFree();
            }
        }
    }

    /// <summary>All buildings' parts joined so far, and the meshes they became (FISHSIM_PERF prints them).</summary>
    public static int MergedParts, MergedInto;

    private bool Mergeable(Node node)
    {
        for (var n = node; n != null && n != this; n = n.GetParent())
            if ((n is Node3D n3 && _spinners.Contains(n3)) || n is Skeleton3D || n is Label3D
                || n.GetScript().VariantType != Variant.Type.Nil)
                return false;
        return true;
    }

    /// <summary>Which vertex arrays a surface has (normals, tangents, colours, UV, UV2): surfaces join only alike.</summary>
    private static int FormatOf(Mesh mesh, int surface)
    {
        var arrays = mesh.SurfaceGetArrays(surface);
        int f = 0;
        int[] kinds = { (int)Mesh.ArrayType.Normal, (int)Mesh.ArrayType.Tangent, (int)Mesh.ArrayType.Color, (int)Mesh.ArrayType.TexUV, (int)Mesh.ArrayType.TexUV2 };
        for (int i = 0; i < kinds.Length; i++)
            if (arrays[kinds[i]].VariantType != Variant.Type.Nil)
                f |= 1 << i;
        return f;
    }

    public override void _Ready()
    {
        MergeStatic();
        // The weathered walls (shaders/building.gdshader) measure their damp foot from the building's ground.
        float ground = GlobalPosition.Y;
        foreach (var node in FindChildren("*", "GeometryInstance3D", true, false))
            if (node is GeometryInstance3D g && g is MeshInstance3D { MaterialOverride: ShaderMaterial { Shader: { } sh } }
                && sh.ResourcePath.StartsWith("res://shaders/building"))
                g.SetInstanceShaderParameter("base_y", ground);
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
        for (int i = 0; i < _lights.Count; i++)
            _lights[i].LightEnergy = _night * 1.1f * flicker * _weights[i];
    }
}
