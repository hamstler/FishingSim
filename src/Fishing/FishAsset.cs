using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
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

    /// <summary>The clear corneas over the eyes, when the model has them.</summary>
    public IReadOnlyList<(Mesh Mesh, Vector3 Position)> Corneas { get; private init; } = Array.Empty<(Mesh, Vector3)>();

    public float EyeRadius { get; private init; }
    public float HeadHeightAtEye { get; private init; }
    public float MaxHeight { get; private init; }
    public float MaxWidth { get; private init; }

    // A few species' models stay loaded (their textures are large); the one used longest ago is let go.
    private const int CacheSize = 6;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, FishAsset?> Cache = new();
    private static readonly LinkedList<string> Recent = new();
    private static readonly Dictionary<string, Task<Dictionary<string, Image>>> Decoding = new();
    private static readonly LinkedList<string> DecodeOrder = new();

    public static bool Has(FishSpecies sp) => For(sp) != null;

    /// <summary>
    /// Starts decoding a species' pictures on a worker thread (when a fish takes the bait), so that its model is ready
    /// without a hitch when the fish is hooked.
    /// </summary>
    public static void Prefetch(FishSpecies sp)
    {
        string id = FishReference.IdOf(sp);
        string path = PathOf(id);
        lock (Gate)
        {
            if (id == "" || Cache.ContainsKey(id) || Decoding.ContainsKey(id) || !FileAccess.FileExists(path))
                return;
            // Decoded pictures wait for their model at most four at a time (browsing the atlas decodes ahead).
            while (Decoding.Count >= 4 && DecodeOrder.First != null)
            {
                Decoding.Remove(DecodeOrder.First.Value);
                DecodeOrder.RemoveFirst();
            }
            Decoding[id] = Task.Run(() => DecodeImages(FileAccess.GetFileAsBytes(path)));
            DecodeOrder.AddLast(id);
        }
    }

    /// <summary>Whether <see cref="For"/> would return at once (the model is loaded, or its pictures are decoded).</summary>
    public static bool IsReady(FishSpecies sp)
    {
        string id = FishReference.IdOf(sp);
        lock (Gate)
            return Cache.ContainsKey(id) || !Decoding.TryGetValue(id, out var task) || task.IsCompleted;
    }

    public static FishAsset? For(FishSpecies sp)
    {
        string id = FishReference.IdOf(sp);
        lock (Gate)
        {
            if (Cache.TryGetValue(id, out var cached))
            {
                Touch(id);
                return cached;
            }
        }
        FishAsset? asset = null;
        string path = PathOf(id);
        if (id != "" && FileAccess.FileExists(path))
        {
            try
            {
                Task<Dictionary<string, Image>>? pending;
                lock (Gate)
                {
                    Decoding.Remove(id, out pending);
                    DecodeOrder.Remove(id);
                }
                Dictionary<string, Image>? images = null;
                try
                {
                    images = pending?.Result;
                }
                catch (AggregateException)
                {
                    images = null;     // decoded again below
                }
                asset = Load(path, images);
            }
            catch (Exception e)
            {
                GD.PushWarning($"fish model {path}: {e.Message}");
            }
        }
        lock (Gate)
        {
            Cache[id] = asset;
            Touch(id);
            while (Recent.Count > CacheSize)
            {
                Cache.Remove(Recent.Last!.Value);
                Recent.RemoveLast();
            }
        }
        return asset;
    }

    private static void Touch(string id)
    {
        Recent.Remove(id);
        Recent.AddFirst(id);
    }

    private static string PathOf(string id) => $"res://assets/fish/models/{id}.glb";

    /// <summary>Whether the pictures are block compressed for the GPU (FISHSIM_RAW_FISH_TEXTURES=1 keeps them plain).</summary>
    public static bool CompressTextures { get; set; } = System.Environment.GetEnvironmentVariable("FISHSIM_RAW_FISH_TEXTURES") != "1";

    /// <summary>
    /// The model's pictures by name, decoded here with their mipmaps: GLTFDocument makes textures without them, and the
    /// fine scales of a 4096-texel skin would shimmer whenever the fish is small on the screen.
    /// </summary>
    private static Dictionary<string, Image> DecodeImages(byte[] glb)
    {
        var images = new Dictionary<string, Image>();
        int jsonLength = BitConverter.ToInt32(glb, 12);
        var json = JsonNode.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength))!;
        int bin = 28 + jsonLength;      // the binary chunk's data: after the JSON chunk and its own length and type
        var views = json["bufferViews"]?.AsArray();
        foreach (var node in json["images"]?.AsArray() ?? new JsonArray())
        {
            if (node?["bufferView"] is not JsonNode viewIndex || views == null)
                continue;
            var view = views[(int)viewIndex]!;
            int offset = (int?)view["byteOffset"] ?? 0, length = (int)view["byteLength"]!;
            var bytes = new byte[length];
            Array.Copy(glb, bin + offset, bytes, 0, length);
            var image = new Image();
            var error = (string?)node["mimeType"] switch
            {
                "image/png" => image.LoadPngFromBuffer(bytes),
                "image/webp" => image.LoadWebpFromBuffer(bytes),
                _ => image.LoadJpgFromBuffer(bytes),
            };
            if (error != Error.Ok)
                continue;
            string name = (string?)node["name"] ?? "";
            bool normal = name.EndsWith("normal");
            // Down to the graphics quality's texture size (Low 1024, Medium 2048): a quarter of the memory per halving.
            int max = Graphics.TextureStore.MaxSize;
            if (image.GetWidth() > max || image.GetHeight() > max)
            {
                float k = (float)max / Math.Max(image.GetWidth(), image.GetHeight());
                image.Resize(Math.Max(1, (int)(image.GetWidth() * k)), Math.Max(1, (int)(image.GetHeight() * k)), Image.Interpolation.Cubic);
            }
            image.GenerateMipmaps(renormalize: normal);
            // On the GPU a 4096-texel skin with its normal map takes ~90 MB as plain pixels and about a fifth of it block
            // compressed (BC1/BC3 for colours, BC5 for the normal map). Where the engine has no compressor (an export
            // without one), the pictures stay as they are.
            if (CompressTextures && image.GetWidth() >= 512)
                image.Compress(Image.CompressMode.S3Tc, normal ? Image.CompressSource.Normal : Image.CompressSource.Srgb);
            images[name] = image;
        }
        return images;
    }

    private static FishAsset? Load(string path, Dictionary<string, Image>? images)
    {
        images ??= DecodeImages(FileAccess.GetFileAsBytes(path));
        var doc = new GltfDocument();
        var state = new GltfState { HandleBinaryImage = 0 };    // the pictures are decoded above, with their mipmaps
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

        Texture2D? Picture(string name) =>
            images.TryGetValue(name, out var image) || images.TryGetValue(name.Split('.')[0], out image)
                ? ImageTexture.CreateFromImage(image)
                : null;
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
            BodyAlbedo = Picture("body_albedo"),
            BodyNormal = Picture("body_normal"),
            Fins = parts.Where(p => p.Name.StartsWith("Fin")).Select(p => (p.Name, p.Mesh, Picture(p.Name))).ToList(),
            Iris = Picture("iris"),
            Eyes = eyes.Select(p => (p.Mesh, p.Transform.Origin)).ToList(),
            Corneas = parts.Where(p => p.Name.StartsWith("Cornea")).Select(p => (p.Mesh, p.Transform.Origin)).ToList(),
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
