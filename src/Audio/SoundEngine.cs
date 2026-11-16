using System;
using Godot;
using FishingSim.Core.Audio;
using FishingSim.Fishing;
using FishingSim.World;

namespace FishingSim.Audio;

/// <summary>
/// Streams <see cref="Synth"/> to the speakers through an AudioStreamGenerator and keeps its levels in step with the
/// game: wind and rain from the weather, water near the shore, birds by day and crickets at night, the reel while
/// reeling, the drag while it gives line, and one-shots from the rod and the angler's steps. Silent when headless.
/// </summary>
public partial class SoundEngine : Node
{
    private const float MixRate = 22050f;

    private readonly Synth _synth = new(MixRate);
    private AudioStreamPlayer? _player;
    private AudioStreamGeneratorPlayback? _playback;
    private float[] _buffer = Array.Empty<float>();
    private const int Chunk = 256;
    private readonly Vector2[] _chunk = new Vector2[Chunk];
    private FishingRod _rodFixed = null!;
    /// <summary>The rod in use (the active one of the rod set, Fishing/RodSet).</summary>
    private FishingRod _rod => FishingRod.ActiveRod ?? _rodFixed;
    private Player _angler = null!;
    private WorldState _world = null!;
    private LakeBed _lake = null!;
    private float _thrash;

    public Synth Synth => _synth;

    /// <summary>Thunder after a lightning flash, <paramref name="delay"/> seconds later (sound travels 340 m/s).</summary>
    public void Thunder(float delay)
    {
        var timer = GetTree()?.CreateTimer(delay);
        if (timer != null)
            timer.Timeout += () => _synth.Play(OneShot.Thunder, Math.Clamp(1.4f - delay * 0.12f, 0.5f, 1.3f));
    }

    /// <summary>A fish rising at a distance: a plop, quieter further off.</summary>
    public void Rise(float distance)
    {
        if (distance < 40f)
            _synth.Play(OneShot.Plop, Math.Clamp(1f - distance / 40f, 0f, 1f) * 0.8f);
    }

    /// <summary>Plays the cues of another rod of the set too.</summary>
    public void Track(FishingRod rod) => rod.SoundCue += (cue, gain) => _synth.Play(cue, gain);

    public void Setup(FishingRod rod, Player angler, WorldState world, LakeBed lake)
    {
        _rodFixed = rod;
        _angler = angler;
        _world = world;
        _lake = lake;
        rod.SoundCue += (cue, gain) => _synth.Play(cue, gain);
        angler.Motion.Footstep += OnStep;
    }

    private void OnStep()
    {
        var p = _angler.GlobalPosition;
        bool wood = LakeBed.OnPier(p.X, p.Z);
        _synth.Play(wood ? OneShot.StepWood : OneShot.StepGrass, Bindings.Held(Act.Sprint) ? 1.2f : 0.8f);
    }

    public override void _Ready()
    {
        if (Graphics.Renderer.IsHeadless)
            return;
        _player = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.15f },
            VolumeDb = -4f,
        };
        AddChild(_player);
        _player.Play();
        _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("sound");
        if (_rod == null)
            return;
        UpdateLevels((float)delta);
        if (_playback == null)
            return;
        int frames = _playback.GetFramesAvailable();
        if (frames <= 0)
            return;
        // Pushed in fixed chunks of a reused array: slicing a fresh array every frame fed the garbage collector.
        frames -= frames % Chunk;
        if (frames <= 0)
            return;
        if (_buffer.Length < frames)
            _buffer = new float[frames];
        _synth.Generate(_buffer.AsSpan(0, frames));
        for (int at = 0; at < frames; at += Chunk)
        {
            for (int i = 0; i < Chunk; i++)
                _chunk[i] = new Vector2(_buffer[at + i], _buffer[at + i]);
            _playback.PushBuffer(_chunk);
        }
    }

    private void UpdateLevels(float dt)
    {
        var p = _angler.GlobalPosition;
        float shore = Math.Clamp(1f - MathF.Abs(_lake.InsideMetres(p.X, p.Z)) / 30f, 0f, 1f);
        float day = _world.Daylight;
        float rain = _world.Weather == Core.Weather.Rain ? 1f : 0f;
        ref var L = ref _synth.Levels;
        L.Wind = Mathf.MoveToward(L.Wind, _world.WindTarget * 0.55f, dt * 0.2f);
        L.Rain = Mathf.MoveToward(L.Rain, rain, dt * 0.3f);
        L.Water = 0.15f + 0.55f * shore;
        L.Birds = day * (1f - rain) * 0.8f;
        L.Crickets = Math.Clamp(1f - day * 2f, 0f, 1f) * (1f - rain);
        bool lineOut = _rod.State is RodState.Waiting or RodState.Fight or RodState.Nibble or RodState.Bite;
        L.Reel = _rod.Reeling && lineOut ? 1f : 0f;
        L.Drag = Mathf.MoveToward(L.Drag, _rod.DragSlipping ? 1f : 0f, dt * 8f);
        // The nearest burning campfire crackles.
        float fire = 0f;
        foreach (var f in World.Props.Campfire.All)
            if (f.Lit && GodotObject.IsInstanceValid(f) && f.IsInsideTree())
                fire = MathF.Max(fire, Math.Clamp(1f - (f.GlobalPosition.DistanceTo(p) - 2f) / 14f, 0f, 1f));
        L.Fire = Mathf.MoveToward(L.Fire, fire, dt * 2f);
        // Frogs from dusk to dawn near the water (fewer in the cold and the rain); the dawn chorus of birds.
        float hour = _world.Hour;
        float dusk = Math.Clamp(1f - day * 1.6f, 0f, 1f);
        L.Frogs = dusk * shore * (1f - rain * 0.7f) * 0.9f;
        float dawn = MathF.Exp(-MathF.Pow((hour - 5.8f) / 1.1f, 2f));
        L.Birds = Math.Clamp(L.Birds * (0.6f + 1.4f * dawn), 0f, 1f);
        // Reeds and grass rustle with the wind, louder by the shore.
        L.Reeds = Mathf.MoveToward(L.Reeds, Math.Clamp(_world.WindTarget * (0.35f + 0.65f * shore), 0f, 1f), dt * 0.3f);
        // Water against the boat when aboard, against the pier's posts when standing on it.
        float hull = Rowboat.Aboard ? 1f : LakeBed.OnPier(p.X, p.Z) ? 0.55f : 0f;
        L.Hull = Mathf.MoveToward(L.Hull, hull * (0.4f + _world.WindTarget), dt * 2f);
        // The village by day: voices and work, fading over 25-90 m from its square.
        var plaza = _lake.Location?.PlazaCenter ?? new Vector2(0f, 28f);
        float village = Math.Clamp(1f - (new Vector2(p.X, p.Z).DistanceTo(plaza) - 25f) / 65f, 0f, 1f);
        L.Village = village * Math.Clamp(day * 1.5f, 0f, 1f) * (1f - rain * 0.6f);

        // A beaten fish splashes at the surface.
        if (_rod.State == RodState.Close && _rod.Lift < 0.3f)
        {
            _thrash -= dt;
            if (_thrash <= 0f)
            {
                _thrash = 0.4f + GD.Randf() * 0.6f;
                _synth.Play(OneShot.SmallSplash, 0.6f);
            }
        }
    }
}
