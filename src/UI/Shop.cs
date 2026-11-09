using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Data;
using FishingSim.Fishing;
using FishingSim.Progress;
using FishingSim.World;

namespace FishingSim.UI;

/// <summary>Which counter the player walked up to; decides which tabs the shop shows.</summary>
public enum ShopKind { Tackle, Bait, FishBuyer, General, Travel }

/// <summary>Tackle shop, bait shop, fish buyer and travel menu, opened by pressing E at a building door (only between casts).</summary>
public partial class Shop : CanvasLayer
{
    private static readonly (string Key, string Title)[] TabDefinitions =
    {
        ("rod", "Wędki"), ("reel", "Kołowrotki"), ("line", "Żyłki"), ("hook", "Haki"),
        ("bait", "Przynęty"), ("fish", "Ryby"), ("records", "Rekordy"), ("travel", "Łowiska"),
    };

    private FishingRod _rod = null!;
    private Profile _profile = null!;
    private TackleLoadout _loadout = null!;
    private LakeBed _lake = null!;
    private TabContainer _tabs = null!;
    private readonly Dictionary<string, VBoxContainer> _lists = new();
    private Label _money = null!, _message = null!, _title = null!;

    public void Setup(FishingRod rod)
    {
        _rod = rod;
        _profile = rod.Profile;
        _loadout = rod.Loadout;
        _lake = rod.Lake;
    }

    public TabContainer Tabs => _tabs;

    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.5f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var panel = new PanelContainer { Theme = UITheme.Make() };
        panel.AnchorLeft = panel.AnchorRight = panel.AnchorTop = panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = -500; panel.OffsetRight = 500; panel.OffsetTop = -310; panel.OffsetBottom = 310;
        AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        var head = new HBoxContainer();
        box.AddChild(head);
        _title = new Label { Text = "Sklep wędkarski", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _title.AddThemeFontSizeOverride("font_size", 26);
        head.AddChild(_title);
        _money = new Label();
        _money.AddThemeFontSizeOverride("font_size", 22);
        head.AddChild(_money);

        _tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddChild(_tabs);
        foreach (var (key, tabTitle) in TabDefinitions)
        {
            var scroll = new ScrollContainer { Name = tabTitle, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            list.AddThemeConstantOverride("separation", 6);
            scroll.AddChild(list);
            _tabs.AddChild(scroll);
            _lists[key] = list;
        }

        _message = new Label();
        _message.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.4f));
        box.AddChild(_message);
        box.AddChild(new Label { Text = "Esc - wyjdź ze sklepu | Odblokowywanie sprzętu zależy od poziomu", Modulate = new Color(1, 1, 1, 0.6f) });
        _profile.Changed += Refresh;
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
        [ShopKind.Tackle] = ("Sklep wędkarski", new[] { "rod", "reel", "line", "hook" }),
        [ShopKind.Bait] = ("Sklep z przynętami", new[] { "bait" }),
        [ShopKind.FishBuyer] = ("Skup ryb", new[] { "fish", "records" }),
        [ShopKind.General] = ("Sklep wędkarski i przynęty", new[] { "rod", "reel", "line", "hook", "bait", "fish", "records" }),
        [ShopKind.Travel] = ("Przystanek: wybierz łowisko", new[] { "travel" }),
    };

    /// <summary>Opens the shop showing only the tabs that belong to this kind of counter.</summary>
    public void OpenFor(ShopKind kind)
    {
        if (Visible || _rod.State != RodState.Idle)
            return;
        var (title, tabs) = Kinds[kind];
        _title.Text = title;
        int first = -1;
        for (int i = 0; i < TabDefinitions.Length; i++)
        {
            bool shown = tabs.Contains(TabDefinitions[i].Key);
            _tabs.SetTabHidden(i, !shown);
            if (shown && first < 0)
                first = i;
        }
        Visible = true;
        _tabs.CurrentTab = Math.Max(first, 0);
        _rod.InputBlocked = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _message.Text = "";
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
        _money.Text = $"{_profile.Money} zł | poziom {_profile.Level}";
        for (int s = 0; s < TackleSlotExtensions.Count; s++)
            FillGear((TackleSlot)s);
        FillFish();
        FillRecords();
        FillTravel();
    }

    private static void Clear(VBoxContainer list)
    {
        foreach (var c in list.GetChildren())
            c.QueueFree();
    }

    private static void Row(VBoxContainer list, string title, string stats, string status, string buttonText, bool enabled, Action onPressed)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        h.AddChild(new Label { Text = title, CustomMinimumSize = new Vector2(240, 0) });
        h.AddChild(new Label { Text = stats, CustomMinimumSize = new Vector2(400, 0), Modulate = new Color(1, 1, 1, 0.7f) });
        h.AddChild(new Label { Text = status, CustomMinimumSize = new Vector2(110, 0) });
        var button = new Button { Text = buttonText, Disabled = !enabled, CustomMinimumSize = new Vector2(170, 0) };
        button.Pressed += onPressed;
        h.AddChild(button);
        list.AddChild(h);
    }

    private static string Stats(TackleItem item) => item switch
    {
        RodData r => $"Rzut x{r.CastMult:0.00} | max {r.MaxLoad:0} kg | amortyzacja {Mathf.RoundToInt(r.ShockAbsorb * 200f)}%",
        ReelData r => $"Hamulec {r.DragMax:0} kg | zwijanie {r.ReelSpeed:0.0} m/s | szpula {r.LineCapacity:0} m",
        LineData l => $"Wytrzymałość {l.Strength:0} kg | widoczność x{l.Visibility:0.00}",
        HookData h => $"Rozmiar #{Mathf.RoundToInt(h.Size)}",
        BaitData b => $"Paczka {b.PackSize} szt.",
        _ => "",
    };

    private void FillGear(TackleSlot slot)
    {
        var list = _lists[slot.Key()];
        Clear(list);
        var items = _loadout.Items(slot);
        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            var item = items[i];
            bool locked = _profile.Level < item.RequiredLevel;
            if (slot == TackleSlot.Bait)
            {
                int have = _profile.BaitCounts.GetValueOrDefault(item.ItemId);
                Row(list, item.DisplayName, Stats(item), locked ? $"Poziom {item.RequiredLevel}" : $"Masz: {have}",
                    $"Kup ({item.Price} zł)", !locked && _profile.Money >= item.Price, () => Buy(slot, index));
            }
            else if (_profile.Owns(slot, item.ItemId))
            {
                bool worn = _loadout.Index[(int)slot] == i;
                Row(list, item.DisplayName, Stats(item), "Posiadane", worn ? "Założone" : "Załóż", !worn, () => Equip(slot, index));
            }
            else
            {
                Row(list, item.DisplayName, Stats(item), locked ? $"Poziom {item.RequiredLevel}" : $"{item.Price} zł",
                    locked ? "Zablokowane" : $"Kup ({item.Price} zł)", !locked && _profile.Money >= item.Price, () => Buy(slot, index));
            }
        }
    }

    private void FillFish()
    {
        var list = _lists["fish"];
        Clear(list);
        if (_profile.Keepnet.Count == 0)
        {
            list.AddChild(new Label { Text = "Siatka jest pusta. Złowione ryby trafiają tutaj, możesz je sprzedać." });
            return;
        }
        foreach (var g in _profile.Keepnet.GroupBy(f => f.Species))
            list.AddChild(new Label { Text = $"{g.Key}: {g.Count()} szt., {g.Sum(f => f.Weight):0.00} kg, wartość {g.Sum(f => f.Value)} zł" });
        var sell = new Button { Text = $"Sprzedaj wszystko ({_profile.KeepnetValue} zł)", CustomMinimumSize = new Vector2(260, 36) };
        sell.Pressed += SellAll;
        list.AddChild(sell);
    }

    private void FillRecords()
    {
        var list = _lists["records"];
        Clear(list);
        list.AddChild(new Label { Text = $"Poziom {_profile.Level} | XP {_profile.Xp} / {_profile.XpToNext} | złowione ryby: {_profile.TotalCatches}" });
        foreach (var sp in _rod.Species)
        {
            float best = _profile.Records.GetValueOrDefault(sp.SpeciesName);
            int n = _profile.CatchCounts.GetValueOrDefault(sp.SpeciesName);
            list.AddChild(new Label { Text = $"{sp.SpeciesName}: rekord {(best > 0f ? $"{best:0.00} kg" : "-")} | złowione {n}" });
        }
    }

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
            Row(list, loc.DisplayName, $"Maks. głębokość {loc.MaxDepth:0.0} m", locked ? $"Poziom {loc.RequiredLevel}" : "",
                here ? "Tu jesteś" : locked ? "Zablokowane" : "Jedź", !here && !locked, () => Travel(index));
        }
    }

    private void Buy(TackleSlot slot, int index)
    {
        var item = _loadout.Items(slot)[index];
        int pack = item is BaitData bait ? bait.PackSize : 1;
        string? error = _profile.Buy(slot, item.ItemId, item.Price, item.RequiredLevel, pack);
        _message.Text = error ?? $"Kupiono: {item.DisplayName}";
    }

    private void Equip(TackleSlot slot, int index)
    {
        _loadout.Index[(int)slot] = index;
        _loadout.SyncToProfile();
        _rod.ApplyTackle();
        _profile.NotifyChanged();
        _message.Text = $"Założono: {_loadout.Items(slot)[index].DisplayName}";
    }

    public void SellAll()
    {
        int value = _profile.SellAll();
        _message.Text = $"Sprzedano ryby za {value} zł";
    }

    private void Travel(int index)
    {
        _lake.SetLocation(index);
        _profile.NotifyChanged();
        _message.Text = $"Łowisko: {_lake.Location.DisplayName}";
    }
}
