using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishingSim.Compat;
using FishingSim.Core;
using FishingSim.Data;
using UnityEngine;
using Color = UnityEngine.Color;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using Mathf = UnityEngine.Mathf;

namespace FishingSim.Game
{
    /// <summary>
    /// The game: makes the world, the angler, the rod and the day, runs the menu, loading, play and pause, and draws the
    /// interface (immediate-mode GUI: menu, HUD, catch panel, shop, settings). Created by the bootstrap scene's only object.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public static string Version => Application.version;

        // the account first (a still picture: nothing of the world is drawn), then the fishery's loading screen (still
        // too), then play; another fishery is chosen in the game (Panel.Travel), like in modern fishing games
        private enum Screen { Login, Loading, Play }
        private enum Panel { None, Pause, Shop, Settings, Help, Keepnet, Records, Smokehouse, Cooking, Notices, Travel, Inventory }

        private Screen _screen = Screen.Login;
        private Panel _panel = Panel.None;
        private Profile _profile = null!;
        private WorldView _world = null!;
        private Player _player = null!;
        private Fishing _rod = null!;
        private Swimmers? _swimmers;
        private Gulls? _gulls;
        private Anglers? _anglers;
        private Mayflies? _mayflies;
        private DayCycle _day = null!;
        private Village _village = null!;
        private Village.Door? _nearDoor;
        private Camp _camp = null!;
        private People _people = null!;
        private Boat _boat = null!;
        private Sound _sound = null!;
        private string _hint = "";
        private System.Action? _interact;
        private Campfire? _fireAt;
        private readonly HashSet<string> _handedIn = new();
        private readonly List<LocationData> _locations = new();
        private int _loadedLocation = -1;
        private string _message = "";
        private float _messageUntil;
        private string _error = "";
        private int _shopTab, _shopFilter;
        private int _inventoryTab;
        private readonly TackleItem?[] _rigDraft = new TackleItem?[5];
        private string _shopSearch = "";
        private Vector2 _scroll;
        private Panel _settingsBack = Panel.None;
        private float _autosave;

        // the still screens: a camera that only clears (the world's camera is off), the fisheries' pictures
        private Camera _backdrop = null!;
        private readonly Dictionary<string, Texture2D?> _pictures = new();

        // the work done while the login screen shows (textures, models, the lake's fish), then the map's loading
        private bool _prepared;
        private float _prepareProgress;
        private string _prepareStep = "";
        private float _loadProgress;
        private string _loadStep = "";
        private int _loadingIndex;
        private string _tip = "";
        private int _warmedFor = -1;
        private bool _loading;
        private int _guessLocation;

        // the account logged in (null: the login screen; a test run plays as a guest without saving)
        private string? _user;
        private bool _registering;
        private string _loginName = "", _loginPass = "", _loginRepeat = "", _loginError = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<GameRoot>() == null)
                new GameObject("Game").AddComponent<GameRoot>();
        }

        private void Awake()
        {
            LoadLog.Mark("start");
            DataStore.Root = Path.Combine(Application.streamingAssetsPath, "data");
            bool noSave = Environment.GetEnvironmentVariable("FISHSIM_NO_SAVE") == "1";
            Profile.SavePath = null;            // a profile is saved once an account logs in (Accounts.ProfilePath)
            Application.logMessageReceived += OnLog;
            Settings.Load();
            TexCache.Tidy();
            _profile = Profile.Load();
            foreach (var path in Lake.LocationPaths)
                if (DataStore.Load<LocationData>(path) is { } loc)
                    _locations.Add(loc);
            if (_locations.Count == 0)
                _error = "Brak danych gry (StreamingAssets/data). Zainstaluj grę ponownie.";

            _day = new GameObject("Day").AddComponent<DayCycle>();
            _day.Hour = _profile.Hour;
            _world = new GameObject("World").AddComponent<WorldView>();
            _village = new GameObject("Village").AddComponent<Village>();
            _camp = new GameObject("Camp").AddComponent<Camp>();
            _people = new GameObject("People").AddComponent<People>();
            _boat = new GameObject("Boat").AddComponent<Boat>();
            var player = new GameObject("Angler");
            _player = player.AddComponent<Player>();
            _player.World = _world;
            _player.Sensitivity = Settings.Mouse;
            _rod = player.AddComponent<Fishing>();
            _rod.Player = _player;
            _rod.World = _world;
            _rod.Day = _day;
            _rod.Profile = _profile;
            _rod.Said += Say;
            _rod.Landed += () => SetCursor(false);
            _boat.World = _world;
            _boat.Day = _day;
            _sound = _player.Camera.gameObject.AddComponent<Sound>();
            _sound.Player = _player;
            _sound.Rod = _rod;
            _sound.Day = _day;
            _sound.World = _world;
            _rod.Cue += (kind, gain) => _sound.Play(kind, gain);
            _rod.BiteBonus = () => _profile.ActiveMeal?.BiteBonus ?? 0f;
            StartCoroutine(MakeArms());
            _player.Step += wood => _sound.Play(wood ? Core.Audio.OneShot.StepWood : Core.Audio.OneShot.StepGrass, 0.7f);
            Settings.Apply(_player.Camera);
            _player.Frozen = true;
            _rod.Blocked = true;

            // the still screens draw no world: its camera is off and this one only clears the screen
            _backdrop = new GameObject("Backdrop").AddComponent<Camera>();
            _backdrop.clearFlags = CameraClearFlags.SolidColor;
            _backdrop.backgroundColor = new Color(0.03f, 0.035f, 0.04f);
            _backdrop.cullingMask = 0;
            _backdrop.depth = -100f;
            _backdrop.allowHDR = false;
            _backdrop.allowMSAA = false;
            ShowWorld(false);
            SetCursor(false);

            // the login screen first (an account's last fishery is guessed, so its pictures and models are made while
            // the name and password are typed); the smoke test and runs without saving play as a guest
            var guess = GuessProfile();
            _guessLocation = guess.Location;
            if (noSave || Smoke.Enabled)
            {
                _user = "gość";
                guess = _profile;
            }
            else
            {
                _screen = Screen.Login;
                _loginName = Accounts.LastUser;
                _registering = Accounts.Count == 0;
            }
            if (_locations.Count > 0)
                StartCoroutine(Prepare(guess));
            if (_user != null && _locations.Count > 0)
                StartCoroutine(LoadMap(Mathf.Clamp(_profile.Location, 0, _locations.Count - 1), true));
            if (Smoke.Enabled)
            {
                var smoke = gameObject.AddComponent<Smoke>();
                smoke.Ready = () => _world.Ready && _screen == Screen.Play && !_loading;
                smoke.EnterPlay = EnterPlay;
                smoke.InventoryForShot = tab =>
                {
                    if (tab < 0)
                        ClosePanel();
                    else
                    {
                        OpenInventory();
                        SelectInventoryTab(tab == 0 ? -1 : tab);
                    }
                };
                smoke.LoadMap = index => StartCoroutine(LoadMap(index, true));
                smoke.Locations = _locations.Count;
                smoke.Rod = _rod;
                smoke.Player = _player;
                smoke.Day = _day;
            }
        }

        /// <summary>The last account's profile (its fishery and tackle), read without logging in; the blank one without it.</summary>
        private Profile GuessProfile()
        {
            string last = Accounts.LastUser;
            if (last.Length == 0 || !File.Exists(Accounts.ProfilePath(last)))
                return _profile;
            try
            {
                Profile.SavePath = Accounts.ProfilePath(last);
                return Profile.Load();
            }
            catch (Exception)
            {
                return _profile;
            }
            finally
            {
                Profile.SavePath = null;
            }
        }

        /// <summary>The models a fishery needs: its village and people, the camp, the other anglers, its fish.</summary>
        private List<string> ModelsFor(int index)
        {
            var ids = new List<string>();
            var loc = _locations[Mathf.Clamp(index, 0, _locations.Count - 1)];
            bool village = !(Smoke.Enabled && Smoke.HandsOnly);
            if (village)
            {
                ids.AddRange(Village.ModelIds(_world.Lake != null && _world.Lake.Location == loc ? _world.Lake : new Lake(loc)));
                foreach (var id in ModelLibrary.IdsIn("characters"))
                    if (id != GripArms.ModelId && id != "characters/player_angler")
                        ids.Add(id);
                ids.Add("stone_fire_pit");
                ids.AddRange(Anglers.ModelIds);
            }
            foreach (var sp in Tackle.AllSpecies)
                if (loc.AbundanceOf(sp.SpeciesName) > 0f)
                    ids.Add("fish/" + Path.GetFileNameWithoutExtension(sp.ResourcePath));
            return ids;
        }

        /// <summary>
        /// The work done while the login screen shows (the user's idea: the game loads its graphics meanwhile): the
        /// painted and photographed textures (from TexCache after the first start), the tackle in hand, then the guessed
        /// fishery's models and fish. The map's loading screen waits only for what is not ready yet.
        /// </summary>
        private IEnumerator Prepare(Profile guess)
        {
            LoadLog.Begin("prepare");
            _prepareStep = "Przygotowanie tekstur";
            _ = ProcTextures.NoiseField;
            yield return null;
            _ = ProcTextures.CloudNoise;
            yield return null;
            _ = ProcTextures.Caustics;
            _ = ProcTextures.Ripples;
            _ = ProcTextures.Foam;
            yield return null;
            _ = LeafPainter.Leaves;
            yield return null;
            _ = LeafPainter.Needles;
            _prepareProgress = 0.1f;
            yield return null;
            _prepareStep = "Przygotowanie terenu";
            var ground = GroundLayers.Prepare(GroundLayers.SizeForQuality);
            while (ground.MoveNext())
            {
                _prepareProgress = 0.1f + 0.4f * GroundLayers.Progress;
                yield return ground.Current;
            }
            _prepareStep = "Przygotowanie materiałów";
            yield return Photos.Prewarm("weathered_brown_planks", "white_planks_clean");
            _prepareProgress = 0.55f;
            // the tackle in hand first, then the fishery's models (glTFast keeps each frame within its time budget)
            _prepareStep = "Wczytywanie modeli";
            var ids = new List<string> { WantedRod(guess), WantedReel(guess) };
            ids.AddRange(ModelsFor(guess.Location));
            foreach (var id in ids)
                ModelLibrary.Start(id);
            while (true)
            {
                int done = ModelLibrary.SettledCount(ids);
                _prepareProgress = 0.55f + 0.45f * done / Mathf.Max(ids.Count, 1);
                if (done >= ids.Count)
                    break;
                yield return null;
            }
            _rod.ApplyTackle();
            _prepareProgress = 1f;
            _prepareStep = "";
            _prepared = true;
            LoadLog.End("prepare");
        }

        private static string WantedRod(Profile p) => Tackle.Rod(p.Rod) is { } r && r.Model.Length > 0 ? r.Model : "rods/float_ostoja";
        private static string WantedReel(Profile p) => Tackle.Reel(p.Reel) is { } r && r.Model.Length > 0 ? r.Model : "reels/small_hokuto";

        private IEnumerator MakeArms()
        {
            yield return null;   // the rod is built in its Start
            if (!GripArms.ShowAngler)
            {
                // the angler is hidden for now (the user): only the rod's framing, no arms' model loaded at all
                GripArms.FrameOnly(_player.Camera, _rod);
                yield break;
            }
            yield return ModelLibrary.Preload(new[] { GripArms.ModelId, "characters/player_angler" });
            try
            {
                // the arms posed by the user in Blender when they are there, else the solved MakeHuman arms
                if (GripArms.Create(_player.Camera, _rod) == null)
                    AnglerArms.Create(_player.Camera, _rod);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            _tacklePreview?.Dispose();
        }

        /// <summary>Errors go to logs/errors.txt in the game's user folder (beside Unity's own Player.log).</summary>
        private static void OnLog(string text, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error)
                return;
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "errors.txt"), $"{DateTime.Now:u} {type}: {text}\n{stack}\n");
            }
            catch (Exception)
            {
                // nothing to do
            }
        }

        private static readonly string[] Tips =
        {
            "Za mocno dokręcony hamulec zrywa żyłkę, za słaby pozwala rybie odejść z całą szpulą.",
            "Ryby przy brzegu płoszą kroki: podchodź spokojnie i nie biegaj po pomoście.",
            "Duże ryby rzadko biorą na małe haczyki, a małe nie połkną dużych.",
            "Spadające ciśnienie i pochmurny dzień ożywiają żerowanie.",
            "O świcie i o zmierzchu ryby podchodzą płyciej, w południe schodzą głębiej.",
            "Kręgi na wodzie zdradzają żerujące ryby: rzuć w ich pobliże.",
            "Garść zanęty przy spławiku (V) przyciąga ryby do łowiska.",
            "Zbyt długi hol męczy rybę, a ryba trzymana w powietrzu traci kondycję.",
            "Przynęta w wodzie z czasem traci świeżość: zmieniaj ją po kilku rzutach.",
            "W zielsku i wśród zatopionych gałęzi ryby chętnie stoją, ale łatwo tam o zaczep.",
            "Skręcona żyłka plącze się przy rzucie: krętlik ogranicza skręcanie przy spinningu.",
            "Tępy haczyk gorzej zacina: osełka (B) przywraca mu ostrość.",
        };

        /// <summary>
        /// A fishery's loading screen (a still picture with its name, a tip and the progress): what the login screen
        /// did not prepare yet, the world, the village and its people, then one render in each direction so the GPU
        /// makes the shaders and takes the meshes and textures before play (no stutter in the first seconds).
        /// </summary>
        private IEnumerator LoadMap(int index, bool play)
        {
            if (_loading)
                yield break;
            _loading = true;
            _screen = Screen.Loading;
            _panel = Panel.None;
            _loadingIndex = index;
            _loadProgress = 0f;
            _loadStep = "Przygotowanie";
            _tip = Tips[UnityEngine.Random.Range(0, Tips.Length)];
            ShowWorld(false);
            SetCursor(false);
            _rod.Reset();
            yield return null;   // the loading screen shows first
            string name = _locations[index].DisplayName;
            LoadLog.Begin($"map {name}");
            bool fresh = _loadedLocation != index;
            if (fresh)
            {
                // the textures (usually ready from the login screen) and this fishery's models
                _loadStep = "Wczytywanie modeli";
                var ids = new List<string> { _rod.WantedRodModel, _rod.WantedReelModel };
                ids.AddRange(ModelsFor(index));
                foreach (var id in ids)
                    ModelLibrary.Start(id);
                var ground = GroundLayers.Prepare(GroundLayers.SizeForQuality);
                while (ground.MoveNext())
                {
                    _loadProgress = 0.15f * GroundLayers.Progress;
                    yield return ground.Current;
                }
                while (true)
                {
                    int done = ModelLibrary.SettledCount(ids);
                    _loadProgress = 0.15f + 0.25f * done / Mathf.Max(ids.Count, 1);
                    if (done >= ids.Count)
                        break;
                    yield return null;
                }
                LoadLog.Mark("models ready");
                _rod.ApplyTackle();
                _loadStep = "Budowanie łowiska";
                var build = _world.Build(_locations[index]);
                while (true)
                {
                    object? step;
                    try
                    {
                        if (!build.MoveNext())
                            break;
                        step = build.Current;
                    }
                    catch (Exception e)
                    {
                        _error = "Nie udało się zbudować mapy: " + e.Message;
                        Debug.LogException(e);
                        _loading = false;
                        yield break;
                    }
                    _loadProgress = 0.4f + 0.35f * _world.Progress;
                    yield return step;
                }
                LoadLog.Mark("world built");
                // the village's buildings and its people (not for the smoke test's hands-only run: only the rod matters)
                _loadStep = "Wioska i ludzie";
                _loadProgress = 0.78f;
                yield return null;
                bool village = !(Smoke.Enabled && Smoke.HandsOnly);
                LoadLog.Begin("village");
                try
                {
                    if (village)
                        _village.Build(_world.Lake);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);   // the map stays playable without its village
                }
                LoadLog.End("village");
                _loadProgress = 0.82f;
                yield return null;
                LoadLog.Begin("camp and people");
                try
                {
                    if (village)
                    {
                        _camp.Build(_world.Lake);
                        _people.Build(_world.Lake, _village, _camp);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                LoadLog.End("camp and people");
                _player.Solids.Clear();
                _player.Solids.AddRange(_village.Solids);
                _player.Solids.AddRange(_world.Solids);
                try
                {
                    _anglers ??= new GameObject("Anglers").AddComponent<Anglers>();
                    _anglers.Build(_world.Lake);
                    _player.Solids.AddRange(_anglers.Solids);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                _loadProgress = 0.86f;
                yield return null;
                _loadedLocation = index;
                _warmedFor = -1;
                _day.Water = _world.WaterMaterial;
                _day.BuildEffects();
                _rod.ClearPlace();
                if (_swimmers == null)
                {
                    _swimmers = new GameObject("Swimmers").AddComponent<Swimmers>();
                    _swimmers.Risen += _rod.NoteRise;
                }
                _swimmers.Watcher = _player.transform;
                _swimmers.Day = _day;
                _mayflies ??= new GameObject("Mayflies").AddComponent<Mayflies>();
                _mayflies.Day = _day;
                _mayflies.Build(_world.Lake);
                if (_gulls == null)
                {
                    _gulls = new GameObject("Gulls").AddComponent<Gulls>();
                    _gulls.Day = _day;
                }
                _swimmers.Clarity = _locations[index].WaterClarity;
                _swimmers.Populate(_world.Lake, Tackle.AllSpecies);
                GC.Collect();
            }
            _profile.Location = index;
            _player.Riding = null;
            _boat.ResetToMooring();
            _player.Place(new Vector3(0f, WorldView.PierDeck, 4.5f), 180f);
            _day.Apply();
            if (_warmedFor != index)
            {
                _loadStep = "Przygotowanie shaderów";
                yield return WarmUp(index);
                _warmedFor = index;
            }
            _loadProgress = 1f;
            LoadLog.End($"map {name}");
            _loading = false;
            if (play)
                EnterPlay();
            else
            {
                _screen = Screen.Login;
                SetCursor(false);
            }
        }

        /// <summary>
        /// Renders the fishery once in eight directions (into a small picture nobody sees) with everything that may
        /// show in play switched on: rain, mist, one of each of the lake's fish beside the angler. The GPU then makes
        /// the shader variants, the shadows' and the post effects' too, and takes the meshes and pictures now instead
        /// of in the first seconds of play.
        /// </summary>
        private IEnumerator WarmUp(int index)
        {
            LoadLog.Begin("warm-up");
            var cam = _player.Camera;
            var rt = new RenderTexture(Mathf.Max(320, UnityEngine.Screen.width / 2), Mathf.Max(180, UnityEngine.Screen.height / 2), 24, RenderTextureFormat.DefaultHDR)
            {
                name = "WarmUp",
                antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing),
            };
            var oldTarget = cam.targetTexture;
            var oldTurn = cam.transform.localRotation;
            var oldPlayerTurn = _player.transform.rotation;
            _rod.ShowRod(true);
            _day.ShowEffects(true);
            var shown = new List<GameObject>();
            var loc = _locations[index];
            var eye = cam.transform.position;
            var ahead = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            int n = 0;
            foreach (var sp in Tackle.AllSpecies)
            {
                if (loc.AbundanceOf(sp.SpeciesName) <= 0f)
                    continue;
                if (ModelLibrary.Fish(Path.GetFileNameWithoutExtension(sp.ResourcePath)) is not { } fish)
                    continue;
                fish.transform.position = eye + ahead * (2.2f + 0.25f * (n % 4)) + Vector3.Cross(Vector3.up, ahead) * (0.3f * (n % 5) - 0.6f) - Vector3.up * 0.35f;
                fish.transform.localScale = Vector3.one * 0.35f;
                shown.Add(fish);
                n++;
            }
            cam.targetTexture = rt;
            const int views = 8;
            for (int i = 0; i < views; i++)
            {
                float pitch = i < 6 ? 8f : i == 6 ? 40f : -25f;
                _player.transform.rotation = Quaternion.Euler(0f, oldPlayerTurn.eulerAngles.y + i * 60f, 0f);
                cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                try
                {
                    cam.Render();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                _loadProgress = 0.9f + 0.1f * (i + 1) / views;
                yield return null;
            }
            cam.targetTexture = oldTarget;
            cam.transform.localRotation = oldTurn;
            _player.transform.rotation = oldPlayerTurn;
            foreach (var go in shown)
                Destroy(go);
            _day.ShowEffects(false);
            rt.Release();
            Destroy(rt);
            LoadLog.End("warm-up");
        }

        private void EnterPlay()
        {
            _screen = Screen.Play;
            _panel = Panel.None;
            ShowWorld(true);
            SetCursor(true);
            if (_profile.ClaimFreeKit())
                Say("Sklep dał ci darmowy zestaw startowy.");
        }

        /// <summary>The world's camera on (play) or off (the still login and loading screens: only the backdrop clears).</summary>
        private void ShowWorld(bool on)
        {
            _player.Camera.enabled = on;
            _backdrop.enabled = !on;
            AudioListener.volume = on ? 1f : 0f;
        }

        private void SetCursor(bool captured)
        {
            _player.enabled = _screen == Screen.Play;
            _rod.ShowRod(_screen == Screen.Play);
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
            _player.Frozen = !captured;
            _rod.Blocked = !captured;
        }

        private void Say(string text)
        {
            _message = text;
            _messageUntil = Time.unscaledTime + 4.5f;
        }

        private void Update()
        {
            if (_screen != Screen.Play)
                return;
            _profile.Hour = _day.Hour;
            _profile.Clock += _day.LastStepHours;
            _autosave += Time.unscaledDeltaTime;
            if (_autosave > 60f)
            {
                _autosave = 0f;
                _profile.Save();
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_rod.State == RodState.Landed)
                    return;
                if (_panel == Panel.None)
                    OpenPanel(Panel.Pause);
                else
                    ClosePanel();
            }
            if (Input.GetKeyDown(KeyCode.I))
            {
                if (_panel == Panel.Inventory)
                    ClosePanel();
                else if (_panel == Panel.None && _rod.State != RodState.Landed)
                    OpenInventory();
            }
            _village.SetNight(_day.Night);
            _world.SetNight(_day.Night);
            Campfire.ClockNow = _profile.Clock;
            _camp.Regrow(_profile.Clock);
            _people.Tick(Time.deltaTime, _day.Hour);
            _boat.Tick(Time.deltaTime, _panel == Panel.None && _rod.State == RodState.Idle);
            FindInteraction();
            if (_panel == Panel.None && _interact != null && Input.GetKeyDown(KeyCode.E))
                _interact();
            if (_panel == Panel.None && Boat.Aboard && Input.GetKeyDown(KeyCode.X))
                Say(_boat.ToggleAnchor());
            if (_panel == Panel.None && _rod.State == RodState.Idle && Cursor.lockState == CursorLockMode.Locked)
                UseTools();
            if (_panel == Panel.None && _rod.State != RodState.Landed)
            {
                if (Input.GetKeyDown(KeyCode.T) && (_rod.State == RodState.Idle))
                    OpenPanel(Panel.Shop);
                else if (Input.GetKeyDown(KeyCode.T))
                    Say("Najpierw wyjmij zestaw z wody.");
                if (Input.GetKeyDown(KeyCode.K))
                    OpenPanel(Panel.Keepnet);
                if (Input.GetKeyDown(KeyCode.F1))
                    OpenPanel(Panel.Help);
                if (Input.GetKeyDown(KeyCode.J))
                    OpenPanel(Panel.Records);
                if (Input.GetKeyDown(KeyCode.M))
                    OpenPanel(Panel.Travel);
                if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
                    SetCursor(true);
            }
            if (_rod.State == RodState.Landed)
            {
                if (Input.GetKeyDown(KeyCode.E) && CanKeep(out _))
                    Decide(true);
                if (Input.GetKeyDown(KeyCode.R))
                    Decide(false);
            }
            // the cursor shows only with a panel open or the fish landed; time runs on (the lake does not pause)
            _day.TimeScale = 1f;
        }

        /// <summary>What E does here: the nearest door, the boat, a campfire, the notice board or a forest patch.</summary>
        private void FindInteraction()
        {
            _hint = "";
            _interact = null;
            _nearDoor = null;
            if (_screen != Screen.Play || _rod.State != RodState.Idle)
                return;
            var p = _player.transform.position;
            if (Boat.Aboard)
            {
                _hint = $"E – wysiądź · X – {(_boat.Anchored ? "podnieś kotwicę" : "rzuć kotwicę")} · W/S/A/D – wiosłuj";
                _interact = () =>
                {
                    if (_boat.StepOut(out var at, out string why))
                    {
                        _player.Riding = null;
                        _player.Place(at, _boat.Yaw);
                    }
                    else
                        Say(why);
                };
                return;
            }
            float best = 3.5f;
            foreach (var d in _village.Doors)
            {
                float dist = Vector3.Distance(d.At, p);
                if (dist < best)
                {
                    best = dist;
                    var door = d;
                    _nearDoor = d;
                    _hint = $"E – {d.Name}";
                    _interact = () => UseDoor(door);
                }
            }
            if (Vector3.Distance(_boat.transform.position, p) < Mathf.Min(best, 3.4f))
            {
                best = Vector3.Distance(_boat.transform.position, p);
                _hint = "E – wsiądź do łódki";
                _interact = () =>
                {
                    _boat.Board();
                    _player.Riding = _boat;
                    Say("W łódce: W/S wiosłuj, A/D skręcaj, X kotwica, E wysiądź.");
                };
            }
            foreach (var fire in _camp.Fires)
            {
                float dist = Vector3.Distance(fire.transform.position, p);
                if (dist < Mathf.Min(best, 2.6f))
                {
                    best = dist;
                    var f = fire;
                    _hint = f.Lit ? $"E – gotuj ({f.Name})" : $"E – rozpal ({f.Name})";
                    _interact = () =>
                    {
                        if (!f.Lit)
                        {
                            f.BurnUntil = _profile.Clock + Campfire.BurnHours;
                            _sound.Play(Core.Audio.OneShot.Ignite);
                            Say("Ogień płonie. E – gotowanie.");
                        }
                        else
                        {
                            _fireAt = f;
                            OpenPanel(Panel.Cooking);
                        }
                    };
                }
            }
            if (Vector3.Distance(_camp.Board, p) < Mathf.Min(best, 2.6f))
            {
                best = Vector3.Distance(_camp.Board, p);
                _hint = "E – tablica ogłoszeń";
                _interact = () => OpenPanel(Panel.Notices);
            }
            if (_camp.NearPatch(p, _profile.Clock) is { } patch && Vector3.Distance(patch.At, p) < best)
            {
                _hint = $"E – zbierz ({Core.Camp.Foraging.NameOf(Core.Camp.Foraging.IdOf(patch.Kind))})";
                _interact = () => Say(_camp.Pick(patch, _profile, _day.Weather == Weather.Rain));
            }
        }

        /// <summary>G – kopanie łopatą, H – siatka na owady, L – podrywka (with the accessory bought).</summary>
        private void UseTools()
        {
            var p = _player.transform.position;
            var ahead = p + Vector3.ProjectOnPlane(_player.Forward, Vector3.up).normalized * 2.5f;
            if (Input.GetKeyDown(KeyCode.G))
            {
                if (!_profile.HasAccessory("lopata")) Say("Potrzebna łopata (sklep, akcesoria).");
                else if (Boat.Aboard) Say("Z łódki nie pokopiesz.");
                else
                {
                    _sound.Play(Core.Audio.OneShot.Dig);
                    Say(_camp.Dig(_world.Lake, p, _day.Hour, _day.Weather, _profile));
                }
            }
            if (Input.GetKeyDown(KeyCode.H))
            {
                if (!_profile.HasAccessory("siatka_na_owady")) Say("Potrzebna siatka na owady (sklep, akcesoria).");
                else
                {
                    _sound.Play(Core.Audio.OneShot.Swish);
                    Say(_camp.Sweep(_world.Lake, p, _day.Hour, _day.Weather, _profile));
                }
            }
            if (Input.GetKeyDown(KeyCode.L))
            {
                if (!_profile.HasAccessory("podrywka")) Say("Potrzebna podrywka (sklep, akcesoria).");
                else
                {
                    _sound.Play(Core.Audio.OneShot.SmallSplash, 0.6f);
                    Say(_camp.LiftNet(_world.Lake, ahead, _day.Hour, _day.Weather, _profile));
                }
            }
        }

        private void UseDoor(Village.Door door)
        {
            switch (door.Kind)
            {
                case BuildingKind.Tackle:
                case BuildingKind.General:
                case BuildingKind.Bait:
                    OpenPanel(Panel.Shop);
                    break;
                case BuildingKind.FishBuyer:
                case BuildingKind.Market:
                    OpenPanel(Panel.Keepnet);
                    break;
                case BuildingKind.Office:
                    OpenPanel(Panel.Records);
                    break;
                case BuildingKind.Smokehouse:
                    OpenPanel(Panel.Smokehouse);
                    break;
                default:
                    Say(door.Name);
                    break;
            }
        }

        private void OpenPanel(Panel p)
        {
            if (p != _panel)
                ReleaseTacklePreview();
            _panel = p;
            _scroll = Vector2.zero;
            SetCursor(false);
        }

        private void ClosePanel()
        {
            ReleaseTacklePreview();
            _panel = Panel.None;
            SetCursor(true);
            _rod.ApplyTackle();
            _profile.Save();
        }

        private void ReleaseTacklePreview()
        {
            _tacklePreview?.Dispose();
            _tacklePreview = null;
        }

        private bool CanKeep(out string why)
        {
            why = "";
            var sp = _rod.Species;
            if (sp == null)
                return false;
            if (_rod.Weight < sp.LegalWeight)
            {
                why = $"Niewymiarowa (poniżej {sp.LegalWeight:0.00} kg): wypuść ją.";
                return false;
            }
            return true;
        }

        private void Decide(bool keep)
        {
            var sp = _rod.Species;
            if (sp == null)
                return;
            bool record = !_profile.Records.TryGetValue(sp.SpeciesName, out float best) || _rod.Weight > best;
            int value = Progression.FishValue(sp, _rod.Weight);
            int level = _profile.Level;
            _rod.Decide(keep);
            Say(keep ? $"{sp.SpeciesName} {_rod.Weight:0.000} kg do siatki ({value} zł)." : $"{sp.SpeciesName} wypuszczona.");
            if (record)
                Say($"Nowy rekord: {sp.SpeciesName}!");
            if (_profile.Level > level)
                Say($"Awans na poziom {_profile.Level}!");
            SetCursor(true);
        }

        private void OnApplicationQuit() => _profile.Save();

        // ================================================================== GUI
        // The look of a modern fishing game rather than a web page: one condensed sans-serif (Bahnschrift on Windows),
        // flat dark panels with square corners and a hairline edge, white text, one warm accent for what is chosen or
        // pressed, status colours only where they mean something; the HUD is a few bars and numbers, no cards.
        private GUIStyle? _panelStyle, _title, _text, _small, _button, _accentButton, _bar, _barFill, _chip;
        private GUIStyle? _field, _card, _cardHot, _tab, _tabOn, _label, _logo, _danger;
        private Texture2D? _dim, _fadeUp, _fadeRight;
        private static Texture2D White => Texture2D.whiteTexture;
        private float _scale = 1f, _styledFor = -1f;

        private static readonly Color Ink = new(0.94f, 0.95f, 0.96f);
        private static readonly Color Muted = new(0.66f, 0.7f, 0.74f);
        private static readonly Color Faint = new(0.68f, 0.72f, 0.76f);
        private static readonly Color Accent = new(0.93f, 0.78f, 0.4f);
        private static readonly Color GoodCol = new(0.46f, 0.82f, 0.46f), WarnCol = new(0.96f, 0.72f, 0.26f), BadCol = new(0.94f, 0.3f, 0.24f);

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        /// <summary>A flat box for 9-slice styles: the fill with a one-pixel edge (square corners).</summary>
        private static Texture2D Flat(Color fill, Color edge)
        {
            const int size = 8;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = edge.a > 0f && (x == 0 || y == 0 || x == size - 1 || y == size - 1) ? edge : fill;
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>A fill with a bar of <paramref name="mark"/> along one side (bottom: tabs; left: the chosen row).</summary>
        private static Texture2D Marked(Color fill, Color mark, bool left)
        {
            const int size = 8;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = left ? (x < 3 ? mark : fill) : (y < 2 ? mark : fill);
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>A gradient of black from transparent to <paramref name="peak"/> (vertical: up from the bottom; else to the right).</summary>
        private static Texture2D Fade(float peak, bool vertical)
        {
            const int n = 256;
            var t = new Texture2D(vertical ? 1 : n, vertical ? n : 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < n; i++)
            {
                float f = i / (n - 1f);
                float a = peak * (vertical ? Mathf.SmoothStep(0f, 1f, 1f - f) : Mathf.SmoothStep(0f, 1f, 1f - f));
                if (vertical)
                    t.SetPixel(0, i, new Color(0.01f, 0.012f, 0.015f, a));
                else
                    t.SetPixel(i, 0, new Color(0.01f, 0.012f, 0.015f, a));
            }
            t.Apply();
            return t;
        }

        private static Font? _font, _fontBold;

        /// <summary>The first of these fonts installed on this computer (none shipped), else Unity's own.</summary>
        private static Font PickFont(params string[] names)
        {
            try
            {
                var installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
                foreach (var n in names)
                    if (installed.Contains(n))
                        return Font.CreateDynamicFontFromOSFont(n, 18);
            }
            catch (Exception)
            {
                // the built-in font below
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void Styles()
        {
            _scale = Mathf.Clamp(UnityEngine.Screen.height / 1080f, 0.7f, 2f);
            if (_panelStyle != null && Mathf.Approximately(_styledFor, _scale))
                return;
            _styledFor = _scale;
            _font ??= PickFont("Bahnschrift", "Segoe UI", "DejaVu Sans", "Liberation Sans", "Arial");
            _fontBold ??= PickFont("Bahnschrift SemiBold", "Bahnschrift", "Segoe UI Semibold", "DejaVu Sans", "Arial");
            int F(int size) => Mathf.RoundToInt(size * _scale);
            RectOffset B(int r) => new(r, r, r, r);
            var hair = new Color(1f, 1f, 1f, 0.09f);
            _dim = Tex(new Color(0.01f, 0.012f, 0.015f, 0.62f));
            _fadeUp = Fade(0.9f, true);
            _fadeRight = Fade(0.88f, false);
            _panelStyle = new GUIStyle
            {
                normal = { background = InventorySkin(new Color(.035f, .065f, .073f, .98f),
                    new Color(.09f, .155f, .16f, .99f), new Color(.22f, .35f, .35f)), textColor = Ink },
                border = B(15),
                padding = new RectOffset(F(22), F(22), F(18), F(18)),
                font = _font,
                fontSize = F(19),
                alignment = TextAnchor.UpperLeft,
            };
            _logo = new GUIStyle { font = _fontBold, fontSize = F(50), normal = { textColor = Ink }, richText = true };
            _title = new GUIStyle { font = _fontBold, fontSize = F(27), normal = { textColor = Ink }, wordWrap = true, richText = true };
            _text = new GUIStyle { font = _font, fontSize = F(19), normal = { textColor = Ink }, wordWrap = true, richText = true };
            _small = new GUIStyle(_text) { fontSize = F(16), normal = { textColor = Muted } };
            _label = new GUIStyle(_text) { font = _fontBold, fontSize = F(13), normal = { textColor = Faint }, wordWrap = false };
            _button = new GUIStyle
            {
                font = _font,
                fontSize = F(18),
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                normal = { background = Flat(new Color(0.12f, 0.13f, 0.145f, 0.96f), hair), textColor = Ink },
                hover = { background = Flat(new Color(0.18f, 0.19f, 0.21f, 1f), new Color(1f, 1f, 1f, 0.16f)), textColor = Color.white },
                active = { background = Flat(new Color(0.08f, 0.085f, 0.095f, 1f), Accent), textColor = Accent },
                onNormal = { background = Flat(new Color(0.12f, 0.13f, 0.145f, 0.96f), Accent), textColor = Accent },
                border = B(2),
                padding = new RectOffset(F(14), F(14), F(8), F(8)),
            };
            _accentButton = new GUIStyle(_button)
            {
                font = _fontBold,
                normal = { background = Flat(Accent, new Color(1f, 0.9f, 0.62f, 1f)), textColor = new Color(0.07f, 0.06f, 0.04f) },
                hover = { background = Flat(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.94f, 0.75f)), textColor = Color.black },
                active = { background = Flat(new Color(0.8f, 0.65f, 0.3f), Accent), textColor = Color.black },
            };
            _danger = new GUIStyle(_button) { normal = { background = Flat(new Color(0.28f, 0.09f, 0.08f, 0.96f), hair), textColor = new Color(1f, 0.86f, 0.83f) } };
            // tabs and chips: text with an accent bar under the chosen one
            _tab = new GUIStyle(_button)
            {
                font = _fontBold,
                fontSize = F(15),
                normal = { background = Flat(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0f)), textColor = Muted },
                hover = { background = Marked(new Color(1f, 1f, 1f, 0.04f), new Color(1f, 1f, 1f, 0.25f), false), textColor = Ink },
                active = { background = Marked(new Color(1f, 1f, 1f, 0.06f), Accent, false), textColor = Accent },
                border = new RectOffset(0, 0, 0, 3),
                padding = new RectOffset(F(10), F(10), F(6), F(8)),
            };
            _tabOn = new GUIStyle(_tab) { normal = { background = Marked(new Color(1f, 1f, 1f, 0.05f), Accent, false), textColor = Ink } };
            _chip = _tab;
            _card = new GUIStyle(_button)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(F(18), F(18), F(10), F(10)),
                wordWrap = true,
                normal = { background = Flat(new Color(0.09f, 0.1f, 0.11f, 0.94f), hair), textColor = Ink },
                hover = { background = Marked(new Color(0.14f, 0.15f, 0.165f, 0.98f), new Color(1f, 1f, 1f, 0.3f), true), textColor = Color.white },
                border = new RectOffset(4, 1, 1, 1),
            };
            _cardHot = new GUIStyle(_card) { normal = { background = Marked(new Color(0.11f, 0.12f, 0.13f, 0.96f), Accent, true), textColor = Color.white } };
            _field = new GUIStyle
            {
                font = _font,
                fontSize = F(19),
                normal = { background = Flat(new Color(0.025f, 0.03f, 0.035f, 0.94f), new Color(1f, 1f, 1f, 0.14f)), textColor = Ink },
                focused = { background = Flat(new Color(0.03f, 0.036f, 0.042f, 0.97f), Accent), textColor = Color.white },
                hover = { background = Flat(new Color(0.03f, 0.036f, 0.042f, 0.95f), new Color(1f, 1f, 1f, 0.26f)), textColor = Ink },
                border = B(2),
                padding = new RectOffset(F(12), F(12), F(10), F(10)),
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
            };
            _bar = new GUIStyle { normal = { background = Flat(new Color(1f, 1f, 1f, 0.13f), new Color(0f, 0f, 0f, 0f)) }, border = B(1) };
            _barFill = new GUIStyle { normal = { background = Flat(Color.white, new Color(0f, 0f, 0f, 0f)) }, border = B(1) };
            GUI.skin.settings.cursorColor = Accent;
            GUI.skin.settings.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
        }

        private float S(float v) => v * _scale;

        /// <summary>Text over the world without a panel: a soft dark shadow under it (colour tags left out of the shadow).</summary>
        private void Shadowed(Rect r, string text, GUIStyle style)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f * old.a);
            string plain = System.Text.RegularExpressions.Regex.Replace(text, "</?color[^>]*>", "");
            GUI.Label(new Rect(r.x + S(1.5f), r.y + S(1.5f), r.width, r.height), plain, style);
            GUI.color = old;
            GUI.Label(r, text, style);
        }

        /// <summary>Screenshots of the scenery only (the smoke test's menu-picture views).</summary>
        public static bool HideHud { get; set; }

        /// <summary>The smoke test's pictures of the login and loading screens: drawn over whatever screen this frame.</summary>
        public static bool ShowLoginForShot { get; set; }
        public static bool ShowLoadingForShot { get; set; }

        /// <summary>The smoke test's picture of a shop tab (-1 = none), and of the fisheries' panel.</summary>
        public static int ShopShotTab { get; set; } = -1;
        public static bool ShowTravelForShot { get; set; }

        private void OnGUI()
        {
            if (HideHud)
                return;
            Styles();
            float w = UnityEngine.Screen.width, h = UnityEngine.Screen.height;
            if (_error.Length > 0)
            {
                GUI.DrawTexture(new Rect(0, 0, w, h), _dim!);
                var er = new Rect(w * 0.3f, h * 0.38f, w * 0.4f, S(220));
                GUI.Box(er, "", _panelStyle);
                GUI.Label(new Rect(er.x + S(24), er.y + S(20), er.width - S(48), S(130)), _error, _text);
                if (GUI.Button(new Rect(er.x + S(24), er.yMax - S(66), er.width - S(48), S(46)), "Zamknij grę", _button))
                    Application.Quit();
                return;
            }
            if (ShowLoginForShot)
            {
                DrawLogin(w, h);
                return;
            }
            if (ShowLoadingForShot)
            {
                DrawLoading(w, h);
                return;
            }
            if (ShopShotTab >= 0)
            {
                _shopTab = ShopShotTab;
                GUI.BeginGroup(new Rect(0, S(64), w, h - S(64)));
                DrawShop(w, h - S(64));
                GUI.EndGroup();
                DrawWorkshopNav(w);
                return;
            }
            if (ShowTravelForShot)
            {
                DrawTravel(w, h);
                return;
            }
            switch (_screen)
            {
                case Screen.Loading:
                    DrawLoading(w, h);
                    break;
                case Screen.Login:
                    if (_panel == Panel.Settings)
                    {
                        DrawBackdrop(w, h, LoginPicture());
                        DrawSettings(w, h);
                    }
                    else
                        DrawLogin(w, h);
                    break;
                case Screen.Play:
                    DrawHud(w, h);
                    if (_rod.State == RodState.Landed)
                        DrawCatch(w, h);
                    bool workshop = _panel is Panel.Shop or Panel.Travel or Panel.Records or Panel.Settings;
                    if (workshop)
                    {
                        GUI.BeginGroup(new Rect(0, S(64), w, h - S(64)));
                        h -= S(64);
                    }
                    switch (_panel)
                    {
                        case Panel.Pause: DrawPause(w, h); break;
                        case Panel.Shop: DrawShop(w, h); break;
                        case Panel.Inventory: DrawInventory(w, h); break;
                        case Panel.Settings: DrawSettings(w, h); break;
                        case Panel.Help: DrawHelp(w, h); break;
                        case Panel.Keepnet: DrawKeepnet(w, h); break;
                        case Panel.Records: DrawRecords(w, h); break;
                        case Panel.Smokehouse: DrawSmokehouse(w, h); break;
                        case Panel.Cooking: DrawCooking(w, h); break;
                        case Panel.Notices: DrawNotices(w, h); break;
                        case Panel.Travel: DrawTravel(w, h); break;
                    }
                    if (workshop)
                    {
                        GUI.EndGroup();
                        DrawWorkshopNav(w);
                    }
                    break;
            }
        }

        /// <summary>A fishery's picture (StreamingAssets/Menu/&lt;id&gt;_17 or _10.jpg: renders of the game), or null.</summary>
        private Texture2D? Picture(int index)
        {
            if (_locations.Count == 0)
                return null;
            var loc = _locations[Mathf.Clamp(index, 0, _locations.Count - 1)];
            string id = Path.GetFileNameWithoutExtension(loc.ResourcePath);
            if (_pictures.TryGetValue(id, out var t))
                return t;
            t = null;
            foreach (var name in new[] { id + "_17", id + "_10", id })
                if (TexCache.Find(Path.Combine(Application.streamingAssetsPath, "Menu", name)) is { } file)
                {
                    t = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "Menu " + name, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                    t.LoadImage(File.ReadAllBytes(file), true);
                    break;
                }
            return _pictures[id] = t;
        }

        /// <summary>The login screen's picture: the last account's fishery.</summary>
        private Texture2D? LoginPicture() => Picture(_guessLocation);

        private void DrawBackdrop(float w, float h, Texture2D? picture)
        {
            if (picture != null)
                GUI.DrawTexture(new Rect(0, 0, w, h), picture, ScaleMode.ScaleAndCrop);
            else
            {
                var old = GUI.color;
                GUI.color = new Color(0.05f, 0.06f, 0.07f);
                GUI.DrawTexture(new Rect(0, 0, w, h), White);
                GUI.color = old;
            }
        }

        /// <summary>
        /// A fishery's loading screen: its picture, still (nothing moves but the progress bar), the name and its
        /// description at the lower left, a tip at the lower right, a thin bar along the bottom with the step.
        /// </summary>
        private void DrawLoading(float w, float h)
        {
            int index = _screen == Screen.Loading ? _loadingIndex : _profile.Location;
            DrawBackdrop(w, h, Picture(index));
            GUI.DrawTexture(new Rect(0, h * 0.52f, w, h * 0.48f), _fadeUp!);
            var loc = _locations.Count > 0 ? _locations[Mathf.Clamp(index, 0, _locations.Count - 1)] : null;
            float x = Mathf.Max(S(48), w * 0.05f), bottom = h - S(64);
            GUI.Label(new Rect(x, bottom - S(196), S(600), S(20)), "ŁOWISKO", _label);
            GUI.Label(new Rect(x, bottom - S(176), w * 0.6f, S(64)), loc?.DisplayName ?? "", _logo);
            GUI.Label(new Rect(x, bottom - S(104), Mathf.Min(w * 0.46f, S(760)), S(76)), loc != null ? Shorten(loc.Description, 230) : "", _small);
            // the tip, lower right
            float tw = Mathf.Min(w * 0.32f, S(520));
            GUI.Label(new Rect(w - x - tw, bottom - S(112), tw, S(20)), "WSKAZÓWKA", _label);
            GUI.Label(new Rect(w - x - tw, bottom - S(90), tw, S(80)), _tip, _small);
            // the progress: the step and the per cent above a thin bar across the screen's foot
            float p = _screen == Screen.Loading ? _loadProgress : 1f;
            GUI.Label(new Rect(x, h - S(44), w * 0.5f, S(24)), $"{(_loadStep.Length > 0 ? _loadStep : "Wczytywanie")}  ·  {p * 100f:0}%", _small);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(0, h - S(6), w, S(6)), White);
            GUI.color = Accent;
            GUI.DrawTexture(new Rect(0, h - S(6), w * Mathf.Clamp01(p), S(6)), White);
            GUI.color = old;
        }

        private void Bar(Rect r, float value, Color colour)
        {
            GUI.Box(r, GUIContent.none, _bar);
            var old = GUI.color;
            GUI.color = colour;
            GUI.Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), GUIContent.none, _barFill);
            GUI.color = old;
        }

        /// <summary>
        /// The login screen: the last fishery's picture (still: the world is not drawn), the game's name and the form
        /// in a column on the left, the background work's progress at the foot (textures and models load meanwhile).
        /// </summary>
        private void DrawLogin(float w, float h)
        {
            DrawBackdrop(w, h, LoginPicture());
            GUI.DrawTexture(new Rect(0, 0, Mathf.Max(w * 0.48f, S(700)), h), _fadeRight!);
            float x = Mathf.Max(S(56), w * 0.06f), cw = S(440);
            float y = h * 0.18f;
            GUI.Label(new Rect(x, y, S(900), S(64)), "FISHING SIM", _logo);
            var old = GUI.color;
            GUI.color = Accent;
            GUI.DrawTexture(new Rect(x + S(2), y + S(66), S(56), S(3)), White);
            GUI.color = old;
            GUI.Label(new Rect(x, y + S(78), S(700), S(28)), "Symulator wędkarstwa", _small);
            y += S(150);
            // logging in or a new account: two text tabs
            float tw = S(160);
            if (GUI.Button(new Rect(x, y, tw, S(38)), "LOGOWANIE", _registering ? _tab : _tabOn))
            {
                _registering = false;
                _loginError = "";
            }
            if (GUI.Button(new Rect(x + tw + S(8), y, tw, S(38)), "NOWE KONTO", _registering ? _tabOn : _tab))
            {
                _registering = true;
                _loginError = "";
            }
            y += S(60);
            GUI.Label(new Rect(x, y, cw, S(20)), "NAZWA KONTA", _label);
            GUI.SetNextControlName("login_name");
            _loginName = GUI.TextField(new Rect(x, y + S(22), cw, S(48)), _loginName, 20, _field!);
            y += S(86);
            GUI.Label(new Rect(x, y, cw, S(20)), "HASŁO", _label);
            GUI.SetNextControlName("login_pass");
            _loginPass = GUI.PasswordField(new Rect(x, y + S(22), cw, S(48)), _loginPass, '•', 64, _field!);
            y += S(86);
            if (_registering)
            {
                GUI.Label(new Rect(x, y, cw, S(20)), "POWTÓRZ HASŁO", _label);
                _loginRepeat = GUI.PasswordField(new Rect(x, y + S(22), cw, S(48)), _loginRepeat, '•', 64, _field!);
                y += S(86);
            }
            if (_loginError.Length > 0)
                GUI.Label(new Rect(x, y - S(4), cw, S(28)), $"<color=#F2806E>{_loginError}</color>", _small);
            y += S(30);
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (GUI.Button(new Rect(x, y, cw, S(52)), _registering ? "UTWÓRZ KONTO I GRAJ" : "ZALOGUJ", _accentButton) || enter)
            {
                _loginError = (_registering ? Accounts.Register(_loginName, _loginPass, _loginRepeat) : Accounts.Login(_loginName, _loginPass)) ?? "";
                if (_loginError.Length == 0)
                    LogIn(_loginName.Trim());
            }
            y += S(66);
            GUI.Label(new Rect(x, y, cw, S(48)), _registering
                ? "Konto i postępy zapisują się na tym komputerze. Hasło nie jest nigdzie zapisane wprost."
                : "Pierwszy raz? Wybierz „Nowe konto”.", _small);
            // the foot: the background work, the version, settings and quit
            if (!_prepared && _prepareStep.Length > 0)
            {
                GUI.Label(new Rect(x, h - S(70), S(600), S(22)), $"{_prepareStep}…  {_prepareProgress * 100f:0}%", _small);
                Bar(new Rect(x, h - S(44), cw, S(3)), _prepareProgress, Accent);
            }
            GUI.Label(new Rect(x, h - S(34), S(400), S(22)), $"wersja {Version}", _label);
            if (GUI.Button(new Rect(w - S(56) - S(300), h - S(80), S(144), S(44)), "Ustawienia", _button))
            {
                _settingsBack = Panel.None;
                _panel = Panel.Settings;
            }
            if (GUI.Button(new Rect(w - S(56) - S(144), h - S(80), S(144), S(44)), "Wyjdź", _button))
                Application.Quit();
            // the name field has the focus at first (or the password when the name is remembered)
            if (Event.current.type == EventType.Repaint && GUI.GetNameOfFocusedControl().Length == 0)
                GUI.FocusControl(_loginName.Length > 0 ? "login_pass" : "login_name");
        }

        /// <summary>The account's own profile, then its fishery's loading screen.</summary>
        private void LogIn(string user)
        {
            bool fresh = !File.Exists(Accounts.ProfilePath(user));
            // the progress from before accounts (profile.json) goes to the first account made
            string legacy = Path.Combine(Application.persistentDataPath, "profile.json");
            if (fresh && Accounts.Count == 1 && File.Exists(legacy))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Accounts.ProfilePath(user))!);
                File.Copy(legacy, Accounts.ProfilePath(user));
            }
            Profile.SavePath = Accounts.ProfilePath(user);
            _profile = Profile.Load();
            _rod.Profile = _profile;
            _rod.ApplyTackle();
            FrameRod();
            _day.Hour = _profile.Hour;
            _user = user;
            _loginPass = _loginRepeat = "";
            GUI.FocusControl(null);
            if (_locations.Count > 0)
                StartCoroutine(LoadMap(Mathf.Clamp(_profile.Location, 0, _locations.Count - 1), true));
            Say($"Witaj, {user}!");
        }

        /// <summary>The rod framed for its length (the user's framing: upright, right of the middle, tip on the line of sight).</summary>
        private void FrameRod()
        {
            GripArms.FrameOnly(_player.Camera, _rod);
        }

        private void LogOut()
        {
            _rod.Reset();
            _profile.Save();
            _user = null;
            Profile.SavePath = null;
            _loginName = Accounts.LastUser;
            _loginPass = _loginRepeat = _loginError = "";
            _registering = false;
            _panel = Panel.None;
            _screen = Screen.Login;
            ShowWorld(false);
            SetCursor(false);
        }

        /// <summary>The fisheries (in the game: M or the pause menu), each with its picture; a click travels there.</summary>
        private void DrawTravel(float w, float h)
        {
            var r = Centre(w, h, Mathf.Min(S(980), w * 0.94f), Mathf.Min(S(150) + _locations.Count * S(132) + S(80), h * 0.94f));
            GUI.Label(new Rect(r.x, r.y, r.width, S(40)), "ŁOWISKA", _title);
            GUI.Label(new Rect(r.x, r.y + S(38), r.width, S(24)), "Podróż zajmuje chwilę: zestaw zostaje zwinięty, siatka jedzie z tobą.", _small);
            var view = new Rect(r.x, r.y + S(78), r.width, Mathf.Max(1f, r.height - S(136)));
            float rowWidth = view.width - S(24);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, rowWidth, _locations.Count * S(132)));
            float y = 0f;
            for (int i = 0; i < _locations.Count; i++)
            {
                var loc = _locations[i];
                bool here = i == _loadedLocation;
                bool locked = loc.RequiredLevel > _profile.Level;
                var row = new Rect(0, y, rowWidth, S(120));
                GUI.enabled = !locked;
                if (GUI.Button(row, GUIContent.none, here ? _cardHot : _card) && !locked)
                {
                    if (here)
                        ClosePanel();
                    else
                    {
                        _profile.Save();
                        int index = i;
                        StartCoroutine(LoadMap(index, true));
                    }
                }
                GUI.enabled = true;
                var pic = Picture(i);
                var thumb = new Rect(row.x + S(14), row.y + S(12), S(171), S(96));
                if (pic != null)
                    GUI.DrawTexture(thumb, pic, ScaleMode.ScaleAndCrop);
                else
                    GUI.Box(thumb, GUIContent.none, _bar);
                float tx = thumb.xMax + S(20), tw = row.xMax - tx - S(170);
                GUI.Label(new Rect(tx, row.y + S(12), tw, S(32)), loc.DisplayName, _title);
                GUI.Label(new Rect(tx, row.y + S(48), tw, S(64)), Shorten(loc.Description, 170), _small);
                string state = here ? "<color=#EBC766>TU JESTEŚ</color>" : locked ? $"OD POZIOMU {loc.RequiredLevel}" : "JEDŹ";
                GUI.Label(new Rect(row.xMax - S(160), row.y + S(48), S(140), S(24)), state, new GUIStyle(_label!) { alignment = TextAnchor.MiddleRight, normal = { textColor = locked ? Faint : Ink } });
                y += S(132);
            }
            GUI.EndScrollView();
            if (GUI.Button(new Rect(r.x, r.yMax - S(48), S(220), S(46)), "Zamknij", _button))
                ClosePanel();
        }

        private Rect Centre(float w, float h, float pw, float ph)
        {
            GUI.DrawTexture(new Rect(0, 0, w, h), _dim!);
            var r = new Rect((w - pw) / 2f, (h - ph) / 2f, pw, ph);
            GUI.Box(r, "", _panelStyle);
            return new Rect(r.x + S(26), r.y + S(22), r.width - S(52), r.height - S(44));
        }

        private void DrawPause(float w, float h)
        {
            // a column at the left over the dimmed game, as games do
            GUI.DrawTexture(new Rect(0, 0, w, h), _dim!);
            GUI.DrawTexture(new Rect(0, 0, S(560), h), _fadeRight!);
            float x = Mathf.Max(S(56), w * 0.05f), y = h * 0.2f, bw = S(360);
            GUI.Label(new Rect(x, y, S(600), S(40)), "PAUZA", _title);
            GUI.Label(new Rect(x, y + S(38), S(600), S(24)), $"{_user}  ·  poziom {_profile.Level}  ·  {_profile.Money} zł", _small);
            y += S(86);
            string[] labels = { "Wróć do gry", "Łowiska (M)", "Sklep wędkarski (T)", "Siatka z rybami (K)", "Rekordy (J)", "Sterowanie (F1)", "Ustawienia", "Wyloguj", "Wyjdź z gry" };
            for (int i = 0; i < labels.Length; i++)
            {
                if (i == 7)
                    y += S(14);
                if (GUI.Button(new Rect(x, y, bw, S(46)), labels[i], i == 0 ? _accentButton : _button))
                {
                    switch (i)
                    {
                        case 0: ClosePanel(); break;
                        case 1: OpenPanel(Panel.Travel); break;
                        case 2:
                            if (_rod.State == RodState.Idle) _panel = Panel.Shop;
                            else Say("Najpierw wyjmij zestaw z wody.");
                            break;
                        case 3: _panel = Panel.Keepnet; break;
                        case 4: _panel = Panel.Records; break;
                        case 5: _panel = Panel.Help; break;
                        case 6: _settingsBack = Panel.Pause; _panel = Panel.Settings; break;
                        case 7: LogOut(); break;
                        case 8:
                            _profile.Save();
                            Application.Quit();
                            break;
                    }
                }
                y += S(54);
            }
        }

        private void DrawSettings(float w, float h)
        {
            var r = Centre(w, h, S(680), S(600));
            GUI.Label(new Rect(r.x, r.y, r.width, S(40)), "USTAWIENIA", _title);
            float y = r.y + S(64);
            Row(ref y, r, "Jakość grafiki", Settings.QualityNames[Settings.Quality], d => Settings.Quality = Mathf.Clamp(Settings.Quality + d, 0, 4));
            Row(ref y, r, "Zasięg widzenia", Settings.ViewNames[Settings.View], d => Settings.View = Mathf.Clamp(Settings.View + d, 0, 2));
            Row(ref y, r, "Limit FPS", Settings.FpsLimits[Settings.Fps] == 0 ? "bez limitu" : Settings.FpsLimits[Settings.Fps].ToString(), d => Settings.Fps = Mathf.Clamp(Settings.Fps + d, 0, Settings.FpsLimits.Length - 1));
            Row(ref y, r, "Synchronizacja pionowa", Settings.VSync ? "włączona" : "wyłączona", _ => Settings.VSync = !Settings.VSync);
            Row(ref y, r, "Pełny ekran", Settings.Fullscreen ? "tak" : "nie (okno)", _ => Settings.Fullscreen = !Settings.Fullscreen);
            Row(ref y, r, "Czułość myszy", Settings.Mouse.ToString("0.0"), d => Settings.Mouse = Mathf.Clamp(Settings.Mouse + d * 0.2f, 0.4f, 6f));
            GUI.Label(new Rect(r.x, y + S(4), r.width, S(44)), "Jakość tekstur zmienia się przy następnym wczytaniu łowiska.", _small);
            if (GUI.Button(new Rect(r.x, r.yMax - S(48), r.width, S(46)), "Gotowe", _accentButton))
            {
                Settings.Save();
                Settings.Apply(_player.Camera);
                _player.Sensitivity = Settings.Mouse;
                FrameRod();
                if (_screen == Screen.Play)
                {
                    _panel = _settingsBack;
                    if (_panel == Panel.None)
                        SetCursor(true);
                }
                else
                    _panel = Panel.None;
            }
        }

        private void Row(ref float y, Rect r, string label, string value, Action<int> change)
        {
            GUI.Label(new Rect(r.x, y + S(10), r.width * 0.5f, S(30)), label, _text);
            if (GUI.Button(new Rect(r.x + r.width * 0.5f, y, S(44), S(44)), "‹", _button))
                change(-1);
            GUI.Label(new Rect(r.x + r.width * 0.5f + S(44), y + S(10), r.width * 0.5f - S(88), S(30)), value, new GUIStyle(_text!) { alignment = TextAnchor.UpperCenter });
            if (GUI.Button(new Rect(r.x + r.width - S(44), y, S(44), S(44)), "›", _button))
                change(1);
            y += S(56);
        }

        private void DrawHelp(float w, float h)
        {
            var r = Centre(w, h, S(760), S(660));
            GUI.Label(new Rect(r.x, r.y, r.width, S(40)), "STEROWANIE", _title);
            GUI.Label(new Rect(r.x, r.y + S(56), r.width, r.height - S(120)),
                "<b>W A S D</b> – chodzenie, <b>Shift</b> – bieg, <b>mysz</b> – rozglądanie\n" +
                "<b>LPM (przytrzymaj)</b> – siła rzutu, puść, żeby zarzucić\n" +
                "<b>LPM</b> w wodzie – zwijanie zestawu (przynęta pracuje, spławik płynie do brzegu)\n" +
                "<b>PPM</b> – zacięcie, gdy spławik zanurkuje / szczytówka się ugnie\n" +
                "W holu: <b>LPM</b> – zwijanie, <b>PPM</b> – podnoszenie wędki, <b>kółko</b> – hamulec\n" +
                "Spinning: <b>PPM</b> – podszarpnięcie przynęty, <b>Shift+LPM</b> – szybciej; zaczep: <b>PPM</b> szarpnij, <b>LPM</b> ciągnij\n" +
                "<b>Ctrl+kółko</b> – grunt spławika (przed rzutem), <b>V</b> – garść zanęty przy spławiku, <b>B</b> – ostrzenie haczyka (osełka)\n" +
                "Złowiona ryba: <b>E</b> – do siatki, <b>R</b> – wypuść\n" +
                "<b>I</b> – ekwipunek i montaż zestawu, <b>T</b> – sklep, <b>K</b> – siatka, <b>J</b> – rekordy, <b>M</b> – łowiska, <b>Tab</b> – szczegóły, <b>Esc</b> – pauza\n" +
                "<b>E</b> – sklepy, skup, wędzarnia, łódka, ognisko, tablica ogłoszeń, grzyby i jagody\n" +
                "<b>G</b> – kopanie łopatą, <b>H</b> – siatka na owady, <b>L</b> – podrywka (akcesoria ze sklepu)\n" +
                "Łódka: <b>W/S</b> wiosła, <b>A/D</b> skręt, <b>X</b> kotwica\n\n" +
                "Pasek ŻYŁKA pokazuje obciążenie żyłki z haczykiem, pasek WĘDKA obciążenie kija; biała kreska to punkt, w którym hamulec zaczyna oddawać żyłkę. Za mocny hamulec zrywa żyłkę, za słaby pozwala rybie odejść.",
                _text);
            if (GUI.Button(new Rect(r.x, r.yMax - S(48), S(220), S(46)), "Zamknij", _button))
                ClosePanel();
        }

        private void DrawHud(float w, float h)
        {
            // the time, the weather and the place, top left: text over the world, no panel
            int hh = Mathf.FloorToInt(_day.Hour), mm = Mathf.FloorToInt((_day.Hour - hh) * 60f);
            string weather = _day.Weather switch { Weather.Clear => "pogodnie", Weather.Cloudy => "pochmurno", _ => "deszcz" };
            var place = _world.Lake?.Location.DisplayName ?? "";
            float x0 = S(28), y0 = S(22);
            var clock = new GUIStyle(_title!) { fontSize = Mathf.RoundToInt(34 * _scale), wordWrap = false };
            Shadowed(new Rect(x0, y0, S(140), S(44)), $"{hh:00}:{mm:00}", clock);
            Shadowed(new Rect(x0 + S(112), y0 + S(4), S(360), S(24)), weather + (_day.Wind > 0.55f ? ", wietrznie" : ""), new GUIStyle(_text!) { wordWrap = false });
            Shadowed(new Rect(x0 + S(112), y0 + S(26), S(420), S(22)), place, new GUIStyle(_small!) { wordWrap = false });
            Shadowed(new Rect(x0, y0 + S(52), S(520), S(22)), $"poziom {_profile.Level}  ·  {_profile.Money} zł  ·  siatka {_profile.Keepnet.Count}", new GUIStyle(_small!) { wordWrap = false });
            if (_profile.ActiveMeal is { } meal)
                Shadowed(new Rect(x0, y0 + S(74), S(520), S(22)), $"{meal.Name}: {(_profile.MealUntil - _profile.Clock):0.0} godz.", new GUIStyle(_small!) { wordWrap = false });

            if (Boat.Aboard && _profile.HasAccessory("echosonda"))
            {
                var er = new Rect(S(28), h * 0.42f, S(190), S(76));
                GUI.Box(er, "", _panelStyle);
                GUI.Label(new Rect(er.x + S(14), er.y + S(8), er.width, S(18)), "ECHOSONDA", _label);
                GUI.Label(new Rect(er.x + S(14), er.y + S(28), er.width, S(40)), $"<size={(int)S(28)}>{_boat.DepthUnder:0.0} m</size>", _text);
            }
            // a small dot in the middle
            if (_panel == Panel.None && _rod.State != RodState.Landed)
            {
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GUI.DrawTexture(new Rect(w / 2f - S(2), h / 2f - S(2), S(4), S(4)), White);
                GUI.color = old;
            }

            if (_panel == Panel.None && _rod.RodVisible)
            {
                // a soft shade along the foot of the screen keeps the bars and numbers readable over bright water
                var shade = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GUI.DrawTexture(new Rect(0, h - S(170), w, S(170)), _fadeUp!);
                GUI.color = shade;
                DrawIndicators(w, h);
                DrawTackleLines(w, h);
            }
            if (_hint.Length > 0 && _panel == Panel.None && _rod.State == RodState.Idle)
            {
                // "[E] what it does": the key in a small box, the rest as text
                string key = _hint.Length > 2 && _hint[1] == ' ' ? _hint.Substring(0, 1) : "";
                string what = key.Length > 0 ? _hint.Substring(_hint.IndexOf('–') >= 0 ? _hint.IndexOf('–') + 1 : 2).Trim() : _hint;
                var style = new GUIStyle(_text!) { wordWrap = false };
                float tw = Mathf.Min(style.CalcSize(new GUIContent(what)).x, w * 0.8f);
                float total = tw + (key.Length > 0 ? S(44) : 0f);
                float hx = (w - total) / 2f, hy = h * 0.62f;
                if (key.Length > 0)
                {
                    GUI.Box(new Rect(hx, hy, S(32), S(32)), GUIContent.none, _panelStyle);
                    GUI.Label(new Rect(hx, hy + S(4), S(32), S(26)), key, new GUIStyle(_text!) { alignment = TextAnchor.UpperCenter, font = _fontBold });
                    hx += S(44);
                }
                Shadowed(new Rect(hx, hy + S(4), tw + S(4), S(28)), what, style);
            }
            if (Time.unscaledTime < _messageUntil)
            {
                var style = new GUIStyle(_text!) { wordWrap = false, alignment = TextAnchor.MiddleCenter };
                float mw = Mathf.Min(style.CalcSize(new GUIContent(_message)).x + S(56), w * 0.8f);
                var mr = new Rect((w - mw) / 2f, h * 0.14f, mw, S(44));
                GUI.Box(mr, "", _panelStyle);
                GUI.Label(mr, _message, style);
            }
        }

        /// <summary>
        /// The fishing indicators at the bottom middle: the line out on the left, two bars (the line with its hook and
        /// the rod: load against what each holds, green to red, the drag's slipping point marked white on the line's
        /// bar), the drag on the right; while the cast is held, its power and where it would land.
        /// </summary>
        private void DrawIndicators(float w, float h)
        {
            float bw = Mathf.Min(S(380), w * 0.34f);
            float cx = w / 2f, y = h - S(78);
            var big = new GUIStyle(_title!) { fontSize = Mathf.RoundToInt(28 * _scale), wordWrap = false, alignment = TextAnchor.UpperRight };
            var bigLeft = new GUIStyle(big) { alignment = TextAnchor.UpperLeft };
            var label = new GUIStyle(_label!) { wordWrap = false };
            var labelRight = new GUIStyle(label) { alignment = TextAnchor.UpperRight };
            float barX = cx - bw / 2f;
            bool fight = _rod.State == RodState.Fight;
            bool wet = _rod.State is not (RodState.Idle or RodState.Charging or RodState.Landed);
            if (_rod.State == RodState.Charging)
            {
                // the cast: power and the distance it would reach without wind
                Shadowed(new Rect(barX - S(150), y - S(8), S(136), S(40)), $"{_rod.AimDistance:0} m", big);
                Shadowed(new Rect(barX, y - S(16), bw, S(18)), "SIŁA RZUTU", label);
                Bar(new Rect(barX, y + S(6), bw, S(8)), _rod.CastPower, Color.Lerp(new Color(0.75f, 0.82f, 0.88f), Accent, _rod.CastPower));
            }
            else
            {
                float load = _rod.LoadKg;
                float line = load / Mathf.Max(_rod.LineLimit, 0.05f), rod = load / Mathf.Max(_rod.RodLimit, 0.05f);
                Color Load(float k) => k < 0.6f ? Color.Lerp(GoodCol, WarnCol, Mathf.Clamp01((k - 0.35f) / 0.25f)) : Color.Lerp(WarnCol, BadCol, Mathf.Clamp01((k - 0.6f) / 0.3f));
                // the line out (the fish's distance in a fight)
                string outText = wet ? $"{(fight && _rod.Fight != null ? _rod.Fight.LineOut : _rod.LineOut):0.0}" : "—";
                Shadowed(new Rect(barX - S(150), y - S(8), S(118), S(40)), outText, big);
                Shadowed(new Rect(barX - S(30), y + S(6), S(26), S(18)), wet ? "m" : "", label);
                Shadowed(new Rect(barX, y - S(16), bw * 0.5f, S(18)), "ŻYŁKA", label);
                Shadowed(new Rect(barX + bw * 0.5f, y - S(16), bw * 0.5f, S(18)), $"{load:0.00} / {_rod.LineLimit:0.0} kg", labelRight);
                // a fight near the break: the line's bar pulses
                float pulse = fight && line > 0.85f ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 9f)) : 1f;
                var lineBar = new Rect(barX, y + S(4), bw, S(9));
                Bar(lineBar, line, Load(line) * new Color(1f, 1f, 1f, pulse));
                float slip = Mathf.Clamp01(_rod.DragKg / Mathf.Max(_rod.LineLimit, 0.05f));
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.95f);
                GUI.DrawTexture(new Rect(lineBar.x + lineBar.width * slip - S(1), lineBar.y - S(4), S(2), lineBar.height + S(8)), White);
                GUI.color = old;
                Bar(new Rect(barX, y + S(20), bw, S(5)), rod, Load(rod));
                Shadowed(new Rect(barX, y + S(28), bw * 0.5f, S(18)), "WĘDKA", label);
                if (Input.GetKey(KeyCode.Tab) && fight && _rod.Fight is { } tf)
                    Shadowed(new Rect(barX, y - S(44), bw, S(22)),
                        $"ryba {tf.Distance:0} m · siła {tf.Stamina * 100f:0}% · {(tf.Slipping ? "hamulec oddaje żyłkę" : tf.Running ? "odjazd" : "zmęczona")}",
                        new GUIStyle(_small!) { wordWrap = false, alignment = TextAnchor.UpperCenter });
            }
            // the drag, right of the bars
            float rx = barX + bw + S(22);
            Shadowed(new Rect(rx, y - S(16), S(160), S(18)), "HAMULEC", label);
            string verdict = _rod.DragKg > _rod.EffectiveLimit * 0.85f ? "  <color=#F2806E>za mocno</color>" : _rod.DragKg < _rod.EffectiveLimit * 0.2f ? "  <color=#A6CBEF>luźno</color>" : "";
            Shadowed(new Rect(rx, y - S(2), S(120), S(40)), $"{_rod.DragSetting * 100f:0}%", bigLeft);
            Shadowed(new Rect(rx, y + S(30), S(260), S(20)), $"{_rod.DragKg:0.0} kg{verdict}", new GUIStyle(_small!) { wordWrap = false });
            if (_rod.Reeling)
                Shadowed(new Rect(barX, y + S(46), bw, S(20)), $"zwijanie {_rod.CrankTurns:0.0} obr/s · {_rod.RetrieveRate:0.00} m/s",
                    new GUIStyle(_small!) { wordWrap = false, alignment = TextAnchor.UpperCenter });
        }

        /// <summary>The tackle in hand, bottom right, in three quiet lines.</summary>
        private void DrawTackleLines(float w, float h)
        {
            string Short(string? name) => name == null ? "—" : name.Split('·')[0].Trim();
            var rod = _rod.RodItem;
            var reel = _rod.ReelItem;
            var line = _rod.LineInUse;
            var bait = _rod.BaitItem;
            var hook = _rod.HookItem;
            string baitText = bait == null ? "brak przynęty" : bait.Reusable ? Short(bait.DisplayName) : $"{Short(bait.DisplayName)} ×{_profile.BaitCount(_profile.Bait)}";
            var style = new GUIStyle(_small!) { wordWrap = false, alignment = TextAnchor.UpperRight };
            float tw = Mathf.Min(S(520), w * 0.3f), x = w - S(28) - tw, y = h - S(96);
            Shadowed(new Rect(x, y, tw, S(22)), $"{Short(rod?.DisplayName)}  ·  {Short(reel?.DisplayName)}", style);
            Shadowed(new Rect(x, y + S(22), tw, S(22)), line == null ? "brak żyłki" : $"{Short(line.DisplayName)} {line.Diameter:0.00} mm  ·  {(hook != null ? Short(hook.DisplayName) : "brak haczyka")}", style);
            Shadowed(new Rect(x, y + S(44), tw, S(22)), $"{baitText}  ·  na szpuli {_rod.SpoolLeft:0} m", style);
        }

        private void DrawCatch(float w, float h)
        {
            var sp = _rod.Species;
            if (sp == null)
                return;
            // the fish itself is shown above the panel (Fishing holds it up before the camera)
            var box = new Rect((w - S(640)) / 2f, h - S(430), S(640), S(400));
            GUI.Box(box, "", _panelStyle);
            var r = new Rect(box.x + S(22), box.y + S(18), box.width - S(44), box.height - S(36));
            GUI.Label(new Rect(r.x, r.y, r.width, S(46)), sp.SpeciesName.ToUpperInvariant(), _title);
            GUI.Label(new Rect(r.x, r.y + S(48), r.width, S(26)), sp.LatinName, _small);
            int value = Progression.FishValue(sp, _rod.Weight);
            int xp = Progression.FishXp(sp, _rod.Weight);
            bool record = !_profile.Records.TryGetValue(sp.SpeciesName, out float best) || _rod.Weight > best;
            float length = sp.LengthRef * Mathf.Pow(Mathf.Max(_rod.Weight, 0.005f), 0.33f) * 100f;
            GUI.Label(new Rect(r.x, r.y + S(92), r.width, S(120)),
                $"<b>{_rod.Weight:0.000} kg</b> · {length:0} cm\nWartość: {value} zł · doświadczenie: +{xp}" + (record ? "\n<color=#f2b84b>Nowy rekord!</color>" : ""), _text);
            GUI.Label(new Rect(r.x, r.y + S(200), r.width, S(80)), Shorten(sp.Description, 220), _small);
            bool keep = CanKeep(out string why);
            if (!keep)
                GUI.Label(new Rect(r.x, r.yMax - S(100), r.width, S(30)), $"<color=#ff8a6a>{why}</color>", _text);
            GUI.enabled = keep;
            if (GUI.Button(new Rect(r.x, r.yMax - S(50), r.width * 0.48f, S(48)), "Do siatki (E)", _accentButton))
                Decide(true);
            GUI.enabled = true;
            if (GUI.Button(new Rect(r.x + r.width * 0.52f, r.yMax - S(50), r.width * 0.48f, S(48)), "Wypuść (R)", _button))
                Decide(false);
        }

        private static string Shorten(string s, int max) => s.Length <= max ? s : s.Substring(0, max).TrimEnd() + "…";

        private void DrawKeepnet(float w, float h)
        {
            var r = Centre(w, h, S(620), S(640));
            GUI.Label(new Rect(r.x, r.y, r.width, S(46)), "SIATKA", _title);
            float y = r.y + S(60);
            var view = new Rect(r.x, y, r.width, r.height - S(170));
            var content = new Rect(0, 0, r.width - S(24), Mathf.Max(_profile.Keepnet.Count * S(34), view.height));
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            for (int i = 0; i < _profile.Keepnet.Count; i++)
            {
                var f = _profile.Keepnet[i];
                int v = Tackle.Species(f.Species) is { } sp ? Progression.FishValue(sp, f.Weight) : 0;
                GUI.Label(new Rect(0, i * S(34), content.width, S(32)), $"{f.Species}  {f.Weight:0.000} kg  ·  {v} zł", _text);
            }
            if (_profile.Keepnet.Count == 0)
                GUI.Label(new Rect(0, 0, content.width, S(32)), "Siatka jest pusta.", _small);
            GUI.EndScrollView();
            int total = _profile.KeepnetValue();
            GUI.enabled = total > 0;
            if (GUI.Button(new Rect(r.x, r.yMax - S(104), r.width, S(48)), $"Sprzedaj wszystko w skupie ({total} zł)", _accentButton))
                Say($"Sprzedano ryby za {_profile.SellKeepnet()} zł.");
            GUI.enabled = true;
            if (GUI.Button(new Rect(r.x, r.yMax - S(50), r.width, S(48)), "Zamknij", _button))
                ClosePanel();
        }

        private void DrawRecords(float w, float h)
        {
            var r = Centre(w, h, S(620), S(640));
            GUI.Label(new Rect(r.x, r.y, r.width, S(46)), $"REKORDY  <size={(int)S(18)}><color=#9AA3AA>{_profile.Records.Count}/{Tackle.AllSpecies.Count} gatunków</color></size>", _title);
            var view = new Rect(r.x, r.y + S(60), r.width, r.height - S(120));
            var species = Tackle.AllSpecies;
            var content = new Rect(0, 0, r.width - S(24), species.Count * S(32));
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            for (int i = 0; i < species.Count; i++)
            {
                var sp = species[i];
                string rec = _profile.Records.TryGetValue(sp.SpeciesName, out float best) ? $"{best:0.000} kg" : "—";
                GUI.Label(new Rect(0, i * S(32), content.width * 0.6f, S(30)), sp.SpeciesName, _profile.Records.ContainsKey(sp.SpeciesName) ? _text : _small);
                GUI.Label(new Rect(content.width * 0.6f, i * S(32), content.width * 0.4f, S(30)), rec, _small);
            }
            GUI.EndScrollView();
            if (GUI.Button(new Rect(r.x, r.yMax - S(50), r.width, S(48)), "Zamknij", _button))
                ClosePanel();
        }

        private void DrawCooking(float w, float h)
        {
            var r = Centre(w, h, S(760), S(640));
            GUI.Label(new Rect(r.x, r.y, r.width, S(46)), "GOTOWANIE", _title);
            var pantry = string.Join(", ", _profile.Pantry.Where(p => p.Value > 0).Select(p => $"{Core.Camp.Foraging.NameOf(p.Key)} {p.Value}"));
            GUI.Label(new Rect(r.x, r.y + S(48), r.width, S(26)), $"Siatka: {_profile.Keepnet.Count} ryb · spiżarnia: {(pantry.Length > 0 ? pantry : "pusta")}", _small);
            float y = r.y + S(84);
            var fish = _profile.Keepnet.Select((f, i) => new Core.Camp.Cooking.Ingredient(i, f.Weight,
                Tackle.Species(f.Species) is { } sp ? Progression.FishValue(sp, f.Weight) : 0,
                Tackle.Species(f.Species) is { } s2 && f.Weight < s2.LegalWeight)).ToList();
            foreach (var recipe in Core.Camp.Cooking.All)
            {
                var missing = Core.Camp.Cooking.Missing(recipe, fish, _profile.HasAccessory("garnek"), _profile.Pantry);
                GUI.Label(new Rect(r.x, y, r.width - S(170), S(28)), $"<b>{recipe.Name}</b> – {recipe.Buff.Name}, {recipe.Buff.Hours:0} godz.", _text);
                GUI.Label(new Rect(r.x, y + S(28), r.width - S(170), S(44)), missing ?? recipe.Description, _small);
                GUI.enabled = missing == null;
                if (GUI.Button(new Rect(r.xMax - S(150), y + S(6), S(150), S(44)), "Gotuj", _accentButton))
                {
                    _sound.Play(Core.Audio.OneShot.Bubble);
                    Say(_profile.Cook(recipe));
                }
                GUI.enabled = true;
                y += S(86);
            }
            if (GUI.Button(new Rect(r.x, r.yMax - S(50), r.width, S(48)), "Zamknij", _button))
                ClosePanel();
        }

        private void DrawNotices(float w, float h)
        {
            var r = Centre(w, h, S(820), S(660));
            GUI.Label(new Rect(r.x, r.y, r.width, S(46)), "TABLICA OGŁOSZEŃ", _title);
            var notes = Camp.Notices(_world.Lake, _profile, _profile.Location);
            int day = (int)(_profile.Clock / 24f);
            float y = r.y + S(60);
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                string key = $"{day}:{_profile.Location}:{i}";
                bool done = _handedIn.Contains(key) || _profile.Notices.Contains(key);
                GUI.Label(new Rect(r.x, y, r.width - S(190), S(28)), $"<b>{n.From}</b>: {n.Wanted} – {n.Reward} zł, +{n.Xp} XP", _text);
                GUI.Label(new Rect(r.x, y + S(28), r.width - S(190), S(48)), n.Text, _small);
                GUI.enabled = !done;
                if (GUI.Button(new Rect(r.xMax - S(170), y + S(8), S(170), S(44)), done ? "oddane" : "Oddaj", _button))
                    Say(HandIn(n, key));
                GUI.enabled = true;
                y += S(88);
            }
            if (GUI.Button(new Rect(r.x, r.yMax - S(50), r.width, S(48)), "Zamknij", _button))
                ClosePanel();
        }

        private string HandIn(Core.Camp.VillageRequest n, string key)
        {
            switch (n.Kind)
            {
                case Core.Camp.RequestKind.Fish:
                {
                    var fits = _profile.Keepnet.Where(f => Tackle.Species(f.Species) is { } sp
                        && Core.Camp.NoticeBoard.FishFits(n, f.Species, f.Weight, f.Weight < sp.LegalWeight)).Take(n.Count).ToList();
                    if (fits.Count < n.Count)
                        return $"Potrzeba: {n.Wanted}. W siatce masz {fits.Count}.";
                    foreach (var f in fits)
                        _profile.Keepnet.Remove(f);
                    break;
                }
                case Core.Camp.RequestKind.Bait:
                    if (_profile.BaitCount(n.Item) < n.Count)
                        return $"Potrzeba: {n.Wanted}.";
                    _profile.Baits[n.Item] = _profile.BaitCount(n.Item) - n.Count;
                    break;
                case Core.Camp.RequestKind.Forage:
                    if (_profile.Pantry.GetValueOrDefault(n.Item) < n.Count)
                        return $"Potrzeba: {n.Wanted}.";
                    _profile.Pantry[n.Item] -= n.Count;
                    break;
                default:
                    return "Wędzone ryby sprzedaje się w wędzarni.";
            }
            _profile.Money += n.Reward;
            _profile.Xp += n.Xp;
            _profile.Notices.Add(key);
            _handedIn.Add(key);
            _sound.Play(Core.Audio.OneShot.Coins);
            return $"{n.From} dziękuje: +{n.Reward} zł, +{n.Xp} XP.";
        }

        private void DrawSmokehouse(float w, float h)
        {
            var r = Centre(w, h, S(620), S(420));
            GUI.Label(new Rect(r.x, r.y, r.width, S(46)), "WĘDZARNIA", _title);
            string state;
            if (_profile.Smoking.Count == 0)
                state = $"Wędzarz przyjmie wymiarowe ryby od 100 g z twojej siatki ({_profile.Keepnet.Count} ryb). " +
                        $"Uwędzona ryba jest warta {Core.Camp.Smoking.ValueFactor:0.0}× więcej niż w skupie.";
            else if (_profile.SmokingHoursLeft > 0f)
                state = $"W dymie wisi {_profile.Smoking.Count} ryb. Gotowe za {_profile.SmokingHoursLeft * 60f:0} min gry.";
            else
                state = $"Uwędzone! {_profile.Smoking.Count} ryb, wędzarz zapłaci {_profile.SmokedValue()} zł.";
            GUI.Label(new Rect(r.x, r.y + S(64), r.width, S(150)), state, _text);
            if (_profile.Smoking.Count == 0)
            {
                if (GUI.Button(new Rect(r.x, r.yMax - S(104), r.width, S(48)), "Oddaj ryby do wędzenia", _accentButton))
                    Say(_profile.StartSmoking());
            }
            else
            {
                GUI.enabled = _profile.SmokingHoursLeft <= 0f;
                if (GUI.Button(new Rect(r.x, r.yMax - S(104), r.width, S(48)), $"Sprzedaj uwędzone ({_profile.SmokedValue()} zł)", _accentButton))
                    Say($"Wędzarz zapłacił {_profile.SellSmoked()} zł.");
                GUI.enabled = true;
            }
            if (GUI.Button(new Rect(r.x, r.yMax - S(50), r.width, S(48)), "Zamknij", _button))
                ClosePanel();
        }

        // ------------------------------------------------------------------ shop
        private static readonly string[] ShopTabs = { "Wędki", "Kołowrotki", "Żyłki", "Haczyki", "Przynęty", "Akcesoria" };
        private static readonly string[] RigSlots = { "Wędka", "Kołowrotek", "Żyłka", "Haczyk", "Przynęta" };
        private GUIStyle? _inventorySurface, _inventoryCard, _inventorySelected, _inventoryHero, _inventoryAction;
        private Texture2D[]? _gearGlyphs;
        private TackleItem? _inventoryInspect;
        private TacklePreview? _tacklePreview;

        private static Texture2D InventorySkin(Color bottom, Color top, Color border)
        {
            const int size = 48;
            const float radius = 13f;
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var corner = Vector2.Max(Vector2.zero, new Vector2(Mathf.Abs(x - 23.5f), Mathf.Abs(y - 23.5f)) - Vector2.one * (24f - radius));
                    float coverage = Mathf.Clamp01(radius - corner.magnitude);
                    var color = Color.Lerp(bottom, top, y / 47f);
                    color = Color.Lerp(color, border, 1f - Mathf.Clamp01(radius - corner.magnitude - 0.7f));
                    color.a *= coverage;
                    pixels[y * size + x] = color;
                }
            image.SetPixels(pixels);
            image.Apply(false, true);
            return image;
        }

        private static Texture2D GearGlyph(params Vector2[][] strokes)
        {
            const int size = 80;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var point = new Vector2(x, y);
                    float distance = float.PositiveInfinity;
                    foreach (var stroke in strokes)
                        for (int i = 1; i < stroke.Length; i++)
                        {
                            var from = stroke[i - 1];
                            var edge = stroke[i] - from;
                            float t = Mathf.Clamp01(Vector2.Dot(point - from, edge) / edge.sqrMagnitude);
                            distance = Mathf.Min(distance, (point - from - edge * t).sqrMagnitude);
                        }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(2.3f - Mathf.Sqrt(distance)));
                }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void InventoryArt()
        {
            if (_inventorySurface != null)
                return;
            GUIStyle Skin(Color bottom, Color top, Color border) => new()
            {
                border = new RectOffset(15, 15, 15, 15),
                normal = { background = InventorySkin(bottom, top, border) },
            };
            _inventorySurface = Skin(new Color(.035f, .065f, .073f, .98f), new Color(.09f, .155f, .16f, .99f), new Color(.22f, .35f, .35f));
            _inventoryCard = Skin(new Color(.07f, .12f, .13f), new Color(.13f, .2f, .21f), new Color(.19f, .29f, .29f));
            _inventorySelected = Skin(new Color(.18f, .17f, .12f), new Color(.27f, .25f, .16f), new Color(.74f, .61f, .31f));
            _inventoryHero = Skin(new Color(.045f, .085f, .09f), new Color(.15f, .26f, .25f), new Color(.24f, .37f, .35f));
            _inventoryAction = Skin(new Color(.72f, .53f, .23f), new Color(.97f, .81f, .45f), new Color(.98f, .85f, .53f));
            _inventoryAction.alignment = TextAnchor.MiddleCenter;
            _inventoryAction.normal.textColor = new Color(.08f, .1f, .095f);
            Vector2[] Stroke(params int[] xy)
            {
                var points = new Vector2[xy.Length / 2];
                for (int i = 0; i < points.Length; i++)
                    points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
                return points;
            }
            _gearGlyphs = new[]
            {
                GearGlyph(Stroke(10,14,26,29,42,45,56,56,71,63), Stroke(17,14,12,19,27,34,32,29,17,14), Stroke(31,34,33,26,42,27,43,36,36,40), Stroke(71,63,72,43)),
                GearGlyph(Stroke(17,51,60,51), Stroke(42,51,42,38,31,32), Stroke(15,35,31,35,38,28,38,17,31,11,15,11,9,17,9,29,15,35), Stroke(38,24,54,24,63,32,69,32)),
                GearGlyph(Stroke(23,57,57,57,62,52,62,29,57,24,23,24,18,29,18,52,23,57), Stroke(21,47,59,47), Stroke(21,39,59,39), Stroke(21,31,57,31), Stroke(57,24,65,17,73,21)),
                GearGlyph(Stroke(44,67,51,61,45,55,39,61,44,67), Stroke(45,55,45,27,40,16,31,13,21,15,15,23,15,32,23,40,22,30)),
                GearGlyph(Stroke(11,48,24,52,47,47,61,38,71,46,69,27,61,34,46,29,24,30,11,39,11,48), Stroke(24,46,26,46), Stroke(38,30,38,19,31,15,25,20,28,26)),
                GearGlyph(Stroke(12,49,67,49,67,18,12,18,12,49), Stroke(12,42,67,42), Stroke(29,49,29,58,50,58,50,49), Stroke(35,42,35,34,44,34,44,42)),
            };
        }

        private void DrawGearGlyph(Rect rectangle, int slot, Color tint)
        {
            var previous = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rectangle, _gearGlyphs![slot], ScaleMode.ScaleToFit);
            GUI.color = previous;
        }

        private void OpenInventory()
        {
            _rigDraft[0] = _rod.RodItem;
            _rigDraft[1] = _rod.ReelItem;
            _rigDraft[2] = _rod.LineInUse;
            _rigDraft[3] = _rod.HookItem;
            _rigDraft[4] = _rod.BaitItem;
            OpenPanel(Panel.Inventory);
            SelectInventoryTab(-1);
        }

        private void SelectInventoryTab(int tab)
        {
            _inventoryTab = tab;
            _inventoryInspect = tab < 0 ? _rigDraft[0] : tab < _rigDraft.Length ? _rigDraft[tab] : null;
            _scroll = Vector2.zero;
        }

        private bool HasInventoryItem(TackleItem item) => item is BaitData { Reusable: false }
            ? _profile.BaitCount(item.ItemId) > 0
            : _profile.Owned.Contains(item.ItemId);

        private Texture2D? _gearAtlas;
        private Dictionary<string, Rect>? _gearRegions;
        private string _inventoryQuery = "";
        private bool _inventorySortByName;

        private void InventoryIcon(Rect rect, TackleItem? item, int category)
        {
            if (_gearRegions == null)
            {
                _gearRegions = new Dictionary<string, Rect>(StringComparer.Ordinal);
                string path = Path.Combine(Application.streamingAssetsPath, "Inventory");
                if (File.Exists(Path.Combine(path, "gear.png")) && File.Exists(Path.Combine(path, "gear.txt")))
                {
                    _gearAtlas = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                    if (_gearAtlas.LoadImage(File.ReadAllBytes(Path.Combine(path, "gear.png")), true))
                    {
                        string[] keys = File.ReadAllLines(Path.Combine(path, "gear.txt"));
                        float cellWidth = 96f / _gearAtlas.width, cellHeight = 96f / _gearAtlas.height;
                        for (int i = 0; i < keys.Length; i++)
                            _gearRegions[keys[i]] = new Rect(i % 16 * cellWidth, 1f - (i / 16 + 1) * cellHeight, cellWidth, cellHeight);
                    }
                }
            }
            string thumbnail = item switch
            {
                RodData rod => string.IsNullOrEmpty(rod.Model) ? "rods/float_ostoja" : rod.Model,
                ReelData reel => string.IsNullOrEmpty(reel.Model) ? "reels/small_hokuto" : reel.Model,
                LineData { Kind: LineKind.Braid } => "line_braid",
                LineData { Kind: LineKind.Fluoro } => "line_fluoro",
                LineData => "line_mono",
                HookData => "hook",
                BaitData { Kind: BaitKind.Natural } => "bait_worm",
                BaitData { Kind: BaitKind.Plant } => "bait_corn",
                BaitData { Kind: BaitKind.Carp } => "bait_carp",
                _ => "",
            };
            if (item != null && _gearAtlas != null &&
                (_gearRegions.TryGetValue(item.ItemId, out var uv) || _gearRegions.TryGetValue(thumbnail, out uv)))
                GUI.DrawTextureWithTexCoords(rect, _gearAtlas, uv);
            else
                DrawGearGlyph(rect, category, item != null ? Muted : Faint);
        }

        private static int EquipmentCategory(TackleItem item) => item switch
        {
            RodData => 0, ReelData => 1, LineData => 2, HookData => 3, BaitData => 4, _ => 5,
        };

        private void DrawWorkshopNav(float w)
        {
            InventoryArt();
            float height = S(64), buttonWidth = w * .122f;
            GUI.Box(new Rect(0, 0, w, height), GUIContent.none, _inventorySurface);
            string[] labels = { "Graj", "Ekwipunek", "Sklep", "Łowiska", "Dziennik" };
            Panel[] destinations = { Panel.None, Panel.Inventory, Panel.Shop, Panel.Travel, Panel.Records };
            _inventoryAction!.font = _fontBold;
            _inventoryAction.fontSize = _button!.fontSize;
            for (int i = 0; i < destinations.Length; i++)
            {
                var destination = destinations[i];
                bool selected = (ShopShotTab >= 0 ? Panel.Shop : _panel) == destination;
                if (GUI.Button(new Rect(S(10) + i * buttonWidth, S(7), buttonWidth - S(5), height - S(14)),
                    labels[i], selected ? _inventoryAction : _button) && !selected)
                {
                    if (destination == Panel.Shop && _rod.State != RodState.Idle)
                    {
                        Say("Najpierw wyjmij zestaw z wody.");
                        continue;
                    }
                    if (_panel == Panel.Inventory || destination == Panel.None)
                        ClosePanel();
                    if (destination == Panel.Inventory)
                        OpenInventory();
                    else if (destination != Panel.None)
                        OpenPanel(destination);
                }
            }
            int level = _profile.Level;
            float start = w * .64f;
            int from = Progression.XpForLevel(level), to = Progression.XpForLevel(level + 1);
            GUI.Label(new Rect(start, S(7), w * .16f, S(22)), $"Poziom {level}  ·  {_profile.Xp} XP", _text);
            var track = new Rect(start, S(37), w * .12f, S(6));
            GUI.Box(track, GUIContent.none, _bar);
            track.width *= Mathf.InverseLerp(from, to, _profile.Xp);
            GUI.Box(track, GUIContent.none, _barFill);
            GUI.Label(new Rect(w * .8f, S(17), w * .12f, S(30)), $"{_profile.Money:N0} zł", _text);
            if (GUI.Button(new Rect(w - S(116), S(8), S(105), height - S(16)), "Ustawienia", _button))
            {
                if (_panel == Panel.Inventory)
                    ClosePanel();
                OpenPanel(Panel.Settings);
            }
        }

        private void DrawInventory(float w, float h)
        {
            InventoryArt();
            GUI.DrawTexture(new Rect(0, 0, w, h), _dim!);
            float gap = S(9), y = S(106), bottom = h - S(48), height = bottom - y;
            float inner = w - gap * 5f;
            var rail = new Rect(gap, y, inner * .121f, height);
            var rig = new Rect(rail.xMax + gap, y, inner * .245f, height);
            var grid = new Rect(rig.xMax + gap, y, inner * .467f, height);
            var detail = new Rect(grid.xMax + gap, y, inner * .167f, height);
            GUI.Box(rail, GUIContent.none, _inventorySurface);
            GUI.Box(rig, GUIContent.none, _inventorySurface);
            GUI.Box(grid, GUIContent.none, _inventorySurface);
            GUI.Box(detail, GUIContent.none, _inventorySurface);
            float pad = S(13);
            GUI.Label(new Rect(rail.x + pad, y + pad, rail.width - 2f * pad, S(35)), "Wyposażenie", _title);
            for (int i = -1; i < ShopTabs.Length; i++)
            {
                var row = new Rect(rail.x + pad, y + S(62 + (i + 1) * 52), rail.width - pad * 2f, S(45));
                bool active = i == _inventoryTab;
                if (GUI.Button(row, GUIContent.none, active ? _inventorySelected : _inventoryCard))
                    SelectInventoryTab(i);
                DrawGearGlyph(new Rect(row.x + S(7), row.y + S(8), S(28), S(28)), i < 0 ? 5 : i, active ? Accent : Muted);
                GUI.Label(new Rect(row.x + S(42), row.y + S(10), row.width - S(46), S(29)), i < 0 ? "Wszystko" : ShopTabs[i], _text);
            }

            GUI.Label(new Rect(rig.x + pad, y + pad, rig.width - pad * 2f, S(36)), "Twój zestaw", _title);
            var stage = new Rect(rig.x + pad, y + S(58), rig.width - pad * 2f, height * .24f);
            if (Picture(_loadedLocation) is { } scenery)
            {
                GUI.DrawTexture(stage, scenery, ScaleMode.ScaleAndCrop);
                GUI.DrawTexture(stage, _fadeUp!);
            }
            else
                GUI.Box(stage, GUIContent.none, _inventoryHero);
            var mainIcon = new Rect(stage.center.x - stage.height * .52f, stage.y, stage.height * 1.04f, stage.height);
            InventoryIcon(mainIcon, _rigDraft[0], 0);
            InventoryIcon(new Rect(mainIcon.center.x, mainIcon.center.y, stage.height * .46f, stage.height * .46f), _rigDraft[1], 1);
            float slotHeight = Mathf.Min(S(77), (bottom - stage.yMax - S(77)) / RigSlots.Length);
            for (int i = 0; i < RigSlots.Length; i++)
            {
                var row = new Rect(rig.x + pad, stage.yMax + S(11) + i * slotHeight, rig.width - pad * 2f, slotHeight - S(5));
                if (GUI.Button(row, GUIContent.none, _inventoryTab == i ? _inventorySelected : _inventoryCard))
                    SelectInventoryTab(i);
                InventoryIcon(new Rect(row.x + S(4), row.y + S(3), row.height - S(6), row.height - S(6)), _rigDraft[i], i);
                float tx = row.x + row.height + S(4);
                GUI.Label(new Rect(tx, row.y + S(3), row.xMax - tx - S(6), S(20)), RigSlots[i], _small);
                GUI.Label(new Rect(tx, row.y + S(21), row.xMax - tx - S(6), row.height - S(20)),
                    _rigDraft[i]?.DisplayName ?? "Wybierz element", _text);
            }
            bool idle = _rod.State == RodState.Idle && !_rod.Swinging;
            bool ready = _rigDraft.All(item => item != null && HasInventoryItem(item));
            GUI.enabled = ready && idle;
            _inventoryAction!.font = _fontBold;
            _inventoryAction.fontSize = _button!.fontSize;
            if (GUI.Button(new Rect(rig.x + pad, bottom - S(50), rig.width - pad * 2f, S(38)), "Zmontuj i załóż", _inventoryAction))
            {
                foreach (var item in _rigDraft)
                    SetEquippedItem(item!);
                ClosePanel();
                FrameRod();
                Say("Zestaw zmontowany.");
            }
            GUI.enabled = true;

            float tabWidth = (grid.width - pad * 2f) / (ShopTabs.Length + 1);
            for (int i = -1; i < ShopTabs.Length; i++)
            {
                var tab = new Rect(grid.x + pad + (i + 1) * tabWidth, y + S(9), tabWidth - S(3), S(66));
                bool selected = _inventoryTab == i;
                if (GUI.Button(tab, GUIContent.none, selected ? _inventorySelected : _inventoryCard))
                    SelectInventoryTab(i);
                DrawGearGlyph(new Rect(tab.center.x - S(13), tab.y + S(3), S(26), S(26)), i < 0 ? 5 : i, selected ? Accent : Muted);
                GUI.Label(new Rect(tab.x + S(4), tab.y + S(31), tab.width - S(8), S(29)), i < 0 ? "Wszystko" : ShopTabs[i], _small);
            }
            var search = new Rect(grid.x + pad, y + S(86), grid.width - pad * 2f - S(139), S(38));
            GUI.SetNextControlName("InventorySearch");
            string query = GUI.TextField(search, _inventoryQuery, 60, _field);
            if (query != _inventoryQuery)
            {
                _inventoryQuery = query;
                _scroll = Vector2.zero;
            }
            if (query.Length == 0 && GUI.GetNameOfFocusedControl() != "InventorySearch")
                GUI.Label(new Rect(search.x + S(10), search.y + S(8), search.width - S(20), S(27)), "Szukaj w ekwipunku…", _small);
            if (GUI.Button(new Rect(search.xMax + S(7), search.y, S(132), search.height),
                _inventorySortByName ? "Sortuj: nazwa" : "Sortuj: typ", _button))
            {
                _inventorySortByName = !_inventorySortByName;
                _scroll = Vector2.zero;
            }
            IEnumerable<TackleItem> items = Tackle.Rods.Cast<TackleItem>().Concat(Tackle.Reels).Concat(Tackle.Lines)
                .Concat(Tackle.Hooks).Concat(Tackle.Baits).Concat(Tackle.Accessories).Where(HasInventoryItem);
            string term = _inventoryQuery.Trim();
            items = items.Where(item => (_inventoryTab < 0 || EquipmentCategory(item) == _inventoryTab) &&
                (term.Length == 0 || item.DisplayName.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0));
            var owned = (_inventorySortByName ? items.OrderBy(item => item.DisplayName) :
                items.OrderBy(EquipmentCategory).ThenBy(item => item.DisplayName)).ToList();
            var view = new Rect(grid.x + pad, y + S(143), grid.width - pad * 2f, height - S(155));
            const int columns = 7;
            float cell = (view.width - S(18)) / columns;
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, cell * columns, Mathf.CeilToInt(owned.Count / (float)columns) * cell));
            for (int i = 0; i < owned.Count; i++)
            {
                var tile = new Rect(i % columns * cell, i / columns * cell, cell - S(6), cell - S(6));
                if (tile.yMax < _scroll.y || tile.y > _scroll.y + view.height)
                    continue;
                var item = owned[i];
                bool active = _inventoryInspect?.ItemId == item.ItemId;
                if (GUI.Button(tile, new GUIContent("", item.DisplayName), active ? _inventorySelected : _inventoryCard))
                    _inventoryInspect = item;
                InventoryIcon(new Rect(tile.x + S(5), tile.y + S(5), tile.width - S(10), tile.height - S(10)), item, EquipmentCategory(item));
                if (item is BaitData { Reusable: false })
                    GUI.Label(new Rect(tile.xMax - S(34), tile.yMax - S(21), S(32), S(19)), $"{_profile.BaitCount(item.ItemId)}", _small);
            }
            GUI.EndScrollView();
            if (owned.Count == 0)
                GUI.Label(new Rect(view.x, view.y, view.width, S(30)), "Brak pasujących przedmiotów.", _small);

            if (_inventoryInspect is { } selectedItem)
            {
                int category = EquipmentCategory(selectedItem);
                var preview = new Rect(detail.x + pad, y + pad, detail.width - 2f * pad, height * .29f);
                bool hasPreview = false;
                if (selectedItem is RodData or ReelData)
                {
                    _tacklePreview ??= new TacklePreview();
                    hasPreview = _tacklePreview.Draw(preview, selectedItem);
                }
                if (!hasPreview)
                {
                    float size = Mathf.Min(preview.width, preview.height);
                    InventoryIcon(new Rect(preview.center.x - size * .5f, preview.center.y - size * .5f, size, size), selectedItem, category);
                }
                else
                    GUI.Label(new Rect(preview.x, preview.yMax, preview.width, S(40)), "Przeciągnij, aby obrócić", _small);
                var name = new Rect(preview.x, preview.yMax + S(40), preview.width, S(106));
                GUI.Label(name, selectedItem.DisplayName, _title);
                GUI.Label(new Rect(name.x, name.yMax + S(3), name.width, S(100)), Stats(selectedItem), _text);
                if (category < _rigDraft.Length)
                {
                    bool inDraft = _rigDraft[category]?.ItemId == selectedItem.ItemId;
                    GUI.enabled = !inDraft;
                    if (GUI.Button(new Rect(detail.x + pad, bottom - S(50), detail.width - pad * 2f, S(38)),
                        inDraft ? "W zestawie" : "Dodaj do zestawu", _inventoryAction))
                        _rigDraft[category] = selectedItem;
                    GUI.enabled = true;
                }
            }
            else
                GUI.Label(new Rect(detail.x + pad, y + pad, detail.width - 2f * pad, S(62)), "Wybierz przedmiot", _text);

            string hint = !idle ? "Wyjmij zestaw z wody, aby zmienić części." :
                !ready ? "Wybierz wszystkie części i dostępną przynętę." : GUI.tooltip;
            GUI.Label(new Rect(rail.x + pad, bottom + S(9), w - pad * 2f, S(26)), hint, _text);
            DrawWorkshopNav(w);
        }

        private TackleItem? _shopSelection;

        private void DrawShop(float w, float h)
        {
            InventoryArt();
            GUI.DrawTexture(new Rect(0, 0, w, h), _dim!);
            float top = S(36), gap = S(11), pad = S(14);
            var rail = new Rect(gap, top, w * .14f, h - top - gap);
            var detail = new Rect(w * .77f, top, w * .23f - gap, rail.height);
            var catalogue = new Rect(rail.xMax + gap, top, detail.x - gap - rail.xMax - gap, rail.height);
            foreach (var panel in new[] { rail, catalogue, detail })
                GUI.Box(panel, GUIContent.none, _inventorySurface);
            GUI.Label(new Rect(rail.x + pad, top + pad, rail.width - pad * 2f, S(44)), "Sklep", _title);
            var r = new Rect(catalogue.x + pad, catalogue.y + pad, catalogue.width - pad * 2f, catalogue.height - pad * 2f);
            GUI.Label(new Rect(r.x, r.y, r.width, S(45)), "Sprzęt wędkarski", _title);
            GUI.Label(new Rect(r.x, r.y + S(53), r.width, S(31)), $"Portfel  ·  {_profile.Money:N0} zł", _text);
            for (int i = 0; i < ShopTabs.Length; i++)
            {
                var row = new Rect(rail.x + pad, top + S(65 + i * 52), rail.width - pad * 2f, S(44));
                bool active = i == _shopTab;
                if (GUI.Button(row, GUIContent.none, active ? _inventorySelected : _inventoryCard))
                {
                    _shopTab = i;
                    _shopFilter = 0;
                    _scroll = Vector2.zero;
                }
                DrawGearGlyph(new Rect(row.x + S(6), row.y + S(8), S(29), S(29)), i, active ? Accent : Muted);
                GUI.Label(new Rect(row.x + S(40), row.y + S(10), row.width - S(44), S(28)), ShopTabs[i], _small);
            }
            if (GUI.Button(new Rect(rail.x + pad, rail.yMax - S(58), rail.width - pad * 2f, S(42)), "Skup ryb", _button))
            {
                ClosePanel();
                OpenPanel(Panel.Keepnet);
            }
            IEnumerable<TackleItem> items = _shopTab switch
            {
                0 => Tackle.Rods,
                1 => Tackle.Reels,
                2 => Tackle.Lines,
                3 => Tackle.Hooks,
                4 => Tackle.Baits,
                _ => Tackle.Accessories,
            };
            // the catalogue is long (81 rods, 87 reels): a search, a filter by kind or size and the order by level and price
            var filters = _shopTab switch
            {
                0 => new[] { "Wszystkie", "Spławikowe", "Spinningowe", "Gruntowe", "Karpiowe" },
                1 => new[] { "Wszystkie", "Do 2500", "3000–5000", "Od 6000" },
                _ => new[] { "Wszystkie" },
            };
            float fx = r.x;
            for (int i = 0; i < filters.Length; i++)
            {
                float bw = Mathf.Min(S(132), r.width / filters.Length - S(6));
                if (GUI.Button(new Rect(fx, r.y + S(104), bw, S(34)), filters[i], i == _shopFilter ? _tabOn : _tab))
                {
                    _shopFilter = i;
                    _scroll = Vector2.zero;
                }
                fx += bw + S(6);
            }
            var search = new Rect(r.x, r.y + S(150), r.width, S(39));
            GUI.SetNextControlName("ShopSearch");
            string input = GUI.TextField(search, _shopSearch, 40, _field);
            if (input != _shopSearch)
            {
                _shopSearch = input;
                _scroll = Vector2.zero;
            }
            if (input.Length == 0 && GUI.GetNameOfFocusedControl() != "ShopSearch")
                GUI.Label(new Rect(search.x + S(12), search.y + S(8), search.width - S(24), S(28)), "Szukaj w sklepie…", _small);
            string query = _shopSearch.Trim().ToLowerInvariant();
            int sizeOf(TackleItem it)
            {
                var m = System.Text.RegularExpressions.Regex.Match(it.DisplayName, @"\b(\d{4,5})\b");
                return m.Success ? int.Parse(m.Groups[1].Value) : 0;
            }
            bool fits(TackleItem it) => _shopFilter == 0 || it switch
            {
                RodData rd => (int)rd.Kind == (_shopFilter == 1 ? (int)RodKind.Float : _shopFilter == 2 ? (int)RodKind.Spinning : _shopFilter == 3 ? (int)RodKind.Bottom : (int)RodKind.Carp),
                ReelData => _shopFilter == 1 ? sizeOf(it) <= 2500 : _shopFilter == 2 ? sizeOf(it) is >= 3000 and <= 5000 : sizeOf(it) >= 6000,
                _ => true,
            };
            var list = items.Where(it => fits(it) && (query.Length == 0 || it.DisplayName.ToLowerInvariant().Contains(query)))
                .OrderBy(it => it.RequiredLevel).ThenBy(it => it.Price).ToList();

            if (_shopSelection == null || !list.Contains(_shopSelection))
                _shopSelection = list.FirstOrDefault();
            var view = new Rect(r.x, r.y + S(206), r.width, Mathf.Max(1f, r.height - S(206)));
            int columns = Mathf.Clamp(Mathf.FloorToInt(view.width / S(145)), 2, 7);
            float tileWidth = (view.width - S(28)) / columns, tileHeight = S(141);
            var tileName = new GUIStyle(_small!) { clipping = TextClipping.Clip, normal = { textColor = Ink } };
            var content = new Rect(0, 0, tileWidth * columns, Mathf.CeilToInt(list.Count / (float)columns) * tileHeight);
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            float level = _profile.Level;
            for (int i = 0; i < list.Count; i++)
            {
                var tile = new Rect(i % columns * tileWidth, i / columns * tileHeight, tileWidth - S(6), tileHeight - S(6));
                if (tile.yMax < _scroll.y || tile.y > _scroll.y + view.height)
                    continue;
                var item = list[i];
                if (GUI.Button(tile, new GUIContent("", item.DisplayName), item == _shopSelection ? _inventorySelected : _inventoryCard))
                    _shopSelection = item;
                float icon = S(58);
                InventoryIcon(new Rect(tile.center.x - icon * .5f, tile.y + S(5), icon, icon), item, _shopTab);
                GUI.Label(new Rect(tile.x + S(8), tile.y + S(65), tile.width - S(16), S(38)), Shorten(item.DisplayName, 34), tileName);
                string price = item.RequiredLevel > level ? $"Poziom {item.RequiredLevel}" : HasInventoryItem(item) && item is not BaitData { Reusable: false }
                    ? "Posiadane" : $"{item.Price:N0} zł";
                GUI.Label(new Rect(tile.x + S(8), tile.yMax - S(24), tile.width - S(16), S(23)), price, _label);
            }
            GUI.EndScrollView();

            if (_shopSelection is not { } selected)
            {
                GUI.Label(new Rect(detail.x + pad, detail.y + pad, detail.width - pad * 2f, S(50)), "Brak pasujących przedmiotów.", _text);
                return;
            }
            var display = new Rect(detail.x + pad, detail.y + pad, detail.width - pad * 2f, detail.height * .30f);
            bool model = false;
            if (selected is RodData or ReelData)
            {
                _tacklePreview ??= new TacklePreview();
                model = _tacklePreview.Draw(display, selected);
            }
            if (!model)
            {
                float size = Mathf.Min(display.width, display.height);
                InventoryIcon(new Rect(display.center.x - size * .5f, display.center.y - size * .5f, size, size), selected, _shopTab);
            }
            else
                GUI.Label(new Rect(display.x, display.yMax, display.width, S(24)), "Przeciągnij, aby obrócić", _small);
            GUI.Label(new Rect(display.x, display.yMax + S(29), display.width, S(92)), selected.DisplayName, _title);
            GUI.Label(new Rect(display.x, display.yMax + S(126), display.width, S(95)), Stats(selected), _text);
            DrawShopActions(selected, detail, pad);
        }

        private void DrawShopActions(TackleItem item, Rect panel, float pad)
        {
            string id = item.ItemId;
            var bait = item as BaitData;
            bool consumable = bait is { Reusable: false };
            bool owned = HasInventoryItem(item);
            bool locked = item.RequiredLevel > _profile.Level;
            bool idle = _rod.State == RodState.Idle && !_rod.Swinging;
            bool equipped = id == _profile.Rod || id == _profile.Reel || id == _profile.Line || id == _profile.Hook || id == _profile.Bait;
            var buy = new Rect(panel.x + pad, panel.yMax - S(107), panel.width - pad * 2f, S(42));
            string purchase = locked ? $"Od poziomu {item.RequiredLevel}" :
                owned && !consumable ? "Posiadane" : consumable ? $"Kup ×{bait!.PackSize} · {item.Price:N0} zł" : $"Kup · {item.Price:N0} zł";
            GUI.enabled = idle && !locked && (!owned || consumable) && _profile.Money >= item.Price;
            if (GUI.Button(buy, purchase, _accentButton))
            {
                _profile.Money -= item.Price;
                if (consumable)
                    _profile.Baits[id] = _profile.BaitCount(id) + bait!.PackSize;
                else
                {
                    _profile.Owned.Add(id);
                    if (bait != null)
                        _profile.Baits[id] = 1;
                    if (item is not AccessoryData)
                        Equip(item);
                }
                _sound.Play(Core.Audio.OneShot.Coins);
                _profile.Save();
            }
            if (item is not AccessoryData)
            {
                GUI.enabled = idle && owned && !equipped;
                if (GUI.Button(new Rect(buy.x, buy.yMax + S(8), buy.width, buy.height),
                    equipped ? "W użyciu" : consumable ? $"Załóż · {_profile.BaitCount(id)} szt." : "Załóż", _button))
                {
                    if (bait is { Reusable: true } && !_profile.Baits.ContainsKey(id))
                        _profile.Baits[id] = 1;
                    Equip(item);
                    _profile.Save();
                }
            }
            GUI.enabled = true;
        }

        private void Equip(TackleItem item)
        {
            SetEquippedItem(item);
            _rod.ApplyTackle();
            FrameRod();   // a longer or shorter rod: its tip back on the line of sight
        }

        private void SetEquippedItem(TackleItem item)
        {
            switch (item)
            {
                case RodData: _profile.Rod = item.ItemId; break;
                case ReelData: _profile.Reel = item.ItemId; break;
                case LineData: _profile.Line = item.ItemId; break;
                case HookData: _profile.Hook = item.ItemId; break;
                case BaitData: _profile.Bait = item.ItemId; break;
            }
        }

        private static string Stats(TackleItem item) => item switch
        {
            RodData r => $"{KindName(r.Kind)} · {(r.Length > 0 ? r.Length : 3.58f):0.00} m · udźwig {r.MaxLoad:0.0} kg · rzut ×{r.CastMult:0.00}",
            ReelData r => $"hamulec do {r.DragMax:0.0} kg · {r.ReelSpeed * 0.24f * 100f:0} cm żyłki na obrót korby · {r.LineCapacity:0} m żyłki",
            LineData l => $"{l.Diameter:0.00} mm · wytrzymałość {l.Strength:0.0} kg · widoczność {l.Visibility:0.00}",
            HookData k => $"{k.DisplayName} · wytrzymałość {k.Strength:0.0} kg · {k.WeightG:0.00} g",
            AccessoryData a => a.Description.Length > 90 ? a.Description.Substring(0, 90) + "…" : a.Description,
            BaitData b => b.Reusable ? $"przynęta sztuczna · {b.SizeCm:0.#} cm · {b.WeightG:0.#} g" : $"{BaitKindName(b.Kind)} · paczka {b.PackSize} szt.",
            _ => "",
        };

        private static string KindName(RodKind k) => k switch
        {
            RodKind.Float => "spławikowa",
            RodKind.Spinning => "spinningowa",
            RodKind.Bottom => "gruntowa",
            _ => "karpiowa",
        };

        private static string BaitKindName(BaitKind k) => k switch
        {
            BaitKind.Natural => "przynęta naturalna",
            BaitKind.Plant => "przynęta roślinna",
            BaitKind.Carp => "kulki proteinowe",
            _ => "przynęta",
        };
    }
}
