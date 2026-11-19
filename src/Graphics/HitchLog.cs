using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace FishingSim.Graphics;

/// <summary>
/// A diary of stutters for the angler to send: every frame over <see cref="LimitMs"/> while a map is played is written
/// to user://logs/hitches.txt (a new file each session) with the game code's slowest scopes, the draw counts, memory,
/// busy background workers and the events noted just before it (a model loaded, a fish built, a photo taken...).
/// A sandbox without the user's GPU cannot reproduce their hitches; this file shows where the time went.
/// Main calls <see cref="Frame"/> once a frame; other code calls <see cref="Note"/> (main thread or workers).
/// </summary>
public static class HitchLog
{
    public const double LimitMs = 50.0;
    private const int MaxLines = 400;

    private static readonly Queue<(ulong Usec, string Text)> Recent = new();
    private static readonly List<(string Name, double Ms)> Scopes = new();
    private static ulong _last, _since;
    private static int _written;
    private static string? _path;

    /// <summary>Remembers an event (kept 2 s) to be named if a hitch follows.</summary>
    public static void Note(string text)
    {
        lock (Recent)
        {
            Recent.Enqueue((Time.GetTicksUsec(), text));
            while (Recent.Count > 32)
                Recent.Dequeue();
        }
    }

    /// <summary>A map was (re)loaded: the loading frames themselves are not hitches.</summary>
    public static void Reset()
    {
        _since = Time.GetTicksUsec();
        _last = 0;
    }

    public static void Frame()
    {
        ulong now = Time.GetTicksUsec();
        double ms = _last == 0 ? 0 : (now - _last) / 1000.0;
        _last = now;
        Perf.Tracking = true;   // the scopes add up their time per frame (no allocation) so a hitch can name them
        if (ms < LimitMs || now - _since < 8_000_000 || _written >= MaxLines || Renderer.IsHeadless)
        {
            if (ms < LimitMs)
                Perf.TakeFrame(Scopes);
            return;
        }
        Perf.TakeFrame(Scopes);
        var sb = new StringBuilder();
        sb.Append($"{DateTime.Now:HH:mm:ss.fff}  klatka {ms:0} ms  (FPS limit {Engine.MaxFps}, gra {now / 1_000_000.0:0.0} s)\n");
        sb.Append($"  kod gry: ");
        int shown = 0;
        foreach (var (name, scopeMs) in Scopes)
        {
            if (shown++ >= 6 || scopeMs < 0.5)
                break;
            sb.Append($"{name} {scopeMs:0.0} ms, ");
        }
        sb.Append('\n');
        sb.Append($"  rysowanie: {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame)} obiektów, "
            + $"{RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)} wywołań, "
            + $"GPU {RenderingServer.ViewportGetMeasuredRenderTimeGpu(Engine.GetMainLoop() is SceneTree t ? t.Root.GetViewportRid() : default):0.0} ms, "
            + $"VRAM {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:0} MB, "
            + $"RAM {OS.GetStaticMemoryUsage() / 1048576.0:0} MB, .NET {GC.GetTotalMemory(false) / 1048576.0:0} MB\n");
        System.Threading.ThreadPool.GetAvailableThreads(out int free, out _);
        System.Threading.ThreadPool.GetMaxThreads(out int max, out _);
        sb.Append($"  wątki w tle: {max - free}\n");
        lock (Recent)
            foreach (var (usec, text) in Recent)
                if (now - usec < 2_000_000)
                    sb.Append($"  {(now - usec) / 1000.0:0} ms wcześniej: {text}\n");
        Write(sb.ToString());
    }

    private static void Write(string text)
    {
        try
        {
            if (_path == null)
            {
                _path = ProjectSettings.GlobalizePath("user://logs/hitches.txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                System.IO.File.WriteAllText(_path, $"Fishing Sim: przycięcia (klatki > {LimitMs:0} ms), {DateTime.Now:u}\n"
                    + $"{RenderingServer.GetVideoAdapterName()} / {OS.GetProcessorName()} / {OS.GetName()}\n\n");
            }
            System.IO.File.AppendAllText(_path, text);
            _written++;
        }
        catch (Exception)
        {
            // the diary stays as it is
        }
    }
}
