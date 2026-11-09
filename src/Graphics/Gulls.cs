using System;
using Godot;

namespace FishingSim.Graphics;

/// <summary>A few gulls circling over the lake by day: flapping V-shaped birds, no physics. Purely decorative.</summary>
public partial class Gulls : Node3D
{
    private sealed class Bird
    {
        public Node3D Body = null!;
        public Node3D Left = null!, Right = null!;
        public float Radius, Height, Speed, Phase, Flap;
        public Vector3 Centre;
    }

    private readonly Bird[] _birds = new Bird[7];
    private float _time;

    /// <summary>0..1 daylight; gulls go to roost at night.</summary>
    public Func<float>? Daylight { get; set; }

    public override void _Ready()
    {
        var rng = new RandomNumberGenerator { Seed = 99 };
        var white = World.Props.BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.97f, 0.97f, 0.95f), CullMode = BaseMaterial3D.CullModeEnum.Disabled });
        var grey = World.Props.BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.58f, 0.62f), CullMode = BaseMaterial3D.CullModeEnum.Disabled });
        for (int i = 0; i < _birds.Length; i++)
        {
            var b = new Bird
            {
                Body = new Node3D(),
                Radius = rng.RandfRange(12f, 38f),
                Height = rng.RandfRange(14f, 26f),
                Speed = rng.RandfRange(0.12f, 0.22f) * (rng.Randf() < 0.5f ? 1f : -1f),
                Phase = rng.Randf() * Mathf.Tau,
                Flap = rng.Randf() * 10f,
                Centre = new Vector3(rng.RandfRange(-25f, 25f), 0f, rng.RandfRange(-70f, -25f)),
            };
            AddChild(b.Body);
            b.Body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.1f, 0.45f) }, MaterialOverride = white });
            b.Left = Wing(b.Body, -1f, white, grey);
            b.Right = Wing(b.Body, 1f, white, grey);
            _birds[i] = b;
        }
    }

    private static Node3D Wing(Node3D body, float side, Material white, Material tip)
    {
        var pivot = new Node3D();
        body.AddChild(pivot);
        pivot.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.55f, 0.02f, 0.2f) }, MaterialOverride = white, Position = new Vector3(side * 0.3f, 0, 0) });
        pivot.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.02f, 0.14f) }, MaterialOverride = tip, Position = new Vector3(side * 0.62f, 0, -0.02f) });
        return pivot;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        float day = Daylight?.Invoke() ?? 1f;
        Visible = day > 0.15f;
        if (!Visible)
            return;
        foreach (var b in _birds)
        {
            float a = b.Phase + _time * b.Speed;
            var pos = b.Centre + new Vector3(MathF.Cos(a) * b.Radius, b.Height + MathF.Sin(_time * 0.3f + b.Phase) * 1.5f, MathF.Sin(a) * b.Radius);
            b.Body.Position = pos;
            // Face along the circle; bank a little into the turn.
            float heading = a + (b.Speed > 0f ? MathF.PI : 0f);
            b.Body.Rotation = new Vector3(0f, heading, -MathF.Sign(b.Speed) * 0.25f);
            // Flap in bursts, glide in between.
            float burst = MathF.Max(0f, MathF.Sin(_time * 0.7f + b.Flap));
            float flap = MathF.Sin(_time * 9f + b.Flap) * 0.55f * burst + 0.15f;
            b.Left.Rotation = new Vector3(0f, 0f, flap);
            b.Right.Rotation = new Vector3(0f, 0f, -flap);
        }
    }
}
