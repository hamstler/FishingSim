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
    private Vector2[] _frames = Array.Empty<Vector2>();
    private FishingRod _rod = null!;
    private Player _angler = null!;
    private WorldState _world = null!;
    private LakeBed _lake = null!;
    private float _thrash;

    public Synth Synth => _synth;

    public void Setup(FishingRod rod, Player angler, WorldState world, LakeBed lake)
    {
        _rod = rod;
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
        _synth.Play(wood ? OneShot.StepWood : OneShot.StepGrass, Input.IsKeyPressed(Key.Shift) ? 1.2f : 0.8f);
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
        if (_rod == null)
            return;
        UpdateLevels((float)delta);
        if (_playback == null)
            return;
        int frames = _playback.GetFramesAvailable();
        if (frames <= 0)
            return;
        if (_buffer.Length < frames)
        {
            _buffer = new float[frames];
            _frames = new Vector2[frames];
        }
        _synth.Generate(_buffer.AsSpan(0, frames));
        for (int i = 0; i < frames; i++)
            _frames[i] = new Vector2(_buffer[i], _buffer[i]);
        _playback.PushBuffer(frames == _frames.Length ? _frames : _frames[..frames]);
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
