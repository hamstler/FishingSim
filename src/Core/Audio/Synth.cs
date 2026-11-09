using System;
using System.Collections.Generic;

namespace FishingSim.Core.Audio;

/// <summary>What a one-shot sound is made of.</summary>
public enum OneShot { Splash, SmallSplash, StepWood, StepGrass, Strike, Catch, LineSnap }

/// <summary>Levels of the continuous sounds (0..1), set every frame by the game.</summary>
public struct AmbientLevels
{
    public float Wind;
    public float Water;
    public float Birds;
    public float Crickets;
    public float Rain;
    public float Reel;      // reel handle turning
    public float Drag;      // drag giving line (the "scream")
}

/// <summary>
/// Procedural sound for the whole game (no audio files): filtered noise for wind, water and rain, chirping birds by
/// day, crickets at night, reel clicks, the drag's buzz, and one-shots (splashes, footsteps, strike, catch, snap).
/// Pure DSP, mono float samples; <c>Audio/SoundEngine</c> streams it to Godot.
/// </summary>
public sealed class Synth
{
    private sealed class Voice
    {
        public OneShot Kind;
        public float T;
        public float Length;
        public float Gain;
        public float Pitch;
    }

    private readonly float _rate;
    private readonly Random _rnd;
    private readonly List<Voice> _voices = new();
    private float _windLp, _waterLp, _rainHp, _rainPrev, _dragLp, _dragPhase;
    private float _reelClock, _cricketPhase, _cricketEnv, _birdT = -1f, _birdLen, _birdF0, _birdF1, _birdPhase, _birdWait = 1f;
    private float _lapPhase;

    public AmbientLevels Levels;

    /// <summary>Overall volume.</summary>
    public float Master { get; set; } = 0.8f;

    public int ActiveVoices => _voices.Count;

    public Synth(float sampleRate = 22050f, int seed = 7)
    {
        _rate = sampleRate;
        _rnd = new Random(seed);
    }

    private float Noise() => (float)_rnd.NextDouble() * 2f - 1f;

    public void Play(OneShot kind, float gain = 1f)
    {
        float len = kind switch
        {
            OneShot.Splash => 0.7f,
            OneShot.SmallSplash => 0.35f,
            OneShot.StepWood => 0.12f,
            OneShot.StepGrass => 0.16f,
            OneShot.Strike => 0.25f,
            OneShot.Catch => 0.6f,
            _ => 0.3f,
        };
        _voices.Add(new Voice { Kind = kind, Length = len, Gain = gain, Pitch = 0.85f + 0.3f * (float)_rnd.NextDouble() });
        if (_voices.Count > 24)
            _voices.RemoveAt(0);
    }

    /// <summary>Fills <paramref name="buffer"/> with the next samples.</summary>
    public void Generate(Span<float> buffer)
    {
        float dt = 1f / _rate;
        var L = Levels;
        for (int i = 0; i < buffer.Length; i++)
        {
            float s = 0f;

            // Wind: low-passed noise whose cutoff breathes slowly.
            float n = Noise();
            _windLp += (n - _windLp) * (0.01f + 0.02f * L.Wind);
            s += _windLp * L.Wind * 1.6f;

            // Water lapping at the shore: low noise swelling every couple of seconds.
            _lapPhase += dt * 0.45f;
            float swell = 0.5f + 0.5f * MathF.Sin(_lapPhase * MathF.Tau);
            _waterLp += (Noise() - _waterLp) * 0.05f;
            s += _waterLp * L.Water * (0.25f + 0.6f * swell * swell) * 0.9f;

            // Rain: high-passed noise.
            float rn = Noise();
            _rainHp = 0.9f * (_rainHp + rn - _rainPrev);
            _rainPrev = rn;
            s += _rainHp * L.Rain * 0.25f;

            // Crickets: 4.5 kHz chirps in bursts.
            if (L.Crickets > 0.01f)
            {
                _cricketPhase += dt;
                float burst = MathF.Sin(_cricketPhase * MathF.Tau * 2.2f) > 0.3f ? 1f : 0f;
                _cricketEnv += (burst - _cricketEnv) * 0.01f;
                float pulse = MathF.Sin(_cricketPhase * MathF.Tau * 28f) > 0f ? 1f : 0f;
                s += MathF.Sin(_cricketPhase * MathF.Tau * 4500f) * pulse * _cricketEnv * L.Crickets * 0.05f;
            }

            // Birds: short frequency sweeps at random intervals.
            if (L.Birds > 0.01f)
            {
                if (_birdT < 0f)
                {
                    _birdWait -= dt;
                    if (_birdWait <= 0f)
                    {
                        _birdT = 0f;
                        _birdLen = 0.08f + 0.18f * (float)_rnd.NextDouble();
                        _birdF0 = 2200f + 2200f * (float)_rnd.NextDouble();
                        _birdF1 = _birdF0 * (0.6f + 0.9f * (float)_rnd.NextDouble());
                        _birdWait = 0.15f + 2.5f * (float)_rnd.NextDouble();
                    }
                }
                else
                {
                    float k = _birdT / _birdLen;
                    float f = _birdF0 + (_birdF1 - _birdF0) * k;
                    _birdPhase += f * dt;
                    s += MathF.Sin(_birdPhase * MathF.Tau) * MathF.Sin(MathF.PI * k) * L.Birds * 0.06f;
                    _birdT += dt;
                    if (_birdT >= _birdLen)
                        _birdT = -1f;
                }
            }

            // Reel: a ratchet click 18 times a second while the handle turns.
            if (L.Reel > 0.01f)
            {
                _reelClock += dt * 18f;
                float frac = _reelClock - MathF.Floor(_reelClock);
                if (frac < 0.06f)
                    s += Noise() * (1f - frac / 0.06f) * L.Reel * 0.25f;
            }

            // Drag: a rasping buzz while line is pulled off the spool.
            if (L.Drag > 0.01f)
            {
                _dragPhase += dt * (55f + 35f * L.Drag);
                float saw = (_dragPhase - MathF.Floor(_dragPhase)) * 2f - 1f;
                _dragLp += (Noise() * 0.6f + saw - _dragLp) * 0.3f;
                s += _dragLp * L.Drag * 0.22f;
            }

            // One-shots.
            for (int v = _voices.Count - 1; v >= 0; v--)
            {
                var voice = _voices[v];
                float k = voice.T / voice.Length;
                s += OneShotSample(voice, k) * voice.Gain;
                voice.T += dt;
                if (voice.T >= voice.Length)
                    _voices.RemoveAt(v);
            }

            buffer[i] = Math.Clamp(s * Master, -1f, 1f);
        }
    }

    private float OneShotSample(Voice v, float k)
    {
        float decay = MathF.Exp(-k * 5f);
        switch (v.Kind)
        {
            case OneShot.Splash:
            case OneShot.SmallSplash:
            {
                // A plop (falling sine) followed by a noisy spray.
                float plop = MathF.Sin(v.T * MathF.Tau * (380f * v.Pitch - 250f * k)) * MathF.Exp(-k * 14f);
                return (plop * 0.5f + Noise() * 0.35f * decay) * (v.Kind == OneShot.Splash ? 0.8f : 0.45f);
            }
            case OneShot.StepWood:
                return (MathF.Sin(v.T * MathF.Tau * 140f * v.Pitch) * 0.6f + Noise() * 0.2f) * MathF.Exp(-k * 9f) * 0.35f;
            case OneShot.StepGrass:
                return Noise() * MathF.Exp(-k * 6f) * MathF.Sin(MathF.PI * MathF.Min(k * 4f, 1f)) * 0.12f;
            case OneShot.Strike:
                return Noise() * MathF.Exp(-k * 7f) * 0.25f + MathF.Sin(v.T * MathF.Tau * 90f) * decay * 0.2f;
            case OneShot.Catch:
            {
                // Two bright plucked notes: a little fanfare.
                float f = k < 0.4f ? 660f : 990f;
                float local = k < 0.4f ? k / 0.4f : (k - 0.4f) / 0.6f;
                return MathF.Sin(v.T * MathF.Tau * f) * MathF.Exp(-local * 6f) * 0.18f;
            }
            default:
                return Noise() * MathF.Exp(-k * 12f) * 0.4f;
        }
    }
}
