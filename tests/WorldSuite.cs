using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FishingSim.Core;
using FishingSim.Data;
using FishingSim.World;

namespace FishingSim.Tests;

/// <summary>Terrain, depth, time of day, weather and waves.</summary>
public sealed class WorldSuite : ITestSuite
{
    public string Name => "world";

    public Task Run(TestContext t)
    {
        var locations = LakeBed.LocationPaths.Select(p => GD.Load<LocationData>(p)).ToList();
        var bed = new LakeBed();
        foreach (var l in locations)
            bed.Locations.Add(l);

        t.Section("depth and terrain");
        for (int i = 0; i < locations.Count; i++)
        {
            bed.SelectInitial(i);
            string name = locations[i].DisplayName;
            t.Check(bed.DepthAt(0f, 6f) == 0f, $"{name}: the shore end of the pier is on land");
            t.Check(bed.DepthAt(0f, -4f) >= 0.2f && bed.DepthAt(0f, -4f) <= 2.1f, $"{name}: shallow water at the pier end ({bed.DepthAt(0f, -4f):0.00} m)");
            t.Check(bed.DepthAt(0f, -30f) > bed.DepthAt(0f, -8f), $"{name}: deeper further out");

            float maxJump = 0f, maxDepth = 0f;
            for (float z = -60f; z < 0f; z += 2.5f)
            {
                for (float x = -20f; x <= 20f; x += 2.5f)
                {
                    maxDepth = MathF.Max(maxDepth, bed.DepthAt(x, z));
                    maxJump = MathF.Max(maxJump, MathF.Abs(bed.DepthAt(x, z) - bed.DepthAt(x, z - 2.5f)));
                }
            }
            t.Check(maxJump < 1.2f, $"{name}: the bed has no cliffs (largest step {maxJump:0.00} m per 2.5 m, under 26 degrees)");
            t.Check(maxDepth <= locations[i].MaxDepth + locations[i].HoleDepth + 0.1f, $"{name}: never deeper than max depth + hole ({maxDepth:0.0} m)");

            // The terrain is continuous across the waterline: height just inside ~ -small depth, just outside ~ small.
            float edgeZ = 20f;
            while (edgeZ > -200f && bed.DepthAt(0f, edgeZ) <= 0f) edgeZ -= 0.25f;
            t.Check(MathF.Abs(bed.HeightAt(0f, edgeZ) - bed.HeightAt(0f, edgeZ + 0.25f)) < 0.5f, $"{name}: no cliff at the waterline");
            t.Check(bed.HeightAt(0f, 6f) is > 0.7f and < 1.4f, $"{name}: land by the pier is about pier height ({bed.HeightAt(0f, 6f):0.00} m)");
            t.Check(bed.HeightAt(150f, 30f) > 3f, $"{name}: hills rise away from the shore");
        }
        t.Check(bed.AbundanceOf("Karp") == locations[bed.LocationIndex].AbundanceOf("Karp"), "abundance follows the selected location");

        t.Section("day and night");
        t.Near(DayClock.RealSecondsUntil(6f, 18f) / 60f, 40f, 0.2f, "the day lasts 40 real minutes");
        t.Near(DayClock.RealSecondsUntil(18f, 6f) / 60f, 20f, 0.2f, "the night lasts 20 real minutes");
        float clock = 17.5f;
        for (int i = 0; i < 60 * 60; i++)
            clock = DayClock.Advance(clock, 1f);
        t.Near(clock, 17.5f, 0.01f, "a full cycle takes one real hour");
        t.Near(DayClock.Advance(17.9f, 60f), 18.4f, 0.01f, "time runs twice as fast after dusk (17:54 + 1 min = 18:24)");

        t.Section("every map has its own shape");
        foreach (var loc in locations)
        {
            bool lobesWet = loc.Lobes.All(e => LocationData.Numbers(e) is var n && bed.DepthAt(loc, n[0], n[1]) > 0.5f);
            bool islandsDry = loc.Islands.All(e => LocationData.Numbers(e) is var n && bed.DepthAt(loc, n[0], n[1]) == 0f && bed.HeightAt(loc, n[0], n[1]) > 0f);
            t.Check(loc.Lobes.Length > 0 && lobesWet && islandsDry, $"{loc.DisplayName}: {loc.Lobes.Length} bays hold water, {loc.Islands.Length} islands are dry land");
        }
        var stawLoc = locations.First(l => l.Squareness > 1.5f);
        // Near a corner of the bounding box: outside a round lake, inside a dug, squarish fish pond.
        float cx = stawLoc.OutlineRx * 0.8f, cz = stawLoc.OutlineCz - stawLoc.OutlineRz * 0.8f;
        t.Check(bed.DepthAt(stawLoc, cx, cz) > 0.5f, $"{stawLoc.DisplayName}: a square fish pond, water reaches its corners");
        var shapes = locations.Select(l => string.Concat(Enumerable.Range(0, 64).Select(k =>
            bed.DepthAt(l, -150f + (k % 8) * 40f, -190f + (k / 8) * 25f) > 0f ? '1' : '0'))).ToList();
        t.Check(shapes.Distinct().Count() == shapes.Count, "no two maps share the same water outline");

        t.Section("location switching");
        bed.SelectInitial(0);
        int unlocked = locations.Count(l => l.RequiredLevel <= 1);
        bool changed = bed.CycleLocation(1);
        t.Check(unlocked < 2 || (changed && bed.Location.RequiredLevel <= 1), "cycling at level 1 only reaches level-1 maps");
        t.Check(locations.Any(l => l.RequiredLevel > 1), "at least one map is locked at level 1");
        bed.SelectInitial(0);
        int visited = 0;
        for (int i = 0; i < 6; i++)
        {
            if (bed.CycleLocation(99)) visited++;
        }
        t.Check(visited >= 3, "at a high level every map is reachable");
        bed.QueueFree();

        t.Section("time and weather");
        var world = new WorldState { Hour = 23.5f };
        world.Advance(1f);
        t.Near(world.Hour, 0.5f, 0.001f, "the clock wraps past midnight");
        world.Hour = 4.9f;
        t.Check(world.Period == DayPeriod.Night, "04:54 is night");
        world.Hour = 5.1f;
        t.Check(world.Period == DayPeriod.Twilight, "05:06 is twilight");
        world.Hour = 12f;
        t.Check(world.Period == DayPeriod.Day, "noon is day");
        world.Hour = 19f;
        t.Check(world.Period == DayPeriod.Twilight, "19:00 is twilight");
        world.Hour = 21f;
        t.Check(world.Period == DayPeriod.Night, "21:00 is night");
        world.Hour = 7.25f;
        t.Check(world.TimeText == "07:15", "time text is HH:MM");
        world.Hour = 12f;
        t.Near(world.Daylight, 1f, 0.001f, "full daylight at noon");
        world.Hour = 0f;
        t.Near(world.Daylight, 0f, 0.001f, "no daylight at midnight");
        world.Weather = Weather.Clear;
        var seen = new System.Collections.Generic.HashSet<Weather> { world.Weather };
        for (int i = 0; i < 3; i++)
        {
            world.CycleWeather();
            seen.Add(world.Weather);
        }
        t.Check(seen.Count == 3 && world.Weather == Weather.Clear, "cycling weather visits all three and wraps");
        world.Weather = Weather.Rain;
        t.Check(world.RainTarget == 1f && world.CloudTarget == 1f && world.WindTarget > 0.8f, "rain means full cloud, rain and strong wind");
        world.Weather = Weather.Clear;
        t.Check(world.RainTarget == 0f && world.CloudTarget < 0.1f, "clear weather means no rain");
        var rolls = new System.Collections.Generic.HashSet<Weather>();
        world.Paused = false;
        for (int i = 0; i < 400; i++)
        {
            world.Advance(3f);
            rolls.Add(world.Weather);
        }
        t.Check(rolls.Count == 3, "random weather rolls reach every kind");
        world.Free();

        t.Section("waves");
        var water = new Water { Wind = 0.1f };
        float calm = water.Amplitude;
        water.Wind = 1f;
        float stormy = water.Amplitude;
        t.Check(stormy > calm * 3f && stormy < 0.1f, $"wind raises the waves ({calm:0.000} m -> {stormy:0.000} m)");
        float peak = Enumerable.Range(0, 400).Max(i => MathF.Abs(water.HeightAt(i * 0.7f, i * 0.3f)));
        t.Check(peak <= stormy + 1e-5f, "wave height never exceeds the amplitude");
        water.Free();
        return Task.CompletedTask;
    }
}
