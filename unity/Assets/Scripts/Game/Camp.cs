using System.Collections.Generic;
using System.Linq;
using FishingSim.Core;
using FishingSim.Core.Camp;
using UnityEngine;
using Mathf = UnityEngine.Mathf;

namespace FishingSim.Game
{
    /// <summary>
    /// Camp life (Core/Camp): digging for worms with the shovel, sweeping insects with the net, the lift net for live
    /// bait, forest pickings (porcini, chanterelles, blueberries round the trees, growing back after a game day), the
    /// angler's campfire by the pier and the village hearth, and the village notice board.
    /// </summary>
    public sealed class Camp : MonoBehaviour
    {
        public sealed class Patch
        {
            public ForageKind Kind;
            public Vector3 At;
            public GameObject Shape = null!;
            public float PickedAt = -1000f;
        }

        public List<Patch> Patches { get; } = new();
        public List<Campfire> Fires { get; } = new();
        public Vector3 Board { get; private set; }

        private readonly List<Vector3> _dug = new();
        private readonly SystemRng _rng = new();
        private float _liftDownAt = -1f;
        private Vector3 _liftAt;

        public bool LiftDown => _liftDownAt >= 0f;

        public void Build(Lake lake)
        {
            foreach (Transform c in transform)
                Destroy(c.gameObject);
            Patches.Clear();
            Fires.Clear();
            _dug.Clear();
            _liftDownAt = -1f;
            // the angler's campfire on dry ground near the pier, the village hearth by the square
            if (Spot(lake, new Vector3(0f, 0f, 7f), 4f, 14f) is { } camp)
                Fires.Add(Campfire.Create(transform, camp, "Twoje ognisko", true));
            var plaza = lake.Location.PlazaCenter;
            if (Spot(lake, new Vector3(plaza.X + 7f, 0f, plaza.Y + 5f), 0f, 10f, allowBuilt: true) is { } hearth)
                Fires.Add(Campfire.Create(transform, hearth, "Ognisko we wsi", true));
            // the notice board on the square
            var board = new Vector3(plaza.X - 4f, 0f, plaza.Y + 2f);
            board.y = lake.HeightAt(board.x, board.z);
            Board = board;
            var k = new MeshKit();
            var wood = Col.Lin(0.36f, 0.27f, 0.18f);
            k.Tube(new Vector3(-0.8f, 0f, 0f), new Vector3(-0.8f, 2.2f, 0f), 0.06f, 0.05f, 6, wood, wood, true);
            k.Tube(new Vector3(0.8f, 0f, 0f), new Vector3(0.8f, 2.2f, 0f), 0.06f, 0.05f, 6, wood, wood, true);
            k.Box(new Vector3(0f, 1.5f, 0f), new Vector3(1.8f, 1.1f, 0.06f), Quaternion.identity, wood);
            k.Box(new Vector3(0f, 2.25f, 0.05f), new Vector3(2f, 0.08f, 0.4f), Quaternion.Euler(-12f, 0f, 0f), wood * 0.8f);
            for (int i = 0; i < 5; i++)
                k.Box(new Vector3(-0.6f + i * 0.3f, 1.5f + (i % 2) * 0.2f, 0.04f), new Vector3(0.24f, 0.3f, 0.01f), Quaternion.Euler(0, 0, i * 4 - 8), Col.Lin(0.88f, 0.85f, 0.74f));
            Shape(transform, "NoticeBoard", k, board, Quaternion.Euler(0f, 180f, 0f));
            // forest pickings round the trees inside the map
            var rng = new System.Random(lake.Location.DisplayName.Length * 7919);
            for (int tries = 0; tries < 4000 && Patches.Count < 42; tries++)
            {
                float x = Mathf.Lerp(Lake.XMin + 20f, Lake.XMax - 20f, (float)rng.NextDouble());
                float z = Mathf.Lerp(Lake.ZMin + 20f, Lake.ZMax - 10f, (float)rng.NextDouble());
                float h = lake.HeightAt(x, z);
                if (h < 1.3f || lake.PlayDistance(x, z) > -15f || lake.Built(x, z) || !lake.IsForest(x, z, h))
                    continue;
                var kind = Foraging.KindFor(0.8f, (float)rng.NextDouble(), _rng);
                var at = new Vector3(x, h, z);
                Patches.Add(new Patch { Kind = kind, At = at, Shape = ForageShape(kind, at, rng.Next()) });
            }
        }

        private static Vector3? Spot(Lake lake, Vector3 around, float minH, float radius, bool allowBuilt = false)
        {
            for (float r = 0f; r <= radius; r += 1f)
                for (int k = 0; k < 12; k++)
                {
                    float a = k / 12f * Mathf.PI * 2f;
                    float x = around.x + Mathf.Cos(a) * r, z = around.z + Mathf.Sin(a) * r;
                    float h = lake.HeightAt(x, z);
                    if (h > Mathf.Max(minH, 1.05f) && lake.IsWalkable(x, z) && (allowBuilt || !lake.Built(x, z, 1f)) && !Lake.OnPier(x, z))
                        return new Vector3(x, h, z);
                }
            return null;
        }

        private GameObject Shape(Transform parent, string name, MeshKit k, Vector3 at, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(at, rot);
            go.AddComponent<MeshFilter>().sharedMesh = k.ToMesh(name);
            go.AddComponent<MeshRenderer>().sharedMaterial = Materials.Lit(0.2f, 0.2f);
            return go;
        }

        private GameObject ForageShape(ForageKind kind, Vector3 at, int seed)
        {
            var k = new MeshKit();
            var rng = new System.Random(seed);
            switch (kind)
            {
                case ForageKind.Blueberry:
                    k.Blob(new Vector3(0f, 0.18f, 0f), new Vector3(0.45f, 0.22f, 0.45f), seed, 0.5f, Col.Lin(0.2f, 0.32f, 0.12f), Col.Lin(0.08f, 0.14f, 0.05f), true, 1);
                    for (int i = 0; i < 14; i++)
                        k.Blob(new Vector3((float)rng.NextDouble() - 0.5f, 0.22f + (float)rng.NextDouble() * 0.15f, (float)rng.NextDouble() - 0.5f) * 0.8f,
                            Vector3.one * 0.025f, i, 0f, Col.Lin(0.15f, 0.2f, 0.45f), Col.Lin(0.05f, 0.07f, 0.2f), true, 0);
                    break;
                default:
                {
                    bool porcini = kind == ForageKind.Porcini;
                    var cap = porcini ? Col.Lin(0.45f, 0.28f, 0.14f) : Col.Lin(0.92f, 0.62f, 0.12f);
                    var stem = porcini ? Col.Lin(0.85f, 0.8f, 0.68f) : Col.Lin(0.92f, 0.7f, 0.25f);
                    for (int i = 0; i < 3; i++)
                    {
                        var c = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.4f;
                        float s = 0.6f + (float)rng.NextDouble() * 0.5f;
                        k.Tube(c, c + Vector3.up * 0.09f * s, (porcini ? 0.03f : 0.012f) * s, (porcini ? 0.022f : 0.02f) * s, 8, stem, stem, false);
                        k.Blob(c + Vector3.up * 0.1f * s, new Vector3(0.06f, porcini ? 0.035f : 0.02f, 0.06f) * s, i, 0.1f, cap, cap * 0.6f, true, 1);
                    }
                    break;
                }
            }
            return Shape(transform, "Forage" + kind, k, at, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
        }

        /// <summary>Patches grow back after a game day.</summary>
        public void Regrow(float clock)
        {
            foreach (var p in Patches)
                p.Shape.SetActive(Foraging.Regrown(p.PickedAt, clock));
        }

        public Patch? NearPatch(Vector3 at, float clock) =>
            Patches.Where(p => Foraging.Regrown(p.PickedAt, clock) && Vector3.Distance(p.At, at) < 1.8f)
                .OrderBy(p => Vector3.Distance(p.At, at)).FirstOrDefault();

        public string Pick(Patch p, Profile profile, bool wet)
        {
            var haul = Foraging.Pick(p.Kind, wet, _rng);
            foreach (var (id, n) in haul.Baits)
                profile.Pantry[id] = profile.Pantry.GetValueOrDefault(id) + n;
            p.PickedAt = profile.Clock;
            p.Shape.SetActive(false);
            return haul.Message;
        }

        // ------------------------------------------------------------------ tools
        public GroundKind GroundAt(Lake lake, float x, float z)
        {
            float h = lake.HeightAt(x, z);
            if (h < 0.03f) return GroundKind.Water;
            if (h > lake.Location.RockLine) return GroundKind.Rock;
            if (lake.PathDistance(x, z) < 1.2f) return GroundKind.Path;
            if (h < 0.7f) return GroundKind.Sand;
            if (h < 1.3f) return GroundKind.Mud;
            return lake.IsForest(x, z, h) ? GroundKind.Forest : GroundKind.Meadow;
        }

        public string Dig(Lake lake, Vector3 at, float hour, Weather weather, Profile profile)
        {
            var ground = GroundAt(lake, at.x, at.z);
            if (!Digging.CanDig(ground))
                return ground switch { GroundKind.Water => "Tu jest woda.", GroundKind.Rock => "Skała: łopata nie wejdzie.", _ => "Ubita ścieżka: tu nic nie ma." };
            bool used = _dug.Any(d => Vector3.Distance(d, at) < 1.2f);
            _dug.Add(at);
            var haul = Digging.Dig(ground, hour, weather, used, _rng);
            Add(profile, haul);
            return haul.Message;
        }

        public string Sweep(Lake lake, Vector3 at, float hour, Weather weather, Profile profile)
        {
            var ground = GroundAt(lake, at.x, at.z);
            bool nearWater = lake.InsideMetres(at.x, at.z) > -6f;
            bool nearTrees = lake.IsForest(at.x, at.z, lake.HeightAt(at.x, at.z)) || lake.ForestDensity(at.x, at.z) > -0.2f;
            float swarm = nearWater ? Sweeping.MayflyActivity(hour, weather) : 0f;
            var haul = Sweeping.Sweep(new SweepContext(ground, nearWater, nearTrees, hour, weather, swarm), _rng);
            Add(profile, haul);
            return haul.Message;
        }

        /// <summary>Lowers the lift net into the water ahead, or lifts it.</summary>
        public string LiftNet(Lake lake, Vector3 ahead, float hour, Weather weather, Profile profile)
        {
            if (_liftDownAt < 0f)
            {
                float depth = lake.DepthAt(ahead.x, ahead.z);
                if (depth < LiftNetting.MinDepth)
                    return "Za płytko: podrywkę opuść do wody (z pomostu albo z płycizny).";
                _liftDownAt = Time.time;
                _liftAt = ahead;
                return "Podrywka w wodzie. Podnieś ją za chwilę (ten sam klawisz).";
            }
            float soak = Time.time - _liftDownAt;
            _liftDownAt = -1f;
            var c = new LiftContext(soak, lake.DepthAt(_liftAt.x, _liftAt.z), hour, weather, 0.3f, false);
            var haul = LiftNetting.Lift(c, _rng);
            Add(profile, haul);
            return haul.Message;
        }

        private static void Add(Profile profile, Haul haul)
        {
            foreach (var (id, n) in haul.Baits)
                profile.Baits[id] = profile.BaitCount(id) + n;
        }

        // ------------------------------------------------------------------ notice board
        public static List<VillageRequest> Notices(Lake lake, Profile profile, int place)
        {
            var fish = Tackle.AllSpecies.Select(sp => new RequestFish(sp.SpeciesName, sp.LegalWeight, sp.PricePerKg,
                    lake.Location.AbundanceOf(sp.SpeciesName) * sp.Rarity))
                .Where(f => f.Commonness > 0.05f).ToList();
            return NoticeBoard.Daily((int)(profile.Clock / 24f), place, fish, true);
        }
    }
}
