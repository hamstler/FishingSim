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
        private LineRenderer _inRodLine = null!;
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
                _procHandle.gameObject.SetActive(!ok);
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
                _rotor = _crank = _bail = _spoolLine = _lineExit = null;
                var model = ModelLibrary.Spawn(reelId, _reelModel);
                _modelledReel = false;
                if (model != null)
                {
                    foreach (var r in model.GetComponentsInChildren<Renderer>())
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var frames = ModelLibrary.Json(reelId) is { } json ? JsonUtility.FromJson<ReelFrame>(json) : null;
                    if (frames != null && frames.rotor is { Length: 3 } && frames.hub is { Length: 3 })
                    {
                        _modelledReel = true;
                        // the moving parts are stored in their own pivots' frames (meshy_rod_split.py): each goes to its pivot
                        _rotor = Pivot(model.transform, model.transform, "Rotor", M(frames.rotor));
                        _crank = Pivot(model.transform, model.transform, "Crank", M(frames.hub));
                        if (_rotor != null && frames.bail_pivot is { Length: 3 })
                            _bail = Pivot(model.transform, _rotor, "Bail", M(frames.bail_pivot));
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
                }
                _spoolFill = -1f;
                SetCrank(_crankAngle);
                SetBail(_bailOpen);
            }
        }

        [Serializable]
        private sealed class ReelFrame
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

        /// <summary>RodRig.SetSpoolFill: the wound line shrinks towards the spool's core as it goes out.</summary>
        private void SetSpoolFill(float fill)
        {
            fill = Mathf.Clamp01(fill);
            if (Mathf.Abs(fill - _spoolFill) < 0.002f)
                return;
            _spoolFill = fill;
            float k = Mathf.Lerp(SpoolCore, 1f, fill);
            _builtSpoolLine.localScale = new Vector3(k, 1f, k);
            if (_spoolLine != null)
                _spoolLine.localScale = new Vector3(k, 1f, k);
        }

        /// <summary>RodRig.LinePath: from the spool's lip through every guide's ring to the tip-top (world space).</summary>
        private int LinePath(Vector3[] into)
        {
            int n = 0;
            var exit = _modelledReel && _lineExit != null ? _lineExit : _builtLineExit;
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
            var go = new GameObject("Line");
            _line = go.AddComponent<LineRenderer>();
            _line.sharedMaterial = Materials.Unlit(new Color(0.85f, 0.85f, 0.8f));
            _line.widthMultiplier = 0.004f;
            _line.numCapVertices = 0;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            go.SetActive(false);
            var inRod = new GameObject("LineInRod");
            _inRodLine = inRod.AddComponent<LineRenderer>();
            _inRodLine.sharedMaterial = _line.sharedMaterial;
            _inRodLine.widthMultiplier = 0.0012f;
            _inRodLine.numCapVertices = 0;
            _inRodLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _inRodLine.receiveShadows = false;
            _line.positionCount = _ropePts.Length;
        }

        /// <summary>The rod shows only in play (the menu's flying camera carries it otherwise).</summary>
        public void ShowRod(bool show)
        {
            if (_rodRoot != null)
                _rodRoot.gameObject.SetActive(show);
            if (_inRodLine != null)
                _inRodLine.enabled = show;
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
                var c = line.LineColor;
                _line.startColor = _line.endColor = Color.white;
            }
            _rope.WaterY = 0f;
            _rope.BottomY = (x, z) => World.Lake.HeightAt(x, z);
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
                FishingMethod.Spinning => Reeling ? TackleWeightKg * 0.6f + 0.05f * Mathf.Pow(Mathf.Min((Reel?.ReelSpeed ?? 3f) * 0.35f, 2f), 2f) : 0f,
                FishingMethod.Bottom => TackleWeightKg * 0.6f + _bitePull * 0.9f,
                _ => Reeling ? 0.03f : 0f,
            };
            return 0.002f + pull * taut;
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
            _lineLength = 0.3f;
            _ropeLive = false;
            Twitch(-0.6f * power);            // the snap of the cast through the blank
            Set(RodState.Flying);
        }

        private void TickFlight(float dt)
        {
            _velocity += Vector3.down * 9.81f * dt;
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
            _lineLength = TipDistance() * 1.05f;
            _ropeLive = false;
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
                // the slack lying on the water is taken up first; only then does the end tackle come in
                float pull = TakeUpSlack(speed * dt);
                p += toAngler.normalized * pull;
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
            // the line never stretches: when the end tackle drifts off, the line follows
            _lineLength = Mathf.Max(_lineLength, TipDistance());

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
            Twitch(0.9f);
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
            float lift = State == RodState.Charging ? -25f * CastPower : State == RodState.Fight && Input.GetMouseButton(1) ? -18f : 0f;
            _rodRoot.localRotation = Quaternion.Slerp(_rodRoot.localRotation, Quaternion.Euler(-32f + lift, -6f, 0f), 1f - Mathf.Exp(-dt * 10f));

            // the crank turns with the line wound in: the rotor lays 2 pi r of line a turn, 5.2 turns to one of the crank
            bool reeling = (State == RodState.Fight || State == RodState.Waiting) && (Input.GetMouseButton(0) || SmokeReel) && !Blocked;
            Reeling = reeling;
            if (reeling)
            {
                float metres = (Reel?.ReelSpeed ?? 3f) * (State == RodState.Fight ? 0.5f : Method == FishingMethod.Spinning ? 0.35f : 0.22f);
                SetCrank(_crankAngle + dt * metres / (2f * Mathf.PI * 0.025f) / 5.2f * 2f * Mathf.PI);
            }

            // the spool empties as the line goes out and fills as it is wound back
            float capacity = Mathf.Max(Reel?.LineCapacity ?? 100f, 10f);
            bool outside = State is not (RodState.Idle or RodState.Charging or RodState.Landed);
            SetSpoolFill(1f - (outside ? Mathf.Min(_lineLength, capacity) : 0f) / capacity);

            // the line inside the rod: from the spool's lip through the guides to the tip-top, bending with the blank
            if (_inRodLine != null && _inRodLine.enabled)
            {
                int n = LinePath(_inRod);
                _inRodLine.positionCount = n;
                for (int i = 0; i < n; i++)
                    _inRodLine.SetPosition(i, _inRod[i]);
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
            _line.positionCount = pts.Length;
            _line.SetPositions(_ropePts);
            var c = LineItem?.LineColor ?? new Compat.Color(0.9f, 0.9f, 0.86f);
            _line.sharedMaterial.color = Color.Lerp(new Color(c.R, c.G, c.B), new Color(1f, 0.2f, 0.15f), Mathf.Clamp01((ratio - 0.5f) * 2f));
        }
    }

    public static class SpeciesNames
    {
        /// <summary>The name shown to the angler (the species' Polish name).</summary>
        public static string DisplayNameOr(this FishSpecies sp) => sp.SpeciesName;
    }
}
