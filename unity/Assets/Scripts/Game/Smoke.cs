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

        /// <summary>FISHSIM_SMOKE_DIR=folder: every shot is also saved there as a full-size PNG (local test runs).</summary>
        private static void SaveFull(Texture2D shot, string name)
        {
            string? dir = Environment.GetEnvironmentVariable("FISHSIM_SMOKE_DIR");
            if (string.IsNullOrEmpty(dir))
                return;
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), shot.EncodeToPNG());
                Debug.Log($"SMOKE PNG {System.IO.Path.Combine(dir, name + ".png")}");
            }
            catch (Exception e)
            {
                Debug.Log($"SMOKE PNG failed: {e.Message}");
            }
        }

        /// <summary>A watchdog: an exception inside the coroutine stops it silently, and the game must still quit.</summary>
        private void Update()
        {
            if (Time.realtimeSinceStartup > 900f)
            {
                Debug.Log("SMOKE FAIL: still running after 900 s (the test coroutine stopped?)");
                Application.Quit(1);
            }
        }

        private IEnumerator Start()
        {
            float started = Time.realtimeSinceStartup;
            // software rendering on CI is slow: lowest quality and a short view keep the frames coming
            // (on a real graphics card: FISHSIM_SMOKE_QUALITY=0..4 and FISHSIM_SMOKE_VIEW=0..2)
            Settings.Quality = int.TryParse(Environment.GetEnvironmentVariable("FISHSIM_SMOKE_QUALITY"), out int q) ? Mathf.Clamp(q, 0, 4) : 0;
            Settings.View = int.TryParse(Environment.GetEnvironmentVariable("FISHSIM_SMOKE_VIEW"), out int v) ? Mathf.Clamp(v, 0, 2) : 0;
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
            if (HandsOnly)
            {
                Settings.Quality = 3;
                Settings.Apply(Player.Camera);
                Day.SetWeatherNow(Core.Weather.Clear);
                yield return HandShots(true);
                Debug.Log("SMOKE OK (hands only)");
                Application.Quit(0);
                yield break;
            }
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
                SaveFull(shot, name);
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
            yield return HandShots(false);
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
                SaveFull(shot, "rain");
                var small = Downscale(shot, 320, 180);
                Debug.Log($"SMOKE JPEG rain {Convert.ToBase64String(small.EncodeToJPG(70))}");
                Destroy(shot);
                Destroy(small);
            }
            // clear at once: easing out of the rain took seconds, so the menu views still had rain and overcast
            Day.SetWeatherNow(Core.Weather.Clear);
            // the Godot menu pictures' views (screenshot mode 0, FISHSIM_SCENIC=1, FISHSIM_CAM_LIFT=7, yaw 30, pitch -6,
            // High quality): the same place and hour, to compare side by side with assets/menu/<map>_10/_17.webp
            int quality = Settings.Quality;
            Settings.Quality = 3;
            Settings.Apply(Player.Camera);
            GameRoot.HideHud = true;
            // no rod, hands or arms in these pictures
            var hidden = new System.Collections.Generic.List<Renderer>();
            foreach (var r in Player.Camera.GetComponentsInChildren<Renderer>())
                if (r.enabled) { r.enabled = false; hidden.Add(r); }
            if (GameObject.Find("AnglerArms") is { } arms)
                foreach (var r in arms.GetComponentsInChildren<Renderer>())
                    if (r.enabled) { r.enabled = false; hidden.Add(r); }
            // nor the fishing line (it hangs in the world, not under the camera)
            foreach (var r in UnityEngine.Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None))
                if (r.enabled) { r.enabled = false; hidden.Add(r); }
            foreach (var (hour, name) in new[] { (10f, "menu10"), (17f, "menu17") })
            {
                Player.Place(new Vector3(0f, WorldView.PierDeck + 7f, -3.5f), 210f);
                Player.Look(210f, 6f);
                Day.Hour = hour;
                Day.Apply();
                for (int i = 0; i < 30; i++)
                    yield return null;
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                SaveFull(shot, name);
                Debug.Log($"SMOKE JPEG {name} {Convert.ToBase64String(shot.EncodeToJPG(85))}");
                Destroy(shot);
            }
            // FISHSIM_SMOKE_WATER=1: the water shader's debug views of the 10 h menu view (1 thickness/10 m,
            // 2 refracted picture, 3 Fresnel, 4 scene depth/50 m), saved as water1..water4
            if (Environment.GetEnvironmentVariable("FISHSIM_SMOKE_WATER") == "1")
            {
                Day.Hour = 10f;
                Day.Apply();
                for (int mode = 1; mode <= 4; mode++)
                {
                    Shader.SetGlobalFloat("_WaterDebug", mode);
                    for (int i = 0; i < 5; i++)
                        yield return null;
                    yield return new WaitForEndOfFrame();
                    var shot = ScreenCapture.CaptureScreenshotAsTexture();
                    SaveFull(shot, "water" + mode);
                    Destroy(shot);
                }
                Shader.SetGlobalFloat("_WaterDebug", 0f);
            }
            GameRoot.HideHud = false;
            foreach (var r in hidden)
                r.enabled = true;
            Settings.Quality = quality;
            Settings.Apply(Player.Camera);
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
                if (!Rod.SmokeTackleReady)
                {
                    Debug.Log($"SMOKE cast {n}: tackle lost in the previous fight, no more casts");
                    break;
                }
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

        /// <summary>FISHSIM_SMOKE_HANDS=1: only the hands on the rod (while their grips are being fitted).</summary>
        public static bool HandsOnly => Environment.GetEnvironmentVariable("FISHSIM_SMOKE_HANDS") == "1";

        /// <summary>
        /// The hands on the rod and the reel's crank, looking a little down (FirstPersonBody's grips), close-ups of the
        /// grips, and with <paramref name="reeling"/> a cast and a few frames of the left hand turning the crank.
        /// </summary>
        private IEnumerator HandShots(bool reeling)
        {
            // the rod's, the reel's and the angler's models first (the hands-only run skips the village, so the map is
            // ready before them and the first shots showed the built rod with no arms)
            float since = Time.realtimeSinceStartup;
            while ((!ModelLibrary.Has("characters/player_angler") || !ModelLibrary.Has("rods/float_ostoja") || !ModelLibrary.Has("reels/small_hokuto")
                    || GameObject.Find("AnglerArms") == null) && Time.realtimeSinceStartup - since < 60f)
                yield return null;
            Rod.ApplyTackle();
            Player.Place(new Vector3(0f, WorldView.PierDeck, 4.5f), 180f);
            Player.Look(180f, 22f);
            Day.Hour = 12f;
            Day.Apply();
            for (int i = 0; i < 20; i++)
                yield return null;
            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            SaveFull(shot, "hands");
            Destroy(shot);
            if (GameObject.Find("AnglerArms")?.GetComponent<AnglerArms>() is { } anglerArms)
            {
                Debug.Log($"SMOKE arms: {anglerArms.Report()}");
            }
            // close-ups of the grips from cameras beside the rod (FISHSIM_CLOSEUP in the Godot game): the right
            // hand on the handle from its right, both hands from ahead, the crank hand from the left
            var eye = Player.Camera.transform;
            var right = Rod.GripRight.position;
            var left = Rod.GripLeft.position;
            var mid = (right + left) * 0.5f;
            var closeViews = new (string Name, Vector3 At, Vector3 Look)[]
            {
                ("grip_right", right + eye.right * 0.3f + eye.up * 0.06f + eye.forward * 0.04f, right),
                ("grip_front", mid + eye.forward * 0.5f + eye.up * 0.12f, mid),
                ("grip_left", left - eye.right * 0.3f + eye.up * 0.08f + eye.forward * 0.06f, left),
                ("grip_below", mid - eye.up * 0.35f + eye.forward * 0.12f, mid),
            };
            var close = new GameObject("CloseUp").AddComponent<Camera>();
            close.depth = Player.Camera.depth + 1f;
            close.cullingMask = Player.Camera.cullingMask;
            close.nearClipPlane = 0.01f;
            close.fieldOfView = 40f;
            close.allowHDR = true;
            close.depthTextureMode |= DepthTextureMode.Depth;
            close.gameObject.AddComponent<PostFx>();
            foreach (var (shotName, from, target) in closeViews)
            {
                close.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from, eye.up));
                for (int i = 0; i < 3; i++)
                    yield return null;
                yield return new WaitForEndOfFrame();
                var c = ScreenCapture.CaptureScreenshotAsTexture();
                SaveFull(c, shotName);
                Destroy(c);
            }
            Destroy(close.gameObject);
            Player.Look(180f, 0f);
        
            if (!reeling)
                yield break;
            // the crank turning: cast, then reel and watch the left hand from beside the reel (reel_0..5)
            Player.Place(new Vector3(0f, WorldView.PierDeck, 4.5f), 180f);
            Rod.SmokeCast(Player.transform.position + Player.transform.forward * 14f);
            float waited = 0f;
            while (Rod.State != RodState.Waiting && waited < 12f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            Rod.SmokeReel = true;
            var cam = new GameObject("ReelCloseUp").AddComponent<Camera>();
            cam.depth = Player.Camera.depth + 1f;
            cam.cullingMask = Player.Camera.cullingMask;
            cam.nearClipPlane = 0.01f;
            cam.fieldOfView = 40f;
            cam.allowHDR = true;
            cam.depthTextureMode |= DepthTextureMode.Depth;
            cam.gameObject.AddComponent<PostFx>();
            for (int n = 0; n < 6; n++)
            {
                for (int i = 0; i < 4; i++)
                {
                    var view = Player.Camera.transform;
                    var knob = Rod.GripLeft.position;
                    var from = knob - view.right * 0.32f + view.up * 0.1f + view.forward * 0.1f;
                    cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(knob - from, view.up));
                    yield return null;
                }
                yield return new WaitForEndOfFrame();
                var c = ScreenCapture.CaptureScreenshotAsTexture();
                SaveFull(c, "reel_" + n);
                Destroy(c);
            }
            Rod.SmokeReel = false;
            Destroy(cam.gameObject);
            Debug.Log($"SMOKE reeling: state {Rod.State} after the cast ({waited:0.0} s)");
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
