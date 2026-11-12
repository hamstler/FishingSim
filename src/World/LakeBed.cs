using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Data;

namespace FishingSim.World;

/// <summary>
/// Procedural terrain: the lake basin, its banks, the surrounding hills and the village grounds.
/// <see cref="DepthAt"/> is the water depth (0 on land) and drives where fish bite;
/// <see cref="HeightAt"/> is the terrain surface used for the mesh and collision (levelled under buildings).
/// </summary>
public partial class LakeBed : Node3D
{
    public const float Step = 2.5f;

    /// <summary>The playable map (also the minimap's area): the angler is stopped at its rounded edge (<see cref="PlayDistance"/>).</summary>
    public const float XMin = -190f, XMax = 190f, ZMin = -220f, ZMax = 100f;

    /// <summary>The terrain is detailed (one vertex every <see cref="Step"/>) this far beyond the playable map...</summary>
    public const float DetailMargin = 30f;

    /// <summary>...and reaches this far beyond it with ever wider spacing, so the mountains around fill the horizon.</summary>
    public const float Outer = 380f;
    public const float PierEndZ = -4f;

    /// <summary>Metres over which depth ramps up from the waterline.</summary>
    public const float ShoreTaper = 6f;

    /// <summary>Largest water depth (m) a player may wade into.</summary>
    public const float WadeDepth = 0.5f;

    /// <summary>How deep the angler may wade here and now (deeper with waders).</summary>
    public float WadeLimit { get; set; } = WadeDepth;

    public static readonly string[] LocationPaths =
    {
        "res://data/locations/jezioro.tres",
        "res://data/locations/staw.tres",
        "res://data/locations/zatoka.tres",
        "res://data/locations/gory.tres",
    };

    private readonly FastNoiseLite _noise = new() { Seed = 1234, Frequency = 0.01f };
    private readonly Dictionary<LocationData, MapLayout> _layouts = new();
    private StaticBody3D? _body;

    public List<LocationData> Locations { get; } = new();
    public int LocationIndex { get; private set; }

    /// <summary>Raised after the terrain was (re)built.</summary>
    public event Action? Rebuilt;

    public LocationData Location => Locations[LocationIndex];

    public MapLayout Layout => LayoutFor(Location);

    public float AbundanceOf(string speciesName) => Location.AbundanceOf(speciesName);

    /// <summary>Selects a location without rebuilding (use before the first <see cref="Rebuild"/>).</summary>
    public void SelectInitial(int index) => LocationIndex = Math.Clamp(index, 0, Locations.Count - 1);

    /// <summary>Switches to the next location the player has the level for. Returns true when it changed.</summary>
    public bool CycleLocation(int level)
    {
        for (int k = 1; k <= Locations.Count; k++)
        {
            int j = (LocationIndex + k) % Locations.Count;
            if (Locations[j].RequiredLevel <= level)
            {
                if (j == LocationIndex)
                    return false;
                SetLocation(j);
                return true;
            }
        }
        return false;
    }

    public void SetLocation(int index)
    {
        SelectInitial(index);
        Rebuild();
    }

    public float ForestDensity(float x, float z) => _noise.GetNoise2D(x * 1.2f + 99f, z * 1.2f + 99f);

    public float Noise(float x, float z) => _noise.GetNoise2D(x, z);

    // ------------------------------------------------------------------ layout
    public MapLayout LayoutFor(LocationData loc)
    {
        if (_layouts.TryGetValue(loc, out var cached))
            return cached;
        var layout = new MapLayout();
        layout.Specs.AddRange(loc.BuildingSpecs());
        var plaza = loc.PlazaCenter;
        layout.Pads.Add(new Pad(plaza, 6f, MathF.Max(NaturalHeight(loc, plaza.X, plaza.Y), 1.25f)));
        foreach (var spec in layout.Specs.Where(s => s.PadRadius > 0f))
            layout.Pads.Add(new Pad(spec.Position, spec.PadRadius, MathF.Max(NaturalHeight(loc, spec.Position.X, spec.Position.Y), 1.25f)));

        layout.Paths.Add(new PathSegment(MapLayout.PierRoot, plaza));
        foreach (var spec in layout.Specs.Where(s => s.IsFunctional || s.Kind is BuildingKind.House))
            layout.Paths.Add(new PathSegment(plaza, spec.DoorPoint(plaza)));
        _layouts[loc] = layout;
        return layout;
    }

    // ------------------------------------------------------------------ depth and height
    /// <summary>Signed distance (m) from the waterline: positive inside the lake, negative on land.</summary>
    public float InsideMetres(float x, float z) => InsideMetres(Location, x, z);

    public float InsideMetres(LocationData loc, float x, float z)
    {
        float dx = MathF.Abs(x / loc.OutlineRx);
        float dz = MathF.Abs((z - loc.OutlineCz) / loc.OutlineRz);
        // Superellipse: 1 = ellipse, bigger = squarer (dug fish ponds).
        float p = 2f * MathF.Max(loc.Squareness, 1f);
        float e = MathF.Pow(MathF.Pow(dx, p) + MathF.Pow(dz, p), 1f / p);
        // Irregular shoreline, but exact around the pier.
        e += _noise.GetNoise2D(x * 2f, z * 2f) * 0.06f * Smooth(8f, 40f, MathF.Abs(x));
        float inside = (1f - e) * MathF.Min(loc.OutlineRx, loc.OutlineRz);

        // Extra bays and arms join the main lake.
        foreach (var lobe in ShapeCache(loc).Lobes)
        {
            float lx = (x - lobe.X) / lobe.Z, lz = (z - lobe.Y) / lobe.W;
            float le = MathF.Sqrt(lx * lx + lz * lz) + _noise.GetNoise2D(x * 2.3f + 50f, z * 2.3f) * 0.08f;
            inside = MathF.Max(inside, (1f - le) * MathF.Min(lobe.Z, lobe.W));
        }
        // Islands cut land out of the water.
        foreach (var island in ShapeCache(loc).Islands)
        {
            float d = new Vector2(x - island.X, z - island.Y).Length() + _noise.GetNoise2D(x * 4f, z * 4f + 90f) * island.Z * 0.25f;
            inside = MathF.Min(inside, d - island.Z);
        }
        return inside;
    }

    private sealed record Shapes(List<Vector4> Lobes, List<Vector3> Islands);
    private readonly Dictionary<LocationData, Shapes> _shapes = new();

    private Shapes ShapeCache(LocationData loc)
    {
        if (_shapes.TryGetValue(loc, out var s))
            return s;
        s = new Shapes(new(), new());
        foreach (var entry in loc.Lobes)
        {
            var n = LocationData.Numbers(entry);
            if (n.Length >= 4)
                s.Lobes.Add(new Vector4(n[0], n[1], n[2], n[3]));
        }
        foreach (var entry in loc.Islands)
        {
            var n = LocationData.Numbers(entry);
            if (n.Length >= 3)
                s.Islands.Add(new Vector3(n[0], n[1], n[2]));
        }
        _shapes[loc] = s;
        return s;
    }

    public float DepthAt(float x, float z) => DepthAt(Location, x, z);

    public float DepthAt(LocationData loc, float x, float z)
    {
        float inside = InsideMetres(loc, x, z);
        if (inside <= 0f)
            return 0f;
        // Depth grows with the distance from the nearest shore, easing into MaxDepth (polynomial smooth-min).
        float lin = 0.6f + inside * loc.Slope;
        const float k = 3f;
        float hh = MathF.Max(k - MathF.Abs(lin - loc.MaxDepth), 0f) / k;
        float d = MathF.Min(lin, loc.MaxDepth) - hh * hh * k * 0.25f;
        float hx = x - loc.HolePos.X, hz = z - loc.HolePos.Y;
        d += loc.HoleDepth * MathF.Exp(-(hx * hx + hz * hz) / (2f * loc.HoleRadius * loc.HoleRadius));
        float bx = x - loc.BarPos.X, bz = z - loc.BarPos.Y;
        d -= loc.BarHeight * MathF.Exp(-(bx * bx + bz * bz) / (2f * loc.BarRadius * loc.BarRadius));
        d = MathF.Max(d, 0.5f);
        return d * Smooth(0f, 1f, Math.Clamp(inside / ShoreTaper, 0f, 1f));
    }

    public float HeightAt(float x, float z) => HeightAt(Location, x, z);

    /// <summary>
    /// Signed distance (m) from the edge of the playable map: negative inside. A rounded rectangle whose edge wanders a
    /// little, so the dense forest and the foot of the mountains that hide it do not run in straight lines.
    /// </summary>
    public float PlayDistance(float x, float z)
    {
        const float r = 60f;
        float cx = (XMin + XMax) * 0.5f, cz = (ZMin + ZMax) * 0.5f;
        float qx = MathF.Abs(x - cx) - (XMax - XMin) * 0.5f + r, qz = MathF.Abs(z - cz) - (ZMax - ZMin) * 0.5f + r;
        float outside = MathF.Sqrt(MathF.Max(qx, 0f) * MathF.Max(qx, 0f) + MathF.Max(qz, 0f) * MathF.Max(qz, 0f));
        return outside + MathF.Min(MathF.Max(qx, qz), 0f) - r + _noise.GetNoise2D(x * 0.8f + 300f, z * 0.8f) * 7f;
    }

    /// <summary>
    /// The mountains closing the map in: they start rising in the forest belt inside the edge, a few metres high where
    /// the angler is stopped, and climb to <see cref="LocationData.RimHeight"/> with ridges, peaks and saddles.
    /// </summary>
    public float RimAt(LocationData loc, float x, float z)
    {
        float d = PlayDistance(x, z);
        if (d < -45f)
            return 0f;
        float t = Smooth(-40f, 170f, d);
        float ridge = 1f - MathF.Abs(_noise.GetNoise2D(x * 0.55f + 77f, z * 0.55f - 31f));
        float peaks = 0.5f + 0.45f * ridge * ridge + 0.25f * _noise.GetNoise2D(x * 1.7f - 12f, z * 1.7f + 40f);
        // A steeper bank right at the edge: the ground rises in front of the angler instead of an invisible wall.
        float bank = 5f * Smooth(-14f, 12f, d);
        return loc.RimHeight * MathF.Pow(t, 1.6f) * peaks + bank;
    }

    private static float Hash(int x, int z, int k, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(k * 83492791) ^ (uint)(seed * 2654435761u);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    /// <summary>A stable number per map (string hashes change between runs in .NET).</summary>
    private static int SeedOf(LocationData loc)
    {
        int h = 17;
        foreach (char c in loc.DisplayName)
            h = h * 31 + c;
        return h;
    }

    /// <summary>
    /// Knolls and hills on the land: about three in four 46 m cells hold one, a smooth dome stretched in some direction,
    /// small or large (up to ~0.45 of its radius high, so they stay walkable), taller on hilly maps.
    /// </summary>
    public float HillsAt(LocationData loc, float x, float z)
    {
        const float cell = 46f;
        int seed = SeedOf(loc);
        int cx = (int)MathF.Floor(x / cell), cz = (int)MathF.Floor(z / cell);
        float h = 0f;
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int ix = cx + dx, iz = cz + dz;
                if (Hash(ix, iz, 1, seed) > 0.75f)
                    continue;
                float hx = (ix + Hash(ix, iz, 2, seed)) * cell, hz = (iz + Hash(ix, iz, 3, seed)) * cell;
                float radius = 12f + 26f * Hash(ix, iz, 4, seed);
                float a = Hash(ix, iz, 6, seed) * MathF.PI, stretch = 0.55f + 0.45f * Hash(ix, iz, 7, seed);
                float px = x - hx, pz = z - hz;
                float u = (px * MathF.Cos(a) + pz * MathF.Sin(a)) / radius;
                float v = (-px * MathF.Sin(a) + pz * MathF.Cos(a)) / (radius * stretch);
                float d2 = u * u + v * v;
                if (d2 >= 1f)
                    continue;
                float height = (1.2f + 8f * MathF.Pow(Hash(ix, iz, 5, seed), 1.5f)) * (0.45f + 0.55f * loc.HillScale);
                height = MathF.Min(height, 0.45f * radius * stretch);
                float k = 1f - d2;
                h += height * k * k;
            }
        return h;
    }

    private readonly Dictionary<LocationData, List<(Vector2 P, float R)>> _flat = new();

    /// <summary>0 where the ground must stay level (the village square, around the buildings), 1 away from them.</summary>
    private float Unflattened(LocationData loc, float x, float z)
    {
        if (!_flat.TryGetValue(loc, out var spots))
        {
            spots = new List<(Vector2, float)> { (loc.PlazaCenter, 14f) };
            foreach (var spec in loc.BuildingSpecs())
                spots.Add((spec.Position, spec.PadRadius + 2f));
            _flat[loc] = spots;
        }
        float k = 1f;
        var p = new Vector2(x, z);
        foreach (var (c, r) in spots)
            k = MathF.Min(k, Smooth(r, r + 22f, p.DistanceTo(c)));
        return k;
    }

    /// <summary>Terrain height without the levelled building pads.</summary>
    public float NaturalHeight(LocationData loc, float x, float z)
    {
        float inside = InsideMetres(loc, x, z);
        if (inside > 0f)
            return -DepthAt(loc, x, z);
        float sd = -inside;
        float hill = loc.HillScale;
        float h = 1.05f * Smooth(0f, 4f, sd);
        h += 0.045f * MathF.Pow(Math.Clamp(sd - 8f, 0f, 90f), 1.25f) * hill;
        h += _noise.GetNoise2D(x * 1.5f, z * 1.5f) * 3f * hill * Math.Clamp(sd / 40f, 0f, 1f);
        h += _noise.GetNoise2D(x * 8f, z * 8f) * 0.4f * Math.Clamp(sd / 10f, 0f, 1f);
        // Hills of every size on the land (not on the beach, not in the village), then the mountains at the edge.
        h += HillsAt(loc, x, z) * Smooth(8f, 30f, sd) * Unflattened(loc, x, z);
        h += RimAt(loc, x, z);
        return h;
    }

    public float HeightAt(LocationData loc, float x, float z)
    {
        float h = NaturalHeight(loc, x, z);
        if (h < 0f)
            return h;
        var p = new Vector2(x, z);
        foreach (var pad in LayoutFor(loc).Pads)
        {
            float d = p.DistanceTo(pad.Center);
            float t = 1f - Smooth(pad.Radius * 0.75f, pad.Radius * 1.6f, d);
            if (t > 0f)
                h = Mathf.Lerp(h, pad.Height, t);
        }
        return h;
    }

    /// <summary>True where the player may stand: on land, in the shallows, or on the pier.</summary>
    public bool IsWalkable(float x, float z)
    {
        if (OnPier(x, z))
            return true;
        // The edge of the map lies in the dense forest at the foot of the mountains: the angler goes no further.
        if (PlayDistance(x, z) > 0f)
            return false;
        return DepthAt(x, z) <= WadeLimit;
    }

    public static bool OnPier(float x, float z) => MathF.Abs(x) <= 1.6f && z >= -4.2f && z <= 6.2f;

    /// <summary>GLSL-style smoothstep.</summary>
    private static float Smooth(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // ------------------------------------------------------------------ mesh
    public Color ColorAt(LocationData loc, float x, float z, float h)
    {
        float n = _noise.GetNoise2D(x * 5f, z * 5f) * 0.08f;
        Color c;
        if (h < 0f)
            c = loc.SandColor.Lerp(new Color(0.12f, 0.11f, 0.09f), Math.Clamp(-h / 7f, 0f, 1f));
        else if (h < 0.6f)
            c = loc.SandColor;
        else if (h < 2f)
            c = loc.SandColor.Lerp(loc.GrassColor, (h - 0.6f) / 1.4f);
        else
        {
            c = loc.GrassColor.Lerp(loc.DarkGrassColor, Math.Clamp((h - 2f) / 6f, 0f, 1f));
            c = c.Lerp(new Color(0.45f, 0.43f, 0.40f), Math.Clamp((h - 11f) / 5f, 0f, 1f));
            c = c.Lerp(new Color(0.93f, 0.95f, 0.98f), Math.Clamp((h - 26f) / 8f, 0f, 1f));   // snow caps
        }
        if (h >= 0.5f)
        {
            float path = 1f - Smooth(0.9f, 1.9f, LayoutFor(loc).PathDistance(x, z));
            if (path > 0f)
                c = c.Lerp(loc.PathColor, path * 0.92f);
        }
        return new Color(c.R + n, c.G + n, c.B + n);
    }

    /// <summary>
    /// How much of the painted grounds lies at a point (terrain.gdshader): sand, dirt path, forest floor, lake bed; the
    /// meadow is the rest, rock and snow come from slope and height in the shader.
    /// </summary>
    public Vector4 GroundAt(LocationData loc, float x, float z, float h)
    {
        if (h < 0f)
        {
            float bed = Smooth(0.25f, 1.6f, -h);
            return new Vector4(1f - bed, 0f, 0f, bed);
        }
        float sand = 1f - Smooth(0.5f, 1.25f, h);
        var layout = LayoutFor(loc);
        float path = h >= 0.4f ? (1f - Smooth(0.8f, 2.0f, layout.PathDistance(x, z))) * 0.95f : 0f;
        // Trodden ground around the buildings.
        path = MathF.Max(path, (1f - Smooth(-2.5f, 0.8f, layout.PadDistance(x, z))) * 0.5f * Smooth(0.6f, 1.2f, h));
        // The forest floor where the trees stand (Scenery plants them where ForestDensity passes this threshold).
        float threshold = -0.08f + (1f - loc.TreeDensity) * 0.5f;
        float forest = Smooth(threshold - 0.04f, threshold + 0.14f, ForestDensity(x, z)) * Smooth(1.2f, 2.6f, h) * 0.95f;
        forest *= 1f - path;
        sand *= 1f - path;
        float sum = sand + path + forest;
        if (sum > 1f)
        {
            sand /= sum;
            path /= sum;
            forest /= sum;
        }
        return new Vector4(sand, path, forest, 0f);
    }

    private ShaderMaterial? _terrainMaterial;

    /// <summary>The ground material (shaders/terrain.gdshader); also animated (caustics) by <see cref="_Process"/>.</summary>
    public ShaderMaterial TerrainMaterial()
    {
        if (_terrainMaterial != null)
            return _terrainMaterial;
        _terrainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") };
        _terrainMaterial.SetShaderParameter("noise_tex", Graphics.ProceduralTextures.Noise(17, 0.01f, 1024));
        _terrainMaterial.SetShaderParameter("caustics_tex", Graphics.ProceduralTextures.Caustics());
        _terrainMaterial.SetShaderParameter("tile", Props.GroundTextures.TileMetres);
        // The painted grounds (a few seconds the first time, then from the disk cache); not needed without a screen.
        if (!Graphics.Renderer.IsHeadless)
        {
            _terrainMaterial.SetShaderParameter("ground_albedo", Props.GroundTextures.Albedo);
            _terrainMaterial.SetShaderParameter("ground_normal", Props.GroundTextures.Normal);
        }
        return _terrainMaterial;
    }

    /// <summary>Tints the painted grounds with the map's colours: each layer's average becomes the map's colour.</summary>
    private void ApplyGroundTints(LocationData loc)
    {
        if (Graphics.Renderer.IsHeadless)
            return;
        var material = TerrainMaterial();
        static Vector3 Lin(Color c) => new(Props.GroundTextures.Lin(c.R), Props.GroundTextures.Lin(c.G), Props.GroundTextures.Lin(c.B));
        Vector3 Towards(Color mapColour, int layer, float amount)
        {
            var mean = Props.GroundTextures.Mean(layer);
            var want = Lin(mapColour);
            var k = new Vector3(want.X / MathF.Max(mean.R, 0.001f), want.Y / MathF.Max(mean.G, 0.001f), want.Z / MathF.Max(mean.B, 0.001f));
            return Vector3.One.Lerp(k, amount);
        }
        var tints = new Vector3[Props.GroundTextures.Count];
        // Meadows a touch calmer than the map's colour (lit smoothly now, without the old banding, they read brighter).
        static Color Calm(Color c) => new Color(c.Luminance, c.Luminance, c.Luminance).Lerp(c, 0.85f) * 0.92f;
        tints[Props.GroundTextures.Grass] = Towards(Calm(loc.GrassColor), Props.GroundTextures.Grass, 1f);
        tints[Props.GroundTextures.Forest] = Towards(loc.DarkGrassColor.Lerp(loc.PathColor, 0.6f) * 0.75f, Props.GroundTextures.Forest, 0.35f);
        tints[Props.GroundTextures.Sand] = Towards(loc.SandColor, Props.GroundTextures.Sand, 1f);
        tints[Props.GroundTextures.Path] = Towards(Calm(loc.PathColor), Props.GroundTextures.Path, 1f);
        tints[Props.GroundTextures.Bed] = Towards(loc.SandColor * 0.7f, Props.GroundTextures.Bed, 0.5f);
        tints[Props.GroundTextures.Rock] = Vector3.One;
        tints[Props.GroundTextures.Snow] = Vector3.One;
        material.SetShaderParameter("layer_tint", tints);
        material.SetShaderParameter("snow_line", loc.SnowLine);
        material.SetShaderParameter("rock_line", loc.RockLine);
        material.SetShaderParameter("dark_grass_tint", Towards(Calm(loc.DarkGrassColor), Props.GroundTextures.Grass, 1f));
    }

    private float _time;

    /// <summary>0..1 sun light reaching the ground (caustics fade at night); set by the sky.</summary>
    public float Daylight { get; set; } = 1f;

    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("lakebed");
        _time += (float)delta;
        if (_terrainMaterial != null)
        {
            _terrainMaterial.SetShaderParameter("time", _time);
            _terrainMaterial.SetShaderParameter("daylight", Daylight);
        }
    }

    /// <summary>
    /// The terrain's grid lines along one axis: every <see cref="Step"/> across the playable map and its margin, then
    /// wider and wider apart (up to 14 m) out to the distant mountains.
    /// </summary>
    private static List<float> GridLines(float min, float max)
    {
        var lines = new List<float>();
        float a = min - DetailMargin, b = max + DetailMargin;
        for (float v = a; v <= b + 0.001f; v += Step)
            lines.Add(v);
        var low = new List<float>();
        float step = Step, at = a;
        while (at > min - Outer)
        {
            step = MathF.Min(step * 1.08f, 14f);
            at -= step;
            low.Add(at);
        }
        low.Reverse();
        step = Step;
        at = lines[^1];
        while (at < max + Outer)
        {
            step = MathF.Min(step * 1.08f, 14f);
            at += step;
            lines.Add(at);
        }
        low.AddRange(lines);
        return low;
    }

    public void Rebuild()
    {
        _body?.QueueFree();
        var loc = Location;
        var xs = GridLines(XMin, XMax);
        var zs = GridLines(ZMin, ZMax);
        int nx = xs.Count, nz = zs.Count;
        var heights = new float[nx * nz];
        for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
                heights[iz * nx + ix] = HeightAt(loc, xs[ix], zs[iz]);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int iz = 0; iz < nz; iz++)
        {
            for (int ix = 0; ix < nx; ix++)
            {
                float x = xs[ix], z = zs[iz];
                float h = heights[iz * nx + ix];
                var ground = GroundAt(loc, x, z, h);
                st.SetColor(ColorAt(loc, x, z, h));
                st.SetUV(new Vector2(ground.X, ground.Y));
                st.SetUV2(new Vector2(ground.Z, ground.W));
                st.AddVertex(new Vector3(x, h, z));
            }
        }
        for (int iz = 0; iz < nz - 1; iz++)
        {
            for (int ix = 0; ix < nx - 1; ix++)
            {
                int a = iz * nx + ix, b = a + 1, c = a + nx, d = c + 1;
                foreach (int i in new[] { a, b, d, a, d, c })
                    st.AddIndex(i);
            }
        }
        st.GenerateNormals();
        var mesh = st.Commit();

        // Collision only where the angler can get to (the playable map and a little beyond).
        var col = new SurfaceTool();
        col.Begin(Mesh.PrimitiveType.Triangles);
        int x0 = xs.FindIndex(v => v >= XMin - 12f), x1 = xs.FindLastIndex(v => v <= XMax + 12f);
        int z0 = zs.FindIndex(v => v >= ZMin - 12f), z1 = zs.FindLastIndex(v => v <= ZMax + 12f);
        for (int iz = z0; iz < z1; iz++)
            for (int ix = x0; ix < x1; ix++)
            {
                Vector3 V(int i, int j) => new(xs[i], heights[j * nx + i], zs[j]);
                foreach (var v in new[] { V(ix, iz), V(ix + 1, iz), V(ix + 1, iz + 1), V(ix, iz), V(ix + 1, iz + 1), V(ix, iz + 1) })
                    col.AddVertex(v);
            }
        var collision = col.Commit();

        ApplyGroundTints(loc);
        _body = new StaticBody3D { CollisionLayer = 1 };
        _body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = TerrainMaterial() });
        var shape = collision.CreateTrimeshShape();
        shape.BackfaceCollision = true;
        _body.AddChild(new CollisionShape3D { Shape = shape });
        AddChild(_body);
        Rebuilt?.Invoke();
    }
}
