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

    /// <summary>
    /// The previous session's Godot log (rotated to godot&lt;date&gt;.log when this one started) reports a corrupt shader
    /// cache ("Malformed smolv": a user's RTX 3080 showed it without any crash, so the crash flag never cleared it).
    /// </summary>
    private static bool LastLogShowsBadCache()
    {
        try
        {
            string dir = ProjectSettings.GlobalizePath("user://logs");
            if (!System.IO.Directory.Exists(dir))
                return false;
            string? newest = null;
            var newestTime = DateTime.MinValue;
            foreach (string file in System.IO.Directory.EnumerateFiles(dir, "godot*.log"))
            {
                if (System.IO.Path.GetFileName(file) == "godot.log")
                    continue;
                var t = System.IO.File.GetLastWriteTimeUtc(file);
                if (t > newestTime)
                    (newest, newestTime) = (file, t);
            }
            if (newest == null || new System.IO.FileInfo(newest).Length > 32 << 20)
                return false;
            return System.IO.File.ReadAllText(newest).Contains("Malformed smolv");
        }
        catch (Exception)
        {
            return false;
        }
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
            if (System.IO.File.Exists(Flag) || LastLogShowsBadCache())
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
        // Exceptions in tasks (fish models, grass, skins prepared in the background) are kept by their Task, not
        // raised: written down when such a task is collected unobserved.
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.AppendAllText(path, $"{DateTime.Now:u} (background task)\n{e.Exception}\n\n");
            }
            catch (Exception)
            {
                // the log stays as it is
            }
            e.SetObserved();
        };
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
