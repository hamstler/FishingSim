using System.Collections.Generic;
using Godot;
using FishingSim.Data;

namespace FishingSim.World;

/// <summary>A flat, levelled circle of ground under a building.</summary>
public readonly record struct Pad(Vector2 Center, float Radius, float Height);

/// <summary>A straight piece of dirt path.</summary>
public readonly record struct PathSegment(Vector2 A, Vector2 B)
{
    /// <summary>The point of the segment nearest to <paramref name="p"/>.</summary>
    public Vector2 Closest(Vector2 p)
    {
        var ab = B - A;
        float len2 = ab.LengthSquared();
        float t = len2 < 1e-6f ? 0f : Mathf.Clamp((p - A).Dot(ab) / len2, 0f, 1f);
        return A + ab * t;
    }

    public float DistanceTo(Vector2 p)
    {
        var ab = B - A;
        float len2 = ab.LengthSquared();
        float t = len2 < 1e-6f ? 0f : Mathf.Clamp((p - A).Dot(ab) / len2, 0f, 1f);
        return p.DistanceTo(A + ab * t);
    }
}

/// <summary>Everything the terrain needs to know about a map's village: buildings, levelled pads and paths.</summary>
public sealed class MapLayout
{
    /// <summary>Where the pier meets the land; every map's main path starts here.</summary>
    public static readonly Vector2 PierRoot = new(0f, 7f);

    public List<BuildingSpec> Specs { get; } = new();
    public List<Pad> Pads { get; } = new();
    public List<PathSegment> Paths { get; } = new();

    /// <summary>Height (m) of the village square; the village ground eases to it.</summary>
    public float PlazaHeight { get; set; }

    /// <summary>Distance (m) from the nearest path, or a large number when there are none.</summary>
    public float PathDistance(float x, float z)
    {
        var p = new Vector2(x, z);
        float best = 1e6f;
        foreach (var seg in Paths)
            best = Mathf.Min(best, seg.DistanceTo(p));
        return best;
    }

    /// <summary>Distance (m) from the edge of the nearest pad (negative inside one).</summary>
    public float PadDistance(float x, float z)
    {
        var p = new Vector2(x, z);
        float best = 1e6f;
        foreach (var pad in Pads)
            best = Mathf.Min(best, p.DistanceTo(pad.Center) - pad.Radius);
        return best;
    }
}
