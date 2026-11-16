using Godot;

namespace FishingSim.UI;

/// <summary>
/// The quick instructions (F1 by default, <see cref="Act.QuickHelp"/>): a carved card on the left listing the keys as
/// they are bound now and short notes on each method. Rewritten when a key is rebound.
/// </summary>
public partial class QuickHelp : PanelContainer
{
    private RichTextLabel _text = null!;

    public override void _Ready()
    {
        Theme = UITheme.Make();
        MouseFilter = MouseFilterEnum.Ignore;
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 18);
        AddChild(margin);
        _text = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(760, 0),
        };
        _text.AddThemeFontSizeOverride("normal_font_size", 15);
        _text.AddThemeFontSizeOverride("bold_font_size", 15);
        margin.AddChild(_text);
        Bindings.Changed += Write;
        Write();
    }

    public override void _ExitTree() => Bindings.Changed -= Write;

    private static string K(Act a) => $"[color=#f3c766][b]{Bindings.Name(a)}[/b][/color]";

    private static string Fixed(string key) => $"[color=#f3c766][b]{key}[/b][/color]";

    private void Write()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"[font_size=22][color=#f3c766]Szybka instrukcja[/color][/font_size]   [color=#ffffffaa]({K(Act.QuickHelp)} zamyka, zmiana klawiszy: Esc → Sterowanie)[/color]\n\n");
        sb.Append($"{K(Act.Forward)}{K(Act.Left)}{K(Act.Back)}{K(Act.Right)} ruch, {K(Act.Sprint)} bieg, {Fixed("mysz")} rozglądanie, {K(Act.Interact)} wejście / rozmowa\n");
        sb.Append($"{Fixed("LPM")} przytrzymaj i puść: rzut (pasek na dole pokazuje siłę), potem {Fixed("LPM")} zwija linkę\n");
        sb.Append($"{Fixed("PPM")} podnosi wędkę: zacięcie, pompowanie, wyciągnięcie ryby; {K(Act.LandingNet)} podbierak\n");
        sb.Append($"{Fixed("kółko")} hamulec (pierścień na środku: zielony dobrze, żółty na granicy, czerwony zerwie zestaw)\n");
        sb.Append("Pasek na dole wypełnia się, gdy ryba ciągnie: im bardziej czerwony, tym bliżej zerwania.\n\n");
        sb.Append($"[color=#f3c766]Spinning[/color]: {Fixed("LPM")} prowadzi przynętę ({K(Act.Sprint)} szybciej), puszczona opada, {Fixed("PPM")} podszarpnięcie albo uwolnienie z zaczepu\n");
        sb.Append($"[color=#f3c766]Spławik[/color]: {K(Act.DepthDown)} / {K(Act.DepthUp)} (lub {Fixed("Ctrl + kółko")}) grunt, {K(Act.LooseFeed)} garść zanęty; branie widać tylko na spławiku\n");
        sb.Append($"[color=#f3c766]Grunt[/color]: koszyczek nęci miejsce, brania na szczytówce i dzwonku; {K(Act.RodRest)} wędka na podpórkę / podnieś\n");
        sb.Append($"{Fixed("1-3")} wędki (do 3 w wodzie), {Fixed("4-6")} łopata, siatka na owady, podrywka, {K(Act.CycleTool)} następne narzędzie\n");
        sb.Append($"Złowiona ryba: {K(Act.Interact)} do siatki, {K(Act.Release)} wypuść; {K(Act.Sharpen)} osełka, {K(Act.NextFishery)} następne łowisko\n\n");
        sb.Append($"{K(Act.Inventory)} ekwipunek  ·  {K(Act.Map)} mapa  ·  {K(Act.Journal)} dziennik  ·  {K(Act.Tournaments)} turnieje  ·  {K(Act.Details)} stan zestawu  ·  {K(Act.Performance)} wydajność  ·  {Fixed("Esc")} menu");
        _text.Text = sb.ToString();
    }
}
