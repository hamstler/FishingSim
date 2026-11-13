using System;
using Godot;
using FishingSim.Core;
using FishingSim.Fishing;
using FishingSim.World;

namespace FishingSim.UI;

/// <summary>
/// The top-right corner of the HUD: one round dial like a brass-rimmed compass. In the middle a round topographic map
/// (120 m across, centred on the angler and turned with him, so ahead is always up; no markers): the land shaded by
/// its relief with contour lines, the lake with its depth contours, the shore, the float and the angler's arrow. The bezel of dark
/// wood carries the compass letters (turning with the map). Round it the ring of the day: gold by day, blue by night,
/// noon at the top, with the sun or the moon riding on it at the hour. A medallion on the lower left shows the weather
/// (sun, moon and stars, clouds, rain) and the wind. Under the dial a plaque: the time, how long until dusk or dawn,
/// the weather and wind, the fishery's name.
/// </summary>
public partial class Minimap : Control
{
    /// <summary>The whole widget's size.</summary>
    public const float WidgetWidth = 250f, WidgetHeight = 318f;

    /// <summary>Kept for the HUD's layout: the dial's diameter.</summary>
    public const float MapSize = 236f;

    /// <summary>Metres shown across the map.</summary>
    public const float ViewMetres = 120f;

    /// <summary>Resolution of the pre-rendered terrain (pixels per metre).</summary>
    private const float TexturePerMetre = 1.5f;

    private const float MapRadius = 92f, BezelOuter = 106f, DayRingRadius = 112f;

    /// <summary>The round map's diameter on screen (it shows <see cref="ViewMetres"/> across).</summary>
    public const float MapDiameter = MapRadius * 2f;

    private static readonly Color Ink = UITheme.Text;
    private static readonly Color Brass = new(0.86f, 0.68f, 0.34f);
    private static readonly Color BrassDark = new(0.45f, 0.33f, 0.15f);
    private static readonly Color Wood = new(0.2f, 0.12f, 0.07f, 0.96f);
    private static readonly Color WoodLight = new(0.33f, 0.21f, 0.12f, 0.96f);
    private static readonly Color DayGold = new(0.98f, 0.78f, 0.32f);
    private static readonly Color NightBlue = new(0.32f, 0.42f, 0.72f);

    private FishingRod _rod = null!;
    private Node3D _player = null!;
    private Scenery _scenery = null!;
    private Data.LocationData? _renderedFor;
    private ImageTexture? _terrain;
    private Rect2 _area;

    private Vector2 Centre => new(WidgetWidth / 2f, 4f + DayRingRadius + 4f);

    public void Setup(FishingRod rod, Node3D player, Scenery scenery)
    {
        _rod = rod;
        _player = player;
        _scenery = scenery;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private System.Threading.Tasks.Task<Image>? _painting;

    /// <summary>The topographic picture covers the whole playable area once (painted in the background when a map
    /// opens); the view slides over it.</summary>
    private void EnsureTerrain()
    {
        var loc = _rod.Lake.Location;
        if (_renderedFor != loc)
        {
            _renderedFor = loc;
            _terrain = null;
            _area = new Rect2(LakeBed.XMin, LakeBed.ZMin, LakeBed.XMax - LakeBed.XMin, LakeBed.ZMax - LakeBed.ZMin);
            int w = (int)(_area.Size.X * TexturePerMetre), h = (int)(_area.Size.Y * TexturePerMetre);
            var area = _area;
            _painting = System.Threading.Tasks.Task.Run(() => MapPreview.RenderTopo(loc, w, h, area));
        }
        if (_terrain == null && _painting is { IsCompleted: true } done)
        {
            if (!done.IsFaulted)
                _terrain = ImageTexture.CreateFromImage(done.Result);
            _painting = null;
        }
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

    /// <summary>World (x, z) to a point on the round map seen north up, within a square of <see cref="MapDiameter"/>.</summary>
    public static Vector2 ToMap(Vector2 world, Vector2 centre) =>
        new Vector2(MapDiameter / 2f, MapDiameter / 2f) + (world - centre) * (MapDiameter / ViewMetres);

    private float _turn;          // radians: the map turned so the angler's view points up
    private Vector2 _view;        // the map's centre in the world (x, z)

    /// <summary>A world point (x, z) on the dial (turned with the angler).</summary>
    private Vector2 OnDial(Vector2 world)
    {
        var v = (world - _view) * (MapRadius * 2f / ViewMetres);
        float c = MathF.Cos(_turn), s = MathF.Sin(_turn);
        return Centre + new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    public override void _Draw()
    {
        if (_rod == null || Graphics.Renderer.IsHeadless)
            return;
        EnsureTerrain();
        var font = GetThemeDefaultFont() ?? ThemeDB.FallbackFont;
        _view = new Vector2(_player.GlobalPosition.X, _player.GlobalPosition.Z);
        var f = -_player.GlobalTransform.Basis.Z;
        var look = new Vector2(f.X, f.Z);
        look = look.LengthSquared() > 1e-4f ? look.Normalized() : Vector2.Up;
        _turn = -MathF.Atan2(look.X, -look.Y);
        var c = Centre;

        DrawDayRing(c);
        // The bezel: dark wood between two brass rims, with a soft shadow outside.
        DrawCircle(c + new Vector2(2f, 3f), BezelOuter + 2f, new Color(0f, 0f, 0f, 0.35f));
        DrawCircle(c, BezelOuter, Wood);
        DrawArc(c, BezelOuter - 1f, 0f, Mathf.Tau, 96, Brass, 2.2f, true);
        DrawArc(c, BezelOuter - 4.5f, 0f, Mathf.Tau, 96, WoodLight, 3f, true);
        DrawArc(c, MapRadius + 2.5f, 0f, Mathf.Tau, 96, Brass, 3f, true);
        DrawArc(c, MapRadius + 4.5f, 0f, Mathf.Tau, 96, BrassDark, 1.2f, true);

        DrawMap(c, font);

        // Compass letters and ticks on the bezel, turning with the map.
        for (int i = 0; i < 16; i++)
        {
            float a = i / 16f * Mathf.Tau - Mathf.Pi / 2f + _turn;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            bool cardinal = i % 4 == 0;
            if (!cardinal)
                DrawLine(c + dir * (MapRadius + 6f), c + dir * (MapRadius + (i % 2 == 0 ? 10f : 8f)), new Color(Brass, 0.7f), 1.2f, true);
        }
        string[] letters = { "N", "E", "S", "W" };
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * Mathf.Tau - Mathf.Pi / 2f + _turn;
            var at = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (MapRadius + 8.5f);
            int size = i == 0 ? 13 : 11;
            var tsize = font.GetStringSize(letters[i], HorizontalAlignment.Left, -1, size);
            DrawString(font, at + new Vector2(-tsize.X / 2f, size * 0.38f), letters[i], HorizontalAlignment.Left, -1, size,
                i == 0 ? new Color(1f, 0.45f, 0.32f) : new Color(Brass, 0.95f));
        }

        DrawWeather(c + new Vector2(-0.72f, 0.72f) * (BezelOuter + 4f), font);
        DrawPlaque(new Vector2(0f, c.Y + DayRingRadius + 8f), font);
    }

    /// <summary>The round map: the topographic picture turned and cut to the circle, the float and its line, the
    /// angler's arrow (always up), and a soft shade round the edge.</summary>
    private void DrawMap(Vector2 c, Font font)
    {
        DrawCircle(c, MapRadius, new Color(0.16f, 0.22f, 0.12f));
        if (_terrain != null)
        {
            const int n = 72;
            var points = new Vector2[n];
            var uvs = new Vector2[n];
            var colours = new Color[n];
            var size = _terrain.GetSize();
            float back = ViewMetres / (MapRadius * 2f);
            float cs = MathF.Cos(-_turn), sn = MathF.Sin(-_turn);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.Tau;
                var d = new Vector2(MathF.Cos(a), MathF.Sin(a)) * MapRadius;
                points[i] = c + d;
                // The dial point back to the world (undo the turn), then to the picture.
                var w = _view + new Vector2(d.X * cs - d.Y * sn, d.X * sn + d.Y * cs) * back;
                uvs[i] = (w - _area.Position) * TexturePerMetre / size;
                colours[i] = new Color(1f, 1f, 1f, 0.97f);
            }
            DrawPolygon(points, colours, uvs, _terrain);
        }

        // Float and line.
        if (_rod.State is RodState.Waiting or RodState.Nibble or RodState.Bite or RodState.Fight or RodState.Close)
        {
            var b = OnDial(new Vector2(_rod.Bobber.GlobalPosition.X, _rod.Bobber.GlobalPosition.Z));
            var d = b - c;
            if (d.Length() > MapRadius - 3f)
                b = c + d.Normalized() * (MapRadius - 3f);
            DrawLine(c, b, new Color(1, 1, 1, 0.55f), 1f, true);
            DrawCircle(b, 3f, _rod.State is RodState.Fight or RodState.Close ? new Color(1f, 0.35f, 0.25f) : Colors.White);
        }

        // A soft shade round the inside of the rim (the glass sits in the bezel).
        for (int i = 0; i < 6; i++)
            DrawArc(c, MapRadius - i * 1.6f, 0f, Mathf.Tau, 72, new Color(0f, 0f, 0f, 0.16f - i * 0.025f), 2f, true);

        // The angler, always pointing up.
        var up = Vector2.Up;
        var perp = Vector2.Right;
        var arrow = new[] { c + up * 10f, c - up * 6f + perp * 6.5f, c - up * 2.5f, c - up * 6f - perp * 6.5f };
        DrawColoredPolygon(arrow, new Color(1f, 0.88f, 0.3f));
        DrawPolyline(new[] { arrow[0], arrow[1], arrow[2], arrow[3], arrow[0] }, new Color(0.1f, 0.06f, 0.03f), 1.4f, true);
    }

    /// <summary>The ring of the day round the bezel: day gold, night blue, noon at the top, the sun or the moon on it.</summary>
    private void DrawDayRing(Vector2 c)
    {
        var world = _rod.World;
        float AngleOf(float hour) => Mathf.Pi / 2f + hour / 24f * Mathf.Tau;   // midnight at the bottom, noon at the top
        DrawCircle(c, DayRingRadius + 5f, new Color(0.06f, 0.04f, 0.02f, 0.85f));
        DrawArc(c, DayRingRadius, AngleOf(DayClock.DayStart), AngleOf(DayClock.NightStart), 48, new Color(DayGold, 0.9f), 5f, true);
        DrawArc(c, DayRingRadius, AngleOf(DayClock.NightStart), AngleOf(DayClock.DayStart + 24f), 48, new Color(NightBlue, 0.9f), 5f, true);
        DrawArc(c, DayRingRadius + 4.5f, 0f, Mathf.Tau, 96, new Color(Brass, 0.8f), 1.2f, true);
        float a = AngleOf(world.Hour);
        var at = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * DayRingRadius;
        bool day = DayClock.IsDaytime(world.Hour);
        if (day)
        {
            for (int i = 0; i < 8; i++)
            {
                float ra = i / 8f * Mathf.Tau;
                var dir = new Vector2(MathF.Cos(ra), MathF.Sin(ra));
                DrawLine(at + dir * 7.5f, at + dir * 10.5f, new Color(1f, 0.85f, 0.4f), 1.6f, true);
            }
            DrawCircle(at, 6.5f, new Color(1f, 0.86f, 0.36f));
            DrawCircle(at, 4.5f, new Color(1f, 0.95f, 0.65f));
        }
        else
        {
            DrawCircle(at, 6.5f, new Color(0.88f, 0.9f, 1f));
            DrawCircle(at + new Vector2(2.6f, -1.8f), 5.2f, new Color(0.06f, 0.04f, 0.02f, 0.95f));
        }
    }

    /// <summary>The weather medallion: sun, moon and stars, clouds or rain, with the wind's strength in streaks.</summary>
    private void DrawWeather(Vector2 at, Font font)
    {
        var world = _rod.World;
        const float r = 27f;
        DrawCircle(at + new Vector2(1.5f, 2.5f), r + 1f, new Color(0f, 0f, 0f, 0.35f));
        DrawCircle(at, r, Wood);
        DrawArc(at, r - 1f, 0f, Mathf.Tau, 48, Brass, 2.2f, true);
        bool day = DayClock.IsDaytime(world.Hour);
        var w = world.Weather;
        var sky = day ? new Color(0.36f, 0.56f, 0.78f) : new Color(0.1f, 0.13f, 0.26f);
        if (w != Weather.Clear)
            sky = day ? new Color(0.42f, 0.47f, 0.53f) : new Color(0.12f, 0.13f, 0.18f);
        DrawCircle(at, r - 3.5f, sky);
        void Cloud(Vector2 p, float k, Color col)
        {
            DrawCircle(p + new Vector2(-7f, 2f) * k, 6f * k, col);
            DrawCircle(p + new Vector2(0f, -2f) * k, 8f * k, col);
            DrawCircle(p + new Vector2(8f, 2f) * k, 6.5f * k, col);
            DrawRect(new Rect2(p + new Vector2(-7f, 2f) * k, new Vector2(15f, 6f) * k), col);
        }
        switch (w)
        {
            case Weather.Clear when day:
                for (int i = 0; i < 10; i++)
                {
                    float ra = i / 10f * Mathf.Tau;
                    var d = new Vector2(MathF.Cos(ra), MathF.Sin(ra));
                    DrawLine(at + d * 11f, at + d * 16f, new Color(1f, 0.85f, 0.35f), 2f, true);
                }
                DrawCircle(at, 9f, new Color(1f, 0.82f, 0.3f));
                DrawCircle(at - new Vector2(2f, 2f), 5f, new Color(1f, 0.95f, 0.65f));
                break;
            case Weather.Clear:
                foreach (var (x, y) in new[] { (-12f, -9f), (10f, -12f), (13f, 6f), (-14f, 8f), (2f, 14f) })
                    DrawCircle(at + new Vector2(x, y), 1.1f, new Color(1f, 1f, 1f, 0.85f));
                DrawCircle(at, 9f, new Color(0.9f, 0.92f, 1f));
                DrawCircle(at + new Vector2(4f, -3f), 7.5f, sky);
                break;
            case Weather.Cloudy:
                if (day)
                    DrawCircle(at + new Vector2(6f, -7f), 7f, new Color(1f, 0.82f, 0.3f));
                Cloud(at + new Vector2(-1f, 3f), 1f, day ? new Color(0.93f, 0.94f, 0.96f) : new Color(0.55f, 0.58f, 0.65f));
                break;
            default:
                Cloud(at + new Vector2(0f, -3f), 1f, day ? new Color(0.72f, 0.75f, 0.8f) : new Color(0.45f, 0.48f, 0.55f));
                for (int i = 0; i < 4; i++)
                {
                    var p = at + new Vector2(-8f + i * 5.5f, 9f + (i % 2) * 3f);
                    DrawLine(p, p + new Vector2(-2f, 6f), new Color(0.6f, 0.78f, 1f), 1.6f, true);
                }
                break;
        }
        // Wind: one to three streaks under the icon.
        int streaks = world.WindTarget < 0.35f ? 1 : world.WindTarget < 0.7f ? 2 : 3;
        for (int i = 0; i < streaks; i++)
        {
            var p = at + new Vector2(-10f + i * 2f, 18.5f - i * 0f);
            DrawLine(p + new Vector2(i * 3f, 0f), p + new Vector2(10f + i * 3f, 0f), new Color(1f, 1f, 1f, 0.75f), 1.4f, true);
        }
        DrawArc(at, r - 3.5f, 0f, Mathf.Tau, 48, new Color(0f, 0f, 0f, 0.4f), 1.5f, true);
    }

    /// <summary>The plaque under the dial: time, until dusk or dawn, weather and wind, the fishery.</summary>
    private void DrawPlaque(Vector2 at, Font font)
    {
        var world = _rod.World;
        bool day = DayClock.IsDaytime(world.Hour);
        var box = new Rect2(at + new Vector2(18f, 0f), new Vector2(WidgetWidth - 36f, 66f));
        DrawStyleBox(UITheme.Box(new Color(0.08f, 0.05f, 0.03f, 0.82f), new Color(Brass, 0.7f), 8, 1), box);
        float until = DayClock.RealSecondsUntil(world.Hour, day ? DayClock.NightStart : DayClock.DayStart) / Math.Max(world.TimeScale, 1f);
        string time = world.TimeText;
        var timeSize = font.GetStringSize(time, HorizontalAlignment.Left, -1, 24);
        string next = $"{MathF.Ceiling(until / 60f):0} min {(day ? "do zmroku" : "do świtu")}";
        var nextSize = font.GetStringSize(next, HorizontalAlignment.Left, -1, 12);
        float rowX = box.Position.X + (box.Size.X - timeSize.X - 8f - nextSize.X) / 2f;
        DrawString(font, new Vector2(rowX, box.Position.Y + 24f), time, HorizontalAlignment.Left, -1, 24, Ink);
        DrawString(font, new Vector2(rowX + timeSize.X + 8f, box.Position.Y + 21f), next, HorizontalAlignment.Left, -1, 12,
            day ? new Color(1f, 0.85f, 0.5f, 0.9f) : new Color(0.75f, 0.82f, 1f, 0.9f));
        string weather = !day && world.Weather == Weather.Clear ? "Pogodnie" : world.Weather.DisplayName();
        string wind = world.WindTarget < 0.35f ? "lekki wiatr" : world.WindTarget < 0.7f ? "umiarkowany wiatr" : "silny wiatr";
        DrawString(font, new Vector2(box.Position.X, box.Position.Y + 42f), $"{weather} · {wind}", HorizontalAlignment.Center, box.Size.X, 13,
            new Color(Ink, 0.88f));
        DrawString(font, new Vector2(box.Position.X, box.Position.Y + 59f), _rod.Lake.Location.DisplayName, HorizontalAlignment.Center, box.Size.X, 13,
            new Color(Brass, 0.95f));
    }
}
