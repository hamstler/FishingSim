using System;
using Godot;

namespace FishingSim;

/// <summary>
/// Writes an unhandled .NET exception (which ends the game without a window, e.g. on a worker thread) to
/// user://logs/crash.txt, next to Godot's own godot.log, so the player can send it.
/// </summary>
public static class CrashLog
{
    private static bool _installed;

    public static string Path => ProjectSettings.GlobalizePath("user://logs/crash.txt");

    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;
        string path = Path;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.AppendAllText(path, $"{DateTime.Now:u}\n{e.ExceptionObject}\n\n");
            }
            catch (Exception)
            {
                // nothing more can be done while the process ends
            }
        };
    }
}
