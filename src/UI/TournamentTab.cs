using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Progress;
using FishingSim.Tournaments;

namespace FishingSim.UI;

/// <summary>Tournament list with entry buttons, and the live ranking once one is running.</summary>
public partial class TournamentTab : HBoxContainer
{
    private TournamentManager _manager = null!;
    private Profile _profile = null!;
    private VBoxContainer _list = null!;
    private VBoxContainer _right = null!;
    private Label _message = null!;
    private float _refreshTimer;

    public void Setup(TournamentManager manager, Profile profile)
    {
        _manager = manager;
        _profile = profile;
    }

    public override void _Ready()
    {
        Name = "Turnieje";
        AddThemeConstantOverride("separation", 18);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.4f };
        left.AddThemeConstantOverride("separation", 8);
        AddChild(left);
        var title = new Label { Text = "Zawody" };
        title.AddThemeFontSizeOverride("font_size", 20);
        left.AddChild(title);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        left.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_list);
        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _message.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.4f));
        left.AddChild(_message);

        _right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _right.AddThemeConstantOverride("separation", 4);
        AddChild(_right);
    }

    public void Refresh()
    {
        if (_list == null)
            return;
        foreach (var c in _list.GetChildren())
            c.QueueFree();
        foreach (var data in _manager.Tournaments)
            _list.AddChild(Card(data));
        RefreshRight();
    }

    private Control Card(TournamentData data)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UITheme.Box(UITheme.Background, UITheme.Border, 8, 1, 10));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        panel.AddChild(box);
        var name = new Label { Text = data.DisplayName };
        name.AddThemeFontSizeOverride("font_size", 19);
        box.AddChild(name);
        box.AddChild(new Label
        {
            Text = data.Description,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1, 1, 1, 0.75f),
        });
        string prizes = string.Join(" / ", data.PrizeMoney.Select(p => $"{p} gr"));
        box.AddChild(new Label
        {
            Text = $"{data.LocationName} | poziom {data.RequiredLevel} | {data.DurationMinutes:0} min | liczy się {data.CountedFish} najcięższych ryb\nWpisowe {data.EntryFee} gr | nagrody: {prizes}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var best = _profile.TournamentHistory.Where(t => t.Name == data.DisplayName).OrderBy(t => t.Rank).FirstOrDefault();
        if (best != null)
            box.AddChild(new Label { Text = $"Twój najlepszy wynik: {best.Rank}. miejsce ({best.Score:0.00} kg)", Modulate = new Color(0.6f, 0.9f, 0.6f) });

        string? why = _manager.CanStart(data);
        var button = new Button { Text = why == null ? $"Start (wpisowe {data.EntryFee} gr)" : why, Disabled = why != null, CustomMinimumSize = new Vector2(0, 38) };
        button.Pressed += () =>
        {
            string? error = _manager.Start(data);
            _message.Text = error ?? $"Zawody rozpoczęte: {data.DisplayName}. Powodzenia!";
            Refresh();
        };
        box.AddChild(button);
        return panel;
    }

    private void RefreshRight()
    {
        foreach (var c in _right.GetChildren())
            c.QueueFree();
        var session = _manager.Active;
        var head = new Label { Text = session != null ? $"Trwa: {session.Data.DisplayName}" : "Ostatnie zawody" };
        head.AddThemeFontSizeOverride("font_size", 20);
        _right.AddChild(head);
        if (session != null)
        {
            _right.AddChild(new Label { Text = $"Pozostało {TimeText(session.Remaining)} | liczy się {session.Data.CountedFish} najcięższych ryb", Modulate = new Color(1, 1, 1, 0.75f) });
            _right.AddChild(StandingsGrid.Create(session.Standings()));
            return;
        }
        if (_profile.TournamentHistory.Count == 0)
        {
            _right.AddChild(new Label { Text = "Jeszcze nie brałeś udziału w zawodach.", Modulate = new Color(1, 1, 1, 0.6f) });
            return;
        }
        _right.AddChild(new Label { Text = $"Zwycięstwa: {_profile.TournamentWins} | udziały: {_profile.TournamentHistory.Count}", Modulate = new Color(1, 1, 1, 0.75f) });
        foreach (var t in Enumerable.Reverse(_profile.TournamentHistory).Take(8))
            _right.AddChild(new Label { Text = $"{t.Name}: {t.Rank}. z {t.Entrants} | {t.Score:0.00} kg | +{t.PrizeMoney} gr, +{t.PrizeXp} XP" });
    }

    public static string TimeText(float seconds) => $"{(int)(seconds / 60f):00}:{(int)(seconds % 60f):00}";

    public override void _Process(double delta)
    {
        _refreshTimer -= (float)delta;
        if (_refreshTimer > 0f || !IsVisibleInTree() || _manager == null)
            return;
        _refreshTimer = 0.5f;
        if (_manager.Active != null)
            RefreshRight();
    }
}
