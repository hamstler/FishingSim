using System;
using System.Collections.Generic;
using Godot;
using FishingSim.Fishing;
using FishingSim.Progress;

namespace FishingSim.UI;

/// <summary>
/// Pops up when a fish is landed and held up: species, weight, value, whether it is a record or undersized, how full
/// the keepnet is, and the choice: into the keepnet (E) or back into the water (R). The fish waits in the hand until
/// the angler decides.
/// </summary>
public partial class CatchPanel : PanelContainer
{
    private FishingRod _rod = null!;
    private Label _title = null!, _facts = null!, _warning = null!;
    private Button _keep = null!, _release = null!;
    private bool _shown;

    /// <summary>The "keep" button (tests).</summary>
    public Button KeepButton => _keep;

    public Button ReleaseButton => _release;

    public void Setup(FishingRod rod) => _rod = rod;

    public override void _Ready()
    {
        Theme = UITheme.Make();
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UITheme.Box(new Color(0.1f, 0.07f, 0.04f, 0.92f), UITheme.Border, 4, 2, 14));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 24);
        _title.AddThemeColorOverride("font_color", UITheme.Accent);
        box.AddChild(_title);
        _facts = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _facts.AddThemeFontSizeOverride("font_size", 16);
        box.AddChild(_facts);
        _warning = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _warning.AddThemeFontSizeOverride("font_size", 15);
        _warning.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.4f));
        box.AddChild(_warning);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 14);
        box.AddChild(row);
        _keep = new Button { Text = "Do siatki  [E]", CustomMinimumSize = new Vector2(190, 44) };
        _keep.Pressed += () => _rod.KeepCatch();
        row.AddChild(_keep);
        _release = new Button { Text = "Do wody  [R]", CustomMinimumSize = new Vector2(190, 44) };
        _release.Pressed += () => _rod.ReleaseCatch();
        row.AddChild(_release);
    }

    public override void _Process(double delta)
    {
        if (_rod == null)
            return;
        bool show = _rod.AwaitingDecision && _rod.CurrentSpecies != null;
        if (show && !_shown)
            Fill();
        if (show != _shown)
        {
            _shown = show;
            Visible = show;
            // A free cursor so the buttons can be clicked; the keys work either way.
            if (DisplayServer.GetName() != "headless")
                Input.MouseMode = show ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        }
    }

    private void Fill()
    {
        var sp = _rod.CurrentSpecies!;
        var profile = _rod.Profile;
        float w = _rod.CurrentWeight;
        var result = _rod.LastCatch;
        _title.Text = $"{sp.SpeciesName}  ·  {(w < 1f ? $"{w * 1000f:0} g" : $"{w:0.00} kg")}";
        string record = result.Record ? "nowy rekord gatunku!" : $"rekord: {profile.Records.GetValueOrDefault(sp.SpeciesName):0.00} kg";
        string net = profile.KeepnetLimit is { } spec ? $"siatka {profile.Keepnet.Count}/{spec.MaxFish}" : $"siatka {profile.Keepnet.Count}";
        _facts.Text = $"{sp.LatinName}\nwartość w skupie ok. {Lore.Money(result.Value)}  ·  +{result.Xp} XP  ·  {record}  ·  {net}";
        bool undersized = w < sp.LegalWeight;
        bool fits = profile.KeepnetFits(w);
        _warning.Text = !fits
            ? "Siatka pełna albo za słaba na tę rybę: możesz ją tylko wypuścić."
            : undersized
                ? $"Niewymiarowa (wymiar ochronny {sp.LegalWeight:0.00} kg): zatrzymanie grozi karą strażnika."
                : "";
        _warning.Visible = _warning.Text != "";
        _keep.Disabled = !fits;
        _release.Text = $"Do wody  [R]  +{Profile.ReleaseXp} XP";
    }
}
