using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using FishingSim.Graphics;

namespace FishingSim.World.Props;

/// <summary>How one person looks: which model, which colouring of the outfit, hair, skin.</summary>
public sealed record Appearance(string Model, int OutfitVariant, string? Hair, Color HairColour, Color SkinTint, bool Beard, bool Hood,
    Color OutfitTint);

/// <summary>
/// The villagers' bodies: Quaternius's base characters dressed in his Peasant and Ranger outfits (CC0: free for any
/// use, commercial too), put together by tools/assets/quaternius.py into assets/characters/&lt;model&gt;.glb: textured,
/// with every hairstyle as its own mesh, on the outfit's skeleton. The pictures (assets/characters/textures) are decoded
/// and block compressed by the game and cached like the other textures; the animations (Universal Animation Library,
/// retargeted to each sex's skeleton) come from anims_male/anims_female.glb and are shared by every villager. Without a
/// screen (tests) nothing is loaded and callers fall back to their primitive figures.
/// </summary>
public static class CharacterAsset
{
    private const int Version = 1;

    public static readonly string[] Models = { "male_peasant", "female_peasant", "male_ranger", "female_ranger" };

    private sealed record Model(PackedScene Scene, JsonObject Info, float Height, bool Male);

    private static readonly Dictionary<string, Model?> Cache = new();
    private static readonly Dictionary<bool, AnimationLibrary?> Libraries = new();
    private static readonly Dictionary<string, Texture2D> Pictures = new();
    private static readonly Dictionary<string, Material> Materials = new();

    private static string Dir => "res://assets/characters";

    public static bool Has(string id) => !Renderer.IsHeadless && FileAccess.FileExists($"{Dir}/{id}.glb");

    /// <summary>The model's height in its rest pose (top of the head, m).</summary>
    public static float HeightOf(string id) => Load(id)?.Height ?? 1.8f;

    /// <summary>
    /// A new villager, feet at the origin facing +Z, dressed as <paramref name="look"/> says, with an AnimationPlayer
    /// holding the shared library (playing <paramref name="idle"/> from a random point once it enters the tree; no
    /// animations at all when it is null, for a skeleton posed by code). Null when the model is missing or there is no
    /// screen.
    /// </summary>
    public static Node3D? Create(Appearance look, string? idle = "Idle_Loop")
    {
        var model = Load(look.Model);
        if (model == null)
            return null;
        var root = model.Scene.Instantiate<Node3D>();
        var hair = model.Info["hair"]?.AsArray().Select(h => (string)h!).ToHashSet() ?? new HashSet<string>();
        foreach (var node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)node;
            string name = mi.Name;
            if (name == "Hair_Beard")
                mi.Visible = look.Beard;
            else if (hair.Contains(name))
                mi.Visible = !look.Hood && name == look.Hair;
            else if (name == "Head_Hood")
                mi.Visible = look.Hood;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                mi.SetSurfaceOverrideMaterial(s, MaterialFor(model, mi.Mesh.SurfaceGetMaterial(s)?.ResourceName ?? "outfit", look));
            mi.VisibilityRangeEnd = 160f;
            mi.VisibilityRangeEndMargin = 10f;
            mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
        }
        if (idle != null && LibraryFor(model.Male) is { } library)
        {
            var player = new AnimationPlayer { Name = "AnimationPlayer" };
            player.AddAnimationLibrary("", library);
            root.AddChild(player);
            root.Ready += () =>
            {
                string name = player.HasAnimation(idle) ? idle : "Idle_Loop";
                player.Play(name);
                player.Seek(GD.Randf() * player.GetAnimation(name).Length, true);
            };
        }
        return root;
    }

    public static AnimationPlayer? PlayerOf(Node root) => root.FindChild("AnimationPlayer", true, false) as AnimationPlayer;

    public static Skeleton3D? SkeletonOf(Node root)
    {
        foreach (var n in root.FindChildren("*", "Skeleton3D", true, false))
            return (Skeleton3D)n;
        return null;
    }

    /// <summary>Decodes and compresses every picture into the disk cache (no textures are made: safe on a worker thread).</summary>
    public static void Prewarm()
    {
        if (Renderer.IsHeadless)
            return;
        using var dir = DirAccess.Open($"{Dir}/textures");
        if (dir == null)
            return;
        foreach (var file in dir.GetFiles())
            if (file.EndsWith(".webp"))
                Picture(file[..^5], cacheOnly: true);
    }

    // ------------------------------------------------------------------ materials
    private static Texture2D? Picture(string name, bool cacheOnly = false)
    {
        lock (Pictures)
        {
            if (Pictures.TryGetValue(name, out var done))
                return done;
            string path = $"{Dir}/textures/{name}.webp";
            if (!FileAccess.FileExists(path))
                return null;
            bool normal = name.EndsWith("_n");
            var (img, _) = TextureStore.Get($"char_{name}", Version, normal ? BlockCompress.Kind.TwoChannel : BlockCompress.Kind.Colour, () =>
            {
                var image = new Image();
                image.LoadWebpFromBuffer(FileAccess.GetFileAsBytes(path));
                image.Convert(Image.Format.Rgba8);
                return image;
            }, normalMap: normal);
            if (cacheOnly)
                return null;
            var tex = ImageTexture.CreateFromImage(img);
            Pictures[name] = tex;
            return tex;
        }
    }

    private static Material MaterialFor(Model model, string slot, Appearance look)
    {
        var spec = model.Info["materials"]?[slot] as JsonObject;
        var albedos = spec?["albedo"]?.AsArray().Select(a => (string)a!).ToArray() ?? Array.Empty<string>();
        string albedo = albedos.Length == 0 ? "" : albedos[Math.Abs(look.OutfitVariant) % albedos.Length];
        Color tint = slot switch
        {
            "skin" => look.SkinTint,
            "hair1" or "hair2" => look.HairColour,
            "outfit" => look.OutfitTint,
            _ => Colors.White,
        };
        string key = $"{slot}|{albedo}|{tint.ToHtml()}";
        lock (Materials)
        {
            if (Materials.TryGetValue(key, out var cached))
                return cached;
            var m = BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = tint, Roughness = 1f, Metallic = 0f }, true);
            if (Picture(albedo) is { } a)
                m.AlbedoTexture = a;
            if ((string?)spec?["normal"] is string n && Picture(n) is { } nt)
            {
                m.NormalEnabled = true;
                m.NormalTexture = nt;
            }
            if ((string?)spec?["orm"] is string orm && Picture(orm) is { } ot)
            {
                m.AOEnabled = true;
                m.AOTexture = ot;
                m.AOTextureChannel = BaseMaterial3D.TextureChannel.Red;
                m.AOLightAffect = 0.25f;
                m.RoughnessTexture = ot;
                m.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green;
                m.MetallicTexture = ot;
                m.MetallicTextureChannel = BaseMaterial3D.TextureChannel.Blue;
                m.Metallic = 1f;
            }
            else if ((string?)spec?["roughness"] is string r && Picture(r) is { } rt)
            {
                m.RoughnessTexture = rt;
                m.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red;
            }
            else
                m.Roughness = (float?)spec?["roughness_value"] ?? 0.8f;
            switch (slot)
            {
                case "skin":
                    // A little light through the skin: ears and fingers glow in the sun instead of going grey.
                    m.SubsurfScatterEnabled = true;
                    m.SubsurfScatterStrength = 0.25f;
                    m.SubsurfScatterSkinMode = true;
                    break;
                case "hair1" or "hair2":
                    m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                    break;
                case "outfit":
                    // Open collars, cuffs and hems show the cloth's inner side.
                    m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                    break;
                case "eye":
                    m.MetallicSpecular = 0.8f;
                    break;
            }
            Materials[key] = m;
            return m;
        }
    }

    // ------------------------------------------------------------------ loading
    private static Model? Load(string id)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(id, out var cached))
                return cached;
            Model? model = null;
            if (Has(id))
            {
                try
                {
                    model = Build(id);
                }
                catch (Exception e)
                {
                    GD.PushWarning($"character {id}: {e.Message}");
                }
            }
            Cache[id] = model;
            return model;
        }
    }

    private static Model? Build(string id)
    {
        var info = JsonNode.Parse(FileAccess.GetFileAsString($"{Dir}/{id}.json")) as JsonObject ?? new JsonObject();
        var root = Generate($"{Dir}/{id}.glb");
        if (root == null)
            return null;
        root.Name = id;
        float top = 0f;
        foreach (var n in root.FindChildren("*", "MeshInstance3D", true, false))
            if (n is MeshInstance3D mi && mi.Name == "Head")
                top = mi.Mesh.GetAabb().End.Y;
        var scene = new PackedScene();
        var err = scene.Pack(root);
        root.Free();
        return err == Error.Ok ? new Model(scene, info, top > 1f ? top : 1.8f, (string?)info["sex"] != "F") : null;
    }

    private static Node3D? Generate(string path)
    {
        var doc = new GltfDocument();
        var state = new GltfState { HandleBinaryImage = 0 };
        if (doc.AppendFromFile(path, state) != Error.Ok || doc.GenerateScene(state) is not Node3D root)
            return null;
        foreach (var node in root.FindChildren("*", "", true, false))
            if (node.Owner == null && node != root)
                node.Owner = root;
        return root;
    }

    private static AnimationLibrary? LibraryFor(bool male)
    {
        lock (Libraries)
        {
            if (Libraries.TryGetValue(male, out var cached))
                return cached;
            AnimationLibrary? library = null;
            string path = $"{Dir}/anims_{(male ? "male" : "female")}.glb";
            if (!Renderer.IsHeadless && FileAccess.FileExists(path) && Generate(path) is { } root)
            {
                if (PlayerOf(root) is { } player && player.HasAnimationLibrary(""))
                {
                    library = player.GetAnimationLibrary("");
                    foreach (var name in library.GetAnimationList())
                    {
                        var anim = library.GetAnimation(name);
                        anim.LoopMode = name.ToString().EndsWith("_Loop") ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
                    }
                    player.RemoveAnimationLibrary("");
                }
                root.Free();
            }
            Libraries[male] = library;
            return library;
        }
    }
}

/// <summary>
/// Turns a villager's head (and a little of the neck) towards the camera after the animation has posed the skeleton:
/// the angler within ten metres in front, otherwise a slow look about. The models face +Z.
/// </summary>
public partial class HeadLook : SkeletonModifier3D
{
    private float _yaw;
    private ulong _last;
    private int _head = -2, _neck = -2;
    private readonly float _phase = GD.Randf() * 40f;

    /// <summary>How far the look about wanders (0 for someone busy at work).</summary>
    public float Wander { get; set; } = 1f;

    public override void _ProcessModification()
    {
        var sk = GetSkeleton();
        if (sk == null)
            return;
        if (_head == -2)
        {
            _head = sk.FindBone("Head");
            _neck = sk.FindBone("neck_01");
        }
        if (_head < 0)
            return;
        ulong now = Time.GetTicksMsec();
        float dt = _last == 0 ? 0f : MathF.Min((now - _last) / 1000f, 0.1f);
        _last = now;
        float t = now / 1000f + _phase;
        float want = (MathF.Sin(t * 0.3f) * 0.35f + MathF.Sin(t * 0.11f) * 0.2f) * Wander;
        if (GetViewport()?.GetCamera3D() is { } cam)
        {
            var local = sk.GlobalTransform.AffineInverse() * cam.GlobalPosition;
            var d = local - sk.GetBoneGlobalPose(_head).Origin;
            float scale = MathF.Max(sk.GlobalTransform.Basis.Scale.X, 0.01f);
            if (new Vector2(d.X, d.Z).Length() * scale < 10f && d.Z > -0.3f)
                want = Math.Clamp(MathF.Atan2(d.X, d.Z), -1.15f, 1.15f);
        }
        _yaw += (want - _yaw) * MathF.Min(dt * 3f, 1f);
        if (_neck >= 0)
            Turn(sk, _neck, _yaw * 0.35f);
        Turn(sk, _head, _yaw * 0.65f);
    }

    /// <summary>Turns a bone about the skeleton's up axis, keeping the rest of its pose.</summary>
    private static void Turn(Skeleton3D sk, int bone, float yaw)
    {
        var global = sk.GetBoneGlobalPose(bone);
        var turned = new Basis(Vector3.Up, yaw) * global.Basis;
        int parent = sk.GetBoneParent(bone);
        var parentBasis = parent >= 0 ? sk.GetBoneGlobalPose(parent).Basis : Basis.Identity;
        sk.SetBonePoseRotation(bone, (parentBasis.Inverse() * turned).GetRotationQuaternion());
    }
}
