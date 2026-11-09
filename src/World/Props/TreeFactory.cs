using System.Collections.Generic;
using Godot;

namespace FishingSim.World.Props;

/// <summary>One kind of tree or plant: a trunk mesh and a crown mesh, both tinted per instance.</summary>
public sealed record PlantMeshes(Mesh? Trunk, Mesh Crown, Color TrunkColor, float TrunkLift, float CrownLift);

/// <summary>Low-poly meshes for the vegetation. Everything is built from primitives and merged into single meshes.</summary>
public static class TreeFactory
{
    private static readonly Dictionary<string, PlantMeshes> Cache = new();

    public static PlantMeshes Get(string kind)
    {
        if (Cache.TryGetValue(kind, out var cached))
            return cached;
        var meshes = kind switch
        {
            "spruce" => new PlantMeshes(Trunk(0.1f, 0.17f, 1.4f), SpruceCrown(), new Color(0.28f, 0.19f, 0.11f), 0.7f, 1.1f),
            "oak" => new PlantMeshes(Trunk(0.12f, 0.2f, 1.8f), RoundCrown(), new Color(0.28f, 0.19f, 0.11f), 0.9f, 2.3f),
            "birch" => new PlantMeshes(Trunk(0.06f, 0.1f, 3.2f), BirchCrown(), new Color(0.86f, 0.85f, 0.8f), 1.6f, 2.6f),
            "pine" => new PlantMeshes(Trunk(0.1f, 0.18f, 5.2f), PineCrown(), new Color(0.42f, 0.24f, 0.14f), 2.6f, 4.4f),
            "willow" => new PlantMeshes(Trunk(0.14f, 0.24f, 2.0f), WillowCrown(), new Color(0.3f, 0.22f, 0.14f), 1.0f, 2.4f),
            "bush" => new PlantMeshes(null, BushCrown(), Colors.White, 0f, 0.4f),
            _ => new PlantMeshes(null, RoundCrown(), Colors.White, 0f, 0f),
        };
        Cache[kind] = meshes;
        return meshes;
    }

    private static CylinderMesh Trunk(float top, float bottom, float height) =>
        new() { TopRadius = top, BottomRadius = bottom, Height = height, RadialSegments = 6, Rings = 1 };

    private static ArrayMesh Merge(params (Mesh Mesh, Transform3D Transform)[] parts)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (mesh, transform) in parts)
            st.AppendFrom(mesh, 0, transform);
        return st.Commit();
    }

    private static Transform3D At(float x, float y, float z) => new(Basis.Identity, new Vector3(x, y, z));

    private static Mesh Cone(float radius, float height, int segments = 9) =>
        new CylinderMesh { TopRadius = 0f, BottomRadius = radius, Height = height, RadialSegments = segments, Rings = 1 };

    private static Mesh Blob(float radius, int segments = 9, int rings = 5, float squash = 1.7f) =>
        new SphereMesh { Radius = radius, Height = radius * squash, RadialSegments = segments, Rings = rings };

    private static ArrayMesh SpruceCrown() => Merge(
        (Cone(1.4f, 1.7f), At(0, 0.85f, 0)),
        (Cone(1.1f, 1.6f), At(0, 1.8f, 0)),
        (Cone(0.8f, 1.5f), At(0, 2.75f, 0)),
        (Cone(0.5f, 1.2f), At(0, 3.5f, 0)));

    private static ArrayMesh RoundCrown() => Merge(
        (Blob(1.25f), At(0, 0, 0)),
        (Blob(0.9f), At(0.7f, -0.3f, 0.35f)),
        (Blob(0.9f), At(-0.6f, -0.2f, -0.5f)),
        (Blob(0.8f), At(0.1f, 0.8f, -0.2f)));

    private static ArrayMesh BirchCrown() => Merge(
        (Blob(0.75f, 8, 4), At(0, 0.2f, 0)),
        (Blob(0.6f, 8, 4), At(0.45f, -0.5f, 0.15f)),
        (Blob(0.55f, 8, 4), At(-0.4f, -0.7f, -0.2f)),
        (Blob(0.5f, 8, 4), At(0.05f, 0.9f, 0.1f)));

    private static ArrayMesh PineCrown() => Merge(
        (Blob(1.5f, 9, 4, 0.9f), At(0, 0, 0)),
        (Blob(1.1f, 9, 4, 0.8f), At(0.9f, -1.3f, 0.3f)),
        (Blob(1.0f, 9, 4, 0.8f), At(-0.8f, -1.6f, -0.4f)),
        (Blob(0.8f, 8, 4, 0.8f), At(0.1f, 0.7f, 0f)));

    private static ArrayMesh WillowCrown() => Merge(
        (Blob(1.6f, 9, 5, 1.2f), At(0, 0.2f, 0)),
        (new CylinderMesh { TopRadius = 1.3f, BottomRadius = 2.3f, Height = 2.2f, RadialSegments = 10, Rings = 1 }, At(0, -0.9f, 0)),
        (Blob(1.2f, 8, 4, 1.1f), At(1.1f, -0.3f, 0.4f)),
        (Blob(1.2f, 8, 4, 1.1f), At(-1f, -0.4f, -0.5f)));

    private static ArrayMesh BushCrown() => Merge(
        (Blob(0.55f, 8, 4), At(0, 0.15f, 0)),
        (Blob(0.4f, 8, 4), At(0.45f, 0f, 0.2f)),
        (Blob(0.4f, 8, 4), At(-0.4f, 0f, -0.2f)));

    /// <summary>Three crossed blades of grass.</summary>
    public static ArrayMesh Tuft()
    {
        if (_tuft != null)
            return _tuft;
        var blade = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.07f, Height = 0.55f, RadialSegments = 4, Rings = 1 };
        _tuft = Merge(
            (blade, new Transform3D(Basis.FromEuler(new Vector3(0.15f, 0f, 0.1f)), new Vector3(0, 0.27f, 0))),
            (blade, new Transform3D(Basis.FromEuler(new Vector3(-0.2f, 1.2f, -0.15f)), new Vector3(0.04f, 0.25f, 0.03f))),
            (blade, new Transform3D(Basis.FromEuler(new Vector3(0.1f, 2.4f, 0.2f)), new Vector3(-0.04f, 0.23f, -0.02f))));
        return _tuft;
    }

    private static ArrayMesh? _tuft;

    /// <summary>A small blossom on a short stalk (the stalk is tinted along with the petals).</summary>
    public static SphereMesh Blossom() => new() { Radius = 0.09f, Height = 0.14f, RadialSegments = 6, Rings = 3 };

    public static SphereMesh Rock() => new() { Radius = 1f, Height = 2f, RadialSegments = 7, Rings = 4 };

    public static CylinderMesh Reed() => new() { TopRadius = 0.004f, BottomRadius = 0.014f, Height = 1.5f, RadialSegments = 4, Rings = 1 };

    public static CylinderMesh Cattail() => new() { TopRadius = 0.016f, BottomRadius = 0.016f, Height = 0.16f, RadialSegments = 6, Rings = 1 };
}
