using System;
using Godot;
using FishingSim.World;
using FishingSim.World.Props;

namespace FishingSim.Graphics;

/// <summary>
/// Dense grass blades around the player (the terrain shader carries the look further away). Clumps sit on a world
/// grid with hashed jitter, so re-centring as the player walks does not make the grass jump. Only grassy ground
/// gets blades: not sand, paths, building pads, steep rock or water. Radius and density follow the graphics quality.
/// </summary>
public partial class GrassField : Node3D
{
    private const float RecenterDistance = 7f;

    private LakeBed _bed = null!;
    private Node3D _follow = null!;
    private MultiMeshInstance3D _instance = null!;
    private Vector2 _centre = new(float.MaxValue, float.MaxValue);
    private static ArrayMesh? _clump;

    public int Count => _instance?.Multimesh?.InstanceCount ?? 0;

    public void Setup(LakeBed bed, Node3D follow)
    {
        _bed = bed;
        _follow = follow;
    }

    public override void _Ready()
    {
        _instance = new MultiMeshInstance3D
        {
            MaterialOverride = TreeFactory.GrassMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_instance);
        _bed.Rebuilt += Invalidate;
        GraphicsQuality.Changed += Invalidate;
    }

    public override void _ExitTree()
    {
        _bed.Rebuilt -= Invalidate;
        GraphicsQuality.Changed -= Invalidate;
    }

    private void Invalidate() => _centre = new Vector2(float.MaxValue, float.MaxValue);

    public override void _Process(double delta)
    {
        using var perfScope = Perf.Scope("grass");
        if (!IsInstanceValid(_follow))
            return;
        var p = new Vector2(_follow.GlobalPosition.X, _follow.GlobalPosition.Z);
        if (p.DistanceTo(_centre) > RecenterDistance)
        {
            _centre = p;
            // The very first field is built at once; later ones are spread over a few frames (no hitch while walking).
            if (_instance.Multimesh == null)
                Build(p);
            else
                _job = BuildSteps(p).GetEnumerator();
        }
        if (_job != null)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            long budget = System.Diagnostics.Stopwatch.Frequency / 500;     // ~2 ms per frame
            while (_job != null && System.Diagnostics.Stopwatch.GetTimestamp() - start < budget)
                if (!_job.MoveNext())
                    _job = null;
        }
    }

    private static float Hash(int x, int z, int k)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(k * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    private System.Collections.Generic.IEnumerator<bool>? _job;

    /// <summary>Places the clumps around <paramref name="centre"/> right away.</summary>
    public void Build(Vector2 centre)
    {
        _job = null;
        foreach (var _ in BuildSteps(centre)) { }
    }

    /// <summary>The build, one row of the grid per step; the new field replaces the old one only when complete.</summary>
    private System.Collections.Generic.IEnumerable<bool> BuildSteps(Vector2 centre)
    {
        _centre = centre;
        float radius = GraphicsQuality.GrassRadius;
        if (radius <= 0f || Renderer.IsHeadless)
        {
            _instance.Multimesh = null;
            yield break;
        }
        float spacing = GraphicsQuality.GrassSpacing;
        var loc = _bed.Location;
        var layout = _bed.Layout;
        var transforms = new System.Collections.Generic.List<Transform3D>();
        var colors = new System.Collections.Generic.List<Color>();
        int x0 = (int)MathF.Floor((centre.X - radius) / spacing), x1 = (int)MathF.Ceiling((centre.X + radius) / spacing);
        int z0 = (int)MathF.Floor((centre.Y - radius) / spacing), z1 = (int)MathF.Ceiling((centre.Y + radius) / spacing);
        for (int iz = z0; iz <= z1; iz++)
        {
            for (int ix = x0; ix <= x1; ix++)
            {
                float x = (ix + Hash(ix, iz, 1)) * spacing, z = (iz + Hash(ix, iz, 2)) * spacing;
                float d = new Vector2(x - centre.X, z - centre.Y).Length();
                if (d > radius)
                    continue;
                // Thin out towards the edge so the field fades into the terrain texture.
                float edge = Math.Clamp((d - radius * 0.55f) / (radius * 0.45f), 0f, 1f);
                if (Hash(ix, iz, 3) < edge * 0.75f)
                    continue;
                float h = _bed.HeightAt(loc, x, z);
                if (h < 0.6f || LakeBed.OnPier(x, z))
                    continue;
                if (layout.PathDistance(x, z) < 1.1f || layout.PadDistance(x, z) < 0.5f)
                    continue;
                float slope = MathF.Abs(_bed.HeightAt(loc, x + 0.6f, z) - h) + MathF.Abs(_bed.HeightAt(loc, x, z + 0.6f) - h);
                if (slope > 0.55f)
                    continue;
                var c = _bed.ColorAt(loc, x, z, h);
                if (c.G < c.R * 1.02f || c.G < c.B)          // sand, path, rock or snow
                    continue;
                float s = (0.7f + 0.6f * Hash(ix, iz, 4)) * (1f - edge * 0.5f);
                var basis = new Basis(Vector3.Up, Hash(ix, iz, 5) * Mathf.Tau).Scaled(new Vector3(s, s * (0.8f + 0.5f * Hash(ix, iz, 6)), s));
                transforms.Add(new Transform3D(basis, new Vector3(x, h - 0.02f, z)));
                float tone = (Hash(ix, iz, 7) - 0.5f) * 0.12f;
                colors.Add(new Color(c.R * 1.05f + tone, c.G * 1.08f + tone, c.B + tone * 0.5f));
            }
            yield return true;
        }
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = _clump ??= TreeFactory.BladeClump(17, 6, 0.42f, 0.035f, 0.2f),
            InstanceCount = transforms.Count,
        };
        for (int i = 0; i < transforms.Count; i++)
        {
            mm.SetInstanceTransform(i, transforms[i]);
            mm.SetInstanceColor(i, colors[i]);
        }
        _instance.Multimesh = mm;
    }
}
