using System;
using System.Collections.Generic;
using Godot;

namespace FishingSim;

/// <summary>Everything the angler can do with a key that can be rebound (the mouse, 1-9 and Esc stay fixed).</summary>
public enum Act
{
    Forward, Back, Left, Right, Sprint,
    LandingNet, DepthUp, DepthDown, LooseFeed, RodRest, Release, Interact, Sharpen, NextFishery, CycleTool,
    Inventory, Map, Journal, Tournaments, Details, QuickHelp, Performance, DevConsole,
}

/// <summary>
/// The key bindings: one key per <see cref="Act"/>, with a Polish name and a group for the controls screen (UI/
/// ControlsPanel) and the F1 quick help (UI/QuickHelp). Saved in user://controls.cfg (not in headless runs or with
/// FISHSIM_NO_SAVE, which always use the defaults). Code asks <see cref="Is"/> for a key event and <see cref="Held"/>
/// for a held key instead of naming keys itself.
/// </summary>
public static class Bindings
{
    public sealed record Entry(Act Act, string Title, string Group, Key Default);

    public static readonly Entry[] All =
    {
        new(Act.Forward, "Do przodu", "Ruch", Key.W),
        new(Act.Back, "Do tyłu", "Ruch", Key.S),
        new(Act.Left, "W lewo", "Ruch", Key.A),
        new(Act.Right, "W prawo", "Ruch", Key.D),
        new(Act.Sprint, "Bieg / szybsze zwijanie", "Ruch", Key.Shift),
        new(Act.Interact, "Wejście, rozmowa / ryba do siatki", "Ruch", Key.E),
        new(Act.LandingNet, "Podbierak", "Wędkowanie", Key.Space),
        new(Act.DepthUp, "Grunt głębiej (spławik)", "Wędkowanie", Key.Bracketright),
        new(Act.DepthDown, "Grunt płyciej (spławik)", "Wędkowanie", Key.Bracketleft),
        new(Act.LooseFeed, "Garść zanęty z ręki", "Wędkowanie", Key.G),
        new(Act.RodRest, "Wędka na podpórkę / podnieś", "Wędkowanie", Key.Z),
        new(Act.Release, "Wypuść rybę", "Wędkowanie", Key.R),
        new(Act.Sharpen, "Naostrz haczyk osełką", "Wędkowanie", Key.K),
        new(Act.NextFishery, "Następne łowisko", "Wędkowanie", Key.L),
        new(Act.CycleTool, "Następne narzędzie", "Wędkowanie", Key.Q),
        new(Act.Inventory, "Ekwipunek", "Okna", Key.I),
        new(Act.Map, "Mapa", "Okna", Key.M),
        new(Act.Journal, "Dziennik", "Okna", Key.J),
        new(Act.Tournaments, "Turnieje", "Okna", Key.T),
        new(Act.Details, "Stan zestawu (przytrzymaj)", "Okna", Key.Tab),
        new(Act.QuickHelp, "Szybka instrukcja", "Okna", Key.F1),
        new(Act.Performance, "Wydajność (FPS)", "Okna", Key.F11),
        new(Act.DevConsole, "Konsola dewelopera", "Okna", Key.F12),
    };

    /// <summary>The controls that cannot be rebound, as (what, how) for the controls screen and the quick help.</summary>
    public static readonly (string What, string How)[] Fixed =
    {
        ("Rozglądanie", "mysz"),
        ("Rzut", "LPM przytrzymaj i puść"),
        ("Zwijanie linki / prowadzenie przynęty", "LPM"),
        ("Zacięcie, pompowanie, podniesienie wędki", "PPM"),
        ("Hamulec", "kółko myszy"),
        ("Grunt spławika", "Ctrl + kółko"),
        ("Wędki 1-3, łopata, siatka, podrywka", "1 - 6"),
        ("Menu", "Esc"),
    };

    private const string FilePath = "user://controls.cfg";
    private static readonly Dictionary<Act, Key> Keys = new();
    private static bool _loaded;

    /// <summary>Raised after a key was rebound (the quick help rewrites itself).</summary>
    public static event Action? Changed;

    public static Entry EntryOf(Act act) => Array.Find(All, x => x.Act == act)!;

    /// <summary>The key bound to <paramref name="act"/>.</summary>
    public static Key Of(Act act)
    {
        Load();
        return Keys.TryGetValue(act, out var k) ? k : EntryOf(act).Default;
    }

    /// <summary>The key's name as shown to the player ("E", "Spacja", "F1").</summary>
    public static string Name(Act act) => KeyName(Of(act));

    public static string KeyName(Key key) => key switch
    {
        Key.Space => "Spacja",
        Key.Shift => "Shift",
        Key.Ctrl => "Ctrl",
        Key.Alt => "Alt",
        Key.Tab => "Tab",
        Key.Enter => "Enter",
        Key.Backspace => "Backspace",
        Key.Bracketleft => "[",
        Key.Bracketright => "]",
        _ => OS.GetKeycodeString(key),
    };

    /// <summary>True for a fresh press (not a repeat) of the key bound to <paramref name="act"/>.</summary>
    public static bool Is(InputEvent e, Act act) => e is InputEventKey { Pressed: true, Echo: false } k && k.Keycode == Of(act);

    /// <summary>The key bound to <paramref name="act"/> is held down.</summary>
    public static bool Held(Act act) => Input.IsKeyPressed(Of(act));

    /// <summary>Keys that cannot be bound (Esc opens the menu, 1-9 pick from the hotbar).</summary>
    public static bool Reserved(Key key) => key is Key.Escape or (>= Key.Key1 and <= Key.Key9) || (key >= Key.F2 && key <= Key.F10);

    /// <summary>Binds <paramref name="key"/> to <paramref name="act"/>; an action that had it takes the old key.</summary>
    public static bool Rebind(Act act, Key key)
    {
        if (Reserved(key))
            return false;
        Load();
        var old = Of(act);
        foreach (var e in All)
            if (e.Act != act && Of(e.Act) == key)
                Keys[e.Act] = old;
        Keys[act] = key;
        Save();
        Changed?.Invoke();
        return true;
    }

    public static void ResetAll()
    {
        Load();
        Keys.Clear();
        Save();
        Changed?.Invoke();
    }

    private static bool Persist => !(DisplayServer.GetName() == "headless" || OS.HasEnvironment("FISHSIM_NO_SAVE"));

    private static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        if (!Persist)
            return;
        var cfg = new ConfigFile();
        if (cfg.Load(FilePath) != Error.Ok)
            return;
        foreach (var e in All)
            if (cfg.HasSectionKey("keys", e.Act.ToString()))
            {
                var key = (Key)(long)cfg.GetValue("keys", e.Act.ToString());
                if (key != Key.None && !Reserved(key))
                    Keys[e.Act] = key;
            }
    }

    private static void Save()
    {
        if (!Persist)
            return;
        var cfg = new ConfigFile();
        foreach (var e in All)
            cfg.SetValue("keys", e.Act.ToString(), (long)Of(e.Act));
        cfg.Save(FilePath);
    }
}
