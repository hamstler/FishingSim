using System;
using Godot;
using FishingSim.Core;
using FishingSim.Fishing;
using FishingSim.World;

namespace FishingSim.UI;

/// <summary>
/// Top-right corner of the HUD: a square minimap (north up, 120 m across, centred on the angler) with the village
/// buildings, landmarks, pier, float and the angler's arrow, and under it a clock: a 24-hour dial with the day and
/// night arcs, the hand on the current hour, the time, the weather and how long until dusk or dawn.
/// </summary>
public partial class Minimap : Control
{
    public const float MapSize = 190f;

    /// <summary>Metres shown across the map.</summary>
    public const float ViewMetres = 120f;

    /// <summary>Resolution of the pre-rendered terrain (pixels per metre).</summary>
    private const float TexturePerMetre = 1.5f;

    private static readonly Color Frame = UITheme.Border;
    private static readonly Color Ink = UITheme.Text;

    private FishingRod _rod = null!;
    private Node3D _player = null!;
    private Scenery _scenery = null!;
    private Data.LocationData? _renderedFor;
    private ImageTexture? _terrain;
    private Rect2 _area;

    public void Setup(FishingRod rod, Node3D player, Scenery scenery)
    {
        _rod = rod;
        _player = player;
        _scenery = scenery;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>The terrain picture covers the whole playable area once; the view slides over it.</summary>
    private void EnsureTerrain()
    {
        var loc = _rod.Lake.Location;
        if (_renderedFor == loc && _terrain != null)
            return;
        _renderedFor = loc;
        _area = new Rect2(LakeBed.XMin, LakeBed.ZMin, LakeBed.XMax - LakeBed.XMin, LakeBed.ZMax - LakeBed.ZMin);
        int w = (int)(_area.Size.X * TexturePerMetre), h = (int)(_area.Size.Y * TexturePerMetre);
        _terrain = MapPreview.Render(loc, w, h, _area);
    }

    private float _redraw;

    /// <summary>Redrawn 15 times a second: plenty for a map, a fraction of the drawing work.</summary>
    public override void _Process(double delta)
    {
        if (_rod == null)
            return;
        _redraw -= (float)delta;
        if (_redraw > 0f)
            return;
        _redraw = 1f / 15f;
        QueueRedraw();
    }

    /// <summary>World (x, z) to a point on the minimap, given the centre of the view.</summary>
    public static Vector2 ToMap(Vector2 world, Vector2 centre) =>
        new Vector2(MapSize / 2f, MapSize / 2f) + (world - centre) * (MapSize / ViewMetres);

    public override void _Draw()
    {
        if (_rod == null || Graphics.Renderer.IsHeadless)
            return;
        EnsureTerrain();
        var centre = new Vector2(_player.GlobalPosition.X, _player.GlobalPosition.Z);
        var box = new Rect2(0, 0, MapSize, MapSize);

        // Frame and terrain.
        DrawRect(box.Grow(5f), new Color(0.08f, 0.05f, 0.03f, 0.9f));
        DrawRect(box.Grow(3f), Frame, false, 2f);
        if (_terrain != null)
        {
            var src = new Rect2(
                (centre.X - ViewMetres / 2f - _area.Position.X) * TexturePerMetre,
                (centre.Y - ViewMetres / 2f - _area.Position.Y) * TexturePerMetre,
                ViewMetres * TexturePerMetre, ViewMetres * TexturePerMetre);
            DrawRect(box, new Color(0.16f, 0.22f, 0.12f));
            // Clip the source to the texture so the edge of the world does not smear.
            var clipped = src.Intersection(new Rect2(Vector2.Zero, _terrain.GetSize()));
            if (clipped.Size.X > 0f && clipped.Size.Y > 0f)
            {
                var dst = new Rect2(
                    (clipped.Position - src.Position) / src.Size * MapSize,
                    clipped.Size / src.Size * MapSize);
                DrawTextureRectRegion(_terrain, dst, clipped, new Color(1, 1, 1, 0.95f));
            }
        }

        // Pier, buildings and landmarks.
        var pierA = ToMap(new Vector2(0f, LakeBed.PierEndZ), centre);
        var pierB = ToMap(new Vector2(0f, 6f), centre);
        if (box.HasPoint(pierA) || box.HasPoint(pierB))
            DrawLine(Clamp(pierA, box), Clamp(pierB, box), new Color(0.62f, 0.44f, 0.26f), 3f);
        var font = ThemeDB.FallbackFont;
        foreach (var marker in _scenery.Markers)
        {
            var p = ToMap(marker.Position, centre);
            if (!box.Grow(-3f).HasPoint(p))
                continue;
            DrawRect(new Rect2(p - new Vector2(3.5f, 3.5f), new Vector2(7, 7)), marker.Color);
            DrawRect(new Rect2(p - new Vector2(3.5f, 3.5f), new Vector2(7, 7)), new Color(0, 0, 0, 0.8f), false, 1f);
        }

        // Float and line.
        var me = new Vector2(MapSize / 2f, MapSize / 2f);
        if (_rod.State is RodState.Waiting or RodState.Nibble or RodState.Bite or RodState.Fight or RodState.Close)
        {
            var b = ToMap(new Vector2(_rod.Bobber.GlobalPosition.X, _rod.Bobber.GlobalPosition.Z), centre);
            DrawLine(me, Clamp(b, box), new Color(1, 1, 1, 0.55f), 1f);
            if (box.HasPoint(b))
                DrawCircle(b, 3f, _rod.State is RodState.Fight or RodState.Close ? new Color(1f, 0.35f, 0.25f) : Colors.White);
        }

        // The angler: an arrow pointing where the camera looks.
        var f = -_player.GlobalTransform.Basis.Z;
        var dir = new Vector2(f.X, f.Z);
        dir = dir.LengthSquared() > 1e-4f ? dir.Normalized() : Vector2.Up;
        var perp = new Vector2(-dir.Y, dir.X);
        DrawColoredPolygon(new[] { me + dir * 9f, me - dir * 5f + perp * 5.5f, me - dir * 2f, me - dir * 5f - perp * 5.5f }, new Color(1f, 0.88f, 0.3f));
        DrawPolyline(new[] { me + dir * 9f, me - dir * 5f + perp * 5.5f, me - dir * 2f, me - dir * 5f - perp * 5.5f, me + dir * 9f }, new Color(0.1f, 0.06f, 0.03f), 1.2f);
        DrawString(font, new Vector2(MapSize / 2f - 4f, 12f), "N", HorizontalAlignment.Left, -1, 12, Ink);

        DrawClock(new Vector2(0f, MapSize + 12f), font);
    }

    private static Vector2 Clamp(Vector2 p, Rect2 box) =>
        new(Math.Clamp(p.X, box.Position.X, box.End.X), Math.Clamp(p.Y, box.Position.Y, box.End.Y));

    /// <summary>The 24-hour dial with day (gold) and night (blue) arcs, plus the time and what comes next.</summary>
    private void DrawClock(Vector2 at, Font font)
    {
        var world = _rod.World;
        const float r = 22f;
        var c = at + new Vector2(r + 2f, r + 2f);
        DrawCircle(c, r + 3f, new Color(0.08f, 0.05f, 0.03f, 0.9f));
        // Midnight at the bottom, noon at the top.
        float AngleOf(float hour) => Mathf.Pi / 2f + hour / 24f * Mathf.Tau;
        DrawArc(c, r - 3f, AngleOf(DayClock.DayStart), AngleOf(DayClock.NightStart), 24, new Color(0.95f, 0.75f, 0.3f), 5f);
        DrawArc(c, r - 3f, AngleOf(DayClock.NightStart), AngleOf(DayClock.DayStart + 24f), 24, new Color(0.3f, 0.38f, 0.65f), 5f);
        DrawArc(c, r + 1f, 0f, Mathf.Tau, 40, Frame, 2f);
        float a = AngleOf(world.Hour);
        var tip = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (r - 2f);
        DrawLine(c, tip, Ink, 2f);
        bool day = DayClock.IsDaytime(world.Hour);
        DrawCircle(tip, 3.5f, day ? new Color(1f, 0.9f, 0.45f) : new Color(0.85f, 0.88f, 1f));
        DrawCircle(c, 2f, Ink);

        float until = DayClock.RealSecondsUntil(world.Hour, day ? DayClock.NightStart : DayClock.DayStart) / Math.Max(world.TimeScale, 1f);
        string next = day ? "do zmroku" : "do świtu";
        var x = c.X + r + 10f;
        float width = MapSize - (x - at.X);
        DrawString(font, new Vector2(x, c.Y - 6f), world.TimeText, HorizontalAlignment.Left, -1, 20, Ink);
        string weather = !day && world.Weather == Core.Weather.Clear ? "Pogodnie" : world.Weather.DisplayName();
        DrawString(font, new Vector2(x, c.Y + 9f), weather, HorizontalAlignment.Left, width, 12, new Color(Ink, 0.8f));
        DrawString(font, new Vector2(x, c.Y + 23f), $"{MathF.Ceiling(until / 60f):0} min {next}", HorizontalAlignment.Left, width, 12,
            day ? new Color(1f, 0.85f, 0.5f, 0.85f) : new Color(0.75f, 0.82f, 1f, 0.85f));
        DrawString(font, new Vector2(at.X, c.Y + r + 22f), _rod.Lake.Location.DisplayName, HorizontalAlignment.Left, MapSize, 13, new Color(Ink, 0.75f));
    }
}
