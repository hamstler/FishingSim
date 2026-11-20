using System.Linq;
using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// An automated check run by CI on the Linux build (FISHSIM_SMOKE=1): waits for the map, looks at a frame (mean
    /// colour, share of magenta = a shader that failed), fishes a few times through the real code (cast, bite, strike,
    /// reel until the fight ends) and prints SMOKE lines, a small JPEG of the frame in base64, then quits (exit 0 on
    /// success, 1 on failure).
    /// </summary>
    public sealed class Smoke : MonoBehaviour
    {
        public Func<bool> Ready { get; set; } = () => false;
        public Action EnterPlay { get; set; } = () => { };
        public Fishing Rod { get; set; } = null!;
        public Player Player { get; set; } = null!;
        public DayCycle Day { get; set; } = null!;

        public static bool Enabled => Environment.GetEnvironmentVariable("FISHSIM_SMOKE") == "1";

        private IEnumerator Start()
        {
            float started = Time.realtimeSinceStartup;
            // software rendering on CI is slow: lowest quality and a short view keep the frames coming
            Settings.Quality = 0;
            Settings.View = 0;
            Settings.Apply(Player.Camera);
            Debug.Log($"SMOKE app {Application.companyName} / {Application.productName} {Application.version}, data {Application.persistentDataPath}, gpu {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})");
            bool ok = true;
            while (!Ready())
            {
                if (Time.realtimeSinceStartup - started > 240f)
                {
                    Debug.Log("SMOKE FAIL: the map did not load in 240 s");
                    Application.Quit(1);
                    yield break;
                }
                yield return null;
            }
            Debug.Log($"SMOKE map loaded in {Time.realtimeSinceStartup - started:0.0} s");
            EnterPlay();
            var views = new (float Hour, string Name, Vector3 At, float Yaw)[]
            {
                (12f, "pier", new Vector3(0f, WorldView.PierDeck, 4.5f), 180f),
                (12f, "village", new Vector3(0f, 0f, 9f), 0f),
                (7f, "dawn", new Vector3(0f, WorldView.PierDeck, 4.5f), 180f),
                (20.5f, "night", new Vector3(0f, 0f, 9f), 0f),
            };
            foreach (var (hour, name, at0, yaw) in views)
            {
                var at = at0;
                if (at.y == 0f)
                    at.y = Player.World.Lake.HeightAt(at.x, at.z);
                Player.Place(at, yaw);
                Day.Hour = hour;
                Day.Apply();
                for (int i = 0; i < 20; i++)
                    yield return null;
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                var px = shot.GetPixels32();
                long r = 0, g = 0, b = 0;
                int magenta = 0;
                foreach (var p in px)
                {
                    r += p.r; g += p.g; b += p.b;
                    if (p.r > 200 && p.g < 60 && p.b > 200)
                        magenta++;
                }
                float share = magenta / (float)px.Length;
                Debug.Log($"SMOKE frame {name} {hour:0.0} h {shot.width}x{shot.height}: mean {r / px.Length},{g / px.Length},{b / px.Length}, magenta {share:P1}");
                if (share > 0.01f)
                    ok = false;
                var small = Downscale(shot, 320, 180);
                Debug.Log($"SMOKE JPEG {name} {Convert.ToBase64String(small.EncodeToJPG(70))}");
                Destroy(shot);
                Destroy(small);
            }
            // rain over the lake
            Player.Place(new Vector3(0f, WorldView.PierDeck, 4.5f), 180f);
            Day.SetWeather(Core.Weather.Rain);
            Day.Hour = 15f;
            for (int i = 0; i < 30; i++)
                yield return null;
            Day.Apply();
            yield return new WaitForEndOfFrame();
            {
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                var small = Downscale(shot, 320, 180);
                Debug.Log($"SMOKE JPEG rain {Convert.ToBase64String(small.EncodeToJPG(70))}");
                Destroy(shot);
                Destroy(small);
            }
            Day.SetWeather(Core.Weather.Clear);
            // the water's inner values at the pier (thickness, refraction, Fresnel, scene depth)
            Player.Place(new Vector3(0f, WorldView.PierDeck, 4.5f), 180f);
            Day.Hour = 12f;
            Day.Apply();
            for (int k = 1; k <= 4; k++)
            {
                Shader.SetGlobalFloat("_WaterDebug", k);
                for (int i = 0; i < 5; i++)
                    yield return null;
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                var small = Downscale(shot, 320, 180);
                Debug.Log($"SMOKE JPEG water{k} {Convert.ToBase64String(small.EncodeToJPG(70))}");
                Destroy(shot);
                Destroy(small);
            }
            Shader.SetGlobalFloat("_WaterDebug", 0f);
            Debug.Log($"SMOKE people: {UnityEngine.Object.FindObjectsByType<Person>(FindObjectsSortMode.None).Length}, anims loaded {ModelLibrary.Has("characters/anims_male")}, arms {GameObject.Find("AnglerArms") != null}");
            Debug.Log($"SMOKE models: fish {ModelLibrary.Has("fish/okon")}, building {ModelLibrary.Has("buildings/dom_mur_pruski")}, rod {ModelLibrary.Has("rods/float_ostoja")}, reel {ModelLibrary.Has("reels/small_hokuto")}");
            // the village's heavy models slow software rendering down: hide it for the fishing part
            if (GameObject.Find("Village") is { } village)
                village.SetActive(false);
            if (GameObject.Find("People") is { } people)
                people.SetActive(false);
            Player.Place(new Vector3(0f, WorldView.PierDeck, 4.5f), 180f);
            Day.Hour = 12f;
            int landed = 0;
            for (int n = 0; n < 2; n++)
            {
                var target = Player.transform.position + Player.transform.forward * (12f + n * 4f);
                Rod.SmokeCast(target);
                Rod.SmokeBiteNow();
                float t = 0f;
                while (Rod.State != RodState.Bite && t < 60f)
                {
                    if (Rod.State == RodState.Waiting)
                        Rod.SmokeBiteNow();
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (Rod.State != RodState.Bite)
                {
                    Debug.Log($"SMOKE cast {n}: no bite ({Rod.State})");
                    Rod.Reset();
                    continue;
                }
                string species = Rod.Species?.SpeciesName ?? "?";
                float weight = Rod.Weight;
                Rod.SmokeStrike();
                Rod.SmokeReel = true;
                t = 0f;
                while (Rod.State == RodState.Fight && t < 90f)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
                Rod.SmokeReel = false;
                Debug.Log($"SMOKE cast {n}: {species} {weight:0.000} kg, fight {t:0.0} s -> {Rod.State}");
                if (Rod.State == RodState.Landed)
                {
                    landed++;
                    yield return new WaitForEndOfFrame();
                    Rod.Decide(false);
                }
                else
                    Rod.Reset();
                yield return null;
            }
            Debug.Log($"SMOKE landed {landed} of 2, fps {1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f):0}");
            Debug.Log(ok ? "SMOKE OK" : "SMOKE FAIL: magenta pixels (a shader failed)");
            Application.Quit(ok ? 0 : 1);
        }

        private static Texture2D Downscale(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(w, h, TextureFormat.RGB24, false);
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}
