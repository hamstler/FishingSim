using System;
using System.Collections.Generic;
using Godot;

namespace FishingSim.Fishing;

/// <summary>
/// First-person rod: cork handle, reel, three tapered blank sections with guides, tip. Built to be a child of the
/// camera; local +Y runs along the rod. <see cref="SetBend"/> flexes the blank.
/// </summary>
public partial class RodRig : Node3D
{
    private readonly List<Node3D> _segments = new();

    /// <summary>The end of the blank; the line starts here.</summary>
    public Node3D Tip { get; private set; } = null!;

    public override void _Ready()
    {
        Position = new Vector3(0.3f, -0.35f, -0.4f);
        RotationDegrees = new Vector3(-80f, 0f, 0f);

        var cork = Mat(new Color(0.36f, 0.22f, 0.13f), 0.9f);      // leather grip
        var black = Mat(new Color(0.22f, 0.15f, 0.09f), 0.8f);       // dark wood fittings
        var metal = Mat(new Color(0.55f, 0.42f, 0.22f), 0.4f, 0.6f);   // brass
        var graphite = Mat(new Color(0.5f, 0.36f, 0.2f), 0.8f);       // hazel wood
        var red = Mat(new Color(0.75f, 0.68f, 0.5f), 0.9f);          // twine whipping

        // Handle, butt cap and reel seat.
        Cyl(this, 0.021f, 0.021f, 0.36f, new Vector3(0, -0.13f, 0), cork);
        Cyl(this, 0.024f, 0.024f, 0.03f, new Vector3(0, -0.32f, 0), black);
        Cyl(this, 0.024f, 0.024f, 0.05f, new Vector3(0, 0.08f, 0), black);

        // Reel hanging under the rod (local -Z is "down" here).
        var reel = new Node3D { Position = new Vector3(0, 0.1f, -0.065f) };
        AddChild(reel);
        Box(this, new Vector3(0.02f, 0.07f, 0.04f), new Vector3(0, 0.1f, -0.03f), black);
        Cyl(reel, 0.033f, 0.033f, 0.07f, Vector3.Zero, graphite).RotationDegrees = new Vector3(0, 0, 90f);
        var spool = Cyl(reel, 0.038f, 0.038f, 0.022f, Vector3.Zero, metal);
        spool.RotationDegrees = new Vector3(0, 0, 90f);
        spool.Position = new Vector3(-0.03f, 0f, 0f);
        Box(reel, new Vector3(0.07f, 0.008f, 0.008f), new Vector3(0.06f, 0f, -0.01f), metal);
        reel.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.014f, Height = 0.028f },
            MaterialOverride = red,
            Position = new Vector3(0.1f, 0f, -0.025f),
        });

        // Blank: ten short sections, each a child of the previous so the bend accumulates along the rod.
        Node3D parent = this;
        float yOffset = 0.2f;
        for (int i = 0; i < Joints; i++)
        {
            float t0 = (float)i / Joints, t1 = (float)(i + 1) / Joints;
            float r0 = Mathf.Lerp(0.017f, 0.004f, t0), r1 = Mathf.Lerp(0.017f, 0.004f, t1);
            var seg = new Node3D { Position = new Vector3(0, yOffset, 0) };
            parent.AddChild(seg);
            Cyl(seg, r1, r0, SegmentLength, new Vector3(0, SegmentLength * 0.5f, 0), graphite);
            if (i % 3 == 0)
                Cyl(seg, r0 * 1.25f, r0 * 1.25f, 0.03f, new Vector3(0, 0.02f, 0), red);     // thread wrap
            if (i % 2 == 1)
            {
                // Guide ring under the blank.
                Box(seg, new Vector3(0.004f, 0.004f, 0.02f), new Vector3(0, SegmentLength * 0.6f, -r1 - 0.008f), metal);
                seg.AddChild(new MeshInstance3D
                {
                    Mesh = new TorusMesh { InnerRadius = 0.0055f, OuterRadius = 0.0095f, Rings = 10, RingSegments = 6 },
                    MaterialOverride = metal,
                    Position = new Vector3(0, SegmentLength * 0.6f, -r1 - 0.019f),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
            _segments.Add(seg);
            parent = seg;
            yOffset = SegmentLength;
        }
        Tip = new Node3D { Position = new Vector3(0, SegmentLength, 0) };
        parent.AddChild(Tip);
    }

    /// <summary>Number of bending sections of the blank (matches <see cref="Core.Physics.RodBendModel"/>).</summary>
    public const int Joints = Core.Physics.RodBendModel.DefaultJoints;

    /// <summary>Length of one section; the blank is 3 m long above the handle.</summary>
    public const float SegmentLength = 0.3f;

    public const float BlankStart = 0.2f;

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
        World.Props.BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic });

    private static MeshInstance3D Cyl(Node3D parent, float topRadius, float bottomRadius, float height, Vector3 pos, Material mat)
    {
        var mi = new MeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,   // first-person gear casts no shadow
            Mesh = new CylinderMesh { TopRadius = topRadius, BottomRadius = bottomRadius, Height = height, RadialSegments = 10, Rings = 1 },
            MaterialOverride = mat,
            Position = pos,
        };
        parent.AddChild(mi);
        return mi;
    }

    private static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Material mat)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat, Position = pos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(mi);
        return mi;
    }
}
