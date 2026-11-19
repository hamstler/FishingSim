using System;
using System.Collections.Generic;

namespace FishingSim.Core.Audio;

/// <summary>What a one-shot sound is made of.</summary>
public enum OneShot { Splash, SmallSplash, StepWood, StepGrass, Strike, Catch, LineSnap, Bell, Coins, Click, Dig, Swish, Ignite, Bubble, Thunder, Plop, Hammer }

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
    public float Fire;      // a campfire nearby (crackle and hiss)
    public float Frogs;     // evening and night by the water: croaking frogs
    public float Reeds;     // wind through the reeds and the grass near the shore
    public float Hull;      // water slapping a hull or the pier's posts
    public float Village;   // distant voices and work in the village
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

    private float _fireLp, _crackle, _swishLp;
    private float _frogT = -1f, _frogWait = 1f, _frogF, _frogPhase, _frogLen;
    private float _reedLp, _reedBp, _reedSway;
    private float _hullT = -1f, _hullWait = 0.8f, _hullLp;
    private float _voiceT = -1f, _voiceWait = 2f, _voiceF, _voicePhase, _voiceLen, _voiceLp, _hammerWait = 3f;
    private float _thunderLp, _thunderLp2;

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
            OneShot.Bell => 0.9f,
            OneShot.Coins => 0.55f,
            OneShot.Click => 0.06f,
            OneShot.Dig => 0.45f,
            OneShot.Swish => 0.35f,
            OneShot.Ignite => 0.9f,
            OneShot.Bubble => 0.25f,
            OneShot.Thunder => 4.5f,
            OneShot.Plop => 0.3f,
            OneShot.Hammer => 0.2f,
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
            float swell = 0.5f + 0.5f * MathF.Sin(_lapPhase * (2f * MathF.PI));
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
                float burst = MathF.Sin(_cricketPhase * (2f * MathF.PI) * 2.2f) > 0.3f ? 1f : 0f;
                _cricketEnv += (burst - _cricketEnv) * 0.01f;
                float pulse = MathF.Sin(_cricketPhase * (2f * MathF.PI) * 28f) > 0f ? 1f : 0f;
                s += MathF.Sin(_cricketPhase * (2f * MathF.PI) * 4500f) * pulse * _cricketEnv * L.Crickets * 0.05f;
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
                    s += MathF.Sin(_birdPhase * (2f * MathF.PI)) * MathF.Sin(MathF.PI * k) * L.Birds * 0.06f;
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

            // Campfire: a soft hiss of burning wood with crackles and the odd pop.
            if (L.Fire > 0.01f)
            {
                _fireLp += (Noise() - _fireLp) * 0.08f;
                s += _fireLp * L.Fire * 0.12f;
                if (_rnd.NextDouble() < dt * 9f * L.Fire)
                    _crackle = 1f;
                if (_crackle > 0.001f)
                {
                    s += Noise() * _crackle * L.Fire * 0.35f;
                    _crackle *= 0.985f;
                }
            }

            // Frogs: rattling croaks (a pulsed low tone) now and then, in little runs.
            if (L.Frogs > 0.01f)
            {
                if (_frogT < 0f)
                {
                    _frogWait -= dt;
                    if (_frogWait <= 0f)
                    {
                        _frogT = 0f;
                        _frogLen = 0.25f + 0.35f * (float)_rnd.NextDouble();
                        _frogF = 260f + 260f * (float)_rnd.NextDouble();
                        _frogWait = 0.2f + 3.5f * (float)_rnd.NextDouble();
                    }
                }
                else
                {
                    float k = _frogT / _frogLen;
                    _frogPhase += _frogF * dt;
                    float pulse = MathF.Sin(_frogT * (2f * MathF.PI) * 32f) > 0f ? 1f : 0.2f;
                    s += MathF.Sin(_frogPhase * (2f * MathF.PI)) * MathF.Sin(_frogPhase * (2f * MathF.PI) * 0.5f) * pulse * MathF.Sin(MathF.PI * k) * L.Frogs * 0.07f;
                    _frogT += dt;
                    if (_frogT >= _frogLen)
                        _frogT = -1f;
                }
            }

            // Reeds and grass: a bright, swaying rustle (band-passed noise) that follows the wind.
            if (L.Reeds > 0.01f)
            {
                _reedSway += dt * 0.31f;
                float n2 = Noise();
                _reedLp += (n2 - _reedLp) * 0.35f;
                _reedBp += (_reedLp - _reedBp) * 0.08f;
                float sway = 0.35f + 0.65f * MathF.Max(MathF.Sin(_reedSway * (2f * MathF.PI)) * MathF.Sin(_reedSway * (2f * MathF.PI) * 0.37f + 1f), 0f);
                s += (_reedLp - _reedBp) * L.Reeds * sway * 0.18f;
            }

            // Water slapping a hull or a post: short hollow knocks at the swell's rhythm.
            if (L.Hull > 0.01f)
            {
                if (_hullT < 0f)
                {
                    _hullWait -= dt;
                    if (_hullWait <= 0f)
                    {
                        _hullT = 0f;
                        _hullWait = 0.6f + 1.6f * (float)_rnd.NextDouble();
                    }
                }
                else
                {
                    _hullLp += (Noise() - _hullLp) * 0.12f;
                    float knock = MathF.Sin(_hullT * (2f * MathF.PI) * 110f) * MathF.Exp(-_hullT * 30f);
                    s += (knock * 0.5f + _hullLp * MathF.Exp(-_hullT * 9f)) * L.Hull * 0.3f;
                    _hullT += dt;
                    if (_hullT > 0.4f)
                        _hullT = -1f;
                }
            }

            // The village: murmured voices (vowel-like formant hums) and a hammer now and then, far off.
            if (L.Village > 0.01f)
            {
                if (_voiceT < 0f)
                {
                    _voiceWait -= dt;
                    if (_voiceWait <= 0f)
                    {
                        _voiceT = 0f;
                        _voiceLen = 0.3f + 1.1f * (float)_rnd.NextDouble();
                        _voiceF = 110f + 120f * (float)_rnd.NextDouble();
                        _voiceWait = 0.4f + 2.5f * (float)_rnd.NextDouble();
                    }
                }
                else
                {
                    float k = _voiceT / _voiceLen;
                    _voicePhase += _voiceF * (1f + 0.08f * MathF.Sin(_voiceT * 9f)) * dt;
                    float saw = (_voicePhase - MathF.Floor(_voicePhase)) * 2f - 1f;
                    _voiceLp += (saw - _voiceLp) * 0.06f;
                    float syll = 0.5f + 0.5f * MathF.Sin(_voiceT * (2f * MathF.PI) * 4.5f);
                    s += _voiceLp * syll * MathF.Sin(MathF.PI * k) * L.Village * 0.06f;
                    _voiceT += dt;
                    if (_voiceT >= _voiceLen)
                        _voiceT = -1f;
                }
                _hammerWait -= dt;
                if (_hammerWait <= 0f)
                {
                    _hammerWait = 4f + 9f * (float)_rnd.NextDouble();
                    Play(OneShot.Hammer, 0.5f * L.Village);
                }
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
                float plop = MathF.Sin(v.T * (2f * MathF.PI) * (380f * v.Pitch - 250f * k)) * MathF.Exp(-k * 14f);
                return (plop * 0.5f + Noise() * 0.35f * decay) * (v.Kind == OneShot.Splash ? 0.8f : 0.45f);
            }
            case OneShot.StepWood:
                return (MathF.Sin(v.T * (2f * MathF.PI) * 140f * v.Pitch) * 0.6f + Noise() * 0.2f) * MathF.Exp(-k * 9f) * 0.35f;
            case OneShot.StepGrass:
                return Noise() * MathF.Exp(-k * 6f) * MathF.Sin(MathF.PI * MathF.Min(k * 4f, 1f)) * 0.12f;
            case OneShot.Strike:
                return Noise() * MathF.Exp(-k * 7f) * 0.25f + MathF.Sin(v.T * (2f * MathF.PI) * 90f) * decay * 0.2f;
            case OneShot.Dig:
            {
                // The blade cuts in (a dull thump) and grinds through soil and roots.
                float thump = MathF.Sin(v.T * (2f * MathF.PI) * (95f * v.Pitch - 40f * k)) * MathF.Exp(-k * 12f);
                float grit = Noise() * MathF.Exp(-k * 3.5f) * (0.6f + 0.4f * MathF.Sin(v.T * (2f * MathF.PI) * 37f));
                return thump * 0.45f + grit * 0.16f;
            }
            case OneShot.Swish:
            {
                // Air through the net: band-passed noise swelling and fading.
                _swishLp += (Noise() - _swishLp) * (0.15f + 0.4f * k);
                return _swishLp * MathF.Sin(MathF.PI * k) * 0.5f;
            }
            case OneShot.Ignite:
            {
                // Dry kindling catching: a soft whoomph and a burst of crackles.
                _swishLp += (Noise() - _swishLp) * 0.05f;
                float crack = _rnd.NextDouble() < 0.004 ? 1f : 0f;
                return _swishLp * MathF.Sin(MathF.PI * MathF.Min(k * 3f, 1f)) * MathF.Exp(-k * 2f) * 0.8f + crack * Noise() * 0.5f;
            }
            case OneShot.Bubble:
                return MathF.Sin(v.T * (2f * MathF.PI) * (300f * v.Pitch + 500f * k)) * MathF.Sin(MathF.PI * k) * 0.12f;
            case OneShot.Catch:
            {
                // Two bright plucked notes: a little fanfare.
                float f = k < 0.4f ? 660f : 990f;
                float local = k < 0.4f ? k / 0.4f : (k - 0.4f) / 0.6f;
                return MathF.Sin(v.T * (2f * MathF.PI) * f) * MathF.Exp(-local * 6f) * 0.18f;
            }
            case OneShot.Bell:
            {
                // A small brass bell: inharmonic partials, quick strike, long ring.
                float t = v.T;
                float ring = MathF.Sin(t * (2f * MathF.PI) * 2100f) + 0.6f * MathF.Sin(t * (2f * MathF.PI) * 2100f * 2.76f) + 0.35f * MathF.Sin(t * (2f * MathF.PI) * 2100f * 5.4f);
                return ring * MathF.Exp(-k * 4f) * 0.09f;
            }
            case OneShot.Coins:
            {
                // Coins tumbling onto a counter: a handful of short metallic clinks at uneven moments.
                float s = 0f;
                for (int i = 0; i < 6; i++)
                {
                    float at = 0.06f * i + 0.025f * MathF.Sin(i * 12.9898f) * MathF.Sin(i * 3.1f);
                    float lt = v.T - at;
                    if (lt < 0f)
                        continue;
                    float f = 3200f + 900f * MathF.Sin(i * 7.3f);
                    s += (MathF.Sin(lt * (2f * MathF.PI) * f) + 0.5f * MathF.Sin(lt * (2f * MathF.PI) * f * 1.58f)) * MathF.Exp(-lt * 38f) * (1f - 0.1f * i);
                }
                return s * 0.07f;
            }
            case OneShot.Thunder:
            {
                // A crack, then a long rumble rolling round the hills (low-passed noise twice, swelling and fading).
                _thunderLp += (Noise() - _thunderLp) * 0.02f;
                _thunderLp2 += (_thunderLp - _thunderLp2) * 0.05f;
                float crack = Noise() * MathF.Exp(-v.T * 18f) * 0.6f;
                float roll = 0.5f + 0.5f * MathF.Sin(v.T * 3.1f) * MathF.Sin(v.T * 1.3f + 0.5f);
                return (crack + _thunderLp2 * 9f * roll * MathF.Min(v.T * 4f, 1f) * MathF.Exp(-k * 2.2f)) * 0.9f;
            }
            case OneShot.Plop:
                // A fish rising: a small, clean plop.
                return MathF.Sin(v.T * (2f * MathF.PI) * (620f * v.Pitch - 380f * k)) * MathF.Exp(-k * 10f) * 0.3f;
            case OneShot.Hammer:
                return (MathF.Sin(v.T * (2f * MathF.PI) * 820f * v.Pitch) * 0.6f + Noise() * 0.4f) * MathF.Exp(-k * 14f) * 0.3f;
            case OneShot.Click:
                return (MathF.Sin(v.T * (2f * MathF.PI) * 1800f) * 0.5f + Noise() * 0.5f) * MathF.Exp(-k * 10f) * 0.12f;
            default:
                return Noise() * MathF.Exp(-k * 12f) * 0.4f;
        }
    }
}
