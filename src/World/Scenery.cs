using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Data;
using FishingSim.World.Props;

namespace FishingSim.World;

/// <summary>
/// Everything that stands on the terrain: the pier, the village (shops, houses, props), vegetation that follows the
/// map's biome (trees, bushes, flowers, grass, reeds) and rocks. Rebuilt whenever the terrain is rebuilt.
/// </summary>
public partial class Scenery : Node3D
{
    private LakeBed _lake = null!;
    private WorldState? _world;
    private readonly List<Node> _dynamic = new();
    private readonly List<Building> _buildings = new();
    private float _lastDaylight = -1f;

    /// <summary>Spots where the player can press E.</summary>
    public List<Interactable> Interactables { get; } = new();

    /// <summary>Labelled places for the map screen.</summary>
    public List<MapMarker> Markers { get; } = new();

    /// <summary>How many of each thing was placed on the current map (for tests and tuning).</summary>
    public Dictionary<string, int> Stats { get; } = new();

    public void Build(LakeBed lake, WorldState? world = null)
    {
        _lake = lake;
        _world = world;
        BuildPier();
        lake.Rebuilt += Rebuild;
        Rebuild();
    }

    public override void _ExitTree()
    {
        if (_lake != null)
            _lake.Rebuilt -= Rebuild;
    }

    private float _time;
    private float _wind = 0.3f;

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (_world == null)
            return;
        _wind = Mathf.MoveToward(_wind, _world.WindTarget, 0.2f * (float)delta);
        TreeFactory.SetWind(_wind, _time);
        float daylight = _world.Daylight;
        if (MathF.Abs(daylight - _lastDaylight) < 0.01f)
            return;
        _lastDaylight = daylight;
        foreach (var b in _buildings)
            b.SetDaylight(daylight);
    }

    // ------------------------------------------------------------------ pier
    private void BuildPier()
    {
        var wood = BuildingFactory.Pixel(new StandardMaterial3D
        {
            AlbedoTexture = Textures.Planks(),
            Uv1Triplanar = true,
            Uv1Scale = Vector3.One * Textures.TriplanarScale,
            Roughness = 0.9f,
        });
        var dark = BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.15f, 0.09f), Roughness = 0.95f });

        var body = new StaticBody3D { CollisionLayer = 1, Position = new Vector3(0, 0.95f, 1) };
        var deckSize = new Vector3(3, 0.2f, 10);
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = deckSize }, MaterialOverride = wood });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = deckSize } });
        AddChild(body);

        // Support posts and cross beams.
        foreach (float z in new[] { -3.5f, -1f, 1.5f, 4f })
        {
            foreach (float x in new[] { -1.35f, 1.35f })
                AddCylinder(new Vector3(x, -0.4f, z), 0.09f, 3f, dark);
            AddBox(new Vector3(0, 0.78f, z), new Vector3(3f, 0.1f, 0.12f), dark);
        }

        // Side railings (open at the casting end and at the shore).
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * 1.42f;
            foreach (float z in new[] { -1f, 1.5f, 4f })
                AddBox(new Vector3(x, 1.5f, z), new Vector3(0.07f, 0.9f, 0.07f), dark);
            AddBox(new Vector3(x, 1.95f, 1.5f), new Vector3(0.06f, 0.06f, 5.2f), dark);
            AddBox(new Vector3(x, 1.65f, 1.5f), new Vector3(0.05f, 0.05f, 5.2f), dark);
        }

        // Lantern post at the shore end.
        AddBox(new Vector3(1.42f, 2.1f, 5.6f), new Vector3(0.09f, 1.9f, 0.09f), dark);
        var lampMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.85f, 0.5f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.7f, 0.3f),
            EmissionEnergyMultiplier = 1.5f,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.26f, 0.22f) },
            MaterialOverride = lampMaterial,
            Position = new Vector3(1.42f, 3.15f, 5.6f),
        });
        AddChild(new OmniLight3D
        {
            LightColor = new Color(1f, 0.75f, 0.45f),
            LightEnergy = 1.2f,
            OmniRange = 12f,
            Position = new Vector3(1.42f, 3f, 5.6f),
        });
    }

    private void AddCylinder(Vector3 pos, float radius, float height, Material mat) =>
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = 8, Rings = 1 },
            MaterialOverride = mat,
            Position = pos,
        });

    private void AddBox(Vector3 pos, Vector3 size, Material mat) =>
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat, Position = pos });

    // ------------------------------------------------------------------ rebuild
    private void Rebuild()
    {
        foreach (var n in _dynamic)
            n.QueueFree();
        _dynamic.Clear();
        _buildings.Clear();
        Interactables.Clear();
        Markers.Clear();
        Stats.Clear();
        _lastDaylight = -1f;

        var loc = _lake.Location;
        BuildVillage(loc);
        var rng = new RandomNumberGenerator { Seed = 42 };
        ScatterTrees(loc, rng);
        ScatterGroundCover(loc, rng);
        ScatterReeds(loc, rng);
        ScatterRocks(loc, rng);
    }

    private void Track(Node node) => _dynamic.Add(node);

    private void Count(string key, int n = 1) => Stats[key] = Stats.GetValueOrDefault(key) + n;

    // ------------------------------------------------------------------ village
    private void BuildVillage(LocationData loc)
    {
        var layout = _lake.Layout;
        int variant = 0;
        foreach (var spec in layout.Specs)
        {
            var building = BuildingFactory.Create(spec, variant++);
            float h = _lake.HeightAt(loc, spec.Position.X, spec.Position.Y);
            building.Position = new Vector3(spec.Position.X, h, spec.Position.Y);
            building.Rotation = new Vector3(0f, spec.FacingYaw(loc.PlazaCenter), 0f);
            AddChild(building);
            Track(building);
            _buildings.Add(building);
            Count("building_" + spec.Kind.ToString().ToLowerInvariant());
            if (!spec.IsFunctional)
                continue;

            var door = spec.DoorPoint(loc.PlazaCenter);
            var spot = new Interactable
            {
                Kind = Enum.Parse<InteractKind>(spec.Kind.ToString()),
                Title = building.Title,
                Position = new Vector3(door.X, _lake.HeightAt(loc, door.X, door.Y), door.Y),
            };
            AddChild(spot);
            Track(spot);
            Interactables.Add(spot);
            Markers.Add(new MapMarker(spec.Position, building.Title, MarkerColor(spec.Kind)));
        }
    }

    private static Color MarkerColor(BuildingKind kind) => kind switch
    {
        BuildingKind.Tackle or BuildingKind.General => new Color(0.95f, 0.75f, 0.2f),
        BuildingKind.Bait => new Color(0.5f, 0.9f, 0.35f),
        BuildingKind.FishBuyer => new Color(0.4f, 0.8f, 1f),
        BuildingKind.Market => new Color(1f, 0.55f, 0.3f),
        BuildingKind.Office => new Color(1f, 0.45f, 0.45f),
        _ => new Color(0.85f, 0.85f, 0.95f),
    };

    // ------------------------------------------------------------------ placement helpers
    /// <summary>Keeps plants off the pier approach, building pads and paths.</summary>
    private bool Clear(LocationData loc, float x, float z, float padMargin = 2.5f, float pathMargin = 2.2f)
    {
        if (MathF.Abs(x) < 14f && z > -8f && z < 14f)
            return false;
        var layout = _lake.Layout;
        return layout.PadDistance(x, z) > padMargin && layout.PathDistance(x, z) > pathMargin;
    }

    private void AddMultiMesh(Mesh mesh, List<Transform3D> transforms, List<Color> colors, Material? material = null)
    {
        if (transforms.Count == 0)
            return;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            InstanceCount = transforms.Count,
        };
        for (int i = 0; i < transforms.Count; i++)
        {
            mm.SetInstanceTransform(i, transforms[i]);
            mm.SetInstanceColor(i, colors[i]);
        }
        var inst = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material ?? VertexColorMaterial() };
        AddChild(inst);
        Track(inst);
    }

    private static StandardMaterial3D VertexColorMaterial() => BuildingFactory.Pixel(new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Roughness = 1f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    });

    private static Color Tint(Color c, float t) => new(c.R + t, c.G + t, c.B + t);

    // ------------------------------------------------------------------ trees
    private static Color CrownColor(string kind) => kind switch
    {
        "spruce" => new Color(0.09f, 0.25f, 0.15f),
        "oak" => new Color(0.24f, 0.42f, 0.11f),
        "birch" => new Color(0.42f, 0.62f, 0.2f),
        "pine" => new Color(0.1f, 0.27f, 0.14f),
        "willow" => new Color(0.38f, 0.52f, 0.2f),
        _ => new Color(0.2f, 0.4f, 0.12f),
    };

    private void ScatterTrees(LocationData loc, RandomNumberGenerator rng)
    {
        string[] kinds = { "spruce", "oak", "birch", "pine", "willow" };
        var trunks = kinds.ToDictionary(k => k, _ => new List<Transform3D>());
        var trunkColors = kinds.ToDictionary(k => k, _ => new List<Color>());
        var crowns = kinds.ToDictionary(k => k, _ => new List<Transform3D>());
        var crownColors = kinds.ToDictionary(k => k, _ => new List<Color>());

        float threshold = -0.05f + (1f - loc.TreeDensity) * 0.5f;
        int cap = (int)(550 * Math.Min(loc.TreeDensity, 1.2f));
        int placed = 0;
        for (int i = 0; i < 4000 && placed < cap; i++)
        {
            float x = rng.RandfRange(-165f, 165f);
            float z = rng.RandfRange(-195f, 68f);
            float s = rng.RandfRange(0.7f, 1.7f);
            float tint = rng.RandfRange(-0.05f, 0.05f);
            float pick = rng.Randf();
            float yaw = rng.Randf() * Mathf.Tau;
            if (_lake.ForestDensity(x, z) < threshold)
                continue;
            float h = _lake.HeightAt(loc, x, z);
            if (h < 1.2f || !Clear(loc, x, z))
                continue;
            float inside = _lake.InsideMetres(loc, x, z);

            // Weighted choice of the kind, nudged by where the tree stands.
            float total = 0f;
            var weights = new float[kinds.Length];
            for (int k = 0; k < kinds.Length; k++)
            {
                float w = loc.TreeShare(kinds[k]);
                if (kinds[k] == "willow")
                    w *= inside > -16f && inside < -1.5f ? 3f : 0f;
                else if (kinds[k] == "pine")
                    w *= h > 3f ? 1.6f : 0.5f;
                else if (kinds[k] == "oak")
                    w *= h < 8f ? 1f : 0.3f;
                else if (kinds[k] == "spruce")
                    w *= h > 5f ? 1.4f : 1f;
                weights[k] = w;
                total += w;
            }
            if (total <= 0f)
                continue;
            float roll = pick * total;
            int chosen = 0;
            for (int k = 0; k < kinds.Length; k++)
            {
                roll -= weights[k];
                if (roll <= 0f)
                {
                    chosen = k;
                    break;
                }
            }
            string kind = kinds[chosen];
            var meshes = TreeFactory.Get(kind);
            var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(s, s, s));
            var pos = new Vector3(x, h - 0.1f, z);
            trunks[kind].Add(new Transform3D(basis, pos + new Vector3(0, meshes.TrunkLift * s, 0)));
            trunkColors[kind].Add(Tint(meshes.TrunkColor, tint * 0.5f));
            crowns[kind].Add(new Transform3D(basis, pos + new Vector3(0, meshes.CrownLift * s, 0)));
            crownColors[kind].Add(Tint(CrownColor(kind), tint));
            placed++;
            Count("tree_" + kind);
        }
        foreach (var kind in kinds)
        {
            var meshes = TreeFactory.Get(kind);
            if (meshes.Trunk != null)
                AddMultiMesh(meshes.Trunk, trunks[kind], trunkColors[kind], TreeFactory.BarkMaterial());
            AddMultiMesh(meshes.Crown, crowns[kind], crownColors[kind], TreeFactory.FoliageMaterial(meshes.Needles));
        }
    }

    // ------------------------------------------------------------------ bushes, flowers, grass
    private void ScatterGroundCover(LocationData loc, RandomNumberGenerator rng)
    {
        var bushes = new List<Transform3D>(); var bushColors = new List<Color>();
        var flowers = new List<Transform3D>(); var flowerColors = new List<Color>();
        var tufts = new List<Transform3D>(); var tuftColors = new List<Color>();
        Color[] palette =
        {
            new(0.97f, 0.97f, 0.92f), new(0.98f, 0.85f, 0.2f), new(0.92f, 0.4f, 0.6f), new(0.6f, 0.4f, 0.85f), new(0.9f, 0.25f, 0.2f),
        };
        int maxBushes = (int)(260 * loc.BushDensity), maxFlowers = (int)(1500 * loc.FlowerDensity), maxTufts = (int)(1100 * loc.BushDensity);
        for (int i = 0; i < 14000 && (bushes.Count < maxBushes || flowers.Count < maxFlowers || tufts.Count < maxTufts); i++)
        {
            float x = rng.RandfRange(-150f, 150f);
            float z = rng.RandfRange(-190f, 64f);
            float kindRoll = rng.Randf();
            float s = rng.RandfRange(0.7f, 1.4f);
            float yaw = rng.Randf() * Mathf.Tau;
            float h = _lake.HeightAt(loc, x, z);
            if (h < 1.0f || h > 14f || !Clear(loc, x, z, 1.5f, 1.6f))
                continue;
            float patch = _lake.Noise(x * 3f + 40f, z * 3f - 70f);   // flowers and grass grow in patches
            if (kindRoll < 0.2f && bushes.Count < maxBushes)
            {
                bushes.Add(new Transform3D(new Basis(Vector3.Up, yaw).Scaled(new Vector3(s, s, s)), new Vector3(x, h, z)));
                bushColors.Add(Tint(CrownColor("bush"), rng.RandfRange(-0.05f, 0.05f)));
            }
            else if (kindRoll < 0.5f && patch > 0.05f && flowers.Count < maxFlowers)
            {
                flowers.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(s, s, s) * 0.8f), new Vector3(x, h + 0.14f, z)));
                flowerColors.Add(palette[rng.RandiRange(0, palette.Length - 1)]);
            }
            else if (tufts.Count < maxTufts)
            {
                tufts.Add(new Transform3D(new Basis(Vector3.Up, yaw).Scaled(new Vector3(s, s * 1.2f, s)), new Vector3(x, h - 0.05f, z)));
                tuftColors.Add(Tint(loc.GrassColor.Lerp(new Color(0.5f, 0.65f, 0.25f), 0.35f), rng.RandfRange(-0.06f, 0.06f)));
            }
        }
        AddMultiMesh(TreeFactory.Get("bush").Crown, bushes, bushColors, TreeFactory.FoliageMaterial(false));
        AddMultiMesh(TreeFactory.Blossom(), flowers, flowerColors);
        AddMultiMesh(TreeFactory.Tuft(), tufts, tuftColors, TreeFactory.GrassMaterial());
        Count("bush", bushes.Count);
        Count("flower", flowers.Count);
        Count("tuft", tufts.Count);
    }

    // ------------------------------------------------------------------ reeds and rocks
    private void ScatterReeds(LocationData loc, RandomNumberGenerator rng)
    {
        List<Transform3D> reeds = new(), heads = new();
        List<Color> colors = new(), headColors = new();
        int maxReeds = (int)(1800 * loc.ReedDensity);
        for (int tries = 0; reeds.Count < maxReeds && tries < 90000; tries++)
        {
            // Sample along the shoreline (a point on the outline ellipse, nudged inwards) instead of the whole map.
            float angle = rng.Randf() * Mathf.Tau;
            float shrink = rng.RandfRange(0.9f, 1.02f);
            float x = loc.OutlineRx * shrink * MathF.Cos(angle);
            float z = loc.OutlineCz + loc.OutlineRz * shrink * MathF.Sin(angle);
            if (MathF.Abs(x) < 9f && z > -14f)
                continue;
            float d = _lake.DepthAt(loc, x, z);
            if (d < 0.15f || d > 0.9f)
                continue;
            if (_lake.ForestDensity(x * 1.7f, z * 1.7f) < 0f)
                continue;
            for (int k = 0; k < 3; k++)
            {
                float rx = x + rng.RandfRange(-0.5f, 0.5f);
                float rz = z + rng.RandfRange(-0.5f, 0.5f);
                float hh = rng.RandfRange(0.9f, 1.5f);
                var euler = new Vector3(rng.RandfRange(-0.12f, 0.12f), rng.Randf() * Mathf.Tau, rng.RandfRange(-0.12f, 0.12f));
                var b = Basis.FromEuler(euler).Scaled(new Vector3(1, hh, 1));
                var origin = new Vector3(rx, -_lake.DepthAt(loc, rx, rz), rz);
                reeds.Add(new Transform3D(b, origin + new Vector3(0, 0.75f * hh, 0)));
                float g = rng.RandfRange(-0.05f, 0.05f);
                colors.Add(new Color(0.42f + g, 0.5f + g, 0.2f));
                if (rng.Randf() < 0.35f)
                {
                    heads.Add(new Transform3D(b, origin + b.Y * 0.72f * hh));
                    headColors.Add(new Color(0.3f, 0.17f, 0.08f));
                }
            }
        }
        AddMultiMesh(TreeFactory.Reed(), reeds, colors);
        AddMultiMesh(TreeFactory.Cattail(), heads, headColors);
        Count("reed", reeds.Count);
    }

    private void ScatterRocks(LocationData loc, RandomNumberGenerator rng)
    {
        List<Transform3D> rocks = new();
        List<Color> colors = new();
        int maxRocks = (int)(160 * loc.RockDensity);
        float size = loc.RockDensity > 1.5f ? 1.7f : 1f;
        for (int tries = 0; rocks.Count < maxRocks && tries < 30000; tries++)
        {
            float x = rng.RandfRange(-160f, 160f);
            float z = rng.RandfRange(-195f, 64f);
            if (MathF.Abs(x) < 6f && z > -6f && z < 12f)
                continue;
            float inside = _lake.InsideMetres(loc, x, z);
            float h = _lake.HeightAt(loc, x, z);
            bool nearShore = inside > -6f && inside < 4f;
            bool hills = h > 5f && rng.Randf() < 0.1f * loc.RockDensity;
            if (!(nearShore || hills) || !Clear(loc, x, z, 1f, 1f))
                continue;
            if (_lake.ForestDensity(x * 2f + 7f, z * 2f) < 0.1f && nearShore)
                continue;
            var scale = new Vector3(rng.RandfRange(0.3f, 1.1f), rng.RandfRange(0.2f, 0.6f), rng.RandfRange(0.3f, 1f)) * size;
            var b = new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(scale);
            rocks.Add(new Transform3D(b, new Vector3(x, h, z)));
            float gr = rng.RandfRange(0.38f, 0.55f);
            colors.Add(new Color(gr, gr * 0.98f, gr * 0.93f));
        }
        AddMultiMesh(TreeFactory.Rock(), rocks, colors, TreeFactory.RockMaterial());
        Count("rock", rocks.Count);
    }
}
