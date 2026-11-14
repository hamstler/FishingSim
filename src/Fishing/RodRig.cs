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
    private Node3D _crank = null!, _rotor = null!;

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

    /// <summary>What the hand on the crank must not enter (rig space): the knob, the crank arm, the reel's body.</summary>
    public static IEnumerable<(Vector3 A, Vector3 B, float Radius)> CrankObstacles(float angle)
    {
        var knob = KnobAt(angle);
        yield return (knob + new Vector3(0.012f, 0f, 0f), knob - new Vector3(0.012f, 0f, 0f), 0.0085f);
        yield return (CrankAxis + new Vector3(-0.008f, 0f, 0f), knob + new Vector3(0.018f, 0f, 0f), 0.0045f);
        yield return (ReelBody + new Vector3(0, -0.02f, 0), ReelBody + new Vector3(0, 0.07f, 0), 0.024f);
    }

    private const float SeatCentre = 0.1f;

    /// <summary>The reel's body centre below the seat (rig space) and the crank's axis point on its left side.</summary>
    private static readonly Vector3 ReelBody = new(0f, SeatCentre - 0.004f, -0.088f);

    private static readonly Vector3 CrankAxis = ReelBody + new Vector3(-0.026f, 0f, 0f);

    /// <summary>Length of the crank arm (axis to knob).</summary>
    public const float CrankArm = 0.046f;

    /// <summary>The crank knob's centre at a crank angle (rig space): it turns in the plane across the left side.</summary>
    public static Vector3 KnobAt(float angle) =>
        CrankAxis + new Vector3(-0.026f, MathF.Sin(angle) * CrankArm, -MathF.Cos(angle) * CrankArm);

    public override void _Ready()
    {
        Position = new Vector3(0.3f, -0.35f, -0.4f);
        RotationDegrees = new Vector3(-80f, 0f, 0f);

        var cork = Mat(new Color(0.72f, 0.56f, 0.38f), 0.85f);        // cork grips
        var corkDark = Mat(new Color(0.55f, 0.4f, 0.26f), 0.9f);      // filler rings in the cork
        var rubber = Mat(new Color(0.12f, 0.11f, 0.1f), 0.7f);         // butt cap
        var seat = Mat(new Color(0.16f, 0.16f, 0.17f), 0.45f, 0.3f);   // graphite reel seat
        var chrome = Mat(new Color(0.72f, 0.72f, 0.74f), 0.25f, 0.9f); // hoods, guides, bail
        var blank = Mat(new Color(0.24f, 0.12f, 0.09f), 0.35f, 0.15f); // lacquered blank
        var whip = Mat(new Color(0.75f, 0.62f, 0.3f), 0.5f, 0.2f);     // gold thread whippings
        var reelBody = Mat(new Color(0.2f, 0.21f, 0.23f), 0.4f, 0.5f); // reel housing
        var reelTrim = Mat(new Color(0.62f, 0.52f, 0.3f), 0.3f, 0.8f); // champagne trim
        var line = Mat(new Color(0.82f, 0.86f, 0.78f), 0.5f);          // line on the spool
        var knob = Mat(new Color(0.32f, 0.2f, 0.12f), 0.55f);          // wooden knob

        // Handle: butt cap, rear cork grip (with a few darker filler rings), reel seat, fore grip, winding check.
        Cyl(this, 0.0155f, 0.016f, 0.03f, new Vector3(0, -0.315f, 0), rubber, 16);
        Cyl(this, 0.0148f, 0.0155f, 0.25f, new Vector3(0, -0.175f, 0), cork, 16);
        Cyl(this, 0.0145f, 0.0148f, 0.1f, new Vector3(0, -0.0f, 0), cork, 16);
        foreach (float y in new[] { -0.24f, -0.12f, -0.03f })
            Cyl(this, 0.0152f, 0.0152f, 0.004f, new Vector3(0, y, 0), corkDark, 16);
        Cyl(this, SeatRadius, SeatRadius, 0.12f, new Vector3(0, SeatCentre, 0), seat, 16);
        Cyl(this, 0.0148f, 0.0142f, 0.016f, new Vector3(0, SeatCentre - 0.052f, 0), chrome, 16);   // rear hood
        Cyl(this, 0.0142f, 0.0148f, 0.016f, new Vector3(0, SeatCentre + 0.05f, 0), chrome, 16);    // front hood
        Cyl(this, 0.0158f, 0.0158f, 0.018f, new Vector3(0, SeatCentre + 0.071f, 0), seat, 16);     // locking nut
        Cyl(this, 0.0115f, 0.0135f, 0.07f, new Vector3(0, SeatCentre + 0.115f, 0), cork, 16);      // fore grip
        Cyl(this, 0.009f, 0.0105f, 0.008f, new Vector3(0, BlankStart - 0.004f, 0), chrome, 16);    // winding check

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
            var blankMesh = Cyl(seg, r1, r0, SegmentLength, new Vector3(0, SegmentLength * 0.5f, 0), blank, 10);
            _blankMeshes.Add(blankMesh);
            _blankMaterial = blank;
            if (guides.TryGetValue(i, out var g))
                Guide(seg, SegmentLength * 0.55f, Mathf.Lerp(r0, r1, 0.55f), g.Ring, g.Height, chrome, whip);
            if (i == 0)
            {
                // A decorative whipping above the fore grip and the hook keeper.
                Cyl(seg, r0 * 1.12f, r0 * 1.12f, 0.025f, new Vector3(0, 0.02f, 0), whip, 10);
                seg.AddChild(new MeshInstance3D
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
        parent.AddChild(top);
        Tip = new Node3D { Position = new Vector3(0, SegmentLength, 0) };
        parent.AddChild(Tip);
    }

    private Node3D? _indicator, _bobbin;

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
            var glow = BuildingFactory.Pixel(new StandardMaterial3D
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
    private Material? _quiverMaterial;

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
        var reel = new Node3D { Name = "Reel" };
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
        _rotor.AddChild(new MeshInstance3D { Mesh = Shapes.Tube(hoop, 0.0012f, 6), MaterialOverride = metal, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        foreach (float side in new[] { -1f, 1f })
            Strut(_rotor, new Vector3(side * 0.025f, 0.022f, 0f), new Vector3(side * bailR, bailY, 0f), 0.002f, metal);
        Cyl(_rotor, 0.004f, 0.004f, 0.007f, new Vector3(bailR, bailY, 0f), trim, 10).RotationDegrees = new Vector3(0, 0, 90f);
        var spool = ReelBody + new Vector3(0, 0.066f, 0);
        Cyl(reel, 0.024f, 0.024f, 0.006f, spool + new Vector3(0, -0.012f, 0), trim, 24);     // skirt
        Cyl(reel, 0.0205f, 0.0205f, 0.018f, spool, line, 24);                                // the line
        Cyl(reel, 0.0228f, 0.0228f, 0.004f, spool + new Vector3(0, 0.011f, 0), trim, 24);    // front lip
        Cyl(reel, 0.0085f, 0.0095f, 0.014f, spool + new Vector3(0, 0.02f, 0), dark, 12);     // drag knob
        // The crank on the left: a cap over the axle, the arm, the knob on its end (pointing outwards).
        _crank = new Node3D { Position = CrankAxis };
        reel.AddChild(_crank);
        Cyl(_crank, 0.0075f, 0.0075f, 0.008f, Vector3.Zero, trim, 14).RotationDegrees = new Vector3(0, 0, 90f);
        var arm = Box(_crank, new Vector3(0.005f, 0.009f, CrankArm + 0.008f), new Vector3(-0.008f, 0f, -CrankArm * 0.5f), body, 0.0022f);
        arm.RotationDegrees = Vector3.Zero;
        Cyl(_crank, 0.0028f, 0.0028f, 0.016f, new Vector3(-0.014f, 0f, -CrankArm), metal, 8).RotationDegrees = new Vector3(0, 0, 90f);
        var knobMesh = Cyl(_crank, 0.0085f, 0.0075f, 0.024f, new Vector3(-0.026f, 0f, -CrankArm), knob, 14);
        knobMesh.RotationDegrees = new Vector3(0, 0, 90f);
        SetCrank(0f);
    }

    /// <summary>Turns the reel's crank (and the rotor with it, five times as fast) to <paramref name="angle"/>.</summary>
    public void SetCrank(float angle)
    {
        if (_crank == null)
            return;
        _crank.Rotation = new Vector3(angle, 0f, 0f);
        _rotor.Rotation = new Vector3(0f, -angle * 5.2f, 0f);
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
