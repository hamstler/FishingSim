using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Core.Camp;
using FishingSim.Data;
using FishingSim.Fishing;
using FishingSim.Progress;

namespace FishingSim.UI;

/// <summary>
/// A carved card in the middle of the screen over a light dim (E or Esc closes it), with a title, a state line, a
/// scrolling body and a footer: the base of the smokehouse's and the notice board's panels.
/// </summary>
public partial class CardPanel : Control
{
    protected FishingRod Rod = null!;
    protected IReadOnlyList<FishSpecies> Species = Array.Empty<FishSpecies>();
    protected Label TitleLabel = null!, State = null!, Footer = null!;
    protected VBoxContainer Body = null!;
    private ScrollContainer _scroll = null!;
    private Control _card = null!;

    public bool IsOpen => Visible;

    public void Setup(FishingRod rod, IReadOnlyList<FishSpecies> species)
    {
        Rod = rod;
        Species = species;
    }

    public override void _Ready()
    {
        Theme = UITheme.Make();
        Visible = false;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 5;
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0.02f, 0.03f, 0.04f, 0.35f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(780, 0) };
        centre.AddChild(card);
        _card = card;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        card.AddChild(box);
        TitleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        TitleLabel.AddThemeFontSizeOverride("font_size", 30);
        TitleLabel.AddThemeColorOverride("font_color", UITheme.Accent);
        box.AddChild(TitleLabel);
        State = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        State.AddThemeFontSizeOverride("font_size", 17);
        box.AddChild(State);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(_scroll);
        Body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Body.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(Body);
        Footer = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1f, 1f, 1f, 0.8f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Footer.AddThemeFontSizeOverride("font_size", 15);
        box.AddChild(Footer);
        var close = new Button { Text = "Zamknij  [E]", CustomMinimumSize = new Vector2(200, 40), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        close.Pressed += Close;
        box.AddChild(close);
    }

    protected void OpenCard()
    {
        Visible = true;
        Rod.InputBlocked = true;
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
        _card.PivotOffset = _card.Size / 2f;
        _card.Scale = new Vector2(0.95f, 0.95f);
        _card.Modulate = new Color(1f, 1f, 1f, 0f);
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(_card, "scale", Vector2.One, 0.25f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_card, "modulate:a", 1f, 0.18f);
    }

    public void Close()
    {
        if (!Visible)
            return;
        Visible = false;
        Rod.InputBlocked = false;
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Rebuilds the body from the profile.</summary>
    public void Refresh()
    {
        foreach (var c in Body.GetChildren())
        {
            Body.RemoveChild(c);
            c.QueueFree();
        }
        Build();
        // The body scrolls when it is taller than about half the screen.
        _scroll.CustomMinimumSize = new Vector2(0f, MathF.Min(GetViewportRect().Size.Y - 270f, MathF.Max(Body.GetCombinedMinimumSize().Y, 60f)));
    }

    protected virtual void Build()
    {
    }

    protected static Label Text(string text, int size = 16, Color? colour = null, float alpha = 1f)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1f, 1f, 1f, alpha) };
        l.AddThemeFontSizeOverride("font_size", size);
        if (colour is Color c)
            l.AddThemeColorOverride("font_color", c);
        return l;
    }

    protected static Label Heading(string text) => Text(text, 20, UITheme.Accent);

    /// <summary>"1 ryba", "3 ryby", "5 ryb".</summary>
    protected static string FishCount(int n) =>
        n == 1 ? "1 ryba" : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? $"{n} ryby" : $"{n} ryb";

    /// <summary>An inset row with an icon, a column of text and room for buttons on the right.</summary>
    protected static (PanelContainer Row, VBoxContainer Text, HBoxContainer Line) Row(Texture2D? icon)
    {
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", UiArt.Nine(UiArt.InsetTexture, 4, 10));
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 12);
        row.AddChild(line);
        if (icon != null)
            line.AddChild(new TextureRect
            {
                Texture = icon, CustomMinimumSize = new Vector2(52, 52), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            });
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        line.AddChild(text);
        return (row, text, line);
    }

    protected Texture2D? FishIcon(string species)
    {
        var sp = Species.FirstOrDefault(s => s.SpeciesName == species);
        return sp != null ? Graphics.Icons.Fish(sp) : null;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible)
            return;
        if (e.IsActionPressed("ui_cancel") || e is InputEventKey { Pressed: true, Echo: false, Keycode: Godot.Key.E })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}

/// <summary>
/// The smokehouse (E at its door): choose fish from the keepnet to hang in the smoke (the smoker takes a small fee for
/// the alder wood), take the smoked ones after two game hours and sell them to the smoker for far more than the buyer
/// pays for raw fish (Core/Camp/Smoking). Smoked fish keep; a notice may ask for one.
/// </summary>
public partial class SmokehousePanel : CardPanel
{
    private readonly HashSet<int> _selected = new();
    private float _refresh;
    private int _shownReady;

    public void Open()
    {
        _selected.Clear();
        foreach (int i in Rod.Profile.Smokable())
            _selected.Add(i);
        OpenCard();
    }

    /// <summary>Hangs the chosen keepnet fish in the smoke. Returns how many went in.</summary>
    public int SmokeSelected()
    {
        var p = Rod.Profile;
        int n = p.StartSmoking(_selected.ToList(), out var error);
        if (n == 0)
        {
            Rod.Notify(error ?? "Nic nie trafiło do wędzarni.", 3f);
            return 0;
        }
        _selected.Clear();
        Rod.Cue(Core.Audio.OneShot.Ignite, 0.6f);
        Rod.Notify($"Wędzarz zawiesił {n} {Ryb(n)} w dymie olchowym. Gotowe za {Smoking.Hours:0} h gry.", 4f);
        Refresh();
        return n;
    }

    public int Collect()
    {
        int n = Rod.Profile.CollectSmoked();
        if (n > 0)
        {
            Rod.Notify($"Odebrano {n} {Ryb(n)} wędzonych, złocistych i pachnących dymem.", 3f);
            Rod.Cue(Core.Audio.OneShot.Click, 0.7f);
        }
        Refresh();
        return n;
    }

    public int SellSmoked()
    {
        int paid = Rod.Profile.SellSmoked();
        if (paid > 0)
        {
            Rod.Notify($"Wędzarz zapłacił {Lore.Money(paid)}.", 3f);
            Rod.Cue(Core.Audio.OneShot.Coins, 0.9f);
        }
        Refresh();
        return paid;
    }

    private static string Ryb(int n) => n == 1 ? "rybę" : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? "ryby" : "ryb";

    protected override void Build()
    {
        var p = Rod.Profile;
        TitleLabel.Text = "Wędzarnia";
        int ready = p.SmokeryReady, hanging = p.Smokery.Count - ready;
        _shownReady = ready;
        float next = p.Smokery.Where(l => Smoking.HoursLeft(l.StartedAt, p.Clock) > 0f).Select(l => Smoking.HoursLeft(l.StartedAt, p.Clock)).DefaultIfEmpty(0f).Min();
        State.Text = p.Smokery.Count == 0
            ? $"Wędzarz uwędzi ryby z siatki nad olchowym dymem w {Smoking.Hours:0} h gry. Wędzona ryba nie traci świeżości, a wędzarz płaci za nią sporo więcej niż skup."
            : (hanging > 0 ? $"W dymie: {hanging} (najbliższe gotowe za {next:0.0} h). " : "") + (ready > 0 ? $"Gotowe do odbioru: {ready}." : "");

        // What hangs in the smoke and the smoked fish first; the keepnet's (maybe long) list last.
        if (p.Smokery.Count > 0)
        {
            Body.AddChild(Heading("W dymie"));
            foreach (var lot in p.Smokery)
            {
                float left = Smoking.HoursLeft(lot.StartedAt, p.Clock);
                Body.AddChild(Text($"{lot.Fish.Species} {lot.Fish.Weight:0.00} kg: " + (left > 0f ? $"jeszcze {left:0.0} h" : "gotowa"), 15, alpha: 0.85f));
            }
            if (ready > 0)
            {
                var take = new Button { Text = $"Odbierz gotowe ({ready})", CustomMinimumSize = new Vector2(260, 40), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
                UITheme.Primary(take);
                take.Pressed += () => Collect();
                Body.AddChild(take);
            }
        }
        if (p.Smoked.Count > 0)
        {
            Body.AddChild(Heading($"Wędzonki ({p.Smoked.Count})"));
            var (row, text, line) = Row(Graphics.Icons.Picking("smoked"));
            text.AddChild(Text(string.Join(", ", p.Smoked.GroupBy(f => f.Species).Select(g => $"{g.Key} ×{g.Count()}")), 16));
            text.AddChild(Text($"Wędzarz da za wszystkie {Lore.Money(p.SmokedValue)}; można je też oddać na ogłoszenie.", 14, alpha: 0.85f));
            var sell = new Button { Text = $"Sprzedaj za {Lore.Money(p.SmokedValue)}", CustomMinimumSize = new Vector2(200, 44), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            UITheme.Primary(sell);
            sell.Pressed += () => SellSmoked();
            line.AddChild(sell);
            Body.AddChild(row);
        }

        Body.AddChild(Heading("Ryby z siatki"));
        float kg = _selected.Where(i => i < p.Keepnet.Count).Sum(i => p.Keepnet[i].Weight);
        var smoke = new Button
        {
            Text = _selected.Count > 0 ? $"Do wędzarni: {_selected.Count} (drewno {Lore.Money(Smoking.Fee(kg))})" : "Zaznacz ryby do wędzenia",
            CustomMinimumSize = new Vector2(320, 44), SizeFlagsHorizontal = SizeFlags.ShrinkCenter, Disabled = _selected.Count == 0,
        };
        UITheme.Primary(smoke);
        smoke.Pressed += () => SmokeSelected();
        if (p.Keepnet.Count == 0)
            Body.AddChild(Text("Siatka jest pusta: najpierw coś złów.", 15, alpha: 0.75f));
        else
            Body.AddChild(smoke);
        for (int i = 0; i < p.Keepnet.Count; i++)
        {
            var f = p.Keepnet[i];
            var (row, text, line) = Row(FishIcon(f.Species));
            text.AddChild(Text($"{f.Species}  {f.Weight:0.00} kg", 18, UITheme.Accent));
            string? refusal = Smoking.Refusal(f.Weight, f.Undersized, false);
            text.AddChild(refusal == null
                ? Text($"Kondycja {f.Condition * 100f:0}%  ·  skup {Lore.Money(f.Value)}  →  wędzona {Lore.Money(Smoking.SmokedValue(f.Value, f.Condition))}", 14, alpha: 0.85f)
                : Text(refusal, 14, new Color(1f, 0.55f, 0.4f)));
            if (refusal == null)
            {
                int index = i;
                var check = new CheckBox { Text = "Do wędzenia", ButtonPressed = _selected.Contains(i), SizeFlagsVertical = SizeFlags.ShrinkCenter };
                check.Toggled += on =>
                {
                    if (on)
                        _selected.Add(index);
                    else
                        _selected.Remove(index);
                    CallDeferred(MethodName.Refresh);
                };
                line.AddChild(check);
            }
            Body.AddChild(row);
        }
        Footer.Text = $"Sakiewka: {Lore.Money(p.Money)}   ·   w siatce: {FishCount(p.Keepnet.Count)}";
    }

    public override void _Process(double delta)
    {
        if (!Visible || Rod.Profile.Smokery.Count == 0)
            return;
        // Rebuilt only when a batch is done (a rebuild would lose the list's scroll position).
        _refresh -= (float)delta;
        if (_refresh <= 0f)
        {
            _refresh = 1f;
            if (Rod.Profile.SmokeryReady != _shownReady)
                Refresh();
        }
    }
}

/// <summary>
/// The village notice board (E): the day's three notices from the villagers (Core/Camp/NoticeBoard) with what each
/// wants, the reward, how much of it the angler has, and a button to hand it in.
/// </summary>
public partial class NoticeBoardPanel : CardPanel
{
    /// <summary>Gives the day's notices for this map (made anew each day).</summary>
    public Func<List<VillageRequest>> Notices { get; set; } = () => new List<VillageRequest>();

    public void Open() => OpenCard();

    /// <summary>Hands in notice <paramref name="index"/>. True when it was done.</summary>
    public bool HandIn(int index)
    {
        var p = Rod.Profile;
        Notices();
        var r = index >= 0 && index < p.Notices.Count ? p.Notices[index] : null;
        string? error = p.HandIn(index, out bool leveled);
        if (error != null || r == null)
        {
            Rod.Notify(error ?? "Nie ma takiego ogłoszenia.", 2.5f);
            return false;
        }
        Rod.Notify($"{r.From}: Dziękuję! Nagroda: {Lore.Money(r.Reward)} i {r.Xp} XP." + (leveled ? $" Nowy poziom: {p.Level}!" : ""), 4f);
        Rod.Cue(Core.Audio.OneShot.Coins, 1f);
        Refresh();
        return true;
    }

    private Texture2D? IconOf(VillageRequest r) => r.Kind switch
    {
        RequestKind.Fish => FishIcon(r.Item),
        RequestKind.Bait => Graphics.Icons.Bait(r.Item),
        RequestKind.Forage => Graphics.Icons.Picking(r.Item),
        _ => Graphics.Icons.Picking("smoked"),
    };

    protected override void Build()
    {
        var p = Rod.Profile;
        var list = Notices();
        TitleLabel.Text = "Tablica ogłoszeń";
        float hours = 24f - p.Clock % 24f;
        State.Text = $"Mieszkańcy przypinają tu, czego potrzebują. Nowe ogłoszenia za {hours:0} h gry.";
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            var (row, text, line) = Row(IconOf(r));
            text.AddChild(Text(r.From, 18, UITheme.Accent));
            text.AddChild(Text(r.Text, 15, alpha: 0.9f));
            text.AddChild(Text($"Nagroda: {Lore.Money(r.Reward)} i {r.Xp} XP", 14, new Color(1f, 0.88f, 0.6f)));
            string? missing = p.NoticeMissing(r);
            if (r.Done)
                text.AddChild(Text("Załatwione. Dziękujemy!", 14, new Color(0.6f, 0.95f, 0.6f)));
            else if (missing != null)
                text.AddChild(Text($"{r.Wanted}: {missing}", 14, new Color(1f, 0.6f, 0.45f)));
            else
                text.AddChild(Text($"{r.Wanted}: masz wszystko", 14, new Color(0.6f, 0.95f, 0.6f)));
            int index = i;
            var give = new Button { Text = r.Done ? "Oddane" : "Oddaj", CustomMinimumSize = new Vector2(120, 44), SizeFlagsVertical = SizeFlags.ShrinkCenter, Disabled = r.Done || missing != null };
            if (!r.Done && missing == null)
                UITheme.Primary(give);
            give.Pressed += () => HandIn(index);
            line.AddChild(give);
            if (r.Done)
                row.Modulate = new Color(1f, 1f, 1f, 0.6f);
            Body.AddChild(row);
        }
        string pantry = Foraging.Describe(p.Pantry);
        Footer.Text = $"Spiżarnia: {(pantry.Length > 0 ? pantry : "pusto")}   ·   wędzonki: {p.Smoked.Count}   ·   w siatce: {FishCount(p.Keepnet.Count)}";
    }
}
