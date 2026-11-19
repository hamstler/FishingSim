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

        private enum Screen { Menu, Loading, Play }
        private enum Panel { None, Pause, Shop, Settings, Help, Keepnet, Records }

        private Screen _screen = Screen.Menu;
        private Panel _panel = Panel.None;
        private Profile _profile = null!;
        private WorldView _world = null!;
        private Player _player = null!;
        private Fishing _rod = null!;
        private DayCycle _day = null!;
        private readonly List<LocationData> _locations = new();
        private int _loadedLocation = -1;
        private string _message = "";
        private float _messageUntil;
        private string _error = "";
        private int _shopTab;
        private Vector2 _scroll;
        private float _menuOrbit;
        private Panel _settingsBack = Panel.None;
        private float _autosave;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<GameRoot>() == null)
                new GameObject("Game").AddComponent<GameRoot>();
        }

        private void Awake()
        {
            DataStore.Root = Path.Combine(Application.streamingAssetsPath, "data");
            bool noSave = Environment.GetEnvironmentVariable("FISHSIM_NO_SAVE") == "1";
            Profile.SavePath = noSave ? null : Path.Combine(Application.persistentDataPath, "profile.json");
            Application.logMessageReceived += OnLog;
            Settings.Load();
            _profile = Profile.Load();
            foreach (var path in Lake.LocationPaths)
                if (DataStore.Load<LocationData>(path) is { } loc)
                    _locations.Add(loc);
            if (_locations.Count == 0)
                _error = "Brak danych gry (StreamingAssets/data). Zainstaluj grę ponownie.";

            _day = new GameObject("Day").AddComponent<DayCycle>();
            _day.Hour = _profile.Hour;
            _world = new GameObject("World").AddComponent<WorldView>();
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
            Settings.Apply(_player.Camera);
            _player.Frozen = true;
            _rod.Blocked = true;
            if (_locations.Count > 0)
                StartCoroutine(LoadMap(Mathf.Clamp(_profile.Location, 0, _locations.Count - 1), false));
        }

        private void OnDestroy() => Application.logMessageReceived -= OnLog;

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

        private IEnumerator LoadMap(int index, bool play)
        {
            _screen = Screen.Loading;
            _rod.Reset();
            yield return null;
            if (_loadedLocation != index)
            {
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
                        yield break;
                    }
                    yield return step;
                }
                _loadedLocation = index;
                _day.Water = _world.WaterMaterial;
                GC.Collect();
            }
            _profile.Location = index;
            _player.Place(new Vector3(0f, WorldView.PierDeck, 4.5f), 180f);
            _day.Apply();
            _screen = play ? Screen.Play : Screen.Menu;
            if (play)
                EnterPlay();
        }

        private void EnterPlay()
        {
            _screen = Screen.Play;
            _panel = Panel.None;
            SetCursor(true);
            if (_profile.ClaimFreeKit())
                Say("Sklep dał ci darmowy zestaw startowy.");
        }

        private void SetCursor(bool captured)
        {
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
            _profile.Hour = _day.Hour;
            if (_screen == Screen.Menu && _world.Ready)
            {
                // the menu shows the lake from a slowly circling camera
                _menuOrbit += Time.deltaTime * 0.02f;
                var cam = _player.Camera.transform;
                cam.position = new Vector3(Mathf.Sin(_menuOrbit) * 70f, 22f, -75f + Mathf.Cos(_menuOrbit) * 95f);
                cam.LookAt(new Vector3(0f, 2f, -60f));
            }
            if (_screen != Screen.Play)
                return;
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

        private void OpenPanel(Panel p)
        {
            _panel = p;
            _scroll = Vector2.zero;
            SetCursor(false);
        }

        private void ClosePanel()
        {
            _panel = Panel.None;
            SetCursor(true);
            _rod.ApplyTackle();
            _profile.Save();
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
        private GUIStyle? _panelStyle, _title, _text, _small, _button, _accentButton, _bar, _barFill, _chip;
        private Texture2D? _dim;
        private float _scale = 1f;

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void Styles()
        {
            _scale = Mathf.Clamp(UnityEngine.Screen.height / 1080f, 0.7f, 2f);
            if (_panelStyle != null && Mathf.Approximately(_panelStyle.fontSize, Mathf.RoundToInt(20 * _scale)))
                return;
            int F(int size) => Mathf.RoundToInt(size * _scale);
            var text = new Color(0.9f, 0.92f, 0.94f);
            var amber = new Color(0.95f, 0.72f, 0.3f);
            _dim = Tex(new Color(0f, 0f, 0f, 0.45f));
            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = Tex(new Color(0.07f, 0.1f, 0.11f, 0.86f)), textColor = text },
                padding = new RectOffset(F(18), F(18), F(14), F(14)),
                fontSize = F(20),
                alignment = TextAnchor.UpperLeft,
            };
            _title = new GUIStyle(GUI.skin.label) { fontSize = F(34), fontStyle = FontStyle.Bold, normal = { textColor = amber }, wordWrap = true };
            _text = new GUIStyle(GUI.skin.label) { fontSize = F(19), normal = { textColor = text }, wordWrap = true, richText = true };
            _small = new GUIStyle(_text) { fontSize = F(15), normal = { textColor = new Color(0.72f, 0.76f, 0.78f) } };
            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = F(19),
                normal = { background = Tex(new Color(0.16f, 0.21f, 0.23f, 0.95f)), textColor = text },
                hover = { background = Tex(new Color(0.22f, 0.29f, 0.31f, 1f)), textColor = Color.white },
                active = { background = Tex(new Color(0.1f, 0.14f, 0.15f, 1f)), textColor = amber },
                padding = new RectOffset(F(12), F(12), F(8), F(8)),
            };
            _accentButton = new GUIStyle(_button)
            {
                normal = { background = Tex(new Color(0.78f, 0.52f, 0.16f, 1f)), textColor = new Color(0.08f, 0.06f, 0.03f) },
                hover = { background = Tex(new Color(0.9f, 0.62f, 0.2f, 1f)), textColor = Color.black },
                fontStyle = FontStyle.Bold,
            };
            _chip = new GUIStyle(_button) { fontSize = F(16), padding = new RectOffset(F(10), F(10), F(5), F(5)) };
            _bar = new GUIStyle { normal = { background = Tex(new Color(1f, 1f, 1f, 0.18f)) } };
            _barFill = new GUIStyle { normal = { background = Tex(Color.white) } };
        }

        private float S(float v) => v * _scale;

        private void OnGUI()
        {
            Styles();
            float w = UnityEngine.Screen.width, h = UnityEngine.Screen.height;
            if (_error.Length > 0)
            {
                GUI.Box(new Rect(w * 0.25f, h * 0.35f, w * 0.5f, h * 0.3f), "", _panelStyle);
                GUI.Label(new Rect(w * 0.27f, h * 0.37f, w * 0.46f, h * 0.2f), _error, _text);
                if (GUI.Button(new Rect(w * 0.42f, h * 0.57f, w * 0.16f, S(44)), "Zamknij grę", _button))
                    Application.Quit();
                return;
            }
            switch (_screen)
            {
                case Screen.Loading:
                    DrawLoading(w, h);
                    break;
                case Screen.Menu:
                    if (_panel == Panel.Settings)
                        DrawSettings(w, h);
                    else
                        DrawMenu(w, h);
                    break;
                case Screen.Play:
                    DrawHud(w, h);
                    if (_rod.State == RodState.Landed)
                        DrawCatch(w, h);
                    switch (_panel)
                    {
                        case Panel.Pause: DrawPause(w, h); break;
                        case Panel.Shop: DrawShop(w, h); break;
                        case Panel.Settings: DrawSettings(w, h); break;
                        case Panel.Help: DrawHelp(w, h); break;
                        case Panel.Keepnet: DrawKeepnet(w, h); break;
                        case Panel.Records: DrawRecords(w, h); break;
                    }
                    break;
            }
        }

        private void DrawLoading(float w, float h)
        {
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.blackTexture);
            string name = _locations.Count > 0 ? _locations[Mathf.Clamp(_profile.Location, 0, _locations.Count - 1)].DisplayName : "";
            GUI.Label(new Rect(w * 0.3f, h * 0.42f, w * 0.4f, S(50)), "Fishing Sim", _title);
            GUI.Label(new Rect(w * 0.3f, h * 0.42f + S(52), w * 0.4f, S(30)), $"Wczytywanie: {name}…", _text);
            Bar(new Rect(w * 0.3f, h * 0.42f + S(92), w * 0.4f, S(10)), _world.Progress, new Color(0.95f, 0.72f, 0.3f));
        }

        private void Bar(Rect r, float value, Color colour)
        {
            GUI.Box(r, GUIContent.none, _bar);
            var old = GUI.color;
            GUI.color = colour;
            GUI.Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), GUIContent.none, _barFill);
            GUI.color = old;
        }

        private void DrawMenu(float w, float h)
        {
            float pw = S(520), x = S(70), y = h * 0.16f;
            GUI.Box(new Rect(x - S(20), y - S(20), pw + S(40), h * 0.72f), "", _panelStyle);
            GUI.Label(new Rect(x, y, pw, S(50)), "Fishing Sim", _title);
            GUI.Label(new Rect(x, y + S(50), pw, S(26)), $"wersja {Version} · Unity", _small);
            y += S(100);
            GUI.Label(new Rect(x, y, pw, S(30)), $"Poziom {_profile.Level} · {_profile.Money} zł · złowione: {_profile.Caught}", _text);
            y += S(44);
            GUI.Label(new Rect(x, y, pw, S(28)), "Łowiska:", _small);
            y += S(30);
            for (int i = 0; i < _locations.Count; i++)
            {
                var loc = _locations[i];
                bool locked = loc.RequiredLevel > _profile.Level;
                string label = locked ? $"{loc.DisplayName}  (od poziomu {loc.RequiredLevel})" : loc.DisplayName;
                GUI.enabled = !locked;
                if (GUI.Button(new Rect(x, y, pw, S(46)), label, i == _profile.Location ? _accentButton : _button))
                {
                    int index = i;
                    StartCoroutine(LoadMap(index, true));
                }
                GUI.enabled = true;
                y += S(54);
            }
            y += S(16);
            if (GUI.Button(new Rect(x, y, pw * 0.48f, S(44)), "Ustawienia", _button))
            {
                _settingsBack = Panel.None;
                _panel = Panel.Settings;
            }
            if (GUI.Button(new Rect(x + pw * 0.52f, y, pw * 0.48f, S(44)), "Wyjdź", _button))
            {
                _profile.Save();
                Application.Quit();
            }
            y += S(60);
            GUI.Label(new Rect(x, y, pw, S(60)), "Wybierz łowisko, żeby zacząć. F1 w grze pokazuje sterowanie.", _small);
        }

        private Rect Centre(float w, float h, float pw, float ph)
        {
            GUI.DrawTexture(new Rect(0, 0, w, h), _dim!);
            var r = new Rect((w - pw) / 2f, (h - ph) / 2f, pw, ph);
            GUI.Box(r, "", _panelStyle);
            return new Rect(r.x + S(22), r.y + S(18), r.width - S(44), r.height - S(36));
        }

        private void DrawPause(float w, float h)
        {
            var r = Centre(w, h, S(460), S(520));
            GUI.Label(new Rect(r.x, r.y, r.width, S(46)), "Pauza", _title);
            float y = r.y + S(70);
            string[] labels = { "Wróć do gry", "Sklep wędkarski (T)", "Siatka z rybami (K)", "Rekordy (J)", "Ustawienia", "Zapisz i wyjdź do menu" };
            for (int i = 0; i < labels.Length; i++)
            {
                if (GUI.Button(new Rect(r.x, y, r.width, S(48)), labels[i], i == 0 ? _accentButton : _button))
                {
                    switch (i)
                    {
                        case 0: ClosePanel(); break;
                        case 1:
                            if (_rod.State == RodState.Idle) _panel = Panel.Shop;
                            else Say("Najpierw wyjmij zestaw z wody.");
                            break;
                        case 2: _panel = Panel.Keepnet; break;
                        case 3: _panel = Panel.Records; break;
                        case 4: _settingsBack = Panel.Pause; _panel = Panel.Settings; break;
                        case 5:
                            _rod.Reset();
                            _profile.Save();
                            _panel = Panel.None;
                            _screen = Screen.Menu;
                            SetCursor(false);
                            _player.Frozen = true;
                            _rod.Blocked = true;
                            break;
                    }
                }
                y += S(58);
            }
        }

        private void DrawSettings(float w, float h)
        {
            var r = Centre(w, h, S(640), S(560));
            GUI.Label(new Rect(r.x, r.y, r.width, S(46)), "Ustawienia", _title);
            float y = r.y + S(70);
            Row(ref y, r, "Jakość grafiki", Settings.QualityNames[Settings.Quality], d => Settings.Quality = Mathf.Clamp(Settings.Quality + d, 0, 4));
            Row(ref y, r, "Zasięg widzenia", Settings.ViewNames[Settings.View], d => Settings.View = Mathf.Clamp(Settings.View + d, 0, 2));
            Row(ref y, r, "Limit FPS", Settings.FpsLimits[Settings.Fps] == 0 ? "bez limitu" : Settings.FpsLimits[Settings.Fps].ToString(), d => Settings.Fps = Mathf.Clamp(Settings.Fps + d, 0, Settings.FpsLimits.Length - 1));
            Row(ref y, r, "Synchronizacja pionowa", Settings.VSync ? "włączona" : "wyłączona", _ => Settings.VSync = !Settings.VSync);
            Row(ref y, r, "Pełny ekran", Settings.Fullscreen ? "tak" : "nie (okno)", _ => Settings.Fullscreen = !Settings.Fullscreen);
            Row(ref y, r, "Czułość myszy", Settings.Mouse.ToString("0.0"), d => Settings.Mouse = Mathf.Clamp(Settings.Mouse + d * 0.2f, 0.4f, 6f));
            y += S(16);
            if (GUI.Button(new Rect(r.x, y, r.width, S(48)), "Gotowe", _accentButton))
            {
                Settings.Save();
                Settings.Apply(_player.Camera);
                _player.Sensitivity = Settings.Mouse;
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
            GUI.Label(new Rect(r.x, y + S(8), r.width * 0.5f, S(32)), label, _text);
            if (GUI.Button(new Rect(r.x + r.width * 0.5f, y, S(44), S(44)), "◀", _button))
                change(-1);
            GUI.Label(new Rect(r.x + r.width * 0.5f + S(52), y + S(8), r.width * 0.5f - S(110), S(32)), value, _text);
            if (GUI.Button(new Rect(r.x + r.width - S(44), y, S(44), S(44)), "▶", _button))
                change(1);
            y += S(56);
        }

        private void DrawHelp(float w, float h)
        {
            var r = Centre(w, h, S(720), S(620));
            GUI.Label(new Rect(r.x, r.y, r.width, S(46)), "Sterowanie", _title);
            GUI.Label(new Rect(r.x, r.y + S(64), r.width, r.height - S(130)),
                "<b>W A S D</b> – chodzenie, <b>Shift</b> – bieg, <b>mysz</b> – rozglądanie\n" +
                "<b>LPM (przytrzymaj)</b> – siła rzutu, puść, żeby zarzucić\n" +
                "<b>LPM</b> w wodzie – zwijanie zestawu (przynęta pracuje, spławik płynie do brzegu)\n" +
                "<b>PPM</b> – zacięcie, gdy spławik zanurkuje / szczytówka się ugnie\n" +
                "W holu: <b>LPM</b> – zwijanie, <b>PPM</b> – podnoszenie wędki, <b>kółko</b> – hamulec\n" +
                "Złowiona ryba: <b>E</b> – do siatki, <b>R</b> – wypuść\n" +
                "<b>T</b> – sklep, <b>K</b> – siatka, <b>J</b> – rekordy, <b>Esc</b> – pauza\n\n" +
                "Za mocny hamulec zrywa żyłkę, za słaby pozwala rybie odejść z całą żyłką. Wybieraj haczyk do ryby: duże ryby rzadko biorą na małe haczyki, małe nie połkną dużych.",
                _text);
            if (GUI.Button(new Rect(r.x, r.yMax - S(50), r.width, S(48)), "Zamknij", _accentButton))
                ClosePanel();
        }

        private void DrawHud(float w, float h)
        {
            // clock, weather, place (top right)
            int hh = Mathf.FloorToInt(_day.Hour), mm = Mathf.FloorToInt((_day.Hour - hh) * 60f);
            string weather = _day.Weather switch { Weather.Clear => "pogodnie", Weather.Cloudy => "pochmurno", _ => "deszcz" };
            var place = _world.Lake?.Location.DisplayName ?? "";
            var box = new Rect(w - S(300), S(16), S(284), S(108));
            GUI.Box(box, "", _panelStyle);
            GUI.Label(new Rect(box.x + S(14), box.y + S(8), box.width, S(36)), $"<b><size={(int)S(30)}>{hh:00}:{mm:00}</size></b>  {weather}", _text);
            GUI.Label(new Rect(box.x + S(14), box.y + S(46), box.width, S(26)), place, _small);
            GUI.Label(new Rect(box.x + S(14), box.y + S(70), box.width, S(26)), $"Poz. {_profile.Level} · {_profile.Money} zł · siatka {_profile.Keepnet.Count}", _small);

            // crosshair dot
            if (_panel == Panel.None && _rod.State != RodState.Landed)
                GUI.Label(new Rect(w / 2f - S(4), h / 2f - S(12), S(12), S(24)), "·", _text);

            // tackle line and the load bar (bottom)
            var rod = Tackle.Rod(_profile.Rod);
            var bait = Tackle.Bait(_profile.Bait);
            string baitText = bait == null ? "brak przynęty" : bait.Reusable ? bait.DisplayName : $"{bait.DisplayName} ×{_profile.BaitCount(_profile.Bait)}";
            GUI.Label(new Rect(S(20), h - S(40), w * 0.6f, S(30)),
                $"{rod?.DisplayName ?? "brak wędki"} · {Tackle.Hook(_profile.Hook)?.DisplayName ?? "brak haczyka"} · {baitText}", _small);
            var bar = new Rect(w / 2f - S(240), h - S(78), S(480), S(14));
            switch (_rod.State)
            {
                case RodState.Charging:
                    Bar(bar, _rod.CastPower, new Color(0.95f, 0.72f, 0.3f));
                    break;
                case RodState.Fight when _rod.Fight is { } f:
                    float load = f.Tension / Mathf.Max(_rod.EffectiveLimit, 0.1f);
                    Bar(bar, load, Color.Lerp(new Color(0.35f, 0.8f, 0.4f), new Color(0.95f, 0.2f, 0.15f), Mathf.Clamp01((load - 0.4f) / 0.6f)));
                    GUI.Label(new Rect(bar.x, bar.y - S(30), bar.width, S(28)),
                        $"Żyłka {f.LineOut:0} m · hamulec {_rod.DragSetting * 100f:0}% · siła ryby {f.Stamina * 100f:0}%", _small);
                    break;
                case RodState.Waiting:
                case RodState.Nibble:
                case RodState.Bite:
                    GUI.Label(new Rect(bar.x, bar.y - S(4), bar.width, S(28)),
                        $"{_rod.Distance:0} m · głębokość {_rod.DepthHere:0.0} m · hamulec {_rod.DragSetting * 100f:0}%", _small);
                    break;
                case RodState.Idle:
                    GUI.Label(new Rect(bar.x, bar.y - S(4), bar.width, S(28)), $"Hamulec {_rod.DragSetting * 100f:0}% (kółko myszy) · F1 – sterowanie", _small);
                    break;
            }
            if (Time.unscaledTime < _messageUntil)
            {
                var size = _text!.CalcSize(new GUIContent(_message));
                float mw = Mathf.Min(size.x + S(40), w * 0.8f);
                var mr = new Rect((w - mw) / 2f, h * 0.2f, mw, S(48));
                GUI.Box(mr, "", _panelStyle);
                GUI.Label(new Rect(mr.x + S(20), mr.y + S(10), mr.width - S(40), S(30)), _message, _text);
            }
        }

        private void DrawCatch(float w, float h)
        {
            var sp = _rod.Species;
            if (sp == null)
                return;
            var r = Centre(w, h, S(620), S(400));
            GUI.Label(new Rect(r.x, r.y, r.width, S(46)), sp.SpeciesName, _title);
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
            GUI.Label(new Rect(r.x, r.y, r.width, S(46)), "Siatka", _title);
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
            GUI.Label(new Rect(r.x, r.y, r.width, S(46)), $"Rekordy ({_profile.Records.Count}/{Tackle.AllSpecies.Count} gatunków)", _title);
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

        // ------------------------------------------------------------------ shop
        private static readonly string[] ShopTabs = { "Wędki", "Kołowrotki", "Żyłki", "Haczyki", "Przynęty" };

        private void DrawShop(float w, float h)
        {
            var r = Centre(w, h, Mathf.Min(S(1100), w * 0.92f), Mathf.Min(S(820), h * 0.92f));
            GUI.Label(new Rect(r.x, r.y, r.width * 0.6f, S(46)), "Sklep wędkarski", _title);
            GUI.Label(new Rect(r.x + r.width * 0.6f, r.y + S(10), r.width * 0.4f, S(30)), $"Portfel: <b>{_profile.Money} zł</b> · poziom {_profile.Level}", _text);
            float x = r.x;
            for (int i = 0; i < ShopTabs.Length; i++)
            {
                float bw = S(150);
                if (GUI.Button(new Rect(x, r.y + S(56), bw, S(40)), ShopTabs[i], i == _shopTab ? _accentButton : _chip))
                {
                    _shopTab = i;
                    _scroll = Vector2.zero;
                }
                x += bw + S(8);
            }
            IEnumerable<TackleItem> items = _shopTab switch
            {
                0 => Tackle.Rods,
                1 => Tackle.Reels,
                2 => Tackle.Lines,
                3 => Tackle.Hooks,
                _ => Tackle.Baits,
            };
            var list = items.ToList();
            var view = new Rect(r.x, r.y + S(110), r.width, r.height - S(176));
            float rowH = S(66);
            var content = new Rect(0, 0, r.width - S(24), list.Count * rowH);
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var row = new Rect(0, i * rowH, content.width, rowH - S(6));
                if (i % 2 == 0)
                    GUI.Box(row, GUIContent.none, _bar);
                string id = item.ItemId;
                bool owned = _profile.Owned.Contains(id);
                bool equipped = id == _profile.Rod || id == _profile.Reel || id == _profile.Line || id == _profile.Hook || id == _profile.Bait;
                GUI.Label(new Rect(row.x + S(10), row.y + S(4), row.width * 0.62f, S(30)), item.DisplayName, _text);
                GUI.Label(new Rect(row.x + S(10), row.y + S(32), row.width * 0.62f, S(26)), Stats(item), _small);
                bool locked = item.RequiredLevel > _profile.Level;
                var buy = new Rect(row.xMax - S(300), row.y + S(10), S(170), S(42));
                var use = new Rect(row.xMax - S(122), row.y + S(10), S(112), S(42));
                if (item is BaitData bait && !bait.Reusable)
                {
                    GUI.enabled = !locked && _profile.Money >= item.Price;
                    if (GUI.Button(buy, locked ? $"poziom {item.RequiredLevel}" : $"{item.Price} zł (×{bait.PackSize})", _button))
                    {
                        _profile.Money -= item.Price;
                        _profile.Baits[id] = _profile.BaitCount(id) + bait.PackSize;
                    }
                    GUI.enabled = _profile.BaitCount(id) > 0 && !equipped;
                    if (GUI.Button(use, equipped ? "w użyciu" : $"użyj ({_profile.BaitCount(id)})", equipped ? _accentButton : _button))
                        _profile.Bait = id;
                    GUI.enabled = true;
                    continue;
                }
                if (item is BaitData lure && owned && !_profile.Baits.ContainsKey(id))
                    _profile.Baits[id] = 1;
                GUI.enabled = !owned && !locked && _profile.Money >= item.Price;
                if (GUI.Button(buy, owned ? "kupione" : locked ? $"poziom {item.RequiredLevel}" : $"{item.Price} zł", _button))
                {
                    _profile.Money -= item.Price;
                    _profile.Owned.Add(id);
                    if (item is BaitData)
                        _profile.Baits[id] = 1;
                    Equip(item);
                }
                GUI.enabled = owned && !equipped;
                if (GUI.Button(use, equipped ? "w użyciu" : "użyj", equipped ? _accentButton : _button))
                    Equip(item);
                GUI.enabled = true;
            }
            GUI.EndScrollView();
            if (GUI.Button(new Rect(r.x, r.yMax - S(50), r.width * 0.48f, S(48)), "Zamknij", _accentButton))
                ClosePanel();
            if (GUI.Button(new Rect(r.x + r.width * 0.52f, r.yMax - S(50), r.width * 0.48f, S(48)), $"Skup ryb (siatka: {_profile.KeepnetValue()} zł)", _button))
                _panel = Panel.Keepnet;
        }

        private void Equip(TackleItem item)
        {
            switch (item)
            {
                case RodData: _profile.Rod = item.ItemId; break;
                case ReelData: _profile.Reel = item.ItemId; break;
                case LineData: _profile.Line = item.ItemId; break;
                case HookData: _profile.Hook = item.ItemId; break;
                case BaitData: _profile.Bait = item.ItemId; break;
            }
            _rod.ApplyTackle();
        }

        private static string Stats(TackleItem item) => item switch
        {
            RodData r => $"{KindName(r.Kind)} · {(r.Length > 0 ? r.Length : 3.58f):0.00} m · udźwig {r.MaxLoad:0.0} kg · rzut ×{r.CastMult:0.00}",
            ReelData r => $"hamulec do {r.DragMax:0.0} kg · zwijanie {r.ReelSpeed:0.0} m/s · {r.LineCapacity:0} m żyłki",
            LineData l => $"{l.Diameter:0.00} mm · wytrzymałość {l.Strength:0.0} kg · widoczność {l.Visibility:0.00}",
            HookData k => $"{k.DisplayName} · wytrzymałość {k.Strength:0.0} kg · {k.WeightG:0.00} g",
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
