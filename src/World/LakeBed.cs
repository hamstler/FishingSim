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
    public const float XMin = -170f, XMax = 170f, ZMin = -200f, ZMax = 70f;
    public const float PierEndZ = -4f;

    /// <summary>Metres over which depth ramps up from the waterline.</summary>
    public const float ShoreTaper = 6f;

    /// <summary>Largest water depth (m) a player may wade into.</summary>
    public const float WadeDepth = 0.5f;

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
        float dx = x / loc.OutlineRx;
        float dz = (z - loc.OutlineCz) / loc.OutlineRz;
        float e = MathF.Sqrt(dx * dx + dz * dz);
        // Irregular shoreline, but exact around the pier.
        e += _noise.GetNoise2D(x * 2f, z * 2f) * 0.06f * Smooth(8f, 40f, MathF.Abs(x));
        return (1f - e) * MathF.Min(loc.OutlineRx, loc.OutlineRz);
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

    /// <summary>Terrain height without the levelled building pads.</summary>
    public float NaturalHeight(LocationData loc, float x, float z)
    {
        float inside = InsideMetres(loc, x, z);
        if (inside > 0f)
            return -DepthAt(loc, x, z);
        float sd = -inside;
        float hill = loc.HillScale;
        float h = 1.05f * Smooth(0f, 4f, sd);
        h += 0.045f * MathF.Pow(MathF.Max(sd - 8f, 0f), 1.25f) * hill;
        h += _noise.GetNoise2D(x * 1.5f, z * 1.5f) * 3f * hill * Math.Clamp(sd / 40f, 0f, 1f);
        h += _noise.GetNoise2D(x * 8f, z * 8f) * 0.4f * Math.Clamp(sd / 10f, 0f, 1f);
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
        return DepthAt(x, z) <= WadeDepth;
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

    private ShaderMaterial? _terrainMaterial;

    /// <summary>The ground material (shaders/terrain.gdshader); also animated (caustics) by <see cref="_Process"/>.</summary>
    public ShaderMaterial TerrainMaterial()
    {
        if (_terrainMaterial != null)
            return _terrainMaterial;
        _terrainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") };
        _terrainMaterial.SetShaderParameter("noise_tex", Graphics.ProceduralTextures.Noise(17, 0.02f, 512));
        _terrainMaterial.SetShaderParameter("detail_normal", Graphics.ProceduralTextures.Noise(23, 0.03f, 512, true, 10f));
        _terrainMaterial.SetShaderParameter("caustics_tex", Graphics.ProceduralTextures.Caustics());
        return _terrainMaterial;
    }

    private float _time;

    /// <summary>0..1 sun light reaching the ground (caustics fade at night); set by the sky.</summary>
    public float Daylight { get; set; } = 1f;

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (_terrainMaterial != null)
        {
            _terrainMaterial.SetShaderParameter("time", _time);
            _terrainMaterial.SetShaderParameter("daylight", Daylight);
        }
    }

    public void Rebuild()
    {
        _body?.QueueFree();
        var loc = Location;
        int xs = (int)MathF.Round((XMax - XMin) / Step);
        int zs = (int)MathF.Round((ZMax - ZMin) / Step);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int iz = 0; iz <= zs; iz++)
        {
            for (int ix = 0; ix <= xs; ix++)
            {
                float x = XMin + ix * Step, z = ZMin + iz * Step;
                float h = HeightAt(loc, x, z);
                st.SetColor(ColorAt(loc, x, z, h));
                st.AddVertex(new Vector3(x, h, z));
            }
        }
        for (int iz = 0; iz < zs; iz++)
        {
            for (int ix = 0; ix < xs; ix++)
            {
                int a = iz * (xs + 1) + ix, b = a + 1, c = a + xs + 1, d = c + 1;
                foreach (int i in new[] { a, b, d, a, d, c })
                    st.AddIndex(i);
            }
        }
        st.GenerateNormals();
        var mesh = st.Commit();

        _body = new StaticBody3D { CollisionLayer = 1 };
        _body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = TerrainMaterial() });
        var shape = mesh.CreateTrimeshShape();
        shape.BackfaceCollision = true;
        _body.AddChild(new CollisionShape3D { Shape = shape });
        AddChild(_body);
        Rebuilt?.Invoke();
    }
}
