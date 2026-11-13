using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Core.Camp;
using FishingSim.World.Props;

namespace FishingSim.World;

/// <summary>
/// What digging leaves behind: a dark hole with a heap of turned soil beside it (they stay for the session on this map;
/// a spot already dug gives no more bait), and clods of soil thrown off the shovel that fly, land and crumble away.
/// </summary>
public partial class DigMarks : Node3D
{
    private const int MaxHoles = 80;

    private sealed class Clod
    {
        public MeshInstance3D Mesh = null!;
        public Vector3 Velocity;
        public float Age;
        public bool Landed;
    }

    private readonly List<(Vector3 At, Node3D View)> _holes = new();
    private readonly List<Clod> _clods = new();
    private LakeBed _lake = null!;

    public int HoleCount => _holes.Count;

    public void Setup(LakeBed lake)
    {
        _lake = lake;
        _lake.Rebuilt += Clear;
    }

    public override void _ExitTree()
    {
        if (_lake != null)
            _lake.Rebuilt -= Clear;
    }

    public void Clear()
    {
        foreach (var (_, view) in _holes)
            view.QueueFree();
        _holes.Clear();
        foreach (var c in _clods)
            c.Mesh.QueueFree();
        _clods.Clear();
    }

    /// <summary>True when a hole was dug within <see cref="Digging.SpotRadius"/> of the point.</summary>
    public bool Used(Vector3 p)
    {
        foreach (var (at, _) in _holes)
            if (new Vector2(at.X - p.X, at.Z - p.Z).Length() < Digging.SpotRadius)
                return true;
        return false;
    }

    /// <summary>A fresh hole at a point on the ground, the heap of soil on the side the shovel threw it.</summary>
    public void AddHole(Vector3 at, Vector3 heapSide)
    {
        var view = new Node3D();
        // The hole: a dark, shallow bowl sunk into the ground; the rim of loose soil around it.
        var hole = new Node3D { Position = at, Rotation = new Vector3(0f, GD.Randf() * Mathf.Tau, 0f) };
        hole.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.2f, Height = 0.12f, RadialSegments = 14, Rings = 5 },
            Scale = new Vector3(1f, 1f, 0.8f), Position = new Vector3(0f, -0.035f, 0f),
            MaterialOverride = BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.06f, 0.045f), Roughness = 1f }),
        });
        hole.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.17f, OuterRadius = 0.27f, Rings = 16, RingSegments = 6 },
            Scale = new Vector3(1f, 0.35f, 0.85f), Position = new Vector3(0f, 0.005f, 0f), MaterialOverride = CampProps.Soil,
        });
        view.AddChild(hole);
        // The heap of turned soil beside the hole.
        var side = new Vector3(heapSide.X, 0f, heapSide.Z);
        side = side.LengthSquared() > 1e-6f ? side.Normalized() : Vector3.Right;
        view.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.17f, Height = 0.14f, RadialSegments = 12, Rings = 6 },
            Position = at + side * 0.42f + new Vector3(0f, -0.02f, 0f),
            Rotation = new Vector3(0f, GD.Randf() * Mathf.Tau, 0f), Scale = new Vector3(1.2f, 1f, 0.9f), MaterialOverride = CampProps.Soil,
        });
        AddChild(view);
        _holes.Add((at, view));
        if (_holes.Count > MaxHoles)
        {
            _holes[0].View.QueueFree();
            _holes.RemoveAt(0);
        }
    }

    /// <summary>Throws a handful of clods from a point.</summary>
    public void Throw(Vector3 from, Vector3 velocity, int count)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        for (int i = 0; i < count; i++)
        {
            float r = rng.RandfRange(0.012f, 0.035f);
            var mesh = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = r, Height = r * 1.6f, RadialSegments = 6, Rings = 3 },
                MaterialOverride = CampProps.Soil,
                Position = from + new Vector3(rng.RandfRange(-0.06f, 0.06f), rng.RandfRange(-0.03f, 0.03f), rng.RandfRange(-0.06f, 0.06f)),
                Rotation = new Vector3(rng.Randf() * 3f, rng.Randf() * 3f, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(mesh);
            var spread = new Vector3(rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(-0.3f, 0.6f), rng.RandfRange(-0.6f, 0.6f));
            _clods.Add(new Clod { Mesh = mesh, Velocity = velocity + spread });
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = _clods.Count - 1; i >= 0; i--)
        {
            var c = _clods[i];
            c.Age += dt;
            if (!c.Landed)
            {
                c.Velocity += Vector3.Down * 9.8f * dt;
                var p = c.Mesh.Position + c.Velocity * dt;
                float ground = _lake?.HeightAt(p.X, p.Z) ?? 0f;
                if (p.Y <= ground)
                {
                    p.Y = ground;
                    c.Landed = true;
                }
                c.Mesh.Position = p;
                c.Mesh.Rotation += new Vector3(4f, 3f, 0f) * dt;
            }
            // Crumble away a few seconds after landing.
            if (c.Age > 5f)
            {
                float k = 1f - (c.Age - 5f) / 2f;
                if (k <= 0f)
                {
                    c.Mesh.QueueFree();
                    _clods.RemoveAt(i);
                    continue;
                }
                c.Mesh.Scale = Vector3.One * k;
            }
        }
    }
}
