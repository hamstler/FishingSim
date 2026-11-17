using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Data;
using FishingSim.Graphics;
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

    /// <summary>The water plant patches (lilies and underwater weed) of this map: where cover-loving fish hold.</summary>
    public List<Core.Methods.WeedBed> WeedBeds { get; } = new();

    /// <summary>Other anglers on the bank: where each sits and where its float lies (BuildAnglers; cleared on rebuild).</summary>
    public List<(Vector3 Seat, Vector3 Float)> Anglers { get; } = new();

    /// <summary>Sunken trees off the wooded banks (ScatterSunkenWood; cleared on rebuild).</summary>
    public List<Core.Methods.SunkenWood> SunkenWood { get; } = new();

    /// <summary>How many of each thing was placed on the current map (for tests and tuning).</summary>
    public Dictionary<string, int> Stats { get; } = new();

    /// <summary>Collision for the trees, boulders and props that have no body of their own.</summary>
    public Solids Solids { get; } = new();

    public void Build(LakeBed lake, WorldState? world = null)
    {
        _lake = lake;
        _world = world;
        AddChild(Solids);
        BuildPier();
        lake.Rebuilt += Rebuild;
        GraphicsQuality.Changed += ApplyFoliageRange;
        Rebuild();
    }

    public override void _ExitTree()
    {
        if (_lake != null)
            _lake.Rebuilt -= Rebuild;
        GraphicsQuality.Changed -= ApplyFoliageRange;
    }

    // Scattered plants with their full drawing range, shortened by the quality setting.
    private readonly List<(GeometryInstance3D Node, float Range)> _ranged = new();

    /// <summary>
    /// Buildings and props with the distance (m) they are drawn to at the normal view distance; scaled by the view
    /// distance setting (beyond it they are not drawn at all).
    /// </summary>
    private readonly List<(Node3D Root, float Range)> _props = new();

    private void Prop(Node3D root, float range) => _props.Add((root, range));

    /// <summary>
    /// Applies the quality and the view distance: how far the plants and the detailed trees reach, where the distant
    /// forest ends (fading out), how far buildings, props and lamp light are drawn.
    /// </summary>
    private void ApplyFoliageRange()
    {
        float plants = GraphicsQuality.FoliageRange * GraphicsQuality.ViewFactor;
        foreach (var (node, range) in _ranged)
            if (IsInstanceValid(node))
                node.VisibilityRangeEnd = range * plants;
        float nearTrees = TreeNearRange * GraphicsQuality.FoliageRange * GraphicsQuality.ViewFactor;
        float horizon = GraphicsQuality.ViewRange;
        foreach (var (near, far) in _treeLods)
            if (IsInstanceValid(near) && IsInstanceValid(far))
            {
                near.VisibilityRangeEnd = nearTrees;
                far.VisibilityRangeBegin = nearTrees;
                // The distant forest ends at the view distance, fading out over its last stretch.
                far.VisibilityRangeEnd = horizon;
                far.VisibilityRangeEndMargin = horizon > 0f ? horizon * 0.15f : 0f;
                far.VisibilityRangeFadeMode = horizon > 0f ? GeometryInstance3D.VisibilityRangeFadeModeEnum.Self
                    : GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
            }
        float view = GraphicsQuality.ViewFactor;
        foreach (var (root, range) in _props)
        {
            if (!IsInstanceValid(root))
                continue;
            foreach (var n in root.FindChildren("*", "GeometryInstance3D", true, false))
            {
                var g = (GeometryInstance3D)n;
                g.VisibilityRangeEnd = range * view;
                g.VisibilityRangeEndMargin = range * view * 0.08f;
            }
            // Lamps and fires: their light fades out further off (the light is costly, the glow stays).
            foreach (var n in root.FindChildren("*", "OmniLight3D", true, false))
            {
                var l = (OmniLight3D)n;
                l.DistanceFadeEnabled = true;
                l.DistanceFadeBegin = 45f * view;
                l.DistanceFadeLength = 15f;
                l.DistanceFadeShadow = 25f * view;
            }
        }
    }

    private float _time;
    private float _wind = 0.3f;

    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("scenery");
        _time += (float)delta;
        if (_world == null)
            return;
        _wind = Mathf.MoveToward(_wind, _world.WindTarget, 0.2f * (float)delta);
        TreeFactory.SetWind(_wind, _time);
        float daylight = _world.Daylight;
        if (MathF.Abs(daylight - _lastDaylight) < 0.01f)
            return;
        _lastDaylight = daylight;
        if (_bulbs != null)
            _bulbs.EmissionEnergyMultiplier = 0.5f + (1f - daylight) * 3f;
        foreach (var b in _buildings)
            b.SetDaylight(daylight);
    }

    // ------------------------------------------------------------------ pier
    private void BuildPier()
    {
        // Old grey-brown boards weathered like the buildings (colour wander, rain streaks, moss in patches); the posts
        // and rails darker, soaked and dirty where they stand in the water. The painted planks when the sets are missing.
        Material wood = Props.BuildingFactory.Weathered("weathered_brown_planks", new Color(0.92f, 0.9f, 0.86f), 0.85f, moss: 0.22f)
            ?? OldDeck();
        Material dark = Props.BuildingFactory.Weathered("weathered_brown_planks", new Color(0.5f, 0.45f, 0.4f), 0.92f, scale: 0.6f, moss: 0.12f)
            ?? BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.15f, 0.09f), Roughness = 0.95f });

        var body = new StaticBody3D { CollisionLayer = 1, Position = new Vector3(0, 0.95f, 1) };
        var deckSize = new Vector3(3, 0.2f, 10);
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = deckSize }, MaterialOverride = wood });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = deckSize } });
        AddChild(body);

        // Support posts and cross beams.
        foreach (float z in new[] { -3.5f, -1f, 1.5f, 4f })
        {
            foreach (float x in new[] { -1.35f, 1.35f })
                // Soaked and dirty from the waterline up (the weathering shader's damp foot, measured from the water).
                AddCylinder(new Vector3(x, -0.4f, z), 0.09f, 3f, dark).SetInstanceShaderParameter("base_y", 0f);
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

        BuildStringLights(dark);

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

    private StandardMaterial3D? _bulbs;

    /// <summary>
    /// Festoon lights over the pier: two masts at the casting end and a post at the shore, ropes sagging between
    /// them, warm bulbs every half metre. Dim by day, glowing at night.
    /// </summary>
    private void BuildStringLights(Material wood)
    {
        // The masts stand behind the angler's casting spot (it is at z = -3), not in front of it: they used to frame half the view.
        foreach (var (x, z, h) in new[] { (-1.42f, -0.4f, 4.4f), (1.42f, -0.4f, 4.4f), (-1.42f, 5.6f, 3.0f) })
            AddBox(new Vector3(x, 1.05f + h / 2f, z), new Vector3(0.1f, h, 0.1f), wood);
        var tops = new[] { new Vector3(-1.42f, 5.45f, -0.4f), new Vector3(1.42f, 5.45f, -0.4f), new Vector3(1.42f, 3.05f, 5.6f), new Vector3(-1.42f, 4.05f, 5.6f) };
        var rope = BuildingFactory.Pixel(new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.18f, 0.12f) });
        _bulbs = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.86f, 0.5f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.72f, 0.32f),
            EmissionEnergyMultiplier = 0.6f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
        var bulbs = new List<Transform3D>();
        foreach (var (a, b) in new[] { (tops[0], tops[1]), (tops[1], tops[2]), (tops[0], tops[3]) })
        {
            const int segments = 12;
            Vector3 Point(float t) => a.Lerp(b, t) + Vector3.Down * (0.45f * 4f * t * (1f - t));
            for (int i = 0; i < segments; i++)
            {
                var p0 = Point(i / (float)segments);
                var p1 = Point((i + 1) / (float)segments);
                var mid = (p0 + p1) * 0.5f;
                var dir = p1 - p0;
                var y = dir.Normalized();
                var x = MathF.Abs(y.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : y.Cross(Vector3.Up).Normalized();
                var zAxis = x.Cross(y).Normalized();
                AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 1f, RadialSegments = 4 },
                    MaterialOverride = rope,
                    Transform = new Transform3D(new Basis(x, y * dir.Length(), zAxis), mid),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
            float length = a.DistanceTo(b);
            int count = (int)(length / 0.5f);
            for (int i = 1; i < count; i++)
                bulbs.Add(new Transform3D(Basis.Identity, Point(i / (float)count) + Vector3.Down * 0.07f));
        }
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new BoxMesh { Size = new Vector3(0.09f, 0.11f, 0.09f) }, InstanceCount = bulbs.Count };
        for (int i = 0; i < bulbs.Count; i++)
            mm.SetInstanceTransform(i, bulbs[i]);
        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = _bulbs, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    private MeshInstance3D AddCylinder(Vector3 pos, float radius, float height, Material mat)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = 12, Rings = 1 },
            MaterialOverride = mat,
            Position = pos,
        };
        AddChild(mi);
        return mi;
    }

    /// <summary>The pier's boards without the photo sets: the old plain material.</summary>
    private static Material OldDeck()
    {
        var deck = Graphics.PhotoTextures.Get("brown_planks_09");
        var wood = BuildingFactory.Pixel(new StandardMaterial3D
        {
            AlbedoTexture = deck?.Albedo ?? Textures.Planks(),
            Uv1Triplanar = true,
            Uv1Scale = Vector3.One * Textures.TriplanarScale,
            Roughness = 0.9f,
        });
        if (deck != null)
        {
            wood.Uv1Scale = Vector3.One / deck.Metres;
            wood.NormalEnabled = true;
            wood.NormalTexture = deck.Normal;
        }
        else if (Textures.NormalFor(Textures.Planks()) is { } planksNormal)
        {
            wood.NormalEnabled = true;
            wood.NormalTexture = planksNormal;
        }
        return wood;
    }


    private void AddBox(Vector3 pos, Vector3 size, Material mat) =>
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat, Position = pos });

    // ------------------------------------------------------------------ rebuild
    private void Rebuild()
    {
        Solids.Clear();
        foreach (var n in _dynamic)
            n.QueueFree();
        _dynamic.Clear();
        _ranged.Clear();
        _treeLods.Clear();
        _buildings.Clear();
        Interactables.Clear();
        Markers.Clear();
        WeedBeds.Clear();
        SunkenWood.Clear();
        _landmarkTrees.Clear();
        Anglers.Clear();
        _anglerParts.Clear();
        Stats.Clear();
        _reserved.Clear();
        _props.Clear();
        _lastDaylight = -1f;

        var loc = _lake.Location;
        BuildVillage(loc);
        BuildCampfires(loc);
        BuildEverydayLife(loc);
        BuildLandmarks(loc);
        BuildAnglers(loc);
        var rng = new RandomNumberGenerator { Seed = 42 };
        ScatterTrees(loc, rng);
        ScatterGroundCover(loc, rng);
        ScatterForage(loc, rng);
        ScatterReeds(loc, rng);
        ScatterShoreline(loc, rng);
        ScatterWaterPlants(loc, rng);
        ScatterSunkenWood(loc, rng);
        ScatterRocks(loc, rng);
        ApplyFoliageRange();
    }

    private void Track(Node node) => _dynamic.Add(node);

    private void Count(string key, int n = 1) => Stats[key] = Stats.GetValueOrDefault(key) + n;

    // ------------------------------------------------------------------ village
    /// <summary>True when this map's village has a smokehouse (notices may ask for smoked fish).</summary>
    public bool HasSmokehouse { get; private set; }

    private void BuildVillage(LocationData loc)
    {
        HasSmokehouse = false;
        var layout = _lake.Layout;
        BuildingFactory.Style = loc.VillageStyle;
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
            Prop(building, spec.PadRadius > 0f ? 300f : 120f);
            Count("building_" + spec.Kind.ToString().ToLowerInvariant());
            if (spec.Kind == BuildingKind.Smokehouse)
            {
                // The smokehouse is a house, but its smoker takes fish from the keepnet.
                var at = spec.DoorPoint(loc.PlazaCenter);
                var smoke = new Interactable
                {
                    Kind = InteractKind.Smokehouse, Title = building.Title, Radius = 3.4f,
                    Position = new Vector3(at.X, _lake.HeightAt(loc, at.X, at.Y), at.Y),
                };
                AddChild(smoke);
                Track(smoke);
                Interactables.Add(smoke);
                Markers.Add(new MapMarker(spec.Position, building.Title, new Color(0.75f, 0.5f, 0.3f)));
                HasSmokehouse = true;
            }
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
        AddWanderers(loc, layout);
    }

    /// <summary>The villagers that walk about (World/Props/Wanderer), for tests.</summary>
    public List<Props.Wanderer> Wanderers { get; } = new();

    /// <summary>
    /// The village's standing villagers (BuildingKind.Villager) walk about by day: from the nearest house's door (where
    /// they go in at night) to their own spot, the square, the well and two neighbours' doors.
    /// </summary>
    private void AddWanderers(LocationData loc, MapLayout layout)
    {
        Wanderers.Clear();
        if (_world == null)
            return;
        var doors = new List<Vector2>();
        Vector2? well = null;
        foreach (var spec in layout.Specs)
        {
            if (spec.IsDwelling)
                doors.Add(spec.DoorPoint(loc.PlazaCenter));
            if (spec.Kind == BuildingKind.Well)
                well = spec.Position + (loc.PlazaCenter - spec.Position).Normalized() * 1.6f;
        }
        if (doors.Count == 0)
            return;
        int n = 0;
        for (int i = 0; i < layout.Specs.Count && i < _buildings.Count; i++)
        {
            var spec = layout.Specs[i];
            if (spec.Kind != BuildingKind.Villager)
                continue;
            var npc = _buildings[i].GetChildren().OfType<Props.Npc>().FirstOrDefault();
            if (npc == null)
                continue;
            var own = spec.Position;
            var byDistance = doors.OrderBy(d => d.DistanceTo(own)).ToList();
            var stops = new List<Vector2> { byDistance[0], own, loc.PlazaCenter + new Vector2((n % 3 - 1) * 2.2f, (n % 2) * 2f - 1f) };
            if (well is { } w)
                stops.Add(w + new Vector2(0.6f * (n % 2 * 2 - 1), 0f));
            for (int k = 1; k < byDistance.Count && k <= 2; k++)
                stops.Add(byDistance[k] + new Vector2(0.4f, 0.4f));
            var walker = Props.Wanderer.Attach(npc, _lake, _world, stops, n * 7 + 3);
            var world = _world;
            var talkRng = new Core.SystemRng(n * 31 + 5);
            var fish = new List<(string, float)>();
            foreach (var key in loc.Abundance.Keys)
                fish.Add(((string)key, loc.Abundance[key].AsSingle()));
            walker.Talk = () => Core.Camp.VillageTalk.Line(new Core.Camp.TalkContext(world.Hour, world.Weather, world.WindTarget, fish), talkRng);
            Wanderers.Add(walker);
            n++;
        }
        Count("villager_walking", n);
    }

    // ------------------------------------------------------------------ campfires
    /// <summary>The campfires on this map: the angler's camp by the pier and the village hearth.</summary>
    public List<Props.Campfire> Campfires { get; } = new();

    /// <summary>Where the angler's camp stands (for tests and screenshots).</summary>
    public Vector3 CampPosition { get; private set; }

    /// <summary>
    /// The angler's camp on the grass beside the pier (a fire ring with a tripod, two logs to sit on, a stack of wood, a
    /// shovel stuck in the ground, a bucket and a crate) and a cooking hearth on the village square with logs around it.
    /// Both are interactable: light the fire, cook fish from the keepnet.
    /// </summary>
    private void BuildCampfires(LocationData loc)
    {
        Campfires.Clear();
        var layout = _lake.Layout;
        bool Free(float x, float z, float pad, float path) =>
            layout.PadDistance(x, z) > pad && layout.PathDistance(x, z) > path && _lake.IsWalkable(x, z);
        // The camp: the nearest flat grass to the pier's foot, clear of paths and buildings.
        Vector2? camp = null;
        for (float r = 7f; r < 40f && camp == null; r += 1.5f)
            for (float a = 0f; a < Mathf.Tau && camp == null; a += 0.25f)
            {
                float x = MathF.Cos(a) * r, z = 9f + MathF.Sin(a) * r * 0.6f;
                float h = _lake.HeightAt(loc, x, z);
                if (h < 0.9f || h > 4f || !Free(x, z, 5f, 3f))
                    continue;
                float slope = MathF.Abs(_lake.HeightAt(loc, x + 1.5f, z) - _lake.HeightAt(loc, x - 1.5f, z))
                              + MathF.Abs(_lake.HeightAt(loc, x, z + 1.5f) - _lake.HeightAt(loc, x, z - 1.5f));
                if (slope < 0.6f)
                    camp = new Vector2(x, z);
            }
        if (camp is Vector2 c)
            AddCampfire(loc, c, "Twoje obozowisko", true);
        // The village hearth: on the square, a few metres off its middle, clear of the buildings.
        var plaza = loc.PlazaCenter;
        bool placed = false;
        for (float r = 5f; r < 24f && !placed; r += 1.5f)
            for (float a = 0.5f; a < Mathf.Tau + 0.5f && !placed; a += 0.35f)
            {
                var p = plaza + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                float h = _lake.HeightAt(loc, p.X, p.Y);
                if (layout.PadDistance(p.X, p.Y) > 3.5f && _lake.IsWalkable(p.X, p.Y) && h > 0.6f
                    && new Vector2(p.X - (camp?.X ?? 1e4f), p.Y - (camp?.Y ?? 1e4f)).Length() > 12f)
                {
                    AddCampfire(loc, p, "Palenisko w wiosce", false);
                    placed = true;
                }
            }
        BuildNoticeBoard(loc);
    }

    /// <summary>Where the notice board stands (for tests and screenshots), or null.</summary>
    public Node3D? NoticeBoard { get; private set; }

    /// <summary>
    /// The notice board on the village square: a roofed board on two posts with the villagers' notes pinned to it,
    /// a few metres off the square's middle, facing it, clear of the buildings and the hearth.
    /// </summary>
    private void BuildNoticeBoard(LocationData loc)
    {
        NoticeBoard = null;
        var layout = _lake.Layout;
        var plaza = loc.PlazaCenter;
        var hearth = Campfires.FirstOrDefault(f => !f.PlayerCamp);
        var avoid = hearth != null ? new Vector2(hearth.GlobalPosition.X, hearth.GlobalPosition.Z) : new Vector2(1e4f, 1e4f);
        if (hearth != null && !hearth.IsInsideTree())
            avoid = new Vector2(hearth.GetParent<Node3D>().Position.X, hearth.GetParent<Node3D>().Position.Z);
        for (float r = 6f; r < 26f; r += 1.5f)
            for (float a = 2.4f; a < Mathf.Tau + 2.4f; a += 0.3f)
            {
                var p = plaza + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                float h = _lake.HeightAt(loc, p.X, p.Y);
                if (layout.PadDistance(p.X, p.Y) < 3f || layout.PathDistance(p.X, p.Y) < 1.6f || !_lake.IsWalkable(p.X, p.Y) || h < 0.6f
                    || p.DistanceTo(avoid) < 6f || layout.Specs.Any(sp => sp.Position.DistanceTo(p) < 3.5f))
                    continue;
                var board = Props.Everyday.NoticeBoard();
                board.Position = new Vector3(p.X, h, p.Y);
                var toPlaza = plaza - p;
                board.Rotation = new Vector3(0f, MathF.Atan2(toPlaza.X, toPlaza.Y), 0f);
                AddChild(board);
                Track(board);
                Prop(board, 120f);
                Solids.BoxIn(board.Transform, new Vector3(0f, 1.1f, 0f), new Vector3(1.9f, 2.2f, 0.35f));
                NoticeBoard = board;
                _reserved.Add((p, 4f));
                var front = p + toPlaza.Normalized() * 1.4f;
                var spot = new Interactable
                {
                    Kind = InteractKind.NoticeBoard, Title = "Tablica ogłoszeń", Radius = 2.6f,
                    Position = new Vector3(front.X, _lake.HeightAt(loc, front.X, front.Y), front.Y),
                };
                AddChild(spot);
                Track(spot);
                Interactables.Add(spot);
                Markers.Add(new MapMarker(p, "Tablica ogłoszeń", new Color(0.95f, 0.85f, 0.55f)));
                Count("notice_board");
                return;
            }
    }

    private void AddCampfire(LocationData loc, Vector2 at, string title, bool playerCamp)
    {
        float h = _lake.HeightAt(loc, at.X, at.Y);
        var root = new Node3D { Position = new Vector3(at.X, h, at.Y), Name = playerCamp ? "Camp" : "Hearth" };
        var fire = new Props.Campfire { Title = title, PlayerCamp = playerCamp };
        root.AddChild(fire);
        // Logs to sit on round the fire, facing it; at the village hearth two villagers sit on them and talk.
        for (int i = 0; i < (playerCamp ? 2 : 3); i++)
        {
            float a = (playerCamp ? 0.6f + i * 2.2f : 0.4f + i * 2.1f);
            Props.Npc? sitter = !playerCamp && i < 2
                ? Props.People.Create(i == 0 ? Props.Role.Elder : Props.Role.Woman, 40 + i,
                    i == 0 ? "Sitting_Idle_Loop" : "Sitting_Talking_Loop", Array.Empty<string>())
                : null;
            // A log as thick as the sitter's seat is high, so whoever sits on it touches it.
            float radius = sitter?.SeatLogRadius ?? (playerCamp ? 0.2f : 0.23f);
            var log = Props.CampProps.SeatLog(1.3f, i + (playerCamp ? 0 : 5), radius, even: sitter != null);
            log.Name = $"SeatLog{i}";
            log.Position = new Vector3(MathF.Cos(a) * 1.7f, 0f, MathF.Sin(a) * 1.7f);
            log.Rotation = new Vector3(0f, -a + Mathf.Pi / 2f, 0f);
            root.AddChild(log);
            if (sitter != null)
            {
                // Facing the fire, the hips right over the log's axis.
                float back = sitter.Seat.Back;
                sitter.Position = new Vector3(MathF.Cos(a) * (1.7f - back), 0f, MathF.Sin(a) * (1.7f - back));
                sitter.Rotation = new Vector3(0f, MathF.Atan2(-MathF.Cos(a), -MathF.Sin(a)), 0f);
                root.AddChild(sitter);
                Count("villager_sitting");
            }
        }
        if (playerCamp)
        {
            // A stack of split wood, a shovel left stuck in the ground, a bucket and a crate.
            for (int i = 0; i < 6; i++)
            {
                var piece = Props.CampProps.SeatLog(0.6f, i + 20);
                piece.Name = $"Firewood{i}";
                piece.Scale = new Vector3(1f, 0.45f, 0.45f);
                piece.Position = new Vector3(-2.4f + (i % 3) * 0.02f, 0.02f + (i / 3) * 0.14f, 1.1f + (i % 3) * 0.16f);
                piece.Rotation = new Vector3(0f, 0.2f, 0f);
                root.AddChild(piece);
            }
            var shovel = Props.CampProps.Shovel(out _);
            shovel.Position = new Vector3(2.3f, -0.12f, 0.9f);
            shovel.RotationDegrees = new Vector3(-12f, 30f, 8f);
            root.AddChild(shovel);
            if (Props.PropAsset.Create("wooden_bucket_01", height: 0.32f) is { } bucket)
            {
                bucket.Position = new Vector3(2.1f, 0f, -0.6f);
                root.AddChild(bucket);
            }
            if (Props.PropAsset.Create("wooden_crate_01", height: 0.45f) is { } crate)
            {
                crate.Position = new Vector3(-2.2f, 0f, -0.9f);
                crate.RotationDegrees = new Vector3(0f, 20f, 0f);
                root.AddChild(crate);
            }
            CampPosition = root.Position;
        }
        AddChild(root);
        Track(root);
        Prop(root, 140f);
        Campfires.Add(fire);
        // The stone ring is solid (the angler walks round the fire), and so are the logs round it.
        Solids.Cylinder(root.Position - new Vector3(0f, 0.2f, 0f), 0.7f, 1.2f);
        foreach (var child in root.GetChildren())
            if (child is Node3D { Name: var n } seat && n.ToString().StartsWith("SeatLog"))
                Solids.BoxIn(root.Transform * seat.Transform, new Vector3(0f, 0.2f, 0f), new Vector3(1.3f, 0.4f, 0.42f));
        _reserved.Add((at, 4.5f));
        var spot = new Interactable { Kind = InteractKind.Campfire, Title = title, Radius = 3.2f, Position = root.Position, Target = fire };
        AddChild(spot);
        Track(spot);
        Interactables.Add(spot);
        Markers.Add(new MapMarker(at, title, new Color(1f, 0.5f, 0.2f)));
    }

    // ------------------------------------------------------------------ everyday life
    /// <summary>
    /// Life round the houses: behind or beside each home one of a washing line (a woman hanging linen), a chopping
    /// block with a woodcutter at a felled trunk, a rack of tools, a fishing net drying on poles (fishing and timber
    /// villages) or a gardener watering with a bucket (farms); and rowboats pulled up on the shore by the pier.
    /// </summary>
    private void BuildEverydayLife(LocationData loc)
    {
        var layout = _lake.Layout;
        int n = 0;
        foreach (var building in _buildings)
        {
            if (!building.Spec.IsDwelling)
                continue;
            int kind = n++ % 4;
            float r = building.Spec.PadRadius;
            // Behind the house or to its side, where no path runs.
            Vector3? at = null;
            float yaw = building.Rotation.Y;
            foreach (var local in new[] { new Vector3(0f, 0f, -r * 0.85f), new Vector3(r * 0.85f, 0f, -1f), new Vector3(-r * 0.85f, 0f, -1f) })
            {
                var p = building.Transform * local;
                if (layout.PathDistance(p.X, p.Z) > 2f && _lake.IsWalkable(p.X, p.Z) && _lake.HeightAt(loc, p.X, p.Z) > 0.6f)
                {
                    at = new Vector3(p.X, _lake.HeightAt(loc, p.X, p.Z), p.Z);
                    break;
                }
            }
            if (at is not Vector3 spot)
                continue;
            var root = new Node3D { Position = spot, Rotation = new Vector3(0f, yaw, 0f) };
            switch (kind)
            {
                case 0:
                    root.AddChild(Props.Everyday.LaundryLine(n));
                    var washer = Props.People.Create(Props.Role.Woman, n * 3 + 1, "Idle_Loop", new[] { "PickUp_Table", "Interact" });
                    washer.Position = new Vector3(0.4f, 0f, 0.9f);
                    washer.RotationDegrees = new Vector3(0f, 180f, 0f);
                    root.AddChild(washer);
                    Count("villager_washing");
                    break;
                case 1:
                    var block = Props.Everyday.ChoppingBlock(n);
                    block.Position = new Vector3(-1.4f, 0f, 0f);
                    root.AddChild(block);
                    // A felled trunk standing up to be split; the woodcutter swings at it.
                    var trunk = new MeshInstance3D
                    {
                        Mesh = new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.24f, Height = 1.5f, RadialSegments = 12 },
                        Position = new Vector3(0.9f, 0.75f, 0f), MaterialOverride = Props.CampProps.Bark,
                    };
                    root.AddChild(trunk);
                    var cutter = Props.People.Create(Props.Role.Hunter, n * 5 + 2, "TreeChopping_Loop", Array.Empty<string>(), Props.Everyday.Axe());
                    cutter.Position = new Vector3(0.9f, 0f, 1.05f);
                    cutter.RotationDegrees = new Vector3(0f, 180f, 0f);
                    root.AddChild(cutter);
                    Count("villager_woodcutter");
                    break;
                case 2:
                    // A rack of tools: a beam on two posts, the tools leaning against it.
                    foreach (float x in new[] { -0.8f, 0.8f })
                        root.AddChild(new MeshInstance3D
                        {
                            Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.045f, Height = 1.2f, RadialSegments = 8 },
                            Position = new Vector3(x, 0.6f, 0f), MaterialOverride = Props.CampProps.DarkWood,
                        });
                    root.AddChild(new MeshInstance3D
                    {
                        Mesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 1.8f, RadialSegments = 8 },
                        Position = new Vector3(0f, 1.15f, 0f), RotationDegrees = new Vector3(0f, 0f, 90f), MaterialOverride = Props.CampProps.DarkWood,
                    });
                    var tools = Props.Everyday.LeaningTools(n);
                    tools.Position = new Vector3(0f, 0f, -0.42f);
                    root.AddChild(tools);
                    break;
                default:
                    if (loc.VillageStyle == "farm")
                    {
                        Node3D? bucket = Props.PropAsset.Create("wooden_bucket_01", height: 0.28f);
                        var gardener = Props.People.Create(Props.Role.Farmer, n * 7 + 3, "Farm_Watering", new[] { "Idle_Loop" }, bucket);
                        root.AddChild(gardener);
                        Count("villager_gardener");
                    }
                    else
                    {
                        root.AddChild(Props.Everyday.NetRack(n));
                        Count("net_rack");
                    }
                    break;
            }
            AddChild(root);
            Track(root);
            Prop(root, 110f);
            Count("everyday_" + kind);
            var frame = root.Transform;
            foreach (var child in root.GetChildren())
                switch (child.Name.ToString())
                {
                    case "Laundry":
                        foreach (float x in new[] { -1.4f, 1.4f })
                            Solids.CylinderIn(frame, new Vector3(x, 0f, 0f), 0.08f, 1.9f);
                        break;
                    case "ChoppingBlock":
                        Solids.CylinderIn(frame, new Vector3(-1.4f, 0f, 0f), 0.36f, 0.55f);
                        Solids.CylinderIn(frame, new Vector3(0.9f, 0f, 0f), 0.26f, 1.5f);
                        break;
                    case "NetRack":
                        foreach (float x in new[] { -1.6f, 1.6f })
                            Solids.CylinderIn(frame, new Vector3(x, 0f, 0f), 0.08f, 1.8f);
                        break;
                }
            if (kind == 2)
                Solids.BoxIn(frame, new Vector3(0f, 0.6f, -0.1f), new Vector3(1.9f, 1.2f, 0.6f));
        }
        // Rowboats pulled up on the beach either side of the pier, bows towards the water.
        int boats = 0;
        foreach (float side in new[] { -1f, 1f })
            for (float x = 7f; x < 40f && boats < (side < 0 ? 1 : 2); x += 2f)
            {
                float bx = side * x;
                for (float z = -2f; z < 14f; z += 1f)
                {
                    float h = _lake.HeightAt(loc, bx, z);
                    if (h < 0.08f || h > 0.45f || layout.PathDistance(bx, z) < 3f)
                        continue;
                    // The bow points down the slope, towards the water.
                    float dx = _lake.HeightAt(loc, bx + 1f, z) - _lake.HeightAt(loc, bx - 1f, z);
                    float dz = _lake.HeightAt(loc, bx, z + 1f) - _lake.HeightAt(loc, bx, z - 1f);
                    float boatYaw = MathF.Atan2(-dx, -dz);
                    var boat = Props.Everyday.Rowboat(boats + (int)loc.RimHeight);
                    boat.Position = new Vector3(bx, h - 0.03f, z);
                    boat.Rotation = new Vector3(0.04f, boatYaw, 0.06f * side);
                    AddChild(boat);
                    Track(boat);
                    Prop(boat, 160f);
                    Solids.BoxIn(boat.Transform, new Vector3(0f, 0.25f, 0f), new Vector3(1.4f, 0.6f, 3.6f));
                    boats++;
                    Count("rowboat");
                    x = 1000f;
                    break;
                }
            }
    }

    // ------------------------------------------------------------------ landmarks
    /// <summary>
    /// The map's one-of-a-kind sights ("kind,x,z[,yaw[,size]]"). Huts, platforms and rock islets stand in the water;
    /// the rest on the ground. Without a yaw they face the middle of the lake.
    /// </summary>
    private void BuildLandmarks(LocationData loc)
    {
        foreach (var entry in loc.Landmarks)
        {
            var parts = entry.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                continue;
            string kind = parts[0];
            var n = LocationData.Numbers(parts[1]);
            if (n.Length < 2)
                continue;
            float x = n[0], z = n[1];
            float depth = _lake.DepthAt(loc, x, z);
            float ground = _lake.HeightAt(loc, x, z);
            float size = n.Length >= 4 ? n[3] : depth;
            var building = BuildingFactory.Landmark(kind, size);
            float y = kind switch
            {
                "stilthouse" or "platforms" => 0f,
                "rocks" => ground > 0f ? ground - 0.4f : ground * 0.3f,
                "waterfall" => MathF.Max(ground, 0f) - 0.4f,
                _ => ground,
            };
            float yaw = n.Length >= 3
                ? Mathf.DegToRad(n[2])
                : MathF.Atan2(-x, loc.OutlineCz - z);
            building.Position = new Vector3(x, y, z);
            building.Rotation = new Vector3(0f, yaw, 0f);
            AddChild(building);
            Track(building);
            _buildings.Add(building);
            Count("landmark_" + kind);
            foreach (var spot in building.TreeSpots)
                _landmarkTrees.Add(building.Transform * spot);
            // Keep trees, plants and the local anglers off the landmark: a long dike gets a row of circles.
            if (kind == "dike")
            {
                float length = MathF.Max(size, 20f);
                var along = new Vector2(MathF.Cos(yaw), -MathF.Sin(yaw));
                for (float t = -length / 2f; t <= length / 2f; t += 4f)
                    _reserved.Add((new Vector2(x, z) + along * t, 6f));
            }
            else if (kind is not ("platforms" or "rocks" or "stilthouse"))
                _reserved.Add((new Vector2(x, z), 8f));
            if (building.Title != "" && kind != "platforms")
                Markers.Add(new MapMarker(new Vector2(x, z), building.Title, new Color(0.8f, 0.78f, 0.7f)));
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
    /// <summary>Spots kept free of trees and plants (the notice board, the campfires): centre and radius.</summary>
    private readonly List<(Vector2 At, float Radius)> _reserved = new();

    private bool Clear(LocationData loc, float x, float z, float padMargin = 2.5f, float pathMargin = 2.2f)
    {
        if (MathF.Abs(x) < 14f && z > -8f && z < 14f)
            return false;
        foreach (var (at, radius) in _reserved)
            if (new Vector2(x, z).DistanceTo(at) < radius)
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

    /// <summary>
    /// Small plants in square cells, each its own instance with a visibility range: dense around the angler, nothing
    /// drawn far away where they would not be seen anyway.
    /// </summary>
    private void AddMultiMeshChunked(Mesh mesh, List<Transform3D> transforms, List<Color> colors, Material? material, float range,
        float cell = 36f, bool shadows = true)
    {
        var groups = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < transforms.Count; i++)
        {
            var o = transforms[i].Origin;
            var key = ((int)MathF.Floor(o.X / cell), (int)MathF.Floor(o.Z / cell));
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = new List<int>();
            list.Add(i);
        }
        foreach (var ((cx, cz), ids) in groups)
        {
            var centre = new Vector3((cx + 0.5f) * cell, 0f, (cz + 0.5f) * cell);
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = ids.Count,
            };
            for (int k = 0; k < ids.Count; k++)
            {
                var t = transforms[ids[k]];
                mm.SetInstanceTransform(k, new Transform3D(t.Basis, t.Origin - centre));
                mm.SetInstanceColor(k, colors[ids[k]]);
            }
            var inst = new MultiMeshInstance3D
            {
                Multimesh = mm, MaterialOverride = material ?? VertexColorMaterial(), Position = centre,
                VisibilityRangeEnd = range * GraphicsQuality.FoliageRange, VisibilityRangeEndMargin = cell * 0.25f,
                CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(inst);
            Track(inst);
            _ranged.Add((inst, range));
        }
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
    // Leaf colours as a forest shows them in summer light: deep and a little grey (the old ones were bright lime:
    // the leaf atlas already brings the leaves' own green).
    private static Color CrownColor(string kind) => kind switch
    {
        "spruce" => new Color(0.17f, 0.3f, 0.2f),
        "oak" => new Color(0.22f, 0.35f, 0.12f),
        "birch" => new Color(0.36f, 0.5f, 0.2f),
        "pine" => new Color(0.2f, 0.33f, 0.19f),
        "willow" => new Color(0.33f, 0.44f, 0.2f),
        _ => new Color(0.19f, 0.34f, 0.13f),
    };

    private readonly List<(float X, float Z, string Kind)> _treeSpots = new();
    private readonly List<(float X, float Z)> _allTrees = new();

    /// <summary>Feet of the trees the landmarks ask for (world positions; planted with the forest's trees).</summary>
    private readonly List<Vector3> _landmarkTrees = new();

    /// <summary>The trunk's radius at the foot for a kind of tree at scale 1 (TreeFactory.Get's trunks).</summary>
    private static float TrunkRadius(string kind) => kind switch
    {
        "spruce" => 0.17f, "oak" => 0.2f, "birch" => 0.1f, "pine" => 0.18f, "willow" => 0.24f, _ => 0.15f,
    };

    private void ScatterTrees(LocationData loc, RandomNumberGenerator rng)
    {
        string[] kinds = { "spruce", "oak", "birch", "pine", "willow" };
        var trunks = kinds.ToDictionary(k => k, _ => new List<Transform3D>());
        var trunkColors = kinds.ToDictionary(k => k, _ => new List<Color>());
        var crowns = kinds.ToDictionary(k => k, _ => new List<Transform3D>());
        var crownColors = kinds.ToDictionary(k => k, _ => new List<Color>());
        _treeSpots.Clear();
        _allTrees.Clear();

        void Plant(string kind, float x, float z, float h, float s, float yaw, float tint, bool understorey)
        {
            var meshes = TreeFactory.Get(kind);
            var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(s, s, s));
            var pos = new Vector3(x, h - 0.1f, z);
            trunks[kind].Add(new Transform3D(basis, pos + new Vector3(0, meshes.TrunkLift * s, 0)));
            trunkColors[kind].Add(Tint(meshes.TrunkColor, tint * 0.5f));
            crowns[kind].Add(new Transform3D(basis, pos + new Vector3(0, meshes.CrownLift * s, 0)));
            crownColors[kind].Add(Tint(CrownColor(kind), tint));
            if (understorey)
                _treeSpots.Add((x, z, kind));
            _allTrees.Add((x, z));
            // The trunk is solid where the angler can walk (beyond the edge nobody reaches it).
            if (_lake.PlayDistance(x, z) < 2f)
                Solids.Cylinder(new Vector3(x, h - 0.3f, z), TrunkRadius(kind) * s * 1.1f, 3f);
            Count("tree_" + kind);
        }

        string Choose(float[] weights, float pick)
        {
            float total = weights.Sum();
            float roll = pick * total;
            for (int k = 0; k < kinds.Length; k++)
            {
                roll -= weights[k];
                if (roll <= 0f)
                    return kinds[k];
            }
            return kinds[0];
        }

        // The woods inside the map, where the map's forest noise says so.
        float threshold = -0.08f + (1f - loc.TreeDensity) * 0.5f;
        int cap = (int)(1550 * Math.Min(loc.TreeDensity, 1.2f));
        int placed = 0;
        for (int i = 0; i < 20000 && placed < cap; i++)
        {
            float x = rng.RandfRange(LakeBed.XMin, LakeBed.XMax);
            float z = rng.RandfRange(LakeBed.ZMin, LakeBed.ZMax);
            float s = rng.RandfRange(0.7f, 1.7f);
            float tint = rng.RandfRange(-0.05f, 0.05f);
            float pick = rng.Randf();
            float yaw = rng.Randf() * Mathf.Tau;
            if (_lake.ForestDensity(x, z) < threshold || _lake.PlayDistance(x, z) > -34f)
                continue;
            float h = _lake.HeightAt(loc, x, z);
            if (h < 1.2f || !Clear(loc, x, z))
                continue;
            float inside = _lake.InsideMetres(loc, x, z);

            // Weighted choice of the kind, nudged by where the tree stands.
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
            }
            if (weights.Sum() <= 0f)
                continue;
            Plant(Choose(weights, pick), x, z, h, s, yaw, tint, true);
            placed++;
        }

        // Trees the landmarks ask for (the row along the fish-pond dike): tall birches and oaks, the same trees as the
        // forest's, standing where the landmark says.
        foreach (var foot in _landmarkTrees)
        {
            float pick = rng.Randf();
            string kind = pick < 0.65f ? "birch" : "oak";
            Plant(kind, foot.X, foot.Z, foot.Y + 0.1f, rng.RandfRange(1.15f, 1.45f), rng.Randf() * Mathf.Tau, rng.RandfRange(-0.1f, 0.1f), false);
        }

        // The forest at the edge of the map: dense at the foot of the mountains, where the angler is stopped (so the
        // edge is a thicket, not an invisible wall), then thinning up the slopes to the tree line.
        float treeLine = MathF.Min(loc.SnowLine - 3f, loc.RimHeight * 0.75f);
        var rimWeights = new float[kinds.Length];
        for (int k = 0; k < kinds.Length; k++)
            rimWeights[k] = kinds[k] switch
            {
                "spruce" => loc.TreeShare("spruce") + 0.35f,
                "pine" => loc.TreeShare("pine") + 0.2f,
                "birch" => loc.TreeShare("birch") + 0.1f,
                "oak" => loc.TreeShare("oak") * 0.6f + 0.04f,
                _ => 0f,
            };
        const float cell = 3.4f;
        int rim = 0;
        for (float gz = LakeBed.ZMin - 140f; gz <= LakeBed.ZMax + 140f; gz += cell)
            for (float gx = LakeBed.XMin - 140f; gx <= LakeBed.XMax + 140f; gx += cell)
            {
                float x = gx + rng.Randf() * cell, z = gz + rng.Randf() * cell;
                float s = rng.RandfRange(0.9f, 2f), yaw = rng.Randf() * Mathf.Tau, tint = rng.RandfRange(-0.06f, 0.05f), pick = rng.Randf();
                float keep = rng.Randf();
                float d = _lake.PlayDistance(x, z);
                if (d < -34f || d > 140f || (d > 2f && keep > 0.3f) || (d < -20f && keep > 0.6f))
                    continue;
                float h = _lake.HeightAt(loc, x, z);
                if (h < 1.2f || h > treeLine - keep * 8f || _lake.InsideMetres(loc, x, z) > -3f || (d < 0f && !Clear(loc, x, z)))
                    continue;
                float slope = MathF.Abs(_lake.HeightAt(loc, x + 1.5f, z) - h) + MathF.Abs(_lake.HeightAt(loc, x, z + 1.5f) - h);
                if (slope > 1.4f)
                    continue;
                Plant(Choose(rimWeights, pick), x, z, h, s, yaw, tint, d < 0f && keep < 0.25f);
                rim++;
            }
        Count("rim_tree", rim);

        foreach (var kind in kinds)
        {
            var meshes = TreeFactory.Get(kind);
            var far = TreeFactory.GetFar(kind);
            if (meshes.Trunk != null)
                AddTrees(meshes.Trunk, far.Trunk!, trunks[kind], trunkColors[kind], TreeFactory.BarkMaterial(kind));
            AddTrees(meshes.Crown, far.Crown, crowns[kind], crownColors[kind], TreeFactory.FoliageMaterial(meshes.Needles));
        }
    }

    /// <summary>Distance (m) up to which trees are drawn in full; beyond it their far versions (fewer, larger leaf cards).</summary>
    private const float TreeNearRange = 130f;

    private readonly List<(GeometryInstance3D Near, GeometryInstance3D Far)> _treeLods = new();

    /// <summary>
    /// Trees in 64 m cells, each cell twice: the full tree near the angler and the far version beyond
    /// <see cref="TreeNearRange"/> (shortened by the quality setting), so thousands of trees can fill the horizon.
    /// </summary>
    private void AddTrees(Mesh near, Mesh far, List<Transform3D> transforms, List<Color> colors, Material material)
    {
        const float cell = 64f;
        var groups = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < transforms.Count; i++)
        {
            var o = transforms[i].Origin;
            var key = ((int)MathF.Floor(o.X / cell), (int)MathF.Floor(o.Z / cell));
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = new List<int>();
            list.Add(i);
        }
        foreach (var ((cx, cz), ids) in groups)
        {
            var centre = new Vector3((cx + 0.5f) * cell, 0f, (cz + 0.5f) * cell);
            MultiMesh Make(Mesh mesh)
            {
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = ids.Count };
                for (int k = 0; k < ids.Count; k++)
                {
                    var t = transforms[ids[k]];
                    mm.SetInstanceTransform(k, new Transform3D(t.Basis, t.Origin - centre));
                    mm.SetInstanceColor(k, colors[ids[k]]);
                }
                return mm;
            }
            float range = TreeNearRange * GraphicsQuality.FoliageRange;
            var nearInst = new MultiMeshInstance3D
            {
                Multimesh = Make(near), MaterialOverride = material, Position = centre,
                VisibilityRangeEnd = range, VisibilityRangeEndMargin = 12f,
            };
            var farInst = new MultiMeshInstance3D
            {
                Multimesh = Make(far), MaterialOverride = material, Position = centre,
                VisibilityRangeBegin = range, VisibilityRangeBeginMargin = 12f,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(nearInst);
            AddChild(farInst);
            Track(nearInst);
            Track(farInst);
            _treeLods.Add((nearInst, farInst));
        }
    }

    // ------------------------------------------------------------------ bushes, flowers, grass, the understorey
    private void ScatterGroundCover(LocationData loc, RandomNumberGenerator rng)
    {
        var bushes = new List<Transform3D>(); var bushColors = new List<Color>();
        var flowers = new List<Transform3D>(); var flowerColors = new List<Color>();
        var tufts = new List<Transform3D>(); var tuftColors = new List<Color>();
        Color[] palette =
        {
            new(0.97f, 0.97f, 0.92f), new(0.98f, 0.85f, 0.2f), new(0.92f, 0.4f, 0.6f), new(0.6f, 0.4f, 0.85f), new(0.9f, 0.25f, 0.2f),
        };
        int maxBushes = (int)(900 * loc.BushDensity), maxFlowers = (int)(3400 * loc.FlowerDensity), maxTufts = (int)(7000 * Math.Max(loc.BushDensity, 0.7f));
        float highest = MathF.Min(loc.SnowLine - 2f, 40f);
        for (int i = 0; i < 90000 && (bushes.Count < maxBushes || flowers.Count < maxFlowers || tufts.Count < maxTufts); i++)
        {
            float x = rng.RandfRange(LakeBed.XMin, LakeBed.XMax);
            float z = rng.RandfRange(LakeBed.ZMin, LakeBed.ZMax);
            float kindRoll = rng.Randf();
            float s = rng.RandfRange(0.7f, 1.4f);
            float yaw = rng.Randf() * Mathf.Tau;
            float edge = _lake.PlayDistance(x, z);
            if (edge > 3f)
                continue;
            // Thick undergrowth in the forest at the map's edge.
            if (edge > -30f)
                kindRoll *= 0.4f;
            float h = _lake.HeightAt(loc, x, z);
            if (h < 1.0f || h > highest || !Clear(loc, x, z, 1.5f, 1.6f))
                continue;
            float patch = _lake.Noise(x * 3f + 40f, z * 3f - 70f);   // flowers and grass grow in patches
            if (kindRoll < 0.12f && bushes.Count < maxBushes)
            {
                bushes.Add(new Transform3D(new Basis(Vector3.Up, yaw).Scaled(new Vector3(s, s, s)), new Vector3(x, h, z)));
                bushColors.Add(Tint(CrownColor("bush"), rng.RandfRange(-0.05f, 0.05f)));
            }
            else if (kindRoll < 0.4f && patch > 0.0f && flowers.Count < maxFlowers)
            {
                flowers.Add(new Transform3D(new Basis(Vector3.Up, yaw).Scaled(new Vector3(s, s * rng.RandfRange(0.7f, 1.3f), s)), new Vector3(x, h - 0.03f, z)));
                flowerColors.Add(palette[rng.RandiRange(0, palette.Length - 1)]);
            }
            else if (tufts.Count < maxTufts && patch > -0.35f)
            {
                tufts.Add(new Transform3D(new Basis(Vector3.Up, yaw).Scaled(new Vector3(s, s * 1.2f, s)), new Vector3(x, h - 0.05f, z)));
                tuftColors.Add(Tint(loc.GrassColor.Lerp(new Color(0.5f, 0.65f, 0.25f), 0.35f), rng.RandfRange(-0.06f, 0.06f)));
            }
        }

        // The forest floor: hazel shrubs and ferns in the shade of the trees.
        var hazels = new List<Transform3D>(); var hazelColors = new List<Color>();
        var ferns = new List<Transform3D>(); var fernColors = new List<Color>();
        foreach (var (tx, tz, kind) in _treeSpots)
        {
            if (kind == "willow")
                continue;
            if (rng.Randf() < 0.35f * loc.BushDensity)
            {
                float a = rng.Randf() * Mathf.Tau, d = rng.RandfRange(2.2f, 4.5f);
                float x = tx + MathF.Cos(a) * d, z = tz + MathF.Sin(a) * d;
                float h = _lake.HeightAt(loc, x, z);
                if (h > 1.1f && Clear(loc, x, z, 1.5f, 1.6f))
                {
                    float sc = rng.RandfRange(0.8f, 1.5f);
                    hazels.Add(new Transform3D(new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(new Vector3(sc, sc * rng.RandfRange(0.9f, 1.3f), sc)),
                        new Vector3(x, h + 0.7f * sc - 0.15f, z)));
                    hazelColors.Add(Tint(new Color(0.26f, 0.42f, 0.13f), rng.RandfRange(-0.05f, 0.05f)));
                }
            }
            int nf = kind is "pine" ? 1 : rng.RandiRange(1, 4);
            for (int k = 0; k < nf; k++)
            {
                float a = rng.Randf() * Mathf.Tau, d = rng.RandfRange(0.8f, 3.5f);
                float x = tx + MathF.Cos(a) * d, z = tz + MathF.Sin(a) * d;
                float h = _lake.HeightAt(loc, x, z);
                if (h < 1.1f || !Clear(loc, x, z, 1.5f, 1.6f))
                    continue;
                float sc = rng.RandfRange(0.7f, 1.3f);
                ferns.Add(new Transform3D(new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(new Vector3(sc, sc, sc)), new Vector3(x, h - 0.03f, z)));
                fernColors.Add(Tint(new Color(0.28f, 0.48f, 0.15f), rng.RandfRange(-0.06f, 0.04f)));
            }
        }

        AddMultiMeshChunked(TreeFactory.Get("bush").Crown, bushes, bushColors, TreeFactory.FoliageMaterial(false), 220f);
        AddMultiMeshChunked(TreeFactory.Get("hazel").Crown, hazels, hazelColors, TreeFactory.FoliageMaterial(false), 220f);
        AddMultiMeshChunked(TreeFactory.Wildflower(), flowers, flowerColors, TreeFactory.FlowerMaterial(), 80f, shadows: false);
        AddMultiMeshChunked(TreeFactory.Tuft(), tufts, tuftColors, TreeFactory.GrassMaterial(), 90f, shadows: false);
        AddMultiMeshChunked(TreeFactory.Fern(), ferns, fernColors, TreeFactory.FernMaterial(), 90f, shadows: false);
        Count("bush", bushes.Count);
        Count("hazel", hazels.Count);
        Count("flower", flowers.Count);
        Count("tuft", tufts.Count);
        Count("fern", ferns.Count);
    }

    // ------------------------------------------------------------------ forest pickings
    /// <summary>Mushrooms and blueberry bushes in the woods on the map (picked with E, grown back after a day).</summary>
    public List<Props.ForagePatch> Forage { get; } = new();

    /// <summary>The most patches on a map, and how far apart they keep (m).</summary>
    private const int ForageCount = 42;
    private const float ForageSpacing = 7f;

    /// <summary>
    /// Porcini by the trunks, chanterelles in the moss and blueberries in the undergrowth: spots round the trees inside
    /// the playable map (not on paths or by buildings), a few metres apart.
    /// </summary>
    private void ScatterForage(LocationData loc, RandomNumberGenerator rng)
    {
        Forage.Clear();
        var core = new Core.SystemRng((int)rng.Randi());
        var spots = new List<Vector2>();
        var trees = _treeSpots.Where(t => t.Kind != "willow").OrderBy(_ => rng.Randf()).ToList();
        foreach (var (tx, tz, kind) in trees)
        {
            if (spots.Count >= ForageCount)
                break;
            float a = rng.Randf() * Mathf.Tau, d = rng.RandfRange(0.9f, 2.8f);
            float x = tx + MathF.Cos(a) * d, z = tz + MathF.Sin(a) * d;
            if (!_lake.IsWalkable(x, z) || _lake.PlayDistance(x, z) > -6f)
                continue;
            float h = _lake.HeightAt(loc, x, z);
            if (h < 1.3f || !Clear(loc, x, z, 2f, 1.8f) || spots.Any(p => p.DistanceTo(new Vector2(x, z)) < ForageSpacing))
                continue;
            if (_lake.GroundKindAt(x, z) is not (Core.Camp.GroundKind.Forest or Core.Camp.GroundKind.Meadow))
                continue;
            float nearTree = d < 1.6f ? 1f : 0.3f, moss = kind is "spruce" or "pine" ? 0.8f : 0.2f;
            var patchKind = Core.Camp.Foraging.KindFor(nearTree, moss, core);
            var patch = Props.ForagePatch.Create(patchKind, spots.Count + (int)(x * 13f));
            patch.Position = new Vector3(x, h - 0.01f, z);
            AddChild(patch);
            Track(patch);
            Prop(patch, 60f);
            Forage.Add(patch);
            spots.Add(new Vector2(x, z));
            var spot = new Interactable { Kind = InteractKind.Forage, Title = patch.Title, Radius = 1.7f, Position = patch.Position, Target = patch };
            AddChild(spot);
            Track(spot);
            Interactables.Add(spot);
            Count("forage_" + patchKind.ToString().ToLowerInvariant());
        }
    }

    /// <summary>Grows back the picked patches whose day has passed.</summary>
    public void RegrowForage(float now)
    {
        foreach (var p in Forage)
            if (GodotObject.IsInstanceValid(p))
                p.Update(now);
    }

    // ------------------------------------------------------------------ reeds and rocks
    private void ScatterReeds(LocationData loc, RandomNumberGenerator rng)
    {
        List<Transform3D> reeds = new(), heads = new();
        List<Color> colors = new(), headColors = new();
        int maxReeds = (int)(4200 * Math.Min(loc.ReedDensity, 2f));
        // Shorelines to sample: the main lake, its bays (x, z, rx, rz) and the islands (sampled just outside them).
        var shores = new List<(float X, float Z, float Rx, float Rz, bool Island)> { (0f, loc.OutlineCz, loc.OutlineRx, loc.OutlineRz, false) };
        foreach (var entry in loc.Lobes)
            if (LocationData.Numbers(entry) is { Length: >= 4 } l)
                shores.Add((l[0], l[1], l[2], l[3], false));
        foreach (var entry in loc.Islands)
            if (LocationData.Numbers(entry) is { Length: >= 3 } i)
                shores.Add((i[0], i[1], i[2], i[2], true));
        for (int tries = 0; reeds.Count < maxReeds && tries < 90000; tries++)
        {
            // Sample along a shoreline (a point on its outline, nudged into the water) instead of the whole map.
            var shore = shores.Count == 1 || rng.Randf() < 0.5f ? shores[0] : shores[rng.RandiRange(1, shores.Count - 1)];
            float angle = rng.Randf() * Mathf.Tau;
            float shrink = shore.Island ? rng.RandfRange(1f, 1.6f) : rng.RandfRange(0.85f, 1.04f);
            float x = shore.X + shore.Rx * shrink * MathF.Cos(angle);
            float z = shore.Z + shore.Rz * shrink * MathF.Sin(angle);
            if (MathF.Abs(x) < 9f && z > -14f)
                continue;
            float d = _lake.DepthAt(loc, x, z);
            if (d < 0.15f || d > 0.9f)
                continue;
            if (_lake.ForestDensity(x * 1.7f, z * 1.7f) < 0f)
                continue;
            for (int k = 0; k < 5; k++)
            {
                float rx = x + rng.RandfRange(-0.8f, 0.8f);
                float rz = z + rng.RandfRange(-0.8f, 0.8f);
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
        AddMultiMeshChunked(TreeFactory.Reed(), reeds, colors, null, 260f, 48f, shadows: false);    // shadows of stalks this thin are barely seen
        AddMultiMeshChunked(TreeFactory.Cattail(), heads, headColors, null, 200f, 48f, shadows: false);
        Count("reed", reeds.Count);
    }

    /// <summary>Points along the shorelines (the lake, its bays, its islands), with their outlines.</summary>
    private List<(float X, float Z, float Rx, float Rz, bool Island)> Shores(LocationData loc)
    {
        var shores = new List<(float X, float Z, float Rx, float Rz, bool Island)> { (0f, loc.OutlineCz, loc.OutlineRx, loc.OutlineRz, false) };
        foreach (var entry in loc.Lobes)
            if (LocationData.Numbers(entry) is { Length: >= 4 } l)
                shores.Add((l[0], l[1], l[2], l[3], false));
        foreach (var entry in loc.Islands)
            if (LocationData.Numbers(entry) is { Length: >= 3 } i)
                shores.Add((i[0], i[1], i[2], i[2], true));
        return shores;
    }

    /// <summary>Near the pier the water is kept open (the angler casts from there).</summary>
    private static bool NearPier(float x, float z) => MathF.Abs(x) < 12f && z > -18f;

    // ------------------------------------------------------------------ the water's edge: rushes and yellow irises
    private void ScatterShoreline(LocationData loc, RandomNumberGenerator rng)
    {
        var rushes = new List<Transform3D>(); var rushColors = new List<Color>();
        var irises = new List<Transform3D>(); var irisColors = new List<Color>();
        var irisFlowers = new List<Transform3D>(); var irisFlowerColors = new List<Color>();
        var shores = Shores(loc);
        int maxRushes = (int)(1400 * Math.Clamp(loc.ReedDensity, 0.3f, 2f)), maxIrises = (int)(320 * Math.Max(loc.ReedDensity, 0.2f));
        for (int tries = 0; (rushes.Count < maxRushes || irises.Count < maxIrises) && tries < 60000; tries++)
        {
            var shore = shores.Count == 1 || rng.Randf() < 0.6f ? shores[0] : shores[rng.RandiRange(1, shores.Count - 1)];
            float angle = rng.Randf() * Mathf.Tau;
            float shrink = shore.Island ? rng.RandfRange(0.95f, 1.25f) : rng.RandfRange(0.93f, 1.08f);
            float x = shore.X + shore.Rx * shrink * MathF.Cos(angle);
            float z = shore.Z + shore.Rz * shrink * MathF.Sin(angle);
            if (NearPier(x, z))
                continue;
            float inside = _lake.InsideMetres(loc, x, z);
            float depth = _lake.DepthAt(loc, x, z);
            if (inside > 0f && depth > 0f && depth < 0.35f && rushes.Count < maxRushes)
            {
                // Rushes stand in the first hand's breadth of water.
                float sc = rng.RandfRange(0.7f, 1.3f);
                rushes.Add(new Transform3D(new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(new Vector3(sc, sc * rng.RandfRange(0.9f, 1.4f), sc)),
                    new Vector3(x, -depth, z)));
                rushColors.Add(Tint(new Color(0.22f, 0.34f, 0.13f), rng.RandfRange(-0.04f, 0.04f)));
            }
            else if (inside > -2.5f && inside < 0.4f && irises.Count < maxIrises && _lake.Noise(x * 2f - 30f, z * 2f + 11f) > 0.1f
                     && Clear(loc, x, z, 1.5f, 1.6f))
            {
                // Yellow flag irises on the wet bank, in clumps.
                float h = MathF.Max(_lake.HeightAt(loc, x, z), -depth);
                float sc = rng.RandfRange(0.8f, 1.2f);
                var b = new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(new Vector3(sc, sc, sc));
                irises.Add(new Transform3D(b, new Vector3(x, h - 0.02f, z)));
                irisColors.Add(Tint(new Color(0.3f, 0.45f, 0.18f), rng.RandfRange(-0.04f, 0.04f)));
                if (rng.Randf() < 0.7f)
                {
                    irisFlowers.Add(new Transform3D(new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(new Vector3(sc, sc, sc)), new Vector3(x + 0.05f, h - 0.02f, z)));
                    irisFlowerColors.Add(new Color(0.98f, 0.84f, 0.15f));
                }
            }
        }
        AddMultiMeshChunked(TreeFactory.Rush(), rushes, rushColors, TreeFactory.GrassMaterial(), 140f, 48f, shadows: false);
        AddMultiMeshChunked(TreeFactory.IrisLeaves(), irises, irisColors, TreeFactory.GrassMaterial(), 120f, 48f, shadows: false);
        AddMultiMeshChunked(TreeFactory.Wildflower(3, 0.85f, 0.07f, 0.045f), irisFlowers, irisFlowerColors, TreeFactory.FlowerMaterial(), 120f, 48f, shadows: false);
        Count("rush", rushes.Count);
        Count("iris", irises.Count);
    }

    // ------------------------------------------------------------------ on the water: lilies and pondweed
    /// <summary>
    /// Trees fallen from the wooded banks into the water: from tree spots near the shore, a few metres out into water
    /// 0.6-3 m deep, at least 12 m apart and away from the pier. A grey, weathered branch or two pokes out of the water
    /// over each, so the angler can see where the snags are.
    /// </summary>
    /// <summary>
    /// Two or three local anglers along the bank, well away from the pier and each other: each sits on a log behind a
    /// hazel rod rest, the rod pointing out over the water, its float lying 9-13 m out. They fish the water in front of
    /// them (Main lets them catch now and then, which leaves that swim fished: FishingRod.Swim).
    /// </summary>
    /// <summary>The ground within <paramref name="radius"/> of (x, z) stays within <paramref name="tolerance"/> m of its
    /// height there (checked over a ring and halfway in).</summary>
    private bool Level(LocationData loc, float x, float z, float radius, float tolerance)
    {
        float h = _lake.HeightAt(loc, x, z);
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.Tau / 8f;
            foreach (float r in new[] { radius * 0.5f, radius })
                if (MathF.Abs(_lake.HeightAt(loc, x + MathF.Cos(a) * r, z + MathF.Sin(a) * r) - h) > tolerance)
                    return false;
        }
        return true;
    }

    private readonly List<(Props.Npc? Angler, Node3D? Rod, MeshInstance3D Float)> _anglerParts = new();

    /// <summary>
    /// A local angler gets a bite and lands a fish: the float goes under, the angler gets up to land it, and a little
    /// later the float is back out on the water.
    /// </summary>
    public void AnglerCatches(int index)
    {
        if (index < 0 || index >= _anglerParts.Count)
            return;
        var (angler, rod, bob) = _anglerParts[index];
        if (!IsInstanceValid(bob))
            return;
        AnglerCaught?.Invoke(index);
        // Catches can come seconds apart (the stool, PassTime): a running show is left alone, and the float and the
        // rod always return to the rest taken before the first one (never to a raised or sunken pose).
        if (bob.GetMeta("catching", false).AsBool())
            return;
        bob.SetMeta("catching", true);
        if (!bob.HasMeta("rest"))
            bob.SetMeta("rest", bob.Position);
        if (rod != null && IsInstanceValid(rod) && !rod.HasMeta("held"))
            rod.SetMeta("held", rod.Transform);
        var rest = bob.GetMeta("rest").AsVector3();
        var tween = bob.CreateTween();
        tween.TweenProperty(bob, "position", rest + new Vector3(0f, -0.25f, 0f), 0.35f);
        tween.TweenCallback(Callable.From(() =>
        {
            bob.Visible = false;
            if (angler != null && IsInstanceValid(angler))
                angler.PlayGesture("Interact");
            // A standing angler (a statue model) strikes: the rod swings up and comes back down.
            if (rod != null && IsInstanceValid(rod))
            {
                var held = rod.GetMeta("held").AsTransform3D();
                var up = new Transform3D(new Basis(Vector3.Right, -Mathf.DegToRad(28f)), Vector3.Zero);
                var hands = rod.GetMeta("hands", Vector3.Zero).AsVector3();
                var raised = new Transform3D(Basis.Identity, hands) * up * new Transform3D(Basis.Identity, -hands) * held;
                var swing = rod.CreateTween();
                swing.TweenProperty(rod, "transform", raised, 0.25f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                swing.TweenInterval(4f);
                swing.TweenProperty(rod, "transform", held, 1.2f).SetTrans(Tween.TransitionType.Sine);
            }
        }));
        tween.TweenInterval(6f);
        tween.TweenCallback(Callable.From(() => { bob.Position = rest; bob.Visible = true; bob.SetMeta("catching", false); }));
    }

    /// <summary>A local angler landed a fish (for the sound and tests).</summary>
    public event Action<int>? AnglerCaught;

    /// <summary>The Meshy fisherman's hands (model units: feet at y = 0, facing +Z) and the rod's grip on its reel seat.</summary>
    private static readonly Vector3 FishermanHands = new(0.02f, 1.087f, 0.244f);
    private const float FishermanModelHeight = 1.904f, RodModelLength = 1.904f, RodGripX = 0.38f, RodBlankY = 0.13f, RodBottomY = -0.162f;

    /// <summary>
    /// The standing angler (assets/models/meshy_fisherman.glb, a posed statue: the user's Meshy model) holding the
    /// Meshy spinning rod (meshy_spinning_rod.glb) by its reel seat, the tip out over the water rising 25 degrees.
    /// Returns the rod (for its strike), or null without the models or a screen (the sitting angler stands in).
    /// </summary>
    private static Node3D? StandingAngler(Node3D root)
    {
        float height = 1.8f;
        var man = Props.PropAsset.Create("meshy_fisherman", height);
        if (man == null)
            return null;
        float rodLength = 1.7f, k = rodLength / RodModelLength;     // 30 % under the first 2.4 m (the user found it too big)
        var rod = Props.PropAsset.Create("meshy_spinning_rod", scale: k);
        if (rod == null)
        {
            man.Free();
            return null;
        }
        root.AddChild(man);
        // The rod's model lies along X (butt at +X, tip at -X) with the reel hanging at -Y; turned so the tip points
        // at the water (+Z) rising 25 degrees, its reel seat in his hands.
        var hands = FishermanHands * (height / FishermanModelHeight);
        var tipWay = new Vector3(0f, MathF.Sin(Mathf.DegToRad(25f)), MathF.Cos(Mathf.DegToRad(25f)));
        var x = -tipWay;
        var y = (Vector3.Up - tipWay * Vector3.Up.Dot(tipWay)).Normalized();
        var basis = new Basis(x, y, x.Cross(y));
        var grip = new Vector3(RodGripX * k, (RodBlankY - RodBottomY) * k, 0f);   // where PropAsset puts the seat
        rod.Transform = new Transform3D(basis, hands - basis * grip);
        rod.SetMeta("hands", hands);
        root.AddChild(rod);
        return rod;
    }

    private void BuildAnglers(LocationData loc)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(loc.DisplayName.Length * 7919 + 17) };
        var shores = Shores(loc);
        var main = shores[0];
        int want = 3;
        for (int tries = 0; Anglers.Count < want && tries < 600; tries++)
        {
            float angle = rng.Randf() * Mathf.Tau;
            float wx = main.X + main.Rx * 0.8f * MathF.Cos(angle), wz = main.Z + main.Rz * 0.8f * MathF.Sin(angle);
            if (_lake.DepthAt(loc, wx, wz) < 1f)
                continue;
            // Walk out from the water to the bank.
            var dir = new Vector2(MathF.Cos(angle) * main.Rx, MathF.Sin(angle) * main.Rz).Normalized();
            Vector2? edge = null;
            for (float d = 0f; d < 60f; d += 0.5f)
            {
                float x = wx + dir.X * d, z = wz + dir.Y * d;
                if (_lake.DepthAt(loc, x, z) <= 0f)
                {
                    edge = new Vector2(x, z);
                    break;
                }
            }
            if (edge is not Vector2 waterline || MathF.Abs(waterline.X) < 40f && waterline.Y > -40f)
                continue;
            // The first level, walkable spot 1.5-7 m up the bank: the terrain mesh (a 2.5 m grid) cuts corners on a
            // steep bank, which would bury the angler's legs or leave his log hanging.
            Vector2? found = null;
            for (float back = 1.5f; back <= 7f && found == null; back += 0.5f)
            {
                var p = waterline + dir * back;
                if (_lake.IsWalkable(p.X, p.Y) && Clear(loc, p.X, p.Y, 3f, 2.5f) && _lake.HeightAt(loc, p.X, p.Y) <= 2.5f
                    && Level(loc, p.X, p.Y, 2.5f, 0.3f))
                    found = p;
            }
            if (found is not Vector2 seat)
                continue;
            bool crowded = false;
            foreach (var (s, _) in Anglers)
                if (new Vector2(s.X, s.Z).DistanceTo(seat) < 60f)
                    crowded = true;
            if (crowded)
                continue;
            var toWater = -dir;
            float reach = rng.RandfRange(9f, 13f);
            var floatAt = seat + toWater * (seat.DistanceTo(waterline) + reach);
            if (_lake.DepthAt(loc, floatAt.X, floatAt.Y) < 0.8f)
                continue;
            float h = _lake.HeightAt(loc, seat.X, seat.Y);
            var root = new Node3D { Name = "Angler", Position = new Vector3(seat.X, h, seat.Y) };
            root.Rotation = new Vector3(0f, MathF.Atan2(toWater.X, toWater.Y), 0f);
            // The angler from the user's Meshy models: a man standing on the bank with the spinning rod in his hands.
            if (StandingAngler(root) is { } standingRod)
            {
                var standingBob = new MeshInstance3D
                {
                    Mesh = new CapsuleMesh { Radius = 0.025f, Height = 0.16f, RadialSegments = 8, Rings = 2 },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.18f, 0.12f), Roughness = 0.5f },
                    Position = new Vector3(floatAt.X, 0.03f, floatAt.Y),
                };
                AddChild(standingBob);
                Track(standingBob);
                AddChild(root);
                Track(root);
                Prop(root, 160f);
                _anglerParts.Add((null, standingRod, standingBob));
                Solids.BoxIn(root.Transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.55f, 1.8f, 0.45f));
                _reserved.Add((seat, 3.5f));
                Anglers.Add((root.Position, new Vector3(floatAt.X, 0f, floatAt.Y)));
                Count("angler");
                continue;
            }
            var angler = Props.People.Create(Props.Role.Fisherman, 60 + Anglers.Count, "Sitting_Idle_Loop", new[] { "Sitting_Talking_Loop" });
            // The log lies across, as thick as his seat is high; he sits with his hips over its axis, facing the water.
            var log = Props.CampProps.SeatLog(1.1f, Anglers.Count + 11, angler.SeatLogRadius, even: true);
            root.AddChild(log);
            angler.Position = new Vector3(0f, 0f, angler.Seat.Back);
            root.AddChild(angler);
            // The rod on a forked stick in front, its tip out over the water.
            var rest = Fishing.RodRest.Create(false);
            rest.Position = new Vector3(0.35f, 0f, 1.4f);
            root.AddChild(rest);
            var rod = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.004f, BottomRadius = 0.016f, Height = 3.6f, RadialSegments = 6 },
                MaterialOverride = Props.CampProps.DarkWood,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            // From the butt by the seat over the fork, rising 22 degrees towards the water.
            var butt = new Vector3(0.3f, 0.2f, 0.5f);
            var along = new Vector3(0f, MathF.Sin(Mathf.DegToRad(22f)), MathF.Cos(Mathf.DegToRad(22f)));
            rod.Position = butt + along * 1.8f;
            rod.Basis = new Basis(Vector3.Right, Mathf.DegToRad(90f - 22f));
            root.AddChild(rod);
            // The float: a red-topped quill riding the water.
            var bob = new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = 0.025f, Height = 0.16f, RadialSegments = 8, Rings = 2 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.18f, 0.12f), Roughness = 0.5f },
                Position = new Vector3(floatAt.X, 0.03f, floatAt.Y),
            };
            AddChild(bob);
            Track(bob);
            AddChild(root);
            Track(root);
            Prop(root, 160f);
            _anglerParts.Add((angler, null, bob));
            Solids.BoxIn(root.Transform, new Vector3(0f, 0.25f, 0f), new Vector3(1.1f, 0.5f, 0.5f));
            _reserved.Add((seat, 3.5f));
            Anglers.Add((root.Position, new Vector3(floatAt.X, 0f, floatAt.Y)));
            Count("angler");
        }
    }

    private void ScatterSunkenWood(LocationData loc, RandomNumberGenerator rng)
    {
        var stems = new List<Transform3D>();
        var colors = new List<Color>();
        var shores = Shores(loc);
        for (int tries = 0; SunkenWood.Count < 16 && tries < 4000; tries++)
        {
            var shore = shores.Count == 1 || rng.Randf() < 0.45f ? shores[0] : shores[rng.RandiRange(1, shores.Count - 1)];
            float angle = rng.Randf() * Mathf.Tau;
            float shrink = shore.Island ? rng.RandfRange(1.05f, 1.5f) : rng.RandfRange(0.8f, 0.97f);
            float cx = shore.X + shore.Rx * shrink * MathF.Cos(angle);
            float cz = shore.Z + shore.Rz * shrink * MathF.Sin(angle);
            float depth = _lake.DepthAt(loc, cx, cz);
            if (depth < 0.6f || depth > 3f || NearPier(cx, cz))
                continue;
            bool crowded = false;
            foreach (var w in SunkenWood)
                if ((w.X - cx) * (w.X - cx) + (w.Z - cz) * (w.Z - cz) < 196f)
                    crowded = true;
            if (crowded)
                continue;
            // Mostly where trees stand on the bank nearby; now and then a drifted trunk elsewhere.
            float nearest = float.MaxValue;
            foreach (var (tx, tz) in _allTrees)
                nearest = MathF.Min(nearest, (tx - cx) * (tx - cx) + (tz - cz) * (tz - cz));
            if (nearest > 30f * 30f && rng.Randf() > 0.15f)
                continue;
            SunkenWood.Add(new Core.Methods.SunkenWood(cx, cz, rng.RandfRange(1.5f, 2.8f)));
            // Grey, weathered branches sticking out of the water at a slant.
            int branches = rng.RandiRange(1, 3);
            for (int b = 0; b < branches; b++)
            {
                float lean = rng.RandfRange(0.35f, 0.9f);
                float yaw = rng.Randf() * Mathf.Tau;
                float len = (depth + rng.RandfRange(0.6f, 1.6f)) / MathF.Cos(lean);
                var basis = (new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, lean)).Scaled(new Vector3(1f, len, 1f));
                var foot = new Vector3(cx + rng.RandfRange(-1f, 1f), -depth, cz + rng.RandfRange(-1f, 1f));
                stems.Add(new Transform3D(basis, foot + basis.Y * 0.5f));
                float g = rng.RandfRange(0.32f, 0.46f);
                var colour = new Color(g, g * 0.93f, g * 0.84f);
                colors.Add(colour);
                // A side twig forking off high up.
                float twig = len * rng.RandfRange(0.3f, 0.45f);
                var tb = (new Basis(Vector3.Up, yaw + rng.RandfRange(-1.2f, 1.2f)) * new Basis(Vector3.Right, lean + rng.RandfRange(0.5f, 0.9f)))
                    .Scaled(new Vector3(0.55f, twig, 0.55f));
                var fork = foot + basis.Y * rng.RandfRange(0.65f, 0.85f);
                stems.Add(new Transform3D(tb, fork + tb.Y * 0.5f));
                colors.Add(colour);
            }
        }
        if (stems.Count > 0)
            AddMultiMesh(new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.11f, Height = 1f, RadialSegments = 7, Rings = 1 },
                stems, colors);
    }

    private void ScatterWaterPlants(LocationData loc, RandomNumberGenerator rng)
    {
        var pads = new List<Transform3D>(); var padColors = new List<Color>();
        var whites = new List<Transform3D>(); var whiteColors = new List<Color>();
        var yellows = new List<Transform3D>(); var yellowColors = new List<Color>();
        var weeds = new List<Transform3D>(); var weedColors = new List<Color>();
        var shores = Shores(loc);
        int patches = (int)MathF.Round(30 * loc.LilyDensity), weedPatches = (int)MathF.Round(22 * loc.LilyDensity);
        int made = 0, madeWeeds = 0;
        for (int tries = 0; (made < patches || madeWeeds < weedPatches) && tries < 20000; tries++)
        {
            bool weed = made >= patches || (madeWeeds < weedPatches && rng.Randf() < 0.4f);
            var shore = shores.Count == 1 || rng.Randf() < 0.45f ? shores[0] : shores[rng.RandiRange(1, shores.Count - 1)];
            float angle = rng.Randf() * Mathf.Tau;
            float shrink = shore.Island ? rng.RandfRange(1.1f, 1.8f) : rng.RandfRange(0.72f, 0.97f);
            float cx = shore.X + shore.Rx * shrink * MathF.Cos(angle);
            float cz = shore.Z + shore.Rz * shrink * MathF.Sin(angle);
            if (NearPier(cx, cz))
                continue;
            float depth = _lake.DepthAt(loc, cx, cz);
            if (weed ? depth < 0.9f || depth > 3.2f : depth < 0.4f || depth > 2.2f)
                continue;
            if (weed)
                madeWeeds++;
            else
                made++;
            // A patch: leaves crowd its middle and thin out to its edge.
            float radius = rng.RandfRange(1.6f, weed ? 3.5f : 5f);
            WeedBeds.Add(new Core.Methods.WeedBed(cx, cz, radius));
            int count = (int)(radius * radius * rng.RandfRange(weed ? 0.9f : 2.2f, weed ? 1.6f : 3.6f));
            bool yellowPatch = !weed && rng.Randf() < 0.3f;
            for (int k = 0; k < count; k++)
            {
                float a = rng.Randf() * Mathf.Tau, d = radius * MathF.Sqrt(rng.Randf());
                float x = cx + MathF.Cos(a) * d, z = cz + MathF.Sin(a) * d;
                float dd = _lake.DepthAt(loc, x, z);
                if (dd < 0.25f || dd > 3.4f || NearPier(x, z))
                    continue;
                var basis = new Basis(Vector3.Up, rng.Randf() * Mathf.Tau);
                if (weed)
                {
                    float sc = rng.RandfRange(0.8f, 1.3f);
                    weeds.Add(new Transform3D(basis.Scaled(new Vector3(sc, 1f, sc)), new Vector3(x, 0.002f, z)));
                    weedColors.Add(Tint(Colors.White, rng.RandfRange(-0.12f, 0.02f)));
                    continue;
                }
                float r = rng.RandfRange(0.15f, yellowPatch ? 0.26f : 0.34f);
                pads.Add(new Transform3D(basis.Scaled(new Vector3(r, 1f, r)), new Vector3(x, 0.004f * (k % 4), z)));
                padColors.Add(Tint(Colors.White, rng.RandfRange(-0.2f, 0.0f)));
                if (rng.Randf() < (yellowPatch ? 0.12f : 0.14f))
                {
                    var fb = new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(Vector3.One * rng.RandfRange(0.85f, 1.25f));
                    var at = new Vector3(x + rng.RandfRange(-0.05f, 0.05f), 0.018f, z + rng.RandfRange(-0.05f, 0.05f));
                    if (yellowPatch)
                    {
                        yellows.Add(new Transform3D(fb, at + Vector3.Up * 0.03f));
                        yellowColors.Add(Colors.White);
                    }
                    else
                    {
                        whites.Add(new Transform3D(fb, at));
                        whiteColors.Add(rng.Randf() < 0.25f ? new Color(1f, 0.82f, 0.88f) : Colors.White);
                    }
                }
            }
        }
        var floating = TreeFactory.FloatingMaterial();
        AddMultiMeshChunked(TreeFactory.LilyPad(), pads, padColors, floating, 160f, 48f, shadows: false);
        AddMultiMeshChunked(TreeFactory.Pondweed(), weeds, weedColors, floating, 120f, 48f, shadows: false);
        AddMultiMeshChunked(TreeFactory.LilyFlower(), whites, whiteColors, floating, 160f, 48f, shadows: false);
        AddMultiMeshChunked(TreeFactory.YellowLily(), yellows, yellowColors, floating, 160f, 48f, shadows: false);
        Count("lily", pads.Count);
        Count("lily_flower", whites.Count + yellows.Count);
        Count("pondweed", weeds.Count);
    }

    private void ScatterRocks(LocationData loc, RandomNumberGenerator rng)
    {
        List<Transform3D> rocks = new();
        List<Color> colors = new();
        int maxRocks = (int)(160 * loc.RockDensity);
        float size = loc.RockDensity > 1.5f ? 1.7f : 1f;
        for (int tries = 0; rocks.Count < maxRocks && tries < 30000; tries++)
        {
            float x = rng.RandfRange(LakeBed.XMin, LakeBed.XMax);
            float z = rng.RandfRange(LakeBed.ZMin, LakeBed.ZMax);
            if (_lake.PlayDistance(x, z) > 0f)
                continue;
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
            // A boulder of unit radius scaled: the angler walks round it (or steps onto a low one).
            Solids.Cylinder(new Vector3(x, h - scale.Y, z), (scale.X + scale.Z) * 0.5f * 0.85f, scale.Y * 2f);
            float gr = rng.RandfRange(0.38f, 0.55f);
            colors.Add(new Color(gr, gr * 0.98f, gr * 0.93f));
        }
        // Big boulders and scree on the mountains around the map.
        int mountain = 0;
        for (int tries = 0; mountain < 60 + 50 * loc.RockDensity && tries < 20000; tries++)
        {
            float x = rng.RandfRange(LakeBed.XMin - 110f, LakeBed.XMax + 110f);
            float z = rng.RandfRange(LakeBed.ZMin - 110f, LakeBed.ZMax + 110f);
            float d = _lake.PlayDistance(x, z);
            if (d < -6f || d > 100f)
                continue;
            float h = _lake.HeightAt(loc, x, z);
            // Below the snow, in groups (scree under the crags), not sprinkled evenly.
            if (h < 3f || h > loc.SnowLine - 3f || _lake.Noise(x * 2.5f + 11f, z * 2.5f) < 0.05f)
                continue;
            float sz = rng.RandfRange(1.2f, 3.6f) * (0.8f + 0.4f * loc.RockDensity);
            var scale = new Vector3(rng.RandfRange(0.8f, 1.3f), rng.RandfRange(0.45f, 0.8f), rng.RandfRange(0.8f, 1.2f)) * sz;
            rocks.Add(new Transform3D(new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(scale), new Vector3(x, h - sz * 0.15f, z)));
            float gr = rng.RandfRange(0.4f, 0.56f);
            colors.Add(new Color(gr, gr * 0.98f, gr * 0.94f));
            mountain++;
        }
        Count("mountain_rock", mountain);

        // Three boulder shapes, each rock one of them.
        for (int v = 0; v < 3; v++)
        {
            var mine = Enumerable.Range(0, rocks.Count).Where(i => i % 3 == v).ToList();
            AddMultiMesh(TreeFactory.Boulder(v), mine.Select(i => rocks[i]).ToList(), mine.Select(i => colors[i]).ToList(), TreeFactory.RockMaterial());
        }
        Count("rock", rocks.Count);
    }
}
