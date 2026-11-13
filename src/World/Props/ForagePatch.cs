using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Core.Camp;

namespace FishingSim.World.Props;

/// <summary>
/// Something to pick on the forest floor (E): a porcini or two under a tree, a cluster of chanterelles in the moss or a
/// low blueberry bush. Picked, the mushrooms are gone (the berries leave the bare bush) until a game day has passed
/// (<see cref="Foraging.RegrowHours"/>).
/// </summary>
public partial class ForagePatch : Node3D
{
    public ForageKind Kind { get; private set; }

    /// <summary>The game hour (Profile.Clock) it was picked at; NaN when never.</summary>
    public float PickedAt { get; private set; } = float.NaN;

    /// <summary>True when there is something to pick.</summary>
    public bool Ripe { get; private set; } = true;

    private Node3D _crop = null!;
    private float _shrink = -1f;

    public string Title => Kind switch
    {
        ForageKind.Porcini => "Borowik",
        ForageKind.Chanterelle => "Kurki",
        _ => "Jagody",
    };

    public static ForagePatch Create(ForageKind kind, int seed)
    {
        var patch = new ForagePatch { Kind = kind, Name = kind.ToString() };
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919 + 13) };
        patch._crop = new Node3D { Name = "Crop" };
        patch.AddChild(patch._crop);
        switch (kind)
        {
            case ForageKind.Porcini:
                int n = 1 + rng.RandiRange(0, 1);
                for (int i = 0; i < n; i++)
                    patch._crop.AddChild(Porcini(rng.RandfRange(1.3f, 1.7f) * (i == 0 ? 1f : 0.7f),
                        new Vector3(i * rng.RandfRange(0.14f, 0.2f), 0f, i * rng.RandfRange(-0.1f, 0.1f)), rng));
                break;
            case ForageKind.Chanterelle:
                int c = rng.RandiRange(5, 8);
                for (int i = 0; i < c; i++)
                {
                    float a = rng.Randf() * Mathf.Tau, d = rng.RandfRange(0f, 0.14f);
                    patch._crop.AddChild(Chanterelle(rng.RandfRange(1f, 1.5f), new Vector3(MathF.Cos(a) * d, 0f, MathF.Sin(a) * d), rng));
                }
                break;
            default:
                // The bush stays when picked; only the berries go. The foliage shader takes the tree's colour from the
                // instance colour, so the lone bush is a one-instance MultiMesh.
                var crown = TreeFactory.Get("bush").Crown;
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = crown, InstanceCount = 1 };
                mm.SetInstanceTransform(0, new Transform3D(new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(new Vector3(0.2f, 0.13f, 0.2f)),
                    new Vector3(0f, 0.08f, 0f)));
                mm.SetInstanceColor(0, new Color(0.2f, 0.36f, 0.12f));
                patch.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = TreeFactory.FoliageMaterial(false) });
                patch._crop.AddChild(new MeshInstance3D { Mesh = Berries, MaterialOverride = BerryMaterial, Position = new Vector3(0f, 0.02f, 0f) });
                break;
        }
        return patch;
    }

    /// <summary>Takes the crop: it shrinks away. False when there was nothing to pick.</summary>
    public bool Pick(float now)
    {
        if (!Ripe)
            return false;
        Ripe = false;
        PickedAt = now;
        _shrink = 0f;
        return true;
    }

    /// <summary>Grows the crop back once a day has passed since picking.</summary>
    public void Update(float now)
    {
        if (Ripe || float.IsNaN(PickedAt) || !Foraging.Regrown(PickedAt, now))
            return;
        Ripe = true;
        _shrink = -1f;
        _crop.Scale = Vector3.One;
        _crop.Visible = true;
    }

    /// <summary>Game hours until it grows back (0 when ripe).</summary>
    public float HoursLeft(float now) => Ripe ? 0f : MathF.Max(0f, PickedAt + Foraging.RegrowHours - now);

    public override void _Process(double delta)
    {
        if (_shrink < 0f)
            return;
        _shrink += (float)delta * 3f;
        float k = MathF.Max(0f, 1f - _shrink);
        _crop.Scale = new Vector3(k, k, k);
        if (k <= 0f)
        {
            _crop.Visible = false;
            _shrink = -1f;
        }
    }

    // ------------------------------------------------------------------ models
    private static readonly Dictionary<string, Material> Mats = new();

    private static Material Mat(string key, Color c, float rough, float sss = 0f)
    {
        if (Mats.TryGetValue(key, out var m))
            return m;
        var mat = new StandardMaterial3D { AlbedoColor = c, Roughness = rough };
        if (sss > 0f)
        {
            mat.SubsurfScatterEnabled = true;
            mat.SubsurfScatterStrength = sss;
        }
        Mats[key] = mat;
        return mat;
    }

    private static Material Stem => Mat("stem", new Color(0.86f, 0.8f, 0.66f), 0.8f, 0.2f);
    private static Material PorciniCap => Mat("cap", new Color(0.42f, 0.25f, 0.12f), 0.45f);
    private static Material ChanterelleFlesh => Mat("chant", new Color(0.97f, 0.66f, 0.16f), 0.7f, 0.25f);
    private static Material BerryMaterial => Mat("berry", new Color(0.12f, 0.15f, 0.32f), 0.35f);

    /// <summary>A porcini: a fat bulging stem with a fine net, a domed chestnut cap.</summary>
    private static Node3D Porcini(float size, Vector3 at, RandomNumberGenerator rng)
    {
        var root = new Node3D { Position = at, Rotation = new Vector3(rng.RandfRange(-0.12f, 0.12f), rng.Randf() * Mathf.Tau, rng.RandfRange(-0.12f, 0.12f)) };
        float h = 0.07f * size, r = 0.022f * size;
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r * 0.85f, BottomRadius = r * 1.35f, Height = h, RadialSegments = 12, Rings = 3 },
            MaterialOverride = Stem, Position = new Vector3(0f, h / 2f, 0f),
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.05f * size, Height = 0.05f * size, RadialSegments = 20, Rings = 8, IsHemisphere = true },
            MaterialOverride = PorciniCap, Position = new Vector3(0f, h - 0.004f, 0f), Scale = new Vector3(1f, 0.62f, 1f),
        });
        // The cap's paler rim.
        root.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.043f * size, OuterRadius = 0.051f * size, Rings = 20, RingSegments = 6 },
            MaterialOverride = Mat("caprim", new Color(0.62f, 0.42f, 0.22f), 0.5f), Position = new Vector3(0f, h - 0.002f, 0f),
            Scale = new Vector3(1f, 0.5f, 1f),
        });
        // The pale pores under the cap.
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.049f * size, BottomRadius = 0.03f * size, Height = 0.006f, RadialSegments = 18, Rings = 1 },
            MaterialOverride = Mat("pores", new Color(0.88f, 0.84f, 0.6f), 0.9f), Position = new Vector3(0f, h - 0.006f, 0f),
        });
        return root;
    }

    /// <summary>A chanterelle: a short stem flaring into a wavy yellow funnel.</summary>
    private static Node3D Chanterelle(float size, Vector3 at, RandomNumberGenerator rng)
    {
        var root = new Node3D { Position = at, Rotation = new Vector3(rng.RandfRange(-0.3f, 0.3f), rng.Randf() * Mathf.Tau, rng.RandfRange(-0.3f, 0.3f)) };
        float h = 0.04f * size;
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.022f * size, BottomRadius = 0.005f * size, Height = h, RadialSegments = 10, Rings = 2 },
            MaterialOverride = ChanterelleFlesh, Position = new Vector3(0f, h / 2f, 0f),
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.017f * size, BottomRadius = 0.022f * size, Height = 0.004f, RadialSegments = 10, Rings = 1 },
            MaterialOverride = Mat("chant_top", new Color(0.9f, 0.58f, 0.12f), 0.75f), Position = new Vector3(0f, h - 0.001f, 0f),
        });
        return root;
    }

    private static ArrayMesh? _berries;

    /// <summary>A few dozen berries on the bush's top (one shared mesh).</summary>
    private static ArrayMesh Berries
    {
        get
        {
            if (_berries != null)
                return _berries;
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            var rng = new RandomNumberGenerator { Seed = 99 };
            const int lat = 4, lon = 6;
            for (int b = 0; b < 30; b++)
            {
                float a = rng.Randf() * Mathf.Tau, d = MathF.Sqrt(rng.Randf()) * 0.17f;
                var c = new Vector3(MathF.Cos(a) * d, 0.1f + rng.RandfRange(-0.03f, 0.05f) - d * 0.3f, MathF.Sin(a) * d);
                float r = rng.RandfRange(0.006f, 0.009f);
                Vector3 P(int i, int j)
                {
                    float th = i / (float)lat * Mathf.Pi, ph = j / (float)lon * Mathf.Tau;
                    return c + new Vector3(MathF.Sin(th) * MathF.Cos(ph), MathF.Cos(th), MathF.Sin(th) * MathF.Sin(ph)) * r;
                }
                for (int i = 0; i < lat; i++)
                    for (int j = 0; j < lon; j++)
                        foreach (var (u, v) in new[] { (i, j), (i + 1, j), (i + 1, j + 1), (i, j), (i + 1, j + 1), (i, j + 1) })
                        {
                            var p = P(u, v);
                            st.SetNormal((p - c).Normalized());
                            st.AddVertex(p);
                        }
            }
            return _berries = st.Commit();
        }
    }
}
