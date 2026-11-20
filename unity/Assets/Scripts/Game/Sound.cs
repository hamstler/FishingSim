using System;
using FishingSim.Core.Audio;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The game's sound: Core/Audio/Synth (every sound is synthesised: wind, water, birds, crickets, rain, frogs, reeds,
    /// the village, the reel and the drag, splashes, steps, coins...) played through the listener's audio filter on the
    /// audio thread; the levels follow the angler's place, the time of day and the weather (Audio/SoundEngine).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class Sound : MonoBehaviour
    {
        public Player Player { get; set; } = null!;
        public Fishing Rod { get; set; } = null!;
        public DayCycle Day { get; set; } = null!;
        public WorldView World { get; set; } = null!;

        private Synth _synth = null!;
        private readonly object _lock = new();
        private float[] _mono = new float[4096];
        private AmbientLevels _levels;
        private float _thrash;
        public static float Volume { get; set; } = 0.8f;

        private void Awake()
        {
            _synth = new Synth(AudioSettings.outputSampleRate);
            var src = GetComponent<AudioSource>();
            src.playOnAwake = true;
            src.spatialBlend = 0f;
            src.loop = true;
            src.Play();
        }

        public void Play(OneShot kind, float gain = 1f)
        {
            lock (_lock)
                _synth.Play(kind, gain);
        }

        private void Update()
        {
            if (World == null || World.Lake == null || Player == null)
                return;
            float dt = Time.deltaTime;
            var p = Player.transform.position;
            var lake = World.Lake;
            float shore = Mathf.Clamp01(1f - Mathf.Abs(lake.InsideMetres(p.x, p.z)) / 30f);
            float day = 1f - Day.Night;
            float rain = Day.Weather == Core.Weather.Rain ? 1f : 0f;
            ref var L = ref _levels;
            L.Wind = Mathf.MoveTowards(L.Wind, Day.Wind * 0.55f, dt * 0.2f);
            L.Rain = Mathf.MoveTowards(L.Rain, rain, dt * 0.3f);
            L.Water = 0.15f + 0.55f * shore;
            L.Birds = day * (1f - rain) * 0.8f;
            L.Crickets = Mathf.Clamp01(1f - day * 2f) * (1f - rain);
            L.Reel = Rod.Reeling ? 1f : 0f;
            L.Drag = Mathf.MoveTowards(L.Drag, Rod.Fight is { Slipping: true } ? 1f : 0f, dt * 8f);
            L.Fire = Mathf.MoveTowards(L.Fire, Campfire.NearestHeat(p), dt * 2f);
            float dusk = Mathf.Clamp01(1f - day * 1.6f);
            L.Frogs = dusk * shore * (1f - rain * 0.7f) * 0.9f;
            float dawn = Mathf.Exp(-Mathf.Pow((Day.Hour - 5.8f) / 1.1f, 2f));
            L.Birds = Mathf.Clamp01(L.Birds * (0.6f + 1.4f * dawn));
            L.Reeds = Mathf.MoveTowards(L.Reeds, Mathf.Clamp01(Day.Wind * (0.35f + 0.65f * shore)), dt * 0.3f);
            float hull = Boat.Aboard ? 1f : Lake.OnPier(p.x, p.z) ? 0.55f : 0f;
            L.Hull = Mathf.MoveTowards(L.Hull, hull * (0.4f + Day.Wind), dt * 2f);
            var plaza = lake.Location.PlazaCenter;
            float village = Mathf.Clamp01(1f - (new Vector2(p.x - plaza.X, p.z - plaza.Y).magnitude - 25f) / 65f);
            L.Village = village * Mathf.Clamp01(day * 1.5f) * (1f - rain * 0.6f);
            if (Rod.State == RodState.Fight && Rod.Fight is { } f && f.Distance < 5f)
            {
                _thrash -= dt;
                if (_thrash <= 0f)
                {
                    _thrash = 0.4f + UnityEngine.Random.value * 0.6f;
                    Play(OneShot.SmallSplash, 0.6f);
                }
            }
            lock (_lock)
            {
                _synth.Levels = _levels;
                _synth.Master = Volume;
            }
        }

        /// <summary>The audio thread asks for samples: the synth's mono signal goes to every channel.</summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            int frames = data.Length / channels;
            if (_mono.Length < frames)
                _mono = new float[frames];
            lock (_lock)
                _synth.Generate(new Span<float>(_mono, 0, frames));
            for (int i = 0, k = 0; i < frames; i++)
                for (int c = 0; c < channels; c++)
                    data[k++] = _mono[i];
        }
    }
}
