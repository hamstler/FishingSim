namespace FishingSim.Core;

public enum DayPeriod { Day, Twilight, Night }

public enum Weather { Clear, Cloudy, Rain }

public static class WeatherExtensions
{
    /// <summary>Key used in <c>FishSpecies.WeatherPref</c>.</summary>
    public static string Key(this Weather w) => w switch
    {
        Weather.Clear => "clear",
        Weather.Cloudy => "cloudy",
        _ => "rain",
    };

    public static string DisplayName(this Weather w) => w switch
    {
        Weather.Clear => "Słonecznie",
        Weather.Cloudy => "Pochmurno",
        _ => "Deszcz",
    };
}

public static class DayPeriodExtensions
{
    public static string DisplayName(this DayPeriod p) => p switch
    {
        DayPeriod.Day => "Dzień",
        DayPeriod.Twilight => "Świt/zmierzch",
        _ => "Noc",
    };
}
