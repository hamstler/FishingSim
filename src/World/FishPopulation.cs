using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Core;
using FishingSim.Core.FishAI;
using FishingSim.Data;
using FishingSim.Fishing;

namespace FishingSim.World;

/// <summary>
/// Fish you can see in the water. Schools are spawned from the map's species abundance and swim with
/// <see cref="FishSwimModel"/>; this node only draws them with <see cref="FishModel"/>. A fish is shown when it is
/// near the angler and shallow enough to be seen through the surface (polarized glasses extend both).
/// These are ambient fish: bites still come from <c>BiteModel</c> (see docs/ROZWOJ.md for linking the two).
/// </summary>
public partial class FishPopulation : Node3D
{
    public const int MaxFish = 150;
    public const float BaseVisibleRange = 28f;
    public const float BaseVisibleDepth = 1.4f;

    /// <summary>Fish seen through the water are drawn a little larger so they read at pixel-art resolution.</summary>
    public const float ViewScale = 1.4f;

    private sealed class BedWater : IWaterVolume
    {
        private readonly LakeBed _bed;
        public BedWater(LakeBed bed) => _bed = bed;
        public float DepthAt(float x, float z) => _bed.DepthAt(x, z);
    }

    private readonly IRng _rng = new SystemRng();
    private readonly Dictionary<int, FishModel> _views = new();
    private FishSwimModel _model = null!;
    private LakeBed _bed = null!;
    private Node3D _watcher = null!;
    private IReadOnlyList<FishSpecies> _species = Array.Empty<FishSpecies>();
    private float _waterY;
    private Vector3 _lastWatcher;

    /// <summary>Horizontal distance (m) within which fish are drawn.</summary>
    public float VisibleRange { get; set; } = BaseVisibleRange;

    /// <summary>Fish deeper than this (m) are hidden by the water.</summary>
    public float VisibleDepth { get; set; } = BaseVisibleDepth;

    /// <summary>How clear the lake is (metres you can see into; per map), as the water shader uses it.</summary>
    public float Clarity { get; set; } = 2.6f;

    /// <summary>0..1: polarized glasses cut the surface glare (as in the water shader).</summary>
    public float GlareCut { get; set; }

    /// <summary>
    /// How well the angler can see a fish through the surface (0..1), as the water shows it: the surface reflects more
    /// the flatter you look across it (Fresnel; polarized glasses cut part of that) and that glare drowns out what
    /// shows through; the water swallows the light along the path to the fish (deeper, further, murkier = less).
    /// From the bank a shallow fish shows to about 12 m, from the higher pier further, with polarized glasses about
    /// twice as far.
    /// </summary>
    public static float Sight(Vector3 eye, Vector3 fish, float waterY, float depth, float clarity, float glareCut)
    {
        float h = MathF.Max(eye.Y - waterY, 0.3f);
        float across = new Vector2(fish.X - eye.X, fish.Z - eye.Z).Length();
        float cosI = h / MathF.Sqrt(h * h + across * across);
        float fresnel = (0.02f + 0.98f * MathF.Pow(1f - cosI, 5f)) * (1f - 0.6f * glareCut);
        float sinT = MathF.Sqrt(MathF.Max(0f, 1f - cosI * cosI)) / 1.33f;
        float cosT = MathF.Sqrt(MathF.Max(0f, 1f - sinT * sinT));
        float path = MathF.Max(depth, 0f) / MathF.Max(cosT, 0.2f);
        // What comes through, less the sky's glare on the surface that drowns it out.
        return MathF.Max(0f, (1f - fresnel) * MathF.Exp(-path * 1.4f / MathF.Max(clarity, 0.3f)) - 0.5f * fresnel);
    }

    /// <summary>Below this much of it seen, a fish is not drawn at all.</summary>
    public const float MinSight = 0.08f;

    public IReadOnlyList<FishAgent> Agents => _model.Agents;

    public int VisibleCount => _views.Values.Count(v => v.Visible);

    public void Setup(LakeBed bed, Node3D watcher, IReadOnlyList<FishSpecies> species, float waterY)
    {
        _bed = bed;
        _watcher = watcher;
        _species = species;
        _waterY = waterY;
        _model = new FishSwimModel(new BedWater(bed), _rng);
        bed.Rebuilt += Populate;
    }

    public override void _Ready() => Populate();

    public override void _ExitTree()
    {
        if (_bed != null)
            _bed.Rebuilt -= Populate;
    }

    /// <summary>Clears the fish and spawns new schools for the current map.</summary>
    public void Populate()
    {
        foreach (var v in _views.Values)
            v.QueueFree();
        _views.Clear();
        _model.Clear();
        int school = 0;
        Graphics.FishSkin.PrewarmLight(_species.Where(sp => _bed.AbundanceOf(sp.SpeciesName) > 0f));
        float totalAbundance = _species.Sum(sp => MathF.Max(_bed.AbundanceOf(sp.SpeciesName), 0f));
        foreach (var sp in _species)
        {
            float abundance = _bed.AbundanceOf(sp.SpeciesName);
            if (abundance <= 0f)
                continue;
            // Every species gets its share of the fish budget, so common shoals do not crowd out the predators.
            int budget = _model.Agents.Count + Math.Max(2, (int)MathF.Round(MaxFish * abundance / totalAbundance));
            bool shoaling = sp.MaxWeight < 4f;          // roach and bream shoal, carp and pike are loners or pairs
            int schools = Math.Max(1, (int)MathF.Round(abundance * (shoaling ? 3f : 4f)));
            for (int s = 0; s < schools && _model.Agents.Count < budget; s++, school++)
            {
                if (FindWater(sp, nearPier: s % 2 == 0, out var centre) is false)
                    continue;
                int size = shoaling ? 4 + (int)_rng.Range(0f, 5f) : 1 + (int)_rng.Range(0f, 2f);
                for (int k = 0; k < size && _model.Agents.Count < budget; k++)
                {
                    var p = centre + new System.Numerics.Vector2(_rng.Range(-1.5f, 1.5f), _rng.Range(-1.5f, 1.5f));
                    if (_bed.DepthAt(p.X, p.Y) < FishSwimModel.MinWaterDepth)
                        p = centre;
                    // Mostly small and medium fish, now and then a big one (the same fish can later take your bait).
                    float weight = sp.MinWeight + (sp.MaxWeight - sp.MinWeight) * MathF.Pow(_rng.NextFloat(), 2.2f);
                    float cruise = 0.35f + 0.25f * sp.Aggression;
                    var agent = _model.Spawn(sp.SpeciesName, weight, p, school, sp.MinDepth, sp.MaxDepth, cruise);
                    var view = FishModel.Create(sp, weight);
                    view.Scale = Vector3.One * ViewScale;
                    view.Visible = false;
                    AddChild(view);
                    _views[agent.Id] = view;
                }
            }
        }
    }

    // ------------------------------------------------------------------ bites from the fish you see
    private readonly Dictionary<string, FishSpecies> _byName = new();

    public FishSpecies? SpeciesOf(FishAgent agent)
    {
        if (_byName.Count == 0)
            foreach (var sp in _species)
                _byName[sp.SpeciesName] = sp;
        return _byName.TryGetValue(agent.Species, out var s) ? s : null;
    }

    /// <summary>Puts a bait in the water (or takes it out with null): interested fish nearby swim to it.</summary>
    public void SetBait(Vector3? at, Func<FishSpecies, float>? interest)
    {
        if (_model == null)
            return;
        _model.Attractor = at is Vector3 p ? new System.Numerics.Vector2(p.X, p.Z) : null;
        _model.Interest = interest == null ? null : a => SpeciesOf(a) is FishSpecies sp ? interest(sp) : 0f;
    }

    /// <summary>A splash at a point: the fish you can see near it dart away.</summary>
    public void Scare(Vector3 at, float radius, float seconds) =>
        _model?.Scare(new System.Numerics.Vector2(at.X, at.Z), radius, seconds);

    /// <summary>The fish near the bait that takes it, weighted by how much each species wants this bait; null = none.</summary>
    public FishAgent? PickBiter(Vector3 at, float radius, Func<FishSpecies, float> weight) =>
        _model?.PickNear(new System.Numerics.Vector2(at.X, at.Z), radius, a => SpeciesOf(a) is FishSpecies sp ? weight(sp) : 0f);

    /// <summary>The fish is on the hook: hide it here, the fight draws its own model.</summary>
    public void Hook(FishAgent agent)
    {
        agent.Hooked = true;
        if (_views.TryGetValue(agent.Id, out var v))
            v.Visible = false;
    }

    /// <summary>The fish got away (or let go of the bait): it swims off in fright from where it was.</summary>
    public void Release(FishAgent agent, Vector3? at = null)
    {
        agent.Hooked = false;
        agent.Fear = 5f;
        if (at is Vector3 p && _bed.DepthAt(p.X, p.Z) >= FishSwimModel.MinWaterDepth)
            agent.Position = new System.Numerics.Vector2(p.X, p.Z);
    }

    /// <summary>The fish was landed: it leaves the lake, and another of its kind appears somewhere out of sight.</summary>
    public void Catch(FishAgent agent)
    {
        _model.Remove(agent);
        if (_views.Remove(agent.Id, out var v))
            v.QueueFree();
        var sp = SpeciesOf(agent);
        if (sp == null || !FindWater(sp, nearPier: false, out var spot))
            return;
        float weight = sp.MinWeight + (sp.MaxWeight - sp.MinWeight) * MathF.Pow(_rng.NextFloat(), 2.2f);
        var fresh = _model.Spawn(sp.SpeciesName, weight, spot, agent.School, sp.MinDepth, sp.MaxDepth, agent.CruiseSpeed);
        var view = FishModel.Create(sp, weight);
        view.Scale = Vector3.One * ViewScale;
        view.Visible = false;
        AddChild(view);
        _views[fresh.Id] = view;
    }

    /// <summary>
    /// Random spot with water inside the species' depth band. Every other school starts near the pier and the village
    /// shore, where the angler spends most time, so there is usually something to see.
    /// </summary>
    private bool FindWater(FishSpecies sp, bool nearPier, out System.Numerics.Vector2 point)
    {
        for (int attempt = 0; attempt < 80; attempt++)
        {
            bool near = nearPier && attempt < 50;
            float x = near ? _rng.Range(-35f, 35f) : _rng.Range(-120f, 120f);
            float z = near ? _rng.Range(-30f, -2f) : _rng.Range(-150f, -2f);
            float d = _bed.DepthAt(x, z);
            if (d >= MathF.Max(sp.MinDepth, FishSwimModel.MinWaterDepth + 0.2f) && d <= sp.MaxDepth + 2f)
            {
                point = new System.Numerics.Vector2(x, z);
                return true;
            }
        }
        point = default;
        return false;
    }

    private float _recycleTimer;

    /// <summary>
    /// Keeps the water around the angler alive: a school that drifted far away (and cannot be seen) is moved to fresh
    /// water 6-28 m from the angler that suits its depth band. The population size stays the same.
    /// </summary>
    private void RecycleFarSchools(System.Numerics.Vector2 watcher)
    {
        foreach (var group in _model.Agents.GroupBy(a => a.School))
        {
            var members = group.ToList();
            var centre = System.Numerics.Vector2.Zero;
            foreach (var m in members)
                centre += m.Position;
            centre /= members.Count;
            if (System.Numerics.Vector2.Distance(centre, watcher) < RecycleDistance)
                continue;
            var first = members[0];
            for (int attempt = 0; attempt < 20; attempt++)
            {
                float ang = _rng.Range(0f, MathF.Tau), r = _rng.Range(6f, 28f);
                var target = watcher + new System.Numerics.Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r;
                float d = _bed.DepthAt(target.X, target.Y);
                if (d < MathF.Max(first.MinDepth, FishSwimModel.MinWaterDepth + 0.2f) || d > first.MaxDepth + 2f)
                    continue;
                foreach (var m in members)
                {
                    var p = target + (m.Position - centre) * 0.3f;
                    m.Position = _bed.DepthAt(p.X, p.Y) >= FishSwimModel.MinWaterDepth ? p : target;
                    m.Fear = 0f;
                }
                break;
            }
        }
    }

    /// <summary>Schools further than this from the angler are moved closer (out of sight).</summary>
    public const float RecycleDistance = 60f;

    /// <summary>
    /// The schools swim and their fish are placed once per rendered frame (not on physics ticks: they move smoothly at
    /// any frame rate, and a slow frame is not made slower by catching up several ticks of fish).
    /// </summary>
    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("fish");
        if (_watcher == null || !IsInstanceValid(_watcher))
            return;
        float dt = MathF.Min((float)delta, 0.05f);
        var w = _watcher.GlobalPosition;
        float speed = dt > 0f ? new Vector2(w.X - _lastWatcher.X, w.Z - _lastWatcher.Z).Length() / dt : 0f;
        _lastWatcher = w;
        _model.Step(dt, new System.Numerics.Vector2(w.X, w.Z), MathF.Min(speed, 10f));
        _recycleTimer -= dt;
        if (_recycleTimer <= 0f)
        {
            _recycleTimer = 0.5f;
            RecycleFarSchools(new System.Numerics.Vector2(w.X, w.Z));
        }

        // Only the fish the angler could really see are drawn: within reach, not too deep, and seen through the surface
        // (steep views near the bank show them, flat views further out only reflect the sky).
        var cam = GetViewport()?.GetCamera3D();
        var eye = cam != null ? cam.GlobalPosition : w + Vector3.Up * 1.6f;
        foreach (var a in _model.Agents)
        {
            if (!_views.TryGetValue(a.Id, out var view))
                continue;
            float dist = new Vector2(a.Position.X - w.X, a.Position.Y - w.Z).Length();
            var at = new Vector3(a.Position.X, _waterY - a.Depth, a.Position.Y);
            bool visible = dist < VisibleRange && a.Depth < VisibleDepth
                           && Sight(eye, at, _waterY, a.Depth, Clarity, GlareCut) > MinSight;
            view.Visible = visible;
            if (!visible)
                continue;
            view.Position = at;
            view.Rotation = new Vector3(0f, a.Heading, 0f);
            view.Wiggle = 0.4f + MathF.Min(a.Speed, 2f);
        }
    }
}
