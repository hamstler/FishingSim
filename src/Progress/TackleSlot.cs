namespace FishingSim.Progress;

/// <summary>The pieces of the rig. The leader is optional ("Bez przyponu" is always owned).</summary>
public enum TackleSlot { Rod, Reel, Line, Hook, Bait, Leader }

public static class TackleSlotExtensions
{
    public const int Count = 6;

    /// <summary>The leader every angler has: no leader at all.</summary>
    public const string NoLeader = "brak";

    /// <summary>Key used in save files.</summary>
    public static string Key(this TackleSlot slot) => slot switch
    {
        TackleSlot.Rod => "rod",
        TackleSlot.Reel => "reel",
        TackleSlot.Line => "line",
        TackleSlot.Hook => "hook",
        TackleSlot.Leader => "leader",
        _ => "bait",
    };

    /// <summary>"Brak sprzętu: wędki, haczyka" (genitive).</summary>
    public static string MissingName(this TackleSlot slot) => slot switch
    {
        TackleSlot.Rod => "wędki",
        TackleSlot.Reel => "kołowrotka",
        TackleSlot.Line => "linki",
        TackleSlot.Hook => "haczyka",
        TackleSlot.Leader => "przyponu",
        _ => "przynęty",
    };

    public static string Title(this TackleSlot slot) => slot switch
    {
        TackleSlot.Rod => "Wędka",
        TackleSlot.Reel => "Kołowrotek",
        TackleSlot.Line => "Linka",
        TackleSlot.Hook => "Haczyk",
        TackleSlot.Leader => "Przypon",
        _ => "Przynęta",
    };
}
