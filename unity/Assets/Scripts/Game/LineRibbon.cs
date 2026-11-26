using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The fishing line drawn as a ribbon of shaders/FishingLine (Godot: FishingRod.UpdateLine's DrawStrip): each point
    /// twice, the line's tangent in the normal, the side in the UV; the shader turns it to the camera and widens it to the
    /// line's diameter or about a pixel, whichever is wider. Buffers are made once (no allocation per frame).
    /// </summary>
    public sealed class LineRibbon : MonoBehaviour
    {
        private Mesh _mesh = null!;
        private Material _material = null!;
        private Vector3[] _verts = null!, _normals = null!;
        private Vector2[] _uv = null!;
        private Color[] _colors = null!;
        private int _capacity, _count;
        private Color _color = Color.white;
        private static readonly int DiameterId = Shader.PropertyToID("_Diameter");
        private static readonly int BandsId = Shader.PropertyToID("_Bands");
        private static readonly int BandColorId = Shader.PropertyToID("_BandColor");

        public static LineRibbon Create(string name, int capacity)
        {
            var go = new GameObject(name);
            var r = go.AddComponent<LineRibbon>();
            r.Init(capacity);
            return r;
        }

        private void Init(int capacity)
        {
            _capacity = capacity;
            _verts = new Vector3[capacity * 2];
            _normals = new Vector3[capacity * 2];
            _uv = new Vector2[capacity * 2];
            _colors = new Color[capacity * 2];
            var tris = new int[(capacity - 1) * 6];
            for (int i = 0; i < capacity - 1; i++)
            {
                int a = i * 2;
                tris[i * 6] = a; tris[i * 6 + 1] = a + 1; tris[i * 6 + 2] = a + 2;
                tris[i * 6 + 3] = a + 1; tris[i * 6 + 4] = a + 3; tris[i * 6 + 5] = a + 2;
            }
            for (int i = 0; i < capacity; i++)
            {
                _uv[i * 2] = new Vector2(-1f, 0f);
                _uv[i * 2 + 1] = new Vector2(1f, 0f);
            }
            _mesh = new Mesh { name = gameObject.name };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts;
            _mesh.normals = _normals;
            _mesh.uv = _uv;
            _mesh.colors = _colors;
            _mesh.triangles = tris;
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            _material = new Material(Materials.Find("FS/FishingLine")) { name = "FishingLine" };
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>The line's colour (the tension tint goes in here too).</summary>
        public Color Color { get => _color; set => _color = value; }

        /// <summary>The line's diameter in metres (LineData.Diameter is in millimetres).</summary>
        public float Diameter { set => _material.SetFloat(DiameterId, value); }

        /// <summary>Braid colour bands: their length in metres (0 = one colour) and the other colour.</summary>
        public void Bands(float metres, Color other)
        {
            _material.SetFloat(BandsId, metres);
            _material.SetColor(BandColorId, other);
        }

        /// <summary>The line through <paramref name="count"/> world points.</summary>
        public void SetPositions(Vector3[] pts, int count)
        {
            count = Mathf.Clamp(count, 2, _capacity);
            float along = 0f;
            for (int i = 0; i < _capacity; i++)
            {
                int k = Mathf.Min(i, count - 1);
                var p = pts[k];
                var t = k < count - 1 ? pts[k + 1] - p : p - pts[k - 1];
                if (k > 0 && k < count - 1)
                    t = pts[k + 1] - pts[k - 1];
                t = t.sqrMagnitude > 1e-10f ? t.normalized : Vector3.up;
                if (i > 0 && i < count)
                    along += Vector3.Distance(pts[k], pts[k - 1]);
                _verts[i * 2] = _verts[i * 2 + 1] = p;
                _normals[i * 2] = _normals[i * 2 + 1] = t;
                _uv[i * 2].y = _uv[i * 2 + 1].y = along;
                _colors[i * 2] = _colors[i * 2 + 1] = _color;
            }
            _count = count;
            _mesh.vertices = _verts;
            _mesh.normals = _normals;
            _mesh.uv = _uv;
            _mesh.colors = _colors;
            // world-space points: the object stays at the origin; the bounds cover the whole line
            _mesh.RecalculateBounds();
        }

        public int Count => _count;
    }
}
