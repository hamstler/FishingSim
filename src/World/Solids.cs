using System.Collections.Generic;
using Godot;

namespace FishingSim.World;

/// <summary>
/// Collision for the many small things of a map that are drawn through MultiMeshes or built without a body of their
/// own (tree trunks, boulders, campfires, boats, racks): one StaticBody3D on layer 1 (the angler collides with it)
/// with a shape per thing. Shapes of the same size are shared. <see cref="Clear"/> empties it for a new map.
/// </summary>
public partial class Solids : StaticBody3D
{
    private readonly Dictionary<long, Shape3D> _shapes = new();

    /// <summary>How many shapes it holds (tests).</summary>
    public int Count { get; private set; }

    public Solids()
    {
        Name = "Solids";
        CollisionLayer = 1;
        CollisionMask = 0;
    }

    /// <summary>An upright cylinder standing on <paramref name="foot"/> (world position).</summary>
    public void Cylinder(Vector3 foot, float radius, float height)
    {
        radius = Mathf.Snapped(Mathf.Max(radius, 0.05f), 0.02f);
        height = Mathf.Snapped(Mathf.Max(height, 0.1f), 0.1f);
        long key = 1L << 40 | (long)(radius * 100f) << 20 | (long)(height * 10f);
        if (!_shapes.TryGetValue(key, out var shape))
            _shapes[key] = shape = new CylinderShape3D { Radius = radius, Height = height };
        Add(shape, new Transform3D(Basis.Identity, foot + new Vector3(0f, height / 2f, 0f)));
    }

    /// <summary>A box of this size whose bottom centre stands on <paramref name="foot"/>, turned by <paramref name="yaw"/>.</summary>
    public void Box(Vector3 foot, Vector3 size, float yaw)
    {
        var s = new Vector3(Mathf.Snapped(size.X, 0.05f), Mathf.Snapped(size.Y, 0.05f), Mathf.Snapped(size.Z, 0.05f));
        long key = 2L << 40 | (long)(s.X * 20f) << 26 | (long)(s.Y * 20f) << 13 | (long)(s.Z * 20f);
        if (!_shapes.TryGetValue(key, out var shape))
            _shapes[key] = shape = new BoxShape3D { Size = s };
        Add(shape, new Transform3D(new Basis(Vector3.Up, yaw), foot + new Vector3(0f, s.Y / 2f, 0f)));
    }

    /// <summary>A box placed in a frame (a prop's transform in the map: its local centre and size).</summary>
    public void BoxIn(Transform3D frame, Vector3 localCentre, Vector3 size)
    {
        var foot = frame * (localCentre - new Vector3(0f, size.Y / 2f, 0f));
        Box(foot, size, frame.Basis.GetEuler().Y);
    }

    /// <summary>An upright cylinder in a frame (a prop's transform in the map).</summary>
    public void CylinderIn(Transform3D frame, Vector3 localFoot, float radius, float height) =>
        Cylinder(frame * localFoot, radius, height);

    private void Add(Shape3D shape, Transform3D at)
    {
        AddChild(new CollisionShape3D { Shape = shape, Transform = at });
        Count++;
    }

    /// <summary>Removes every shape (a new map).</summary>
    public void Clear()
    {
        foreach (var c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        Count = 0;
    }
}
