using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The rowboat at the pier (World/Rowboat): a clinker double-ender moored on the pier's right; E boards it, W/S row
    /// ahead/astern, A/D turn (one oar pulls, the other backs), Shift pulls harder; it rides the waves, stops in the
    /// shallows, at the pier and at the map's edge; X drops or weighs the anchor (the wind drifts a free boat); E steps
    /// out onto the pier or a dry bank. With the echo sounder (accessory) the HUD shows the depth under the hull.
    /// </summary>
    public sealed class Boat : MonoBehaviour
    {
        public static bool Aboard { get; private set; }
        public static readonly Vector3 Mooring = new(2.75f, 0f, -1.5f);
        public const float SeatedEye = 0.85f;

        public WorldView World { get; set; } = null!;
        public DayCycle Day { get; set; } = null!;
        public bool Anchored { get; private set; }
        public float DepthUnder { get; private set; }
        public float Speed => _velocity.magnitude;

        private Vector3 _velocity;
        private float _yaw = 180f, _turn, _strokeL, _strokeR;
        private Vector3 _anchorAt;
        private Transform _oarL = null!, _oarR = null!, _hull = null!;

        private void Awake()
        {
            _hull = new GameObject("Hull").transform;
            _hull.SetParent(transform, false);
            var k = new MeshKit();
            BuildHull(k);
            _hull.gameObject.AddComponent<MeshFilter>().sharedMesh = k.ToMesh("Rowboat");
            // Everyday.Rowboat: painted white_planks_clean sides, tarred bottom, bare wood inside (colours from the mesh,
            // grain from the photographed planks); the vertex-coloured fallback without the set
            var mat = Photos.Weathered("white_planks_clean", Color.white, 0.75f, weathering: 0.8f, moss: 0.05f, twoSided: true, vertexColour: true);
            if (mat == null)
            {
                mat = Materials.Lit(0.25f, 0.35f);
                mat.SetFloat("_Cull", 0f);
            }
            _hull.gameObject.AddComponent<MeshRenderer>().sharedMaterial = mat;
            _oarL = Oar(-1f, mat);
            _oarR = Oar(1f, mat);
        }

        private Transform Oar(float side, Material mat)
        {
            var pivot = new GameObject(side < 0 ? "OarL" : "OarR").transform;
            pivot.SetParent(_hull, false);
            pivot.localPosition = new Vector3(side * 0.62f, 0.62f, 0.15f);
            var k = new MeshKit();
            var wood = Col.Lin(0.62f, 0.5f, 0.34f);
            k.Tube(new Vector3(-side * 0.7f, 0f, 0f), new Vector3(side * 1.6f, 0f, 0f), 0.022f, 0.02f, 6, wood, wood, true);
            k.Box(new Vector3(side * 1.85f, 0f, 0f), new Vector3(0.5f, 0.02f, 0.13f), Quaternion.identity, wood);
            var go = new GameObject("Shaft");
            go.transform.SetParent(pivot, false);
            go.AddComponent<MeshFilter>().sharedMesh = k.ToMesh("Oar");
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return pivot;
        }

        /// <summary>A pointed-both-ends hull: white sides, tarred bottom, bare wood inside, thwarts and floorboards.</summary>
        private static void BuildHull(MeshKit k)
        {
            const int stations = 18, ribs = 10;
            const float half = 2.15f;
            var white = Col.Lin(0.86f, 0.85f, 0.8f);
            var tar = Col.Lin(0.1f, 0.09f, 0.08f);
            var inside = Col.Lin(0.5f, 0.38f, 0.25f);
            var verts = new Vector3[stations + 1, ribs + 1];
            for (int i = 0; i <= stations; i++)
            {
                float z = Mathf.Lerp(-half, half, i / (float)stations);
                float t = z / half;
                float beam = 0.72f * Mathf.Pow(Mathf.Max(1f - t * t, 0f), 0.65f);
                float sheer = 0.62f + 0.18f * t * t;     // the gunwale rises to stem and sternpost
                float keel = 0.02f + 0.16f * t * t;
                for (int j = 0; j <= ribs; j++)
                {
                    float a = j / (float)ribs * Mathf.PI;      // 0 = port gunwale, pi = starboard
                    float x = -Mathf.Cos(a) * beam;
                    float y = keel + (sheer - keel) * (1f - Mathf.Pow(Mathf.Sin(a), 1.6f));
                    verts[i, j] = new Vector3(x, y, z);
                }
            }
            // smooth shells: shared vertices with normals across the neighbours (flat quads showed the inside as a
            // checkerboard of light and dark squares); the outside, then the inside 2 cm in along the normal, in bare wood
            var outerIdx = new int[stations + 1, ribs + 1];
            var innerIdx = new int[stations + 1, ribs + 1];
            for (int i = 0; i <= stations; i++)
                for (int j = 0; j <= ribs; j++)
                {
                    var du = verts[Mathf.Min(i + 1, stations), j] - verts[Mathf.Max(i - 1, 0), j];
                    var dv = verts[i, Mathf.Min(j + 1, ribs)] - verts[i, Mathf.Max(j - 1, 0)];
                    var n = Vector3.Cross(dv, du);
                    // at the pointed ends the stations meet in one point: lean on the hull's side there
                    if (n.sqrMagnitude < 1e-10f)
                        n = new Vector3(verts[i, j].x, -0.3f, 0f);
                    n.Normalize();
                    var p = verts[i, j];
                    outerIdx[i, j] = k.Add(p, n, p.y < 0.22f ? tar : white);
                    innerIdx[i, j] = k.Add(p - n * 0.02f, -n, inside);
                }
            for (int i = 0; i < stations; i++)
                for (int j = 0; j < ribs; j++)
                {
                    int a = outerIdx[i, j], b = outerIdx[i + 1, j], c = outerIdx[i + 1, j + 1], d = outerIdx[i, j + 1];
                    k.Tri(a, d, c);
                    k.Tri(a, c, b);
                    a = innerIdx[i, j]; b = innerIdx[i + 1, j]; c = innerIdx[i + 1, j + 1]; d = innerIdx[i, j + 1];
                    k.Tri(a, b, c);
                    k.Tri(a, c, d);
                }
            // thwarts and floorboards, fitted to the hull: a thwart reaches the planking (between the inner and outer
            // shells), each floorboard ends where the hull at the floor's height gets narrower than its outer edge (one
            // straight 0.6 x 3 m board stuck out through both sides towards the ends, where the hull narrows and the
            // keel rises)
            var thwart = Col.Lin(0.55f, 0.42f, 0.28f);
            foreach (float z in new[] { -0.9f, 0.15f, 1.1f })
            {
                float w = HalfWidthAt(z, 0.42f, half) - 0.006f;
                k.Box(new Vector3(0f, 0.42f, z), new Vector3(2f * w, 0.04f, 0.22f), Quaternion.identity, thwart);
            }
            const float floorY = 0.13f, plank = 0.1f, gap = 0.018f;
            for (int p = -2; p <= 2; p++)
            {
                float x = p * (plank + gap);
                float edge = Mathf.Abs(x) + plank * 0.5f + 0.03f;      // 2 cm inside the inner shell, a little more
                float zEnd = 0f;
                for (float z = 0f; z < half; z += 0.01f)
                {
                    if (HalfWidthAt(z, floorY - 0.015f, half) < edge)
                        break;
                    zEnd = z;
                }
                if (zEnd < 0.3f)
                    continue;
                k.Box(new Vector3(x, floorY, 0f), new Vector3(plank, 0.025f, 2f * zEnd), Quaternion.identity, thwart * (p % 2 == 0 ? 0.8f : 0.74f));
            }
        }

        /// <summary>The hull's half-width at (z, height y), from the same lines as BuildHull (0 below the keel).</summary>
        private static float HalfWidthAt(float z, float y, float half)
        {
            float t = Mathf.Clamp(z / half, -1f, 1f);
            float beam = 0.72f * Mathf.Pow(Mathf.Max(1f - t * t, 0f), 0.65f);
            float sheer = 0.62f + 0.18f * t * t;
            float keel = 0.02f + 0.16f * t * t;
            if (y <= keel)
                return 0f;
            float f = Mathf.Clamp01((y - keel) / (sheer - keel));
            float sin = Mathf.Pow(1f - f, 1f / 1.6f);
            return Mathf.Sqrt(Mathf.Max(0f, 1f - sin * sin)) * beam;
        }

        public void ResetToMooring()
        {
            Aboard = false;
            Anchored = false;
            _velocity = Vector3.zero;
            _yaw = 180f;
            transform.position = Mooring;
            // out until there is water under the bow
            for (int i = 0; i < 20 && World.Lake.DepthAt(Mooring.x + i * 0.5f, Mooring.z - 2f) < 0.55f; i++)
                transform.position = Mooring + Vector3.right * (i * 0.5f);
        }

        /// <summary>Where the angler's eyes are aboard (on the after thwart, facing the bow).</summary>
        public Vector3 Seat => transform.TransformPoint(new Vector3(0f, 0.42f + SeatedEye - Player.EyeHeight, -0.9f));
        public float Yaw => _yaw;

        public void Board() => Aboard = true;

        /// <summary>Steps out onto the pier or a dry bank near the boat; false (and the reason) when there is none.</summary>
        public bool StepOut(out Vector3 at, out string why)
        {
            var p = transform.position;
            at = Vector3.zero;
            why = "";
            for (float r = 1.6f; r <= 3.5f; r += 0.3f)
                for (int k = 0; k < 16; k++)
                {
                    float a = k / 16f * Mathf.PI * 2f;
                    float x = p.x + Mathf.Cos(a) * r, z = p.z + Mathf.Sin(a) * r;
                    if (Lake.OnPier(x, z))
                    {
                        at = new Vector3(x, WorldView.PierDeck, z);
                        Aboard = false;
                        Anchored = false;
                        return true;
                    }
                    float h = World.Lake.HeightAt(x, z);
                    if (h > 0.05f && World.Lake.IsWalkable(x, z))
                    {
                        at = new Vector3(x, h, z);
                        Aboard = false;
                        Anchored = false;
                        return true;
                    }
                }
            why = "Tu nie wysiądziesz: podpłyń do pomostu albo do brzegu.";
            return false;
        }

        public string ToggleAnchor()
        {
            if (!Aboard)
                return "";
            if (Anchored)
            {
                Anchored = false;
                return "Kotwica podniesiona.";
            }
            if (DepthUnder > 12f)
                return "Za głęboko na kotwicę (do 12 m).";
            Anchored = true;
            _anchorAt = transform.position;
            return "Kotwica rzucona.";
        }

        /// <summary>Rows with the keys while aboard (only with empty hands: the rod idle).</summary>
        public void Tick(float dt, bool canRow)
        {
            var lake = World.Lake;
            float w = canRow && Aboard && Input.GetKey(KeyCode.W) ? 1f : canRow && Aboard && Input.GetKey(KeyCode.S) ? -1f : 0f;
            float turn = canRow && Aboard && Input.GetKey(KeyCode.D) ? 1f : canRow && Aboard && Input.GetKey(KeyCode.A) ? -1f : 0f;
            float hard = Input.GetKey(KeyCode.LeftShift) ? 1.5f : 1f;
            var forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            var right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
            // each oar's stroke: the drive is the pushing half of a sine
            if (w != 0f || turn != 0f)
            {
                _strokeL += dt * 4.2f * hard;
                _strokeR += dt * 4.2f * hard;
            }
            float driveL = Mathf.Max(Mathf.Sin(_strokeL), 0f), driveR = Mathf.Max(Mathf.Sin(_strokeR), 0f);
            float oarL = Mathf.Clamp(w + turn, -1f, 1f), oarR = Mathf.Clamp(w - turn, -1f, 1f);
            var thrust = forward * (oarL * driveL + oarR * driveR) * 0.85f * hard;
            _turn += (oarL * driveL - oarR * driveR) * 2.6f * hard * dt;
            _turn -= _turn * Mathf.Min(1.8f * dt, 1f);
            _yaw += _turn * Mathf.Rad2Deg * dt * 0.6f;
            _velocity += thrust * dt;
            // drag: little ahead, much sideways
            float ahead = Vector3.Dot(_velocity, forward), side = Vector3.Dot(_velocity, right);
            ahead -= ahead * Mathf.Min(0.55f * dt, 1f);
            side -= side * Mathf.Min(2.6f * dt, 1f);
            _velocity = forward * ahead + right * side;
            if (Aboard && !Anchored)
                _velocity += new Vector3(0.6f, 0f, 0.3f).normalized * Day.Wind * 0.12f * dt;
            if (Anchored)
            {
                var pull = _anchorAt - transform.position;
                pull.y = 0f;
                _velocity += pull * (0.8f * dt) - _velocity * Mathf.Min(1.2f * dt, 1f);
            }
            if (!Aboard)
                _velocity = Vector3.zero;
            var next = transform.position + _velocity * dt;
            // shallows, the pier and the map's edge stop the boat
            var bow = next + forward * 2f;
            bool blocked = lake.DepthAt(bow.x, bow.z) < 0.35f || lake.DepthAt(next.x, next.z) < 0.35f
                           || Mathf.Abs(next.x) <= 1.75f && next.z > -4.4f && next.z < 6.4f
                           || lake.PlayDistance(next.x, next.z) > -1f;
            if (blocked)
                _velocity = -_velocity * 0.2f;
            else
                transform.position = next;
            // ride the waves: heave from the middle, pitch from bow and stern, roll from the beam
            var p = transform.position;
            float t = Time.time;
            float mid = Waves.HeightAt(p.x, p.z, t);
            float fore = Waves.HeightAt(p.x + forward.x * 1.8f, p.z + forward.z * 1.8f, t);
            float aft = Waves.HeightAt(p.x - forward.x * 1.8f, p.z - forward.z * 1.8f, t);
            float port = Waves.HeightAt(p.x - right.x * 0.7f, p.z - right.z * 0.7f, t);
            float stbd = Waves.HeightAt(p.x + right.x * 0.7f, p.z + right.z * 0.7f, t);
            float pitch = Mathf.Atan2(aft - fore, 3.6f) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(port - stbd, 1.4f) * Mathf.Rad2Deg;
            transform.SetPositionAndRotation(new Vector3(p.x, mid - 0.08f, p.z), Quaternion.Euler(pitch, _yaw, roll));
            DepthUnder = lake.DepthAt(p.x, p.z);
            // oars follow the strokes while rowing, else they rest along the hull
            float swingL = oarL != 0f ? Mathf.Cos(_strokeL) * 30f : 0f, swingR = oarR != 0f ? Mathf.Cos(_strokeR) * 30f : 0f;
            _oarL.localRotation = Quaternion.Euler(0f, swingL, oarL != 0f ? 12f - driveL * 14f : 6f);
            _oarR.localRotation = Quaternion.Euler(0f, -swingR, oarR != 0f ? -12f + driveR * 14f : -6f);
        }
    }
}
