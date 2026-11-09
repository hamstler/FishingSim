using System;
using Godot;
using FishingSim.Core;

namespace FishingSim.World;

/// <summary>Time of day and weather. Fish activity reads from here; the sky and water controllers follow it.</summary>
public partial class WorldState : Node
{
    private readonly IRng _rng = new SystemRng();
    private float _weatherTimer = 4f;   // game hours until the next weather roll

    public float Hour { get; set; } = 7f;
    public Weather Weather { get; set; } = Weather.Clear;
    public bool Paused { get; set; }

    /// <summary>Game minutes per real second (1 = a full day lasts 24 real minutes).</summary>
    public float MinutesPerSecond { get; set; } = 1f;

    public override void _Process(double delta)
    {
        if (!Paused)
            Advance((float)delta * MinutesPerSecond / 60f);
    }

    public void Advance(float hours)
    {
        Hour = Mathf.PosMod(Hour + hours, 24f);
        _weatherTimer -= hours;
        if (_weatherTimer <= 0f)
            RollWeather();
    }

    private void RollWeather()
    {
        float r = _rng.NextFloat();
        Weather = r < 0.5f ? Weather.Clear : r < 0.8f ? Weather.Cloudy : Weather.Rain;
        _weatherTimer = _rng.Range(2f, 6f);
    }

    public void CycleWeather()
    {
        Weather = (Weather)(((int)Weather + 1) % 3);
        _weatherTimer = 4f;
    }

    public DayPeriod Period =>
        (Hour >= 5f && Hour < 8f) || (Hour >= 18f && Hour < 21f) ? DayPeriod.Twilight
        : Hour >= 21f || Hour < 5f ? DayPeriod.Night
        : DayPeriod.Day;

    public string TimeText => $"{(int)Hour:00}:{(int)((Hour - MathF.Floor(Hour)) * 60f):00}";

    /// <summary>0 (dark) .. 1 (full sun).</summary>
    public float Daylight => Math.Clamp(MathF.Sin((Hour - 6f) / 24f * Mathf.Tau) * 2f + 0.35f, 0f, 1f);

    // Targets the sky/water controllers ease towards (0..1).
    public float CloudTarget => Weather switch { Weather.Clear => 0.05f, Weather.Cloudy => 0.65f, _ => 1f };
    public float RainTarget => Weather == Weather.Rain ? 1f : 0f;
    public float WindTarget => Weather switch { Weather.Clear => 0.25f, Weather.Cloudy => 0.5f, _ => 0.85f };
}
