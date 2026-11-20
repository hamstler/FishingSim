using FishingSim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishingSim.Game
{
    /// <summary>
    /// Time of day and weather: moves the sun and the moon, colours the sky, the ambient light, the fog and the water's
    /// reflection (Core/DayClock: the day 06-18 lasts 40 real minutes, the night 20). The weather changes every few hours.
    /// </summary>
    public sealed class DayCycle : MonoBehaviour
    {
        public float Hour { get; set; } = 7f;
        public Weather Weather { get; private set; } = Weather.Clear;
        public float TimeScale { get; set; } = 1f;
        public Material? Water { get; set; }

        /// <summary>0 by day .. 1 at night (lamps, windows).</summary>
        public float Night { get; private set; }

        /// <summary>Wind strength 0..1 (calm sunny days to rainy gusts), drifting slowly.</summary>
        public float Wind { get; private set; } = 0.25f;

        /// <summary>Game hours that passed in the last frame.</summary>
        public float LastStepHours { get; private set; }

        private Light _sun = null!, _moon = null!;
        private Material _sky = null!;
        private float _cloud = 0.35f, _rain, _wetness;
        private float _nextWeather = 3f;
        private readonly SystemRng _rng = new();

        private void Awake()
        {
            _sky = Materials.Sky();
            RenderSettings.skybox = _sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            _sun = MakeLight("Sun", 1f, LightShadows.Soft);
            _moon = MakeLight("Moon", 0.15f, LightShadows.None);
            _moon.color = new Color(0.55f, 0.65f, 0.9f);
            RenderSettings.sun = _sun;
        }

        private Light MakeLight(string name, float intensity, LightShadows shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = intensity;
            l.shadows = shadows;
            l.shadowStrength = 0.85f;
            l.shadowBias = 0.04f;
            l.shadowNormalBias = 0.3f;
            return l;
        }

        public DayPeriod Period =>
            Hour < 4.5f || Hour >= 21f ? DayPeriod.Night : Hour < 7f || Hour >= 18.5f ? DayPeriod.Twilight : DayPeriod.Day;

        /// <summary>Daylight on the water, 0 at night .. 1 bright midday (Core's method rules).</summary>
        public float Light => FishingSim.Core.Methods.Method.LightLevel(Hour, Weather);

        public void SetWeather(Weather w)
        {
            Weather = w;
            _cloud = w switch { Weather.Clear => 0.3f, Weather.Cloudy => 0.75f, _ => 0.95f };
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float before = Hour;
            Hour = DayClock.Advance(Hour, dt, TimeScale);
            LastStepHours = Hour >= before ? Hour - before : Hour + 24f - before;
            if (Hour < before)
                _nextWeather -= 24f;
            if (Hour >= _nextWeather)
            {
                float r = _rng.NextFloat();
                SetWeather(r < 0.5f ? Weather.Clear : r < 0.82f ? Weather.Cloudy : Weather.Rain);
                _nextWeather = Hour + 2f + _rng.NextFloat() * 4f;
            }
            _rain = Mathf.MoveTowards(_rain, Weather == Weather.Rain ? 1f : 0f, dt * 0.05f);
            Waves.Push(dt);
            // the ground soaks in about two minutes of rain and dries in about ten (LakeBed.Wetness)
            _wetness = Mathf.MoveTowards(_wetness, Weather == Weather.Rain ? 1f : 0f, dt * (Weather == Weather.Rain ? 1f / 120f : 1f / 600f));
            Shader.SetGlobalFloat("_Wetness", _wetness);
            float windTarget = (Weather switch { Weather.Clear => 0.2f, Weather.Cloudy => 0.45f, _ => 0.7f })
                               + 0.15f * Mathf.Sin(Time.time * 0.05f);
            Wind = Mathf.MoveTowards(Wind, Mathf.Clamp01(windTarget), dt * 0.01f);
            // foliage sway (Godot's foliage_wind / foliage_time globals)
            Shader.SetGlobalFloat("_FoliageWind", Mathf.Clamp01(Wind + _rain * 0.25f));
            Shader.SetGlobalFloat("_FoliageTime", Time.time);
            Apply();
        }

        public void Apply()
        {
            // The sun climbs from the east (+x) at 6 h to the south at noon and sets in the west at 18 h.
            float dayAngle = (Hour - 6f) / 12f * Mathf.PI;
            var sunDir = new Vector3(Mathf.Cos(dayAngle), Mathf.Sin(dayAngle) * 0.85f, -0.45f).normalized;
            float elevation = sunDir.y;
            float day = Mathf.Clamp01(elevation * 4f + 0.25f);
            float golden = Mathf.Clamp01(1f - Mathf.Abs(elevation - 0.08f) * 6f);
            Night = 1f - Mathf.Clamp01(elevation * 6f + 0.4f);
            float cloud = Mathf.Lerp(_cloud, 1f, _rain * 0.5f);
            _sun.transform.rotation = Quaternion.LookRotation(-sunDir);
            var sunColour = Color.Lerp(new Color(1f, 0.55f, 0.28f), new Color(1f, 0.96f, 0.9f), Mathf.Clamp01(elevation * 3.5f));
            _sun.color = sunColour;
            _sun.intensity = Mathf.Clamp01(elevation * 5f) * 1.25f * (1f - cloud * 0.55f);
            _sun.enabled = elevation > -0.05f;
            var moonDir = new Vector3(-sunDir.x, Mathf.Max(-sunDir.y, 0.15f), 0.3f).normalized;
            _moon.transform.rotation = Quaternion.LookRotation(-moonDir);
            _moon.intensity = (1f - day) * 0.22f * (1f - cloud * 0.6f);
            _moon.enabled = day < 0.9f;

            var top = Color.Lerp(new Color(0.02f, 0.03f, 0.07f), new Color(0.22f, 0.42f, 0.78f), day);
            var horizon = Color.Lerp(new Color(0.05f, 0.07f, 0.12f), new Color(0.72f, 0.8f, 0.88f), day);
            horizon = Color.Lerp(horizon, new Color(0.98f, 0.62f, 0.38f), golden * 0.65f);
            var grey = new Color(0.55f, 0.58f, 0.62f) * Mathf.Lerp(0.12f, 1f, day);
            top = Color.Lerp(top, grey * 0.9f, cloud * 0.75f);
            horizon = Color.Lerp(horizon, grey, cloud * 0.7f);
            _sky.SetColor("_Top", top);
            _sky.SetColor("_Horizon", horizon);
            _sky.SetColor("_Ground", horizon * 0.45f);
            _sky.SetVector("_SunDir", sunDir);
            _sky.SetColor("_SunColor", sunColour * (1f - cloud * 0.7f));
            _sky.SetFloat("_Cloud", cloud);
            _sky.SetColor("_CloudColor", Color.Lerp(new Color(0.08f, 0.09f, 0.12f), Color.Lerp(Color.white, new Color(1f, 0.8f, 0.65f), golden), day) * (1f - _rain * 0.45f));
            _sky.SetFloat("_Night", 1f - day);

            RenderSettings.ambientSkyColor = Color.Lerp(top, horizon, 0.4f) * Mathf.Lerp(0.6f, 1f, day);
            RenderSettings.ambientEquatorColor = horizon * 0.7f;
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.17f, 0.13f) * Mathf.Lerp(0.15f, 1f, day);
            RenderSettings.fogColor = horizon;
            RenderSettings.fogDensity = Mathf.Lerp(0.0016f, 0.006f, _rain) + Mathf.Lerp(0.0015f, 0f, day) + golden * 0.0006f;

            Shader.SetGlobalColor("_SkyTop", top);
            Shader.SetGlobalColor("_SkyHorizon", horizon);
            Shader.SetGlobalVector("_SunDirection", sunDir);
            Shader.SetGlobalColor("_SunTint", sunColour * _sun.intensity);
            Shader.SetGlobalFloat("_Daylight", day);
            Waves.Wind = Mathf.Clamp01(Wind + _rain * 0.25f);
            if (Water != null)
            {
                Water.SetFloat("_Wind", Waves.Wind);
                Water.SetFloat("_Rain", _rain);
            }
        }
    }
}
