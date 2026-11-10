using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Data;

namespace FishingSim.Fishing;

/// <summary>
/// A hand-built model of a species (assets/fish/models/&lt;id&gt;.glb, made by tools/fish_model from reference
/// photographs): the body with its painted skin and normal map, each fin with its own painted texture, and the eyes.
/// Loaded at run time with GLTFDocument (the folder is not imported by the editor) and shared by every close-up fish
/// of the species. Model space as <see cref="FishModel"/> uses it: unit length, nose at +z 0.5.
/// </summary>
public sealed class FishAsset
{
    public Mesh Body { get; private init; } = null!;
    public Texture2D? BodyAlbedo { get; private init; }
    public Texture2D? BodyNormal { get; private init; }

    /// <summary>Fins by name (FinDorsal1, FinDorsal2, FinAnal, FinCaudal, FinPectoral, FinPelvic) with their textures.</summary>
    public IReadOnlyList<(string Name, Mesh Mesh, Texture2D? Texture)> Fins { get; private init; } = Array.Empty<(string, Mesh, Texture2D?)>();

    public Texture2D? Iris { get; private init; }

    /// <summary>The eyes (right, then left) and where they sit.</summary>
    public IReadOnlyList<(Mesh Mesh, Vector3 Position)> Eyes { get; private init; } = Array.Empty<(Mesh, Vector3)>();

    public float EyeRadius { get; private init; }
    public float HeadHeightAtEye { get; private init; }
    public float MaxHeight { get; private init; }
    public float MaxWidth { get; private init; }

    private static readonly Dictionary<string, FishAsset?> Cache = new();

    public static bool Has(FishSpecies sp) => For(sp) != null;

    public static FishAsset? For(FishSpecies sp)
    {
        string id = FishReference.IdOf(sp);
        if (Cache.TryGetValue(id, out var cached))
            return cached;
        FishAsset? asset = null;
        string path = $"res://assets/fish/models/{id}.glb";
        if (id != "" && FileAccess.FileExists(path))
        {
            try
            {
                asset = Load(path);
            }
            catch (Exception e)
            {
                GD.PushWarning($"fish model {path}: {e.Message}");
            }
        }
        Cache[id] = asset;
        return asset;
    }

    private static FishAsset? Load(string path)
    {
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(path, state) != Error.Ok)
            return null;
        var root = doc.GenerateScene(state) as Node3D;
        if (root == null)
            return null;
        var parts = new List<(string Name, Mesh Mesh, Transform3D Transform)>();
        foreach (var node in root.FindChildren("*", "", true, false))
        {
            Mesh? mesh = node switch
            {
                MeshInstance3D mi => mi.Mesh,
                ImporterMeshInstance3D imi => imi.Mesh?.GetMesh(),
                _ => null,
            };
            if (mesh != null && node is Node3D n3)
                parts.Add((node.Name, mesh, GlobalOf(n3, root)));
        }
        root.Free();

        static Texture2D? Albedo(Mesh m) => (m.SurfaceGetMaterial(0) as BaseMaterial3D)?.AlbedoTexture;
        var body = parts.FirstOrDefault(p => p.Name == "Body");
        var eyes = parts.Where(p => p.Name.ToString().StartsWith("Eye")).OrderBy(p => p.Name == "EyeLeft").ToList();
        if (body.Mesh == null || eyes.Count == 0)
            return null;
        var aabb = body.Mesh.GetAabb();
        var eyeAabb = eyes[0].Mesh.GetAabb();
        float eyeZ = eyes[0].Transform.Origin.Z;
        // Body height at the eye, from the body's vertices near it.
        float lo = float.MaxValue, hi = float.MinValue;
        var verts = body.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        foreach (var v in verts)
            if (MathF.Abs(v.Z - eyeZ) < 0.008f)
            {
                lo = MathF.Min(lo, v.Y);
                hi = MathF.Max(hi, v.Y);
            }
        return new FishAsset
        {
            Body = body.Mesh,
            BodyAlbedo = Albedo(body.Mesh),
            BodyNormal = (body.Mesh.SurfaceGetMaterial(0) as BaseMaterial3D)?.NormalTexture,
            Fins = parts.Where(p => p.Name.StartsWith("Fin")).Select(p => (p.Name, p.Mesh, Albedo(p.Mesh))).ToList(),
            Iris = Albedo(eyes[0].Mesh),
            Eyes = eyes.Select(p => (p.Mesh, p.Transform.Origin)).ToList(),
            EyeRadius = eyeAabb.Size.Y * 0.5f,
            HeadHeightAtEye = hi > lo ? hi - lo : aabb.Size.Y * 0.5f,
            MaxHeight = aabb.Size.Y,
            MaxWidth = aabb.Size.X,
        };
    }

    /// <summary>A node's transform relative to the scene root (the scene is not in the tree, so no GlobalTransform).</summary>
    private static Transform3D GlobalOf(Node3D n, Node3D root)
    {
        var t = n.Transform;
        for (var p = n.GetParent() as Node3D; p != null && p != root; p = p.GetParent() as Node3D)
            t = p.Transform * t;
        return t;
    }
}
