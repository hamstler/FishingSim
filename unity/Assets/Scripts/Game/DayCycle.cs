using FishingSim.Core;
using UnityEngine;
using System.Collections.Generic;
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

        /// <summary>The moon's phase (0 new .. 0.5 full .. 1), set from the profile's clock.</summary>
        public float MoonPhase { get; set; } = 0.35f;

        /// <summary>Moonlight on the water: the moon's illumination at night through the clouds (WorldState.MoonLight).</summary>
        public float MoonLight => FishingSim.Core.Methods.Moon.Illumination(MoonPhase) * Night
            * (Weather == Weather.Rain ? 0.15f : Weather == Weather.Cloudy ? 0.45f : 1f);

        /// <summary>Game hours that passed in the last frame.</summary>
        public float LastStepHours { get; private set; }

        private Light _sun = null!, _moon = null!;
        private Material _sky = null!;
        private float _cloud = 0.35f, _rain, _wetness;
        private float _nextWeather = 3f;
        private bool _started => Time.timeSinceLevelLoad > 1f;
        private readonly SystemRng _rng = new();

        private void Awake()
        {
            _sky = Materials.Sky();
            RenderSettings.skybox = _sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
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
            if (!_started)
                _cloud = w switch { Weather.Clear => 0.05f, Weather.Cloudy => 0.65f, _ => 1f };
        }

        /// <summary>The weather at once, without easing (clouds, rain, wetness and wind at their targets): screenshots.</summary>
        public void SetWeatherNow(Weather w)
        {
            Weather = w;
            _cloud = w switch { Weather.Clear => 0.05f, Weather.Cloudy => 0.65f, _ => 1f };
            _rain = w == Weather.Rain ? 1f : 0f;
            _wetness = _rain;
            Wind = w switch { Weather.Clear => 0.2f, Weather.Cloudy => 0.45f, _ => 0.7f };
            Shader.SetGlobalFloat("_Wetness", _wetness);
            if (_rainRenderer != null)
                _rainRenderer.enabled = _rain > 0.02f;
            _applied = (float.NaN, 0f, 0f);
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
            // WorldState's targets, eased like SkyController
            float cloudTarget = Weather switch { Weather.Clear => 0.05f, Weather.Cloudy => 0.65f, _ => 1f };
            _cloud = Mathf.MoveTowards(_cloud, cloudTarget, 0.25f * dt);
            _rain = Mathf.MoveTowards(_rain, Weather == Weather.Rain ? 1f : 0f, 0.3f * dt);
            float windTarget = (Weather switch { Weather.Clear => 0.2f, Weather.Cloudy => 0.45f, _ => 0.7f }) + 0.15f * Mathf.Sin(Time.time * 0.05f);
            Wind = Mathf.MoveTowards(Wind, Mathf.Clamp01(windTarget), dt * 0.2f);
            _cloudTime += dt * (1f + Wind * 4f);
            Shader.SetGlobalFloat("_CloudDrift", _cloudTime);
            Waves.Push(dt);
            // the ground soaks in about two minutes of rain and dries in about ten (LakeBed.Wetness)
            _wetness = Mathf.MoveTowards(_wetness, Weather == Weather.Rain ? 1f : 0f, dt * (Weather == Weather.Rain ? 1f / 120f : 1f / 600f));
            Shader.SetGlobalFloat("_Wetness", _wetness);
            Shader.SetGlobalFloat("_FoliageWind", Mathf.Clamp01(Wind + _rain * 0.25f));
            Shader.SetGlobalFloat("_FoliageTime", Time.time);
            Storm(dt);
            // the sun, sky and fog change in steps (2 game minutes or a noticeable change of the weather)
            var state = (Mathf.Floor(Hour * 30f) / 30f, Mathf.Round(_cloud * 50f) / 50f, Mathf.Round(_rain * 50f) / 50f);
            if (!state.Equals(_applied))
                Apply();
            _rainMat?.SetFloat("_Shown", _rain > 0.02f ? Mathf.Clamp(_rain, 0.05f, 1f) : 0f);
            if (_rainRenderer != null)
                _rainRenderer.enabled = _rain > 0.02f;
            if (_rainRenderer != null && Camera.main != null)
                _rainRenderer.transform.position = Camera.main.transform.position;
        }

        private (float, float, float) _applied = (float.NaN, 0f, 0f);
        private float _cloudTime, _flash, _nextFlash = 20f;
        private Light? _lightning;
        private Material? _rainMat, _mistMat;
        private MeshRenderer? _rainRenderer, _mistRenderer;

        // ---------------------------------------------------------------- SkyController's formulas
        private static readonly (float Hour, Color Top, Color Horizon)[] Keys =
        {
            (0f, new Color(0.02f, 0.03f, 0.08f), new Color(0.05f, 0.07f, 0.15f)),
            (5f, new Color(0.04f, 0.06f, 0.16f), new Color(0.10f, 0.12f, 0.25f)),
            (6f, new Color(0.20f, 0.30f, 0.55f), new Color(0.90f, 0.55f, 0.40f)),
            (7.5f, new Color(0.30f, 0.52f, 0.85f), new Color(0.85f, 0.80f, 0.70f)),
            (12f, new Color(0.25f, 0.50f, 0.90f), new Color(0.70f, 0.82f, 0.95f)),
            (17f, new Color(0.30f, 0.50f, 0.85f), new Color(0.85f, 0.80f, 0.70f)),
            (19f, new Color(0.25f, 0.30f, 0.55f), new Color(0.95f, 0.52f, 0.32f)),
            (20.5f, new Color(0.06f, 0.08f, 0.20f), new Color(0.35f, 0.20f, 0.25f)),
            (22f, new Color(0.02f, 0.03f, 0.08f), new Color(0.05f, 0.07f, 0.15f)),
            (24f, new Color(0.02f, 0.03f, 0.08f), new Color(0.05f, 0.07f, 0.15f)),
        };

        private static (Color Top, Color Horizon) SampleSky(float hour)
        {
            for (int i = 0; i < Keys.Length - 1; i++)
                if (hour >= Keys[i].Hour && hour <= Keys[i + 1].Hour)
                {
                    float t = Mathf.InverseLerp(Keys[i].Hour, Keys[i + 1].Hour, hour);
                    return (Color.Lerp(Keys[i].Top, Keys[i + 1].Top, t), Color.Lerp(Keys[i].Horizon, Keys[i + 1].Horizon, t));
                }
            return (Keys[0].Top, Keys[0].Horizon);
        }

        public static Color SunColour(float elevation)
        {
            var horizon = new Color(1f, 0.42f, 0.18f);
            var golden = new Color(1f, 0.74f, 0.46f);
            var noon = new Color(1f, 0.97f, 0.92f);
            float e = Mathf.Clamp01(elevation);
            return e < 0.18f ? Color.Lerp(horizon, golden, e / 0.18f) : Color.Lerp(golden, noon, Mathf.Clamp01((e - 0.18f) / 0.35f));
        }

        public static float TwilightAt(float el) => Mathf.Clamp01((el + 0.18f) / 0.16f) * (1f - Mathf.Clamp01((el - 0.02f) / 0.1f));

        private static float Bump(float hour, float centre, float width)
        {
            float d = (hour - centre) / width;
            return Mathf.Exp(-d * d);
        }

        public static float MistAt(float hour, float wind, float rain) =>
            (Bump(hour, 6.5f, 1.2f) + 0.45f * Bump(hour, 19.3f, 0.9f)) * Mathf.Clamp01(1f - wind * 1.1f) * (1f - rain * 0.5f);

        /// <summary>Godot's light direction: rotation (-elevation, -30 deg, 0), shining along its -Z.</summary>
        private static Vector3 LightTravel(float elDeg, float yawDeg)
        {
            float e = elDeg * Mathf.Deg2Rad, y = yawDeg * Mathf.Deg2Rad;
            var f = new Vector3(0f, -Mathf.Sin(e), -Mathf.Cos(e));
            return new Vector3(f.x * Mathf.Cos(y) + f.z * Mathf.Sin(y), f.y, -f.x * Mathf.Sin(y) + f.z * Mathf.Cos(y));
        }

        public void Apply()
        {
            float hour = Mathf.Floor(Hour * 30f) / 30f;
            _applied = (hour, Mathf.Round(_cloud * 50f) / 50f, Mathf.Round(_rain * 50f) / 50f);
            float el = Mathf.Sin((hour - 6f) / 24f * Mathf.PI * 2f);
            float elDeg = Mathf.Asin(Mathf.Clamp(el, -1f, 1f)) * Mathf.Rad2Deg;
            float day = Mathf.Clamp01(el * 2.2f + 0.25f);
            Night = 1f - day;
            var (top, hor) = SampleSky(hour);
            var grey = new Color(0.55f, 0.58f, 0.62f) * Mathf.Lerp(0.12f, 1f, day);
            hor = Color.Lerp(hor, grey * 1.05f, _cloud * 0.85f);
            top = Color.Lerp(top, grey, _cloud * 0.8f);
            float night = Mathf.Clamp01(-el * 3f);
            float cover = Mathf.Clamp01(0.22f + _cloud * 0.78f);
            float twilight = TwilightAt(el) * (1f - _cloud * 0.7f);
            var sunTravel = LightTravel(Mathf.Max(elDeg, -4f), -30f);
            var sunDir = -sunTravel;
            var flat = LightTravel(0f, -30f);
            var sunColour = SunColour(el);

            _sun.transform.rotation = Quaternion.LookRotation(sunTravel);
            _sun.color = sunColour;
            _sun.intensity = 1.9f * day * (1f - _cloud * 0.65f);
            _sun.enabled = el > -0.12f;
            _sun.shadows = _cloud < 0.75f && day > 0.02f ? LightShadows.Soft : LightShadows.None;
            _moon.transform.rotation = Quaternion.LookRotation(LightTravel(elDeg, 150f));
            _moon.color = new Color(0.58f, 0.68f, 1f);
            _moon.intensity = 0.25f * (0.2f + 0.8f * FishingSim.Core.Methods.Moon.Illumination(MoonPhase)) * Mathf.Clamp01(-el * 2.5f) * (1f - _cloud * 0.6f);
            _moon.enabled = _moon.intensity > 0.001f;

            _sky.SetFloat("_CloudCover", cover);
            _sky.SetFloat("_Night", night);
            _sky.SetFloat("_Stars", Mathf.Clamp01((-el - 0.08f) * 6f) * (1f - _cloud * 0.6f));
            _sky.SetFloat("_Twilight", twilight);
            _sky.SetVector("_SunFlat", new Vector4(-flat.x, 0f, -flat.z, 0f));
            _sky.SetFloat("_Turbidity", 4f + _cloud * 6f + _rain * 20f);
            _sky.SetVector("_SunDir", sunDir);
            _sky.SetColor("_SunColor", sunColour.linear);
            _sky.SetFloat("_SunEnergy", _sun.intensity);
            _sky.SetFloat("_SunOn", el > -0.12f ? 1f : 0f);
            if (_sky.GetTexture("_CloudNoise") == null)
                _sky.SetTexture("_CloudNoise", ProcTextures.CloudNoise);

            // ambient: a cool fixed colour mixed with the sky by day (AmbientLightSkyContribution)
            float golden = Mathf.Clamp01(1f - Mathf.Abs(el - 0.12f) / 0.16f) * (1f - _cloud * 0.8f);
            float energy = Mathf.Lerp(0.75f, 0.72f, day) * (1f - 0.18f * golden);
            var fixedAmb = Color.Lerp(new Color(0.36f, 0.45f, 0.66f), new Color(0.3f, 0.42f, 0.7f), golden);
            float skyShare = Mathf.Lerp(0.35f, 1f, day);
            var skyTop = Color.Lerp(top, grey, cover * 0.6f) * Mathf.Lerp(0.15f, 1f, day);
            var skyHor = hor * Mathf.Lerp(0.15f, 1f, day);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(fixedAmb, skyTop, skyShare) * energy;
            RenderSettings.ambientEquatorColor = Color.Lerp(fixedAmb, skyHor, skyShare) * energy * 0.85f;
            RenderSettings.ambientGroundColor = Color.Lerp(fixedAmb * 0.5f, new Color(0.22f, 0.2f, 0.15f), skyShare) * energy * 0.6f;
            // light shafts: weak by day, strong at the golden hour, more in the morning mist (ShaftStrength x golden)
            Shader.SetGlobalFloat("_ShaftStrength", el > -0.02f ? (Mathf.Lerp(0.05f, 0.25f, Mathf.Clamp01(el * 3f)) + 0.6f * golden * (0.4f + 0.6f * MistAt(hour, Wind, _rain))) * (1f - _cloud * 0.8f) : 0f);
            Shader.SetGlobalFloat("_SceneExposure", Mathf.Lerp(1.9f, 1.05f, day) + 0.12f * golden);

            // fog: haze lit by the sky; denser with cloud, rain and the morning mist
            float mist = MistAt(hour, Wind, _rain);
            float hazeLight = Mathf.Clamp(day + 0.3f * twilight, 0.06f, 1f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            // (Godot: density 0.0016 with aerial perspective 0.75, which blends the far land towards the sky's darker blue
            // above the horizon, not its pale band: the mountains kept their brown slopes. Faded to the pale horizon here
            // they went bleached beige, so the clear-day haze is thinner and a deeper blue-grey.)
            RenderSettings.fogDensity = Mathf.Lerp(0.0011f, 0.004f, _cloud) + _rain * 0.006f + 0.0006f * mist;
            var haze = Color.Lerp(Color.Lerp(hor, new Color(0.42f, 0.53f, 0.63f), 0.55f), sunColour * 0.85f, 0.2f * golden);
            RenderSettings.fogColor = Color.Lerp(haze * 0.82f, haze, _cloud) * hazeLight;

            // mist banks (dawn mist glows with the low sun; at dusk it goes blue-grey)
            if (_mistMat != null && _mistRenderer != null)
            {
                float shown = Mathf.Round(mist * 40f) / 40f;
                _mistRenderer.enabled = shown > 0.02f;
                _mistMat.SetFloat("_Density", shown * 0.9f);
                _mistMat.SetColor("_MistColor", Color.Lerp(SunColour(Mathf.Clamp01(el)), new Color(0.75f, 0.8f, 0.86f), 0.6f) * Mathf.Clamp(0.18f + el * 2.2f, 0.18f, 0.85f));
            }

            Shader.SetGlobalColor("_SkyTop", Color.Lerp(top, grey, cover * 0.6f) * Mathf.Lerp(0.15f, 1f, day));
            Shader.SetGlobalColor("_SkyHorizon", hor * hazeLight);
            Shader.SetGlobalVector("_SunDirection", sunDir);
            Shader.SetGlobalColor("_SunTint", sunColour * _sun.intensity);
            Shader.SetGlobalFloat("_Daylight", Mathf.Clamp01(day * (1f - _cloud * 0.5f)));
            Waves.Wind = Mathf.Clamp01(Wind + _rain * 0.25f);
            if (Water != null)
            {
                Water.SetFloat("_Wind", Waves.Wind);
                Water.SetFloat("_Rain", _rain);
            }
        }

        /// <summary>A storm (rain > 0.7, wind > 0.6): lightning every 15-60 s, two quick pulses of a cold light.</summary>
        private void Storm(float dt)
        {
            bool storm = _rain > 0.7f && Wind > 0.6f;
            if (storm)
            {
                _nextFlash -= dt;
                if (_nextFlash <= 0f)
                {
                    _flash = 0.45f;
                    _nextFlash = 15f + _rng.NextFloat() * 45f;
                }
            }
            if (_flash > 0f || (_lightning != null && _lightning.intensity > 0f))
            {
                if (_lightning == null)
                {
                    _lightning = MakeLight("Lightning", 0f, LightShadows.None);
                    _lightning.color = new Color(0.82f, 0.86f, 1f);
                    _lightning.transform.rotation = Quaternion.Euler(70f, 30f, 0f);
                }
                _flash = Mathf.Max(0f, _flash - dt);
                float t = 0.45f - _flash;
                float pulse = t < 0.08f ? 1f : t < 0.16f ? 0.15f : t < 0.26f ? 0.8f : Mathf.Max(0f, 1f - (t - 0.26f) * 5f) * 0.5f;
                _lightning.intensity = _flash > 0f ? 3.5f * pulse : 0f;
            }
        }

        /// <summary>Rain (7000 drops) and the mist banks over the map; made once.</summary>
        public void BuildEffects()
        {
            if (_rainRenderer == null)
            {
                const int drops = 7000;
                var v = new Vector3[drops * 4];
                var uv = new Vector2[drops * 4];
                var uv2 = new Vector4[drops * 4];
                var tri = new int[drops * 6];
                var rng = new System.Random(77);
                Vector2[] corners = { new(-0.5f, -0.5f), new(0.5f, -0.5f), new(0.5f, 0.5f), new(-0.5f, 0.5f) };
                for (int i = 0; i < drops; i++)
                {
                    var place = new Vector4((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
                    for (int c = 0; c < 4; c++)
                    {
                        uv[i * 4 + c] = corners[c];
                        uv2[i * 4 + c] = place;
                    }
                    tri[i * 6] = i * 4; tri[i * 6 + 1] = i * 4 + 1; tri[i * 6 + 2] = i * 4 + 2;
                    tri[i * 6 + 3] = i * 4; tri[i * 6 + 4] = i * 4 + 2; tri[i * 6 + 5] = i * 4 + 3;
                }
                var mesh = new Mesh { name = "Rain", indexFormat = IndexFormat.UInt32 };
                mesh.vertices = v;
                mesh.uv = uv;
                mesh.SetUVs(1, uv2);
                mesh.triangles = tri;
                mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 1e5f));
                var go = new GameObject("Rain");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                _rainRenderer = go.AddComponent<MeshRenderer>();
                _rainRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _rainRenderer.receiveShadows = false;
                _rainMat = new Material(Shader.Find("FS/Rain")) { name = "Rain" };
                _rainRenderer.sharedMaterial = _rainMat;
                _rainRenderer.enabled = false;
            }
            if (_mistRenderer == null)
            {
                float w = Lake.XMax - Lake.XMin + 120f, d = Lake.ZMax - Lake.ZMin + 120f;
                var centre = new Vector3((Lake.XMin + Lake.XMax) / 2f, 0f, (Lake.ZMin + Lake.ZMax) / 2f);
                var v = new List<Vector3>();
                var uv2 = new List<Vector2>();
                var tri = new List<int>();
                for (int i = 0; i < 5; i++)
                {
                    float y = 0.1f + i * 0.32f;
                    int s = v.Count;
                    v.Add(centre + new Vector3(-w / 2, y, -d / 2)); v.Add(centre + new Vector3(w / 2, y, -d / 2));
                    v.Add(centre + new Vector3(w / 2, y, d / 2)); v.Add(centre + new Vector3(-w / 2, y, d / 2));
                    for (int c = 0; c < 4; c++)
                        uv2.Add(new Vector2(i / 4f, 0f));
                    tri.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
                }
                var mesh = new Mesh { name = "MistBanks" };
                mesh.SetVertices(v);
                mesh.SetUVs(1, uv2);
                mesh.SetTriangles(tri, 0);
                mesh.RecalculateBounds();
                var go = new GameObject("MistBanks");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                _mistRenderer = go.AddComponent<MeshRenderer>();
                _mistRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _mistRenderer.receiveShadows = false;
                _mistMat = new Material(Shader.Find("FS/Mist")) { name = "Mist" };
                _mistMat.SetTexture("_Noise", ProcTextures.NoiseField);
                _mistRenderer.sharedMaterial = _mistMat;
                _mistRenderer.enabled = false;
            }
            _applied = (float.NaN, 0f, 0f);
        }
    }
}
