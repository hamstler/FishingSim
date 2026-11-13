using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Core;
using FishingSim.Core.Camp;
using FishingSim.Graphics;

namespace FishingSim.World;

/// <summary>
/// Mayfly swarms along the shore. Each map gets a dozen or so spots at the water's edge (more of them near the pier);
/// from late afternoon to dusk in calm weather the mayflies dance there (shaders/mayfly.gdshader on a MultiMesh), and a
/// sweep of the insect net through a swarm catches some (Core/Camp/Sweeping). A swarm thinned by the net fills up again
/// over a few minutes.
/// </summary>
public partial class InsectSwarms : Node3D
{
    private const int PerSwarm = 45;
    private const float Reach = 2.6f;

    private sealed class Swarm
    {
        public Vector3 Position;
        public float Fullness = 1f;
        public MultiMeshInstance3D? View;
    }

    private readonly List<Swarm> _swarms = new();
    private LakeBed _lake = null!;
    private WorldState _world = null!;
    private ShaderMaterial? _material;

    /// <summary>How busy the mayflies are right now (0..1).</summary>
    public float Activity { get; private set; }

    public IReadOnlyList<Vector3> Spots => _swarms.ConvertAll(s => s.Position);

    public void Setup(LakeBed lake, WorldState world)
    {
        _lake = lake;
        _world = world;
        _lake.Rebuilt += Build;
        Build();
    }

    public override void _ExitTree()
    {
        if (_lake != null)
            _lake.Rebuilt -= Build;
    }

    private void Build()
    {
        foreach (var s in _swarms)
            s.View?.QueueFree();
        _swarms.Clear();
        // Candidate spots: a little above the water line, a few metres from it.
        var candidates = new List<Vector3>();
        for (float x = LakeBed.XMin + 6f; x < LakeBed.XMax - 6f; x += 5f)
            for (float z = LakeBed.ZMin + 6f; z < LakeBed.ZMax - 6f; z += 5f)
            {
                float h = _lake.HeightAt(x, z);
                if (h > 0.08f && h < 0.55f && _lake.IsWalkable(x, z))
                    candidates.Add(new Vector3(x, h, z));
            }
        // Nearest the pier first, so the angler finds one, then spread out at least 24 m apart.
        candidates.Sort((a, b) => (a.X * a.X + a.Z * a.Z).CompareTo(b.X * b.X + b.Z * b.Z));
        foreach (var c in candidates)
        {
            if (_swarms.Count >= 14)
                break;
            bool apart = true;
            foreach (var s in _swarms)
                if (new Vector2(s.Position.X - c.X, s.Position.Z - c.Z).Length() < 24f)
                    apart = false;
            if (!apart)
                continue;
            // Over the water's edge: a step towards the lake from the spot.
            var swarm = new Swarm { Position = new Vector3(c.X, MathF.Max(c.Y, 0f), c.Z) };
            _swarms.Add(swarm);
        }
        if (Renderer.IsHeadless)
            return;
        _material ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/mayfly.gdshader") };
        var quad = new QuadMesh { Size = new Vector2(0.03f, 0.03f) };
        foreach (var s in _swarms)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = quad, InstanceCount = PerSwarm };
            for (int i = 0; i < PerSwarm; i++)
                mm.SetInstanceTransform(i, Transform3D.Identity);
            mm.CustomAabb = new Aabb(new Vector3(-1.2f, 0f, -1.2f), new Vector3(2.4f, 2.6f, 2.4f));
            s.View = new MultiMeshInstance3D
            {
                Multimesh = mm, MaterialOverride = _material, Position = s.Position, Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = 70f,
            };
            AddChild(s.View);
        }
    }

    private float _tick;

    /// <summary>The swarms' state changes slowly: updated four times a second.</summary>
    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("insects");
        if (_world == null)
            return;
        _tick += (float)delta;
        if (_tick < 0.25f)
            return;
        float step = _tick;
        _tick = 0f;
        Activity = Sweeping.MayflyActivity(_world.Hour, _world.Weather);
        foreach (var s in _swarms)
        {
            s.Fullness = MathF.Min(1f, s.Fullness + step / 150f);
            if (s.View != null)
            {
                s.View.Visible = Activity > 0.02f;
                s.View.SetInstanceShaderParameter("fade", Activity * (0.25f + 0.75f * s.Fullness));
            }
        }
        _material?.SetShaderParameter("light", Math.Clamp(_world.Daylight * 1.4f, 0f, 1f));
    }

    /// <summary>How thick the swarm is at a point (0..1): the evening's activity, the swarm's fullness, the distance.</summary>
    public float DensityAt(Vector3 p)
    {
        float best = 0f;
        foreach (var s in _swarms)
        {
            float d = new Vector2(s.Position.X - p.X, s.Position.Z - p.Z).Length();
            float above = p.Y - s.Position.Y;
            if (d > Reach || above < -0.5f || above > 3.5f)
                continue;
            best = MathF.Max(best, (1f - d / Reach) * s.Fullness);
        }
        return Math.Clamp(best * 1.4f, 0f, 1f) * Activity;
    }

    /// <summary>A net went through the swarm here: it thins out.</summary>
    public void Deplete(Vector3 p, float amount)
    {
        foreach (var s in _swarms)
            if (new Vector2(s.Position.X - p.X, s.Position.Z - p.Z).Length() < Reach)
                s.Fullness = MathF.Max(0f, s.Fullness - amount);
    }
}
