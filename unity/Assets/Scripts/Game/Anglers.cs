using System.Collections.Generic;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// Scenery.BuildAnglers: up to three local anglers on the main shore, 60 m apart, away from the pier, each the
    /// Meshy fisherman standing on level bank facing the water with the spinning rod in his hands (tip rising 25 degrees)
    /// and a red float 9-13 m out.
    /// </summary>
    public sealed class Anglers : MonoBehaviour
    {
        private static readonly Vector3 FishermanHands = new(0.02f, 1.087f, 0.244f);
        private const float FishermanModelHeight = 1.904f, RodModelLength = 1.904f, RodGripX = 0.38f, RodBlankY = 0.13f, RodBottomY = -0.162f;

        public List<(Vector3 Seat, Vector3 Float)> Spots { get; } = new();
        public List<(Vector3 At, float Radius)> Solids { get; } = new();

        public static readonly string[] ModelIds = { "meshy_fisherman", "meshy_spinning_rod" };

        public void Build(Lake lake)
        {
            foreach (Transform c in transform)
                Destroy(c.gameObject);
            Spots.Clear();
            Solids.Clear();
            var loc = lake.Location;
            var rng = new System.Random(loc.DisplayName.Length * 7919 + 17);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float cx = 0f, cz = loc.OutlineCz, rx = loc.OutlineRx, rz = loc.OutlineRz;
            bool Level(float x, float z, float radius, float tol)
            {
                float h = lake.HeightAt(x, z);
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI * 2f / 8f;
                    foreach (float r in new[] { radius * 0.5f, radius })
                        if (Mathf.Abs(lake.HeightAt(x + Mathf.Cos(a) * r, z + Mathf.Sin(a) * r) - h) > tol)
                            return false;
                }
                return true;
            }
            for (int tries = 0; Spots.Count < 3 && tries < 600; tries++)
            {
                float angle = R(0f, Mathf.PI * 2f);
                float wx = cx + rx * 0.8f * Mathf.Cos(angle), wz = cz + rz * 0.8f * Mathf.Sin(angle);
                if (lake.DepthAt(wx, wz) < 1f)
                    continue;
                var dir = new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * rz).normalized;
                Vector2? edge = null;
                for (float d = 0f; d < 60f; d += 0.5f)
                {
                    float x = wx + dir.x * d, z = wz + dir.y * d;
                    if (lake.DepthAt(x, z) <= 0f)
                    {
                        edge = new Vector2(x, z);
                        break;
                    }
                }
                if (edge is not Vector2 waterline || (Mathf.Abs(waterline.x) < 40f && waterline.y > -40f))
                    continue;
                Vector2? found = null;
                for (float back = 1.5f; back <= 7f && found == null; back += 0.5f)
                {
                    var p = waterline + dir * back;
                    if (lake.IsWalkable(p.x, p.y) && !lake.Built(p.x, p.y, 3f) && lake.PathDistance(p.x, p.y) > 2.5f
                        && lake.HeightAt(p.x, p.y) <= 2.5f && Level(p.x, p.y, 2.5f, 0.3f))
                        found = p;
                }
                if (found is not Vector2 seat)
                    continue;
                bool crowded = false;
                foreach (var (s, _) in Spots)
                    if (Vector2.Distance(new Vector2(s.x, s.z), seat) < 60f)
                        crowded = true;
                if (crowded)
                    continue;
                var toWater = -dir;
                var floatAt = seat + toWater * (Vector2.Distance(seat, waterline) + R(9f, 13f));
                if (lake.DepthAt(floatAt.x, floatAt.y) < 0.8f)
                    continue;
                float h = lake.HeightAt(seat.x, seat.y);
                var root = new GameObject("Angler").transform;
                root.SetParent(transform, false);
                root.SetPositionAndRotation(new Vector3(seat.x, h, seat.y), Quaternion.Euler(0f, Mathf.Atan2(toWater.x, toWater.y) * Mathf.Rad2Deg, 0f));
                if (!Standing(root))
                {
                    Destroy(root.gameObject);
                    return;
                }
                var bob = new MeshKit();
                bob.Tube(new Vector3(0f, -0.05f, 0f), new Vector3(0f, 0.08f, 0f), 0.025f, 0.012f, 8, Col.Lin(0.9f, 0.18f, 0.12f), Col.Lin(0.95f, 0.85f, 0.2f), true);
                var bobGo = new GameObject("Float");
                bobGo.transform.SetParent(transform, false);
                bobGo.transform.position = new Vector3(floatAt.x, 0.03f, floatAt.y);
                bobGo.AddComponent<MeshFilter>().sharedMesh = bob.ToMesh("Float");
                bobGo.AddComponent<MeshRenderer>().sharedMaterial = Materials.Lit(0.4f, 0f);
                Solids.Add((root.position, 0.45f));
                Spots.Add((root.position, new Vector3(floatAt.x, 0f, floatAt.y)));
            }
        }

        /// <summary>Scenery.StandingAngler: the man 1.8 m tall, the rod 1.7 m held by its reel seat at his hands.</summary>
        private static bool Standing(Transform root)
        {
            const float height = 1.8f, rodLength = 1.7f;
            var man = ModelLibrary.Spawn("meshy_fisherman");
            var rod = ModelLibrary.Spawn("meshy_spinning_rod");
            if (man == null || rod == null)
            {
                if (man != null) Destroy(man);
                if (rod != null) Destroy(rod);
                return false;
            }
            // PropAsset.Create: scaled to its height, centred on x/z, feet on the ground
            var mb = ModelLibrary.BoundsOf("meshy_fisherman");
            float km = height / Mathf.Max(mb.size.y, 0.001f);
            var manHolder = new GameObject("Fisherman").transform;
            manHolder.SetParent(root, false);
            man.transform.SetParent(manHolder, false);
            man.transform.localScale = Vector3.one * km;
            man.transform.localPosition = new Vector3(-mb.center.x * km, -mb.min.y * km, -mb.center.z * km);

            float k = rodLength / RodModelLength;
            var rbnd = ModelLibrary.BoundsOf("meshy_spinning_rod");
            var rodHolder = new GameObject("Rod").transform;
            rodHolder.SetParent(root, false);
            rod.transform.SetParent(rodHolder, false);
            rod.transform.localScale = Vector3.one * k;
            rod.transform.localPosition = new Vector3(-rbnd.center.x * k, -rbnd.min.y * k, -rbnd.center.z * k);
            // the rod lies along x (glTF mirrored: tip at +x, butt at -x), the reel below; the tip to the water rising 25 deg
            var hands = new Vector3(-FishermanHands.x, FishermanHands.y, FishermanHands.z) * (height / FishermanModelHeight);
            var tipWay = new Vector3(0f, Mathf.Sin(25f * Mathf.Deg2Rad), Mathf.Cos(25f * Mathf.Deg2Rad));
            var x = tipWay;
            var y = (Vector3.up - tipWay * Vector3.Dot(Vector3.up, tipWay)).normalized;
            var rot = Quaternion.LookRotation(Vector3.Cross(x, y), y);
            var grip = new Vector3(-RodGripX * k, (RodBlankY - RodBottomY) * k, 0f);
            rodHolder.localRotation = rot;
            rodHolder.localPosition = hands - rot * grip;
            return true;
        }
    }
}
