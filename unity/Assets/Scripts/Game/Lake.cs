using System;
using System.Collections.Generic;
using FishingSim.Compat;
using FishingSim.Data;

namespace FishingSim.Game;

/// <summary>
/// The map's shape: the lake's outline and depth, the land's height (hills, knolls, hollows, the mountain rim), the
/// ground's colour and where the angler may walk. A port of the Godot game's World/LakeBed (its maths, unchanged) without
/// the village layout; pure, so it can be computed off the main thread.
/// </summary>
public sealed class Lake
{
    public const float XMin = -190f, XMax = 190f, ZMin = -220f, ZMax = 100f;
    public const float Outer = 380f;
    public const float ShoreTaper = 6f;
    public const float WadeDepth = 0.5f;

    public static readonly string[] LocationPaths =
    {
        "res://data/locations/jezioro.tres",
        "res://data/locations/staw.tres",
        "res://data/locations/zatoka.tres",
        "res://data/locations/gory.tres",
    };

    private readonly Noise _noise = new(1234, 0.01f);
    private readonly List<(float X, float Z, float Rx, float Rz)> _lobes = new();
    private readonly List<(float X, float Z, float R)> _islands = new();
    private readonly List<(Vector2 P, float R)> _flat = new();
    private readonly List<(Vector2 C, float R, float H)> _pads = new();
    private readonly List<(Vector2 A, Vector2 B)> _paths = new();

    /// <summary>The village's buildings and props (LocationData.Buildings).</summary>
    public List<BuildingSpec> Specs { get; } = new();
    public float PlazaHeight { get; private set; } = 1.25f;

    public LocationData Location { get; }

    public Lake(LocationData loc)
    {
        Location = loc;
        foreach (var entry in loc.Lobes)
            if (LocationData.Numbers(entry) is { Length: >= 4 } n)
                _lobes.Add((n[0], n[1], n[2], n[3]));
        foreach (var entry in loc.Islands)
            if (LocationData.Numbers(entry) is { Length: >= 3 } n)
                _islands.Add((n[0], n[1], n[2]));
        _flat.Add((loc.PlazaCenter, 14f));
        Specs.AddRange(loc.BuildingSpecs());
        foreach (var spec in Specs)
            _flat.Add((spec.Position, spec.PadRadius + 2f));
        // Level pads under the square and every building (World/LakeBed.LayoutFor), and the paths: pier to square,
        // streets, each door to the nearest street.
        var plaza = loc.PlazaCenter;
        PlazaHeight = MathF.Max(NaturalHeight(plaza.X, plaza.Y), 1.25f);
        _pads.Add((plaza, 6f, PlazaHeight));
        foreach (var spec in Specs)
            if (spec.PadRadius > 0f)
            {
                var p = spec.Position;
                float ground = VillageGround(p.X, p.Y, NaturalHeight(p.X, p.Y));
                _pads.Add((p, spec.PadRadius, MathF.Max(ground, 1.25f)));
            }
        _paths.Add((new Vector2(0f, 6f), plaza));
        var streets = new List<(Vector2, Vector2)>();
        foreach (var entry in loc.Streets)
            if (LocationData.Numbers(entry) is { Length: >= 4 } n)
                streets.Add((new Vector2(n[0], n[1]), new Vector2(n[2], n[3])));
        _paths.AddRange(streets);
        foreach (var spec in Specs)
        {
            if (!spec.IsFunctional && !spec.IsDwelling)
                continue;
            var door = spec.DoorPoint(plaza);
            var to = plaza;
            float best = door.DistanceTo(plaza);
            foreach (var (a, b) in streets)
            {
                var q = Closest(a, b, door);
                if (door.DistanceTo(q) < best)
                    (best, to) = (door.DistanceTo(q), q);
            }
            _paths.Add((to, door));
        }
    }

    private static Vector2 Closest(Vector2 a, Vector2 b, Vector2 p)
    {
        var ab = b - a;
        float t = Math.Clamp((p - a).Dot(ab) / MathF.Max(ab.Dot(ab), 1e-6f), 0f, 1f);
        return a + ab * t;
    }

    /// <summary>True on or near a building's pad, the square or a path: no trees, bushes or stones there.</summary>
    public bool Built(float x, float z, float margin = 3f)
    {
        var p = new Vector2(x, z);
        foreach (var (c, r, _) in _pads)
            if (p.DistanceTo(c) < r + margin)
                return true;
        return PathDistance(x, z) < 2.2f;
    }

    /// <summary>Signed distance (m) to the nearest building pad's edge (negative on a pad).</summary>
    public float PadDistance(float x, float z)
    {
        var p = new Vector2(x, z);
        float best = float.MaxValue;
        foreach (var (c, r, _) in _pads)
            best = MathF.Min(best, p.DistanceTo(c) - r);
        return best;
    }

    /// <summary>
    /// How much of the photographed grounds lies at a point (LakeBed.GroundAt): x sand, y path, z forest floor,
    /// w lake bed; the meadow is the rest, rock and snow come from slope and height in the terrain shader.
    /// </summary>
    public (float Sand, float Path, float Forest, float Bed) GroundAt(float x, float z, float h)
    {
        if (h < 0f)
        {
            float bed = Smooth(0.25f, 1.6f, -h);
            return (1f - bed, 0f, 0f, bed);
        }
        float sand = 1f - Smooth(0.5f, 1.25f, h);
        float path = h >= 0.4f ? (1f - Smooth(0.8f, 2.0f, PathDistance(x, z))) * 0.95f : 0f;
        path = MathF.Max(path, (1f - Smooth(-2.5f, 0.8f, PadDistance(x, z))) * 0.5f * Smooth(0.6f, 1.2f, h));
        float threshold = -0.08f + (1f - Location.TreeDensity) * 0.5f;
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
        return (sand, path, forest, 0f);
    }

    /// <summary>Distance (m) to the nearest path or street.</summary>
    public float PathDistance(float x, float z)
    {
        float best = float.MaxValue;
        foreach (var (a, b) in _paths)
            best = MathF.Min(best, SegmentDistance(x, z, a.X, a.Y, b.X, b.Y));
        return best;
    }

    /// <summary>The ground eases part way to the square's level near the village (LakeBed.VillageGround).</summary>
    private float VillageGround(float x, float z, float h)
    {
        float d = new Vector2(x, z).DistanceTo(Location.PlazaCenter);
        if (d > 70f)
            return h;
        float w = (1f - Smooth(18f, 55f, d)) * 0.55f * Smooth(5f, 14f, -InsideMetres(x, z));
        return h + (PlazaHeight - h) * w;
    }

    public float ForestDensity(float x, float z) => _noise.Get(x * 1.2f + 99f, z * 1.2f + 99f);
    public float NoiseAt(float x, float z) => _noise.Get(x, z);

    /// <summary>Signed distance (m) from the waterline: positive inside the lake, negative on land.</summary>
    public float InsideMetres(float x, float z)
    {
        var loc = Location;
        float dx = MathF.Abs(x / loc.OutlineRx);
        float dz = MathF.Abs((z - loc.OutlineCz) / loc.OutlineRz);
        float p = 2f * MathF.Max(loc.Squareness, 1f);
        float e = MathF.Pow(MathF.Pow(dx, p) + MathF.Pow(dz, p), 1f / p);
        e += _noise.Get(x * 2f, z * 2f) * 0.06f * Smooth(8f, 40f, MathF.Abs(x));
        float inside = (1f - e) * MathF.Min(loc.OutlineRx, loc.OutlineRz);
        foreach (var (lx0, lz0, rx, rz) in _lobes)
        {
            float lx = (x - lx0) / rx, lz = (z - lz0) / rz;
            float le = MathF.Sqrt(lx * lx + lz * lz) + _noise.Get(x * 2.3f + 50f, z * 2.3f) * 0.08f;
            inside = MathF.Max(inside, (1f - le) * MathF.Min(rx, rz));
        }
        foreach (var (ix, iz, r) in _islands)
        {
            float d = MathF.Sqrt((x - ix) * (x - ix) + (z - iz) * (z - iz)) + _noise.Get(x * 4f, z * 4f + 90f) * r * 0.25f;
            inside = MathF.Min(inside, d - r);
        }
        return inside;
    }

    public float DepthAt(float x, float z)
    {
        var loc = Location;
        float inside = InsideMetres(x, z);
        if (inside <= 0f)
            return 0f;
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

    public float PlayDistance(float x, float z)
    {
        const float r = 60f;
        float cx = (XMin + XMax) * 0.5f, cz = (ZMin + ZMax) * 0.5f;
        float qx = MathF.Abs(x - cx) - (XMax - XMin) * 0.5f + r, qz = MathF.Abs(z - cz) - (ZMax - ZMin) * 0.5f + r;
        float outside = MathF.Sqrt(MathF.Max(qx, 0f) * MathF.Max(qx, 0f) + MathF.Max(qz, 0f) * MathF.Max(qz, 0f));
        return outside + MathF.Min(MathF.Max(qx, qz), 0f) - r + _noise.Get(x * 0.8f + 300f, z * 0.8f) * 7f;
    }

    public float RimAt(float x, float z)
    {
        float d = PlayDistance(x, z);
        if (d < -45f)
            return 0f;
        float t = Smooth(-40f, 170f, d);
        float n = _noise.Get(x * 0.45f + 77f, z * 0.45f - 31f);
        float ridge = 1f - MathF.Sqrt(n * n + 0.03f);
        float peaks = 0.5f + 0.45f * ridge * ridge + 0.12f * _noise.Get(x * 0.9f - 12f, z * 0.9f + 40f);
        float bank = 5f * Smooth(-14f, 12f, d);
        return Location.RimHeight * MathF.Pow(t, 1.6f) * peaks + bank;
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

    public int Seed
    {
        get
        {
            int h = 17;
            foreach (char c in Location.DisplayName)
                h = unchecked(h * 31 + c);
            return h;
        }
    }

    /// <summary>Domes on a grid of cells (hills, knolls, hollows share the shape, differ in size and odds).</summary>
    private float Domes(float x, float z, float cell, int seed, float odds, float rMin, float rSpan, float stretchMin,
        Func<float, float, float> height, bool max)
    {
        int cx = (int)MathF.Floor(x / cell), cz = (int)MathF.Floor(z / cell);
        float h = 0f;
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int ix = cx + dx, iz = cz + dz;
                if (Hash(ix, iz, 1, seed) > odds)
                    continue;
                float hx = (ix + Hash(ix, iz, 2, seed)) * cell, hz = (iz + Hash(ix, iz, 3, seed)) * cell;
                float radius = rMin + rSpan * Hash(ix, iz, 4, seed);
                float a = Hash(ix, iz, 6, seed) * MathF.PI, stretch = stretchMin + (1f - stretchMin) * Hash(ix, iz, 7, seed);
                float px = x - hx, pz = z - hz;
                float u = (px * MathF.Cos(a) + pz * MathF.Sin(a)) / radius;
                float v = (-px * MathF.Sin(a) + pz * MathF.Cos(a)) / (radius * stretch);
                float d2 = u * u + v * v;
                if (d2 >= 1f)
                    continue;
                float top = height(Hash(ix, iz, 5, seed), radius * stretch);
                float k = 1f - d2;
                if (max)
                    h = MathF.Max(h, top * k * k * (3f - 2f * k));
                else
                    h += top * k * k;
            }
        return h;
    }

    public float HillsAt(float x, float z)
    {
        float scale = 0.45f + 0.55f * Location.HillScale;
        return Domes(x, z, 46f, Seed, 0.75f, 12f, 26f, 0.55f,
            (r, size) => MathF.Min((1.2f + 8f * MathF.Pow(r, 1.5f)) * scale, 0.45f * size), false);
    }

    public float KnollsAt(float x, float z) => Domes(x, z, 19f, Seed + 7121, 0.66f, 5f, 8f, 0.5f,
        (r, size) => MathF.Min(0.4f + 1.6f * r, 0.25f * size), false);

    public float HollowsAt(float x, float z) => Domes(x, z, 31f, Seed + 3307, 0.5f, 6f, 12f, 0.55f,
        (r, size) => MathF.Min(0.5f + 2.2f * r, 0.22f * size), true);

    private float Unflattened(float x, float z)
    {
        float k = 1f;
        var p = new Vector2(x, z);
        foreach (var (c, r) in _flat)
            k = MathF.Min(k, Smooth(r, r + 12f, p.DistanceTo(c)));
        return k;
    }

    private float VillageShare(float x, float z) =>
        1f - Smooth(30f, 70f, new Vector2(x, z).DistanceTo(Location.PlazaCenter));

    /// <summary>The ground's height (negative = the lake bed under the water level 0), levelled under the village.</summary>
    public float HeightAt(float x, float z)
    {
        float h = NaturalHeight(x, z);
        if (h < 0f || _pads.Count == 0)
            return h;
        h = VillageGround(x, z, h);
        var p = new Vector2(x, z);
        foreach (var (c, r, height) in _pads)
        {
            float d = p.DistanceTo(c);
            float t = 1f - Smooth(r * 0.75f, r * 1.6f, d);
            if (t > 0f)
                h += (height - h) * t;
        }
        return h;
    }

    /// <summary>Terrain height without the levelled pads.</summary>
    public float NaturalHeight(float x, float z)
    {
        var loc = Location;
        float inside = InsideMetres(x, z);
        if (inside > 0f)
            return -DepthAt(x, z);
        float sd = -inside;
        float hill = loc.HillScale;
        float h = 1.05f * Smooth(0f, 4f, sd);
        h += 0.045f * MathF.Pow(Math.Clamp(sd - 8f, 0f, 90f), 1.25f) * hill;
        h += _noise.Get(x * 1.5f, z * 1.5f) * 3f * hill * Math.Clamp(sd / 40f, 0f, 1f);
        h += _noise.Get(x * 8f, z * 8f) * 0.4f * Math.Clamp(sd / 10f, 0f, 1f);
        h += _noise.Get(x * 3.6f + 51f, z * 3.6f - 17f) * 1.6f * Math.Clamp((sd - 4f) / 16f, 0f, 1f);
        float free = Unflattened(x, z);
        float village = VillageShare(x, z);
        h += HillsAt(x, z) * Smooth(8f, 30f, sd) * free * (1f - 0.5f * village);
        h += KnollsAt(x, z) * Smooth(4f, 12f, sd) * (0.35f + 0.65f * free);
        float hollow = HollowsAt(x, z) * Smooth(9f, 22f, sd) * free * (1f - 0.4f * village);
        h = MathF.Max(h - hollow, MathF.Min(h, 0.9f));
        h += RimAt(x, z);
        return h;
    }

    public static bool OnPier(float x, float z) => MathF.Abs(x) <= 1.6f && z >= -4.2f && z <= 6.2f;

    /// <summary>True where the angler may stand: on land, in the shallows, or on the pier.</summary>
    public bool IsWalkable(float x, float z, float wadeLimit = WadeDepth)
    {
        if (OnPier(x, z))
            return true;
        if (PlayDistance(x, z) > 0f)
            return false;
        return DepthAt(x, z) <= wadeLimit;
    }

    /// <summary>True where trees grow: forest noise over the threshold, dry ground, inside the rim.</summary>
    public bool IsForest(float x, float z, float h)
    {
        float threshold = -0.08f + (1f - Location.TreeDensity) * 0.5f;
        return h > 1.6f && h < Location.RockLine + 6f && ForestDensity(x, z) > threshold;
    }

    public Color ColorAt(float x, float z, float h)
    {
        var loc = Location;
        float n = _noise.Get(x * 5f, z * 5f) * 0.08f;
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
            if (IsForest(x, z, h))
                c = c.Lerp(new Color(0.16f, 0.17f, 0.09f), 0.55f);
            c = c.Lerp(new Color(0.45f, 0.43f, 0.40f), Math.Clamp((h - loc.RockLine) / 5f, 0f, 1f));
            c = c.Lerp(new Color(0.93f, 0.95f, 0.98f), Math.Clamp((h - loc.SnowLine) / 8f, 0f, 1f));
        }
        // Trodden paths and streets.
        float path = 1f - Smooth(0.9f, 1.9f, PathDistance(x, z));
        if (h >= 0.5f && path > 0f)
            c = c.Lerp(loc.PathColor, path * 0.92f);
        return new Color(c.R + n, c.G + n, c.B + n);
    }

    private static float SegmentDistance(float px, float pz, float ax, float az, float bx, float bz)
    {
        float vx = bx - ax, vz = bz - az;
        float t = Math.Clamp(((px - ax) * vx + (pz - az) * vz) / (vx * vx + vz * vz), 0f, 1f);
        float dx = px - (ax + vx * t), dz = pz - (az + vz * t);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public static float Smooth(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
