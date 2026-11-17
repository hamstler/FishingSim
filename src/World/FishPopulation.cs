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

    public int VisibleCount => _views.Values.Count(v => v.Visible) + _close.Values.Count(v => v.Visible);

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
        foreach (var v in _close.Values)
            v.QueueFree();
        _close.Clear();
        _closeSeen.Clear();
        _model.Clear();
        int school = 0;
        Graphics.FishSkin.PrewarmLight(_species.Where(sp => _bed.AbundanceOf(sp.SpeciesName) > 0f));
        // The species' hand-built models in light copies: decoded in the background, worn as soon as they are ready.
        _pendingLite.Clear();
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
                    var view = FishModel.Create(sp, weight, lite: Fishing.FishAsset.LiteIfReady(sp, out _));
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
        if (_close.TryGetValue(agent.Id, out var c))
            c.Visible = false;
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
        if (_close.Remove(agent.Id, out var c))
            c.QueueFree();
        _closeSeen.Remove(agent.Id);
        var sp = SpeciesOf(agent);
        if (sp == null || !FindWater(sp, nearPier: false, out var spot))
            return;
        float weight = sp.MinWeight + (sp.MaxWeight - sp.MinWeight) * MathF.Pow(_rng.NextFloat(), 2.2f);
        var fresh = _model.Spawn(sp.SpeciesName, weight, spot, agent.School, sp.MinDepth, sp.MaxDepth, agent.CruiseSpeed);
        var view = FishModel.Create(sp, weight, lite: Fishing.FishAsset.LiteIfReady(sp, out _));
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
    private readonly List<FishSpecies> _pendingLite = new();

    /// <summary>
    /// One species a frame: when its model's light copy is decoded, its fish change from the lofted shape to the model
    /// (same place, heading and visibility).
    /// </summary>
    private void UpgradeOneSpecies()
    {
        for (int i = 0; i < _pendingLite.Count; i++)
        {
            var sp = _pendingLite[i];
            var lite = Fishing.FishAsset.LiteIfReady(sp, out bool pending);
            if (pending)
                continue;
            _pendingLite.RemoveAt(i);
            if (lite == null)
                return;
            foreach (var a in _model.Agents)
            {
                if (a.Species != sp.SpeciesName || !_views.TryGetValue(a.Id, out var old))
                    continue;
                var view = FishModel.Create(sp, a.Weight, lite: lite);
                view.Scale = old.Scale;
                view.Visible = old.Visible;
                view.Position = old.Position;
                view.Rotation = old.Rotation;
                AddChild(view);
                _views[a.Id] = view;
                old.QueueFree();
            }
            return;
        }
    }

    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("fish");
        if (_watcher == null || !IsInstanceValid(_watcher))
            return;
        if (_pendingLite.Count > 0)
            UpgradeOneSpecies();
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
        // Of those, only the nearest MaxShown are drawn, and on screen each wears the very model of the catch panel and
        // the atlas (FishModel detailed: the species' hand-built model at full quality). The light lofted views stand
        // in only without a GPU (tests).
        int shown = 0;
        foreach (var a in _model.Agents)
        {
            if (!_views.TryGetValue(a.Id, out var view))
                continue;
            view.Visible = false;
            if (a.Hooked)
                continue;
            float dist = new Vector2(a.Position.X - w.X, a.Position.Y - w.Z).Length();
            var at = new Vector3(a.Position.X, _waterY - a.Depth, a.Position.Y);
            // A big fish shows from deeper (its dark back against the bottom) than a small one.
            bool visible = dist < VisibleRange && a.Depth < VisibleDepth + view.Length * 1.5f
                           && Sight(eye, at, _waterY, a.Depth, Clarity, GlareCut) > MinSight
                           && view.Length * ViewScale >= MinShownLength;
            if (visible)
                InsertNearest(dist, a, ref shown);
        }
        _frameClock += dt;
        bool full = !Graphics.Renderer.IsHeadless;
        bool madeOne = false, askedOne = false;
        for (int i = 0; i < shown; i++)
        {
            var a = _nearest[i].Agent;
            FishModel? model;
            if (!full)
                model = _views[a.Id];
            else if (!_close.TryGetValue(a.Id, out model))
            {
                // One new close model a frame, once its species' pictures are decoded (Prefetch: a worker thread).
                var sp = SpeciesOf(a);
                if (sp == null)
                    continue;
                // One preparation asked for a frame (the nearest fish first); the rest wait their turn.
                if (!askedOne && !FishAsset.IsReady(sp))
                {
                    FishAsset.Prefetch(sp);
                    askedOne = true;
                }
                if (madeOne || !FishAsset.IsReady(sp))
                    continue;
                model = FishModel.Create(sp, a.Weight, detailed: true);
                model.Scale = Vector3.One * ViewScale;
                AddChild(model);
                _close[a.Id] = model;
                madeOne = true;
            }
            model.Position = new Vector3(a.Position.X, _waterY - a.Depth, a.Position.Y);
            model.Rotation = new Vector3(0f, a.Heading, 0f);
            model.Wiggle = 0.4f + MathF.Min(a.Speed, 2f);
            model.Visible = true;
            _closeSeen[a.Id] = _frameClock;
        }
        // Close models not shown this frame are hidden, and let go after a few seconds out of sight.
        if (full && _close.Count > 0)
        {
            _drop.Clear();
            foreach (var (id, model) in _close)
            {
                if (_closeSeen.TryGetValue(id, out float seen) && seen >= _frameClock)
                    continue;
                model.Visible = false;
                if (_frameClock - seen > 4f)
                    _drop.Add(id);
            }
            foreach (int id in _drop)
            {
                _close.Remove(id, out var m);
                m?.QueueFree();
                _closeSeen.Remove(id);
            }
        }
    }

    private readonly Dictionary<int, FishModel> _close = new();
    private readonly Dictionary<int, float> _closeSeen = new();
    private readonly List<int> _drop = new();
    private float _frameClock;

    /// <summary>Fry shorter than this (as drawn, m) are not shown: a few centimetres of fish read as specks, not fish.</summary>
    public float MinShownLength { get; set; } = 0.16f;

    /// <summary>How many fish are drawn at most (the nearest ones the angler can see).</summary>
    public int MaxShown { get; set; } = 14;

    private (float Dist, FishAgent Agent)[] _nearest = new (float, FishAgent)[14];

    /// <summary>Keeps the MaxShown nearest in _nearest, sorted, without allocating.</summary>
    private void InsertNearest(float dist, FishAgent agent, ref int count)
    {
        int cap = Math.Min(MaxShown, 4096);
        if (_nearest.Length < cap)
            _nearest = new (float, FishAgent)[cap];
        if (count == cap && dist >= _nearest[count - 1].Dist)
            return;
        int i = count < cap ? count++ : count - 1;
        while (i > 0 && _nearest[i - 1].Dist > dist)
        {
            _nearest[i] = _nearest[i - 1];
            i--;
        }
        _nearest[i] = (dist, agent);
    }
}
