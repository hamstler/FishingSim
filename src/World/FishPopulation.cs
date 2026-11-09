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
    public const int MaxFish = 48;
    public const float BaseVisibleRange = 20f;
    public const float BaseVisibleDepth = 1.4f;

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

    /// <summary>Colour the water casts on fish below the surface (set per map).</summary>
    public Color Tint { get; set; } = new(0.62f, 0.78f, 0.74f);

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
                    float weight = _rng.Range(sp.MinWeight, MathF.Max(sp.MinWeight, sp.MaxWeight * 0.6f));
                    float cruise = 0.35f + 0.25f * sp.Aggression;
                    var agent = _model.Spawn(sp.SpeciesName, weight, p, school, sp.MinDepth, sp.MaxDepth, cruise);
                    var view = FishModel.Create(sp, weight);
                    view.Visible = false;
                    AddChild(view);
                    _views[agent.Id] = view;
                }
            }
        }
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

    public override void _PhysicsProcess(double delta)
    {
        if (_watcher == null || !IsInstanceValid(_watcher))
            return;
        float dt = (float)delta;
        var w = _watcher.GlobalPosition;
        float speed = dt > 0f ? new Vector2(w.X - _lastWatcher.X, w.Z - _lastWatcher.Z).Length() / dt : 0f;
        _lastWatcher = w;
        _model.Step(dt, new System.Numerics.Vector2(w.X, w.Z), MathF.Min(speed, 10f));

        foreach (var a in _model.Agents)
        {
            if (!_views.TryGetValue(a.Id, out var view))
                continue;
            float dist = new Vector2(a.Position.X - w.X, a.Position.Y - w.Z).Length();
            bool visible = dist < VisibleRange && a.Depth < VisibleDepth;
            view.Visible = visible;
            if (!visible)
                continue;
            view.Position = new Vector3(a.Position.X, _waterY - a.Depth, a.Position.Y);
            view.Rotation = new Vector3(0f, a.Heading, 0f);
            view.Wiggle = 0.4f + MathF.Min(a.Speed, 2f);
            float clarity = (1f - a.Depth / VisibleDepth) * (1f - 0.5f * MathF.Max(dist / VisibleRange - 0.6f, 0f) / 0.4f);
            view.SetUnderwater(Tint, clarity);
        }
    }
}
