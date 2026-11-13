using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Data;
using FishingSim.Fishing;
using FishingSim.Graphics;
using FishingSim.Progress;
using FishingSim.World;

namespace FishingSim.UI;

/// <summary>Which counter the player walked up to; decides which tabs the shop shows.</summary>
public enum ShopKind { Tackle, Bait, FishBuyer, Market, General, Travel }

/// <summary>
/// The stalls: tackle (rods, reels, lines, hooks, keepnets, landing nets, accessories), bait, the fish buyer (fixed
/// prices), the fish market (live prices), records and the coaching stop. Items are shown as picture cards in a grid;
/// selecting one shows a large picture, the description and stat bars compared with what is in use, and the buy or
/// equip button. Opened by pressing E at a building door (only between casts).
/// </summary>
public partial class Shop : CanvasLayer
{
    private static readonly (string Key, string Title)[] TabDefinitions =
    {
        ("rod", "Wędki"), ("reel", "Kołowrotki"), ("line", "Linki"), ("leader", "Przypony"), ("hook", "Haczyki"),
        ("keepnet", "Siatki"), ("landingnet", "Kasarki"), ("accessory", "Akcesoria"),
        ("bait", "Przynęty"), ("fish", "Ryby"), ("market", "Targ"), ("records", "Rekordy"), ("travel", "Łowiska"),
    };

    private static readonly Color Gold = new(1f, 0.84f, 0.45f);
    private static readonly Color Muted = new(0.85f, 0.78f, 0.65f, 0.75f);

    // Browsing: the category rail, sub-categories, search, sort and the "only what I can buy" switch.
    private readonly Dictionary<string, Button> _rail = new();
    private HBoxContainer _chips = null!;
    private Control _filterBar = null!;
    private LineEdit _search = null!;
    private OptionButton _sort = null!;
    private CheckBox _onlyAvailable = null!;
    private readonly Dictionary<string, int> _subcategory = new();
    private bool _animateCards;
    private Control _root = null!;
    private TextureRect _coin = null!;
    private int _shownMoney = -1;

    private FishingRod _rod = null!;
    private Profile _profile = null!;
    private TackleLoadout _loadout = null!;
    private LakeBed _lake = null!;
    private GearCatalog? _gear;
    private TabContainer _tabs = null!;
    private readonly Dictionary<string, Container> _lists = new();
    private Label _money = null!, _message = null!, _title = null!;
    private Button _freeKit = null!;
    private bool _tackleCounter;

    /// <summary>The free-kit button (tests).</summary>
    public Button FreeKitButton => _freeKit;

    private void UpdateFreeKit()
    {
        bool needed = _tackleCounter && !_profile.HasKit;
        _freeKit.Visible = needed;
        if (!needed)
            return;
        int cost = _loadout.CostOfMissing();
        string missing = string.Join(", ", _profile.MissingKit().Select(m => m.MissingName()));
        bool can = _profile.CanClaimFreeKit(cost);
        _freeKit.Disabled = !can;
        _freeKit.Text = can
            ? $"Odbierz darmowy zestaw (brakuje: {missing})"
            : $"Brakuje: {missing}. Kup nowy sprzęt ({Lore.Money(cost)}): darmowy zestaw tylko dla tych, których na niego nie stać";
    }

    private void ClaimFreeKit()
    {
        string? error = _profile.ClaimFreeKit(_loadout.CostOfMissing());
        if (error == null)
        {
            _loadout.Resync();
            _rod.ApplyTackle();
        }
        _message.Text = error ?? "Kramarz wręcza Ci prosty zestaw. Powodzenia!";
    }

    // detail panel
    private TextureRect _detailIcon = null!;
    private Label _detailName = null!, _detailText = null!, _detailState = null!;
    private VBoxContainer _detailStats = null!;
    private Button _detailButton = null!;
    private Action? _detailAction;
    private object? _selected;

    public void Setup(FishingRod rod)
    {
        _rod = rod;
        _profile = rod.Profile;
        _loadout = rod.Loadout;
        _lake = rod.Lake;
        _gear = rod.Gear;
    }

    public TabContainer Tabs => _tabs;

    /// <summary>Selects the n-th card of the current tab (tools and tests).</summary>
    public void SelectCard(int n)
    {
        var key = TabDefinitions[_tabs.CurrentTab].Key;
        var cards = _lists[key].GetChildren().OfType<Button>().ToList();
        if (n >= 0 && n < cards.Count)
            cards[n].EmitSignal(BaseButton.SignalName.Pressed);
    }

    // ------------------------------------------------------------------ layout
    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        var dim = new ColorRect { Color = new Color(0.03f, 0.02f, 0.01f, 0.62f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var panel = new PanelContainer { Theme = UITheme.Make() };
        panel.AnchorLeft = panel.AnchorRight = panel.AnchorTop = panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = -630; panel.OffsetRight = 630; panel.OffsetTop = -350; panel.OffsetBottom = 350;
        panel.Resized += () => panel.PivotOffset = panel.Size / 2f;
        AddChild(panel);
        _root = panel;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        // Header: the stall's name, the purse with a coin, the angler's level.
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        box.AddChild(head);
        _title = new Label { Text = "Kram wędkarski", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _title.AddThemeFontSizeOverride("font_size", 28);
        _title.AddThemeColorOverride("font_color", Gold);
        _title.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        _title.AddThemeConstantOverride("shadow_offset_x", 2);
        _title.AddThemeConstantOverride("shadow_offset_y", 2);
        head.AddChild(_title);
        _coin = new TextureRect
        {
            Texture = UiArt.Coin, CustomMinimumSize = new Vector2(30, 30), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _coin.Resized += () => _coin.PivotOffset = _coin.Size / 2f;
        head.AddChild(_coin);
        _money = new Label { VerticalAlignment = VerticalAlignment.Center };
        _money.AddThemeFontSizeOverride("font_size", 22);
        _money.AddThemeColorOverride("font_color", Gold);
        head.AddChild(_money);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        box.AddChild(body);

        // The category rail: a big picture and a name for every kind of goods at this counter.
        var railScroll = new ScrollContainer { CustomMinimumSize = new Vector2(176, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(railScroll);
        var rail = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rail.AddThemeConstantOverride("separation", 4);
        railScroll.AddChild(rail);
        var railGroup = new ButtonGroup();

        var center = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        center.AddThemeConstantOverride("separation", 6);
        body.AddChild(center);
        center.AddChild(BuildFilterBar());

        _tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TabsVisible = false };
        center.AddChild(_tabs);
        foreach (var (key, tabTitle) in TabDefinitions)
        {
            var scroll = new ScrollContainer { Name = tabTitle, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            Container list = IsGridTab(key)
                ? new GridContainer { Columns = 4, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }
                : new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            list.AddThemeConstantOverride("h_separation", 10);
            list.AddThemeConstantOverride("v_separation", 10);
            list.AddThemeConstantOverride("separation", 8);
            var pad = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (var side in new[] { "left", "right", "top", "bottom" })
                pad.AddThemeConstantOverride("margin_" + side, 6);
            pad.AddChild(list);
            scroll.AddChild(pad);
            _tabs.AddChild(scroll);
            _lists[key] = list;

            var k = key;
            var railButton = new Button
            {
                Text = tabTitle, Icon = TabIcon(key), ToggleMode = true, ButtonGroup = railGroup, ExpandIcon = false,
                Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 50),
            };
            railButton.AddThemeConstantOverride("icon_max_width", 42);
            railButton.AddThemeConstantOverride("h_separation", 10);
            railButton.AddThemeFontSizeOverride("font_size", 16);
            railButton.AddThemeStyleboxOverride("pressed", UiArt.Button("gold"));
            railButton.AddThemeStyleboxOverride("hover_pressed", UiArt.Button("goldhover"));
            railButton.AddThemeColorOverride("font_pressed_color", new Color(0.2f, 0.11f, 0.04f));
            railButton.AddThemeColorOverride("font_hover_pressed_color", new Color(0.2f, 0.11f, 0.04f));
            railButton.Pressed += () => ShowCategory(k);
            rail.AddChild(railButton);
            _rail[key] = railButton;
        }
        _tabs.TabChanged += _ =>
        {
            if (_rail.TryGetValue(CurrentKey, out var current))
                current.SetPressedNoSignal(true);
            ShowNothing();
            UpdateFilterBar();
        };

        body.AddChild(BuildDetailPanel());

        _message = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _message.AddThemeColorOverride("font_color", Gold);
        _message.AddThemeFontSizeOverride("font_size", 18);
        box.AddChild(_message);
        // The free starter kit: only at the tackle counter, only for anglers with nothing to fish with and no money.
        _freeKit = new Button { CustomMinimumSize = new Vector2(0, 40), Visible = false };
        _freeKit.Pressed += ClaimFreeKit;
        box.AddChild(_freeKit);
        box.AddChild(new Label
        {
            Text = "Esc - wyjdź  |  kliknij kartę, aby zobaczyć szczegóły i porównać ze sprzętem w użyciu  |  lepszy sprzęt odblokowuje się z poziomem",
            Modulate = Muted, HorizontalAlignment = HorizontalAlignment.Center,
        });
        _profile.Changed += Refresh;
    }

    /// <summary>Shows a category (from the rail): its page, its sub-categories, cards that fade in.</summary>
    private void ShowCategory(string key)
    {
        int i = Array.FindIndex(TabDefinitions, d => d.Key == key);
        if (i < 0)
            return;
        _rod.Cue(Core.Audio.OneShot.Click, 0.6f);
        _animateCards = true;
        _tabs.CurrentTab = i;
        Refresh();
    }

    private string CurrentKey => _tabs.CurrentTab >= 0 ? TabDefinitions[_tabs.CurrentTab].Key : "";

    // ------------------------------------------------------------------ browsing: sub-categories, search, sort
    private static readonly string[] SortNames = { "Polecane", "Cena: od najtańszych", "Cena: od najdroższych", "Poziom", "Nazwa" };

    private Control BuildFilterBar()
    {
        // Two rows: the sub-categories, then search, order and the "only what I can buy" switch.
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 6);
        _chips = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _chips.AddThemeConstantOverride("separation", 4);
        rows.AddChild(_chips);
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);
        rows.AddChild(bar);
        _search = new LineEdit { PlaceholderText = "Szukaj…", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true };
        _search.TextChanged += _ =>
        {
            _animateCards = false;
            Refresh();
        };
        bar.AddChild(_search);
        _sort = new OptionButton { CustomMinimumSize = new Vector2(150, 0), TooltipText = "Kolejność" };
        foreach (var name in SortNames)
            _sort.AddItem(name);
        _sort.ItemSelected += _ =>
        {
            _animateCards = true;
            Refresh();
        };
        bar.AddChild(_sort);
        _onlyAvailable = new CheckBox { Text = "Dostępne", TooltipText = "Pokaż tylko to, co możesz kupić na swoim poziomie" };
        _onlyAvailable.Toggled += _ =>
        {
            _animateCards = true;
            Refresh();
        };
        bar.AddChild(_onlyAvailable);
        _filterBar = rows;
        return rows;
    }

    /// <summary>The sub-categories of a page: a name and which items belong to it ("Wszystkie" first).</summary>
    private static List<(string Name, Func<TackleItem, bool> Has)> Subcategories(string key)
    {
        var all = new List<(string, Func<TackleItem, bool>)> { ("Wszystkie", _ => true) };
        switch (key)
        {
            case "rod":
                all.Add(("Spławikowe", i => i is RodData { Kind: RodKind.Float }));
                all.Add(("Spinningowe", i => i is RodData { Kind: RodKind.Spinning }));
                all.Add(("Gruntowe", i => i is RodData { Kind: RodKind.Bottom }));
                all.Add(("Karpiowe", i => i is RodData { Kind: RodKind.Carp }));
                break;
            case "reel":
                all.Add(("Lekkie", i => i is ReelData { LineCapacity: <= 80f }));
                all.Add(("Uniwersalne", i => i is ReelData { LineCapacity: > 80f and <= 130f }));
                all.Add(("Duże", i => i is ReelData { LineCapacity: > 130f }));
                break;
            case "line":
                all.Add(("Żyłki", i => i is LineData { Kind: LineKind.Mono }));
                all.Add(("Plecionki", i => i is LineData { Kind: LineKind.Braid }));
                break;
            case "leader":
                all.Add(("Niewidoczne", i => i is LeaderData { BiteProof: <= 0f, Visibility: > 0f }));
                all.Add(("Z drutu", i => i is LeaderData { BiteProof: > 0f }));
                break;
            case "hook":
                all.Add(("Małe", i => i is HookData { Size: <= 3f }));
                all.Add(("Średnie", i => i is HookData { Size: > 3f and < 7f }));
                all.Add(("Duże", i => i is HookData { Size: >= 7f }));
                break;
            case "bait":
                all.Add(("Naturalne", i => i is BaitData { Kind: BaitKind.Natural }));
                all.Add(("Roślinne", i => i is BaitData { Kind: BaitKind.Plant }));
                all.Add(("Karpiowe", i => i is BaitData { Kind: BaitKind.Carp }));
                all.Add(("Sztuczne", i => i is BaitData { Kind: BaitKind.Lure }));
                break;
        }
        return all;
    }

    private void UpdateFilterBar()
    {
        string key = CurrentKey;
        bool goods = IsGoods(key);
        _filterBar.Visible = goods || key == "records";
        _sort.Visible = _onlyAvailable.Visible = goods;
        _chips.Visible = goods && Subcategories(key).Count > 1;
        foreach (var c in _chips.GetChildren())
            c.QueueFree();
        if (!goods)
            return;
        var subs = Subcategories(key);
        if (subs.Count <= 1)
            return;
        var group = new ButtonGroup();
        int current = _subcategory.GetValueOrDefault(key);
        for (int i = 0; i < subs.Count; i++)
        {
            int index = i;
            var chip = new Button { Text = subs[i].Name, ToggleMode = true, ButtonGroup = group, ButtonPressed = i == current };
            chip.AddThemeFontSizeOverride("font_size", 14);
            chip.AddThemeStyleboxOverride("normal", UiArt.Button("normal", 10, 4));
            chip.AddThemeStyleboxOverride("hover", UiArt.Button("hover", 10, 4));
            chip.AddThemeStyleboxOverride("pressed", UiArt.Button("gold", 10, 4));
            chip.AddThemeStyleboxOverride("hover_pressed", UiArt.Button("goldhover", 10, 4));
            chip.AddThemeColorOverride("font_pressed_color", new Color(0.2f, 0.11f, 0.04f));
            chip.AddThemeColorOverride("font_hover_pressed_color", new Color(0.2f, 0.11f, 0.04f));
            chip.Pressed += () =>
            {
                _subcategory[key] = index;
                _rod.Cue(Core.Audio.OneShot.Click, 0.5f);
                _animateCards = true;
                Refresh();
            };
            _chips.AddChild(chip);
        }
    }

    private static bool IsGoods(string key) => key is "rod" or "reel" or "line" or "leader" or "hook" or "bait" or "keepnet" or "landingnet" or "accessory";

    /// <summary>The items of a page as the browsing controls want them: in the chosen sub-category, matching the search,
    /// buyable if so asked, in the chosen order.</summary>
    private List<(int Index, TackleItem Item)> Browse(string key, IReadOnlyList<TackleItem> items)
    {
        var subs = Subcategories(key);
        var has = subs[Math.Clamp(_subcategory.GetValueOrDefault(key), 0, subs.Count - 1)].Has;
        string q = _search.Text.Trim().ToLowerInvariant();
        var list = items.Select((item, i) => (Index: i, Item: item))
            .Where(p => has(p.Item))
            .Where(p => q == "" || p.Item.DisplayName.ToLowerInvariant().Contains(q))
            .Where(p => !_onlyAvailable.ButtonPressed || p.Item.RequiredLevel <= _profile.Level)
            .ToList();
        return _sort.Selected switch
        {
            1 => list.OrderBy(p => p.Item.Price).ToList(),
            2 => list.OrderByDescending(p => p.Item.Price).ToList(),
            3 => list.OrderBy(p => p.Item.RequiredLevel).ThenBy(p => p.Item.Price).ToList(),
            4 => list.OrderBy(p => p.Item.DisplayName).ToList(),
            _ => list.OrderBy(p => p.Item.RequiredLevel > _profile.Level ? 1 : 0).ThenBy(p => p.Item.RequiredLevel).ThenBy(p => p.Item.Price).ToList(),
        };
    }

    private static bool IsGridTab(string key) => key is "rod" or "reel" or "line" or "leader" or "hook" or "keepnet" or "landingnet" or "accessory" or "bait" or "records";

    private Texture2D TabIcon(string key) => key switch
    {
        "records" => Icons.Fish(_rod.Species.Count > 0 ? _rod.Species[0] : new FishSpecies()),
        "fish" or "market" => Icons.Fish(_rod.Species.Count > 2 ? _rod.Species[2] : new FishSpecies()),
        "travel" => Icons.Category("rod"),
        _ => Icons.Category(key),
    };


    private Label _detailTier = null!;
    private ColorRect _flash = null!;

    private Control BuildDetailPanel()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        panel.AddThemeStyleboxOverride("panel", UiArt.Nine(UiArt.InsetTexture, 4, 12));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        // The picture on parchment, in a pool of light.
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(0, 146) };
        frame.AddThemeStyleboxOverride("panel", UiArt.Nine(UiArt.ParchmentTexture, 10, 4));
        box.AddChild(frame);
        frame.AddChild(new TextureRect { Texture = UiArt.Spotlight, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale });
        _detailIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(136, 136),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
        };
        _detailIcon.Resized += () => _detailIcon.PivotOffset = _detailIcon.Size / 2f;
        frame.AddChild(_detailIcon);
        _flash = new ColorRect { Color = new Color(1f, 0.92f, 0.6f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddChild(_flash);
        _detailName = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center };
        _detailName.AddThemeFontSizeOverride("font_size", 20);
        _detailName.AddThemeColorOverride("font_color", Gold);
        _detailName.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        _detailName.AddThemeConstantOverride("shadow_offset_y", 2);
        box.AddChild(_detailName);
        _detailTier = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _detailTier.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(_detailTier);
        _detailText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = Muted };
        box.AddChild(_detailText);
        _detailStats = new VBoxContainer();
        _detailStats.AddThemeConstantOverride("separation", 2);
        box.AddChild(_detailStats);
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        _detailState = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        box.AddChild(_detailState);
        _detailButton = new Button { CustomMinimumSize = new Vector2(0, 46), Icon = UiArt.Coin, ExpandIcon = false };
        _detailButton.AddThemeConstantOverride("icon_max_width", 26);
        _detailButton.AddThemeFontSizeOverride("font_size", 18);
        UITheme.Primary(_detailButton);
        _detailButton.Pressed += () => _detailAction?.Invoke();
        box.AddChild(_detailButton);
        return panel;
    }

    /// <summary>A purchase: the picture flashes, coins burst from the button, the purse jingles.</summary>
    private void PurchaseEffect()
    {
        _rod.Cue(Core.Audio.OneShot.Coins, 1f);
        _flash.Color = new Color(1f, 0.92f, 0.6f, 0.65f);
        _flash.CreateTween().TweenProperty(_flash, "color:a", 0f, 0.45f);
        var pop = _detailIcon.CreateTween();
        pop.TweenProperty(_detailIcon, "scale", new Vector2(1.12f, 1.12f), 0.08f);
        pop.TweenProperty(_detailIcon, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        var coins = new CpuParticles2D
        {
            Texture = UiArt.Coin, Amount = 22, OneShot = true, Explosiveness = 0.9f, Lifetime = 1.0,
            Direction = new Vector2(0, -1), Spread = 65f, InitialVelocityMin = 220f, InitialVelocityMax = 380f,
            Gravity = new Vector2(0, 900f), ScaleAmountMin = 0.45f, ScaleAmountMax = 0.8f,
            AngularVelocityMin = -360f, AngularVelocityMax = 360f, Position = _detailButton.Size / 2f,
        };
        _detailButton.AddChild(coins);
        coins.Emitting = true;
        GetTree().CreateTimer(1.4).Timeout += coins.QueueFree;
    }

    public override void _ExitTree()
    {
        if (_profile != null)
            _profile.Changed -= Refresh;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private static readonly Dictionary<ShopKind, (string Title, string[] Tabs)> Kinds = new()
    {
        [ShopKind.Tackle] = ("Kram wędkarski", new[] { "rod", "reel", "line", "leader", "hook", "keepnet", "landingnet", "accessory" }),
        [ShopKind.Bait] = ("Kram z przynętami", new[] { "bait" }),
        [ShopKind.FishBuyer] = ("Skup ryb", new[] { "fish", "records" }),
        [ShopKind.Market] = ("Targ rybny", new[] { "market", "records" }),
        [ShopKind.General] = ("Kram wędkarski i przynęty",
            new[] { "rod", "reel", "line", "leader", "hook", "keepnet", "landingnet", "accessory", "bait", "fish", "records" }),
        [ShopKind.Travel] = ("Postój wozów: dokąd jedziesz?", new[] { "travel" }),
    };

    /// <summary>Opens the shop showing only the tabs that belong to this kind of counter.</summary>
    public void OpenFor(ShopKind kind)
    {
        if (Visible || _rod.State != RodState.Idle)
            return;
        var (title, tabs) = Kinds[kind];
        _title.Text = title;
        _tackleCounter = tabs.Contains("rod");
        int first = -1;
        for (int i = 0; i < TabDefinitions.Length; i++)
        {
            bool shown = tabs.Contains(TabDefinitions[i].Key);
            _tabs.SetTabHidden(i, !shown);
            _rail[TabDefinitions[i].Key].Visible = shown;
            if (shown && first < 0)
                first = i;
        }
        Visible = true;
        _tabs.CurrentTab = Math.Max(first, 0);
        _rail[TabDefinitions[_tabs.CurrentTab].Key].ButtonPressed = true;
        _rail.Values.First(b => b.Visible).GetParent<Control>().GetParent<Control>().Visible = tabs.Length > 1;
        UpdateFilterBar();
        _animateCards = true;
        _shownMoney = -1;
        // The stall opens: the board fades in and settles.
        _root.Modulate = new Color(1, 1, 1, 0);
        _root.Scale = new Vector2(0.97f, 0.97f);
        var open = _root.CreateTween().SetParallel();
        open.TweenProperty(_root, "modulate:a", 1f, 0.18f);
        open.TweenProperty(_root, "scale", Vector2.One, 0.22f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _rod.InputBlocked = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _message.Text = "";
        ShowNothing();
        Refresh();
    }

    /// <summary>Opens the all-in-one shop, or closes the shop when it is open (tools and tests).</summary>
    public void Toggle()
    {
        if (Visible)
            Close();
        else
            OpenFor(ShopKind.General);
    }

    public void Close()
    {
        Visible = false;
        _rod.InputBlocked = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void Refresh()
    {
        if (!Visible)
            return;
        ShowMoney();
        UpdateFreeKit();
        for (int s = 0; s < TackleSlotExtensions.Count; s++)
            FillTackle((TackleSlot)s);
        foreach (var slot in GearSlotExtensions.All)
            FillGear(slot);
        FillFish();
        FillMarket();
        FillRecords();
        FillTravel();
        if (_selected != null)
            Reselect();
        else
            ShowNothing();
        _animateCards = false;
    }

    /// <summary>The purse: counts to the new sum and the coin jumps when it changes.</summary>
    private void ShowMoney()
    {
        int target = _profile.Money;
        string Text(int m) => $"{Lore.Money(m)}   ·   poziom {_profile.Level}";
        if (_shownMoney < 0 || _shownMoney == target)
        {
            _shownMoney = target;
            _money.Text = Text(target);
            return;
        }
        int from = _shownMoney;
        _shownMoney = target;
        var tw = _money.CreateTween();
        tw.TweenMethod(Callable.From<float>(v => _money.Text = Text(Mathf.RoundToInt(v))), (float)from, (float)target, 0.5f);
        var jump = _coin.CreateTween();
        jump.TweenProperty(_coin, "scale", new Vector2(1.35f, 1.35f), 0.08f);
        jump.TweenProperty(_coin, "scale", Vector2.One, 0.25f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private static void Clear(Container list)
    {
        foreach (var c in list.GetChildren())
            c.QueueFree();
    }

    // ------------------------------------------------------------------ cards
    private enum Badge { None, Owned, InUse, Locked }

    /// <summary>Quality tiers by the level that unlocks an item: the colour of the card's ribbon and its name.</summary>
    private static (Color Color, string Name) Tier(int level) => level switch
    {
        <= 1 => (new Color(0.62f, 0.5f, 0.36f), "zwykły"),
        <= 3 => (new Color(0.42f, 0.68f, 0.32f), "dobry"),
        <= 5 => (new Color(0.36f, 0.56f, 0.86f), "wyborny"),
        _ => (new Color(0.86f, 0.5f, 0.92f), "mistrzowski"),
    };

    /// <summary>
    /// A goods card: a quality ribbon, the picture on parchment in a pool of light, the name and a price or state line,
    /// a seal for owned and in-use goods, a padlock over locked ones. It grows a little under the mouse.
    /// </summary>
    private Button Card(Container list, Texture2D icon, string name, string status, Color statusColor, Action onSelect, bool dark = false,
        int level = 0, Badge badge = Badge.None, bool price = false)
    {
        var card = new Button { CustomMinimumSize = new Vector2(138, 180), ClipText = true, FocusMode = Control.FocusModeEnum.None };
        card.AddThemeStyleboxOverride("normal", UiArt.Button("normal", 6, 6));
        card.AddThemeStyleboxOverride("hover", UiArt.Button("hover", 6, 6));
        card.AddThemeStyleboxOverride("pressed", UiArt.Button("goldpressed", 6, 6));
        card.Resized += () => card.PivotOffset = card.Size / 2f;
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.OffsetLeft = 7; box.OffsetRight = -7; box.OffsetTop = 7; box.OffsetBottom = -6;
        card.AddChild(box);
        if (level > 0)
        {
            var tier = Tier(level);
            var ribbon = new ColorRect { Color = tier.Color, CustomMinimumSize = new Vector2(0, 4), MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddChild(ribbon);
        }
        var frame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(0, 96) };
        frame.AddThemeStyleboxOverride("panel", UiArt.Nine(UiArt.ParchmentTexture, 10, 2));
        box.AddChild(frame);
        frame.AddChild(new TextureRect
        {
            Texture = UiArt.Spotlight, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        frame.AddChild(new TextureRect
        {
            Texture = icon,
            CustomMinimumSize = new Vector2(92, 92),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
            Modulate = dark ? new Color(0.08f, 0.06f, 0.05f) : badge == Badge.Locked ? new Color(0.62f, 0.58f, 0.54f) : Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        if (badge != Badge.None)
        {
            // The seal in the corner (owned / in use) or a padlock in the middle (locked).
            var mark = new TextureRect
            {
                Texture = badge switch { Badge.Owned => UiArt.Owned, Badge.InUse => UiArt.Star, _ => UiArt.Lock },
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                CustomMinimumSize = badge == Badge.Locked ? new Vector2(40, 40) : new Vector2(28, 28),
                SizeFlagsHorizontal = badge == Badge.Locked ? Control.SizeFlags.ShrinkCenter : Control.SizeFlags.ShrinkEnd,
                SizeFlagsVertical = badge == Badge.Locked ? Control.SizeFlags.ShrinkCenter : Control.SizeFlags.ShrinkBegin,
            };
            frame.AddChild(mark);
        }
        var title = new Label
        {
            Text = name,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(120, 36),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(title);
        var line = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 4);
        if (price)
            line.AddChild(new TextureRect
            {
                Texture = UiArt.Coin, CustomMinimumSize = new Vector2(16, 16), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        var st = new Label { Text = status, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        st.AddThemeFontSizeOverride("font_size", 13);
        st.AddThemeColorOverride("font_color", statusColor);
        line.AddChild(st);
        box.AddChild(line);
        card.Pressed += () =>
        {
            _rod.Cue(Core.Audio.OneShot.Click, 0.5f);
            onSelect();
        };
        // Under the mouse the card comes forward a little.
        card.MouseEntered += () =>
        {
            card.ZIndex = 1;
            card.CreateTween().TweenProperty(card, "scale", new Vector2(1.05f, 1.05f), 0.09f).SetTrans(Tween.TransitionType.Sine);
        };
        card.MouseExited += () =>
        {
            card.ZIndex = 0;
            card.CreateTween().TweenProperty(card, "scale", Vector2.One, 0.12f).SetTrans(Tween.TransitionType.Sine);
        };
        list.AddChild(card);
        if (_animateCards)
        {
            // A new page: the cards come in one after another.
            int n = list.GetChildCount() - 1;
            card.Modulate = new Color(1, 1, 1, 0);
            var tw = card.CreateTween();
            tw.TweenInterval(Math.Min(n * 0.025f, 0.45f));
            tw.TweenProperty(card, "modulate:a", 1f, 0.16f);
        }
        return card;
    }

    private static readonly Color Owned = new(0.6f, 0.9f, 0.5f), Locked = new(0.9f, 0.5f, 0.4f), Price = new(1f, 0.84f, 0.45f);

    private void FillTackle(TackleSlot slot)
    {
        var list = _lists[slot.Key()];
        Clear(list);
        foreach (var (i, item) in Browse(slot.Key(), _loadout.Items(slot)))
        {
            int index = i;
            bool locked = _profile.Level < item.RequiredLevel;
            string status;
            Color color;
            Badge badge;
            bool price = false;
            if (slot == TackleSlot.Bait)
            {
                int have = _profile.BaitCounts.GetValueOrDefault(item.ItemId);
                badge = locked ? Badge.Locked : _loadout.Index[(int)slot] == i && have > 0 ? Badge.InUse : have > 0 ? Badge.Owned : Badge.None;
                (status, color) = locked ? ($"poziom {item.RequiredLevel}", Locked) : ($"{item.Price} gr · masz {have}", Price);
                price = !locked;
            }
            else if (_profile.Owns(slot, item.ItemId))
            {
                bool worn = _loadout.Index[(int)slot] == i;
                (status, color) = worn ? ("w użyciu", Owned) : ("posiadane", Owned);
                badge = worn ? Badge.InUse : Badge.Owned;
            }
            else
            {
                (status, color) = locked ? ($"poziom {item.RequiredLevel}", Locked) : (Lore.Money(item.Price), Price);
                badge = locked ? Badge.Locked : Badge.None;
                price = !locked;
            }
            Card(list, Icons.For(item), item.DisplayName, status, color, () => SelectTackle(slot, index), level: item.RequiredLevel, badge: badge, price: price);
        }
        if (list.GetChildCount() == 0)
            list.AddChild(new Label { Text = "Nic tu nie pasuje do wybranych filtrów.", Modulate = Muted });
    }

    private void FillGear(GearSlot slot)
    {
        var list = _lists[slot.Key()];
        Clear(list);
        if (_gear == null)
            return;
        foreach (var (_, item) in Browse(slot.Key(), _gear.Items(slot)))
        {
            var it = item;
            bool locked = _profile.Level < item.RequiredLevel;
            bool owned = _profile.Gear.Owns(slot, item.ItemId);
            bool used = owned && (!slot.IsEquippable() || _profile.Gear.EquippedId(slot) == item.ItemId);
            var (status, color) = used ? ("w użyciu", Owned) : owned ? ("posiadane", Owned)
                : locked ? ($"poziom {item.RequiredLevel}", Locked) : (Lore.Money(item.Price), Price);
            var badge = used ? Badge.InUse : owned ? Badge.Owned : locked ? Badge.Locked : Badge.None;
            Card(list, Icons.For(item), item.DisplayName, status, color, () => SelectGear(slot, it), level: item.RequiredLevel, badge: badge,
                price: !owned && !locked);
        }
        if (list.GetChildCount() == 0)
            list.AddChild(new Label { Text = "Nic tu nie pasuje do wybranych filtrów.", Modulate = Muted });
    }

    // ------------------------------------------------------------------ detail panel
    private void ShowNothing()
    {
        _selected = null;
        _reselect = null;
        _detailIcon.Modulate = Colors.White;
        _detailState.Text = "";
        if (_detailTier != null)
            _detailTier.Text = "";
        Clear(_detailStats);
        _detailButton.Visible = false;
        string key = _tabs.CurrentTab >= 0 ? TabDefinitions[_tabs.CurrentTab].Key : "";
        if (key is "fish" or "market")
        {
            // The keepnet at a glance, with the biggest fish in the picture.
            var biggest = _profile.Keepnet.OrderByDescending(f => f.Weight).FirstOrDefault();
            var sp = biggest != null ? SpeciesNamed(biggest.Species) : null;
            _detailIcon.Texture = sp != null ? Icons.Fish(sp) : Icons.Category("keepnet");
            _detailName.Text = biggest != null ? $"Największa: {biggest.Species} {biggest.Weight:0.00} kg" : "Pusta siatka";
            _detailText.Text = _profile.Keepnet.Count == 0 ? "Złów coś, a potem wróć tutaj sprzedać ryby."
                : $"W siatce: {_profile.Keepnet.Count} ryb, {_profile.KeepnetWeight:0.0} kg.\nSkup płaci stałą cenę, targ: cenę dnia z premią za świeże i trofeowe ryby.";
            _detailState.Text = _profile.Keepnet.Count == 0 ? "" : $"Skup zapłaci: {Lore.Money(_profile.KeepnetValue - _profile.UndersizedCount * Profile.UndersizedFine)}";
            return;
        }
        if (key == "travel")
        {
            _detailIcon.Texture = Postcard.For(_lake.Location);
            _detailName.Text = $"Jesteś: {_lake.Location.DisplayName}";
            _detailText.Text = "Wóz zawiezie cię na inne łowisko. Każde ma swoje ryby, wodę i pogodę.";
            return;
        }
        _detailIcon.Texture = null;
        _detailName.Text = "Wybierz towar";
        _detailText.Text = "Kliknij kartę po lewej, aby zobaczyć szczegóły i porównać ze sprzętem, którego używasz.";
    }

    private Action? _reselect;

    private void Reselect() => _reselect?.Invoke();

    private void Show(object key, Texture2D icon, string name, string text, IEnumerable<(string Label, float Value, float Current, string Shown)> stats,
        string state, string? button, bool enabled, Action? action)
    {
        bool fresh = !Equals(_selected, key);
        _selected = key;
        _detailIcon.Modulate = Colors.White;
        _detailIcon.Texture = icon;
        _detailName.Text = name;
        _detailText.Text = text;
        _detailState.Text = state;
        if (key is TackleItem item)
        {
            var tier = Tier(item.RequiredLevel);
            _detailTier.Text = $"{KindName(item)} · {tier.Name} · poziom {item.RequiredLevel}";
            _detailTier.AddThemeColorOverride("font_color", tier.Color);
        }
        else
        {
            _detailTier.Text = "";
        }
        Clear(_detailStats);
        foreach (var (label, value, current, shown) in stats)
            _detailStats.AddChild(StatBar(label, value, current, shown, fresh));
        if (fresh)
        {
            // A new item: its picture pops into the light.
            _detailIcon.Scale = new Vector2(0.82f, 0.82f);
            _detailIcon.CreateTween().TweenProperty(_detailIcon, "scale", Vector2.One, 0.28f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
        _detailButton.Visible = button != null;
        _detailButton.Text = button ?? "";
        _detailButton.Disabled = !enabled;
        _detailAction = action;
    }

    /// <summary>What kind of thing an item is, in a few words (the detail panel's subtitle).</summary>
    private static string KindName(TackleItem item) => item switch
    {
        RodData r => r.Kind switch { RodKind.Spinning => "wędka spinningowa", RodKind.Bottom => "wędka gruntowa", RodKind.Carp => "wędka karpiowa", _ => "wędka spławikowa" },
        ReelData => "kołowrotek",
        LineData l => l.Kind == LineKind.Braid ? "plecionka" : "żyłka",
        LeaderData => "przypon",
        HookData => "haczyk",
        BaitData b => b.Kind switch { BaitKind.Lure => "przynęta sztuczna", BaitKind.Plant => "przynęta roślinna", BaitKind.Carp => "przynęta karpiowa", _ => "przynęta naturalna" },
        KeepnetData => "siatka",
        LandingNetData => "kasarek",
        _ => "akcesorium",
    };

    /// <summary>A labelled bar (0..1) with a tick for the item in use, so better and worse read at a glance.</summary>
    private static Control StatBar(string label, float value, float current, string shown, bool animate = false)
    {
        var row = new VBoxContainer();
        row.AddThemeConstantOverride("separation", 1);
        var top = new HBoxContainer();
        top.AddChild(new Label { Text = label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Modulate = Muted });
        var val = new Label { Text = shown };
        val.AddThemeColorOverride("font_color", current < 0f ? Colors.White : value > current + 0.01f ? Owned : value < current - 0.01f ? Locked : Colors.White);
        top.AddChild(val);
        row.AddChild(top);
        var bar = new ProgressBar { ShowPercentage = false, MaxValue = 1, Step = 0, Value = animate ? 0f : Math.Clamp(value, 0f, 1f), CustomMinimumSize = new Vector2(0, 12) };
        row.AddChild(bar);
        if (animate)
            bar.CreateTween().TweenProperty(bar, "value", (double)Math.Clamp(value, 0f, 1f), 0.4f)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        if (current >= 0f)
        {
            var tick = new ColorRect { Color = Colors.White, CustomMinimumSize = new Vector2(2, 14), MouseFilter = Control.MouseFilterEnum.Ignore };
            bar.AddChild(tick);
            tick.AnchorLeft = tick.AnchorRight = Math.Clamp(current, 0f, 1f);
            tick.OffsetLeft = -1; tick.OffsetRight = 1; tick.OffsetTop = -2; tick.OffsetBottom = 12;
        }
        return row;
    }

    private static IEnumerable<(string, float, float, string)> Stats(TackleItem item, TackleItem? current)
    {
        switch (item)
        {
            case RodData r:
                var cr = current as RodData;
                yield return ("Zasięg rzutu", r.CastMult / 1.4f, cr?.CastMult / 1.4f ?? -1f, $"x{r.CastMult:0.00}");
                yield return ("Wytrzymałość", r.MaxLoad / 18f, cr?.MaxLoad / 18f ?? -1f, $"{r.MaxLoad:0} kg");
                yield return ("Amortyzacja zrywów", r.ShockAbsorb / 0.5f, cr?.ShockAbsorb / 0.5f ?? -1f, $"{r.ShockAbsorb * 200f:0}%");
                yield return ("Akcja (szczytowa → paraboliczna)", r.Action, cr?.Action ?? -1f, r.Action < 0.34f ? "szczytowa" : r.Action < 0.67f ? "średnia" : "paraboliczna");
                break;
            case ReelData r:
                var ce = current as ReelData;
                yield return ("Siła hamulca", r.DragMax / 14f, ce?.DragMax / 14f ?? -1f, $"{r.DragMax:0} kg");
                yield return ("Szybkość zwijania", r.ReelSpeed / 4f, ce?.ReelSpeed / 4f ?? -1f, $"{r.ReelSpeed:0.0} m/s");
                yield return ("Pojemność szpuli", r.LineCapacity / 220f, ce?.LineCapacity / 220f ?? -1f, $"{r.LineCapacity:0} m");
                break;
            case LineData l:
                var cl = current as LineData;
                yield return ("Wytrzymałość", l.Strength / 26f, cl?.Strength / 26f ?? -1f, $"{l.Strength:0} kg");
                yield return ("Niewidoczność w wodzie", 1f / MathF.Max(l.Visibility, 0.5f) * 0.6f, cl != null ? 1f / MathF.Max(cl.Visibility, 0.5f) * 0.6f : -1f, $"x{l.Visibility:0.00}");
                yield return ("Rozciągliwość (tłumi zrywy)", l.Stretch / 0.6f, cl != null ? cl.Stretch / 0.6f : -1f, l.Kind == LineKind.Braid ? "plecionka" : $"{l.Stretch * 100f:0}%");
                break;
            case LeaderData d:
                var cd = current as LeaderData;
                bool none = d.ItemId == TackleSlotExtensions.NoLeader;
                yield return ("Wytrzymałość", none ? 1f : d.Strength / 16f, cd == null ? -1f : cd.ItemId == TackleSlotExtensions.NoLeader ? 1f : cd.Strength / 16f, none ? "jak linka" : $"{d.Strength:0} kg");
                yield return ("Niewidoczność w wodzie", none ? 0.6f : 1f / MathF.Max(d.Visibility, 0.5f) * 0.6f, cd == null ? -1f : cd.Visibility <= 0f ? 0.6f : 1f / MathF.Max(cd.Visibility, 0.5f) * 0.6f, none ? "jak linka" : $"x{d.Visibility:0.00}");
                yield return ("Odporność na zęby", d.BiteProof, cd?.BiteProof ?? -1f, $"{d.BiteProof * 100f:0}%");
                break;
            case HookData h:
                var ch = current as HookData;
                yield return ("Rozmiar", h.Size / 10f, ch?.Size / 10f ?? -1f, $"#{h.Size:0}");
                break;
            case KeepnetData k:
                var ck = current as KeepnetData;
                yield return ("Ryb w siatce", k.MaxFish / 50f, ck?.MaxFish / 50f ?? -1f, $"{k.MaxFish}");
                yield return ("Udźwig", k.MaxKg / 60f, ck?.MaxKg / 60f ?? -1f, $"{k.MaxKg:0} kg");
                yield return ("Świeżość ryb", 1f - k.ConditionLossPerHour / 0.06f, ck != null ? 1f - ck.ConditionLossPerHour / 0.06f : -1f, $"-{k.ConditionLossPerHour * 100f:0.#}%/h");
                break;
            case LandingNetData n:
                var cn = current as LandingNetData;
                yield return ("Największa ryba", n.MaxWeight / 25f, cn?.MaxWeight / 25f ?? -1f, $"{n.MaxWeight:0.#} kg");
                yield return ("Długość rączki", n.Reach / 3.5f, cn?.Reach / 3.5f ?? -1f, $"{n.Reach:0.#} m");
                break;
            case BaitData { Reusable: true }:
                break;
            case BaitData b:
                yield return ("W paczce", b.PackSize / 20f, -1f, $"{b.PackSize} szt.");
                break;
        }
    }

    private static string Describe(TackleItem item) => item.Description != "" && item is not AccessoryData
        ? item.Description + (item is BaitData { Kind: BaitKind.Lure } ? "\nPrzynęta sztuczna: nie zużywa się, bierze tylko podczas zwijania (LPM)." : "")
        : item switch
    {
        RodData => "Wędka decyduje o zasięgu rzutu i o tym, ile ryba może pociągnąć, zanim kij pęknie.",
        ReelData => "Kołowrotek: hamulec oddaje linkę uciekającej rybie, szpula mieści zapas linki.",
        LineData => "Mocniejsza linka wytrzyma więcej, ale grubszą ryby łatwiej zauważą.",
        HookData => "Większy haczyk lepiej trzyma duże ryby, drobnica go nie połknie.",
        BaitData => "Przynęta: każdy gatunek ma swoje ulubione. Atlas w dzienniku podpowie.",
        KeepnetData => "Siatka trzyma złowione ryby do sprzedaży. Ciasna szybciej je męczy.",
        LandingNetData => "Kasarek (Spacja) wyjmuje rybę przy brzegu. Za mały nie utrzyma dużej.",
        AccessoryData a => a.Description,
        _ => "",
    };

    private void SelectTackle(TackleSlot slot, int index)
    {
        _reselect = () => SelectTackle(slot, index);
        var item = _loadout.Items(slot)[index];
        bool locked = _profile.Level < item.RequiredLevel;
        var current = slot == TackleSlot.Bait ? null : _loadout.Selected(slot);
        string? button;
        bool enabled;
        string state;
        Action? action;
        if (slot == TackleSlot.Bait)
        {
            int pack = ((BaitData)item).PackSize;
            state = $"Masz: {_profile.BaitCounts.GetValueOrDefault(item.ItemId)} szt.";
            button = locked ? $"Wymaga poziomu {item.RequiredLevel}"
                : pack == 1 ? $"Kup za {Lore.Money(item.Price)}" : $"Kup paczkę ({pack} szt.) za {Lore.Money(item.Price)}";
            enabled = !locked && _profile.Money >= item.Price;
            action = () => Buy(slot, index);
        }
        else if (_profile.Owns(slot, item.ItemId))
        {
            bool worn = _loadout.Index[(int)slot] == index;
            state = worn ? "Tego używasz." : "Masz to w ekwipunku.";
            button = worn ? "W użyciu" : "Załóż";
            enabled = !worn;
            action = () => Equip(slot, index);
        }
        else
        {
            state = locked ? $"Odblokujesz na poziomie {item.RequiredLevel}." : $"Cena: {Lore.Money(item.Price)}";
            button = locked ? "Zablokowane" : $"Kup za {Lore.Money(item.Price)}";
            enabled = !locked && _profile.Money >= item.Price;
            action = () => Buy(slot, index);
        }
        Show(item, Icons.For(item), item.DisplayName, Describe(item), Stats(item, current), state, button, enabled, action);
    }

    private void SelectGear(GearSlot slot, TackleItem item)
    {
        _reselect = () => SelectGear(slot, item);
        if (_gear == null)
            return;
        bool locked = _profile.Level < item.RequiredLevel;
        bool owned = _profile.Gear.Owns(slot, item.ItemId);
        var current = slot.IsEquippable() ? _gear.Find(slot, _profile.Gear.EquippedId(slot)) : null;
        string? button;
        bool enabled;
        string state;
        Action? action;
        if (owned && slot.IsEquippable())
        {
            bool used = _profile.Gear.EquippedId(slot) == item.ItemId;
            state = used ? "Tego używasz." : "Masz to w ekwipunku.";
            button = used ? "W użyciu" : "Używaj";
            enabled = !used;
            action = () => { _gear.Equip(slot, item); _message.Text = $"Używasz: {item.DisplayName}"; };
        }
        else if (owned)
        {
            state = "Masz to: działa samo.";
            button = null;
            enabled = false;
            action = null;
        }
        else
        {
            state = locked ? $"Odblokujesz na poziomie {item.RequiredLevel}." : $"Cena: {Lore.Money(item.Price)}";
            button = locked ? "Zablokowane" : $"Kup za {Lore.Money(item.Price)}";
            enabled = !locked && _profile.Money >= item.Price;
            action = () =>
            {
                string? error = _gear.Buy(slot, item);
                _message.Text = error ?? $"Kupiono: {item.DisplayName}";
                if (error == null)
                    PurchaseEffect();
            };
        }
        Show(item, Icons.For(item), item.DisplayName, Describe(item), Stats(item, current), state, button, enabled, action);
    }

    private void Buy(TackleSlot slot, int index)
    {
        var item = _loadout.Items(slot)[index];
        int pack = item is BaitData bait ? bait.PackSize : 1;
        bool hadOne = slot == TackleSlot.Bait || _profile.Owns(slot, _loadout.Selected(slot).ItemId);
        string? error = _profile.Buy(slot, item.ItemId, item.Price, item.RequiredLevel, pack);
        _message.Text = error ?? $"Kupiono: {item.DisplayName}";
        if (error == null)
            PurchaseEffect();
        // Replacing a destroyed piece: put the new one on straight away.
        if (error == null && !hadOne)
            Equip(slot, index);
    }

    private void Equip(TackleSlot slot, int index)
    {
        _loadout.Index[(int)slot] = index;
        _loadout.SyncToProfile();
        _rod.ApplyTackle();
        _profile.NotifyChanged();
        _message.Text = $"Założono: {_loadout.Items(slot)[index].DisplayName}";
    }

    // ------------------------------------------------------------------ fish: buyer, market, records
    private FishSpecies? SpeciesNamed(string name) => _rod.Species.FirstOrDefault(s => s.SpeciesName == name);

    /// <summary>A row with a fish picture: name, details and an optional button.</summary>
    private void FishRow(Container list, string species, string title, string details, string? button, Action? onPressed, string? price = null)
    {
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", UITheme.Box(new Color(0.25f, 0.17f, 0.1f, 0.8f), new Color(0.45f, 0.3f, 0.16f), 0, 1, 6));
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        row.AddChild(h);
        var sp = SpeciesNamed(species);
        h.AddChild(new TextureRect
        {
            Texture = sp != null ? Icons.Fish(sp) : null,
            CustomMinimumSize = new Vector2(72, 54),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        });
        var texts = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var t = new Label { Text = title };
        t.AddThemeFontSizeOverride("font_size", 17);
        texts.AddChild(t);
        texts.AddChild(new Label { Text = details, Modulate = Muted });
        h.AddChild(texts);
        if (price != null)
        {
            var p = new Label { Text = price, VerticalAlignment = VerticalAlignment.Center };
            p.AddThemeFontSizeOverride("font_size", 18);
            p.AddThemeColorOverride("font_color", Gold);
            h.AddChild(p);
        }
        if (button != null)
        {
            var b = new Button { Text = button, CustomMinimumSize = new Vector2(110, 0) };
            b.Pressed += () => onPressed?.Invoke();
            h.AddChild(b);
        }
        list.AddChild(row);
    }

    private void FillFish()
    {
        var list = _lists["fish"];
        Clear(list);
        if (_profile.Keepnet.Count == 0)
        {
            list.AddChild(new Label { Text = "Siatka jest pusta. Złowione ryby trafiają tutaj, możesz je sprzedać.", Modulate = Muted });
            return;
        }
        if (_profile.KeepnetLimit is { } net)
            list.AddChild(new Label { Text = $"Siatka: {_profile.Keepnet.Count}/{net.MaxFish} ryb, {_profile.KeepnetWeight:0.0}/{net.MaxKg:0} kg", Modulate = Muted });
        foreach (var g in _profile.Keepnet.GroupBy(f => f.Species))
            FishRow(list, g.Key, $"{g.Key} × {g.Count()}", $"razem {g.Sum(f => f.Weight):0.00} kg", null, null, Lore.Money(g.Sum(f => f.Value)));
        if (_profile.UndersizedCount > 0)
            list.AddChild(new Label { Text = $"Niewymiarowe ryby: {_profile.UndersizedCount}. Strażnik pobierze {Profile.UndersizedFine} gr kary za każdą.", Modulate = new Color(1f, 0.55f, 0.45f) });
        var sell = new Button { Text = $"Sprzedaj wszystko za {Lore.Money(_profile.KeepnetValue - _profile.UndersizedCount * Profile.UndersizedFine)}", CustomMinimumSize = new Vector2(0, 44) };
        sell.Pressed += SellAll;
        list.AddChild(sell);
    }

    private void FillMarket()
    {
        var list = _lists["market"];
        Clear(list);
        list.AddChild(new Label
        {
            Text = $"Dzień {_profile.Market.Day + 1} targu. Ceny zmieniają się codziennie, świeże i trofeowe ryby są droższe, "
                   + "a każda sprzedana ryba obniża cenę gatunku (popyt wraca z czasem).",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = Muted,
        });
        if (_profile.Keepnet.Count == 0)
        {
            list.AddChild(new Label { Text = "Siatka jest pusta.", Modulate = Muted });
            return;
        }
        for (int i = 0; i < _profile.Keepnet.Count; i++)
        {
            int index = i;
            var f = _profile.Keepnet[i];
            int quote = _profile.MarketQuote(f, _rod.Species);
            float demand = _profile.Market.Demand(f.Species) * _profile.Market.DailyFactor(f.Species);
            FishRow(list, f.Species, $"{f.Species} {f.Weight:0.00} kg",
                $"kondycja {f.Condition * 100f:0}%  ·  popyt {demand * 100f:0}%  ·  skup płaci {Lore.Money(f.Value)}{(f.Undersized ? "  ·  niewymiarowa!" : "")}",
                "Sprzedaj", () => SellAtMarket(index), Lore.Money(quote));
        }
        var sell = new Button { Text = "Sprzedaj wszystko na targu", CustomMinimumSize = new Vector2(0, 44) };
        sell.Pressed += SellAllAtMarket;
        list.AddChild(sell);
    }

    private void SellAtMarket(int index)
    {
        int paid = _profile.SellAtMarket(index, _rod.Species);
        _message.Text = $"Sprzedano na targu za {Lore.Money(paid)}";
    }

    public void SellAllAtMarket()
    {
        int paid = _profile.SellAllAtMarket(_rod.Species);
        _message.Text = $"Sprzedano wszystko na targu za {Lore.Money(paid)}";
    }

    private void FillRecords()
    {
        var list = _lists["records"];
        Clear(list);
        string q = _search.Text.Trim().ToLowerInvariant();
        foreach (var sp in _rod.Species)
        {
            var s = sp;
            if (q != "" && !(_profile.CatchCounts.GetValueOrDefault(sp.SpeciesName) > 0 && sp.SpeciesName.ToLowerInvariant().Contains(q)))
                continue;
            float best = _profile.Records.GetValueOrDefault(sp.SpeciesName);
            int n = _profile.CatchCounts.GetValueOrDefault(sp.SpeciesName);
            bool known = n > 0;
            Card(list, Icons.Fish(sp), known ? sp.SpeciesName : "???", known ? $"rekord {best:0.00} kg" : "nie złowiono",
                known ? Price : Muted, () => SelectSpecies(s), dark: !known);
        }
    }

    private void SelectSpecies(FishSpecies sp)
    {
        _reselect = () => SelectSpecies(sp);
        int n = _profile.CatchCounts.GetValueOrDefault(sp.SpeciesName);
        if (n == 0)
        {
            Show(sp, Icons.Fish(sp), "Nieznany gatunek", "Złów tę rybę, a dowiesz się o niej więcej.", Array.Empty<(string, float, float, string)>(), "", null, false, null);
            _detailIcon.Modulate = new Color(0.08f, 0.06f, 0.05f);
            return;
        }
        _detailIcon.Modulate = Colors.White;
        float best = _profile.Records.GetValueOrDefault(sp.SpeciesName);
        Show(sp, Icons.Fish(sp), sp.SpeciesName, GameUI.SpeciesTips(sp), new[]
        {
            ("Twój rekord", best / sp.MaxWeight, -1f, $"{best:0.00} kg"),
            ("Trofeum od", sp.TrophyThreshold / sp.MaxWeight, -1f, $"{sp.TrophyThreshold:0.0} kg"),
            ("Cena za kilogram", sp.PricePerKg / 90f, -1f, Lore.Money((int)sp.PricePerKg)),
        }, $"Złowione: {n} szt.", null, false, null);
    }

    // ------------------------------------------------------------------ travel
    private void FillTravel()
    {
        var list = _lists["travel"];
        Clear(list);
        for (int i = 0; i < _lake.Locations.Count; i++)
        {
            int index = i;
            var loc = _lake.Locations[i];
            bool here = _lake.LocationIndex == i;
            bool locked = _profile.Level < loc.RequiredLevel;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 14);
            row.AddChild(new TextureRect
            {
                Texture = Postcard.For(loc),
                CustomMinimumSize = new Vector2(192, 108),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                Modulate = locked ? new Color(0.45f, 0.45f, 0.45f) : Colors.White,
            });
            var texts = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var name = new Label { Text = loc.DisplayName };
            name.AddThemeFontSizeOverride("font_size", 20);
            name.AddThemeColorOverride("font_color", Gold);
            texts.AddChild(name);
            texts.AddChild(new Label { Text = loc.Description, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = Muted, CustomMinimumSize = new Vector2(300, 0) });
            texts.AddChild(new Label { Text = $"głębokość do {loc.MaxDepth + loc.HoleDepth:0} m" + (locked ? $"  ·  od poziomu {loc.RequiredLevel}" : "") });
            row.AddChild(texts);
            var go = new Button { Text = here ? "Tu jesteś" : locked ? "Zablokowane" : "Jedź wozem", Disabled = here || locked, CustomMinimumSize = new Vector2(130, 0) };
            go.Pressed += () => Travel(index);
            row.AddChild(go);
            list.AddChild(row);
        }
    }

    public void SellAll()
    {
        int value = _profile.SellAll();
        _message.Text = $"Sprzedano ryby za {Lore.Money(value)}";
    }

    private void Travel(int index)
    {
        _lake.SetLocation(index);
        _profile.NotifyChanged();
        _message.Text = $"Łowisko: {_lake.Location.DisplayName}";
    }
}
