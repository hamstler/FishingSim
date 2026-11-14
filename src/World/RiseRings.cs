using System;
using FishingSim.Core.Methods;
using Godot;

namespace FishingSim.World;

/// <summary>
/// Rings where fish take insects from the surface (Core/Methods/Rises): a small pool of flat quads with
/// shaders/rise_ring.gdshader placed over surface-feeding fish of the swim model within sight, as often as the hour,
/// weather and wind allow. Ticks a few times a second; each ring spreads for two seconds.
/// </summary>
public partial class RiseRings : Node3D
{
    private const int Pool = 10;
    private const float Life = 2.2f;
    private const float Range = 45f;

    private readonly MeshInstance3D[] _rings = new MeshInstance3D[Pool];
    private readonly float[] _age = new float[Pool];
    private readonly Random _rng = new(4242);
    private FishPopulation _fish = null!;
    private WorldState _world = null!;
    private Node3D _watcher = null!;
    private float _waterY;
    private float _due;
    private ShaderMaterial _mat = null!;

    /// <summary>A fish of this species rose at (x, z).</summary>
    public event Action<string, float, float>? Risen;

    /// <summary>Rings stop spreading and no new ones come (screenshots).</summary>
    public bool Frozen { get; set; }

    /// <summary>Rings shown since the start (for tests).</summary>
    public int Count { get; private set; }

    public void Setup(FishPopulation fish, WorldState world, Node3D watcher, float waterY)
    {
        _fish = fish;
        _world = world;
        _watcher = watcher;
        _waterY = waterY;
    }

    public override void _Ready()
    {
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/rise_ring.gdshader") };
        var quad = new PlaneMesh { Size = new Vector2(1f, 1f), SubdivideWidth = 3, SubdivideDepth = 3 };
        for (int i = 0; i < Pool; i++)
        {
            _rings[i] = new MeshInstance3D
            {
                Mesh = quad, MaterialOverride = _mat, Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_rings[i]);
            _age[i] = 1f;
        }
        _due = 2f;
    }

    public override void _Process(double delta)
    {
        float dt = MathF.Min((float)delta, 0.1f);
        if (Frozen)
            return;
        for (int i = 0; i < Pool; i++)
        {
            if (_age[i] >= 1f)
                continue;
            _age[i] += dt / Life;
            if (_age[i] >= 1f)
                _rings[i].Visible = false;
            else
                _rings[i].SetInstanceShaderParameter("age", _age[i]);
        }
        _due -= dt;
        if (_due > 0f)
            return;
        // The next rise: exponential gaps at the hour's rate (seconds between = 60 / rate).
        float rate = Rises.PerMinute(_world.Hour, _world.Weather, _world.WindTarget);
        _due = -MathF.Log(1f - (float)_rng.NextDouble() * 0.999f) * 60f / MathF.Max(rate, 0.05f);
        _mat.SetShaderParameter("light", 0.25f + 0.75f * _world.Daylight);
        Spawn();
    }

    /// <summary>A ring over a surface feeder within sight (none when there is none).</summary>
    public bool Spawn()
    {
        var agents = _fish.Agents;
        if (agents.Count == 0)
            return false;
        var eye = _watcher.GlobalPosition;
        // A few random draws rather than a scan: cheap and good enough.
        for (int tries = 0; tries < 12; tries++)
        {
            var a = agents[_rng.Next(agents.Count)];
            if (a.Hooked || a.Fear > 0f || !Rises.Riser(FeedingHabits.LayerOf(a.Species)))
                continue;
            float dx = a.Position.X - eye.X, dz = a.Position.Y - eye.Z;
            if (dx * dx + dz * dz > Range * Range)
                continue;
            SpawnAt(a.Position.X, a.Position.Y, 1.4f + 0.8f * MathF.Min(a.Weight, 2f));
            Risen?.Invoke(a.Species, a.Position.X, a.Position.Y);
            return true;
        }
        return false;
    }

    /// <summary>A ring at (x, z) of this size (m), starting now (also for screenshots).</summary>
    public void SpawnAt(float x, float z, float size, float age = 0f)
    {
        int slot = 0;
        for (int i = 1; i < Pool; i++)
            if (_age[i] > _age[slot])
                slot = i;
        var ring = _rings[slot];
        ring.Scale = new Vector3(size, 1f, size);
        ring.GlobalPosition = new Vector3(x, _waterY + 0.02f, z);
        _age[slot] = age;
        ring.SetInstanceShaderParameter("age", age);
        ring.Visible = true;
        Count++;
    }
}
