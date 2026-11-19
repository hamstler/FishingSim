using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;

namespace FishingSim.World.Props;

/// <summary>
/// The large models' meshes as Godot keeps them (with their generated LODs, no materials), saved in user://meshcache
/// after the first load: a Meshy building of a million triangles took seconds to parse from its .glb and more to get
/// LODs, on every map load. One manifest per model (&lt;key&gt;.json: node names and transforms) and one compressed
/// .res per mesh; the key carries the .glb's size, so a rebuilt model is read afresh (its old files are deleted).
/// </summary>
public static class MeshCache
{
    private const string Dir = "user://meshcache";
    private const int Version = 1;

    /// <summary>Models with fewer vertices than this are quick to read and are not cached.</summary>
    public const int MinVertices = 20000;

    private static string Manifest(string key) => $"{Dir}/{key}_v{Version}.json";
    private static string MeshPath(string key, int i) => $"{Dir}/{key}_v{Version}_{i}.res";

    public static List<(string Name, ArrayMesh Mesh, Transform3D Transform)>? Read(string key)
    {
        if (OS.HasEnvironment("FISHSIM_NO_MESH_CACHE") || !FileAccess.FileExists(Manifest(key)))
            return null;
        try
        {
            if (JsonNode.Parse(FileAccess.GetFileAsString(Manifest(key))) is not JsonArray parts)
                return null;
            var list = new List<(string, ArrayMesh, Transform3D)>();
            for (int i = 0; i < parts.Count; i++)
            {
                if (ResourceLoader.Load(MeshPath(key, i), "ArrayMesh", ResourceLoader.CacheMode.Ignore) is not ArrayMesh mesh)
                    return null;
                var f = parts[i]!["t"]!.AsArray();
                var t = new Transform3D(
                    new Basis(new Vector3((float)f[0]!, (float)f[1]!, (float)f[2]!), new Vector3((float)f[3]!, (float)f[4]!, (float)f[5]!),
                        new Vector3((float)f[6]!, (float)f[7]!, (float)f[8]!)),
                    new Vector3((float)f[9]!, (float)f[10]!, (float)f[11]!));
                list.Add(((string?)parts[i]!["name"] ?? "", mesh, t));
            }
            return list;
        }
        catch (Exception e)
        {
            GD.PushWarning($"mesh cache {key}: {e.Message}");
            return null;
        }
    }

    public static void Write(string key, List<(string Name, ArrayMesh Mesh, Transform3D Transform)> parts)
    {
        int vertices = 0;
        foreach (var (_, mesh, _) in parts)
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                vertices += mesh.SurfaceGetArrayLen(s);
        if (vertices < MinVertices || OS.HasEnvironment("FISHSIM_NO_MESH_CACHE"))
            return;
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(Dir);
            DropStale(key);
            var manifest = new JsonArray();
            for (int i = 0; i < parts.Count; i++)
            {
                var (name, mesh, t) = parts[i];
                if (ResourceSaver.Save(mesh, MeshPath(key, i), ResourceSaver.SaverFlags.Compress) != Error.Ok)
                    return;
                manifest.Add(new JsonObject
                {
                    ["name"] = name,
                    ["t"] = new JsonArray(t.Basis.Column0.X, t.Basis.Column0.Y, t.Basis.Column0.Z, t.Basis.Column1.X, t.Basis.Column1.Y,
                        t.Basis.Column1.Z, t.Basis.Column2.X, t.Basis.Column2.Y, t.Basis.Column2.Z, t.Origin.X, t.Origin.Y, t.Origin.Z),
                });
            }
            // The manifest last: a run stopped half-way leaves no manifest, so the model is read from its .glb again.
            using var f = FileAccess.Open(Manifest(key), FileAccess.ModeFlags.Write);
            f?.StoreString(manifest.ToJsonString());
        }
        catch (Exception e)
        {
            GD.PushWarning($"mesh cache {key}: {e.Message}");
        }
    }

    /// <summary>Deletes the cached files of other versions of the same model (the key ends in the .glb's size).</summary>
    private static void DropStale(string key)
    {
        int cut = key.LastIndexOf('_');
        if (cut <= 0)
            return;
        string model = key[..(cut + 1)];
        using var dir = DirAccess.Open(Dir);
        if (dir == null)
            return;
        foreach (var file in dir.GetFiles())
            if (file.StartsWith(model) && !file.StartsWith(key + "_v" + Version) && char.IsDigit(file[model.Length]))
                dir.Remove(file);
    }
}
