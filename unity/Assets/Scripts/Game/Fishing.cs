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

        /// <summary>What the line (with the hook) holds and what the rod holds (kg): the HUD's two load bars.</summary>
        public float LineLimit { get; private set; } = 3f;
        public float RodLimit { get; private set; } = 3f;
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
        private LineRibbon _line = null!;
        private Vector3 _velocity, _origin;
        private float _stateTime, _biteTimer, _nibbleLeft, _dip, _lineSlack = 1f;
        private bool _readyToStrike;
        private float _hookHold = 0.8f;

        // ---- the blank, the reel and the line (Godot: RodRig + FishingRod.UpdateBend/UpdateLine)
        private Core.Physics.RodBendModel? _bendModel;
        private RodData? _bendFor;
        private readonly Core.Physics.LineRope _rope = new();
        private bool _ropeLive;
        private float _lineLength, _bailOpen, _drawTight, _bitePull, _crankAngle, _spoolFill = 1f, _section = SegmentLength;
        private readonly List<Transform> _guides = new();
        private readonly Vector3[] _ropePts = new Vector3[Core.Physics.LineRope.Points];
        private readonly Vector3[] _inRod = new Vector3[16];
        private LineRibbon _inRodLine = null!;
        private Transform? _bail, _spoolLine, _lineExit;
        private Transform _builtReel = null!, _builtRotor = null!, _builtCrank = null!, _builtBail = null!, _builtSpoolLine = null!, _builtLineExit = null!;
        private Vector3 _bailAxis = Vector3.right;
        private bool _modelledReel;

        /// <summary>The line paid out from the tip to the end tackle (m): grows with the cast, shrinks as it is reeled.</summary>
        public float LineOut => _lineLength;

        /// <summary>Slack lying on the water while waiting (m).</summary>
        public float Slack => State is RodState.Waiting or RodState.Nibble or RodState.Bite ? Mathf.Max(0f, _lineLength - TipDistance()) : 0f;

        /// <summary>The wound line's radius on an empty spool, as a share of the full one (RodRig.SpoolCore).</summary>
        private const float SpoolCore = 0.62f;

        /// <summary>A float of a few grams with its split shot; a filled feeder.</summary>
        private const float FloatRigGrams = 4.5f, FeederGrams = 40f;

        // ---- the conditions behind every bite (Core/Methods, as FishingRod uses them)
        /// <summary>Frightened fish: splashes and the angler's steps; every bite clock slows with it.</summary>
        public Disturbance Disturbance { get; } = new();
        /// <summary>The air pressure and its trend (falling pressure: feeding fish).</summary>
        public Barometer Barometer { get; } = new();
        private float _baitFresh = 1f, _seasonPace = 1f, _windShore, _shoreCheck;
        private bool _baitOn = true;
        private Vector3 _lastEye;
        private bool _eyeSeen;
        private float _jump, _weedTime, _tilt, _run, _swimLoad;

        /// <summary>Baited spots on the bottom (the feeder's groundbait, loose feed): bites come faster there.</summary>
        public GroundbaitSpots Groundbait { get; } = new();
        /// <summary>The fish taken from each place: a swim fished hard goes quiet for a few hours.</summary>
        public SwimPressure Swim { get; } = new();
        /// <summary>Where a shoaling fish was caught, its shoal is still about for a while.</summary>
        public Shoals Shoal { get; } = new();
        private Vector3 _hookedAt;
        private readonly Queue<(float X, float Z, float At)> _rises = new();

        /// <summary>A new map: the baited spots, fished swims, shoals, rises and frightened fish belong to the old one.</summary>
        public void ClearPlace()
        {
            Groundbait.Clear();
            Swim.Clear();
            Shoal.Clear();
            Disturbance.Clear();
            _rises.Clear();
        }

        /// <summary>A fish rose at (x, z) (Swimmers.Risen): a bait cast to the ring soon after draws the risers.</summary>
        public void NoteRise(string species, float x, float z)
        {
            _rises.Enqueue((x, z, Time.time));
            while (_rises.Count > 16)
                _rises.Dequeue();
        }

        /// <summary>Shoals.Boost of recent catches, times the draw of a fresh rise ring near the bait (Rises.Boost).</summary>
        private float ShoalAt(string species, float x, float z)
        {
            float boost = Shoal.Any ? Shoal.Boost(species, x, z, Profile.Clock) : 1f;
            float rise = 1f;
            foreach (var (rx, rz, at) in _rises)
                rise = Mathf.Max(rise, Rises.Boost(new Vector2(rx - x, rz - z).magnitude, Time.time - at));
            return boost * (Rises.Riser(FeedingHabits.LayerOf(species)) ? rise : 1f);
        }

        /// <summary>The place has been fished hard (bites slow down).</summary>
        public bool FishedOut => SwimPressure.FishedOut(_swimLoad);

        /// <summary>The groundbait's draw at the bait now.</summary>
        private float GroundbaitPace => Method == FishingMethod.Spinning ? 1f : GroundbaitSpots.PaceFor(Groundbait.HeatAt(_bobber.position.x, _bobber.position.z));

        /// <summary>How this species takes a bait under a float (FeedingHabits: dive, pluck, lift, run).</summary>
        private BiteStyle Style => Species != null ? FeedingHabits.StyleOf(Species.SpeciesName) : BiteStyle.Dive;

        /// <summary>The lure in the water (spinning): its depth by the retrieve, falls, twitches and snags (LureMotion).</summary>
        public LureMotion? Lure { get; private set; }

        /// <summary>The lure is stuck on the bottom: RMB jerks it, a held LMB pulls (the line may give).</summary>
        public bool Snagged => Lure?.Snagged == true;

        /// <summary>How the lure's colours suit the light and the water now (1 = neutral).</summary>
        public float LureAppeal => Method == FishingMethod.Spinning && Bait != null
            ? FishingSim.Core.Methods.Method.ColourAppeal(Bait.LureColor, Bait.AccentColor, Bait.Metallic, Day.Light, Clarity)
            : 1f;

        /// <summary>The hooked fish is in a weed bed or among branches: lift the rod (RMB) to drag it out.</summary>
        public bool FishInWeeds => State == RodState.Fight && _weedTime > 0.2f;

        /// <summary>The float's depth setting (m): where the bait hangs under a float.</summary>
        public float FloatDepth { get; set; } = 1.2f;

        /// <summary>The water's temperature now at the surface (degrees Celsius).</summary>
        public float WaterTemp => WaterTemperature.At(World.Lake.Location.WaterTemperature, Day.Hour, Day.Weather);

        /// <summary>Where in the water column the bait is (1 bottom .. 0 surface); null = not judged.</summary>
        private float? BaitLayerAt(float depth) => Method switch
        {
            FishingMethod.Bottom => 1f,
            FishingMethod.Float => depth > 0.05f ? Mathf.Clamp01(FloatDepth / depth) : 1f,
            _ => depth > 0.05f ? Mathf.Clamp01(Mathf.Min(1.2f, depth) / depth) : 1f,
        };

        /// <summary>The temperature at the bait (the surface's, cooling below the mixed top layer).</summary>
        public float BaitTemperature => WaterTemperature.AtDepth(WaterTemp, DepthHere * (BaitLayerAt(DepthHere) ?? 1f));

        /// <summary>Plants or sunken wood at a point (1 inside .. 0 four metres out) and how steeply the bottom drops there.</summary>
        public (float Cover, float Edge) StructureAt(float x, float z)
        {
            const float d = 1.5f;
            var lake = World.Lake;
            float gx = (lake.DepthAt(x + d, z) - lake.DepthAt(x - d, z)) / (2f * d);
            float gz = (lake.DepthAt(x, z + d) - lake.DepthAt(x, z - d)) / (2f * d);
            float cover = 0f;
            foreach (var (bx, bz, r) in Forest.WeedBeds)
                cover = Mathf.Max(cover, Structure.CoverAt(new[] { new WeedBed(bx, bz, r) }, x, z));
            foreach (var (wx, wz, r) in Forest.SunkenWood)
                cover = Mathf.Max(cover, Structure.WoodAt(new[] { new SunkenWood(wx, wz, r) }, x, z));
            return (cover, Structure.EdgeFrom(Mathf.Sqrt(gx * gx + gz * gz)));
        }

        /// <summary>What slows or quickens every bite clock: frightened fish, the pressure's trend, feeding spells, a
        /// wind blowing onto this shore.</summary>
        private float ConditionPace => Disturbance.Pace * Barometer.Pace * FeedingSpells.Pace(Profile.Clock, Profile.Location)
            * WindShore.Pace(_windShore, Day.Wind) * SwimPressure.Pace(_swimLoad);

        /// <summary>How well the bait still works (fresh .. washed out), and how its kind suits the water's temperature.</summary>
        private float BaitPace => Method == FishingMethod.Spinning ? 1f : _baitOn ? BaitWear.Appeal(_baitFresh) * _seasonPace : 0f;

        /// <summary>Rain and wind hide the angler's noise.</summary>
        private float NoiseMasking => Disturbance.Masking(Day.Weather, Day.Wind);

        /// <summary>The water's clarity now (the map's).</summary>
        private float Clarity => World.Lake.Location.WaterClarity;

        /// <summary>Particle baits that can be thrown in loose (FishingRod.LooseFeed).</summary>
        private static bool LooseFeedable(string id) => id is "maggot" or "corn" or "kasza" or "konopie" or "groch" or "bread" or "ochotka";

        /// <summary>
        /// FishingRod.TickDisturbance: the frightened fish calm down; the angler's steps near the bait frighten them
        /// (more running, wading); once a second the wind's push onto this shore and the bait's season are read.
        /// </summary>
        private void TickConditions(float dt)
        {
            Disturbance.Decay(dt);
            Barometer.Step(Day.LastStepHours, Day.Weather);
            Groundbait.Decay(Day.LastStepHours);
            // loose feed (V; G digs): a handful of particle bait round the float
            if (Input.GetKeyDown(KeyCode.V) && !Blocked && Method == FishingMethod.Float
                && State is RodState.Waiting or RodState.Nibble or RodState.Bite && LooseFeedable(Profile.Bait))
            {
                if (Profile.BaitCount(Profile.Bait) >= 3)
                {
                    for (int i = 0; i < 3; i++)
                        Profile.UseBait();
                    var f = _bobber.position;
                    float spread = Mathf.Min(1f + 0.06f * Distance, 3f);
                    Groundbait.Feed(f.x + (_rng.NextFloat() - 0.5f) * spread, f.z + (_rng.NextFloat() - 0.5f) * spread, 0.7f);
                    Disturbance.Add(0.04f * NoiseMasking);
                    Cue?.Invoke(Core.Audio.OneShot.SmallSplash, 0.4f);
                    Said?.Invoke("Garść zanęty przy spławiku.");
                }
                else
                    Said?.Invoke("Za mało przynęty na zanętę (potrzeba 3).");
            }
            var eye = Player.transform.position;
            float speed = _eyeSeen && dt > 0f ? new Vector2(eye.x - _lastEye.x, eye.z - _lastEye.z).magnitude / dt : 0f;
            _lastEye = eye;
            _eyeSeen = true;
            if (State is RodState.Waiting or RodState.Nibble or RodState.Bite && speed < 12f)
            {
                var bait = _bobber.position;
                float dist = new Vector2(eye.x - bait.x, eye.z - bait.z).magnitude;
                Disturbance.Add(Disturbance.Steps(speed, dist, World.Lake.DepthAt(eye.x, eye.z) > 0.05f, dt) * NoiseMasking);
                // the bait washes out in the water (a dissolving one falls off the hook when spent)
                if (Method != FishingMethod.Spinning && _baitOn)
                {
                    _baitFresh = BaitWear.Step(_baitFresh, Profile.Bait, dt);
                    if (_baitFresh <= 0f && BaitWear.Dissolves(Profile.Bait))
                    {
                        _baitOn = false;
                        Said?.Invoke("Przynęta rozmokła i spadła z haczyka.");
                    }
                }
                _shoreCheck -= dt;
                if (_shoreCheck <= 0f)
                {
                    _shoreCheck = 1f;
                    var wind = new Vector3(0.8f, 0f, 0.6f) * (Day.Wind * 3f);
                    _windShore = WindShore.Exposure(World.Lake.DepthAt, bait.x, bait.z, wind.x, wind.z);
                    _seasonPace = Bait != null ? BaitSeason.Pace((int)Bait.Kind, BaitTemperature) : 1f;
                    _swimLoad = Swim.Load(bait.x, bait.z, Profile.Clock);
                }
            }
            else
            {
                _windShore = 0f;
                _shoreCheck = 0f;
            }
        }

        /// <summary>The tackle in use, for the HUD.</summary>
        public RodData? RodItem => Rod;
        public ReelData? ReelItem => Reel;
        public LineData? LineInUse => LineItem;
        public HookData? HookItem => Hook;
        public BaitData? BaitItem => Bait;

        /// <summary>What the drag holds before it gives line (kg).</summary>
        public float DragKg => DragSetting * (Reel?.DragMax ?? 3f);

        /// <summary>What pulls the line now (kg): the fight's tension, else what the tip carries (BendLoad).</summary>
        public float LoadKg => State == RodState.Fight && Fight != null ? Fight.Tension : BendLoad();

        /// <summary>Line still on the spool (m).</summary>
        public float SpoolLeft => Mathf.Max(0f, (Reel?.LineCapacity ?? 100f) - (State is RodState.Idle or RodState.Charging or RodState.Landed ? 0f : (Fight?.LineOut ?? _lineLength)));

        /// <summary>Where a cast at the power held now would land, without wind (m).</summary>
        public float AimDistance => Mathf.Lerp(5f, CastReach(), Mathf.Max(CastPower, 0.08f));

        /// <summary>A full-power cast's reach with this rod and end tackle (m).</summary>
        private float CastReach()
        {
            float weight = Method == FishingMethod.Bottom ? 40f : Method == FishingMethod.Spinning ? Mathf.Max(Bait?.WeightG ?? 8f, 3f) : 4.5f;
            return (14f + 30f * Mathf.Clamp01(weight / 25f)) * (Rod?.CastMult ?? 1f);
        }

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
        internal const float ButtY = -0.33f, BlankStart = 0.25f, SegmentLength = 0.3f;
        private Transform _frame = null!, _procHandle = null!, _rodModel = null!, _reelModel = null!;
        private readonly List<GameObject> _procBlank = new();
        private Transform? _rotor, _crank;

        /// <summary>Where the angler's hands go (AnglerArms): the handle above the seat, the crank's knob.</summary>
        public Transform GripRight { get; private set; } = null!;
        public Transform GripLeft { get; private set; } = null!;
        public bool RodVisible => _rodRoot != null && _rodRoot.gameObject.activeInHierarchy;

        /// <summary>The rod's own frame (RodRig's: the rod's and reel's models hang here) and the reel's crank pivot.</summary>
        public Transform? Frame => _frame;
        public Transform? CrankPivot => _crank;

        /// <summary>The straight blank's tip in the rod's frame (butt to tip along +Y).</summary>
        public Vector3 TipRestLocal => new(0f, BlankStart + _section * 10f, 0f);

        private bool _framed;
        private Quaternion _frameTurn = Quaternion.identity;

        /// <summary>The rod held where posed arms put it (GripArms: the camera at the posed figure's eyes) instead of
        /// RigBasePosition/RigRest.</summary>
        public void SetFraming(Vector3 position, Quaternion turn)
        {
            _framed = true;
            _frameTurn = turn;
            if (_rodRoot != null)
            {
                _rodRoot.localPosition = position;
                _rodRoot.localRotation = turn;
            }
        }

        private Quaternion RestTurn(float lift) => _framed ? Quaternion.Euler(lift, 0f, 0f) * _frameTurn : RigRest(lift);
        private string _shownRod = "", _shownReel = "", _shownRodItem = "", _shownReelItem = "";

        /// <summary>The rod's and the reel's model ids now shown ("" while the built ones show).</summary>
        public string ShownRodId => _shownRod;
        public string ShownReelId => _shownReel;

        /// <summary>
        /// The framing of the user's reference (a Russian Fishing 4 clip): the rod comes in from the lower right, the
        /// handle under the right forearm, and rises steeply towards the middle; the reel hangs under it, a little to the
        /// left, its crank reached by the left hand from the lower left. The rig 11 cm right of the eyes, 20 cm below and
        /// 30 cm ahead, the blank 38 degrees up and turned 12 degrees in, rolled 12 degrees.
        /// </summary>
        private static readonly Vector3 RigBasePosition = new(0.11f, -0.2f, 0.3f);
        private const float RigPitch = -38f, RigYaw = -12f, RigRoll = -12f;

        /// <summary>The rig's rest turn, pitched further up by <paramref name="lift"/> degrees (negative = up).</summary>
        private static Quaternion RigRest(float lift) => Quaternion.Euler(RigPitch + lift, RigYaw, 0f) * Quaternion.AngleAxis(RigRoll, Vector3.forward);

        private void BuildRod()
        {
            var root = new GameObject("Rod").transform;
            root.SetParent(Player.Camera.transform, false);
            root.localPosition = RigBasePosition;
            root.localRotation = RigRest(0f);
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
            _procHandle = new GameObject("ProcHandle").transform;
            _procHandle.SetParent(_frame, false);
            Part(_procHandle, "Handle", handle.ToMesh("Handle"), mat);
            BuildReel(mat, dark, metal);
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
                // the guide's ring: the line runs through its middle (RodRig's LineGuide<i>), bending with the section
                float ring = Mathf.Lerp(0.009f, 0.0025f, i / 9f);
                k.Tube(new Vector3(-ring, SegmentLength - 0.01f, -r1 - 0.015f - ring), new Vector3(ring, SegmentLength - 0.01f, -r1 - 0.015f - ring), 0.0009f, 0.0009f, 4, metal, metal);
                var guide = new GameObject($"LineGuide{i}").transform;
                guide.SetParent(s, false);
                guide.localPosition = new Vector3(0f, SegmentLength - 0.01f, -r1 - 0.015f - ring);
                _guides.Add(guide);
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
        /// The built fixed-spool reel (without a model): foot and stem under the seat, the body, the spool with its wound
        /// line (shrinking as line goes out), the rotor carrying the wire bail (open for the cast) and the crank on the left.
        /// </summary>
        private void BuildReel(Material mat, Color dark, Color metal)
        {
            _builtReel = new GameObject("BuiltReel").transform;
            _builtReel.SetParent(_frame, false);
            var body = new MeshKit();
            body.Tube(new Vector3(0, 0.1f, -0.016f), new Vector3(0, 0.1f, -0.065f), 0.005f, 0.005f, 6, dark, dark);
            body.Box(new Vector3(0, 0.1f, -0.014f), new Vector3(0.012f, 0.05f, 0.004f), Quaternion.identity, dark);
            body.Blob(new Vector3(0, 0.096f, -0.088f), new Vector3(0.028f, 0.034f, 0.03f), 1, 0f, dark, dark * 0.7f, true, 1);
            body.Tube(new Vector3(0, 0.155f, -0.088f), new Vector3(0, 0.168f, -0.088f), 0.026f, 0.026f, 14, metal, metal, true);   // spool lip
            body.Tube(new Vector3(0, 0.168f, -0.088f), new Vector3(0, 0.18f, -0.088f), 0.012f, 0.009f, 10, dark, dark, true);     // drag knob
            Part(_builtReel, "Body", body.ToMesh("ReelBody"), mat);
            // the wound line: a cylinder about the spool's axis, scaled by the spool's fill
            _builtSpoolLine = new GameObject("SpoolLine").transform;
            _builtSpoolLine.SetParent(_builtReel, false);
            _builtSpoolLine.localPosition = new Vector3(0f, 0f, -0.088f);
            var line = new MeshKit();
            var lineCol = Col.Lin(0.85f, 0.85f, 0.8f);
            line.Tube(new Vector3(0, 0.128f, 0f), new Vector3(0, 0.155f, 0f), 0.023f, 0.023f, 16, lineCol, lineCol, false);
            Part(_builtSpoolLine, "Line", line.ToMesh("SpoolLine"), mat);
            // the rotor turns about the spool's axis; the bail (a half ring on two pivots) on it
            _builtRotor = new GameObject("Rotor").transform;
            _builtRotor.SetParent(_builtReel, false);
            _builtRotor.localPosition = new Vector3(0f, 0.12f, -0.088f);
            var rotor = new MeshKit();
            rotor.Tube(new Vector3(0, 0f, 0), new Vector3(0, 0.012f, 0), 0.027f, 0.029f, 14, dark, dark, true);
            rotor.Box(new Vector3(0.028f, 0.02f, 0), new Vector3(0.006f, 0.03f, 0.01f), Quaternion.identity, dark);
            rotor.Box(new Vector3(-0.028f, 0.02f, 0), new Vector3(0.006f, 0.03f, 0.01f), Quaternion.identity, dark);
            Part(_builtRotor, "Cup", rotor.ToMesh("Rotor"), mat);
            _builtBail = new GameObject("Bail").transform;
            _builtBail.SetParent(_builtRotor, false);
            _builtBail.localPosition = new Vector3(0f, 0.034f, 0f);
            var bail = new MeshKit();
            for (int i = 0; i < 12; i++)
            {
                float a0 = i / 12f * Mathf.PI, a1 = (i + 1) / 12f * Mathf.PI;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.03f, 0.012f, Mathf.Sin(a0) * 0.03f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.03f, 0.012f, Mathf.Sin(a1) * 0.03f);
                bail.Tube(p0, p1, 0.0012f, 0.0012f, 4, metal, metal);
            }
            Part(_builtBail, "Wire", bail.ToMesh("Bail"), mat);
            // the crank on the left: its arm and knob turn about the hub
            _builtCrank = new GameObject("Crank").transform;
            _builtCrank.SetParent(_builtReel, false);
            _builtCrank.localPosition = new Vector3(0.03f, 0.09f, -0.088f);
            var crank = new MeshKit();
            crank.Tube(Vector3.zero, new Vector3(0.025f, 0f, 0f), 0.004f, 0.004f, 6, metal, metal, true);
            crank.Tube(new Vector3(0.025f, 0f, 0f), new Vector3(0.025f, -0.04f, 0f), 0.003f, 0.003f, 6, metal, metal, true);
            crank.Tube(new Vector3(0.025f, -0.04f, 0f), new Vector3(0.05f, -0.04f, 0f), 0.006f, 0.005f, 8, dark, dark, true);
            Part(_builtCrank, "Arm", crank.ToMesh("Crank"), mat);
            _builtLineExit = new GameObject("LineExit").transform;
            _builtLineExit.SetParent(_builtReel, false);
            _builtLineExit.localPosition = new Vector3(0f, 0.157f, -0.088f + 0.024f);
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
            _section = section;
            for (int i = 0; i < _guides.Count; i++)
            {
                var g = _guides[i].localPosition;
                _guides[i].localPosition = new Vector3(g.x, section - 0.01f, g.z);
            }
            float k = section / SegmentLength;
            // a new item on the same model has its own finish: the model is made again
            if (rodId != _shownRod || (rod?.ItemId ?? "") != _shownRodItem)
            {
                _shownRod = rodId;
                _shownRodItem = rod?.ItemId ?? "";
                foreach (Transform c in _rodModel)
                    Destroy(c.gameObject);
                for (int i = 0; i < _segments.Count; i++)
                    foreach (Transform c in _segments[i])
                        if (c.name.StartsWith("Model"))
                            Destroy(c.gameObject);
                var model = ModelLibrary.Spawn(rodId, _rodModel);
                bool ok = model != null;
                // only the tackle in use is loaded: a rod bought or taken from the shop loads now and shows when ready
                if (model == null && !ModelLibrary.Settled(rodId))
                {
                    ModelLibrary.Start(rodId);
                    _waitRod = rodId;
                }
                if (model != null)
                {
                    // the series' finish (generate_tackle.py: series on one model look apart)
                    if (rod != null)
                        Finish(model, new Color(rod.ModelTint.R, rod.ModelTint.G, rod.ModelTint.B));
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
                _procHandle.gameObject.SetActive(!ok);
                foreach (var p in _procBlank)
                    p.SetActive(!ok);
                // remembered only once shown: a model still loading (the first ApplyTackle runs before the preload ends)
                // is tried again on the next call, else the built rod stayed for good
                if (!ok)
                    _shownRod = "";
            }
            for (int i = 0; i < _segments.Count; i++)
                foreach (Transform c in _segments[i])
                    if (c.name.StartsWith("Model"))
                        c.localScale = new Vector3(1f, k, 1f);
            if (reelId != _shownReel || (reel?.ItemId ?? "") != _shownReelItem)
            {
                _shownReel = reelId;
                _shownReelItem = reel?.ItemId ?? "";
                GripLeft.SetParent(_frame, false);
                GripLeft.localPosition = new Vector3(0f, -0.2f, 0.02f);
                foreach (Transform c in _reelModel)
                    Destroy(c.gameObject);
                _rotor = _crank = _bail = _spoolLine = _lineExit = null;
                var model = ModelLibrary.Spawn(reelId, _reelModel);
                _modelledReel = false;
                if (model == null && !ModelLibrary.Settled(reelId))
                {
                    ModelLibrary.Start(reelId);
                    _waitReel = reelId;
                    _shownReel = "";   // tried again once loaded
                }
                // the reel's size and finish: a 1000 is smaller than a 5000 on the same body (scaled about the reel's
                // foot at the seat, so it stays on the rod)
                float reelScale = reel != null && reel.ModelScale > 0.1f ? reel.ModelScale : 1f;
                _reelModel.localScale = Vector3.one * reelScale;
                _reelModel.localPosition = new Vector3(0f, 0.1f, 0f) * (1f - reelScale);
                if (model != null && reel != null)
                    Finish(model, new Color(reel.ModelTint.R, reel.ModelTint.G, reel.ModelTint.B));
                if (model != null)
                {
                    foreach (var r in model.GetComponentsInChildren<Renderer>())
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var frames = ModelLibrary.Json(reelId) is { } json ? JsonUtility.FromJson<ReelFrame>(json) : null;
                    if (frames != null && frames.rotor is { Length: 3 } && frames.hub is { Length: 3 })
                    {
                        _modelledReel = true;
                        // the moving parts are stored in their own pivots' frames (meshy_rod_split.py): each goes to its pivot
                        (_rotor, _crank, _bail) = AssembleReel(model.transform, frames);
                        _bailAxis = frames.bail_axis is { Length: 3 } ? M(frames.bail_axis).normalized : Vector3.right;
                        // the wound line cut out of the body, shrinking towards the spool's axis as line goes out
                        var rotorAt = M(frames.rotor);
                        float shellRadius = 0f;
                        if (ModelLibrary.Find(model.transform, "Body") is { } bodyNode && bodyNode.GetComponent<MeshFilter>() is { } mf && mf.sharedMesh != null)
                        {
                            var (rest, shell, radius) = SpoolShell(reelId, mf.sharedMesh, rotorAt);
                            if (shell != null)
                            {
                                mf.sharedMesh = rest;
                                shellRadius = radius;
                                _spoolLine = new GameObject("SpoolLine").transform;
                                _spoolLine.SetParent(bodyNode.parent, false);
                                _spoolLine.localPosition = new Vector3(rotorAt.x, 0f, rotorAt.z);
                                var go = new GameObject("Shell");
                                go.transform.SetParent(_spoolLine, false);
                                go.transform.localPosition = new Vector3(-rotorAt.x, 0f, -rotorAt.z);
                                go.AddComponent<MeshFilter>().sharedMesh = shell;
                                var sr = go.AddComponent<MeshRenderer>();
                                sr.sharedMaterials = bodyNode.GetComponent<MeshRenderer>().sharedMaterials;
                                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            }
                        }
                        _lineExit = new GameObject("LineExit").transform;
                        _lineExit.SetParent(model.transform, false);
                        _lineExit.localPosition = rotorAt + new Vector3(0f, 0.045f, shellRadius > 0f ? shellRadius : 0.028f);
                        if (_crank != null)
                        {
                            // the knob: out from the hub along the crank's axis, the arm's length down
                            GripLeft.SetParent(_crank, false);
                            GripLeft.localPosition = new Vector3(-frames.knob_out, -frames.arm, 0f);
                        }
                    }
                }
                _builtReel.gameObject.SetActive(!_modelledReel);
                if (!_modelledReel)
                {
                    if (model != null)
                        Destroy(model);
                    GripLeft.SetParent(_builtCrank, false);
                    GripLeft.localPosition = new Vector3(0.05f, -0.04f, 0f);
                    _shownReel = "";   // tried again once the model has loaded
                }
                _spoolFill = -1f;
                SetCrank(_crankAngle);
                SetBail(_bailOpen);
            }
        }

        /// <summary>A model's finish: each of its materials (copied for this model) multiplied by the tint.</summary>
        private static void Finish(GameObject model, Color tint)
        {
            if (Mathf.Abs(tint.r - 1f) + Mathf.Abs(tint.g - 1f) + Mathf.Abs(tint.b - 1f) < 0.01f)
                return;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.materials;          // instances: the shared model's materials stay as they are
                foreach (var m in mats)
                    foreach (var prop in new[] { "baseColorFactor", "_BaseColor", "_Color" })
                        if (m.HasProperty(prop))
                        {
                            m.SetColor(prop, m.GetColor(prop) * tint);
                            break;
                        }
                r.materials = mats;
            }
        }

        [Serializable]
        internal sealed class ReelFrame
        {
            public float[]? hub;
            public float[]? rotor;
            public float[]? bail_pivot;
            public float[]? bail_axis;
            public float[]? body;
            public float arm = 0.036f;
            public float knob_out = -0.049f;
        }

        /// <summary>A point of the reel's json (the Godot rig's frame) in the imported model's frame (glTF x mirrored).</summary>
        private static Vector3 M(float[] a) => new(-a[0], a[1], a[2]);

        internal static (Transform? Rotor, Transform? Crank, Transform? Bail) AssembleReel(Transform model, ReelFrame frames)
        {
            var rotor = frames.rotor is { Length: 3 } ? Pivot(model, model, "Rotor", M(frames.rotor)) : null;
            var crank = frames.hub is { Length: 3 } ? Pivot(model, model, "Crank", M(frames.hub)) : null;
            var bail = rotor != null && frames.bail_pivot is { Length: 3 }
                ? Pivot(model, rotor, "Bail", M(frames.bail_pivot)) : null;
            return (rotor, crank, bail);
        }

        /// <summary>
        /// A pivot for a reel part whose mesh is stored in its own frame (meshy_rod_split.py: the rotor about the spool's
        /// axis, the crank about its hub, the bail about its two ends in the rotor's frame): the pivot sits at
        /// <paramref name="at"/> under <paramref name="parent"/> and carries the part at its origin.
        /// </summary>
        private static Transform? Pivot(Transform model, Transform parent, string part, Vector3 at)
        {
            if (ModelLibrary.Find(model, part) is not { } t)
                return null;
            var pivot = new GameObject(part + "Pivot").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = at;
            t.SetParent(pivot, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            return pivot;
        }

        private static readonly Dictionary<string, (Mesh Body, Mesh? Shell, float Radius)> SpoolShells = new();

        /// <summary>
        /// RodRig.SpoolShellOf: the body's triangles in front of the rotor, 14-31.5 mm off the spool's axis and facing
        /// outwards, are the wound line; they go into their own mesh (cached per model), the rest stays the body.
        /// </summary>
        private static (Mesh Body, Mesh? Shell, float Radius) SpoolShell(string id, Mesh body, Vector3 rotor)
        {
            if (SpoolShells.TryGetValue(id, out var done))
                return done;
            var verts = body.vertices;
            var normals = body.normals;
            var rest = Instantiate(body);
            var shell = Instantiate(body);
            float radius = 0f;
            bool any = false;
            for (int si = 0; si < body.subMeshCount; si++)
            {
                var index = body.GetTriangles(si);
                var keep = new List<int>(index.Length);
                var cut = new List<int>();
                if (normals.Length == verts.Length)
                    for (int t = 0; t + 2 < index.Length; t += 3)
                    {
                        var c = (verts[index[t]] + verts[index[t + 1]] + verts[index[t + 2]]) / 3f;
                        var n = normals[index[t]] + normals[index[t + 1]] + normals[index[t + 2]];
                        var radial = new Vector3(c.x - rotor.x, 0f, c.z - rotor.z);
                        float r = radial.magnitude, dy = c.y - rotor.y;
                        bool line = dy > 0.033f && dy < 0.056f && r > 0.014f && r < 0.0315f
                            && n.sqrMagnitude > 1e-8f && Vector3.Dot(radial.normalized, n.normalized) > 0.6f;
                        var into = line ? cut : keep;
                        into.Add(index[t]); into.Add(index[t + 1]); into.Add(index[t + 2]);
                        if (line)
                        {
                            radius = Mathf.Max(radius, r);
                            any = true;
                        }
                    }
                else
                    keep.AddRange(index);
                rest.SetTriangles(keep, si);
                shell.SetTriangles(cut, si);
            }
            var result = any && radius > 0f ? (rest, (Mesh?)shell, radius) : (body, null, 0f);
            if (!any)
            {
                Destroy(rest);
                Destroy(shell);
            }
            SpoolShells[id] = result;
            return result;
        }

        /// <summary>RodRig.SetCrank: the crank to <paramref name="angle"/> and the rotor with it, 5.2 times as fast
        /// (rotations about the rig's axes, mirrored with the imported models).</summary>
        private void SetCrank(float angle)
        {
            _crankAngle = angle;
            var crank = Quaternion.Euler(-angle * Mathf.Rad2Deg, 0f, 0f);
            var rotor = Quaternion.Euler(0f, -angle * 5.2f * Mathf.Rad2Deg, 0f);
            if (_crank != null) _crank.localRotation = crank;
            if (_rotor != null) _rotor.localRotation = rotor;
            _builtCrank.localRotation = Quaternion.Euler(angle * Mathf.Rad2Deg, 0f, 0f);
            _builtRotor.localRotation = Quaternion.Euler(0f, angle * 5.2f * Mathf.Rad2Deg, 0f);
        }

        /// <summary>RodRig.SetBail: open (1) for the cast, flipped shut (0) when the line lands or the crank turns.</summary>
        private void SetBail(float open)
        {
            _bailOpen = Mathf.Clamp01(open);
            if (_bail != null)
                _bail.localRotation = Quaternion.AngleAxis(-_bailOpen * 2.7f * Mathf.Rad2Deg, _bailAxis);
            _builtBail.localRotation = Quaternion.Euler(_bailOpen * 2.7f * Mathf.Rad2Deg, 0f, 0f);
        }

        private static float WoundRadius(float fill) =>
            Mathf.Sqrt(Mathf.Lerp(SpoolCore * SpoolCore, 1f, fill));

        /// <summary>At constant spool width, the amount of wound line is proportional to its annular area.</summary>
        private void SetSpoolFill(float fill)
        {
            fill = Mathf.Clamp01(fill);
            if (Mathf.Abs(fill - _spoolFill) < 0.002f)
                return;
            _spoolFill = fill;
            float k = WoundRadius(fill);
            _builtSpoolLine.localScale = new Vector3(k, 1f, k);
            if (_spoolLine != null)
                _spoolLine.localScale = new Vector3(k, 1f, k);
        }

        /// <summary>With the bail closed, the line runs from the winding surface via the rotating roller to the guides.</summary>
        private int LinePath(Vector3[] into)
        {
            int n = 0;
            var exit = _modelledReel && _lineExit != null ? _lineExit : _builtLineExit;
            bool modelled = _modelledReel && _lineExit != null;
            var rotor = modelled ? _rotor : _builtRotor;
            if (_bailOpen < 0.05f && rotor != null)
            {
                Vector3 axis = rotor.position;
                Vector3 forward = rotor.up;
                float lipRadius = Vector3.ProjectOnPlane(exit.position - axis, forward).magnitude;
                float lipHeight = Vector3.Dot(exit.position - axis, forward);
                Vector3 roller = modelled && _bail != null
                    ? _bail.position
                    : axis + rotor.right * (0.03f * rotor.lossyScale.x) + forward * lipHeight;
                Vector3 radial = Vector3.ProjectOnPlane(roller - axis, forward).normalized;
                if (radial == Vector3.zero)
                    radial = rotor.right;
                float traverse = 0.5f - 0.5f * Mathf.Cos(_crankAngle);
                into[n++] = axis + forward * (lipHeight - Mathf.Lerp(0.024f, 0.010f, traverse) * rotor.lossyScale.y)
                    + radial * (lipRadius * WoundRadius(_spoolFill));
                into[n++] = roller;
            }
            else
                into[n++] = exit.position;
            foreach (var g in _guides)
                into[n++] = g.position;
            into[n++] = _tip.position;
            return n;
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
            // the line paid out and the line inside the rod (spool lip, guides, tip-top): ribbons of FS/FishingLine, as
            // thick as the line or a pixel, dimmed at night, glinting in the sun (Godot: fishing_line.gdshader)
            _line = LineRibbon.Create("Line", _ropePts.Length);
            _line.Color = new Color(0.85f, 0.85f, 0.8f);
            _line.Diameter = 0.00025f;
            _line.gameObject.SetActive(false);
            _inRodLine = LineRibbon.Create("LineInRod", _inRod.Length);
            _inRodLine.Color = _line.Color;
            _inRodLine.Diameter = 0.00025f;
        }

        /// <summary>The rod shows only in play (the menu's flying camera carries it otherwise).</summary>
        public void ShowRod(bool show)
        {
            if (_rodRoot != null)
                _rodRoot.gameObject.SetActive(show);
            if (_inRodLine != null)
                _inRodLine.gameObject.SetActive(show);
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
            // a worn line holds less (LineWear.Strength)
            float lineHold = line != null ? LineWear.Strength(line.Strength, Profile.WearOf(Profile.Line)) : 1f;
            EffectiveLimit = Mathf.Min(lineHold, Mathf.Min(rod?.MaxLoad ?? 1f, hook?.Strength ?? 1f));
            LineLimit = Mathf.Min(lineHold, hook?.Strength ?? 1f);
            RodLimit = rod?.MaxLoad ?? 1f;
            if (line != null)
            {
                var c = line.LineColor;
                _line.Color = _inRodLine.Color = new Color(c.R, c.G, c.B);
                _line.Diameter = _inRodLine.Diameter = Mathf.Max(line.Diameter, 0.05f) * 0.001f;   // LineData: millimetres
            }
            if (rod != null && bait() is { } b)
                Method = FishingSim.Core.Methods.Method.For(rod.Kind, b.Kind);
            // the blank: ten springy joints, stiffer the more power, the whole blank working on a slow rod
            if (rod != null && (_bendModel == null || _bendFor != rod || Mathf.Abs(_bendModel.Length - _section * 10f) > 0.01f))
            {
                _bendModel = new Core.Physics.RodBendModel(_section * 10f, rod.MaxLoad, rod.Action);
                _bendFor = rod;
            }
            if (line != null)
            {
                var (sink, floats) = Core.Physics.LineRope.Behaviour((int)line.Kind);
                _rope.SinkSpeed = sink;
                _rope.Floats = floats;
            }
            _rope.WaterY = 0f;
            _rope.BottomY = (x, z) => World.Lake.HeightAt(x, z);
            BaitData? bait() => Bait;
        }

        /// <summary>FishingRod.MakeBiteContext: the bait, hook and depth, the place's plants and drop-offs, where the bait
        /// hangs in the water column and the temperature there, the moonlight at night, the light.</summary>
        private BiteContext Context(float depth)
        {
            var at = _bobber != null ? _bobber.position : Player.transform.position;
            var (cover, edge) = StructureAt(at.x, at.z);
            float? layer = BaitLayerAt(depth);
            return new BiteContext(
                Profile.Bait,
                Hook?.Size ?? 5f,
                depth,
                World.Lake.Location,
                Day.Period,
                Day.Weather,
                layer,
                WaterTemperature.AtDepth(WaterTemp, depth * (layer ?? 1f)),
                cover,
                edge,
                Day.Night > 0.5f ? Day.MoonLight : null,
                Shoal.Any || _rises.Count > 0 ? sp => ShoalAt(sp, at.x, at.z) : null,
                Day.Light);
        }

        private IReadOnlyList<FishSpecies> SpeciesHere => Tackle.AllSpecies;

        // ------------------------------------------------------------------ per frame
        private string? _waitRod, _waitReel;

        /// <summary>The rod's and the reel's model ids this rig wants (the shop's choice), loaded or not.</summary>
        public string WantedRodModel => Rod != null && Rod.Model.Length > 0 ? Rod.Model : "rods/float_ostoja";
        public string WantedReelModel => Reel != null && Reel.Model.Length > 0 ? Reel.Model : "reels/small_hokuto";

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _stateTime += dt;
            // a rod or reel model that was still loading when the tackle was put together: shown now
            if ((_waitRod != null && ModelLibrary.Settled(_waitRod)) || (_waitReel != null && ModelLibrary.Settled(_waitReel)))
            {
                _waitRod = _waitReel = null;
                ApplyTackle();
            }
            bool canAct = !Blocked && Cursor.lockState == CursorLockMode.Locked;
            bool lmbDown = canAct && Input.GetMouseButtonDown(0) || SmokeCharge && State == RodState.Idle, lmb = canAct && Input.GetMouseButton(0) || SmokeReel || SmokeCharge,
                lmbUp = canAct && Input.GetMouseButtonUp(0);
            bool rmbDown = canAct && Input.GetMouseButtonDown(1), rmb = canAct && Input.GetMouseButton(1);
            if (canAct)
            {
                // wheel: the drag; with Ctrl the float's depth (FloatDepth 0.2-6 m, only between casts)
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    if (State == RodState.Idle)
                        FloatDepth = Mathf.Clamp(FloatDepth + Input.mouseScrollDelta.y * 0.1f, 0.2f, 6f);
                }
                else
                    DragSetting = Mathf.Clamp01(DragSetting + Input.mouseScrollDelta.y * 0.05f);
            }
            Day.MoonPhase = Moon.Phase(Profile.Clock);
            TickConditions(dt);
            // B: sharpen the hook on the hone (FishingRod.SharpenHook; needs the hone, between casts)
            if (canAct && State == RodState.Idle && Input.GetKeyDown(KeyCode.B))
            {
                if (!Profile.HasAccessory("oselka"))
                    Said?.Invoke("Potrzebna osełka (sklep, akcesoria).");
                else if (Profile.DullOf(Profile.Hook) <= 0.001f)
                    Said?.Invoke("Haczyk jest ostry.");
                else
                {
                    Profile.HookDull[Profile.Hook] = 0f;
                    Said?.Invoke("Haczyk naostrzony.");
                }
            }

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
                    // the rod goes up to 60 degrees while the button is held; let go, it swings fast back to where it was
                    // and the end tackle leaves the tip at the end of the swing (the power is held from the release)
                    if (_swing < 0f)
                    {
                        CastPower = Mathf.PingPong(_stateTime / 1.1f, 1f);
                        if (lmbUp || !lmb)
                            _swing = 0f;
                    }
                    else
                    {
                        _swing += dt;
                        if (_swing >= SwingSeconds)
                        {
                            _swing = -1f;
                            Cast();
                        }
                    }
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

        private float TipDistance() => _tip != null && _bobber != null ? Vector3.Distance(_tip.position, _bobber.position) : 0f;

        /// <summary>
        /// FishingRod.TakeUpSlack: reeling in <paramref name="amount"/> metres of line takes up the slack lying on the water
        /// first (twice as fast: a slack line offers no resistance); what is left moves the end tackle.
        /// </summary>
        private float TakeUpSlack(float amount)
        {
            float slack = _lineLength - TipDistance();
            if (slack > 0.01f && slack < 0.25f && amount > 0f && _drawTight > 0.5f)
            {
                _lineLength = TipDistance() + 0.005f;
                return amount;
            }
            if (slack > 0.03f && amount > 0f)
            {
                float take = Mathf.Min(slack - 0.02f, amount * 2f);
                _lineLength -= take;
                amount -= take / 2f;
            }
            return Mathf.Max(amount, 0f);
        }

        /// <summary>A kick through the blank (bites, the strike, the cast).</summary>
        private void Twitch(float strength) => _bendModel?.Impulse(strength);

        /// <summary>The end tackle's weight hanging from the line (kg): the lure; the float rig or the feeder with the hook.</summary>
        public float TackleWeightKg => (Method == FishingMethod.Spinning ? Mathf.Max(Bait?.WeightG ?? 8f, 1f)
            : (Method == FishingMethod.Bottom ? FeederGrams : FloatRigGrams) + (Hook?.WeightG ?? 0.1f)) / 1000f;

        /// <summary>
        /// FishingRod.BendLoad: what pulls the tip (kg). The fight's tension; the hanging end tackle before the cast; with the
        /// line out only what a tight line carries (a lure drawn through the water, the feeder, a bite on the quiver, a
        /// float drawn in); a slack line pulls a couple of grams, so the blank stands straight.
        /// </summary>
        public float BendLoad()
        {
            if (State == RodState.Fight && Fight != null)
                return Fight.Tension;
            if (State is RodState.Idle or RodState.Charging)
                return TackleWeightKg;
            if (State is not (RodState.Waiting or RodState.Nibble or RodState.Bite))
                return 0f;
            float taut = Mathf.Clamp01(1f - Slack / 0.25f);
            float pull = Method switch
            {
                FishingMethod.Spinning => (Snagged ? (Reeling ? 0.6f : 0.25f) * EffectiveLimit : 0f)
                    + (Reeling ? TackleWeightKg * 0.6f + 0.05f * Mathf.Pow(Mathf.Min(Lure?.Speed ?? 0f, 2f), 2f) : 0f),
                FishingMethod.Bottom => TackleWeightKg * 0.6f + _bitePull * 0.9f,
                _ => Reeling ? 0.03f : 0f,
            };
            return 0.002f + pull * taut;
        }

        private void Set(RodState s)
        {
            State = s;
            _stateTime = 0f;
            _swing = -1f;
        }

        /// <summary>Crank turns a second: a steady wind 1.1, Shift fast 1.8.</summary>
        public float CrankTurns => Input.GetKey(KeyCode.LeftShift) ? 1.8f : 1.1f;

        /// <summary>
        /// Metres of line one turn of the crank lays on the spool: the reel's gear (ReelData.ReelSpeed, about 0.84 m a turn
        /// on a full spool of a 3.5) times how full the spool is (a spool run low has a smaller diameter, so it brings in
        /// less a turn: down to 70 %).
        /// </summary>
        public float MetresPerTurn => (Reel?.ReelSpeed ?? 3f) * 0.24f * (0.7f + 0.3f * Mathf.Clamp01(_spoolFill < 0f ? 1f : _spoolFill));

        /// <summary>Line brought in a second while cranking (m/s).</summary>
        public float RetrieveRate => CrankTurns * MetresPerTurn;

        /// <summary>The cast's forward swing: seconds from the release to the end tackle leaving the tip (-1 = not swinging).</summary>
        private float _swing = -1f;
        private const float SwingSeconds = 0.16f, CastLift = 60f;

        private void Cast()
        {
            var rod = Rod!;
            float power = Mathf.Max(CastPower, 0.08f);
            float weight = Method == FishingMethod.Bottom ? 40f : Method == FishingMethod.Spinning ? Mathf.Max(Bait?.WeightG ?? 8f, 3f) : 4.5f;
            float reach = CastReach();
            var dir = Flat(Player.Forward).normalized;
            var (degrees, speedFactor) = Casting.Scatter(power, _rng);     // a soft bell round the aim, wider at full power
            dir = Quaternion.Euler(0f, degrees, 0f) * dir;
            float distance = Mathf.Lerp(5f, reach, power) * speedFactor;
            _baitFresh = 1f;          // a fresh bait on every cast
            _baitOn = true;
            // a twisted line can throw a bird's nest: the cast dies short, half the twist comes out, the line wears
            if (LineItem != null && _rng.NextFloat() < LineTwist.TangleChance(Profile.TwistOf(Profile.Line), (int)LineItem.Kind))
            {
                distance *= 0.3f;
                Profile.LineTwist[Profile.Line] = LineTwist.AfterTangle(Profile.TwistOf(Profile.Line));
                Profile.LineWear[Profile.Line] = Mathf.Min(Profile.WearOf(Profile.Line) + 0.03f, LineWear.Max);
                Said?.Invoke("Splątanie żyłki (\"broda\")! Zestaw padł blisko.");
            }
            _origin = Player.transform.position;
            _bobber.position = _tip.position;
            // a real flight: launched from the tip at LaunchDegrees, gravity, air drag by the end tackle's weight and
            // size (a light float rig slows fast, a feeder flies on), carried by the wind as Casting.WindCatch says.
            // The launch speed is the one that, without wind, lands where the cast's power aims (the old balance kept)
            _flightDrag = AirDrag(weight);
            _flightWind = WindVelocity * Casting.WindCatch(Method, weight);
            float start = _bobber.position.y - Waves.HeightAt(_bobber.position.x, _bobber.position.z, Time.time);
            float v0 = LaunchSpeed(distance, _flightDrag, start);
            float a = LaunchDegrees * Mathf.Deg2Rad;
            _velocity = dir * (v0 * Mathf.Cos(a)) + Vector3.up * (v0 * Mathf.Sin(a));
            _bobber.gameObject.SetActive(true);
            _line.gameObject.SetActive(true);
            _lineLength = 0.3f;
            _ropeLive = false;
            Twitch(-0.6f * power);            // the snap of the cast through the blank
            Set(RodState.Flying);
        }

        /// <summary>The cast's launch angle above the horizon (degrees).</summary>
        private const float LaunchDegrees = 32f;

        private float _flightDrag;
        private Vector3 _flightWind;

        /// <summary>The wind at the water (m/s), as the line and the flight feel it.</summary>
        private Vector3 WindVelocity => new Vector3(0.8f, 0f, 0.6f) * (Day != null ? Day.Wind * 3f : 0.6f);

        /// <summary>Quadratic air drag of the end tackle (1/m): 1/2 rho Cd A / m, the cross-section growing a little with
        /// the weight (a 4.5 g float rig about 0.02, a 40 g feeder about 0.006, a 10 g lure about 0.008).</summary>
        private static float AirDrag(float weightG)
        {
            float area = 1.1e-4f + 6e-6f * weightG;       // m2
            return 0.5f * 1.2f * 0.85f * area / (Mathf.Max(weightG, 1f) / 1000f);
        }

        /// <summary>How far a launch at this speed carries (no wind), from a height above the water.</summary>
        private static float Carry(float v0, float drag, float height)
        {
            float a = LaunchDegrees * Mathf.Deg2Rad;
            float vx = v0 * Mathf.Cos(a), vy = v0 * Mathf.Sin(a), x = 0f, y = height;
            const float dt = 0.01f;
            for (int i = 0; i < 2000 && y > 0f; i++)
            {
                float v = Mathf.Sqrt(vx * vx + vy * vy);
                vx -= drag * v * vx * dt;
                vy -= (9.81f + drag * v * vy) * dt;
                x += vx * dt;
                y += vy * dt;
            }
            return x;
        }

        /// <summary>The launch speed that carries <paramref name="distance"/> metres (bisection on <see cref="Carry"/>).</summary>
        private static float LaunchSpeed(float distance, float drag, float height)
        {
            float lo = 1f, hi = 90f;
            for (int i = 0; i < 24; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (Carry(mid, drag, height) < distance) lo = mid; else hi = mid;
            }
            return 0.5f * (lo + hi);
        }

        private void TickFlight(float dt)
        {
            // gravity and the drag of the air moving past the end tackle (the wind's velocity taken off it)
            var air = _velocity - _flightWind;
            _velocity += (Vector3.down * 9.81f - air * (_flightDrag * air.magnitude)) * dt;
            _bobber.position += _velocity * dt;
            // the line runs off the open spool behind the flying end tackle
            _lineLength = Mathf.Max(_lineLength, TipDistance() * 1.04f);
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
                // a little slack on landing (more for a float than for a lure); the bail snaps shut
                _lineLength = TipDistance() * (Method == FishingMethod.Spinning ? 1.02f : 1.07f);
                Cue?.Invoke(Method == FishingMethod.Bottom ? Core.Audio.OneShot.Splash : Core.Audio.OneShot.SmallSplash, 0.8f);
                // the splash frightens the fish round it (less in rain and wind)
                Disturbance.Add(Disturbance.Splash(Method, TackleWeightKg * 1000f, DepthHere) * NoiseMasking);
                Lure = Method == FishingMethod.Spinning && Bait != null
                    ? new LureMotion(FishingSim.Core.Methods.Method.FamilyOf(Bait.AffinityAs), LureTuning.From(Bait))
                    : null;
                // the feeder empties one bait as groundbait on the bottom where it lies (with at least two left)
                if (Method == FishingMethod.Bottom && Profile.BaitCount(Profile.Bait) >= 2)
                {
                    Profile.UseBait();
                    Groundbait.Feed(p.x, p.z);
                }
                NewBiteTimer();
                Set(RodState.Waiting);
            }
        }

        // ------------------------------------------------------------------ automated test (Smoke)
        /// <summary>Held down by the smoke test: reels as if the left button were held.</summary>
        public bool SmokeReel { get; set; }

        /// <summary>The smoke test hooks a fish on the end tackle in the water (a fight to photograph the HUD in).</summary>
        public void SmokeHook(string species, float weight)
        {
            if (State is not (RodState.Waiting or RodState.Nibble or RodState.Bite) || Tackle.Species(species) is not { } sp)
                return;
            Species = sp;
            Weight = weight;
            StartFight(1f);
        }

        /// <summary>Held by the smoke test: the cast button held (charging); let go, the rod swings and casts.</summary>
        public bool SmokeCharge { get; set; }

        /// <summary>The cast's forward swing is under way (the smoke test photographs it).</summary>
        public bool Swinging => _swing >= 0f;

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
            _lineLength = TipDistance() * 1.05f;
            _ropeLive = false;
            Lure = Method == FishingMethod.Spinning && Bait != null
                ? new LureMotion(FishingSim.Core.Methods.Method.FamilyOf(Bait.AffinityAs), LureTuning.From(Bait))
                : null;
            NewBiteTimer();
            Set(RodState.Waiting);
        }

        /// <summary>Makes the bite clock run out now.</summary>
        public void SmokeBiteNow() => _biteTimer = 0f;

        /// <summary>Strikes at the bite (always hooks).</summary>
        public void SmokeStrike()
        {
            // a line or rod lost in an earlier fight leaves nothing to strike with
            if (State == RodState.Bite && Species != null && Reel != null && LineItem != null && Rod != null)
                StartFight(1f);
        }

        /// <summary>Whether the rod, reel and line are all there (the smoke test stops fishing when one was lost).</summary>
        public bool SmokeTackleReady => Reel != null && LineItem != null && Rod != null;

        private void NewBiteTimer()
        {
            var ctx = Context(DepthHere);
            float attraction = BiteModel.Attraction(SpeciesHere, ctx);
            // LineShyness: in clear water and good light the fish see the line better
            float vis = LineShyness.Effective(LineItem?.Visibility ?? 1f, Clarity, Day.Light);
            _biteTimer = BiteModel.WaitSeconds(_rng, 4f, 12f, vis, attraction) * (Method == FishingMethod.Spinning ? 0.6f : 1f)
                         / (1f + BiteBonus());
        }

        /// <summary>
        /// The spinning retrieve (FishingRod.TickSpinning): the lure runs at the depth its speed gives (Shift: faster), sinks
        /// when the reel stands, hops on a twitch (RMB), snags among sunken wood; a snag is jerked (RMB: often free, now and
        /// then the line gives) or pulled (LMB held: frees it or, after a few seconds, breaks the line). Returns true when the
        /// end tackle left the water (handled).
        /// </summary>
        private bool TickLure(float dt, bool lmb, bool rmbDown, ref Vector3 p)
        {
            var lure = Lure!;
            float wood = 0f;
            foreach (var (wx, wz, r) in Forest.SunkenWood)
                wood = Mathf.Max(wood, Structure.WoodAt(new[] { new SunkenWood(wx, wz, r) }, p.x, p.z));
            lure.SnagFactor = Structure.SnagFactor(wood);
            if (lure.Snagged)
            {
                SnagResult result = SnagResult.Stuck;
                if (rmbDown)
                {
                    result = lure.Jerk(_rng);
                    Profile.LineWear[Profile.Line] = Mathf.Min(Profile.WearOf(Profile.Line) + LineWear.PerSnagJerk, LineWear.Max);
                    Twitch(0.5f);
                }
                else if (lmb)
                {
                    result = lure.Pull(_rng, dt);
                    Profile.LineWear[Profile.Line] = Mathf.Min(Profile.WearOf(Profile.Line) + LineWear.PerSnagPullSecond * dt, LineWear.Max);
                }
                if (result == SnagResult.Freed)
                    Said?.Invoke("Przynęta odczepiona.");
                else if (result == SnagResult.Broke)
                {
                    Said?.Invoke("Zaczep! Żyłka pękła — przynęta i haczyk zostały na dnie.");
                    Cue?.Invoke(Core.Audio.OneShot.LineSnap, 1f);
                    Profile.Destroy(Profile.Hook);
                    Profile.UseBait();
                    Reset();
                    return true;
                }
                return false;
            }
            if (rmbDown)
            {
                // a twitch: the lure darts towards the angler (the slack goes first) and flashes
                float dart = TakeUpSlack(lure.Twitch());
                var toAngler = Flat(Player.transform.position - p);
                if (toAngler.sqrMagnitude > 1e-4f)
                    p += toAngler.normalized * dart;
                Twitch(0.3f);
            }
            float retrieve = lmb ? RetrieveRate : 0f;
            bool wasSnagged = lure.Snagged;
            lure.Step(dt, retrieve, Mathf.Max(DepthHere, 0.05f), _rng);
            if (!wasSnagged && lure.Snagged)
            {
                Said?.Invoke("Zaczep! RMB: szarpnij, LMB: ciągnij.");
                Profile.HookDull[Profile.Hook] = Mathf.Min(Profile.DullOf(Profile.Hook) + HookWear.PerSnag, HookWear.Max);
            }
            return false;
        }

        private void TickWaiting(float dt, bool lmb, bool rmbDown)
        {
            var p = _bobber.position;
            if (Lure != null && State == RodState.Waiting && TickLure(dt, lmb, rmbDown, ref p))
                return;
            // retrieve: the float/feeder comes in, the lure works
            if (lmb && State == RodState.Waiting && !Snagged)
            {
                float speed = RetrieveRate;
                var toAngler = Flat(Player.transform.position - p);
                if (toAngler.magnitude < 2.2f || World.Lake.HeightAt(p.x, p.z) > -0.08f)
                {
                    Said?.Invoke("Zestaw wyjęty z wody.");
                    Reset();
                    return;
                }
                // the slack lying on the water is taken up first; only then does the end tackle come in
                float pull = TakeUpSlack(speed * dt);
                p += toAngler.normalized * pull;
                // a spinning lure twists the line as it is drawn (a swivel takes most of it out)
                if (Method == FishingMethod.Spinning && Bait != null && pull > 0f)
                {
                    var family = FishingSim.Core.Methods.Method.FamilyOf(Bait.AffinityAs);
                    float twist = LineTwist.PerMetre(family, Profile.HasAccessory("kretlik")) * pull;
                    Profile.LineTwist[Profile.Line] = Mathf.Min(Profile.TwistOf(Profile.Line) + twist, LineTwist.Max);
                }
                DepthHere = -World.Lake.HeightAt(p.x, p.z);
                _lineSlack = Mathf.MoveTowards(_lineSlack, 0f, dt * 2f);
            }
            float surface = Waves.HeightAt(p.x, p.z, Time.time);
            float y = Method switch
            {
                FishingMethod.Float => surface - 0.01f - _dip * 0.09f,
                FishingMethod.Spinning => Lure != null ? Mathf.Max(surface - Lure.Depth, -DepthHere + 0.03f) : Mathf.Max(surface - 0.5f, -DepthHere + 0.1f),
                _ => -DepthHere + 0.05f,
            };
            // a running bite slides the float away across the water before it goes under
            if (State == RodState.Bite && Method == FishingMethod.Float && Style == BiteStyle.Run && _run > 0f)
            {
                var away = Flat(p - Player.transform.position).normalized;
                p += away * dt * 0.45f * _run;
            }
            _bobber.position = new Vector3(p.x, y, p.z);
            // a lifting bite lays the float flat on the water
            _bobber.rotation = Quaternion.Euler(_tilt * 85f, 0f, 0f);
            // the line never stretches: when the end tackle drifts off, the line follows
            _lineLength = Mathf.Max(_lineLength, TipDistance());

            // the bite clock runs while the bait waits (a lure only while it is drawn)
            if (State == RodState.Waiting)
            {
                if (Method == FishingMethod.Spinning && Lure != null && Rod != null)
                    // the lure's play drives the bite clock: its quality, a falling jig, the flash after a twitch, the colours
                    _biteTimer -= dt * Lure.BitePace * FishingSim.Core.Methods.Method.LureRodMatch(Rod.Kind) * 2f * ConditionPace * LureAppeal;
                else if (Method != FishingMethod.Spinning)
                    _biteTimer -= dt * ConditionPace * BaitPace * GroundbaitPace;
                if (rmbDown && Method != FishingMethod.Spinning)
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
                // little plucks at the float / the quiver tip (small fish pluck quicker and harder)
                float pluck = Style == BiteStyle.Pluck ? 0.5f : 0.35f;
                _dip = Mathf.Max(0f, Mathf.Sin(_stateTime * (Style == BiteStyle.Pluck ? 14f : 9f)) * pluck * (_rng.NextFloat() < 0.5f ? 1f : 0.4f));
                if (Method != FishingMethod.Float && _rng.NextFloat() < dt * 3f)
                    Twitch(Method == FishingMethod.Spinning ? 0.25f : 0.45f);      // the quiver tip plucks (the float shows bites on its own)
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
            // Bite (FishingRod.FloatBite): by the species' style the float goes under, is pulled under in a short tug, rises
            // and lies flat (a bait lifted off the bottom), or slides away and then goes under; the tip pulls round
            if (Method == FishingMethod.Float)
                switch (Style)
                {
                    case BiteStyle.Lift:
                        _dip = Mathf.MoveTowards(_dip, -0.6f, dt * 2f);
                        _tilt = Mathf.MoveTowards(_tilt, 1f, dt * 1.6f);
                        break;
                    case BiteStyle.Run:
                        _run = Mathf.MoveTowards(_run, 1f, dt * 2f);
                        _dip = _stateTime > 0.7f ? Mathf.MoveTowards(_dip, 1f, dt * 4f) : 0.15f;
                        break;
                    case BiteStyle.Pluck:
                        _dip = Mathf.MoveTowards(_dip, 1f, dt * 9f);
                        break;
                    default:
                        _dip = Mathf.MoveTowards(_dip, 1f, dt * 5f);
                        break;
                }
            else
                _dip = Mathf.MoveTowards(_dip, 1f, dt * 5f);
            // the hook window: longer in warm water, shorter in cold (WaterTemperature.WindowFactor at the bait)
            float window = FishingSim.Core.Methods.Method.HookWindow(Method) * WaterTemperature.WindowFactor(BaitTemperature) + 0.4f;
            // a float is ready to strike only once it is under (or lying flat on a lift): sooner the fish only feels it
            bool ready = Method != FishingMethod.Float || _dip >= 0.9f || (Style == BiteStyle.Lift && _tilt >= 0.85f);
            if (rmbDown && !ready)
            {
                Said?.Invoke("Za wcześnie! Ryba uciekła.");
                Missed(keepBait: 0.6f);
                return;
            }
            if (rmbDown)
            {
                float lateness = Mathf.Clamp01(_stateTime / window);
                // Strike.Efficiency: a long stretchy line and slack lose the strike's force; a loose drag too
                float efficiency = Strike.Efficiency(Distance, LineItem?.Stretch ?? 0.5f, Slack)
                    * Strike.DragFactor(DragSetting * (Reel?.DragMax ?? 3f));
                float chance = 0.95f * (1f - 0.4f * lateness) * efficiency * HookWear.SetFactor(Profile.DullOf(Profile.Hook));
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
            _tilt = 0f;
            _run = 0f;
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
            _hookedAt = _bobber.position;
            _tilt = 0f;
            _run = 0f;
            _bobber.rotation = Quaternion.identity;
            _jump = 0f;
            _weedTime = 0f;
            Disturbance.Add(0.3f);       // a fish thrashing on the hook frightens the others
            Fight = new FightModel(FishProfile.From(Species!, Weight), gear, _rng, Mathf.Max(to.magnitude, 2.5f), Mathf.Atan2(to.x, to.z), DragSetting)
            {
                HookHold = hold,
            };
            ShowFishModel(Species!);
            float length = Species!.LengthRef * Mathf.Pow(Mathf.Max(Weight, 0.005f), 0.33f);
            _fish.localScale = Vector3.one * Mathf.Clamp(length, 0.08f, 2.2f);
            _fish.gameObject.SetActive(true);
            Cue?.Invoke(Core.Audio.OneShot.Strike, 1f);
            Twitch(0.9f);
            Set(RodState.Fight);
        }

        private void TickFight(float dt, bool reeling, bool lifting)
        {
            var fight = Fight!;
            var outcome = fight.Step(dt, DragSetting, reeling, lifting);
            // the load wears the line (more near its breaking point)
            if (LineItem != null)
                Profile.LineWear[Profile.Line] = Mathf.Min(Profile.WearOf(Profile.Line) + LineWear.FromLoad(fight.Tension, LineItem.Strength, dt), LineWear.Max);
            if (outcome == FightOutcome.Ongoing)
            {
                // a leap: the fish clears the water and shakes its head; a rod held high (or a weakly set hook) loses it
                if (_jump <= 0f && fight.Distance < 30f && _rng.NextFloat() < dt * (0.02f + 0.08f * (Species?.Aggression ?? 0.5f)) * (fight.Bursting ? 3f : 1f))
                    _jump = 1f;
                if (_jump > 0f)
                {
                    float before = _jump;
                    _jump = Mathf.Max(0f, _jump - dt * 1.6f);
                    if (before > 0.5f && _jump <= 0.5f)
                    {
                        Cue?.Invoke(Core.Audio.OneShot.SmallSplash, 1f);
                        float load = fight.Tension / Mathf.Max(fight.PeakPull, 0.1f);
                        if (_rng.NextFloat() < Jumps.ThrowChance(lifting, fight.HookHold, load))
                        {
                            Said?.Invoke("Ryba wyskoczyła z wody i wypięła się.");
                            LoseFish();
                            return;
                        }
                    }
                }
                // a small fish on the line can be grabbed by a pike, perch or zander (PreyAttack)
                if (Species != null && PreyAttack.IsPrey(Species.SpeciesName, Weight) && _rng.NextFloat() < PreyAttack.ChancePerSecond(PredatorPresence()) * dt)
                {
                    PredatorTakes();
                    return;
                }
                // in a weed bed or among sunken branches the line tangles unless the rod is lifted to drag it out
                var at = _origin + new Vector3(fight.Position.X, 0f, fight.Position.Y);
                var (cover, _) = StructureAt(at.x, at.z);
                if (cover > 0.6f && !lifting)
                {
                    _weedTime += dt;
                    if (_weedTime > Structure.TangleSeconds)
                    {
                        Said?.Invoke("Ryba wplątała żyłkę w zielsko i się zerwała.");
                        Profile.Destroy(Profile.Hook);
                        LoseFish();
                        return;
                    }
                }
                else
                    _weedTime = Mathf.Max(0f, _weedTime - dt * 2f);
            }
            var pos = _origin + new Vector3(fight.Position.X, 0f, fight.Position.Y);
            float surface = Waves.HeightAt(pos.x, pos.z, Time.time);
            float depthAt = -World.Lake.HeightAt(pos.x, pos.z);
            float swim = Mathf.Lerp(-0.6f, -0.12f, Mathf.Clamp01(1f - fight.Distance / 25f));
            swim = Mathf.Max(swim, -Mathf.Max(depthAt - 0.15f, 0.05f));
            if (_jump > 0f)
                swim = Mathf.Max(swim, 0.9f * Mathf.Sin(Mathf.Clamp01(1f - _jump) * Mathf.PI) - 0.1f);     // the leap's arc
            _fish.position = new Vector3(pos.x, surface + swim, pos.z);
            _fish.rotation = Quaternion.Euler(0f, fight.Heading * Mathf.Rad2Deg, Mathf.Sin(Time.time * 9f) * 8f);
            _bobber.position = Vector3.Lerp(_bobber.position, new Vector3(pos.x, surface, pos.z) + (_origin - pos).normalized * 0.4f, 1f - Mathf.Exp(-dt * 10f));
            _lineSlack = fight.LineSlack ? 0.6f : 0f;
            switch (outcome)
            {
                case FightOutcome.Landed:
                    Cue?.Invoke(Core.Audio.OneShot.Catch, 1f);
                    // every fish wears the line a little and blunts the hook; the fight takes some twist out
                    Profile.LineWear[Profile.Line] = Mathf.Min(Profile.WearOf(Profile.Line) + LineWear.PerFish, LineWear.Max);
                    Profile.HookDull[Profile.Hook] = Mathf.Min(Profile.DullOf(Profile.Hook) + HookWear.PerFish(Weight), HookWear.Max);
                    Profile.LineTwist[Profile.Line] = Mathf.Max(0f, Profile.TwistOf(Profile.Line) - LineTwist.FightRelief(fight.Elapsed));
                    // the swim is fished a little more; a shoaling fish's shoal stays about
                    Swim.Add(_hookedAt.x, _hookedAt.z, Profile.Clock + 1f);
                    if (Species != null && Shoals.IsShoaling(Species.SpeciesName))
                        Shoal.Add(Species.SpeciesName, _hookedAt.x, _hookedAt.z, Profile.Clock);
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

        /// <summary>How many predators the lake holds (tendency x abundance of PreyAttack.Predators).</summary>
        private float PredatorPresence()
        {
            float sum = 0f;
            foreach (var (name, tendency) in PreyAttack.Predators)
                sum += tendency * World.Lake.Location.AbundanceOf(name);
            return sum;
        }

        /// <summary>
        /// FishingRod.PredatorTakes: a predator grabs the little fish on the line; the fight goes on with it (a big one, as
        /// on a live bait), hooked less firmly.
        /// </summary>
        public void PredatorTakes()
        {
            if (Fight == null || Species == null)
                return;
            var choices = new List<(FishSpecies Sp, float W)>();
            float total = 0f;
            foreach (var sp in SpeciesHere)
                if (PreyAttack.Predators.TryGetValue(sp.SpeciesName, out float tendency) && sp.SpeciesName != Species.SpeciesName)
                {
                    float w = tendency * World.Lake.Location.AbundanceOf(sp.SpeciesName);
                    if (w > 0f)
                    {
                        choices.Add((sp, w));
                        total += w;
                    }
                }
            if (choices.Count == 0)
                return;
            float roll = _rng.NextFloat() * total;
            var predator = choices[^1].Sp;
            foreach (var (sp, w) in choices)
            {
                roll -= w;
                if (roll <= 0f)
                {
                    predator = sp;
                    break;
                }
            }
            string prey = Species.SpeciesName;
            var old = Fight;
            Species = predator;
            Weight = BiteModel.RollWeight(predator, (Hook?.Size ?? 5f) + BaitSize.HookShift("livebait"), _rng);
            var reel = Reel!;
            var rod = Rod!;
            var line = LineItem!;
            float shock = Mathf.Min(rod.ShockAbsorb + 0.12f * line.Stretch, 0.5f);
            var gear = new FightGear(reel.DragMax, EffectiveLimit, shock, reel.ReelSpeed, reel.LineCapacity);
            Fight = new FightModel(FishProfile.From(predator, Weight), gear, _rng, Mathf.Max(old.Distance, 2.5f), old.FishAngle, DragSetting)
            {
                HookHold = PreyAttack.Hold,
            };
            ShowFishModel(predator);
            float length = predator.LengthRef * Mathf.Pow(Mathf.Max(Weight, 0.005f), 0.33f);
            _fish.localScale = Vector3.one * Mathf.Clamp(length, 0.08f, 2.2f);
            Twitch(1.2f);
            Cue?.Invoke(Core.Audio.OneShot.Strike, 1f);
            Said?.Invoke($"{predator.SpeciesName} chwycił holowaną rybę ({prey})!");
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
            if (keep && !BagLimits.MayKeep(Species.SpeciesName, Profile.KeptOf(Species.SpeciesName)))
            {
                Said?.Invoke($"Limit dzienny: {Species.SpeciesName} — tylko {BagLimits.LimitOf(Species.SpeciesName)} szt. dziennie. Ryba wraca do wody.");
                keep = false;
            }
            Profile.AddCatch(Species, Weight);
            if (keep)
                Profile.KeptToday[Species.SpeciesName] = Profile.KeptOf(Species.SpeciesName) + 1;
            if (keep)
                Profile.Keepnet.Add(new KeptFish { Species = Species.SpeciesName, Name = Species.DisplayNameOr(), Weight = Weight });
            else
            {
                Profile.Xp += Mathf.RoundToInt(Progression.FishXp(Species, Weight) * 0.2f);   // a small bonus for letting it go
                Swim.Add(_hookedAt.x, _hookedAt.z, Profile.Clock, SwimPressure.Released - 1f);  // a released fish counts half
            }
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
            Lure = null;
            _tilt = 0f;
            _run = 0f;
            if (State != RodState.Landed || Species == null)
                Set(RodState.Idle);
            else
                Set(RodState.Idle);
        }

        // ------------------------------------------------------------------ rod, line
        private void UpdateVisuals(float dt)
        {
            bool lineOut = State is RodState.Waiting or RodState.Nibble or RodState.Bite or RodState.Fight;
            // the bail opens for the cast and snaps shut when the line lands
            SetBail(Mathf.MoveTowards(_bailOpen, State is RodState.Charging or RodState.Flying ? 1f : 0f, dt * 7f));
            _bitePull = Mathf.MoveTowards(_bitePull, State == RodState.Bite && Method == FishingMethod.Bottom ? 1f : 0f, dt * 3f);

            // the blank: every joint bends by the line's moment about it (RodBendModel), in the plane of the line
            if (_bendModel != null)
            {
                float load = BendLoad();
                Vector3 target = lineOut ? _bobber.position : _frame.position + Vector3.down * 10f;
                var straightTip = new Vector3(0f, BlankStart + _bendModel.Length, 0f);
                var dir = (_frame.InverseTransformPoint(target) - straightTip).normalized;
                var perp = dir - Vector3.up * dir.y;
                float side = perp.magnitude;
                var axis = side > 1e-4f ? Vector3.Cross(Vector3.up, perp / side) : Vector3.right;
                _bendModel.Step(dt, load, dir.y, side);
                var angles = _bendModel.Angles;
                for (int i = 0; i < _segments.Count && i < angles.Length; i++)
                    _segments[i].localRotation = Quaternion.AngleAxis(angles[i] * Mathf.Rad2Deg, axis);
            }

            // the rod lifts while charging and in the fight when lifting
            // charging: up to 60 degrees over half a second; the release swings it back fast (the cast's snap)
            float lift = State == RodState.Charging && _swing < 0f ? -CastLift * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_stateTime / 0.5f))
                : State == RodState.Fight && Input.GetMouseButton(1) ? -18f : 0f;
            float rate = State == RodState.Charging && _swing >= 0f ? 28f : 10f;
            _rodRoot.localRotation = Quaternion.Slerp(_rodRoot.localRotation, RestTurn(lift), 1f - Mathf.Exp(-dt * rate));

            // the crank turns with the line wound in: the rotor lays 2 pi r of line a turn, 5.2 turns to one of the crank
            bool reeling = (State == RodState.Fight || State == RodState.Waiting) && (Input.GetMouseButton(0) || SmokeReel) && !Blocked;
            Reeling = reeling;
            if (reeling)
                SetCrank(_crankAngle + dt * CrankTurns * 2f * Mathf.PI);     // the crank at the turns the line comes in by

            // the spool empties as the line goes out and fills as it is wound back
            float capacity = Mathf.Max(Reel?.LineCapacity ?? 100f, 10f);
            bool outside = State is not (RodState.Idle or RodState.Charging or RodState.Landed);
            SetSpoolFill(1f - (outside ? Mathf.Min(Fight?.LineOut ?? _lineLength, capacity) : 0f) / capacity);

            // the line inside the rod: from the spool's lip through the guides to the tip-top, bending with the blank
            if (_inRodLine != null && _inRodLine.gameObject.activeSelf)
                _inRodLine.SetPositions(_inRod, LinePath(_inRod));

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
            {
                _ropeLive = false;
                return;
            }
            UpdateLine(dt);
            _bobberRenderer.enabled = Method == FishingMethod.Float || State == RodState.Fight && Method == FishingMethod.Float;
        }

        private static Compat.Vector3 C(Vector3 v) => new(v.x, v.y, v.z);
        private static Vector3 U(Compat.Vector3 v) => new(v.X, v.Y, v.Z);

        /// <summary>
        /// FishingRod.UpdateLine: the line paid out is a rope (Core's LineRope: hangs in the air, lies on the surface film,
        /// sinks if it is fluorocarbon, never through the bottom); a fish keeps it taut from the tip to its mouth, a
        /// retrieve draws it straight with a light line's sag; tinted red as the load nears the breaking point.
        /// </summary>
        private void UpdateLine(float dt)
        {
            var a = _tip.position;
            var b = _bobber.position + Vector3.up * (Method == FishingMethod.Float ? 0.09f : 0f);
            bool drawing = Reeling && State is RodState.Waiting or RodState.Nibble or RodState.Bite && Slack < 0.08f;
            _drawTight = Mathf.MoveTowards(_drawTight, drawing ? 1f : 0f, dt * (drawing ? 6f : 1.5f));
            float ratio = State == RodState.Fight && Fight != null ? Mathf.Clamp01(Fight.Tension / Mathf.Max(EffectiveLimit, 0.1f)) : 0f;
            if (State == RodState.Fight)
                _lineLength = Vector3.Distance(a, b) * (1f + 0.004f * (1f - ratio));
            if (!_ropeLive)
            {
                _rope.Reset(C(a), C(b));
                _ropeLive = true;
            }
            _rope.Length = Mathf.Max(_lineLength, 0.05f);
            _rope.Wind = C(new Vector3(0.8f, 0f, 0.6f) * (Day != null ? Day.Wind * 3f : 0.6f));
            _rope.Step(dt, C(a), C(b));
            var pts = _rope.Positions;
            float dist = Vector3.Distance(a, b);
            float hold = 0f, sag = 0f;
            if (State == RodState.Fight)
            {
                hold = 0.92f + 0.08f * ratio;
                sag = dist * 0.008f * (1f - ratio) * (1f - ratio);
            }
            else if (_drawTight > 0f)
            {
                hold = 0.95f * _drawTight;
                sag = dist * 0.006f;
            }
            for (int i = 0; i < pts.Length; i++)
            {
                var p = U(pts[i]);
                if (hold > 0f)
                {
                    float t = i / (float)(pts.Length - 1);
                    var straight = Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t));
                    p = Vector3.Lerp(p, straight, hold);
                }
                _ropePts[i] = p;
            }
            var c = LineItem?.LineColor ?? new Compat.Color(0.9f, 0.9f, 0.86f);
            _line.Color = Color.Lerp(new Color(c.R, c.G, c.B), new Color(1f, 0.2f, 0.15f), Mathf.Clamp01((ratio - 0.5f) * 2f));
            _line.SetPositions(_ropePts, pts.Length);
        }
    }

    public static class SpeciesNames
    {
        /// <summary>The name shown to the angler (the species' Polish name).</summary>
        public static string DisplayNameOr(this FishSpecies sp) => sp.SpeciesName;
    }
}
