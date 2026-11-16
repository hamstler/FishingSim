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

    private static string Flag => ProjectSettings.GlobalizePath("user://running.flag");

    private static void CleanExit()
    {
        try { System.IO.File.Delete(Flag); } catch (Exception) { }
    }

    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;
        // A flag lives while the game runs and goes at a clean exit. Found at start, the last run crashed: its shader
        // and pipeline caches may be half written (Godot then fails on "Malformed smolv" at every start), so they go.
        try
        {
            if (System.IO.File.Exists(Flag))
                foreach (var dir in new[] { "user://shader_cache", "user://vulkan" })
                {
                    string full = ProjectSettings.GlobalizePath(dir);
                    // Only the files: the engine made the cache's folders before this runs and writes into them for
                    // the rest of the session (removing the folders left every shader unsaved: "_save_to_cache").
                    if (System.IO.Directory.Exists(full))
                        foreach (string file in System.IO.Directory.EnumerateFiles(full, "*", System.IO.SearchOption.AllDirectories))
                            try { System.IO.File.Delete(file); } catch (Exception) { }
                }
            System.IO.File.WriteAllText(Flag, DateTime.Now.ToString("u"));
            AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanExit();
            if (Engine.GetMainLoop() is SceneTree tree)
                tree.Root.TreeExiting += CleanExit;
        }
        catch (Exception)
        {
            // caches stay as they are
        }
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
