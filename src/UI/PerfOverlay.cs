using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Godot;
using FishingSim.Graphics;

namespace FishingSim.UI;

/// <summary>
/// The performance overlay (F11, top left): frames per second and the frame time split into what the processor does
/// (the game's measured code, preparing the drawing, physics) and what the graphics card does (Godot measures the viewport's GPU time),
/// which of the two holds the frame back, the worst frame of the last second (stutter), what is drawn (objects, draw
/// calls, triangles), the 3D resolution and settings, memory (process RAM, .NET heap, video memory), busy worker
/// threads (textures painted in the background) and the game's slowest systems (<see cref="Perf"/> scopes).
/// Updated four times a second; nothing is measured while it is hidden.
/// </summary>
public partial class PerfOverlay : PanelContainer
{
    /// <summary>Set by the screenshot tool to show the overlay from the start.</summary>
    public static bool ShownByDefault { get; set; }

    private Label _text = null!;
    private Rid _viewport;
    private float _refresh;
    private int _frames;
    private double _frameSum, _renderCpuSum, _gpuSum;
    private double _worst, _worstShown;
    private float _worstAge;
    private readonly List<(string Name, double Ms)> _scopes = new();
    private readonly StringBuilder _sb = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", UITheme.Box(new Color(0.03f, 0.03f, 0.03f, 0.72f), new Color(1f, 0.84f, 0.45f, 0.5f), 6, 1, 8));
        _text = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _text.AddThemeFontSizeOverride("font_size", 13);
        _text.AddThemeColorOverride("font_color", new Color(0.92f, 0.95f, 0.9f));
        _text.AddThemeConstantOverride("line_spacing", 1);
        AddChild(_text);
        _viewport = GetViewport().GetViewportRid();
        Show(ShownByDefault);
    }

    public void Toggle() => Show(!Visible);

    private void Show(bool on)
    {
        Visible = on;
        Perf.Live = on;
        RenderingServer.ViewportSetMeasureRenderTime(_viewport, on);
        SetProcess(on);
        _refresh = 0f;
        Reset();
        _worst = _worstShown = 0.0;
        _worstAge = 0f;
        if (on)
            _text.Text = "Pomiar…";
    }

    public override void _ExitTree()
    {
        Perf.Live = false;
        if (_viewport.IsValid)
            RenderingServer.ViewportSetMeasureRenderTime(_viewport, false);
    }

    private void Reset()
    {
        _frames = 0;
        _frameSum = _renderCpuSum = _gpuSum = 0.0;
    }

    public override void _Process(double delta)
    {
        // Every frame's times are added up and shown as the average of the last quarter second.
        double ms = delta * 1000.0;
        _frames++;
        _frameSum += ms;
        _renderCpuSum += RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport) + RenderingServer.GetFrameSetupTimeCpu();
        _gpuSum += RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport);
        // The worst frame (a stutter) over the last second.
        _worst = Math.Max(_worst, ms);
        _worstAge += (float)delta;
        if (_worstAge >= 1f)
        {
            _worstShown = _worst;
            _worst = 0.0;
            _worstAge = 0f;
        }
        _refresh -= (float)delta;
        if (_refresh > 0f)
            return;
        _refresh = 0.25f;
        Perf.TakeWindow(_scopes);
        if (_frames == 0)
            return;

        double frame = _frameSum / _frames;
        double fps = 1000.0 / Math.Max(frame, 0.001);
        // The game's code: the sum of its measured systems (Godot's TimeProcess is the worst whole frame of the second,
        // drawing included, so it cannot tell scripts apart). Physics: Godot's worst physics step of the last second.
        double code = 0.0;
        foreach (var (_, total) in _scopes)
            code += total;
        code /= _frames;
        double physics = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        double renderCpu = _renderCpuSum / _frames;
        double gpu = _gpuSum / _frames;
        double objects = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        double draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        double prims = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        double vram = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0;
        double ram = System.Environment.WorkingSet / 1048576.0;
        double heap = GC.GetTotalMemory(false) / 1048576.0;
        ThreadPool.GetAvailableThreads(out int freeWorkers, out _);
        ThreadPool.GetMaxThreads(out int maxWorkers, out _);
        int busy = Math.Max(0, maxWorkers - freeWorkers);

        var vp = GetViewport();
        var size = vp is Window w ? w.Size : (Vector2I)vp.GetVisibleRect().Size;
        float scale = GraphicsQuality.RenderScale / 100f;

        _sb.Clear();
        _sb.Append($"FPS {fps:0}   klatka {frame:0.0} ms   najgorsza w ostatniej sekundzie {Math.Max(_worstShown, _worst):0} ms\n");
        _sb.Append($"CPU: kod gry {code:0.0} ms · przygotowanie obrazu {renderCpu:0.0} ms · fizyka (maks.) {physics:0.0} ms\n");
        _sb.Append($"GPU (karta graficzna) {gpu:0.0} ms");
        // The processor and the card work side by side: the frame lasts as long as the slower of the two. When the
        // card's time fills the frame, the card holds it back.
        if (fps < 55.0)
            _sb.Append(gpu >= frame * 0.8 ? "   → hamuje karta graficzna" : "   → hamuje procesor");
        _sb.Append('\n');
        _sb.Append($"Rysowanie: {objects:0} obiektów · {draws:0} wywołań · {prims / 1e6:0.00} M trójkątów\n");
        _sb.Append($"3D {(int)(size.X * scale)}x{(int)(size.Y * scale)} ({GraphicsQuality.RenderScale}%) · grafika {GraphicsQuality.Title(GraphicsQuality.Level)}"
            + $" · zasięg {GraphicsQuality.ViewTitle(GraphicsQuality.View)}\n");
        _sb.Append($"Pamięć: RAM {ram / 1024.0:0.00} GB · .NET {heap:0} MB · VRAM {vram:0} MB");
        if (busy > 0)
            _sb.Append($"\nPrace w tle: {busy} wątk{(busy == 1 ? "" : busy < 5 ? "i" : "ów")} (np. malowanie tekstur)");
        int shown = 0;
        foreach (var (name, total) in _scopes)
        {
            double per = total / _frames;
            if (per < 0.05 || shown == 6)
                break;
            _sb.Append(shown == 0 ? "\nNajwolniejsze: " : " · ").Append($"{name} {per:0.0}");
            shown++;
        }
        if (shown > 0)
            _sb.Append(" ms");
        _text.Text = _sb.ToString();
        Reset();
    }
}
