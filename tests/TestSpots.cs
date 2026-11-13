using System;
using Godot;
using FishingSim.World;

namespace FishingSim.Tests;

/// <summary>Places to stand for tests and screenshots.</summary>
public static class TestSpots
{
    /// <summary>
    /// Where to stand with a lift net: walking in from far out towards the lake's middle (islands would stop a walk
    /// outwards), the first walkable spot (on the bank or wading in the shallows) with water at least
    /// <paramref name="minDepth"/> deep a lift net's reach further in; near the pier when there is a choice. Returns the
    /// spot and the way to the water, or null.
    /// </summary>
    public static (Vector3 Stand, Vector3 ToWater)? LiftNetBank(LakeBed lake, float minDepth = 0.35f)
    {
        var centre = new Vector2(0f, lake.Location.OutlineCz);
        (Vector3, Vector3)? best = null;
        float bestScore = float.MaxValue;
        for (float a = 0f; a < Mathf.Tau; a += 0.04f)
        {
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            bool reachedWater = false;
            for (float r = 320f; r > 5f; r -= 0.4f)
            {
                var stand = centre + dir * r;
                float h = lake.HeightAt(stand.X, stand.Y);
                reachedWater |= h < 0.1f;
                if (!reachedWater)
                    continue;
                if (!lake.IsWalkable(stand.X, stand.Y) || LakeBed.OnPier(stand.X, stand.Y))
                    break;
                var net = stand - dir * 2.5f;
                float depth = -lake.HeightAt(net.X, net.Y);
                if (depth < minDepth)
                    continue;
                float score = stand.DistanceTo(new Vector2(0f, 8f)) * 0.01f + MathF.Max(0f, -h) * 2f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = (new Vector3(stand.X, h, stand.Y), new Vector3(-dir.X, 0f, -dir.Y));
                }
                break;
            }
        }
        return best;
    }
}
