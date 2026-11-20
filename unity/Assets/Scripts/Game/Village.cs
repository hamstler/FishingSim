using System.Collections.Generic;
using System.Linq;
using FishingSim.Data;
using UnityEngine;
using Mathf = UnityEngine.Mathf;

namespace FishingSim.Game
{
    /// <summary>
    /// The map's village from LocationData.Buildings: the user's Meshy buildings (World/Props/BuildingFactory's table:
    /// model, size, extra yaw), tents, stalls, woodpiles, carts and drying racks built from shapes, warm lamps at the
    /// doors by night, and the doors of the trades the angler can visit (E).
    /// </summary>
    public sealed class Village : MonoBehaviour
    {
        private readonly struct Look
        {
            public readonly string Id;
            public readonly float Size, Yaw;
            public readonly bool ByHeight;
            public readonly string? Sign;

            public Look(string id, float size, bool byHeight, float yaw, string? sign = null)
            {
                Id = id;
                Size = size;
                ByHeight = byHeight;
                Yaw = yaw;
                Sign = sign;
            }
        }

        private static readonly Dictionary<BuildingKind, Look> Meshy = new()
        {
            [BuildingKind.Tackle] = new("buildings/kram_wedkarski", 10f, false, 0f, "KRAM WĘDKARSKI"),
            [BuildingKind.General] = new("buildings/kram_wedkarski", 10f, false, 0f, "KRAM I PRZYNĘTY"),
            [BuildingKind.Bait] = new("buildings/kram_z_przynetami", 7f, false, 0f, "PRZYNĘTY"),
            [BuildingKind.FishBuyer] = new("buildings/skup_ryb", 8f, false, 0f, "SKUP RYB"),
            [BuildingKind.Market] = new("buildings/targ_rybny", 9f, false, 0f, "TARG RYBNY"),
            [BuildingKind.Office] = new("buildings/gildia_wedkarzy", 12f, false, 0f, "GILDIA WĘDKARZY"),
            [BuildingKind.BusStop] = new("buildings/postoj_wozow", 5f, false, 0f),
            [BuildingKind.Smokehouse] = new("buildings/wedzarnia", 4.2f, false, 0f),
            [BuildingKind.House] = new("buildings/dom_mur_pruski", 12f, false, 0f),
            [BuildingKind.Cabin] = new("buildings/chata_z_bali", 9f, false, 0f),
            [BuildingKind.RedHouse] = new("buildings/czerwony_dom_skandynawski", 11f, false, 0f),
            [BuildingKind.StoneHouse] = new("buildings/kamienny_dom_lupek", 10f, false, 0f),
            [BuildingKind.Barn] = new("buildings/stodola", 16f, false, -90f),
            [BuildingKind.Windmill] = new("buildings/wiatrak_kozlak", 14f, true, -90f),
            [BuildingKind.Boathouse] = new("buildings/szopa_na_lodzie", 10f, false, 0f),
            [BuildingKind.Well] = new("buildings/studnia_z_daszkiem", 3.2f, true, 0f),
        };

        /// <summary>A place the angler can use: its kind, its door (where E works) and its sign.</summary>
        public readonly struct Door
        {
            public readonly BuildingKind Kind;
            public readonly Vector3 At;
            public readonly string Name;

            public Door(BuildingKind kind, Vector3 at, string name)
            {
                Kind = kind;
                At = at;
                Name = name;
            }
        }

        public List<Door> Doors { get; } = new();
        public List<Light> Lamps { get; } = new();
        public List<(Vector3 At, float Radius)> Solids { get; } = new();

        private Material _lit = null!;

        /// <summary>The Meshy buildings this map's village needs (loaded before it is built).</summary>
        public static IEnumerable<string> ModelIds(Lake lake) =>
            lake.Specs.Where(s => Meshy.ContainsKey(s.Kind)).Select(s => Meshy[s.Kind].Id).Distinct();

        public void Build(Lake lake)
        {
            foreach (Transform c in transform)
                Destroy(c.gameObject);
            Doors.Clear();
            Lamps.Clear();
            Solids.Clear();
            _lit = Materials.Lit(0.15f, 0.3f);
            var plaza = lake.Location.PlazaCenter;
            foreach (var spec in lake.Specs)
            {
                var p = spec.Position;
                var at = new Vector3(p.X, lake.HeightAt(p.X, p.Y), p.Y);
                float yaw = spec.FacingYaw(plaza) * Mathf.Rad2Deg;
                var holder = new GameObject(spec.Kind.ToString()).transform;
                holder.SetParent(transform, false);
                holder.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
                if (Meshy.TryGetValue(spec.Kind, out var look) && ModelLibrary.Has(look.Id))
                    Model(holder, look, spec);
                else
                    Shape(holder, spec);
                if (spec.IsFunctional || spec.Kind == BuildingKind.Smokehouse)
                {
                    var d = spec.DoorPoint(plaza);
                    var door = new Vector3(d.X, lake.HeightAt(d.X, d.Y), d.Y);
                    Doors.Add(new Door(spec.Kind, door, look.Sign ?? Name(spec.Kind)));
                    Lamp(holder, door);
                }
            }
        }

        private static string Name(BuildingKind k) => k switch
        {
            BuildingKind.Tackle => "Kram wędkarski",
            BuildingKind.General => "Kram",
            BuildingKind.Bait => "Przynęty",
            BuildingKind.FishBuyer => "Skup ryb",
            BuildingKind.Market => "Targ rybny",
            BuildingKind.Office => "Gildia wędkarzy",
            BuildingKind.BusStop => "Postój wozów",
            BuildingKind.Smokehouse => "Wędzarnia",
            _ => k.ToString(),
        };

        private void Model(Transform holder, Look look, BuildingSpec spec)
        {
            var b = ModelLibrary.BoundsOf(look.Id);
            bool across = Mathf.Abs(Mathf.Sin(look.Yaw * Mathf.Deg2Rad)) > 0.7f;
            float w = across ? b.size.z : b.size.x, d = across ? b.size.x : b.size.z;
            float k = look.ByHeight ? look.Size / Mathf.Max(b.size.y, 1e-3f) : look.Size / Mathf.Max(Mathf.Max(w, d), 1e-3f);
            var model = ModelLibrary.Spawn(look.Id, holder);
            if (model == null)
                return;
            // glTF is mirrored on its way into Unity (x flipped), so the Godot table's extra yaw turns the other way
            model.transform.localRotation = Quaternion.Euler(0f, -look.Yaw, 0f);
            model.transform.localScale = Vector3.one * k;
            var centre = Quaternion.Euler(0f, -look.Yaw, 0f) * new Vector3(b.center.x, b.min.y, b.center.z) * k;
            model.transform.localPosition = new Vector3(-centre.x, -centre.y - 0.05f, -centre.z);
            Solids.Add((holder.position, Mathf.Min(w, d) * k * 0.43f));
        }

        private void Lamp(Transform holder, Vector3 door)
        {
            var go = new GameObject("DoorLamp");
            go.transform.SetParent(holder, true);
            go.transform.position = door + Vector3.up * 2.4f;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.72f, 0.42f);
            l.range = 9f;
            l.intensity = 0f;
            l.shadows = LightShadows.None;
            Lamps.Add(l);
        }

        /// <summary>Lamps glow from dusk to dawn.</summary>
        public void SetNight(float night)
        {
            foreach (var l in Lamps)
            {
                l.intensity = night * 1.6f;
                l.enabled = night > 0.02f;
            }
        }

        // ------------------------------------------------------------------ small things built from shapes
        private void Shape(Transform holder, BuildingSpec spec)
        {
            var k = new MeshKit();
            var wood = Col.Lin(0.42f, 0.32f, 0.22f);
            var dark = Col.Lin(0.25f, 0.19f, 0.13f);
            var canvas = Col.Lin(0.78f, 0.72f, 0.6f);
            var red = Col.Lin(0.62f, 0.2f, 0.16f);
            var rng = new System.Random((int)(spec.Position.X * 13 + spec.Position.Y * 7));
            switch (spec.Kind)
            {
                case BuildingKind.Tent:
                case BuildingKind.MarketTent:
                {
                    float s = spec.Kind == BuildingKind.Tent ? 1f : 1.4f;
                    var c = rng.NextDouble() < 0.5 ? canvas : red;
                    // a ridge tent: two sloping roof planes and gables
                    Vector3 P(float x, float y, float z) => new Vector3(x, y, z) * s;
                    k.Quad(P(-1.5f, 0, -2f), P(-1.5f, 0, 2f), P(0, 2f, 2f), P(0, 2f, -2f), c);
                    k.Quad(P(1.5f, 0, 2f), P(1.5f, 0, -2f), P(0, 2f, -2f), P(0, 2f, 2f), c * 0.9f);
                    int a = k.Add(P(-1.5f, 0, -2f), Vector3.back, c * 0.8f); k.Add(P(0, 2f, -2f), Vector3.back, c * 0.8f); k.Add(P(1.5f, 0, -2f), Vector3.back, c * 0.8f);
                    k.Tri(a, a + 1, a + 2);
                    k.Tube(P(0, 0, 2.05f), P(0, 2.1f, 2.05f), 0.05f, 0.04f, 6, dark, dark, true);
                    k.Tube(P(0, 0, -2.05f), P(0, 2.1f, -2.05f), 0.05f, 0.04f, 6, dark, dark, true);
                    break;
                }
                case BuildingKind.Stall:
                    k.Box(new Vector3(0, 0.45f, 0), new Vector3(2.4f, 0.9f, 1f), Quaternion.identity, wood);
                    foreach (float x in new[] { -1.1f, 1.1f })
                        foreach (float z in new[] { -0.45f, 0.45f })
                            k.Tube(new Vector3(x, 0, z), new Vector3(x, 2.2f, z), 0.04f, 0.04f, 6, dark, dark, true);
                    k.Quad(new Vector3(-1.4f, 2.1f, -0.8f), new Vector3(-1.4f, 2.3f, 0.8f), new Vector3(1.4f, 2.3f, 0.8f), new Vector3(1.4f, 2.1f, -0.8f), red);
                    for (int i = 0; i < 6; i++)
                        k.Blob(new Vector3(-0.9f + i * 0.36f, 0.98f, 0f), new Vector3(0.14f, 0.09f, 0.14f), i, 0.3f,
                            Col.Lin(0.55f + (float)rng.NextDouble() * 0.3f, 0.45f, 0.25f), Col.Lin(0.3f, 0.25f, 0.15f), true, 1);
                    break;
                case BuildingKind.Woodpile:
                    for (int row = 0; row < 4; row++)
                        for (int i = 0; i < 8 - row; i++)
                        {
                            float x = -1.2f + i * 0.3f + row * 0.15f, y = 0.14f + row * 0.26f;
                            k.Tube(new Vector3(x, y, -0.5f), new Vector3(x, y, 0.5f), 0.13f, 0.13f, 8, wood, wood, true);
                        }
                    break;
                case BuildingKind.Cart:
                    k.Box(new Vector3(0, 0.75f, 0), new Vector3(1.4f, 0.12f, 2.4f), Quaternion.identity, wood);
                    k.Box(new Vector3(0.68f, 0.98f, 0), new Vector3(0.06f, 0.4f, 2.4f), Quaternion.identity, wood);
                    k.Box(new Vector3(-0.68f, 0.98f, 0), new Vector3(0.06f, 0.4f, 2.4f), Quaternion.identity, wood);
                    foreach (float x in new[] { -0.8f, 0.8f })
                        k.Tube(new Vector3(x, 0.5f, -0.02f), new Vector3(x, 0.5f, 0.02f), 0.5f, 0.5f, 14, dark, dark, true);
                    k.Tube(new Vector3(0, 0.7f, 1.2f), new Vector3(0, 0.45f, 3f), 0.05f, 0.04f, 6, wood, wood, true);
                    break;
                case BuildingKind.DryingRack:
                    foreach (float x in new[] { -1.4f, 1.4f })
                        k.Tube(new Vector3(x, 0, 0), new Vector3(x, 1.9f, 0), 0.06f, 0.05f, 6, dark, dark, true);
                    k.Tube(new Vector3(-1.5f, 1.8f, 0), new Vector3(1.5f, 1.8f, 0), 0.04f, 0.04f, 6, wood, wood, true);
                    for (int i = 0; i < 5; i++)
                        k.Box(new Vector3(-1f + i * 0.5f, 1.45f, 0), new Vector3(0.05f, 0.6f, 0.9f), Quaternion.identity, Col.Lin(0.4f, 0.45f, 0.4f));
                    break;
                case BuildingKind.Bench:
                    k.Box(new Vector3(0, 0.45f, 0), new Vector3(1.6f, 0.06f, 0.4f), Quaternion.identity, wood);
                    foreach (float x in new[] { -0.7f, 0.7f })
                        k.Box(new Vector3(x, 0.22f, 0), new Vector3(0.08f, 0.45f, 0.35f), Quaternion.identity, dark);
                    break;
                case BuildingKind.Crates:
                    for (int i = 0; i < 3; i++)
                        k.Box(new Vector3(i * 0.7f - 0.7f, 0.3f + (i == 1 ? 0.6f : 0f), 0), new Vector3(0.6f, 0.6f, 0.6f), Quaternion.Euler(0, i * 17, 0), wood);
                    break;
                case BuildingKind.Haystack:
                    k.Blob(new Vector3(0, 1f, 0), new Vector3(1.6f, 1.3f, 1.6f), 5, 0.25f, Col.Lin(0.75f, 0.62f, 0.32f), Col.Lin(0.45f, 0.36f, 0.18f), true, 2);
                    break;
                case BuildingKind.Lamp:
                    k.Tube(Vector3.zero, new Vector3(0, 2.6f, 0), 0.06f, 0.05f, 6, dark, dark, true);
                    k.Box(new Vector3(0, 2.75f, 0), new Vector3(0.25f, 0.3f, 0.25f), Quaternion.identity, Col.Lin(0.9f, 0.75f, 0.4f));
                    Lamp(holder, holder.position + Vector3.up * 0.3f);
                    break;
                case BuildingKind.Signpost:
                    k.Tube(Vector3.zero, new Vector3(0, 2.2f, 0), 0.06f, 0.05f, 6, dark, dark, true);
                    k.Box(new Vector3(0.35f, 1.9f, 0), new Vector3(0.8f, 0.22f, 0.05f), Quaternion.identity, wood);
                    break;
                case BuildingKind.Fence:
                    for (int i = 0; i < 6; i++)
                        k.Tube(new Vector3(i * 1.2f - 3f, 0, 0), new Vector3(i * 1.2f - 3f, 1.1f, 0), 0.05f, 0.05f, 6, dark, dark, true);
                    k.Box(new Vector3(0, 0.95f, 0), new Vector3(7f, 0.08f, 0.04f), Quaternion.identity, wood);
                    k.Box(new Vector3(0, 0.5f, 0), new Vector3(7f, 0.08f, 0.04f), Quaternion.identity, wood);
                    break;
                case BuildingKind.Boat:
                    k.Blob(new Vector3(0, 0.25f, 0), new Vector3(0.7f, 0.3f, 2.1f), 2, 0f, Col.Lin(0.85f, 0.85f, 0.82f), Col.Lin(0.2f, 0.18f, 0.15f), true, 2);
                    break;
                default:
                    return;   // villagers are placed by the people (Npcs)
            }
            var go = new GameObject("Shape");
            go.transform.SetParent(holder, false);
            go.AddComponent<MeshFilter>().sharedMesh = k.ToMesh(spec.Kind.ToString());
            // the props' wood in the photographed weathered planks (their colours from the mesh, the grain from the photo)
            var mr = go.AddComponent<MeshRenderer>();
            var weathered = Photos.Weathered("weathered_brown_planks", Color.white, 0.88f, moss: 0.25f, twoSided: true, vertexColour: true);
            mr.sharedMaterial = weathered ?? _lit;
            if (weathered != null)
            {
                var block = new MaterialPropertyBlock();
                block.SetFloat("_BaseY", holder.position.y);
                mr.SetPropertyBlock(block);
            }
            if (spec.PadRadius > 2f)
                Solids.Add((holder.position, spec.PadRadius * 0.4f));
        }
    }
}
