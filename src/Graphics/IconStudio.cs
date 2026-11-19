using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Data;
using FishingSim.Fishing;

namespace FishingSim.Graphics;

/// <summary>
/// Product photographs for the shop: the real 3D models of lures, rods and reels rendered in a small studio (its own
/// world, a soft-box sky for the metal to reflect, key, fill and rim lights, 4x MSAA at 512 px, scaled down with
/// Lanczos to <see cref="Icons.TextureSize"/>, a soft contact shadow). The painted icon is shown first and the photo
/// replaces its picture in place (every control showing that texture updates). Photos are kept in
/// user://texcache/icons so each is rendered once. Not available headless (the painted icons stay).
/// </summary>
public partial class IconStudio : Node
{
    public enum Shot { Lure, Plate, Rod, Reel }

    /// <summary>Bump when the models or the studio change.</summary>
    public const int Version = 9;     // 9: small_fjordline re-cut (its whole crank arm turns)
    private const int RenderSize = 512;
    private const string Dir = "user://texcache/icons";

    private static IconStudio? _instance;
    private static readonly HashSet<string> Requested = new();
    private readonly Queue<(string File, ImageTexture Target, Shot Shot, object Item)> _queue = new();
    private SubViewport _view = null!;
    private Camera3D _camera = null!;
    private Node3D _stage = null!;
    private bool _busy;

    public static bool Available => DisplayServer.GetName() != "headless" && !Engine.IsEditorHint();

    /// <summary>Photos still waiting to be taken.</summary>
    public static int Pending => _instance == null ? 0 : _instance._queue.Count + (_instance._busy ? 1 : 0);

    /// <summary>Replaces <paramref name="target"/>'s picture with a photo of the item (from the cache or rendered soon).</summary>
    public static void Request(string key, string signature, ImageTexture target, Shot shot, object item)
    {
        if (!Available || !Requested.Add(key))
            return;
        string file = $"{Dir}/{Sanitise(key)}_{Hash(signature):x8}_v{Version}.png";
        if (FileAccess.FileExists(file))
        {
            var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(file));
            if (img != null && !img.IsEmpty())
            {
                target.SetImage(img);
                return;
            }
        }
        Ensure();
        _instance?._queue.Enqueue((file, target, shot, item));
    }

    private static string Sanitise(string s)
    {
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (!char.IsLetterOrDigit(chars[i]))
                chars[i] = '_';
        return new string(chars);
    }

    private static uint Hash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s)
            h = (h ^ c) * 16777619;
        return h;
    }

    private static void Ensure()
    {
        if (_instance != null && IsInstanceValid(_instance))
            return;
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;
        _instance = new IconStudio { Name = "IconStudio" };
        tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
    }

    public override void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute(Dir);
        _view = new SubViewport
        {
            Size = new Vector2I(RenderSize, RenderSize), TransparentBg = true, OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Msaa4X, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(_view);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky, AmbientLightEnergy = 0.55f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            Sky = new Sky { SkyMaterial = new PanoramaSkyMaterial { Panorama = SoftBoxes() }, RadianceSize = Sky.RadianceSizeEnum.Size256 },
            TonemapMode = Godot.Environment.ToneMapper.Filmic, TonemapExposure = 1.05f,
        };
        _view.AddChild(new WorldEnvironment { Environment = env });
        // Key light from the upper left front, a cooler fill from the right, a rim light from behind.
        _view.AddChild(new DirectionalLight3D { LightEnergy = 1.6f, LightColor = new Color(1f, 0.97f, 0.92f), RotationDegrees = new Vector3(-40f, -60f, 0f), ShadowEnabled = false });
        _view.AddChild(new DirectionalLight3D { LightEnergy = 0.5f, LightColor = new Color(0.85f, 0.9f, 1f), RotationDegrees = new Vector3(-10f, -150f, 0f) });
        _view.AddChild(new DirectionalLight3D { LightEnergy = 0.9f, RotationDegrees = new Vector3(-20f, 100f, 0f) });
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 0.1f, Near = 0.01f, Far = 20f, Current = true };
        _view.AddChild(_camera);
        // The camera stands on -X looking along +X: +Z is to the right, +Y up.
        _camera.Position = new Vector3(-5f, 0f, 0f);
        _camera.LookAt(Vector3.Zero, Vector3.Up);
        _stage = new Node3D { Name = "Stage" };
        _view.AddChild(_stage);
    }

    /// <summary>A studio panorama: dark grey, a big soft box overhead and two strip lights either side.</summary>
    private static ImageTexture SoftBoxes()
    {
        const int w = 512, h = 256;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgbf);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float lat = 0.5f - (y + 0.5f) / h;            // 0.5 up .. -0.5 down
                float lon = (x + 0.5f) / w;
                float v = 0.12f + 0.1f * lat;
                if (lat > 0.3f) v += 2.6f;                                    // overhead box
                float d1 = MathF.Abs(lon - 0.25f), d2 = MathF.Abs(lon - 0.75f);
                if (MathF.Abs(lat) < 0.22f && (d1 < 0.035f || d2 < 0.02f)) v += d1 < 0.035f ? 3.2f : 1.6f;
                if (lat < -0.2f) v *= 0.5f;                                   // floor
                img.SetPixel(x, y, new Color(v, v * 0.98f, v * 0.95f));
            }
        return ImageTexture.CreateFromImage(img);
    }

    public override void _Process(double delta)
    {
        if (_busy || _queue.Count == 0)
            return;
        var job = _queue.Dequeue();
        Take(job.File, job.Target, job.Shot, job.Item);
    }

    private async void Take(string file, ImageTexture target, Shot shot, object item)
    {
        _busy = true;
        Node3D? model = null;
        try
        {
            model = Pose(shot, item);
            if (model == null)
                return;
            // Frame the model: its bounds as the camera sees them (Z across, Y up), a margin round it.
            var box = Bounds(model);
            float size = MathF.Max(box.Size.Z, box.Size.Y) * 1.16f;
            var centre = box.GetCenter();
            _camera.Size = MathF.Max(size, 0.005f);
            _camera.Position = new Vector3(centre.X - 5f, centre.Y, centre.Z);
            _view.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var img = _view.GetTexture().GetImage();
            if (img == null || img.IsEmpty())
                return;
            img.Convert(Image.Format.Rgba8);
            img.Resize(Icons.TextureSize, Icons.TextureSize, Image.Interpolation.Lanczos);
            img = WithShadow(img);
            target.SetImage(img);
            img.SavePng(file);
        }
        catch (Exception e)
        {
            GD.PushWarning($"IconStudio: {e.Message}");
        }
        finally
        {
            model?.QueueFree();
            _busy = false;
        }
    }

    /// <summary>A soft shadow under the object, offset down and right, so it sits on the card.</summary>
    private static Image WithShadow(Image img)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        var src = img.GetData();
        var a = new float[w * h];
        for (int i = 0; i < w * h; i++)
            a[i] = src[i * 4 + 3] / 255f;
        var s = new float[w * h];
        const int dx = 3, dy = 5, r = 4;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                int n = 0;
                for (int j = -r; j <= r; j += 2)
                    for (int i = -r; i <= r; i += 2)
                    {
                        int sx = x - dx + i, sy = y - dy + j;
                        if (sx >= 0 && sy >= 0 && sx < w && sy < h)
                            sum += a[sy * w + sx];
                        n++;
                    }
                s[y * w + x] = sum / n * 0.35f;
            }
        var dst = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            float fa = a[i], sa = s[i] * (1f - fa);
            float outA = fa + sa;
            for (int c = 0; c < 3; c++)
            {
                float col = src[i * 4 + c] / 255f;
                // The render's colours are premultiplied by its alpha over the transparent background.
                float straight = fa > 1e-3f ? MathF.Min(col, 1f) : 0f;
                float mixed = outA > 1e-4f ? (straight * fa + 0.05f * sa) / outA : 0f;
                dst[i * 4 + c] = (byte)Math.Clamp((int)(mixed * 255f + 0.5f), 0, 255);
            }
            dst[i * 4 + 3] = (byte)Math.Clamp((int)(outA * 255f + 0.5f), 0, 255);
        }
        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, dst);
    }

    private static Aabb FacesBox(Transform3D xf, Mesh mesh)
    {
        var faces = mesh.GetFaces();
        if (faces.Length == 0)
            return xf * mesh.GetAabb();
        var box = new Aabb(xf * faces[0], Vector3.Zero);
        for (int i = 1; i < faces.Length; i += 3)
            box = box.Expand(xf * faces[i]);
        return box;
    }

    private static Aabb Bounds(Node3D root)
    {
        Aabb? box = null;
        void Walk(Node n)
        {
            if (n is VisualInstance3D vi && (n is not Node3D nd || nd.IsVisibleInTree()))
            {
                // The modelled reel's parts report AABBs well off their vertices (the frame came out twice too large
                // with the reel in a corner, or cut the crank off): meshes are measured from their faces.
                var b = n is MeshInstance3D { Mesh: { } m } ? FacesBox(vi.GlobalTransform, m) : vi.GlobalTransform * vi.GetAabb();
                box = box == null ? b : box.Value.Merge(b);
            }
            foreach (var c in n.GetChildren())
                Walk(c);
        }
        Walk(root);
        return box ?? new Aabb(Vector3.Zero, Vector3.One * 0.05f);
    }

    /// <summary>Builds the item's model on the stage, posed for its photograph.</summary>
    private Node3D? Pose(Shot shot, object item)
    {
        switch (shot)
        {
            case Shot.Lure:
            case Shot.Plate:
            {
                var bait = (BaitData)item;
                var lure = LureModels.Build(bait, out var spin, out _);
                var holder = new Node3D();
                _stage.AddChild(holder);
                holder.AddChild(lure);
                if (shot == Shot.Plate)
                    // A spoon lies with its painted face to the camera, diagonally.
                    holder.Rotation = new Vector3(-0.55f, 0f, Mathf.Pi / 2f);
                else
                    // Side view, nose up and to the right, turned a little to show the flank and back.
                    holder.Rotation = new Vector3(-0.32f, -0.42f, 0f);
                if (spin != null)
                    spin.Rotation = new Vector3(0f, 0f, Mathf.Pi);   // the blade's painted face to the camera
                return holder;
            }
            case Shot.Rod:
            {
                var rig = new RodRig();                        // the item's own Meshy model (built parts without one)
                _stage.AddChild(rig);
                rig.Position = Vector3.Zero;
                rig.SetLook((RodData)item, null);
                // The handle and the butt section to the first guide, diagonally: cork or foam, seat, whippings.
                foreach (var c in rig.GetChildren())
                    if (c is Node3D n && ReelParts.Contains(n.Name.ToString()))
                        n.Visible = false;
                rig.Rotation = new Vector3(Mathf.DegToRad(52f), 0.35f, 0f);
                HideBeyond(rig, 0.95f);
                return rig;
            }
            case Shot.Reel:
            {
                var rig = new RodRig();                        // the item's own Meshy model (the built reel without one)
                _stage.AddChild(rig);
                rig.Position = Vector3.Zero;
                rig.SetLook(null, (ReelData)item);
                foreach (var c in rig.GetChildren())
                    if (c is Node3D n && !ReelParts.Contains(n.Name.ToString()))
                        n.Visible = false;
                rig.Rotation = new Vector3(-Mathf.Pi / 2f + 0.15f, 0.5f, 0f);
                return rig;
            }
        }
        return null;
    }

    /// <summary>The rig's reel nodes: the built one and a modelled one's body, rotor (with the bail) and crank.</summary>
    private static readonly HashSet<string> ReelParts = new() { "Reel", "ModelReel", "ModelRotor", "ModelCrank" };

    /// <summary>Hides the parts of a rod further than <paramref name="reach"/> metres up the blank (rig space).</summary>
    private static void HideBeyond(RodRig rig, float reach)
    {
        void Walk(Node n)
        {
            foreach (var c in n.GetChildren())
            {
                if (c is Node3D nd)
                {
                    var local = rig.GlobalTransform.AffineInverse() * nd.GlobalPosition;
                    if (local.Y > reach)
                    {
                        nd.Visible = false;
                        continue;
                    }
                }
                Walk(c);
            }
        }
        Walk(rig);
    }
}
