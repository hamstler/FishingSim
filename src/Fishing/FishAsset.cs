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
    /// <summary>A model made elsewhere (assets/fish/meshy): one baked skin over an atlas of UV islands, no own fins or eyes.</summary>
    public bool Foreign { get; private init; }

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
    private const int CacheSize = 12;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, FishAsset?> Cache = new();
    private static readonly LinkedList<string> Recent = new();
    private static readonly Dictionary<string, Task<Prepared?>> Decoding = new();
    private static readonly LinkedList<string> DecodeOrder = new();

    public static bool Has(FishSpecies sp) => For(sp) != null;

    /// <summary>Whether the species has a model file (cheap: nothing is loaded).</summary>
    public static bool HasModel(FishSpecies sp)
    {
        string id = FishReference.IdOf(sp);
        return id != "" && FileAccess.FileExists(PathOf(id));
    }

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
                if (Decoding.Remove(DecodeOrder.First.Value, out var dropped))
                    dropped.ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully)
                            t.Result?.DisposeImages();
                    });
                DecodeOrder.RemoveFirst();
            }
            Decoding[id] = Task.Run(() => Prepare(path));
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
                Task<Prepared?>? pending;
                lock (Gate)
                {
                    Decoding.Remove(id, out pending);
                    DecodeOrder.Remove(id);
                }
                Prepared? prepared = null;
                try
                {
                    prepared = pending?.Result;
                }
                catch (AggregateException)
                {
                    prepared = null;     // prepared again below
                }
                asset = Load(path, prepared);
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
                if (Cache.Remove(Recent.Last!.Value, out var old))
                    old?.Release();
                Recent.RemoveLast();
            }
        }
        return asset;
    }

    /// <summary>
    /// Lets go of the textures and meshes now (their native and GPU memory otherwise waits for the finaliser; the lake's
    /// fish load and drop species all the time). Fish still showing them keep them: the engine counts references.
    /// </summary>
    private void Release()
    {
        BodyAlbedo?.Dispose();
        BodyNormal?.Dispose();
        Iris?.Dispose();
        foreach (var (_, mesh, texture) in Fins)
        {
            mesh.Dispose();
            texture?.Dispose();
        }
        foreach (var (mesh, _) in Eyes)
            mesh.Dispose();
        foreach (var (mesh, _) in Corneas)
            mesh.Dispose();
        Body.Dispose();
    }

    private static void Touch(string id)
    {
        Recent.Remove(id);
        Recent.AddFirst(id);
    }

    /// <summary>
    /// The species' model: a model dropped into assets/fish/meshy/&lt;id&gt;.glb (made elsewhere, e.g. with Meshy AI) wins
    /// over the built one in assets/fish/models.
    /// </summary>
    private static string PathOf(string id) =>
        FileAccess.FileExists($"res://assets/fish/meshy/{id}.glb") ? $"res://assets/fish/meshy/{id}.glb" : $"res://assets/fish/models/{id}.glb";

    private static bool IsForeign(string path) => path.Contains("/fish/meshy/");

    /// <summary>Whether the pictures are block compressed for the GPU (FISHSIM_RAW_FISH_TEXTURES=1 keeps them plain).</summary>
    public static bool CompressTextures { get; set; } = System.Environment.GetEnvironmentVariable("FISHSIM_RAW_FISH_TEXTURES") != "1";

    /// <summary>
    /// The model's pictures by name, decoded here with their mipmaps: GLTFDocument makes textures without them, and the
    /// fine scales of a 4096-texel skin would shimmer whenever the fish is small on the screen.
    /// </summary>
    private static Dictionary<string, Image> DecodeImages(byte[] glb, int maxBody = 0, int maxFin = 0)
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
            if (maxBody > 0)
                max = name.StartsWith("Fin") || name == "iris" ? maxFin : maxBody;
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

    // ------------------------------------------------------------------ the lake's fish
    /// <summary>Texture sizes of the light copies the lake's fish wear (body and normal map; fins and iris).</summary>
    public const int LiteBody = 1024, LiteFin = 512;

    private static readonly Dictionary<string, FishAsset?> LiteCache = new();
    private static readonly Dictionary<string, Task<Prepared?>> LiteDecoding = new();

    /// <summary>
    /// Starts decoding a light copy of the species' model (small pictures) on a worker thread, for the fish swimming in
    /// the lake; <see cref="LiteIfReady"/> hands it over once decoded. All the lake's species stay cached (a few hundred
    /// kilobytes each).
    /// </summary>
    public static void PrefetchLite(FishSpecies sp)
    {
        string id = FishReference.IdOf(sp);
        string path = PathOf(id);
        lock (Gate)
        {
            if (id == "" || LiteCache.ContainsKey(id) || LiteDecoding.ContainsKey(id) || !FileAccess.FileExists(path))
                return;
            LiteDecoding[id] = Task.Run(() => Prepare(path, LiteBody, LiteFin));
        }
    }

    /// <summary>
    /// The light copy when it is ready (its meshes are read here, on the calling thread); null while it is still being
    /// decoded (<paramref name="pending"/>) or when the species has no model.
    /// </summary>
    public static FishAsset? LiteIfReady(FishSpecies sp, out bool pending)
    {
        string id = FishReference.IdOf(sp);
        Task<Prepared?>? task;
        lock (Gate)
        {
            pending = false;
            if (LiteCache.TryGetValue(id, out var cached))
                return cached;
            if (!LiteDecoding.TryGetValue(id, out task))
                return null;
            if (!task.IsCompleted)
            {
                pending = true;
                return null;
            }
            LiteDecoding.Remove(id);
        }
        FishAsset? asset = null;
        try
        {
            asset = Load(PathOf(id), task.Result);
        }
        catch (Exception e)
        {
            GD.PushWarning($"fish model {id} (lake copy): {e.Message}");
        }
        lock (Gate)
            LiteCache[id] = asset;
        return asset;
    }

    /// <summary>
    /// A model read and prepared off the main thread: its pictures decoded (mipmaps, block compression), its meshes
    /// parsed with their levels of detail and, for a foreign model, joined, turned and given tangents. Only the upload
    /// to the GPU is left for the main thread.
    /// </summary>
    private sealed class Prepared
    {
        public required Dictionary<string, Image> Images { get; init; }
        public required List<(string Name, ImporterMesh Mesh, Transform3D Transform)> Parts { get; init; }
        public bool Foreign { get; init; }
        public float HeadHeightAtEye { get; init; }
        public float EyeRadius { get; init; }
        public Aabb BodyBox { get; init; }

        public void DisposeImages()
        {
            foreach (var image in Images.Values)
                image.Dispose();
        }
    }

    /// <summary>Reads and prepares a model (any thread: no rendering-server calls).</summary>
    private static Prepared? Prepare(string path, int maxBody = 0, int maxFin = 0)
    {
        var glb = FileAccess.GetFileAsBytes(path);
        var images = DecodeImages(glb, maxBody, maxFin);
        var doc = new GltfDocument();
        var state = new GltfState { HandleBinaryImage = 0 };
        // GLTFDocument decodes every picture in full even when told to discard them (4K WebP, and the memory for it):
        // it is handed the model without its pictures, which are decoded above with their mipmaps.
        if (doc.AppendFromBuffer(Graphics.ImageDecode.WithoutPictures(glb), path.GetBaseDir(), state) != Error.Ok)
        {
            foreach (var image in images.Values)
                image.Dispose();
            return null;
        }
        // The meshes straight from the parsed state (ImporterMesh, plain arrays): no scene is generated (that would
        // make the GPU meshes here, on a worker thread, and without levels of detail).
        var nodes = state.GetNodes();
        var meshes = state.GetMeshes();
        var parts = new List<(string Name, ImporterMesh Mesh, Transform3D Transform)>();
        for (int n = 0; n < nodes.Count; n++)
        {
            var node = nodes[n];
            if (node.Mesh < 0 || node.Mesh >= meshes.Count || meshes[node.Mesh].Mesh is not { } im)
                continue;
            var xf = node.Xform;
            for (int p = node.Parent; p >= 0; p = nodes[p].Parent)
                xf = nodes[p].Xform * xf;
            parts.Add((node.ResourceName, im, xf));
        }
        if (IsForeign(path))
            return PrepareForeign(path, parts, images);
        foreach (var part in parts)
            WithLods(part.Mesh);
        var body = parts.FirstOrDefault(p => p.Name == "Body");
        var eyes = parts.Where(p => p.Name.StartsWith("Eye")).ToList();
        if (body.Mesh == null || eyes.Count == 0)
            return new Prepared { Images = images, Parts = parts };
        var verts = body.Mesh.GetSurfaceArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var eyeVerts = eyes[0].Mesh.GetSurfaceArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        float eyeZ = eyes[0].Transform.Origin.Z;
        // Body height at the eye, from the body's vertices near it.
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var v in verts)
            if (MathF.Abs(v.Z - eyeZ) < 0.008f)
            {
                lo = MathF.Min(lo, v.Y);
                hi = MathF.Max(hi, v.Y);
            }
        var box = BoxOf(verts);
        return new Prepared
        {
            Images = images, Parts = parts, BodyBox = box,
            EyeRadius = BoxOf(eyeVerts).Size.Y * 0.5f,
            HeadHeightAtEye = hi > lo ? hi - lo : box.Size.Y * 0.5f,
        };
    }

    private static Aabb BoxOf(Vector3[] verts)
    {
        var box = verts.Length > 0 ? new Aabb(verts[0], Vector3.Zero) : new Aabb();
        foreach (var v in verts)
            box = box.Expand(v);
        return box;
    }

    /// <summary>Uploads a prepared model (main thread) and lets go of its decoded pictures.</summary>
    private static FishAsset? Load(string path, Prepared? prepared)
    {
        prepared ??= Prepare(path);
        if (prepared == null)
            return null;
        try
        {
            return Build(prepared);
        }
        finally
        {
            // The textures hold their own copies; the decoded pictures' native memory goes now, not at finalisation.
            prepared.DisposeImages();
        }
    }

    private static FishAsset? Build(Prepared prepared)
    {
        var images = prepared.Images;
        Texture2D? Picture(string name) =>
            images.TryGetValue(name, out var image) || images.TryGetValue(name.Split('.')[0], out image)
                ? ImageTexture.CreateFromImage(image)
                : null;
        if (prepared.Foreign)
        {
            // The skin and the normal map: the first colour picture, and the one named *normal*.
            string? normalName = images.Keys.FirstOrDefault(k => k.ToLowerInvariant().Contains("normal"));
            string? colourName = images.Keys.FirstOrDefault(k => k != normalName && !k.ToLowerInvariant().Contains("rough") && !k.ToLowerInvariant().Contains("metal"));
            return new FishAsset
            {
                Foreign = true,
                Body = prepared.Parts[0].Mesh.GetMesh(),
                BodyAlbedo = colourName != null ? ImageTexture.CreateFromImage(images[colourName]) : null,
                BodyNormal = normalName != null ? ImageTexture.CreateFromImage(images[normalName]) : null,
                EyeRadius = 0.02f,
                HeadHeightAtEye = prepared.HeadHeightAtEye,
                MaxHeight = prepared.BodyBox.Size.Y,
                MaxWidth = prepared.BodyBox.Size.X,
            };
        }
        var parts = prepared.Parts.Select(p => (p.Name, Mesh: (Mesh)p.Mesh.GetMesh(), p.Transform)).ToList();
        var body = parts.FirstOrDefault(p => p.Name == "Body");
        var eyes = parts.Where(p => p.Name.StartsWith("Eye")).OrderBy(p => p.Name == "EyeLeft").ToList();
        if (body.Mesh == null || eyes.Count == 0)
            return null;
        return new FishAsset
        {
            Body = body.Mesh,
            BodyAlbedo = Picture("body_albedo"),
            BodyNormal = Picture("body_normal"),
            Fins = parts.Where(p => p.Name.StartsWith("Fin")).Select(p => (p.Name, p.Mesh, Picture(p.Name))).ToList(),
            Iris = Picture("iris"),
            Eyes = eyes.Select(p => (p.Mesh, p.Transform.Origin)).ToList(),
            Corneas = parts.Where(p => p.Name.StartsWith("Cornea")).Select(p => (p.Mesh, p.Transform.Origin)).ToList(),
            EyeRadius = prepared.EyeRadius,
            HeadHeightAtEye = prepared.HeadHeightAtEye,
            MaxHeight = prepared.BodyBox.Size.Y,
            MaxWidth = prepared.BodyBox.Size.X,
        };
    }

    /// <summary>
    /// A model made outside tools/fish_model (one or more meshes in any orientation and size, e.g. from Meshy AI): all
    /// its meshes are joined into one body, turned so the fish lies along +Z with the nose at +0.5 (unit length), the
    /// back up and centred. The nose is found from the shape (the head is thicker across than the tail fin) unless
    /// &lt;id&gt;.json beside it says {"nose": "+x" | "-x" | "+y" | "-y" | "+z" | "-z", "up": ...}. Pictures: the first
    /// colour picture is the skin, one named *normal* the normal map. The eyes are in the skin, so none are added.
    /// </summary>
    private static Prepared? PrepareForeign(string path, List<(string Name, ImporterMesh Mesh, Transform3D Transform)> parts,
        Dictionary<string, Image> images)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var index = new List<int>();
        foreach (var (_, mesh, xf) in parts)
            for (int sfc = 0; sfc < mesh.GetSurfaceCount(); sfc++)
            {
                var arr = mesh.GetSurfaceArrays(sfc);
                var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var n = arr[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var uv = arr[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                var ix = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
                int start = verts.Count;
                for (int i = 0; i < v.Length; i++)
                {
                    verts.Add(xf * v[i]);
                    normals.Add(n != null ? (xf.Basis * n[i]).Normalized() : Vector3.Up);
                    uvs.Add(uv != null ? uv[i] : Vector2.Zero);
                }
                if (ix != null)
                    foreach (int k in ix)
                        index.Add(start + k);
                else
                    for (int k = 0; k < v.Length; k++)
                        index.Add(start + k);
            }
        if (verts.Count < 3)
        {
            foreach (var image in images.Values)
                image.Dispose();
            return null;
        }
        var box = new Aabb(verts[0], Vector3.Zero);
        foreach (var v in verts)
            box = box.Expand(v);
        var size = box.Size;
        Vector3 Axis(int i) => i == 0 ? Vector3.Right : i == 1 ? Vector3.Up : Vector3.Back;
        float Comp(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
        int along = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
        int[] others = Enumerable.Range(0, 3).Where(i => i != along).ToArray();
        int across = Comp(size, others[0]) <= Comp(size, others[1]) ? others[0] : others[1];   // the thin side: width
        int upAxis = others[0] == across ? others[1] : others[0];
        Vector3 nose, up;
        string json = path.GetBaseName() + ".json";
        var spec = FileAccess.FileExists(json) ? JsonNode.Parse(FileAccess.GetFileAsString(json)) as JsonObject : null;
        static Vector3? Dir(string? s) => s switch
        {
            "+x" => Vector3.Right, "-x" => Vector3.Left, "+y" => Vector3.Up, "-y" => Vector3.Down, "+z" => Vector3.Back, "-z" => Vector3.Forward, _ => null,
        };
        if (Dir((string?)spec?["nose"]) is { } givenNose)
            nose = givenNose;
        else
        {
            // The head end is thicker across than the tail's thin fin: compare the width in the end tenths.
            float lo = Comp(box.Position, along), len = Comp(size, along);
            float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
            foreach (var v in verts)
            {
                float t = (Comp(v, along) - lo) / len, c = Comp(v, across);
                if (t < 0.15f) { minA = MathF.Min(minA, c); maxA = MathF.Max(maxA, c); }
                else if (t > 0.85f) { minB = MathF.Min(minB, c); maxB = MathF.Max(maxB, c); }
            }
            float wTail = maxA - minA, wHead = maxB - minB;
            nose = Axis(along) * (wHead >= wTail ? 1f : -1f);
        }
        up = Dir((string?)spec?["up"]) ?? Axis(upAxis);
        // Model basis: X = right side, Y = up, Z = nose.
        var z = nose.Normalized();
        var y = (up - z * up.Dot(z)).Normalized();
        var x = y.Cross(z);
        var toModel = new Basis(x, y, z).Transposed();
        float length = MathF.Abs(Comp(size, along));
        var centre = box.GetCenter();
        var outV = new Vector3[verts.Count];
        var outN = new Vector3[verts.Count];
        var outBox = new Aabb();
        for (int i = 0; i < verts.Count; i++)
        {
            outV[i] = toModel * (verts[i] - centre) / length;
            outN[i] = (toModel * normals[i]).Normalized();
            outBox = i == 0 ? new Aabb(outV[i], Vector3.Zero) : outBox.Expand(outV[i]);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = outV;
        arrays[(int)Mesh.ArrayType.Normal] = outN;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = index.ToArray();
        // Tangents for the normal map, and levels of detail (a lake fish a few metres off needs a fraction of them).
        var st = new SurfaceTool();
        st.CreateFromArrays(arrays);
        st.GenerateTangents();
        var body = new ImporterMesh();
        body.AddSurface(Mesh.PrimitiveType.Triangles, st.CommitToArrays());
        WithLods(body);
        // Height at the head (the eye would sit about a fifth back from the nose).
        float lo2 = float.MaxValue, hi2 = float.MinValue;
        foreach (var v in outV)
            if (MathF.Abs(v.Z - 0.32f) < 0.02f)
            {
                lo2 = MathF.Min(lo2, v.Y);
                hi2 = MathF.Max(hi2, v.Y);
            }
        return new Prepared
        {
            Images = images, Foreign = true, BodyBox = outBox,
            Parts = new List<(string, ImporterMesh, Transform3D)> { ("Body", body, Transform3D.Identity) },
            HeadHeightAtEye = hi2 > lo2 ? hi2 - lo2 : outBox.Size.Y * 0.5f,
        };
    }

    /// <summary>Levels of detail for meshes worth it (the body, ray-tubed fins); Godot picks one by screen size.</summary>
    private static void WithLods(ImporterMesh mesh)
    {
        if (mesh.GetSurfaceCount() > 0 && mesh.GetSurfaceLodCount(0) == 0
            && mesh.GetSurfaceArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length > 1500)
            mesh.GenerateLods(25f, 60f, new Godot.Collections.Array());
    }

}
