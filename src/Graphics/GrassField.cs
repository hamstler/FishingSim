using System;
using Godot;
using FishingSim.Data;
using FishingSim.World;
using FishingSim.World.Props;

namespace FishingSim.Graphics;

/// <summary>
/// Dense grass blades around the player (the terrain shader carries the look further away). Clumps sit on a world
/// grid with hashed jitter, so re-centring as the player walks does not make the grass jump. Only grassy ground
/// gets blades: not sand, paths, building pads, steep rock or water. Radius and density follow the graphics quality.
/// </summary>
public partial class GrassField : Node3D
{
    private const float RecenterDistance = 7f;

    private LakeBed _bed = null!;
    private Node3D _follow = null!;
    private MultiMeshInstance3D _instance = null!;
    private Vector2 _centre = new(float.MaxValue, float.MaxValue);

    public int Count => _instance?.Multimesh?.InstanceCount ?? 0;

    public void Setup(LakeBed bed, Node3D follow)
    {
        _bed = bed;
        _follow = follow;
    }

    public override void _Ready()
    {
        _instance = new MultiMeshInstance3D
        {
            MaterialOverride = TreeFactory.GrassMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_instance);
        _bed.Rebuilt += Invalidate;
        GraphicsQuality.Changed += Invalidate;
    }

    public override void _ExitTree()
    {
        _bed.Rebuilt -= Invalidate;
        GraphicsQuality.Changed -= Invalidate;
    }

    private void Invalidate() => _centre = new Vector2(float.MaxValue, float.MaxValue);

    public override void _Process(double delta)
    {
        using var perfScope = Perf.Scope("grass");
        if (!IsInstanceValid(_follow))
            return;
        if (_job is { IsCompleted: true })
        {
            var job = _job;
            _job = null;
            if (job.IsCompletedSuccessfully && job.Result.Centre == _centre)
                Apply(job.Result.Buffer, job.Result.Count);
        }
        var p = new Vector2(_follow.GlobalPosition.X, _follow.GlobalPosition.Z);
        if (p.DistanceTo(_centre) > RecenterDistance)
        {
            // The very first field is built at once; later ones are placed on a worker thread (the ground's height,
            // colour and kind for twenty thousand cells took several milliseconds a frame while walking) and replace
            // the old field when complete. One build at a time: a newer centre waits for the running one.
            if (_instance.Multimesh == null)
                Build(p);
            else if (_job == null)
            {
                _centre = p;
                var loc = _bed.Location;
                float radius = GraphicsQuality.GrassRadius, spacing = GraphicsQuality.GrassSpacing;
                _workBed ??= new LakeBed();
                var bed = _workBed;
                _job = System.Threading.Tasks.Task.Run(() => Compute(bed, loc, p, radius, spacing));
            }
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            _job?.Wait();
            _workBed?.Free();
            _workBed = null;
        }
    }

    private static float Hash(int x, int z, int k)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(k * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    private sealed record Field(Vector2 Centre, float[] Buffer, int Count);

    private System.Threading.Tasks.Task<Field>? _job;
    // The worker's own terrain sampler (LakeBed keeps noise objects and caches that are not shared across threads).
    private LakeBed? _workBed;

    /// <summary>Places the clumps around <paramref name="centre"/> right away.</summary>
    public void Build(Vector2 centre)
    {
        _job?.Wait();
        _job = null;
        _centre = centre;
        if (GraphicsQuality.GrassRadius <= 0f || Renderer.IsHeadless)
        {
            _instance.Multimesh = null;
            return;
        }
        var field = Compute(_bed, _bed.Location, centre, GraphicsQuality.GrassRadius, GraphicsQuality.GrassSpacing);
        Apply(field.Buffer, field.Count);
    }

    /// <summary>
    /// The clumps round a centre as one MultiMesh buffer (12 floats of transform and 4 of colour each). Pure: runs on
    /// a worker thread with its own <paramref name="bed"/>.
    /// </summary>
    private static Field Compute(LakeBed bed, LocationData loc, Vector2 centre, float radius, float spacing)
    {
        var layout = bed.LayoutFor(loc);
        var data = new System.Collections.Generic.List<float>(65536);
        int x0 = (int)MathF.Floor((centre.X - radius) / spacing), x1 = (int)MathF.Ceiling((centre.X + radius) / spacing);
        int z0 = (int)MathF.Floor((centre.Y - radius) / spacing), z1 = (int)MathF.Ceiling((centre.Y + radius) / spacing);
        for (int iz = z0; iz <= z1; iz++)
        {
            for (int ix = x0; ix <= x1; ix++)
            {
                float x = (ix + Hash(ix, iz, 1)) * spacing, z = (iz + Hash(ix, iz, 2)) * spacing;
                float d = new Vector2(x - centre.X, z - centre.Y).Length();
                if (d > radius)
                    continue;
                // Thin out towards the edge so the field fades into the terrain texture.
                float edge = Math.Clamp((d - radius * 0.55f) / (radius * 0.45f), 0f, 1f);
                if (Hash(ix, iz, 3) < edge * 0.75f)
                    continue;
                float h = bed.HeightAt(loc, x, z);
                if (h < 0.6f || LakeBed.OnPier(x, z))
                    continue;
                if (layout.PathDistance(x, z) < 1.1f || layout.PadDistance(x, z) < 0.5f)
                    continue;
                float slope = MathF.Abs(bed.HeightAt(loc, x + 0.6f, z) - h) + MathF.Abs(bed.HeightAt(loc, x, z + 0.6f) - h);
                if (slope > 0.55f)
                    continue;
                var c = bed.ColorAt(loc, x, z, h);
                if (c.G < c.R * 1.02f || c.G < c.B)          // sand, path, rock or snow
                    continue;
                // Only the meadow gets blades; under the trees (forest floor) just a few.
                var ground = bed.GroundAt(loc, x, z, h);
                if (1f - ground.X - ground.Y - ground.Z - ground.W < 0.5f && (ground.Z < 0.4f || Hash(ix, iz, 8) > 0.2f))
                    continue;
                float s = (0.7f + 0.6f * Hash(ix, iz, 4)) * (1f - edge * 0.5f);
                var b = new Basis(Vector3.Up, Hash(ix, iz, 5) * Mathf.Tau).Scaled(new Vector3(s, s * (0.8f + 0.5f * Hash(ix, iz, 6)), s));
                float tone = (Hash(ix, iz, 7) - 0.5f) * 0.12f;
                data.Add(b.X.X); data.Add(b.Y.X); data.Add(b.Z.X); data.Add(x);
                data.Add(b.X.Y); data.Add(b.Y.Y); data.Add(b.Z.Y); data.Add(h - 0.02f);
                data.Add(b.X.Z); data.Add(b.Y.Z); data.Add(b.Z.Z); data.Add(z);
                data.Add(c.R * 1.05f + tone); data.Add(c.G * 1.08f + tone); data.Add(c.B + tone * 0.5f); data.Add(1f);
            }
        }
        int n = data.Count / 16;
        if (System.Environment.GetEnvironmentVariable("FISHSIM_GRASS_DEBUG") == "1")
            GD.Print($"grass: {n} clumps within {radius:0} m of {centre}");
        // The capacity is rounded up (only the first n are drawn) so the engine's buffer keeps its size.
        int capacity = Math.Max(2048, (n + 2047) / 2048 * 2048);
        var buffer = new float[capacity * 16];
        data.CopyTo(buffer);
        return new Field(centre, buffer, n);
    }

    /// <summary>The new field replaces the old one (main thread).</summary>
    private void Apply(float[] buffer, int n)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = TreeFactory.MeadowClump(),
            InstanceCount = buffer.Length / 16,
            VisibleInstanceCount = n,
        };
        mm.Buffer = buffer;
        // The old field is let go at once: left to the garbage collector its GPU buffer would linger, one more for
        // every few steps the angler takes.
        var old = _instance.Multimesh;
        _instance.Multimesh = mm;
        old?.Dispose();
    }
}
