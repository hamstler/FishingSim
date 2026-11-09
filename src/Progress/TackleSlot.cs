namespace FishingSim.Progress;

public enum TackleSlot { Rod, Reel, Line, Hook, Bait }

public static class TackleSlotExtensions
{
    public const int Count = 5;

    /// <summary>Key used in save files.</summary>
    public static string Key(this TackleSlot slot) => slot switch
    {
        TackleSlot.Rod => "rod",
        TackleSlot.Reel => "reel",
        TackleSlot.Line => "line",
        TackleSlot.Hook => "hook",
        _ => "bait",
    };

    /// <summary>"Brak sprzętu: wędki, haczyka" (genitive).</summary>
    public static string MissingName(this TackleSlot slot) => slot switch
    {
        TackleSlot.Rod => "wędki",
        TackleSlot.Reel => "kołowrotka",
        TackleSlot.Line => "linki",
        TackleSlot.Hook => "haczyka",
        _ => "przynęty",
    };

    public static string Title(this TackleSlot slot) => slot switch
    {
        TackleSlot.Rod => "Wędka",
        TackleSlot.Reel => "Kołowrotek",
        TackleSlot.Line => "Linka",
        TackleSlot.Hook => "Haczyk",
        _ => "Przynęta",
    };
}
