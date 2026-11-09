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

        // Blank: three sections, each a child of the previous so the bend accumulates.
        Node3D parent = this;
        float yOffset = 0.2f;
        float[][] radii = { new[] { 0.017f, 0.012f }, new[] { 0.012f, 0.0085f }, new[] { 0.0085f, 0.004f } };
        for (int i = 0; i < 3; i++)
        {
            var seg = new Node3D { Position = new Vector3(0, yOffset, 0) };
            parent.AddChild(seg);
            Cyl(seg, radii[i][1], radii[i][0], 1f, new Vector3(0, 0.5f, 0), graphite);
            Cyl(seg, radii[i][0] * 1.25f, radii[i][0] * 1.25f, 0.035f, new Vector3(0, 0.03f, 0), red);   // thread wrap
            foreach (float gy in new[] { 0.45f, 0.85f })
            {
                float r = Mathf.Lerp(radii[i][0], radii[i][1], gy);
                Box(seg, new Vector3(0.004f, 0.004f, 0.02f), new Vector3(0, gy, -r - 0.008f), metal);
                seg.AddChild(new MeshInstance3D
                {
                    Mesh = new TorusMesh { InnerRadius = 0.0065f, OuterRadius = 0.0105f, Rings = 10, RingSegments = 6 },
                    MaterialOverride = metal,
                    Position = new Vector3(0, gy, -r - 0.02f),
                });
            }
            _segments.Add(seg);
            parent = seg;
            yOffset = 1f;
        }
        Tip = new Node3D { Position = new Vector3(0, 1f, 0) };
        parent.AddChild(Tip);
    }

    /// <summary>0..1: how far the blank flexes (the tip droops towards the water).</summary>
    public void SetBend(float amount)
    {
        for (int i = 0; i < _segments.Count; i++)
            _segments[i].Rotation = new Vector3(-amount * (0.07f + 0.07f * i), 0f, 0f);
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
