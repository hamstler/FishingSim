using System;
using System.Collections.Generic;
using Godot;
using FishingSim.World.Props;

namespace FishingSim.Fishing;

/// <summary>
/// First-person spinning rod, built to be a child of the camera (local +Y runs along the rod towards the tip, local -Z
/// is "down", the side the reel hangs on, +X is right). From the butt: a rubber butt cap, the rear cork grip, the reel
/// seat with its hoods and locking nut (where the right hand holds the rod, the reel's stem between its fingers), the
/// fore grip, a winding check, then the blank in ten bending sections with guides underneath, large near the reel and
/// smaller towards the tip-top. Under the seat hangs a classic fixed-spool reel: foot, stem, gearbox body, rotor with
/// two arms, spool with line on it and the drag knob in front, the wire bail round the spool with its line roller, and
/// on the left side the crank handle with its knob (turned by the left hand while reeling, see <see cref="SetCrank"/>).
/// <see cref="SetJointAngles"/> flexes the blank.
/// </summary>
public partial class RodRig : Node3D
{
    private readonly List<Node3D> _segments = new();
    private Node3D _crank = null!, _rotor = null!, _bail = null!;

    /// <summary>The end of the blank; the line starts here.</summary>
    public Node3D Tip { get; private set; } = null!;

    /// <summary>Where the right hand closes round the reel seat (rig space): the reel's stem lies between its middle
    /// and ring fingers.</summary>
    public static readonly Vector3 GripPoint = new(0f, SeatCentre, 0f);

    /// <summary>The reel seat's radius (the hand wraps round it).</summary>
    public const float SeatRadius = 0.0125f;

    /// <summary>Where the reel's stem leaves the foot under the seat (rig space).</summary>
    public static readonly Vector3 StemAt = new(0f, SeatCentre - 0.004f, -SeatRadius - 0.004f);

    /// <summary>
    /// What the hand on the seat must not enter (rig space): the grips, the seat with its hoods and nut, the reel's
    /// foot, stem and body.
    /// </summary>
    public static IEnumerable<(Vector3 A, Vector3 B, float Radius)> SeatObstacles()
    {
        yield return (new Vector3(0, -0.3f, 0), new Vector3(0, SeatCentre - 0.062f, 0), 0.0153f);
        yield return (new Vector3(0, SeatCentre - 0.058f, 0), new Vector3(0, SeatCentre + 0.058f, 0), SeatRadius + 0.001f);
        yield return (new Vector3(0, SeatCentre - 0.058f, 0), new Vector3(0, SeatCentre - 0.046f, 0), 0.0148f);
        yield return (new Vector3(0, SeatCentre + 0.044f, 0), new Vector3(0, SeatCentre + 0.08f, 0), 0.016f);
        yield return (new Vector3(0, SeatCentre + 0.08f, 0), new Vector3(0, BlankStart, 0), 0.0135f);
        yield return (new Vector3(0, SeatCentre - 0.026f, -SeatRadius - 0.0015f), new Vector3(0, SeatCentre + 0.026f, -SeatRadius - 0.0015f), 0.0025f);
        yield return (StemAt, new Vector3(0, StemAt.Y + 0.006f, -0.072f), 0.0042f);
        yield return (ReelBody + new Vector3(0, -0.02f, 0), ReelBody + new Vector3(0, 0.07f, 0), 0.024f);
    }

    /// <summary>What the hand on the crank must not enter (rig space): the knob, the crank arm, the reel's body
    /// (this rig's reel: the built one or the modelled one).</summary>
    /// <summary>The crank knob alone as a capsule (rig space): what the left hand's fingers close on.</summary>
    public (Vector3 A, Vector3 B, float Radius) KnobObstacle(float angle)
    {
        var knob = KnobAt(angle);
        return (knob + new Vector3(0.012f, 0f, 0f), knob - new Vector3(0.012f, 0f, 0f), 0.0085f);
    }

    public IEnumerable<(Vector3 A, Vector3 B, float Radius)> CrankObstacles(float angle)
    {
        var knob = KnobAt(angle);
        var body = ModelledReel ? _modelBodyCentre : ReelBody;
        yield return KnobObstacle(angle);
        yield return (CrankAxis + new Vector3(-0.008f, 0f, 0f), knob + new Vector3(0.018f, 0f, 0f), 0.0045f);
        yield return (body + new Vector3(0, -0.02f, 0), body + new Vector3(0, 0.07f, 0), 0.024f);
    }

    private const float SeatCentre = 0.1f;

    /// <summary>The reel's body centre below the seat (rig space) and the crank's axis point on its left side.</summary>
    private static readonly Vector3 ReelBody = new(0f, SeatCentre - 0.004f, -0.088f);

    private static readonly Vector3 BuiltCrankAxis = ReelBody + new Vector3(-0.026f, 0f, 0f);
    private const float BuiltCrankArm = 0.046f, BuiltKnobOut = -0.05f;

    /// <summary>The crank's hub (rig space): the built reel's, or the modelled reel's when this rig shows one.</summary>
    private Vector3 CrankAxis = BuiltCrankAxis;

    /// <summary>Length of the crank arm (axis to knob) of this rig's reel.</summary>
    public float CrankArm { get; private set; } = BuiltCrankArm;

    /// <summary>The crank knob's centre at a crank angle (rig space): it turns in the plane across the left side.</summary>
    public Vector3 KnobAt(float angle) =>
        CrankAxis + new Vector3(KnobOut, MathF.Sin(angle) * CrankArm, -MathF.Cos(angle) * CrankArm);

    /// <summary>How far the knob stands out to the left of the crank's axis: clear of the reel, so the left hand holds
    /// it between the thumb and the index finger.</summary>
    public float KnobOut { get; private set; } = BuiltKnobOut;

    /// <summary>False before the rig enters the tree: the built rod and reel only (IconStudio photographs the built
    /// parts, which carry each item's colours).</summary>
    public bool UseModels { get; set; } = true;

    /// <summary>
    /// A modelled reel's moving parts (rig space, from the &lt;id&gt;.json that tools/assets/meshy_rod_split.py --reel
    /// writes beside the model): the crank's hub, arm and knob offset, the rotor's pivot on the spool's axis, the bail's
    /// pivot (rotor space) and axis, the body's centre (the hand's obstacle).
    /// </summary>
    private sealed record ReelFrame(Vector3 Hub, float Arm, float KnobOut, Vector3 Rotor, Vector3 BailPivot, Vector3 BailAxis, Vector3 Body);

    private static readonly Dictionary<string, ReelFrame?> Frames = new();

    private static ReelFrame? FrameOf(string id)
    {
        if (Frames.TryGetValue(id, out var cached))
            return cached;
        ReelFrame? frame = null;
        string path = $"res://assets/models/{id}.json";
        if (FileAccess.FileExists(path))
        {
            try
            {
                var j = System.Text.Json.Nodes.JsonNode.Parse(FileAccess.GetFileAsString(path))!;
                Vector3 V(string key) => new((float)j[key]![0]!, (float)j[key]![1]!, (float)j[key]![2]!);
                frame = new ReelFrame(V("hub"), (float)j["arm"]!, (float)j["knob_out"]!, V("rotor"), V("bail_pivot"),
                    V("bail_axis").Normalized(), V("body"));
            }
            catch (Exception e)
            {
                GD.PushWarning($"reel frame {path}: {e.Message}");
            }
        }
        Frames[id] = frame;
        return frame;
    }

    private Node3D? _modelRotor, _modelCrank, _modelBail, _modelReelBody;

    private static readonly Dictionary<string, (Mesh Body, Mesh? Shell, float Radius)> SpoolShells = new();

    /// <summary>
    /// Splits a modelled reel's body into the wound line on its spool (the triangles in front of the rotor, 14-31.5 mm
    /// off the spool's axis, facing outwards) and the rest; cached per model. The rest keeps every other surface as is.
    /// </summary>
    private static (Mesh Body, Mesh? Shell, float Radius) SpoolShellOf(string id, Mesh body, Vector3 rotor)
    {
        if (SpoolShells.TryGetValue(id, out var done))
            return done;
        var rest = new ArrayMesh();
        var shell = new ArrayMesh();
        float radius = 0f;
        for (int si = 0; si < body.GetSurfaceCount(); si++)
        {
            var arrays = body.SurfaceGetArrays(si);
            var material = body.SurfaceGetMaterial(si);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (index.Length == 0 || normals.Length != verts.Length)
            {
                rest.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                rest.SurfaceSetMaterial(rest.GetSurfaceCount() - 1, material);
                continue;
            }
            var keep = new List<int>(index.Length);
            var cut = new List<int>();
            for (int t = 0; t + 2 < index.Length; t += 3)
            {
                var c = (verts[index[t]] + verts[index[t + 1]] + verts[index[t + 2]]) / 3f;
                var n = normals[index[t]] + normals[index[t + 1]] + normals[index[t + 2]];
                var radial = new Vector3(c.X - rotor.X, 0f, c.Z - rotor.Z);
                float r = radial.Length(), dy = c.Y - rotor.Y;
                bool line = dy > 0.033f && dy < 0.056f && r > 0.014f && r < 0.0315f
                    && n.LengthSquared() > 1e-8f && radial.Normalized().Dot(n.Normalized()) > 0.6f;
                var into = line ? cut : keep;
                into.Add(index[t]); into.Add(index[t + 1]); into.Add(index[t + 2]);
                if (line)
                    radius = MathF.Max(radius, r);
            }
            foreach (var (mesh, list) in new[] { (rest, keep), (shell, cut) })
            {
                if (list.Count == 0)
                    continue;
                var copy = new Godot.Collections.Array();
                copy.Resize(arrays.Count);
                for (int k = 0; k < arrays.Count; k++)
                    copy[k] = arrays[k];
                copy[(int)Mesh.ArrayType.Index] = list.ToArray();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, copy);
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
            }
        }
        // Too few triangles: no recognisable wound line on this model; leave the body whole.
        var result = shell.GetSurfaceCount() > 0 && radius > 0f ? ((Mesh)rest, (Mesh?)shell, radius) : (body, null, 0f);
        SpoolShells[id] = result;
        return result;
    }
    private Vector3 _bailAxis = Vector3.Right, _modelBodyCentre;

    /// <summary>Screenshots: FISHSIM_BAIL=0..1 holds the bail at least this far open.</summary>
    private static readonly float BailShot = float.TryParse(System.Environment.GetEnvironmentVariable("FISHSIM_BAIL"),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : 0f;

    public override void _Ready()
    {
        Position = new Vector3(0.3f, -0.35f, -0.4f);
        RotationDegrees = new Vector3(-80f, 0f, 0f);

        // Procedural materials (shaders/tackle.gdshader): carbon under lacquer, cork granules, brushed metal, the
        // anodised housing, line cross-wound on the spool, thread whippings; sharp however close the eye is.
        var cork = _cork = Tackle(1, new Color(0.72f, 0.56f, 0.38f), 0.85f);              // cork grips
        var corkDark = Tackle(1, new Color(0.55f, 0.4f, 0.26f), 0.9f, shade: 0.2f);        // filler rings in the cork
        var rubber = Tackle(7, new Color(0.12f, 0.11f, 0.1f), 0.8f);                        // butt cap
        var seat = Tackle(4, new Color(0.16f, 0.16f, 0.17f), 0.38f, 0.3f);                  // graphite reel seat
        var chrome = Tackle(3, new Color(0.74f, 0.74f, 0.76f), 0.1f, 1f);                   // hoods, guides, bail
        var blank = _blank = Tackle(0, new Color(0.24f, 0.12f, 0.09f), 0.32f, 0.1f);       // lacquered carbon blank
        var whip = _whip = Tackle(6, new Color(0.75f, 0.62f, 0.3f), 0.35f, 0.2f);          // gold thread whippings
        var reelBody = _reelBody = Tackle(4, new Color(0.2f, 0.21f, 0.23f), 0.34f, 0.55f); // anodised reel housing
        var reelTrim = _reelTrim = Tackle(3, new Color(0.62f, 0.52f, 0.3f), 0.26f, 0.9f);  // brushed champagne trim
        var line = Tackle(5, new Color(0.82f, 0.86f, 0.78f), 0.4f);                         // line on the spool
        var knob = Tackle(7, new Color(0.32f, 0.2f, 0.12f), 0.6f);                          // the crank's knob

        // Handle: butt cap, rear cork grip (with a few darker filler rings), reel seat, fore grip, winding check (in their
        // own node, so a modelled rod can stand in for them: UseModel).
        var handle = _procHandle = new Node3D { Name = "ProcHandle" };
        AddChild(handle);
        Cyl(handle, 0.0155f, 0.016f, 0.03f, new Vector3(0, -0.315f, 0), rubber, 16);
        Cyl(handle, 0.0148f, 0.0155f, 0.25f, new Vector3(0, -0.175f, 0), cork, 16);
        Cyl(handle, 0.0145f, 0.0148f, 0.1f, new Vector3(0, -0.0f, 0), cork, 16);
        foreach (float y in new[] { -0.24f, -0.12f, -0.03f })
            Cyl(handle, 0.0152f, 0.0152f, 0.004f, new Vector3(0, y, 0), corkDark, 16);
        Cyl(handle, SeatRadius, SeatRadius, 0.12f, new Vector3(0, SeatCentre, 0), seat, 16);
        Cyl(handle, 0.0148f, 0.0142f, 0.016f, new Vector3(0, SeatCentre - 0.052f, 0), chrome, 16);   // rear hood
        Cyl(handle, 0.0142f, 0.0148f, 0.016f, new Vector3(0, SeatCentre + 0.05f, 0), chrome, 16);    // front hood
        Cyl(handle, 0.0158f, 0.0158f, 0.018f, new Vector3(0, SeatCentre + 0.071f, 0), seat, 16);     // locking nut
        Cyl(handle, 0.0115f, 0.0135f, 0.07f, new Vector3(0, SeatCentre + 0.115f, 0), cork, 16);      // fore grip
        Cyl(handle, 0.009f, 0.0105f, 0.008f, new Vector3(0, BlankStart - 0.004f, 0), chrome, 16);    // winding check

        BuildReel(reelBody, reelTrim, chrome, line, knob, seat);

        // Blank: ten short sections, each a child of the previous so the bend accumulates along the rod. Guides hang
        // under it (the reel's side), the first one large, then smaller to the tip-top, each with its whippings.
        Node3D parent = this;
        float yOffset = BlankStart;
        var guides = new Dictionary<int, (float Ring, float Height)>
        {
            [1] = (0.017f, 0.034f), [2] = (0.011f, 0.024f), [3] = (0.008f, 0.017f), [4] = (0.0065f, 0.013f),
            [5] = (0.0055f, 0.011f), [6] = (0.005f, 0.0095f), [7] = (0.0045f, 0.0085f), [8] = (0.004f, 0.0075f),
        };
        for (int i = 0; i < Joints; i++)
        {
            float t0 = (float)i / Joints, t1 = (float)(i + 1) / Joints;
            float r0 = Mathf.Lerp(0.0078f, 0.0017f, MathF.Pow(t0, 0.85f)), r1 = Mathf.Lerp(0.0078f, 0.0017f, MathF.Pow(t1, 0.85f));
            var seg = new Node3D { Position = new Vector3(0, yOffset, 0) };
            parent.AddChild(seg);
            // The section's own blank, guide and whippings (a modelled rod's piece takes their place: UseModel).
            var proc = new Node3D { Name = "Proc" };
            seg.AddChild(proc);
            _procSections.Add(proc);
            var blankMesh = Cyl(proc, r1, r0, SegmentLength, new Vector3(0, SegmentLength * 0.5f, 0), blank, 10);
            _blankMeshes.Add(blankMesh);
            _blankMaterial = blank;
            if (guides.TryGetValue(i, out var g))
            {
                Guide(proc, SegmentLength * 0.55f, Mathf.Lerp(r0, r1, 0.55f), g.Ring, g.Height, chrome, whip);
                // Where the line runs through this guide (the ring's middle; under proc, so it follows ApplyLength and
                // stays put for a modelled rod whose own guides hang in the same places).
                var mark = new Node3D { Name = $"LineGuide{i}", Position = new Vector3(0, SegmentLength * 0.55f, -Mathf.Lerp(r0, r1, 0.55f) - g.Height) };
                proc.AddChild(mark);
                _lineGuides.Add(mark);
            }
            if (i == 0)
            {
                // A decorative whipping above the fore grip and the hook keeper.
                Cyl(proc, r0 * 1.12f, r0 * 1.12f, 0.025f, new Vector3(0, 0.02f, 0), whip, 10);
                proc.AddChild(new MeshInstance3D
                {
                    Mesh = new TorusMesh { InnerRadius = 0.0022f, OuterRadius = 0.0034f, Rings = 8, RingSegments = 5 },
                    MaterialOverride = chrome, Position = new Vector3(0, 0.06f, -r0 - 0.004f), RotationDegrees = new Vector3(0, 0, 90f),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
            _segments.Add(seg);
            parent = seg;
            yOffset = SegmentLength;
        }
        // The tip-top guide.
        var top = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.0022f, OuterRadius = 0.0036f, Rings = 10, RingSegments = 5 },
            MaterialOverride = chrome, Position = new Vector3(0, SegmentLength + 0.004f, -0.003f), RotationDegrees = new Vector3(90f, 0, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _procSections[^1].AddChild(top);
        Tip = new Node3D { Position = new Vector3(0, SegmentLength, 0) };
        parent.AddChild(Tip);
        _built = true;
        SetLook(_lookRod, _lookReel);
    }

    private Node3D? _procHandle, _builtReel;
    private readonly List<Node3D> _lineGuides = new();
    private Node3D _builtLineExit = null!;
    private MeshInstance3D? _builtSpoolLine;
    private Vector3 _modelLineExit;
    private float _spoolFill = 1f;

    /// <summary>
    /// The line inside the rod, in world space: from the spool (where it leaves the lip towards the first guide)
    /// through every guide's ring to the tip-top; returns how many points it wrote (FishingRod.UpdateLine draws it).
    /// </summary>
    public int LinePath(Vector3[] into)
    {
        int n = 0;
        if (into.Length < _lineGuides.Count + 2)
            return 0;
        into[n++] = ModelledReel ? ToGlobal(_modelLineExit) : _builtLineExit.GlobalPosition;
        foreach (var g in _lineGuides)
            into[n++] = g.GlobalPosition;
        into[n++] = Tip.GlobalPosition;
        return n;
    }

    /// <summary>How full the spool is (0 empty .. 1 full): the wound line shrinks towards the spool's core as it is
    /// cast out and grows back as it is reeled in (FishingRod: 1 - line out / the reel's capacity).</summary>
    public void SetSpoolFill(float fill)
    {
        fill = Math.Clamp(fill, 0f, 1f);
        if (MathF.Abs(fill - _spoolFill) < 0.002f)
            return;
        _spoolFill = fill;
        float k = Mathf.Lerp(SpoolCore, 1f, fill);
        if (_builtSpoolLine != null)
            _builtSpoolLine.Scale = new Vector3(k, 1f, k);
        if (_modelSpoolLine != null)
            _modelSpoolLine.Scale = new Vector3(k, 1f, k);
    }

    /// <summary>The wound line's radius on an empty spool, as a share of the full one (the core under the line).</summary>
    private const float SpoolCore = 0.62f;
    private Node3D? _modelSpoolLine;
    private readonly List<Node3D> _procSections = new();
    private readonly List<MeshInstance3D> _modelSections = new();
    private bool _quiverOn;

    private bool _built;

    /// <summary>The default rod and reel models (PropAsset ids) for items that name none; Main warms them while the menu
    /// shows.</summary>
    public const string ModelRodId = "meshy_rod2_player", ModelReelId = "meshy_reel";

    /// <summary>A rod modelled by the user (RodData.Model, else ModelRodId: a .glb cut by tools/assets/meshy_rod_split.py
    /// into the handle and the ten bending sections) stands in for the built handle and blank, each section hung on
    /// its joint so it bends as the built one did. Not without a screen.</summary>
    public bool Modelled { get; private set; }

    private string? _rodModelId, _reelModelId;
    private MeshInstance3D? _modelHandle;

    /// <summary>The rod's length (m, butt to tip): the ten sections share what the handle leaves.</summary>
    public float RodLength { get; private set; } = DefaultLength;

    public const float DefaultLength = BlankStart + Joints * SegmentLength - ButtY;
    private const float ButtY = -0.33f;

    /// <summary>One section's length for this rod.</summary>
    public float SectionLength => (RodLength + ButtY - BlankStart) / Joints;

    /// <summary>Takes a replaced model's node out of the tree at once (a queued free left it there, holding its name,
    /// till the frame's end: the new one got renamed and IconStudio missed it) and frees it.</summary>
    private static void Drop(Node? node)
    {
        if (node == null)
            return;
        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }

    private void ShowRodModel(string id)
    {
        if (id == _rodModelId)
            return;
        _rodModelId = id;
        Drop(_modelHandle);
        _modelHandle = null;
        foreach (var piece in _modelSections)
            Drop(piece);
        _modelSections.Clear();
        Modelled = false;
        _procHandle!.Visible = true;
        foreach (var proc in _procSections)
            proc.Visible = true;
        if (id == "" || World.Props.PropAsset.Named(id) is not { } parts || !parts.TryGetValue("Handle", out var handleMesh))
            return;
        for (int i = 0; i < _segments.Count; i++)
            if (!parts.ContainsKey($"Seg{i}"))
                return;
        Modelled = true;
        _procHandle.Visible = false;
        AddChild(_modelHandle = new MeshInstance3D { Name = "ModelHandle", Mesh = handleMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        for (int i = 0; i < _segments.Count; i++)
        {
            _procSections[i].Visible = false;
            var piece = new MeshInstance3D { Name = "Model", Mesh = parts[$"Seg{i}"], CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _segments[i].AddChild(piece);
            _modelSections.Add(piece);
        }
        ApplyLength();
        ShowQuiver();
    }

    /// <summary>Stretches the ten sections to the rod's length (the handle keeps its size): each joint sits one section
    /// further, each section's visible parts are scaled along the blank.</summary>
    private void ApplyLength()
    {
        float len = SectionLength, k = len / SegmentLength;
        for (int i = 0; i < _segments.Count; i++)
        {
            if (i > 0)
                _segments[i].Position = new Vector3(0f, len, 0f);
            _procSections[i].Scale = new Vector3(1f, k, 1f);
            if (i < _modelSections.Count)
                _modelSections[i].Scale = new Vector3(1f, k, 1f);
        }
        Tip.Position = new Vector3(0f, len, 0f);
    }

    /// <summary>A reel modelled by the user (ReelData.Model, else ModelReelId; its frame in the .json beside it) in place
    /// of the built one: the body stands, the rotor with the bail turns about the spool's axis and the crank about its
    /// hub (SetCrank), the bail's wire swings over about its pivots (SetBail); the hands follow its knob (KnobAt).</summary>
    public bool ModelledReel { get; private set; }

    private void ShowReelModel(string id)
    {
        if (id == _reelModelId)
            return;
        _reelModelId = id;
        Drop(_modelReelBody);
        Drop(_modelRotor);
        Drop(_modelCrank);
        Drop(_modelSpoolLine);
        _modelReelBody = _modelRotor = _modelCrank = _modelBail = null;
        _modelSpoolLine = null;
        ModelledReel = false;
        _builtReel!.Visible = true;
        CrankAxis = BuiltCrankAxis;
        CrankArm = BuiltCrankArm;
        KnobOut = BuiltKnobOut;
        if (id == "" || FrameOf(id) is not { } f || World.Props.PropAsset.Named(id) is not { } parts
            || !parts.TryGetValue("Body", out var body) || !parts.TryGetValue("Rotor", out var rotor)
            || !parts.TryGetValue("Crank", out var crank))
            return;
        ModelledReel = true;
        _builtReel.Visible = false;
        var off = GeometryInstance3D.ShadowCastingSetting.Off;
        // The wound line is cut out of the body into its own shell that shrinks towards the spool's axis as line goes
        // out (SetSpoolFill); the line leaves the spool's lip towards the rod.
        var (rest, shell, shellRadius) = SpoolShellOf(id, body, f.Rotor);
        AddChild(_modelReelBody = new MeshInstance3D { Name = "ModelReel", Mesh = rest, CastShadow = off });
        _modelSpoolLine = null;
        if (shell != null)
        {
            _modelSpoolLine = new Node3D { Name = "ModelSpoolLine", Position = new Vector3(f.Rotor.X, 0f, f.Rotor.Z) };
            _modelSpoolLine.AddChild(new MeshInstance3D { Mesh = shell, Position = new Vector3(-f.Rotor.X, 0f, -f.Rotor.Z), CastShadow = off });
            AddChild(_modelSpoolLine);
            float k = Mathf.Lerp(SpoolCore, 1f, _spoolFill);
            _modelSpoolLine.Scale = new Vector3(k, 1f, k);
        }
        _modelLineExit = f.Rotor + new Vector3(0f, 0.045f, shellRadius > 0f ? shellRadius : 0.028f);
        _modelRotor = new Node3D { Name = "ModelRotor", Position = f.Rotor };
        _modelRotor.AddChild(new MeshInstance3D { Mesh = rotor, CastShadow = off });
        AddChild(_modelRotor);
        if (parts.TryGetValue("Bail", out var bail))          // a reel whose wire could not be cut keeps it on the rotor
        {
            _modelBail = new Node3D { Name = "ModelBail", Position = f.BailPivot };
            _modelBail.AddChild(new MeshInstance3D { Mesh = bail, CastShadow = off });
            _modelRotor.AddChild(_modelBail);
        }
        _modelCrank = new Node3D { Name = "ModelCrank", Position = f.Hub };
        _modelCrank.AddChild(new MeshInstance3D { Mesh = crank, CastShadow = off });
        AddChild(_modelCrank);
        _bailAxis = f.BailAxis;
        _modelBodyCentre = f.Body;
        CrankAxis = f.Hub;
        CrankArm = f.Arm;
        KnobOut = f.KnobOut;
        SetCrank(_crankAngle);
        SetBail(_bailOpen);
    }

    private float _bailOpen;

    private float _crankAngle;

    /// <summary>A quiver tip (bottom rods) is the built blank's bright last two sections, also on a modelled rod.</summary>
    private void ShowQuiver()
    {
        if (!Modelled)
            return;
        for (int i = _segments.Count - 2; i < _segments.Count; i++)
        {
            _procSections[i].Visible = _quiverOn;
            _modelSections[i].Visible = !_quiverOn;
        }
    }

    private Node3D? _indicator, _bobbin;
    private StandardMaterial3D? _indicatorGlow;

    /// <summary>
    /// The bite indicator for bottom fishing: a bright bobbin hanging on a cord from the line between the reel and
    /// the first guide. <paramref name="lift"/> 0 hangs, 1 pulled right up to the blank (a fish running with the bait).
    /// </summary>
    public void SetIndicator(bool shown, float lift)
    {
        if (_indicator == null)
        {
            if (!shown)
                return;
            _indicator = new Node3D { Position = new Vector3(0f, 0.42f, -0.03f) };
            AddChild(_indicator);
            _bobbin = new Node3D();
            _indicator.AddChild(_bobbin);
            var cord = Mat(new Color(0.15f, 0.15f, 0.15f), 0.6f);
            var glow = _indicatorGlow = BuildingFactory.Pixel(new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.55f, 0.08f), Roughness = 0.4f,
                EmissionEnabled = true, Emission = new Color(1f, 0.5f, 0.05f), EmissionEnergyMultiplier = 0.5f,
            });
            Cyl(_bobbin, 0.0007f, 0.0007f, 0.12f, new Vector3(0f, 0f, 0.06f), cord, 4).RotationDegrees = new Vector3(90f, 0f, 0f);
            Cyl(_bobbin, 0.008f, 0.008f, 0.03f, Vector3.Zero, glow, 10).RotationDegrees = new Vector3(90f, 0f, 0f);
        }
        _indicator.Visible = shown;
        if (_bobbin != null)
            _bobbin.Position = new Vector3(0f, 0f, -0.13f * (1f - Math.Clamp(lift, 0f, 1f)));
    }

    private readonly List<MeshInstance3D> _blankMeshes = new();
    private Material? _blankMaterial;
    private Data.RodData? _lookRod;
    private Data.ReelData? _lookReel;
    private ShaderMaterial? _cork, _blank, _whip, _reelBody, _reelTrim;

    private static Shader? _tackleShader;

    /// <summary>A material of shaders/tackle.gdshader: kind (0 blank, 1 cork, 2 foam, 3 metal, 4 housing, 5 spool line,
    /// 6 whipping, 7 rubber), colour, roughness, metal.</summary>
    private static ShaderMaterial Tackle(int kind, Color colour, float roughness, float metallic = 0f, float shade = 0f)
    {
        _tackleShader ??= GD.Load<Shader>("res://shaders/tackle.gdshader");
        var m = new ShaderMaterial { Shader = _tackleShader };
        m.SetShaderParameter("kind", kind);
        m.SetShaderParameter("base_color", colour);
        m.SetShaderParameter("roughness_base", roughness);
        m.SetShaderParameter("metallic_base", metallic);
        m.SetShaderParameter("shade", shade);
        return m;
    }

    /// <summary>Dresses the rig in the maker's colours of a rod and reel (blank lacquer, whippings, cork or foam
    /// grips, reel housing and trim).</summary>
    public void SetLook(Data.RodData? rod, Data.ReelData? reel)
    {
        _lookRod = rod ?? _lookRod;
        _lookReel = reel ?? _lookReel;
        if (_blank == null || !_built)
            return;
        RodLength = _lookRod is { Length: > 1f } r ? Math.Clamp(r.Length, 1.6f, 4.6f) : DefaultLength;
        if (UseModels)
        {
            ShowRodModel(_lookRod is { Model: { Length: > 0 } m } ? m : ModelRodId);
            ShowReelModel(_lookReel is { Model: { Length: > 0 } n } ? n : ModelReelId);
        }
        ApplyLength();
        if (rod != null)
        {
            _blank.SetShaderParameter("base_color", rod.BlankColor);
            _whip!.SetShaderParameter("base_color", rod.WhipColor);
            _cork!.SetShaderParameter("base_color", rod.FoamGrip ? new Color(0.09f, 0.09f, 0.1f) : new Color(0.72f, 0.56f, 0.38f));
            _cork.SetShaderParameter("kind", rod.FoamGrip ? 2 : 1);
        }
        if (reel != null)
        {
            _reelBody!.SetShaderParameter("base_color", reel.BodyColor);
            _reelTrim!.SetShaderParameter("base_color", reel.TrimColor);
        }
    }
    private Material? _quiverMaterial;

    /// <summary>After dark the quiver tip and the bite indicator fade, unless a glow tip (świetlik) is fitted.</summary>
    public void SetNight(float darkness, bool glowTip)
    {
        float energy = glowTip && darkness > 0.3f ? 1.2f + 2f * darkness : 0.35f * (1f - 0.95f * darkness);
        if (_quiverMaterial is StandardMaterial3D q && MathF.Abs(q.EmissionEnergyMultiplier - energy) > 0.02f)
        {
            var c = glowTip && darkness > 0.3f ? new Color(0.35f, 1f, 0.4f) : new Color(1f, 0.4f, 0.1f);
            q.Emission = c;
            q.EmissionEnergyMultiplier = energy;
        }
        if (_indicatorGlow != null && MathF.Abs(_indicatorGlow.EmissionEnergyMultiplier - energy) > 0.02f)
            _indicatorGlow.EmissionEnergyMultiplier = energy * 1.4f;
    }

    /// <summary>
    /// A bottom rod's quiver tip: the last two sections of the blank painted bright (white and signal orange, a little
    /// self-lit) so the smallest pluck on the bait shows against the water; other rods keep the lacquered blank.
    /// </summary>
    public void SetQuiverTip(bool on)
    {
        if (_blankMeshes.Count < 2 || _blankMaterial == null)
            return;
        _quiverMaterial ??= BuildingFactory.Pixel(new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.45f, 0.12f), Roughness = 0.35f,
            EmissionEnabled = true, Emission = new Color(1f, 0.4f, 0.1f), EmissionEnergyMultiplier = 0.35f,
        });
        for (int i = _blankMeshes.Count - 2; i < _blankMeshes.Count; i++)
            _blankMeshes[i].MaterialOverride = on ? _quiverMaterial : _blankMaterial;
        _quiverOn = on;
        ShowQuiver();
    }

    /// <summary>A guide under the blank: a ring standing on a two-legged frame, whipped to the blank.</summary>
    private static void Guide(Node3D seg, float y, float blankR, float ringR, float height, Material metal, Material whip)
    {
        float ringZ = -blankR - height;
        foreach (float side in new[] { -1f, 1f })
        {
            // A leg from the blank (a little to the side, along the rod) to the ring's rim.
            var from = new Vector3(0f, y + side * height * 0.55f, -blankR * 0.9f);
            var to = new Vector3(0f, y + side * 0.0015f, ringZ + ringR);
            Strut(seg, from, to, 0.0011f + ringR * 0.04f, metal);
            Cyl(seg, blankR * 1.15f, blankR * 1.15f, 0.007f, new Vector3(0, from.Y, 0), whip, 8);
        }
        seg.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = ringR * 0.78f, OuterRadius = ringR, Rings = 16, RingSegments = 6 },
            MaterialOverride = metal, Position = new Vector3(0, y, ringZ), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>The fixed-spool reel under the seat.</summary>
    private void BuildReel(Material body, Material trim, Material metal, Material line, Material knob, Material dark)
    {
        var reel = _builtReel = new Node3D { Name = "Reel" };
        AddChild(reel);
        // Foot clamped in the seat and the stem down to the body, leaning a little forward.
        Box(reel, new Vector3(0.011f, 0.056f, 0.0035f), new Vector3(0, SeatCentre, -SeatRadius - 0.0015f), body, 0.0015f);
        // The stem is thin along the rod: it passes between the middle and ring fingers of the hand on the seat.
        var stem = Box(reel, new Vector3(0.013f, 0.0065f, 0.06f), new Vector3(0, StemAt.Y, -0.045f), body, 0.0025f);
        stem.RotationDegrees = new Vector3(-8f, 0f, 0f);
        // Gearbox body: a rounded housing, a trim ring where the rotor turns, a cap at the back.
        reel.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 20, Rings = 12 }, MaterialOverride = body,
            Position = ReelBody, Scale = new Vector3(0.042f, 0.06f, 0.05f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        Cyl(reel, 0.0215f, 0.0215f, 0.006f, ReelBody + new Vector3(0, 0.026f, 0), trim, 20);
        // Rotor (turns as the line is wound) with its two arms; the spool with line, its lip, the drag knob.
        _rotor = new Node3D { Position = ReelBody + new Vector3(0, 0.041f, 0) };
        reel.AddChild(_rotor);
        Cyl(_rotor, 0.019f, 0.023f, 0.026f, Vector3.Zero, body, 20);
        foreach (float side in new[] { -1f, 1f })
            Box(_rotor, new Vector3(0.006f, 0.03f, 0.008f), new Vector3(side * 0.024f, 0.01f, 0f), body, 0.002f);
        // The bail: a wire hoop from one arm round the front underside of the spool to the other, the line roller on
        // the right, small bail arms.
        float bailY = 0.03f, bailR = 0.029f;
        var hoop = new List<Vector3>();
        for (int i = 0; i <= 18; i++)
        {
            float a = i / 18f * Mathf.Pi;
            hoop.Add(new Vector3(MathF.Cos(a) * bailR, bailY + MathF.Sin(a) * 0.006f, -MathF.Sin(a) * bailR));
        }
        // The bail turns on pivots at the rotor's arms: closed it guides the line onto the roller; opened for the cast
        // it swings over to the other side of the spool (SetBail).
        _bail = new Node3D { Position = new Vector3(0f, 0.022f, 0f) };
        _rotor.AddChild(_bail);
        var pivot = new Vector3(0f, 0.022f, 0f);
        for (int i = 0; i < hoop.Count; i++)
            hoop[i] -= pivot;
        _bail.AddChild(new MeshInstance3D { Mesh = Shapes.Tube(hoop, 0.0012f, 6), MaterialOverride = metal, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        foreach (float side in new[] { -1f, 1f })
        {
            Strut(_bail, new Vector3(side * 0.025f, 0f, 0f), new Vector3(side * bailR, bailY - 0.022f, 0f), 0.002f, metal);
            Cyl(_rotor, 0.0032f, 0.0032f, 0.003f, new Vector3(side * 0.0255f, 0.022f, 0f), trim, 12).RotationDegrees = new Vector3(0, 0, 90f);   // pivot caps
        }
        Cyl(_bail, 0.004f, 0.004f, 0.007f, new Vector3(bailR, bailY - 0.022f, 0f), trim, 12).RotationDegrees = new Vector3(0, 0, 90f);       // line roller
        var spool = ReelBody + new Vector3(0, 0.066f, 0);
        Cyl(reel, 0.024f, 0.024f, 0.006f, spool + new Vector3(0, -0.012f, 0), trim, 24);     // skirt
        _builtSpoolLine = Cyl(reel, 0.0205f, 0.0205f, 0.018f, spool, line, 24);              // the line
        _builtLineExit = new Node3D { Name = "LineExit", Position = spool + new Vector3(0, 0.009f, 0.021f) };
        reel.AddChild(_builtLineExit);
        Cyl(reel, 0.0228f, 0.0228f, 0.004f, spool + new Vector3(0, 0.011f, 0), trim, 24);    // front lip
        Cyl(reel, 0.0085f, 0.0095f, 0.014f, spool + new Vector3(0, 0.02f, 0), dark, 16);     // drag knob
        for (int i = 0; i < 10; i++)
        {
            // Grip ribs round the drag knob.
            float a = i / 10f * Mathf.Tau;
            Box(reel, new Vector3(0.0018f, 0.012f, 0.0018f), spool + new Vector3(MathF.Cos(a) * 0.0093f, 0.02f, MathF.Sin(a) * 0.0093f), dark, 0.0006f);
        }
        // The line clip on the spool's skirt, the side cover with its screws, the anti-reverse switch at the back.
        Box(reel, new Vector3(0.003f, 0.007f, 0.006f), spool + new Vector3(0.0245f, -0.012f, 0f), trim, 0.001f);
        Cyl(reel, 0.017f, 0.017f, 0.0025f, ReelBody + new Vector3(0.0205f, -0.004f, 0f), trim, 24).RotationDegrees = new Vector3(0, 0, 90f);
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * Mathf.Tau + 0.5f;
            Cyl(reel, 0.0016f, 0.0016f, 0.001f, ReelBody + new Vector3(0.022f, -0.004f + MathF.Sin(a) * 0.012f, MathF.Cos(a) * 0.012f), dark, 8)
                .RotationDegrees = new Vector3(0, 0, 90f);
        }
        Box(reel, new Vector3(0.006f, 0.004f, 0.009f), ReelBody + new Vector3(0f, -0.031f, 0.012f), dark, 0.0015f);
        // The crank on the left: a cap over the axle, the arm, the knob on its end (pointing outwards).
        _crank = new Node3D { Position = BuiltCrankAxis };
        reel.AddChild(_crank);
        Cyl(_crank, 0.0075f, 0.0075f, 0.008f, Vector3.Zero, trim, 14).RotationDegrees = new Vector3(0, 0, 90f);
        var arm = Box(_crank, new Vector3(0.005f, 0.009f, BuiltCrankArm + 0.008f), new Vector3(-0.008f, 0f, -BuiltCrankArm * 0.5f), body, 0.0022f);
        arm.RotationDegrees = Vector3.Zero;
        // The knob's shaft runs out to the left, clear of the reel's side.
        float shaft = -BuiltKnobOut - 0.018f;
        Cyl(_crank, 0.0028f, 0.0028f, shaft, new Vector3(-0.006f - shaft * 0.5f, 0f, -BuiltCrankArm), metal, 8).RotationDegrees = new Vector3(0, 0, 90f);
        var knobMesh = Cyl(_crank, 0.0085f, 0.0075f, 0.024f, new Vector3(BuiltKnobOut, 0f, -BuiltCrankArm), knob, 14);
        knobMesh.RotationDegrees = new Vector3(0, 0, 90f);
        SetCrank(0f);
    }

    /// <summary>Turns the reel's crank (and the rotor with it, five times as fast) to <paramref name="angle"/>.</summary>
    public void SetCrank(float angle)
    {
        if (_crank == null)
            return;
        _crank.Rotation = new Vector3(angle, 0f, 0f);
        _rotor.Rotation = new Vector3(0f, angle * 5.2f, 0f);
        _crankAngle = angle;
        if (_modelCrank != null)
        {
            _modelCrank.Rotation = new Vector3(angle, 0f, 0f);
            _modelRotor!.Rotation = new Vector3(0f, angle * 5.2f, 0f);
        }
    }

    /// <summary>Opens (1) or closes (0) the bail: open for the cast, flipped closed when the reel turns again.</summary>
    public void SetBail(float open)
    {
        open = Math.Clamp(MathF.Max(open, BailShot), 0f, 1f);
        if (_bail != null)
            _bail.Rotation = new Vector3(open * 2.7f, 0f, 0f);
        _bailOpen = open;
        if (_modelBail != null)
            _modelBail.Basis = new Basis(_bailAxis, open * 2.7f);
    }

    /// <summary>Number of bending sections of the blank (matches <see cref="Core.Physics.RodBendModel"/>).</summary>
    public const int Joints = Core.Physics.RodBendModel.DefaultJoints;

    /// <summary>Length of one section; the blank is 3 m long above the handle.</summary>
    public const float SegmentLength = 0.3f;

    /// <summary>Where the blank leaves the fore grip.</summary>
    public const float BlankStart = 0.25f;

    /// <summary>Applies the bend of every joint, rotating about <paramref name="axis"/> (rig space, perpendicular to the rod).</summary>
    public void SetJointAngles(ReadOnlySpan<float> angles, Vector3 axis)
    {
        if (axis.LengthSquared() < 1e-6f)
            axis = Vector3.Right;
        axis = axis.Normalized();
        for (int i = 0; i < _segments.Count && i < angles.Length; i++)
            _segments[i].Basis = new Basis(axis, angles[i]);
    }

    /// <summary>Simple droop for tools: 0..1 bends every joint a little downwards.</summary>
    public void SetBend(float amount)
    {
        for (int i = 0; i < _segments.Count; i++)
            _segments[i].Rotation = new Vector3(-amount * (0.02f + 0.03f * i / _segments.Count), 0f, 0f);
    }

    private static StandardMaterial3D Mat(Color color, float roughness, float metallic = 0f) =>
        BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic }, metallic > 0.5f);

    private static MeshInstance3D Cyl(Node3D parent, float topRadius, float bottomRadius, float height, Vector3 pos, Material mat, int sides = 10)
    {
        var mi = new MeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,   // first-person gear casts no shadow
            Mesh = new CylinderMesh { TopRadius = topRadius, BottomRadius = bottomRadius, Height = height, RadialSegments = sides, Rings = 1 },
            MaterialOverride = mat,
            Position = pos,
        };
        parent.AddChild(mi);
        return mi;
    }

    private static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Material mat, float round = 0f)
    {
        var mi = new MeshInstance3D
        {
            Mesh = round > 0f ? Shapes.RoundedBox(size, round) : new BoxMesh { Size = size }, MaterialOverride = mat, Position = pos,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>A thin rod from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static void Strut(Node3D parent, Vector3 a, Vector3 b, float r, Material mat)
    {
        var dir = b - a;
        float len = dir.Length();
        if (len < 1e-5f)
            return;
        var y = dir / len;
        var x = MathF.Abs(y.X) > 0.9f ? Vector3.Forward.Cross(y).Normalized() : Vector3.Right.Cross(y).Normalized();
        var mi = Cyl(parent, r, r, len, (a + b) * 0.5f, mat, 6);
        mi.Basis = new Basis(x, y, x.Cross(y));
    }
}
