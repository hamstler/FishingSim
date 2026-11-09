namespace FishingSim.UI;

/// <summary>Medieval wording shared by the UI: the currency is the grosz (gr).</summary>
public static class Lore
{
    public const string Currency = "gr";

    public static string Money(int amount) => $"{amount} {Currency}";
}
