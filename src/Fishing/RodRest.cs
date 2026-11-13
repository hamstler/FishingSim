using System;
using Godot;

namespace FishingSim.Fishing;

/// <summary>
/// What the rod lies on when it is put down (Z): a front rest with a fork the blank lies in and a short back rest under
/// the butt. Without the bought rod rest it is a forked hazel stick pushed into the ground; with it (the shop's
/// "Podpórka z dzwoneczkiem") a dark metal rest with a brass bell that swings when it rings (<see cref="Ring"/>).
/// The node stands on the ground facing +Z towards the line's end.
/// </summary>
public partial class RodRest : Node3D
{
    /// <summary>Height of the front fork above the ground (m).</summary>
    public const float ForkHeight = 0.5f;

    public bool HasBell { get; private set; }

    private Node3D? _bell;
    private float _ring;
    private Material _material = null!;

    public static RodRest Create(bool bought)
    {
        var rest = new RodRest { Name = "RodRest", HasBell = bought };
        rest._material = bought
            ? new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.17f, 0.18f), Metallic = 0.7f, Roughness = 0.4f }
            : new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.33f, 0.22f), Roughness = 0.9f };   // hazel bark
        rest.Stake(Vector3.Zero, ForkHeight, bought ? 0.006f : 0.011f, 0.06f);
        if (bought)
        {
            // The bell hangs from a little arm in front of the fork.
            var brass = new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.67f, 0.3f), Metallic = 0.95f, Roughness = 0.25f };
            rest._bell = new Node3D { Position = new Vector3(0f, ForkHeight + 0.03f, 0.05f) };
            rest.AddChild(rest._bell);
            Part(rest, new CylinderMesh { TopRadius = 0.002f, BottomRadius = 0.002f, Height = 0.05f, RadialSegments = 4 }, rest._material,
                new Vector3(0f, ForkHeight + 0.03f, 0.025f), new Vector3(Mathf.Pi / 2f, 0f, 0f));
            Part(rest._bell, new CylinderMesh { TopRadius = 0.004f, BottomRadius = 0.012f, Height = 0.018f, RadialSegments = 12 }, brass,
                new Vector3(0f, -0.012f, 0f));
            Part(rest._bell, new SphereMesh { Radius = 0.003f, Height = 0.006f, RadialSegments = 8, Rings = 4 }, brass, new Vector3(0f, -0.023f, 0f));
        }
        rest.SetProcess(false);
        return rest;
    }

    /// <summary>A stake standing at a point with a V fork on top.</summary>
    private void Stake(Vector3 at, float height, float radius, float forkSpread)
    {
        Part(this, new CylinderMesh { TopRadius = radius, BottomRadius = radius * 0.7f, Height = height, RadialSegments = 7 }, _material,
            at + new Vector3(0f, height / 2f - 0.04f, 0f));
        foreach (float side in new[] { -1f, 1f })
            Part(this, new CylinderMesh { TopRadius = radius * 0.6f, BottomRadius = radius * 0.8f, Height = 0.07f, RadialSegments = 6 }, _material,
                at + new Vector3(side * forkSpread * 0.3f, height - 0.01f, 0f), new Vector3(0f, 0f, -side * 0.45f));
    }

    /// <summary>The back rest under the rod's butt (a world point): a short stake reaching up to it.</summary>
    public void PlaceBackRest(Vector3 butt)
    {
        var local = ToLocal(butt);
        float height = MathF.Max(local.Y, 0.08f);
        Stake(new Vector3(local.X, 0f, local.Z), height, HasBell ? 0.005f : 0.009f, 0.05f);
    }

    /// <summary>The bell rings: it swings for a moment.</summary>
    public void Ring()
    {
        if (_bell == null)
            return;
        _ring = 1f;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _ring = MathF.Max(0f, _ring - (float)delta * 0.8f);
        if (_bell != null)
            _bell.Rotation = new Vector3(MathF.Sin(_ring * 40f) * 0.5f * _ring, 0f, 0f);
        if (_ring <= 0f)
            SetProcess(false);
    }

    private static MeshInstance3D Part(Node3D parent, Mesh mesh, Material mat, Vector3 at, Vector3? rot = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = at, Rotation = rot ?? Vector3.Zero };
        parent.AddChild(mi);
        return mi;
    }
}
