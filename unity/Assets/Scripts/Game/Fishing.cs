using System;
using System.Collections.Generic;
using System.Linq;
using FishingSim.Core;
using FishingSim.Core.Methods;
using FishingSim.Data;
using UnityEngine;

namespace FishingSim.Game
{
    public enum RodState { Idle, Charging, Flying, Waiting, Nibble, Bite, Fight, Landed }

    /// <summary>
    /// The rod in hand: casting, the float/feeder/lure in the water, bites (Core's BiteModel with the map, bait, hook,
    /// depth, time and weather), the strike and the fight (Core's FightModel with the rod, reel, line and hook), landing,
    /// and broken tackle. Visuals: the rod bending under load, the line with sag, the float riding the waves, the fish.
    /// LMB: hold to charge the cast, hold to reel. RMB: strike, hold to lift in the fight. Wheel: drag.
    /// </summary>
    public sealed class Fishing : MonoBehaviour
    {
        public Player Player { get; set; } = null!;
        public WorldView World { get; set; } = null!;
        public DayCycle Day { get; set; } = null!;
        public Profile Profile { get; set; } = null!;

        public RodState State { get; private set; } = RodState.Idle;
        public FishingMethod Method { get; private set; }
        public float CastPower { get; private set; }
        public float DragSetting { get; set; } = 0.55f;
        public FightModel? Fight { get; private set; }
        public FishSpecies? Species { get; private set; }
        public float Weight { get; private set; }
        public float EffectiveLimit { get; private set; } = 3f;
        public float Distance => _bobber != null ? Flat(_bobber.position - Player.transform.position).magnitude : 0f;
        public float DepthHere { get; private set; }
        public bool Blocked { get; set; }

        /// <summary>Messages for the HUD ("Za wcześnie", "Żyłka pękła"...).</summary>
        public event Action<string>? Said;
        public event Action? Landed;

        /// <summary>Sounds of the rod (splash, strike, catch, line snap...).</summary>
        public event Action<Core.Audio.OneShot, float>? Cue;

        /// <summary>The reel turns (line out and the left button held).</summary>
        public bool Reeling { get; private set; }

        /// <summary>A well-fed angler waits less for a bite (the meal's buff).</summary>
        public Func<float> BiteBonus { get; set; } = () => 0f;

        private readonly SystemRng _rng = new();
        private Transform _rodRoot = null!, _tip = null!;
        private readonly List<Transform> _segments = new();
        private Transform _bobber = null!, _fish = null!;
        private MeshFilter _fishMesh = null!;
        private Renderer _bobberRenderer = null!;
        private LineRenderer _line = null!;
        private readonly Vector3[] _linePts = new Vector3[24];
        private Vector3 _velocity, _origin;
        private float _stateTime, _biteTimer, _nibbleLeft, _dip, _bend, _lineSlack = 1f, _reelAngle;
        private bool _readyToStrike;
        private float _hookHold = 0.8f;

        private RodData? Rod => Tackle.Rod(Profile.Rod);
        private ReelData? Reel => Tackle.Reel(Profile.Reel);
        private LineData? LineItem => Tackle.Line(Profile.Line);
        private HookData? Hook => Tackle.Hook(Profile.Hook);
        private BaitData? Bait => Tackle.Bait(Profile.Bait);

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        private void Start()
        {
            BuildRod();
            BuildBobber();
            BuildLine();
            var fish = new GameObject("HookedFish");
            _fish = fish.transform;
            var built = new GameObject("Built");
            built.transform.SetParent(_fish, false);
            _fishMesh = built.AddComponent<MeshFilter>();
            built.AddComponent<MeshRenderer>().sharedMaterial = Materials.Lit(0.55f, 0.08f);
            fish.SetActive(false);
            ApplyTackle();
            ShowRod(Player.enabled);
        }

        // ------------------------------------------------------------------ visuals
        // The rod is built in the Godot game's RodRig frame (the blank along +Y from the butt at -0.33, the reel seat
        // at 0.1, the blank from 0.25 in ten sections, the reel hanging at -Z), so the user's Meshy rod and reel models
        // (laid in that frame by tools/assets/meshy_rod_split.py) fit as they are. _frame turns it into the camera's
        // rod root: +Y along the rod (+Z), the reel side down.
        private const float ButtY = -0.33f, BlankStart = 0.25f, SegmentLength = 0.3f;
        private Transform _frame = null!, _procHandle = null!, _rodModel = null!, _reelModel = null!;
        private readonly List<GameObject> _procBlank = new();
        private Transform? _rotor, _crank;

        /// <summary>Where the angler's hands go (AnglerArms): the handle above the seat, the crank's knob.</summary>
        public Transform GripRight { get; private set; } = null!;
        public Transform GripLeft { get; private set; } = null!;
        public bool RodVisible => _rodRoot != null && _rodRoot.gameObject.activeInHierarchy;
        private string _shownRod = "", _shownReel = "";

        private void BuildRod()
        {
            var root = new GameObject("Rod").transform;
            root.SetParent(Player.Camera.transform, false);
            root.localPosition = new Vector3(0.26f, -0.38f, 0.5f);
            root.localRotation = Quaternion.Euler(-32f, -6f, 0f);
            _rodRoot = root;
            _frame = new GameObject("RigFrame").transform;
            _frame.SetParent(root, false);
            _frame.localRotation = Quaternion.AngleAxis(180f, new Vector3(0f, 1f, 1f).normalized);
            var mat = Materials.Lit(0.6f, 0.05f);
            var cork = Col.Lin(0.62f, 0.5f, 0.34f);
            var dark = Col.Lin(0.12f, 0.12f, 0.13f);
            var metal = Col.Lin(0.55f, 0.56f, 0.58f);
            // built handle and reel (used without the models)
            var handle = new MeshKit();
            handle.Tube(new Vector3(0, ButtY, 0), new Vector3(0, BlankStart, 0), 0.016f, 0.0135f, 10, cork, cork, true);
            handle.Tube(new Vector3(0, 0.1f, -0.016f), new Vector3(0, 0.1f, -0.065f), 0.005f, 0.005f, 6, dark, dark);
            handle.Blob(new Vector3(0, 0.096f, -0.088f), new Vector3(0.028f, 0.034f, 0.03f), 1, 0f, dark, dark * 0.7f, true, 1);
            handle.Tube(new Vector3(0, 0.125f, -0.088f), new Vector3(0, 0.165f, -0.088f), 0.024f, 0.024f, 12, metal, metal, true);
            handle.Tube(new Vector3(0.03f, 0.09f, -0.088f), new Vector3(0.055f, 0.09f, -0.088f), 0.004f, 0.004f, 6, metal, metal, true);
            _procHandle = new GameObject("ProcHandle").transform;
            _procHandle.SetParent(_frame, false);
            Part(_procHandle, "Handle", handle.ToMesh("Handle"), mat);
            _rodModel = new GameObject("RodModel").transform;
            _rodModel.SetParent(_frame, false);
            _reelModel = new GameObject("ReelModel").transform;
            _reelModel.SetParent(_frame, false);
            // the blank in ten sections that bend
            Transform parent = _frame;
            var blank = Col.Lin(0.18f, 0.1f, 0.08f);
            for (int i = 0; i < 10; i++)
            {
                var s = new GameObject($"Seg{i}").transform;
                s.SetParent(parent, false);
                s.localPosition = new Vector3(0, i == 0 ? BlankStart : SegmentLength, 0);
                var k = new MeshKit();
                float r0 = Mathf.Lerp(0.0105f, 0.0022f, i / 10f), r1 = Mathf.Lerp(0.0105f, 0.0022f, (i + 1) / 10f);
                k.Tube(Vector3.zero, new Vector3(0, SegmentLength, 0), r0, r1, 6, blank, blank, i == 9);
                k.Tube(new Vector3(0, SegmentLength - 0.01f, -r1), new Vector3(0, SegmentLength - 0.01f, -r1 - 0.015f), 0.0015f, 0.0015f, 4, metal, metal);
                var proc = new GameObject("Proc");
                proc.transform.SetParent(s, false);
                Part(proc.transform, "Blank", k.ToMesh("Blank"), mat);
                _procBlank.Add(proc);
                _segments.Add(s);
                parent = s;
            }
            var tip = new GameObject("Tip").transform;
            tip.SetParent(parent, false);
            tip.localPosition = new Vector3(0, SegmentLength, 0);
            _tip = tip;
            GripRight = new GameObject("GripRight").transform;
            GripRight.SetParent(_frame, false);
            GripRight.localPosition = new Vector3(0f, 0.03f, 0.03f);
            GripLeft = new GameObject("GripLeft").transform;
            GripLeft.SetParent(_frame, false);
            GripLeft.localPosition = new Vector3(0f, -0.2f, 0.02f);
        }

        /// <summary>
        /// Shows the rod's and reel's Meshy models (RodData.Model / ReelData.Model, default the series' look) on the
        /// built frame: the handle and each blank section on its joint (scaled to the rod's length), the reel under the
        /// seat with its rotor and crank turning while reeling. Without the models the built rod stays.
        /// </summary>
        private void ShowModels(RodData? rod, ReelData? reel)
        {
            string rodId = rod != null && rod.Model.Length > 0 ? rod.Model : "rods/float_ostoja";
            string reelId = reel != null && reel.Model.Length > 0 ? reel.Model : "reels/small_hokuto";
            float length = rod != null && rod.Length > 1f ? Mathf.Clamp(rod.Length, 1.6f, 4.6f) : 3.58f;
            float section = (length - BlankStart + ButtY) / 10f;
            for (int i = 0; i < _segments.Count; i++)
                if (i > 0)
                    _segments[i].localPosition = new Vector3(0f, section, 0f);
            _tip.localPosition = new Vector3(0f, section, 0f);
            float k = section / SegmentLength;
            if (rodId != _shownRod)
            {
                _shownRod = rodId;
                foreach (Transform c in _rodModel)
                    Destroy(c.gameObject);
                for (int i = 0; i < _segments.Count; i++)
                    foreach (Transform c in _segments[i])
                        if (c.name.StartsWith("Model"))
                            Destroy(c.gameObject);
                var model = ModelLibrary.Spawn(rodId, _rodModel);
                bool ok = model != null;
                if (model != null)
                {
                    for (int i = 0; i < _segments.Count; i++)
                        if (ModelLibrary.Find(model.transform, $"Seg{i}") is { } seg)
                        {
                            seg.SetParent(_segments[i], false);
                            seg.localPosition = Vector3.zero;
                            seg.localRotation = Quaternion.identity;
                            seg.name = "Model" + i;
                        }
                    foreach (var r in model.GetComponentsInChildren<Renderer>())
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                _procHandle.gameObject.SetActive(!ok || !ModelLibrary.Has(reelId));
                foreach (var p in _procBlank)
                    p.SetActive(!ok);
            }
            for (int i = 0; i < _segments.Count; i++)
                foreach (Transform c in _segments[i])
                    if (c.name.StartsWith("Model"))
                        c.localScale = new Vector3(1f, k, 1f);
            if (reelId != _shownReel)
            {
                _shownReel = reelId;
                GripLeft.SetParent(_frame, false);
                GripLeft.localPosition = new Vector3(0f, -0.2f, 0.02f);
                foreach (Transform c in _reelModel)
                    Destroy(c.gameObject);
                _rotor = _crank = null;
                var model = ModelLibrary.Spawn(reelId, _reelModel);
                if (model != null)
                {
                    foreach (var r in model.GetComponentsInChildren<Renderer>())
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var frames = ModelLibrary.Json(reelId) is { } json ? JsonUtility.FromJson<ReelFrame>(json) : null;
                    _rotor = Pivot(model.transform, "Rotor", frames?.rotor);
                    _crank = Pivot(model.transform, "Crank", frames?.hub);
                    if (_crank != null && frames != null)
                    {
                        // the knob: out from the hub along the crank's axis, the arm's length down
                        GripLeft.SetParent(_crank, false);
                        GripLeft.localPosition = new Vector3(-frames.knob_out, -frames.arm, 0f);
                    }
                }
                _procHandle.gameObject.SetActive(model == null || _rodModel.childCount == 0);
            }
        }

        [Serializable]
        private sealed class ReelFrame
        {
            public float[]? hub;
            public float[]? rotor;
            public float arm = 0.036f;
            public float knob_out = -0.049f;
        }

        /// <summary>A pivot at a reel part's axis point (glTF coordinates, x mirrored into Unity) carrying the part.</summary>
        private static Transform? Pivot(Transform model, string part, float[]? at)
        {
            if (ModelLibrary.Find(model, part) is not { } t)
                return null;
            var pivot = new GameObject(part + "Pivot").transform;
            pivot.SetParent(t.parent, false);
            pivot.localPosition = at is { Length: 3 } ? new Vector3(-at[0], at[1], at[2]) : t.localPosition;
            t.SetParent(pivot, true);
            return pivot;
        }

        private static void Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void BuildBobber()
        {
            var k = new MeshKit();
            var red = Col.Lin(0.9f, 0.12f, 0.08f);
            var white = Col.Lin(0.92f, 0.92f, 0.88f);
            k.Blob(new Vector3(0, 0.0f, 0), new Vector3(0.022f, 0.035f, 0.022f), 1, 0f, white, white * 0.8f, true, 2);
            k.Tube(new Vector3(0, 0.02f, 0), new Vector3(0, 0.1f, 0), 0.006f, 0.003f, 8, red, red, true);
            k.Tube(new Vector3(0, -0.03f, 0), new Vector3(0, -0.08f, 0), 0.003f, 0.0015f, 6, white, white, true);
            var go = new GameObject("Float");
            go.AddComponent<MeshFilter>().sharedMesh = k.ToMesh("Float");
            _bobberRenderer = go.AddComponent<MeshRenderer>();
            _bobberRenderer.sharedMaterial = Materials.Lit(0.4f, 0f);
            _bobber = go.transform;
            go.SetActive(false);
        }

        private void BuildLine()
        {
            var go = new GameObject("Line");
            _line = go.AddComponent<LineRenderer>();
            _line.sharedMaterial = Materials.Unlit(new Color(0.85f, 0.85f, 0.8f));
            _line.positionCount = _linePts.Length;
            _line.widthMultiplier = 0.004f;
            _line.numCapVertices = 0;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            go.SetActive(false);
        }

        /// <summary>The rod shows only in play (the menu's flying camera carries it otherwise).</summary>
        public void ShowRod(bool show)
        {
            if (_rodRoot != null)
                _rodRoot.gameObject.SetActive(show);
        }

        // ------------------------------------------------------------------ tackle
        public string? Missing()
        {
            if (Rod == null) return "Brak wędki: kup ją w sklepie (T).";
            if (Reel == null) return "Brak kołowrotka (T).";
            if (LineItem == null) return "Brak żyłki (T).";
            if (Hook == null) return "Brak haczyka (T).";
            if (Bait == null || (!Bait.Reusable && Profile.BaitCount(Profile.Bait) <= 0)) return "Brak przynęty (T).";
            return null;
        }

        public void ApplyTackle()
        {
            var rod = Rod;
            if (_frame != null)
                ShowModels(rod, Reel);
            var line = LineItem;
            var hook = Hook;
            EffectiveLimit = Mathf.Min(line?.Strength ?? 1f, Mathf.Min(rod?.MaxLoad ?? 1f, hook?.Strength ?? 1f));
            if (line != null)
            {
                var c = line.LineColor;
                _line.sharedMaterial.color = new Color(c.R, c.G, c.B);
                _line.widthMultiplier = Mathf.Clamp(line.Diameter * 0.012f, 0.0025f, 0.006f);
            }
            if (rod != null && bait() is { } b)
                Method = FishingSim.Core.Methods.Method.For(rod.Kind, b.Kind);
            BaitData? bait() => Bait;
        }

        private BiteContext Context(float depth) => new(
            Profile.Bait,
            Hook?.Size ?? 5f,
            depth,
            World.Lake.Location,
            Day.Period,
            Day.Weather,
            Method == FishingMethod.Bottom ? 1f : null,
            null,
            null,
            0f,
            null,
            null,
            Day.Light);

        private IReadOnlyList<FishSpecies> SpeciesHere => Tackle.AllSpecies;

        // ------------------------------------------------------------------ per frame
        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _stateTime += dt;
            bool canAct = !Blocked && Cursor.lockState == CursorLockMode.Locked;
            bool lmbDown = canAct && Input.GetMouseButtonDown(0), lmb = canAct && Input.GetMouseButton(0) || SmokeReel, lmbUp = canAct && Input.GetMouseButtonUp(0);
            bool rmbDown = canAct && Input.GetMouseButtonDown(1), rmb = canAct && Input.GetMouseButton(1);
            if (canAct)
                DragSetting = Mathf.Clamp01(DragSetting + Input.mouseScrollDelta.y * 0.05f);

            switch (State)
            {
                case RodState.Idle:
                    if (lmbDown)
                    {
                        if (Missing() is { } why)
                            Said?.Invoke(why);
                        else
                        {
                            ApplyTackle();
                            Set(RodState.Charging);
                        }
                    }
                    break;
                case RodState.Charging:
                    CastPower = Mathf.PingPong(_stateTime / 1.1f, 1f);
                    if (lmbUp || !lmb)
                        Cast();
                    break;
                case RodState.Flying:
                    TickFlight(dt);
                    break;
                case RodState.Waiting:
                case RodState.Nibble:
                case RodState.Bite:
                    TickWaiting(dt, lmb, rmbDown);
                    break;
                case RodState.Fight:
                    TickFight(dt, lmb, rmb);
                    break;
            }
            UpdateVisuals(dt);
        }

        private void Set(RodState s)
        {
            State = s;
            _stateTime = 0f;
        }

        private void Cast()
        {
            var rod = Rod!;
            float power = Mathf.Max(CastPower, 0.08f);
            float weight = Method == FishingMethod.Bottom ? 40f : Method == FishingMethod.Spinning ? Mathf.Max(Bait?.WeightG ?? 8f, 3f) : 4.5f;
            float reach = (14f + 30f * Mathf.Clamp01(weight / 25f)) * rod.CastMult;
            var dir = Flat(Player.Forward).normalized;
            float scatter = (float)(_rng.NextFloat() - 0.5f) * (1.5f + 4.5f * power) * Mathf.Deg2Rad;
            dir = Quaternion.Euler(0f, scatter * Mathf.Rad2Deg, 0f) * dir;
            float distance = Mathf.Lerp(5f, reach, power);
            _origin = Player.transform.position;
            _bobber.position = _tip.position;
            float flight = 0.6f + distance / 28f;
            var target = _origin + dir * distance;
            _velocity = (target - _bobber.position) / flight;
            _velocity.y = (0f - _bobber.position.y) / flight + 9.81f * flight * 0.5f;
            _bobber.gameObject.SetActive(true);
            _line.gameObject.SetActive(true);
            Set(RodState.Flying);
        }

        private void TickFlight(float dt)
        {
            _velocity += Vector3.down * 9.81f * dt;
            _bobber.position += _velocity * dt;
            var p = _bobber.position;
            float ground = World.Lake.HeightAt(p.x, p.z);
            float water = Waves.HeightAt(p.x, p.z, Time.time);
            if (p.y <= Mathf.Max(ground, water))
            {
                if (ground > -0.15f || Lake.OnPier(p.x, p.z))
                {
                    Said?.Invoke("Zestaw wylądował na brzegu.");
                    Reset();
                    return;
                }
                DepthHere = -ground;
                _lineSlack = 1f;
                Cue?.Invoke(Method == FishingMethod.Bottom ? Core.Audio.OneShot.Splash : Core.Audio.OneShot.SmallSplash, 0.8f);
                NewBiteTimer();
                Set(RodState.Waiting);
            }
        }

        // ------------------------------------------------------------------ automated test (Smoke)
        /// <summary>Held down by the smoke test: reels as if the left button were held.</summary>
        public bool SmokeReel { get; set; }

        /// <summary>Puts the end tackle at a point of the lake as if cast there.</summary>
        public void SmokeCast(Vector3 target)
        {
            ApplyTackle();
            _origin = Player.transform.position;
            DepthHere = -World.Lake.HeightAt(target.x, target.z);
            _bobber.position = new Vector3(target.x, 0f, target.z);
            _bobber.gameObject.SetActive(true);
            _line.gameObject.SetActive(true);
            _lineSlack = 1f;
            NewBiteTimer();
            Set(RodState.Waiting);
        }

        /// <summary>Makes the bite clock run out now.</summary>
        public void SmokeBiteNow() => _biteTimer = 0f;

        /// <summary>Strikes at the bite (always hooks).</summary>
        public void SmokeStrike()
        {
            if (State == RodState.Bite && Species != null)
                StartFight(1f);
        }

        private void NewBiteTimer()
        {
            var ctx = Context(DepthHere);
            float attraction = BiteModel.Attraction(SpeciesHere, ctx);
            float vis = LineItem?.Visibility ?? 1f;
            _biteTimer = BiteModel.WaitSeconds(_rng, 4f, 12f, vis, attraction) * (Method == FishingMethod.Spinning ? 0.6f : 1f)
                         / (1f + BiteBonus());
        }

        private void TickWaiting(float dt, bool lmb, bool rmbDown)
        {
            var p = _bobber.position;
            // retrieve: the float/feeder comes in, the lure works
            if (lmb && State == RodState.Waiting)
            {
                float speed = (Reel?.ReelSpeed ?? 3f) * (Method == FishingMethod.Spinning ? 0.35f : 0.22f);
                var toAngler = Flat(Player.transform.position - p);
                if (toAngler.magnitude < 2.2f || World.Lake.HeightAt(p.x, p.z) > -0.08f)
                {
                    Said?.Invoke("Zestaw wyjęty z wody.");
                    Reset();
                    return;
                }
                p += toAngler.normalized * speed * dt;
                DepthHere = -World.Lake.HeightAt(p.x, p.z);
                _lineSlack = Mathf.MoveTowards(_lineSlack, 0f, dt * 2f);
            }
            float surface = Waves.HeightAt(p.x, p.z, Time.time);
            float y = Method switch
            {
                FishingMethod.Float => surface - 0.01f - _dip * 0.09f,
                FishingMethod.Spinning => Mathf.Max(surface - 0.5f, -DepthHere + 0.1f),
                _ => -DepthHere + 0.05f,
            };
            _bobber.position = new Vector3(p.x, y, p.z);

            // the bite clock runs while the bait waits (a lure only while it is drawn)
            if (State == RodState.Waiting)
            {
                if (Method != FishingMethod.Spinning || lmb)
                    _biteTimer -= dt;
                if (rmbDown)
                    Said?.Invoke("Nic nie bierze.");
                if (_biteTimer <= 0f)
                {
                    Species = BiteModel.Pick(SpeciesHere, Context(DepthHere), _rng);
                    if (Species == null)
                    {
                        NewBiteTimer();
                        return;
                    }
                    Weight = BiteModel.RollWeight(Species, Hook?.Size ?? 5f, _rng);
                    _nibbleLeft = Method == FishingMethod.Spinning ? 0f : 0.8f + _rng.NextFloat() * 2.5f;
                    Set(RodState.Nibble);
                }
                _dip = Mathf.MoveTowards(_dip, 0f, dt * 3f);
                return;
            }
            if (State == RodState.Nibble)
            {
                // little plucks at the float / the quiver tip
                _dip = Mathf.Max(0f, Mathf.Sin(_stateTime * 9f) * 0.35f * (_rng.NextFloat() < 0.5f ? 1f : 0.4f));
                if (rmbDown)
                {
                    Said?.Invoke("Za wcześnie! Ryba uciekła.");
                    Missed(keepBait: 0.6f);
                    return;
                }
                _nibbleLeft -= dt;
                if (_nibbleLeft <= 0f)
                    Set(RodState.Bite);
                return;
            }
            // Bite: the float goes under (the tip pulls round) for the hook window
            _dip = Mathf.MoveTowards(_dip, 1f, dt * 5f);
            float window = FishingSim.Core.Methods.Method.HookWindow(Method) + 0.4f;
            if (rmbDown)
            {
                float lateness = Mathf.Clamp01(_stateTime / window);
                float chance = 0.9f * (1f - 0.4f * lateness) * Mathf.Lerp(1f, 0.75f, Mathf.Clamp01((Distance - 15f) / 50f) * (LineItem?.Stretch ?? 0.5f));
                if (_rng.NextFloat() < chance)
                    StartFight(1f - 0.4f * lateness);
                else
                {
                    Said?.Invoke("Niedocięta! Ryba się spięła.");
                    Missed(keepBait: 0.35f);
                }
                return;
            }
            if (_stateTime > window)
            {
                Said?.Invoke(Method == FishingMethod.Float ? "Spławik wypłynął: ryba zerwała przynętę." : "Ryba odpuściła.");
                Missed(keepBait: 0.35f);
            }
        }

        private void Missed(float keepBait)
        {
            if (_rng.NextFloat() > keepBait)
                Profile.UseBait();
            Species = null;
            _dip = 0f;
            if (Missing() != null)
            {
                Reset();
                return;
            }
            NewBiteTimer();
            Set(RodState.Waiting);
        }

        private void StartFight(float hold)
        {
            var reel = Reel!;
            var line = LineItem!;
            var rod = Rod!;
            float shock = Mathf.Min(rod.ShockAbsorb + 0.12f * line.Stretch, 0.5f);
            var gear = new FightGear(reel.DragMax, EffectiveLimit, shock, reel.ReelSpeed, reel.LineCapacity);
            var to = Flat(_bobber.position - Player.transform.position);
            _origin = Player.transform.position;
            _hookHold = hold;
            Fight = new FightModel(FishProfile.From(Species!, Weight), gear, _rng, Mathf.Max(to.magnitude, 2.5f), Mathf.Atan2(to.x, to.z), DragSetting)
            {
                HookHold = hold,
            };
            ShowFishModel(Species!);
            float length = Species!.LengthRef * Mathf.Pow(Mathf.Max(Weight, 0.005f), 0.33f);
            _fish.localScale = Vector3.one * Mathf.Clamp(length, 0.08f, 2.2f);
            _fish.gameObject.SetActive(true);
            Cue?.Invoke(Core.Audio.OneShot.Strike, 1f);
            Set(RodState.Fight);
        }

        private void TickFight(float dt, bool reeling, bool lifting)
        {
            var fight = Fight!;
            var outcome = fight.Step(dt, DragSetting, reeling, lifting);
            var pos = _origin + new Vector3(fight.Position.X, 0f, fight.Position.Y);
            float surface = Waves.HeightAt(pos.x, pos.z, Time.time);
            float depthAt = -World.Lake.HeightAt(pos.x, pos.z);
            float swim = Mathf.Lerp(-0.6f, -0.12f, Mathf.Clamp01(1f - fight.Distance / 25f));
            swim = Mathf.Max(swim, -Mathf.Max(depthAt - 0.15f, 0.05f));
            _fish.position = new Vector3(pos.x, surface + swim, pos.z);
            _fish.rotation = Quaternion.Euler(0f, fight.Heading * Mathf.Rad2Deg, Mathf.Sin(Time.time * 9f) * 8f);
            _bobber.position = Vector3.Lerp(_bobber.position, new Vector3(pos.x, surface, pos.z) + (_origin - pos).normalized * 0.4f, 1f - Mathf.Exp(-dt * 10f));
            _lineSlack = fight.LineSlack ? 0.6f : 0f;
            switch (outcome)
            {
                case FightOutcome.Landed:
                    Cue?.Invoke(Core.Audio.OneShot.Catch, 1f);
                    Profile.UseBait();
                    Set(RodState.Landed);
                    _bobber.gameObject.SetActive(false);
                    _line.gameObject.SetActive(false);
                    Landed?.Invoke();
                    break;
                case FightOutcome.LineBroke:
                    BreakTackle();
                    break;
                case FightOutcome.SpooledOut:
                    Said?.Invoke("Ryba wybrała całą żyłkę ze szpuli i ją zerwała. Żyłka i haczyk stracone.");
                    Profile.Destroy(Profile.Line);
                    Profile.Destroy(Profile.Hook);
                    LoseFish();
                    break;
                case FightOutcome.HookThrown:
                    Said?.Invoke("Ryba wypięła się z haczyka.");
                    LoseFish();
                    break;
            }
        }

        private GameObject? _fishModel;

        /// <summary>The species' Meshy model when there is one (assets/fish/meshy), else the built fish.</summary>
        private void ShowFishModel(FishSpecies sp)
        {
            if (_fishModel != null)
                Destroy(_fishModel);
            string id = System.IO.Path.GetFileNameWithoutExtension(sp.ResourcePath);
            _fishModel = ModelLibrary.Fish(id);
            if (_fishModel != null)
                _fishModel.transform.SetParent(_fish, false);
            else
                _fishMesh.sharedMesh = FishMesh.For(sp);
            _fishMesh.gameObject.SetActive(_fishModel == null);
        }

        /// <summary>The weakest link breaks: a rod snaps, a line parts (hook lost with it) or a hook opens.</summary>
        private void BreakTackle()
        {
            Cue?.Invoke(Core.Audio.OneShot.LineSnap, 1f);
            float line = LineItem?.Strength ?? 0f, rod = Rod?.MaxLoad ?? 0f, hook = Hook?.Strength ?? 0f;
            if (rod <= line && rod <= hook)
            {
                Said?.Invoke($"Wędka pękła! ({Rod?.DisplayName})");
                Profile.Destroy(Profile.Rod);
            }
            else if (hook < line)
            {
                Said?.Invoke("Haczyk się rozgiął. Haczyk stracony.");
                Profile.Destroy(Profile.Hook);
            }
            else
            {
                Said?.Invoke("Żyłka pękła! Haczyk z przynętą został w rybie.");
                Profile.Destroy(Profile.Hook);
            }
            Profile.UseBait();
            LoseFish();
        }

        private void LoseFish()
        {
            Fight = null;
            Species = null;
            _fish.gameObject.SetActive(false);
            Reset();
            Profile.Save();
        }

        /// <summary>The landed fish: kept in the keepnet or released (from the catch panel).</summary>
        public void Decide(bool keep)
        {
            if (State != RodState.Landed || Species == null)
                return;
            Profile.AddCatch(Species, Weight);
            if (keep)
                Profile.Keepnet.Add(new KeptFish { Species = Species.SpeciesName, Name = Species.DisplayNameOr(), Weight = Weight });
            else
                Profile.Xp += Mathf.RoundToInt(Progression.FishXp(Species, Weight) * 0.2f);   // a small bonus for letting it go
            Fight = null;
            Species = null;
            _fish.gameObject.SetActive(false);
            Reset();
            Profile.Save();
        }

        public void Reset()
        {
            if (_bobber == null)
                return;   // not built yet (Start runs after the game's first map load begins)
            _bobber.gameObject.SetActive(false);
            _line.gameObject.SetActive(false);
            _dip = 0f;
            _lineSlack = 1f;
            CastPower = 0f;
            if (State != RodState.Landed || Species == null)
                Set(RodState.Idle);
            else
                Set(RodState.Idle);
        }

        // ------------------------------------------------------------------ rod, line
        private void UpdateVisuals(float dt)
        {
            float load = 0f;
            if (State == RodState.Fight && Fight != null)
                load = Mathf.Clamp01(Fight.Tension / Mathf.Max(EffectiveLimit, 0.5f));
            else if (State == RodState.Bite && Method != FishingMethod.Float)
                load = 0.12f;
            else if (State == RodState.Nibble && Method != FishingMethod.Float)
                load = _dip * 0.05f;
            _bend = Mathf.Lerp(_bend, load, 1f - Mathf.Exp(-dt * 12f));
            for (int i = 0; i < _segments.Count; i++)
            {
                float w = Mathf.Pow((i + 1) / (float)_segments.Count, 1.6f);
                _segments[i].localRotation = Quaternion.Euler(-_bend * 16f * w, 0f, 0f);
            }
            // the rod lifts while charging and in the fight when lifting
            float lift = State == RodState.Charging ? -25f * CastPower : State == RodState.Fight && Input.GetMouseButton(1) ? -18f : 0f;
            _rodRoot.localRotation = Quaternion.Slerp(_rodRoot.localRotation, Quaternion.Euler(-32f + lift, -6f, 0f), 1f - Mathf.Exp(-dt * 10f));
            bool reeling = (State == RodState.Fight || State == RodState.Waiting) && (Input.GetMouseButton(0) || SmokeReel) && !Blocked;
            Reeling = reeling;
            if (reeling)
            {
                _reelAngle += dt * 600f;
                if (_rotor != null)
                    _rotor.localRotation = Quaternion.Euler(0f, _reelAngle * 1.4f, 0f);
                if (_crank != null)
                    _crank.localRotation = Quaternion.Euler(_reelAngle, 0f, 0f);
            }

            if (State == RodState.Landed && Species != null)
            {
                // the catch held up before the camera, turning slowly (the catch panel sits below it)
                var cam = Player.Camera.transform;
                float length = Species.LengthRef * Mathf.Pow(Mathf.Max(Weight, 0.005f), 0.33f);
                float shown = Mathf.Clamp(length, 0.12f, 0.5f);
                _fish.gameObject.SetActive(true);
                _fish.position = cam.position + cam.forward * 0.85f + cam.up * 0.13f;
                _fish.rotation = Quaternion.LookRotation(cam.right, cam.up) * Quaternion.Euler(0f, Mathf.Sin(_stateTime * 0.6f) * 35f, 0f);
                _fish.localScale = Vector3.one * shown;
            }
            if (!_line.gameObject.activeSelf)
                return;
            var a = _tip.position;
            var b = _bobber.position + Vector3.up * (Method == FishingMethod.Float ? 0.09f : 0f);
            float sag = Vector3.Distance(a, b) * 0.06f * _lineSlack + (State == RodState.Fight ? 0.01f : 0.02f);
            for (int i = 0; i < _linePts.Length; i++)
            {
                float t = i / (float)(_linePts.Length - 1);
                var p = Vector3.Lerp(a, b, t);
                p.y -= sag * 4f * t * (1f - t);
                float water = Waves.HeightAt(p.x, p.z, Time.time);
                if (p.y < water && State != RodState.Fight)
                    p.y = water;
                _linePts[i] = p;
            }
            _line.SetPositions(_linePts);
            _bobberRenderer.enabled = Method == FishingMethod.Float || State == RodState.Fight && Method == FishingMethod.Float;
        }
    }

    public static class SpeciesNames
    {
        /// <summary>The name shown to the angler (the species' Polish name).</summary>
        public static string DisplayNameOr(this FishSpecies sp) => sp.SpeciesName;
    }
}
