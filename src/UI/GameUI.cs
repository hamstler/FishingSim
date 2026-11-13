using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using FishingSim.Core;
using FishingSim.Data;
using FishingSim.Fishing;
using FishingSim.Progress;
using FishingSim.Tournaments;
using FishingSim.World;

namespace FishingSim.UI;

/// <summary>
/// Player screen with three tabs: equipment (I), map (M) and fishing journal (J).
/// The game keeps running behind it; Space still sets the hook so a bite is not lost.
/// </summary>
public partial class GameUI : CanvasLayer
{
    private const int InventoryTab = 0, MapTab = 1, JournalTab = 2, TournamentTabIndex = 3;
    private const float MapShownWidth = 700f;
    private const float MapShownHeight = 500f;

    private FishingRod _rod = null!;
    private Profile _profile = null!;
    private TackleLoadout _loadout = null!;
    private LakeBed _lake = null!;
    private Player _player = null!;
    private TournamentManager _tournaments = null!;
    private Scenery _scenery = null!;
    private TournamentTab _tournamentTab = null!;

    private TabContainer _tabs = null!;
    private readonly Label[] _slotName = new Label[TackleSlotExtensions.Count];
    private readonly TextureRect[] _slotIcon = new TextureRect[TackleSlotExtensions.Count];
    private readonly Label[] _slotStats = new Label[TackleSlotExtensions.Count];
    private readonly Button[] _slotPrev = new Button[TackleSlotExtensions.Count];
    private readonly Button[] _slotNext = new Button[TackleSlotExtensions.Count];
    private Label _summary = null!, _chanceNote = null!, _depthLabel = null!;
    private HSlider _depthSlider = null!;
    private readonly List<(Label Name, ProgressBar Bar, Label Percent, Control Row)> _chanceRows = new();
    private TextureRect _mapRect = null!;
    private MapOverlay _mapOverlay = null!;
    private Label _mapInfo = null!;
    private readonly Dictionary<int, ImageTexture> _mapCache = new();
    private int _mapLocation = -1;
    private Rect2 _mapArea;
    private System.Threading.Tasks.Task<Image>? _mapPainting;
    private int _mapPaintingFor = -1;
    private readonly List<Rect2> _labelRects = new();
    private Label _journalHead = null!;
    private VBoxContainer _journalList = null!, _speciesList = null!;

    public void Setup(FishingRod rod, Player player, TournamentManager tournaments, Scenery scenery)
    {
        _tournaments = tournaments;
        _scenery = scenery;
        _rod = rod;
        _profile = rod.Profile;
        _loadout = rod.Loadout;
        _lake = rod.Lake;
        _player = player;
    }

    public override void _Ready()
    {
        Layer = 9;
        Visible = false;
        var panel = new PanelContainer { Theme = UITheme.Make() };
        panel.AnchorLeft = panel.AnchorRight = panel.AnchorTop = panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = -560; panel.OffsetRight = 560; panel.OffsetTop = -340; panel.OffsetBottom = 340;
        AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 14);
        panel.AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);

        var head = new HBoxContainer();
        box.AddChild(head);
        var title = new Label { Text = "Ekran wędkarza", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 26);
        head.AddChild(title);
        head.AddChild(new Label { Text = "I - ekwipunek | M - mapa | J - dziennik | T - turnieje | Esc - zamknij", Modulate = new Color(1, 1, 1, 0.6f) });

        _tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddChild(_tabs);
        BuildInventory();
        BuildMap();
        BuildJournal();
        _tournamentTab = new TournamentTab();
        _tournamentTab.Setup(_tournaments, _profile);
        _tabs.AddChild(_tournamentTab);
        _profile.Changed += Refresh;
        _tournaments.ActiveChanged += Refresh;
    }

    public override void _ExitTree()
    {
        if (_profile != null)
            _profile.Changed -= Refresh;
        if (_tournaments != null)
            _tournaments.ActiveChanged -= Refresh;
    }

    // ------------------------------------------------------------------ open / close
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        int tab = key.Keycode switch { Key.I => InventoryTab, Key.M => MapTab, Key.J => JournalTab, Key.T => TournamentTabIndex, _ => -1 };
        if (key.Keycode == Key.Escape && Visible)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
        else if (tab >= 0)
        {
            if (Visible && _tabs.CurrentTab == tab)
                Close();
            else if (!OtherModalOpen())
                Open(tab);
            GetViewport().SetInputAsHandled();
        }
    }

    private bool OtherModalOpen() =>
        GetParent().GetChildren().Any(n => n is Shop { Visible: true } or PauseMenu { Visible: true } or TournamentResultsScreen { Visible: true });

    public void Open(int tab)
    {
        Visible = true;
        _tabs.CurrentTab = tab;
        _rod.InputBlocked = true;
        _rod.AllowHookKey = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
    }

    public void Close()
    {
        Visible = false;
        _rod.InputBlocked = false;
        _rod.AllowHookKey = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public int CurrentTab => _tabs.CurrentTab;

    private void Refresh()
    {
        if (!Visible)
            return;
        RefreshInventory();
        RefreshMap();
        RefreshJournal();
        _tournamentTab.Refresh();
    }

    public override void _Process(double delta)
    {
        // Start painting a newly opened map's picture at once (in the background), not when M is first pressed.
        if (_lake != null && _lake.LocationIndex != _mapLocation)
            RefreshMap();
        CollectMap();
        if (Visible && _tabs.CurrentTab == MapTab)
        {
            _mapOverlay.QueueRedraw();
            UpdateMapInfo();
        }
    }

    // ------------------------------------------------------------------ inventory tab
    private void BuildInventory()
    {
        var page = new HBoxContainer { Name = "Ekwipunek" };
        page.AddThemeConstantOverride("separation", 18);
        _tabs.AddChild(page);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(600, 0) };
        left.AddThemeConstantOverride("separation", 6);
        page.AddChild(left);
        var leftTitle = new Label { Text = "Założony sprzęt" };
        leftTitle.AddThemeFontSizeOverride("font_size", 20);
        left.AddChild(leftTitle);
        for (int i = 0; i < TackleSlotExtensions.Count; i++)
        {
            int slot = i;
            // Each piece of the rig: its picture on parchment, its name with arrows to change it, its numbers.
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", UiArt.Nine(UiArt.InsetTexture, 4, 6));
            left.AddChild(card);
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 10);
            card.AddChild(h);
            var frame = new PanelContainer { CustomMinimumSize = new Vector2(56, 56) };
            frame.AddThemeStyleboxOverride("panel", UiArt.Nine(UiArt.ParchmentTexture, 10, 2));
            h.AddChild(frame);
            var icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps, CustomMinimumSize = new Vector2(50, 50),
            };
            frame.AddChild(icon);
            _slotIcon[slot] = icon;
            var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            v.AddThemeConstantOverride("separation", 0);
            h.AddChild(v);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            v.AddChild(row);
            row.AddChild(new Label { Text = ((TackleSlot)slot).Title(), CustomMinimumSize = new Vector2(90, 0), Modulate = new Color(1, 1, 1, 0.6f) });
            var prev = new Button { Text = "◀" };
            prev.Pressed += () => CycleSlot(slot, -1);
            row.AddChild(prev);
            var name = new Label { CustomMinimumSize = new Vector2(250, 0), HorizontalAlignment = HorizontalAlignment.Center, ClipText = true };
            name.AddThemeFontSizeOverride("font_size", 17);
            row.AddChild(name);
            var next = new Button { Text = "▶" };
            next.Pressed += () => CycleSlot(slot, 1);
            row.AddChild(next);
            var stats = new Label { Modulate = new Color(1, 1, 1, 0.7f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
            stats.AddThemeFontSizeOverride("font_size", 13);
            v.AddChild(stats);
            _slotName[slot] = name;
            _slotStats[slot] = stats;
            _slotPrev[slot] = prev;
            _slotNext[slot] = next;
        }
        _summary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        left.AddChild(_summary);

        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        page.AddChild(right);
        var rightTitle = new Label { Text = "Co weźmie na ten zestaw" };
        rightTitle.AddThemeFontSizeOverride("font_size", 20);
        right.AddChild(rightTitle);
        _chanceNote = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.7f) };
        right.AddChild(_chanceNote);
        _depthLabel = new Label();
        right.AddChild(_depthLabel);
        _depthSlider = new HSlider { MinValue = 0.5, MaxValue = 10, Step = 0.5, Value = 3 };
        _depthSlider.ValueChanged += _ => RefreshInventory();
        right.AddChild(_depthSlider);
        for (int i = 0; i < 8; i++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var name = new Label { CustomMinimumSize = new Vector2(150, 0) };
            var bar = new ProgressBar { CustomMinimumSize = new Vector2(200, 22), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ShowPercentage = false, MaxValue = 100 };
            var pct = new Label { CustomMinimumSize = new Vector2(54, 0), HorizontalAlignment = HorizontalAlignment.Right };
            row.AddChild(name);
            row.AddChild(bar);
            row.AddChild(pct);
            right.AddChild(row);
            _chanceRows.Add((name, bar, pct, row));
        }
    }

    private void CycleSlot(int slot, int step)
    {
        if (_rod.State != RodState.Idle)
            return;
        _loadout.Cycle((TackleSlot)slot, step);
        _rod.ApplyTackle();
        _profile.NotifyChanged();
    }

    private string ItemStats(TackleSlot slot, TackleItem item) => item switch
    {
        RodData r => $"Rzut x{r.CastMult:0.00} | wytrzymałość wędki {r.MaxLoad:0} kg | amortyzacja {Mathf.RoundToInt(r.ShockAbsorb * 200f)}%",
        ReelData r => $"Hamulec do {r.DragMax:0} kg | zwijanie {r.ReelSpeed:0.0} m/s | szpula {r.LineCapacity:0} m",
        LineData l => $"{(l.Kind == LineKind.Braid ? "Plecionka" : "Żyłka")} {l.Strength:0} kg | widoczność x{l.Visibility:0.00} | "
            + (l.Kind == LineKind.Braid ? "bez rozciągliwości: pewne zacięcie, zrywy idą w wędkę" : "rozciąga się i tłumi zrywy"),
        HookData h => $"Rozmiar #{Mathf.RoundToInt(h.Size)} | większy hak = większe ryby, ale gorzej bierze drobnica",
        LeaderData d when d.ItemId == TackleSlotExtensions.NoLeader => "Linka idzie prosto do haczyka (szczupak może ją przegryźć)",
        LeaderData d => $"Wytrzymałość {d.Strength:0} kg | widoczność x{d.Visibility:0.00} | odporność na zęby {d.BiteProof * 100f:0}%",
        BaitData { Kind: BaitKind.Lure } b => $"Przynęta sztuczna, masz {_profile.BaitCounts.GetValueOrDefault(b.ItemId)} szt. Spinning: prowadź zwijając (LPM, Shift szybciej), PPM podszarpuje.",
        _ => $"Zapas: {_profile.BaitCounts.GetValueOrDefault(item.ItemId)} szt.",
    };

    private void RefreshInventory()
    {
        bool idle = _rod.State == RodState.Idle;
        for (int s = 0; s < TackleSlotExtensions.Count; s++)
        {
            var slot = (TackleSlot)s;
            var item = _loadout.Selected(slot);
            _slotName[s].Text = item.DisplayName;
            _slotIcon[s].Texture = Graphics.Icons.For(item);
            _slotStats[s].Text = ItemStats(slot, item);
            _slotPrev[s].Disabled = !idle;
            _slotNext[s].Disabled = !idle;
        }
        string weak = _rod.LineStrength > _rod.RodMaxLoad ? "wędka"
            : _loadout.Leader.Strength < _loadout.Line.Strength ? "przypon" : "linka";
        _summary.Text = $"Najsłabsze ogniwo: {weak}, limit {_rod.EffectiveLimit:0.0} kg. Maks. hamulec {_rod.DragMax:0.0} kg. "
            + $"Zasięg rzutu do ok. {_rod.EstimateCastRange():0} m."
            + (idle ? "" : "\nSprzęt można zmieniać tylko między rzutami.");

        float depth = (float)_depthSlider.Value;
        _depthLabel.Text = $"Głębokość wody: {depth:0.0} m";
        var chances = _rod.BiteChances(depth);
        bool any = chances.Any(c => c.Share > 0f);
        float attraction = _rod.Attraction();
        string speed = attraction < 0.3f ? "bardzo wolno" : attraction < 0.7f ? "wolno" : attraction < 1.2f ? "średnio" : "szybko";
        _chanceNote.Text = any
            ? $"{_rod.World.TimeText}, {_rod.World.Weather.DisplayName()}, {_rod.Lake.Location.DisplayName}. Brania będą {speed}."
            : "Na tę przynętę i hak nic tu nie weźmie. Zmień przynętę lub głębokość.";
        for (int i = 0; i < _chanceRows.Count; i++)
        {
            var row = _chanceRows[i];
            row.Row.Visible = i < chances.Count && chances[i].Share > 0.005f;
            if (!row.Row.Visible)
                continue;
            row.Name.Text = chances[i].Species.SpeciesName;
            row.Bar.Value = chances[i].Share * 100f;
            row.Percent.Text = $"{Mathf.RoundToInt(chances[i].Share * 100f)}%";
        }
    }

    // ------------------------------------------------------------------ map tab
    private void BuildMap()
    {
        var page = new HBoxContainer { Name = "Mapa" };
        page.AddThemeConstantOverride("separation", 16);
        _tabs.AddChild(page);
        // The map keeps its own size (set to the map's proportions in RefreshMap): it must not stretch with the page.
        _mapRect = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            CustomMinimumSize = new Vector2(MapShownWidth, 480),
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
        page.AddChild(_mapRect);
        _mapOverlay = new MapOverlay { Painter = DrawMapOverlay };
        _mapOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _mapRect.AddChild(_mapOverlay);

        var side = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        side.AddThemeConstantOverride("separation", 10);
        page.AddChild(side);
        var title = new Label { Text = "Mapa łowiska" };
        title.AddThemeFontSizeOverride("font_size", 20);
        side.AddChild(title);
        var legend = new Label
        {
            Text = "Poziomice co 2 m (co 10 m grubsze), linie głębokości co 1 m (co 5 m grubsze)\n"
                + "Ciemna zieleń: las · jasne pasy: ścieżki · piasek: plaże\n"
                + "Żółta strzałka: Ty · biały punkt: spławik · brązowa kładka: pomost\n"
                + "Kwadraty: kramy i miejsca · kręgi: odległość od Ciebie",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1, 1, 1, 0.75f),
        };
        legend.AddThemeFontSizeOverride("font_size", 14);
        side.AddChild(legend);
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.42f, 0.78f, 0.82f));
        gradient.SetColor(1, new Color(0.03f, 0.14f, 0.3f));
        side.AddChild(new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = gradient, FillFrom = Vector2.Zero, FillTo = new Vector2(1, 0), Width = 256, Height = 12 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            CustomMinimumSize = new Vector2(0, 14),
        });
        var scale = new HBoxContainer();
        scale.AddChild(new Label { Text = "płytko", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        scale.AddChild(new Label { Text = "9 m i więcej" });
        side.AddChild(scale);
        side.AddChild(new HSeparator());
        var hover = new Label { Text = "Pod kursorem" };
        hover.AddThemeFontSizeOverride("font_size", 20);
        side.AddChild(hover);
        // The readout under the cursor sits in a scroll box filling the rest of the column, so a long list of fish
        // never makes the page (and the map with it) taller.
        var infoScroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        side.AddChild(infoScroll);
        _mapInfo = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Najedź kursorem na mapę.",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        infoScroll.AddChild(_mapInfo);
    }

    private void RefreshMap()
    {
        int idx = _lake.LocationIndex;
        if (idx == _mapLocation)
            return;
        _mapLocation = idx;
        var loc = _lake.Location;
        _mapArea = MapPreview.Bounds(loc);
        // The topographic map is painted in the background as soon as a map opens (well under a second on several
        // cores); headless runs have nothing to show it on.
        _mapRect.Texture = _mapCache.GetValueOrDefault(idx);
        if (_mapRect.Texture == null && _mapPaintingFor != idx && !Graphics.Renderer.IsHeadless)
        {
            const int w = 960;
            int h = (int)MathF.Round(w * _mapArea.Size.Y / _mapArea.Size.X);
            var area = _mapArea;
            _mapPaintingFor = idx;
            _mapPainting = System.Threading.Tasks.Task.Run(() => MapPreview.RenderTopo(loc, w, h, area));
        }
        // Fit the whole map (lake and village) inside the tab.
        float aspect = _mapArea.Size.Y / _mapArea.Size.X;
        float shownWidth = MathF.Min(MapShownWidth, MapShownHeight / aspect);
        _mapRect.CustomMinimumSize = new Vector2(shownWidth, shownWidth * aspect);
    }

    /// <summary>World (x, z) to a pixel position on a map drawn at <paramref name="size"/>.</summary>
    public Vector2 WorldToPixel(float x, float z, Vector2 size) =>
        new((x - _mapArea.Position.X) / _mapArea.Size.X * size.X, (z - _mapArea.Position.Y) / _mapArea.Size.Y * size.Y);

    public Vector2 PixelToWorld(Vector2 p, Vector2 size) =>
        new(_mapArea.Position.X + p.X / size.X * _mapArea.Size.X, _mapArea.Position.Y + p.Y / size.Y * _mapArea.Size.Y);

    public Rect2 MapArea => _mapArea;
    public void EnsureMap() => RefreshMap();

    /// <summary>True once the topographic picture of the current map is on screen (screenshots wait for it).</summary>
    public bool MapReady => _mapRect.Texture != null;

    /// <summary>Takes the background painting once it is done.</summary>
    private void CollectMap()
    {
        if (_mapPainting is not { IsCompleted: true } done)
            return;
        if (!done.IsFaulted)
        {
            var texture = ImageTexture.CreateFromImage(done.Result);
            _mapCache[_mapPaintingFor] = texture;
            if (_mapPaintingFor == _mapLocation)
                _mapRect.Texture = texture;
        }
        else
            GD.PushWarning($"Map painting failed: {done.Exception?.GetBaseException().Message}");
        _mapPainting = null;
        _mapPaintingFor = -1;
    }

    /// <summary>The map's on-screen size (tests: it must not change while the cursor moves over it).</summary>
    public Vector2 MapShownSize => _mapRect.Size;

    /// <summary>The readout under the cursor (tests).</summary>
    public string MapInfoText => _mapInfo.Text;

    /// <summary>Puts the cursor on the map at a fraction of its size (screenshots and tests).</summary>
    public void HoverMap(Vector2 fraction)
    {
        _mapOverlay.SetHover(fraction * _mapOverlay.Size);
        UpdateMapInfo();
    }

    private void DrawMapOverlay(MapOverlay c)
    {
        if (_mapArea.Size == Vector2.Zero)
            return;
        var size = c.Size;
        var font = c.GetThemeDefaultFont() ?? ThemeDB.FallbackFont;
        var ink = new Color(0.05f, 0.04f, 0.03f, 0.85f);
        if (_mapRect.Texture == null)
        {
            c.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.16f, 0.22f, 0.12f));
            c.DrawString(font, new Vector2(0f, size.Y / 2f), "Rysowanie mapy…", HorizontalAlignment.Center, size.X, 18, new Color(1, 1, 1, 0.8f));
        }
        var pp = WorldToPixel(_player.GlobalPosition.X, _player.GlobalPosition.Z, size);
        float pxPerMetre = size.X / _mapArea.Size.X;
        foreach (int r in new[] { 10, 20, 30, 40, 60 })
        {
            c.DrawArc(pp, r * pxPerMetre, 0f, Mathf.Tau, 64, new Color(1, 1, 1, 0.22f), 1f, true);
            if (r is 20 or 40 or 60)
                Outlined(c, font, pp + new Vector2(r * pxPerMetre + 2f, -3f), $"{r} m", 11, new Color(1, 1, 1, 0.7f));
        }
        // The pier: a wooden walk with dark edges.
        var pierA = WorldToPixel(0f, LakeBed.PierEndZ, size);
        var pierB = WorldToPixel(0f, 6f, size);
        float pierWidth = MathF.Max(3f, 2.4f * pxPerMetre);
        c.DrawLine(pierA, pierB, ink, pierWidth + 2f);
        c.DrawLine(pierA, pierB, new Color(0.62f, 0.44f, 0.24f), pierWidth);

        // Places: a square each; their names placed where they do not cover one another (beside, above or below the
        // square), else left out (the readout names the place under the cursor).
        _labelRects.Clear();
        foreach (var marker in _scenery.Markers)
        {
            var mp = WorldToPixel(marker.Position.X, marker.Position.Y, size);
            _labelRects.Add(new Rect2(mp - new Vector2(6, 6), new Vector2(12, 12)));
        }
        _labelRects.Add(new Rect2(pp - new Vector2(12, 12), new Vector2(24, 24)));
        foreach (var marker in _scenery.Markers)
        {
            var mp = WorldToPixel(marker.Position.X, marker.Position.Y, size);
            c.DrawRect(new Rect2(mp - new Vector2(5, 5), new Vector2(10, 10)), marker.Color);
            c.DrawRect(new Rect2(mp - new Vector2(5, 5), new Vector2(10, 10)), ink, false, 1.5f);
            var text = font.GetStringSize(marker.Label, HorizontalAlignment.Left, -1, 12);
            Span<Vector2> spots = stackalloc Vector2[]
            {
                mp + new Vector2(9f, -text.Y / 2f),
                mp + new Vector2(-9f - text.X, -text.Y / 2f),
                mp + new Vector2(-text.X / 2f, -9f - text.Y),
                mp + new Vector2(-text.X / 2f, 9f),
            };
            foreach (var spot in spots)
            {
                var rect = new Rect2(spot, text).Grow(1f);
                if (rect.Position.X < 0f || rect.Position.Y < 0f || rect.End.X > size.X || rect.End.Y > size.Y)
                    continue;
                bool free = true;
                foreach (var other in _labelRects)
                    if (other.Intersects(rect))
                    {
                        free = false;
                        break;
                    }
                if (!free)
                    continue;
                _labelRects.Add(rect);
                Outlined(c, font, spot + new Vector2(0f, text.Y * 0.78f), marker.Label, 12, new Color(1f, 0.97f, 0.88f));
                break;
            }
        }

        var f = -_player.GlobalTransform.Basis.Z;
        var dir = new Vector2(f.X, f.Z).Normalized();
        var perp = new Vector2(-dir.Y, dir.X);
        var arrow = new[] { pp + dir * 12f, pp - dir * 7f + perp * 7f, pp - dir * 3f, pp - dir * 7f - perp * 7f };
        c.DrawColoredPolygon(arrow, new Color(1f, 0.9f, 0.3f));
        c.DrawPolyline(new[] { arrow[0], arrow[1], arrow[2], arrow[3], arrow[0] }, ink, 1.5f, true);

        if (_rod.State is RodState.Waiting or RodState.Nibble or RodState.Bite or RodState.Fight or RodState.Close)
        {
            var bp = WorldToPixel(_rod.Bobber.GlobalPosition.X, _rod.Bobber.GlobalPosition.Z, size);
            c.DrawLine(pp, bp, new Color(1, 1, 1, 0.5f), 1f, true);
            c.DrawCircle(bp, 5f, Colors.White);
            c.DrawArc(bp, 8f, 0f, Mathf.Tau, 20, new Color(1f, 0.4f, 0.3f), 2f, true);
        }

        DrawScaleAndNorth(c, font, size, pxPerMetre);

        if (c.HasHover)
        {
            c.DrawLine(c.Hover + new Vector2(-9, 0), c.Hover + new Vector2(9, 0), ink, 3.5f);
            c.DrawLine(c.Hover + new Vector2(0, -9), c.Hover + new Vector2(0, 9), ink, 3.5f);
            c.DrawLine(c.Hover + new Vector2(-9, 0), c.Hover + new Vector2(9, 0), Colors.White, 1.5f);
            c.DrawLine(c.Hover + new Vector2(0, -9), c.Hover + new Vector2(0, 9), Colors.White, 1.5f);
        }
    }

    /// <summary>Text with a dark outline (reads over the map's light and dark parts).</summary>
    private static void Outlined(Control c, Font font, Vector2 at, string text, int size, Color colour)
    {
        c.DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, size, 4, new Color(0.05f, 0.04f, 0.03f, 0.85f));
        c.DrawString(font, at, text, HorizontalAlignment.Left, -1, size, colour);
    }

    /// <summary>A scale bar (bottom left) and a north arrow (top right) on small dark plates.</summary>
    private static void DrawScaleAndNorth(Control c, Font font, Vector2 size, float pxPerMetre)
    {
        var plate = new Color(0.05f, 0.04f, 0.03f, 0.6f);
        var light = new Color(1f, 0.97f, 0.88f);
        // The longest round length that fits in about a fifth of the map.
        int metres = 100;
        foreach (int m in new[] { 100, 50, 25, 20, 10 })
        {
            metres = m;
            if (m * pxPerMetre <= size.X * 0.22f)
                break;
        }
        float len = metres * pxPerMetre;
        var origin = new Vector2(14f, size.Y - 16f);
        c.DrawRect(new Rect2(origin + new Vector2(-8f, -24f), new Vector2(len + 16f, 32f)), plate);
        for (int i = 0; i < 4; i++)
        {
            var seg = new Rect2(origin + new Vector2(len * i / 4f, -4f), new Vector2(len / 4f, 4f));
            c.DrawRect(seg, i % 2 == 0 ? light : new Color(0.1f, 0.08f, 0.06f));
        }
        c.DrawRect(new Rect2(origin + new Vector2(0f, -4f), new Vector2(len, 4f)), light, false, 1f);
        c.DrawString(font, origin + new Vector2(0f, -9f), "0", HorizontalAlignment.Left, -1, 11, light);
        c.DrawString(font, origin + new Vector2(len - 30f, -9f), $"{metres} m", HorizontalAlignment.Right, 30f, 11, light);

        var n = new Vector2(size.X - 24f, 30f);
        c.DrawCircle(n, 17f, plate);
        c.DrawColoredPolygon(new[] { n + new Vector2(0f, -13f), n + new Vector2(6f, 4f), n + new Vector2(0f, 0f) }, new Color(0.95f, 0.35f, 0.25f));
        c.DrawColoredPolygon(new[] { n + new Vector2(0f, -13f), n + new Vector2(0f, 0f), n + new Vector2(-6f, 4f) }, new Color(0.7f, 0.2f, 0.14f));
        c.DrawString(font, n + new Vector2(-17f, 15f), "N", HorizontalAlignment.Center, 34f, 12, light);
    }

    private void UpdateMapInfo()
    {
        if (!_mapOverlay.HasHover)
        {
            _mapInfo.Text = "Najedź kursorem na mapę, aby zobaczyć głębokość i szanse brań.";
            return;
        }
        var w = PixelToWorld(_mapOverlay.Hover, _mapOverlay.Size);
        float depth = _lake.DepthAt(w.X, w.Y);
        float dist = new Vector2(w.X - _player.GlobalPosition.X, w.Y - _player.GlobalPosition.Z).Length();
        var sb = new StringBuilder();
        // A place under the cursor (its name may be left off the map where the labels crowd).
        foreach (var marker in _scenery.Markers)
            if (WorldToPixel(marker.Position.X, marker.Position.Y, _mapOverlay.Size).DistanceTo(_mapOverlay.Hover) < 10f)
            {
                sb.Append(marker.Label).Append('\n');
                break;
            }
        if (depth <= 0f)
        {
            string ground = _lake.GroundKindAt(w.X, w.Y) switch
            {
                Core.Camp.GroundKind.Forest => "las",
                Core.Camp.GroundKind.Path => "ścieżka",
                Core.Camp.GroundKind.Sand => "plaża",
                Core.Camp.GroundKind.Mud => "wilgotny brzeg",
                Core.Camp.GroundKind.Rock => "skały",
                Core.Camp.GroundKind.Meadow => "łąka",
                _ => "brzeg",
            };
            sb.Append($"Ląd: {ground}, {MathF.Max(0f, _lake.HeightAt(w.X, w.Y)):0} m nad wodą\nOdległość od Ciebie: {dist:0} m");
            string land = sb.ToString();
            if (_mapInfo.Text != land)
                _mapInfo.Text = land;
            return;
        }
        float reach = _rod.EstimateCastRange();
        sb.Append($"Głębokość: {depth:0.0} m · {dist:0} m od Ciebie\n");
        sb.Append(dist <= reach ? "W zasięgu rzutu." : $"Poza zasięgiem rzutu (do ok. {reach:0} m).");
        sb.Append("\nNa obecny zestaw: ");
        // The fish run on as one wrapped paragraph (a name never breaks from its share: no-break spaces).
        var shown = _rod.BiteChances(depth).Where(c => c.Share >= 0.01f).ToList();
        for (int i = 0; i < shown.Count; i++)
        {
            if (i > 0)
                sb.Append("\u00A0· ");
            sb.Append(shown[i].Species.SpeciesName.Replace(' ', '\u00A0')).Append('\u00A0').Append(Mathf.RoundToInt(shown[i].Share * 100f)).Append('%');
        }
        if (shown.Count == 0)
            sb.Append("nic tu nie weźmie");
        string text = sb.ToString();
        if (_mapInfo.Text != text)
            _mapInfo.Text = text;
    }

    // ------------------------------------------------------------------ journal tab
    private void BuildJournal()
    {
        var page = new VBoxContainer { Name = "Dziennik" };
        page.AddThemeConstantOverride("separation", 8);
        _tabs.AddChild(page);
        _journalHead = new Label();
        _journalHead.AddThemeFontSizeOverride("font_size", 19);
        page.AddChild(_journalHead);
        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        cols.AddThemeConstantOverride("separation", 18);
        page.AddChild(cols);

        var leftBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.5f };
        cols.AddChild(leftBox);
        var lt = new Label { Text = "Ostatnie połowy" };
        lt.AddThemeFontSizeOverride("font_size", 20);
        leftBox.AddChild(lt);
        _journalList = ScrollList(leftBox, 4);

        var rightBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cols.AddChild(rightBox);
        var rt = new Label { Text = "Gatunki" };
        rt.AddThemeFontSizeOverride("font_size", 20);
        rightBox.AddChild(rt);
        _speciesList = ScrollList(rightBox, 10);
    }

    private static VBoxContainer ScrollList(Control parent, int separation)
    {
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        parent.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", separation);
        scroll.AddChild(list);
        return list;
    }

    private static void ClearChildren(Node node)
    {
        foreach (var c in node.GetChildren())
            c.QueueFree();
    }

    private void RefreshJournal()
    {
        float totalKg = _profile.Journal.Sum(e => e.Weight);
        _journalHead.Text = $"Poziom {_profile.Level} (XP {_profile.Xp} / {_profile.XpToNext}) | złowione ryby: {_profile.TotalCatches} | w dzienniku: {totalKg:0.0} kg | {_profile.Money} gr";

        ClearChildren(_journalList);
        if (_profile.Journal.Count == 0)
            _journalList.AddChild(new Label { Text = "Dziennik jest pusty. Złów pierwszą rybę!" });
        foreach (var e in Enumerable.Reverse(_profile.Journal).Take(40))
        {
            string where = e.Location != null ? $"{e.Location}, {e.Depth ?? 0f:0.0} m" : "?";
            _journalList.AddChild(new Label
            {
                Text = $"{e.Hour ?? "--:--"}  {e.Species} {e.Weight:0.00} kg\n     {where} | {e.Bait ?? "?"}, {e.Hook ?? "?"}",
            });
        }

        ClearChildren(_speciesList);
        int known = _rod.Species.Count(sp => _profile.CatchCounts.GetValueOrDefault(sp.SpeciesName) > 0);
        _speciesList.AddChild(new Label { Text = $"Atlas ryb: poznane {known} z {_rod.Species.Count} gatunków", Modulate = new Color(1f, 0.85f, 0.5f) });
        foreach (var sp in _rod.Species)
        {
            var entries = _profile.Journal.Where(e => e.Species == sp.SpeciesName).ToList();
            var label = new Label();
            if (entries.Count > 0)
            {
                string favourite = entries.GroupBy(e => e.Bait ?? "?").OrderByDescending(g => g.Count()).First().Key;
                float record = _profile.Records.GetValueOrDefault(sp.SpeciesName);
                label.Text = $"{sp.SpeciesName}\n   w dzienniku: {entries.Count} szt., razem {entries.Sum(e => e.Weight):0.0} kg\n"
                    + $"   rekord: {record:0.00} kg | najlepsza przynęta: {favourite}\n   {SpeciesTips(sp)}";
            }
            else
            {
                label.Text = $"{sp.SpeciesName}\n   jeszcze nie złowiono";
                label.Modulate = new Color(1, 1, 1, 0.55f);
            }
            _speciesList.AddChild(label);
        }
    }

    private static readonly (string Id, string Name)[] BaitNames =
    {
        ("worm", "robak"), ("maggot", "czerwie"), ("insect", "koniki"), ("bread", "chleb"),
        ("corn", "pszenica"), ("boilie", "kulka"), ("livebait", "żywiec"),
    };

    /// <summary>What the angler has learned about a species once caught: baits it takes, depth and best time.</summary>
    public static string SpeciesTips(FishSpecies sp)
    {
        var baits = BaitNames.Where(b => sp.AffinityFor(b.Id) >= 0.8f).OrderByDescending(b => sp.AffinityFor(b.Id)).Select(b => b.Name).ToList();
        string when = sp.NightActivity > sp.DayActivity * 1.2f ? "nocą"
            : sp.TwilightBonus >= 1.5f ? "o świcie i zmierzchu"
            : sp.DayActivity > sp.NightActivity * 2f ? "za dnia"
            : "o każdej porze";
        return $"bierze na: {(baits.Count > 0 ? string.Join(", ", baits) : "-")} | {sp.MinDepth:0.#}-{sp.MaxDepth:0.#} m | najlepiej {when}";
    }
}
