using System;
using System.Collections.Generic;
using FishingSim.Core;
using FishingSim.Core.FishAI;
using FishingSim.Data;
using UnityEngine;
using Mathf = UnityEngine.Mathf;
using Vec2 = System.Numerics.Vector2;

namespace FishingSim.Game
{
    /// <summary>
    /// The fish you can see in the lake (World/FishPopulation): schools per species by the map's abundance swimming in
    /// Core's FishSwimModel, spooked by the angler; only the 14 nearest the angler can really see are drawn (through the
    /// surface: Fresnel glare vs. what comes up through the water), each wearing the species' model.
    /// </summary>
    public sealed class Swimmers : MonoBehaviour
    {
        public const int MaxFish = 150, MaxShown = 14;
        public const float VisibleRange = 28f, VisibleDepth = 1.4f, ViewScale = 1.4f, MinShownLength = 0.16f, MinSight = 0.08f;

        public Transform? Watcher { get; set; }
        public float Clarity { get; set; } = 3f;

        private FishSwimModel? _model;
        private Lake? _lake;
        private readonly IRng _rng = new SystemRng(4242);
        private readonly Dictionary<string, FishSpecies> _byName = new();
        private readonly Dictionary<int, GameObject> _shown = new();
        private readonly Dictionary<int, float> _seen = new();
        private readonly (float Dist, FishAgent? Agent)[] _nearest = new (float, FishAgent?)[MaxShown];
        private readonly List<int> _drop = new();
        private float _clock;
        private Vector3 _lastWatcher;

        private sealed class Water : IWaterVolume
        {
            private readonly Lake _lake;
            public Water(Lake lake) => _lake = lake;
            public float DepthAt(float x, float z) => _lake.DepthAt(x, z);
        }

        public void Populate(Lake lake, IReadOnlyList<FishSpecies> species)
        {
            foreach (var go in _shown.Values)
                Destroy(go);
            _shown.Clear();
            _seen.Clear();
            _byName.Clear();
            _lake = lake;
            _model = new FishSwimModel(new Water(lake), _rng);
            float total = 0f;
            foreach (var sp in species)
            {
                _byName[sp.SpeciesName] = sp;
                total += Mathf.Max(lake.Location.AbundanceOf(sp.SpeciesName), 0f);
            }
            if (total <= 0f)
                return;
            int school = 0;
            foreach (var sp in species)
            {
                float abundance = lake.Location.AbundanceOf(sp.SpeciesName);
                if (abundance <= 0f)
                    continue;
                ModelLibrary.Start("fish/" + Id(sp));
                int budget = _model.Agents.Count + Math.Max(2, (int)MathF.Round(MaxFish * abundance / total));
                bool shoaling = sp.MaxWeight < 4f;
                int schools = Math.Max(1, (int)MathF.Round(abundance * (shoaling ? 3f : 4f)));
                for (int s = 0; s < schools && _model.Agents.Count < budget; s++, school++)
                {
                    if (!FindWater(sp, s % 2 == 0, out var centre))
                        continue;
                    int size = shoaling ? 4 + (int)_rng.Range(0f, 5f) : 1 + (int)_rng.Range(0f, 2f);
                    for (int k = 0; k < size && _model.Agents.Count < budget; k++)
                    {
                        var p = centre + new Vec2(_rng.Range(-1.5f, 1.5f), _rng.Range(-1.5f, 1.5f));
                        if (lake.DepthAt(p.X, p.Y) < FishSwimModel.MinWaterDepth)
                            p = centre;
                        float weight = sp.MinWeight + (sp.MaxWeight - sp.MinWeight) * MathF.Pow(_rng.NextFloat(), 2.2f);
                        _model.Spawn(sp.SpeciesName, weight, p, school, sp.MinDepth, sp.MaxDepth, 0.35f + 0.25f * sp.Aggression);
                    }
                }
            }
        }

        private static string Id(FishSpecies sp) => System.IO.Path.GetFileNameWithoutExtension(sp.ResourcePath);

        private bool FindWater(FishSpecies sp, bool nearPier, out Vec2 point)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                bool near = nearPier && attempt < 50;
                float x = near ? _rng.Range(-35f, 35f) : _rng.Range(-120f, 120f);
                float z = near ? _rng.Range(-30f, -2f) : _rng.Range(-150f, -2f);
                float d = _lake!.DepthAt(x, z);
                if (d >= MathF.Max(sp.MinDepth, FishSwimModel.MinWaterDepth + 0.2f) && d <= sp.MaxDepth + 2f)
                {
                    point = new Vec2(x, z);
                    return true;
                }
            }
            point = default;
            return false;
        }

        public static float Sight(Vector3 eye, Vector3 fish, float depth, float clarity)
        {
            float h = MathF.Max(eye.y, 0.3f);
            float across = new Vector2(fish.x - eye.x, fish.z - eye.z).magnitude;
            float cosI = h / MathF.Sqrt(h * h + across * across);
            float fresnel = 0.02f + 0.98f * MathF.Pow(1f - cosI, 5f);
            float sinT = MathF.Sqrt(MathF.Max(0f, 1f - cosI * cosI)) / 1.33f;
            float cosT = MathF.Sqrt(MathF.Max(0f, 1f - sinT * sinT));
            float path = MathF.Max(depth, 0f) / MathF.Max(cosT, 0.2f);
            return MathF.Max(0f, (1f - fresnel) * MathF.Exp(-path * 1.4f / MathF.Max(clarity, 0.3f)) - 0.5f * fresnel);
        }

        private float LengthOf(FishAgent a) => _byName.TryGetValue(a.Species, out var sp) ? sp.LengthRef * Mathf.Pow(Mathf.Max(a.Weight, 0.005f), 0.33f) : 0.3f;

        private void Update()
        {
            if (_model == null || Watcher == null || _lake == null)
                return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            var w = Watcher.position;
            float speed = (w - _lastWatcher).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            _lastWatcher = w;
            _model.Step(dt, new Vec2(w.x, w.z), Mathf.Min(speed, 8f));

            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : w + Vector3.up * 1.6f;
            int shown = 0;
            foreach (var a in _model.Agents)
            {
                if (a.Hooked)
                    continue;
                float dist = new Vector2(a.Position.X - w.x, a.Position.Y - w.z).magnitude;
                if (dist > VisibleRange)
                    continue;
                float len = LengthOf(a);
                var at = new Vector3(a.Position.X, -a.Depth, a.Position.Y);
                if (a.Depth < VisibleDepth + len * 1.5f && Sight(eye, at, a.Depth, Clarity) > MinSight && len * ViewScale >= MinShownLength)
                    Insert(dist, a, ref shown);
            }
            _clock += dt;
            bool madeOne = false;
            for (int i = 0; i < shown; i++)
            {
                var a = _nearest[i].Agent!;
                if (!_shown.TryGetValue(a.Id, out var go))
                {
                    if (madeOne || !_byName.TryGetValue(a.Species, out var sp))
                        continue;
                    string id = Id(sp);
                    if (!ModelLibrary.Has("fish/" + id))
                        continue;
                    go = ModelLibrary.Fish(id);
                    if (go == null)
                        continue;
                    go.transform.SetParent(transform, false);
                    go.transform.localScale = Vector3.one * LengthOf(a) * ViewScale;
                    _shown[a.Id] = go;
                    madeOne = true;
                }
                go.SetActive(true);
                // a gentle swimming sway of the body round its heading
                float sway = Mathf.Sin(_clock * (4f + Mathf.Min(a.Speed, 2f) * 4f) + a.Id) * (4f + a.Speed * 6f);
                go.transform.SetPositionAndRotation(new Vector3(a.Position.X, -a.Depth, a.Position.Y), Quaternion.Euler(0f, a.Heading * Mathf.Rad2Deg + sway, 0f));
                _seen[a.Id] = _clock;
            }
            _drop.Clear();
            foreach (var (id, go) in _shown)
            {
                if (_seen.TryGetValue(id, out float seen) && seen >= _clock)
                    continue;
                go.SetActive(false);
                if (_clock - seen > 4f)
                    _drop.Add(id);
            }
            foreach (int id in _drop)
            {
                if (_shown.Remove(id, out var go))
                    Destroy(go);
                _seen.Remove(id);
            }
        }

        /// <summary>Keeps the MaxShown nearest, sorted, without allocating.</summary>
        private void Insert(float dist, FishAgent a, ref int count)
        {
            int i;
            if (count < MaxShown)
                i = count++;
            else
            {
                i = MaxShown - 1;
                if (_nearest[i].Dist <= dist)
                    return;
            }
            while (i > 0 && _nearest[i - 1].Dist > dist)
            {
                _nearest[i] = _nearest[i - 1];
                i--;
            }
            _nearest[i] = (dist, a);
        }
    }
}
