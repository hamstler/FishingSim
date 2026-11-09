namespace FishingSim.Accounts;

/// <summary>Who is logged in and which map was picked. Static, so it survives scene changes.</summary>
public static class Session
{
    public static string Username { get; set; } = "";
    public static int MapIndex { get; set; } = -1;

    public static bool IsLoggedIn => Username.Length > 0;

    public static void Logout()
    {
        Username = "";
        MapIndex = -1;
    }
}
