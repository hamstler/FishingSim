using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishingSim.Core.FishAI;

/// <summary>The water the fish live in: depth below the surface at a point (0 or less = land).</summary>
public interface IWaterVolume
{
    float DepthAt(float x, float z);
}

/// <summary>One visible fish. Position is horizontal (X, Z) plus <see cref="Depth"/> below the surface.</summary>
public sealed class FishAgent
{
    public int Id { get; init; }
    public string Species { get; init; } = "";
    public float Weight { get; init; }
    public int School { get; init; }
    public float MinDepth { get; init; } = 0.3f;
    public float MaxDepth { get; init; } = 6f;
    public float CruiseSpeed { get; init; } = 0.5f;

    /// <summary>Depth this fish likes to cruise at (inside its band; most fish keep to the upper half).</summary>
    public float Cruise { get; set; }

    public Vector2 Position;
    public Vector2 Velocity;
    public float Depth;

    /// <summary>Seconds of panic left after being spooked.</summary>
    public float Fear;

    internal float WanderAngle;

    public float Heading => MathF.Atan2(Velocity.X, Velocity.Y);
    public float Speed => Velocity.Length();
}

/// <summary>
/// Steering model for fish you can see in the water: wandering, shoaling with fish of the same school, keeping to
/// the species' depth band, avoiding the shore, and bolting away from a fast-moving or close angler. Pure logic
/// (System.Numerics), the scene node <c>World/FishPopulation</c> only draws the agents.
/// </summary>
public sealed class FishSwimModel
{
    public const float SchoolRadius = 4f;
    public const float SpookRadius = 6f;
    public const float MinWaterDepth = 0.35f;

    private readonly IRng _rng;
    private readonly IWaterVolume _water;
    private readonly List<FishAgent> _agents = new();
    private int _nextId;

    public IReadOnlyList<FishAgent> Agents => _agents;

    public FishSwimModel(IWaterVolume water, IRng rng)
    {
        _water = water;
        _rng = rng;
    }

    public void Clear() => _agents.Clear();

    public FishAgent Spawn(string species, float weight, Vector2 position, int school, float minDepth, float maxDepth, float cruiseSpeed)
    {
        float bottom = _water.DepthAt(position.X, position.Y);
        var agent = new FishAgent
        {
            Id = _nextId++,
            Species = species,
            Weight = weight,
            School = school,
            MinDepth = minDepth,
            MaxDepth = maxDepth,
            CruiseSpeed = cruiseSpeed,
            Position = position,
            WanderAngle = _rng.Range(0f, MathF.Tau),
        };
        agent.Cruise = minDepth + (maxDepth - minDepth) * 0.5f * _rng.NextFloat() * _rng.NextFloat();
        agent.Depth = PreferredDepth(agent, bottom);
        agent.Velocity = new Vector2(MathF.Sin(agent.WanderAngle), MathF.Cos(agent.WanderAngle)) * cruiseSpeed;
        _agents.Add(agent);
        return agent;
    }

    /// <summary>Depth the fish wants at a point: inside its band, but never touching the bottom.</summary>
    public static float PreferredDepth(FishAgent a, float bottom)
    {
        float floor = MathF.Max(bottom - 0.15f, 0.1f);
        float want = a.Cruise > 0f ? a.Cruise : (a.MinDepth + a.MaxDepth) * 0.5f;
        want = Math.Clamp(want, a.MinDepth, a.MaxDepth);
        return Math.Clamp(want, 0.1f, floor);
    }

    /// <summary>
    /// Advances every fish. <paramref name="threat"/> is the angler's position on the ground and
    /// <paramref name="threatSpeed"/> how fast they move (running on the shore spooks fish further away).
    /// </summary>
    public void Step(float dt, Vector2 threat, float threatSpeed)
    {
        if (dt <= 0f)
            return;
        float spook = SpookRadius * (0.5f + MathF.Min(threatSpeed / 3f, 1.5f));
        foreach (var a in _agents)
        {
            // Wander: a slowly drifting heading.
            a.WanderAngle += _rng.Range(-1.2f, 1.2f) * dt;
            var steer = new Vector2(MathF.Sin(a.WanderAngle), MathF.Cos(a.WanderAngle)) * a.CruiseSpeed;

            // Shoal with nearby fish of the same school (cohesion + alignment + separation).
            Vector2 centre = Vector2.Zero, heading = Vector2.Zero, push = Vector2.Zero;
            int mates = 0;
            foreach (var b in _agents)
            {
                if (ReferenceEquals(a, b) || b.School != a.School)
                    continue;
                var d = b.Position - a.Position;
                float dist = d.Length();
                if (dist > SchoolRadius)
                    continue;
                mates++;
                centre += b.Position;
                heading += b.Velocity;
                if (dist < 0.6f && dist > 1e-4f)
                    push -= d / (dist * dist) * 0.15f;
            }
            if (mates > 0)
            {
                steer += (centre / mates - a.Position) * 0.4f;
                steer += (heading / mates - a.Velocity) * 0.5f;
                steer += push;
            }

            // Flee the angler.
            var away = a.Position - threat;
            float threatDist = away.Length();
            if (threatDist < spook && threatDist > 1e-4f)
                a.Fear = MathF.Max(a.Fear, 1.5f + (spook - threatDist) * 0.3f);
            if (a.Fear > 0f)
            {
                a.Fear -= dt;
                if (threatDist > 1e-4f)
                    steer += away / threatDist * a.CruiseSpeed * 4f;
            }

            // Keep off the shore: look ahead and turn towards deeper water.
            var dir = a.Velocity.LengthSquared() > 1e-6f ? Vector2.Normalize(a.Velocity) : new Vector2(0f, -1f);
            var ahead = a.Position + dir * 1.5f;
            if (_water.DepthAt(ahead.X, ahead.Y) < MinWaterDepth + 0.2f)
                steer += DeeperDirection(a.Position) * a.CruiseSpeed * 3f;

            float maxSpeed = a.CruiseSpeed * (a.Fear > 0f ? 4f : 1.6f);
            var v = a.Velocity + steer * dt * 2f;
            float len = v.Length();
            if (len > maxSpeed)
                v *= maxSpeed / len;
            a.Velocity = v;

            var next = a.Position + v * dt;
            float bottom = _water.DepthAt(next.X, next.Y);
            if (bottom < MinWaterDepth)
            {
                // Never beach: bounce back instead of moving onto land.
                a.Velocity = -a.Velocity * 0.5f;
                a.WanderAngle += MathF.PI;
                bottom = _water.DepthAt(a.Position.X, a.Position.Y);
            }
            else
            {
                a.Position = next;
            }

            float want = PreferredDepth(a, bottom);
            a.Depth += (want - a.Depth) * Math.Clamp(dt * 0.8f, 0f, 1f);
            a.Depth = Math.Clamp(a.Depth, 0.05f, MathF.Max(bottom - 0.05f, 0.05f));
        }
    }

    private Vector2 DeeperDirection(Vector2 p)
    {
        Vector2 best = Vector2.Zero;
        float bestDepth = float.MinValue;
        for (int k = 0; k < 8; k++)
        {
            float ang = k / 8f * MathF.Tau;
            var d = new Vector2(MathF.Sin(ang), MathF.Cos(ang));
            float depth = _water.DepthAt(p.X + d.X * 3f, p.Y + d.Y * 3f);
            if (depth > bestDepth)
            {
                bestDepth = depth;
                best = d;
            }
        }
        return best;
    }
}
