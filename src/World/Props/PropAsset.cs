using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Godot;
using FishingSim.Graphics;

namespace FishingSim.World.Props;

/// <summary>
/// Props modelled and photographed by Poly Haven artists (CC0: free for any use, commercial too): a wine barrel, a
/// crate, a lantern, a bucket, a wicker basket, a stone fire pit, a tree stump. assets/models/&lt;id&gt;.glb, packed by
/// tools/assets/polyhaven.py with WebP pictures. The meshes come from GltfDocument; the pictures are decoded here,
/// given mipmaps and block compressed (colour, normal map, AO-roughness-metal), cached like the other textures.
/// Without a screen (tests) nothing is loaded and callers fall back to their own primitives.
/// </summary>
public static class PropAsset
{
    private const int Version = 1;

    private sealed record Model(List<(Mesh Mesh, Transform3D Transform)> Parts, Aabb Bounds, Dictionary<string, Mesh> Named);

    /// <summary>The model's meshes by their node names (e.g. the player's rod: Handle, Seg0 ..), materials set; null
    /// when the model is missing or there is no screen.</summary>
    public static IReadOnlyDictionary<string, Mesh>? Named(string id) => Load(id)?.Named;

    /// <summary>The model's bounds in its own units (null when missing or headless).</summary>
    public static Aabb? BoundsOf(string id) => Load(id)?.Bounds;

    private static readonly Dictionary<string, Model?> Cache = new();

    private static string PathOf(string id) => $"res://assets/models/{id}.glb";

    /// <summary>
    /// A new node holding the model, standing on y = 0 and centred, scaled to <paramref name="height"/> metres high
    /// (or by <paramref name="scale"/> when no height is given). Null when the model is missing or there is no screen.
    /// </summary>
    public static Node3D? Create(string id, float height = 0f, float scale = 1f)
    {
        var model = Load(id);
        if (model == null)
            return null;
        float k = height > 0f ? height / MathF.Max(model.Bounds.Size.Y, 0.001f) : scale;
        var centre = model.Bounds.GetCenter();
        var holder = new Node3D
        {
            Scale = Vector3.One * k,
            Position = new Vector3(-centre.X * k, -model.Bounds.Position.Y * k, -centre.Z * k),
        };
        foreach (var (mesh, transform) in model.Parts)
            holder.AddChild(new MeshInstance3D { Mesh = mesh, Transform = transform });
        var root = new Node3D { Name = id };
        root.AddChild(holder);
        return root;
    }

    private static Model? Load(string id)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(id, out var cached))
                return cached;
            Model? model = null;
            if (!Renderer.IsHeadless && FileAccess.FileExists(PathOf(id)))
            {
                try
                {
                    model = Build(id);
                }
                catch (Exception e)
                {
                    GD.PushWarning($"prop {id}: {e.Message}");
                }
            }
            Cache[id] = model;
            return model;
        }
    }

    private static Model? Build(string id)
    {
        string path = PathOf(id);
        byte[] glb = FileAccess.GetFileAsBytes(path);
        int jsonLength = BitConverter.ToInt32(glb, 12);
        var json = JsonNode.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength))!;
        int bin = 28 + jsonLength;
        var views = json["bufferViews"]!.AsArray();
        var images = json["images"]?.AsArray() ?? new JsonArray();
        var textures = json["textures"]?.AsArray() ?? new JsonArray();

        // The pictures, by image index, decoded once and cached compressed.
        var decoded = new Dictionary<int, Texture2D>();
        Texture2D? Picture(int? textureIndex, BlockCompress.Kind kind)
        {
            if (textureIndex is not int ti || ti >= textures.Count)
                return null;
            // The picture's index: "source", or EXT_texture_webp's (WebP-only files, e.g. the Meshy props).
            if (((int?)textures[ti]?["source"] ?? (int?)textures[ti]?["extensions"]?["EXT_texture_webp"]?["source"]) is not int ii)
                return null;
            if (decoded.TryGetValue(ii, out var done))
                return done;
            var node = images[ii]!;
            var view = views[(int)node["bufferView"]!]!;
            int offset = (int?)view["byteOffset"] ?? 0, length = (int)view["byteLength"]!;
            // The key is a flat file name (ids like "rods/carp_kaminari" made user://texcache/prop_rods/..., a folder
            // that never existed, so nothing was cached), with the model's size: a rebuilt model never meets old pictures.
            var (img, _) = TextureStore.Get($"prop_{id.Replace('/', '_')}_{glb.Length}_{ii}", Version, kind, () =>
            {
                var bytes = new byte[length];
                Array.Copy(glb, bin + offset, bytes, 0, length);
                var image = new Image();
                if ((string?)node["mimeType"] == "image/webp")
                    image.LoadWebpFromBuffer(bytes);
                else if ((string?)node["mimeType"] == "image/png")
                    image.LoadPngFromBuffer(bytes);
                else
                    image.LoadJpgFromBuffer(bytes);
                return image;
            }, normalMap: kind == BlockCompress.Kind.TwoChannel);
            var tex = (Texture2D)ImageTexture.CreateFromImage(img);
            decoded[ii] = tex;
            return tex;
        }

        // Materials by name: colour, normal map, and the packed AO (R), roughness (G), metal (B).
        var materials = new Dictionary<string, StandardMaterial3D>();
        foreach (var mat in json["materials"]?.AsArray() ?? new JsonArray())
        {
            if (mat == null)
                continue;
            var pbr = mat["pbrMetallicRoughness"];
            var m = BuildingFactory.Pixel(new StandardMaterial3D { Roughness = 1f, Metallic = 0f }, true);
            if (Picture((int?)pbr?["baseColorTexture"]?["index"], BlockCompress.Kind.Colour) is { } albedo)
                m.AlbedoTexture = albedo;
            if (Picture((int?)mat["normalTexture"]?["index"], BlockCompress.Kind.TwoChannel) is { } normal)
            {
                m.NormalEnabled = true;
                m.NormalTexture = normal;
            }
            if (Picture((int?)pbr?["metallicRoughnessTexture"]?["index"], BlockCompress.Kind.Colour) is { } arm)
            {
                m.RoughnessTexture = arm;
                m.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green;
                m.MetallicTexture = arm;
                m.MetallicTextureChannel = BaseMaterial3D.TextureChannel.Blue;
                m.Metallic = (float?)pbr?["metallicFactor"] ?? 1f;
                m.AOEnabled = true;
                m.AOTexture = arm;
                m.AOTextureChannel = BaseMaterial3D.TextureChannel.Red;
                m.AOLightAffect = 0.3f;
            }
            if ((string?)mat["alphaMode"] is "MASK" or "BLEND")
            {
                m.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            }
            materials[(string?)mat["name"] ?? ""] = m;
        }

        // The meshes: from the mesh cache (a large model's parse and LOD generation took seconds per map load), or
        // read from the glTF, given LODs and cached for the next time.
        string flat = $"{id.Replace('/', '_')}_{glb.Length}";
        var loaded = MeshCache.Read(flat) ?? ReadMeshes(id, glb, path, flat);
        if (loaded == null)
            return null;
        var parts = new List<(Mesh, Transform3D)>();
        var named = new Dictionary<string, Mesh>();
        var bounds = new Aabb();
        bool first = true;
        foreach (var (name, am, t) in loaded)
        {
            for (int s = 0; s < am.GetSurfaceCount(); s++)
            {
                string mname = am.SurfaceGetName(s);
                if (materials.TryGetValue(mname, out var m) || (materials.Count == 1 && (m = materials.Values.First()) != null))
                    am.SurfaceSetMaterial(s, m);
            }
            parts.Add((am, t));
            named[name] = am;
            var box = t * am.GetAabb();
            bounds = first ? box : bounds.Merge(box);
            first = false;
        }
        return parts.Count == 0 ? null : new Model(parts, bounds, named);
    }

    /// <summary>The model's meshes (node name, mesh with LODs and each surface named after its glTF material, transform)
    /// read from the glTF; meshes over <see cref="MeshCache.MinVertices"/> are written to the mesh cache.</summary>
    private static List<(string Name, ArrayMesh Mesh, Transform3D Transform)>? ReadMeshes(string id, byte[] glb, string path, string flat)
    {
        var doc = new GltfDocument();
        var state = new GltfState { HandleBinaryImage = 0 };
        if (doc.AppendFromBuffer(ImageDecode.WithoutPictures(glb, keepMaterialNames: true), path.GetBaseDir(), state) != Error.Ok
            || doc.GenerateScene(state) is not Node3D root)
            return null;
        var list = new List<(string, ArrayMesh, Transform3D)>();
        foreach (var node in root.FindChildren("*", "", true, false))
        {
            Mesh? mesh = node switch
            {
                MeshInstance3D mi => mi.Mesh,
                ImporterMeshInstance3D imi => imi.Mesh?.GetMesh(),
                _ => null,
            };
            if (mesh is not ArrayMesh am || node is not Node3D n3)
                continue;
            // Props seen from afar (the Meshy buildings and anglers, barrels, the fire pit...) get distance LODs like
            // the fish (FishAsset.WithLods); the rod and reel in the angler's hands are always close and keep theirs.
            if (!id.StartsWith("rods/") && !id.StartsWith("reels/") && WithLods(am) is { } lodded)
                am = lodded;
            // The surfaces are matched to the materials by name; the glTF's own (picture-less) materials are dropped
            // so a cached mesh holds no material.
            for (int s = 0; s < am.GetSurfaceCount(); s++)
            {
                am.SurfaceSetName(s, am.SurfaceGetMaterial(s)?.ResourceName ?? "");
                am.SurfaceSetMaterial(s, null);
            }
            list.Add((node.Name, am, GlobalOf(n3, root)));
        }
        root.Free();
        if (list.Count == 0)
            return null;
        MeshCache.Write(flat, list);
        return list;
    }

    /// <summary>The mesh again with generated LODs (Godot picks one by its size on screen; runtime-loaded glTF gets
    /// none), or null when it is small or the conversion fails.</summary>
    private static ArrayMesh? WithLods(ArrayMesh am)
    {
        int vertices = 0;
        for (int s = 0; s < am.GetSurfaceCount(); s++)
            vertices += am.SurfaceGetArrayLen(s);
        if (vertices <= 1500)
            return null;
        try
        {
            using var im = new ImporterMesh();
            for (int s = 0; s < am.GetSurfaceCount(); s++)
                im.AddSurface(am.SurfaceGetPrimitiveType(s), am.SurfaceGetArrays(s), material: am.SurfaceGetMaterial(s),
                    name: am.SurfaceGetName(s));
            im.GenerateLods(25f, 60f, new Godot.Collections.Array());
            return im.GetMesh();
        }
        catch (Exception e)
        {
            GD.PushWarning($"PropAsset LODs: {e.Message}");
            return null;
        }
    }

    private static Transform3D GlobalOf(Node3D n, Node3D root)
    {
        var t = n.Transform;
        for (var p = n.GetParent() as Node3D; p != null && p != root; p = p.GetParent() as Node3D)
            t = p.Transform * t;
        return t;
    }
}
