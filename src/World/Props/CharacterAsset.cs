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
    private const int Version = 2;     // 2: cloth pictures toned down (ClothGrade)

    public static readonly string[] Models = { "male_peasant", "female_peasant", "male_ranger", "female_ranger",
        "mh_male_worker", "mh_male_casual", "mh_male_denim", "mh_male_old", "mh_female_casual", "mh_female_middle" };

    /// <summary>The realistic MakeHuman villagers (tools/assets/makehuman_build.py), used when present.</summary>
    public static bool Realistic => Has("mh_male_worker");

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
    /// How much this body's pelvis stands lower (below 1) or higher than the animations' (MakeHuman models: its legs'
    /// length against the Quaternius skeleton the shared animations were made for); 1 for the Quaternius models.
    /// </summary>
    public static float PelvisScaleOf(string id) => (float?)Load(id)?.Info["pelvis_scale"] ?? 1f;

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
        // A model that has only its own hairstyle (the MakeHuman ones) shows that one whatever the look asked for.
        string? hairShown = look.Hair != null && hair.Contains(look.Hair) ? look.Hair : hair.FirstOrDefault();
        foreach (var node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)node;
            string name = mi.Name;
            if (name == "Hair_Beard")
                mi.Visible = look.Beard;
            else if (hair.Contains(name))
                mi.Visible = !look.Hood && name == hairShown;
            else if (name == "Head_Hood")
                mi.Visible = look.Hood;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                mi.SetSurfaceOverrideMaterial(s, MaterialFor(model, mi.Mesh.SurfaceGetMaterial(s)?.ResourceName ?? "outfit", look));
            mi.VisibilityRangeEnd = 160f;
            mi.VisibilityRangeEndMargin = 10f;
            mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
        }
        // The shared animations lift the pelvis as high as the Quaternius legs are long: fit that to this body's legs.
        float pelvis = PelvisScaleOf(look.Model);
        if (MathF.Abs(pelvis - 1f) > 0.002f && SkeletonOf(root) is { } fitSkeleton)
            fitSkeleton.AddChild(new PelvisFit { Factor = pelvis });
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
            bool alpha = name.EndsWith("_t");     // hair cards, brows, lashes: colour with alpha
            // The file's size joins the cache key, so a rebuilt picture never meets an old cached one.
            long bytes;
            using (var f = FileAccess.Open(path, FileAccess.ModeFlags.Read))
                bytes = (long)(f?.GetLength() ?? 0);
            var kind = normal ? BlockCompress.Kind.TwoChannel : alpha ? BlockCompress.Kind.ColourAlpha : BlockCompress.Kind.Colour;
            var (img, _) = TextureStore.Get($"char_{name}_{bytes}", Version, kind, () =>
            {
                var image = new Image();
                image.LoadWebpFromBuffer(FileAccess.GetFileAsBytes(path));
                image.Convert(Image.Format.Rgba8);
                if (IsCloth(name))
                    ClothGrade(image);
                return image;
            }, normalMap: normal);
            if (cacheOnly)
                return null;
            var tex = ImageTexture.CreateFromImage(img);
            Pictures[name] = tex;
            return tex;
        }
    }

    /// <summary>Clothes, hats and shoes (MakeHuman's pictures: the worker's overalls are a saturated light blue, the
    /// shirts paper white), toned down as they are read.</summary>
    private static bool IsCloth(string name) =>
        name.StartsWith("mh_") && (name.EndsWith("_outfit_a") || name.EndsWith("_hat_a") || name.EndsWith("_shoes_a"));

    /// <summary>Worn, dyed cloth instead of fresh catalogue colours: saturation down to <see cref="ClothSaturation"/> and
    /// the highlights rolled off towards <see cref="ClothWhite"/>, so white shirts and bright denim stop glowing in the
    /// sun far away (the small figure turns into one flat bright spot there). Dark cloth stays as it is.</summary>
    private static void ClothGrade(Image image)
    {
        byte[] px = image.GetData();
        for (int i = 0; i < px.Length; i += 4)
        {
            float r = px[i] / 255f, g = px[i + 1] / 255f, b = px[i + 2] / 255f;
            float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            // Soft knee: unchanged below Knee, then easing towards ClothWhite.
            float yk = y <= ClothKnee ? y : ClothKnee + (ClothWhite - ClothKnee) * (1f - MathF.Exp(-(y - ClothKnee) / (ClothWhite - ClothKnee)));
            float k = y > 1e-4f ? yk / y : 1f;
            r = (y + (r - y) * ClothSaturation) * k;
            g = (y + (g - y) * ClothSaturation) * k;
            b = (y + (b - y) * ClothSaturation) * k;
            px[i] = (byte)Math.Clamp(r * 255f + 0.5f, 0f, 255f);
            px[i + 1] = (byte)Math.Clamp(g * 255f + 0.5f, 0f, 255f);
            px[i + 2] = (byte)Math.Clamp(b * 255f + 0.5f, 0f, 255f);
        }
        image.SetData(image.GetWidth(), image.GetHeight(), image.HasMipmaps(), image.GetFormat(), px);
    }

    private const float ClothSaturation = 0.6f;
    private const float ClothKnee = 0.35f;
    private const float ClothWhite = 0.62f;

    /// <summary>FISHSIM_NPC_PLAIN=1 (screenshots): no baked shade and no skin relief, to compare.</summary>
    private static readonly bool Plain = System.Environment.GetEnvironmentVariable("FISHSIM_NPC_PLAIN") == "1";

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
            if ((!Plain || slot != "skin") && (string?)spec?["normal"] is string n && Picture(n) is { } nt)
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
            if (!Plain && (string?)spec?["ao"] is string ao && Picture(ao) is { } aot)
            {
                // Baked contact shade (MakeHuman models): folds, seams, the collar on the neck, under the chin. It darkens
                // the sky's light fully and the sun's a little, so creases still read in direct light.
                m.AOEnabled = true;
                m.AOTexture = aot;
                m.AOTextureChannel = BaseMaterial3D.TextureChannel.Red;
                m.AOLightAffect = 0.45f;
            }
            if ((bool?)spec?["alpha"] == true)
            {
                // Hair cards, brows and lashes: soft edges blended over what is behind, sorted by a depth pre-pass.
                m.Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
                m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            }
            switch (slot)
            {
                case "skin":
                    // No screen-space subsurface scattering: in Godot 4.3 its blur pulled the background through half the
                    // face (a see-through "ghost" with straight edges on the shaded cheek). A soft warm sheen at grazing
                    // angles stands in for the light under the skin.
                    m.RimEnabled = true;
                    m.Rim = 0.18f;
                    m.RimTint = 0.25f;
                    // Skin has a soft sheen (oil, the light grazing the cheek), not chalk.
                    m.MetallicSpecular = 0.42f;
                    if (m.RoughnessTexture == null)
                        m.Roughness = 0.55f;
                    // The face's folds (eye sockets, under the nose and chin, behind the ears) shade in sunlight too.
                    if (m.AOEnabled)
                        m.AOLightAffect = 0.7f;
                    // Pores and creases (MakeHuman skins: made from the painting by the build), kept faint.
                    m.NormalScale = 0.6f;
                    break;
                case "hair1" or "hair2":
                    m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                    // Hair: a faint sheen along the strands.
                    m.RimEnabled = true;
                    m.Rim = 0.25f;
                    m.RimTint = 0.8f;
                    break;
                case "outfit":
                    // Open collars, cuffs and hems show the cloth's inner side.
                    m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                    // Woven cloth catches the light at grazing angles (fibre sheen), so folds and seams read in shade.
                    // Kept faint: on a small figure far away the rim brightened the whole silhouette.
                    m.RimEnabled = true;
                    m.Rim = 0.12f;
                    m.RimTint = 0.55f;
                    m.MetallicSpecular = 0.2f;
                    break;
                case "eye":
                    // A wet eye: a sharp clear-coat highlight over the iris; the pack's irises are painted bright, so a little
                    // darker (they glowed turquoise in the sun).
                    m.AlbedoColor = new Color(0.72f, 0.72f, 0.72f);
                    m.MetallicSpecular = 0.8f;
                    m.ClearcoatEnabled = true;
                    m.Clearcoat = 1f;
                    m.ClearcoatRoughness = 0.04f;
                    break;
                case "shoes" or "hat":
                    m.RimEnabled = true;
                    m.Rim = 0.2f;
                    m.RimTint = 0.6f;
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
/// <summary>
/// Scales the pelvis's animated position by <see cref="Factor"/>: the shared animations were made for longer (or shorter)
/// legs, so without it a short-legged villager's feet hang above the ground and its seat floats over the log.
/// </summary>
public partial class PelvisFit : SkeletonModifier3D
{
    private int _pelvis = -2;

    public float Factor { get; set; } = 1f;

    public override void _ProcessModification()
    {
        var sk = GetSkeleton();
        if (sk == null)
            return;
        if (_pelvis == -2)
            _pelvis = sk.FindBone("pelvis");
        if (_pelvis >= 0)
            sk.SetBonePosePosition(_pelvis, sk.GetBonePosePosition(_pelvis) * Factor);
    }
}

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
        using var perfScope = Graphics.Perf.Scope("head-look");
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
