using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FishingSim.Core.Camp;
using FishingSim.Data;
using FishingSim.Fishing;
using FishingSim.Progress;
using FishingSim.World.Props;

namespace FishingSim.UI;

/// <summary>
/// The campfire's panel (E at a campfire): light the fire, then cook a dish from the keepnet: a fish roasted on the spit
/// or a fish soup in the pot (Core/Camp/Cooking). The dish cooks on the fire for some seconds (the fish turn on the
/// spit, the soup steams), then it is eaten and its buff starts (more XP, quicker bites, for some game hours).
/// </summary>
public partial class CookingPanel : Control
{
    private FishingRod _rod = null!;
    private GearCatalog _gear = null!;
    private IReadOnlyList<FishSpecies> _species = Array.Empty<FishSpecies>();
    private Campfire? _fire;
    private Label _title = null!, _state = null!, _footer = null!;
    private Button _light = null!;
    private VBoxContainer _recipes = null!;
    private ScrollContainer _scroll = null!;
    private Control _card = null!;
    private float _refresh;

    public bool IsOpen => Visible;

    public Campfire? Fire => _fire;

    public void Setup(FishingRod rod, GearCatalog gear, IReadOnlyList<FishSpecies> species)
    {
        _rod = rod;
        _gear = gear;
        _species = species;
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
        var card = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        centre.AddChild(card);
        _card = card;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        card.AddChild(box);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 30);
        _title.AddThemeColorOverride("font_color", UITheme.Accent);
        box.AddChild(_title);
        _state = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _state.AddThemeFontSizeOverride("font_size", 17);
        box.AddChild(_state);
        _light = new Button { Text = "Rozpal ogień", CustomMinimumSize = new Vector2(240, 46), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        UITheme.Primary(_light);
        _light.Pressed += LightFire;
        box.AddChild(_light);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(_scroll);
        _recipes = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _recipes.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_recipes);
        _footer = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1f, 1f, 1f, 0.8f) };
        _footer.AddThemeFontSizeOverride("font_size", 15);
        box.AddChild(_footer);
        var close = new Button { Text = "Zamknij  [E]", CustomMinimumSize = new Vector2(200, 40), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        close.Pressed += Close;
        box.AddChild(close);
    }

    public void Open(Campfire fire)
    {
        _fire = fire;
        Visible = true;
        _rod.InputBlocked = true;
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
        _rod.InputBlocked = false;
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public void LightFire()
    {
        if (_fire == null || _fire.Lit)
            return;
        _fire.Light();
        _rod.Cue(Core.Audio.OneShot.Ignite, 1f);
        _rod.Notify("Ogień się rozpalił.", 2f);
        Refresh();
    }

    private static string Buff(MealBuff b) =>
        $"{b.Name}: +{b.XpBonus * 100f:0}% XP, brania o {b.BiteBonus * 100f:0}% szybsze, przez {b.Hours:0} h gry";

    private void Refresh()
    {
        if (_fire == null)
            return;
        var profile = _rod.Profile;
        _title.Text = _fire.Title;
        _state.Text = _fire.Cooking is { } dish
            ? $"Na ogniu: {dish.Name} ({_fire.CookProgress * 100f:0}%)"
            : _fire.Lit
                ? $"Ogień płonie, drewna starczy jeszcze na {_fire.BurnHoursLeft:0.0} h."
                : "Ognisko wygasło. Rozpal ogień, żeby gotować.";
        _light.Visible = !_fire.Lit;
        foreach (var c in _recipes.GetChildren())
        {
            _recipes.RemoveChild(c);
            c.QueueFree();
        }
        bool hasPot = _gear.Has(AccessoryKind.CookingPot) || !_fire.PlayerCamp;
        var ingredients = profile.Ingredients();
        foreach (var recipe in Cooking.All)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiArt.Nine(UiArt.InsetTexture, 4, 10));
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 12);
            row.AddChild(line);
            line.AddChild(new TextureRect
            {
                Texture = recipe.Extras is { Count: > 0 } extras ? Graphics.Icons.Picking(extras.Keys.First())
                    : recipe.NeedsPot ? Graphics.Icons.For(GD.Load<AccessoryData>("res://data/tackle/accessories/garnek.tres")) : FishIcon(),
                CustomMinimumSize = new Vector2(56, 56), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
            var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var name = new Label { Text = recipe.Name };
            name.AddThemeFontSizeOverride("font_size", 20);
            name.AddThemeColorOverride("font_color", UITheme.Accent);
            text.AddChild(name);
            text.AddChild(new Label { Text = recipe.Description, Modulate = new Color(1f, 1f, 1f, 0.85f) });
            var buff = new Label { Text = Buff(recipe.Buff), Modulate = new Color(1f, 0.88f, 0.6f) };
            buff.AddThemeFontSizeOverride("font_size", 14);
            text.AddChild(buff);
            string? missing = Cooking.Missing(recipe, ingredients, hasPot, profile.Pantry);
            if (missing != null)
            {
                var warn = new Label { Text = missing };
                warn.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.4f));
                warn.AddThemeFontSizeOverride("font_size", 14);
                text.AddChild(warn);
            }
            else if (Cooking.Pick(recipe, ingredients) is { } picked)
            {
                var parts = picked.Select(i => $"{profile.Keepnet[i].Species} {profile.Keepnet[i].Weight:0.00} kg").ToList();
                if (recipe.Extras != null)
                    parts.AddRange(recipe.Extras.Select(e => $"{Foraging.NameOf(e.Key)} ×{e.Value}"));
                var uses = new Label { Text = "Zużyje: " + string.Join(", ", parts) };
                uses.AddThemeFontSizeOverride("font_size", 14);
                uses.Modulate = new Color(1f, 1f, 1f, 0.7f);
                text.AddChild(uses);
            }
            line.AddChild(text);
            var cook = new Button { Text = "Gotuj", CustomMinimumSize = new Vector2(120, 44), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            cook.Disabled = missing != null || !_fire.Lit || _fire.Cooking != null;
            var r = recipe;
            cook.Pressed += () => Cook(r);
            line.AddChild(cook);
            _recipes.AddChild(row);
        }
        string net = profile.KeepnetLimit is { } spec ? $"{profile.Keepnet.Count}/{spec.MaxFish}" : $"{profile.Keepnet.Count}";
        // Five recipes do not fit a small screen: the list scrolls.
        _scroll.CustomMinimumSize = new Vector2(0f, MathF.Min(GetViewportRect().Size.Y * 0.52f, MathF.Max(_recipes.GetCombinedMinimumSize().Y, 100f)));
        string pantry = Foraging.Describe(profile.Pantry);
        _footer.Text = $"Siatka: {net} ryb   ·   spiżarnia: {(pantry.Length > 0 ? pantry : "pusto")}   ·   " + (profile.ActiveMeal is { } meal ? $"{meal.Name} jeszcze {profile.MealHoursLeft:0.0} h" : "jeszcze nic nie jadłeś");
    }

    private Texture2D FishIcon()
    {
        var name = _rod.Profile.Keepnet.FirstOrDefault()?.Species;
        var sp = _species.FirstOrDefault(s => s.SpeciesName == name) ?? _species.FirstOrDefault();
        return sp != null ? Graphics.Icons.Fish(sp) : Graphics.Icons.For(new BaitData());
    }

    /// <summary>Puts the dish on the fire; when it is done it is eaten (the fish leave the keepnet, the buff starts).</summary>
    public bool Cook(Recipe recipe)
    {
        if (_fire == null || !_fire.Lit || _fire.Cooking != null)
            return false;
        var profile = _rod.Profile;
        bool hasPot = _gear.Has(AccessoryKind.CookingPot) || !_fire.PlayerCamp;
        var ingredients = profile.Ingredients();
        if (Cooking.Missing(recipe, ingredients, hasPot, profile.Pantry) != null || Cooking.Pick(recipe, ingredients) is not { } picked)
            return false;
        var models = new List<Node3D>();
        foreach (int i in picked)
        {
            var f = profile.Keepnet[i];
            var sp = _species.FirstOrDefault(s => s.SpeciesName == f.Species);
            if (sp == null)
                continue;
            var model = FishModel.Create(sp, f.Weight);
            float scale = Math.Min(1f, 0.32f / Math.Max(model.Length, 0.01f));
            model.Scale = Vector3.One * scale;
            model.Wiggle = 0f;
            models.Add(model);
        }
        var fire = _fire;
        if (!fire.StartCooking(recipe, models))
            return false;
        void Done(Campfire f, Recipe dish)
        {
            f.Cooked -= Done;
            var used = profile.Cook(dish, hasPot, out var error);
            if (used == null)
            {
                _rod.Notify(error ?? "Nie udało się ugotować.", 3f);
                return;
            }
            string what = used.Count > 0 ? $" ({string.Join(", ", used.Select(u => u.Species.ToLowerInvariant()))})" : "";
            _rod.Notify($"Smacznego! {dish.Name}{what}. {Buff(dish.Buff)}.", 5f);
            _rod.Cue(Core.Audio.OneShot.Catch, 0.7f);
        }
        fire.Cooked += Done;
        _rod.Cue(recipe.NeedsPot ? Core.Audio.OneShot.Bubble : Core.Audio.OneShot.Ignite, 0.7f);
        _rod.Notify(recipe.Id == Cooking.Compote.Id ? "Kompot się gotuje..." : recipe.NeedsPot ? "Zupa się gotuje..." : "Ryba piecze się nad ogniem...", 3f);
        Close();
        return true;
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        _refresh -= (float)delta;
        if (_refresh <= 0f)
        {
            _refresh = 0.5f;
            if (_fire?.Cooking != null)
                Refresh();
        }
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
