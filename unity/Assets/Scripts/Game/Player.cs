using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// The angler in first person: walks on the ground's height (no physics colliders: the map's height function is the
    /// floor), wades only into the shallows, stays inside the map, stands on the pier; mouse look while the cursor is
    /// captured.
    /// </summary>
    public sealed class Player : MonoBehaviour
    {
        public const float EyeHeight = 1.65f;
        public Camera Camera { get; private set; } = null!;
        public WorldView World { get; set; } = null!;
        public bool Frozen { get; set; }
        public float Sensitivity { get; set; } = 2.2f;

        private float _yaw, _pitch, _bob, _ground;

        private void Awake()
        {
            var cam = new GameObject("Camera");
            cam.transform.SetParent(transform, false);
            cam.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
            Camera = cam.AddComponent<Camera>();
            Camera.nearClipPlane = 0.05f;
            Camera.farClipPlane = 1400f;
            Camera.fieldOfView = 70f;
            Camera.depthTextureMode = DepthTextureMode.Depth;
            Camera.clearFlags = CameraClearFlags.Skybox;
            cam.AddComponent<AudioListener>();
            cam.tag = "MainCamera";
        }

        public void Place(Vector3 at, float yawDegrees)
        {
            transform.position = at;
            _ground = at.y;
            _yaw = yawDegrees;
            _pitch = 5f;
            Apply();
        }

        public Vector3 Eye => Camera.transform.position;
        public Vector3 Forward => Camera.transform.forward;

        private float FloorAt(float x, float z)
        {
            float h = World.Lake.HeightAt(x, z);
            if (Lake.OnPier(x, z))
                h = Mathf.Max(h, WorldView.PierDeck);
            return Mathf.Max(h, -Lake.WadeDepth);
        }

        private void Update()
        {
            if (World == null || World.Lake == null)
                return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                _yaw += Input.GetAxisRaw("Mouse X") * Sensitivity;
                _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * Sensitivity, -80f, 80f);
            }
            var move = Vector3.zero;
            if (!Frozen)
            {
                if (Input.GetKey(KeyCode.W)) move.z += 1f;
                if (Input.GetKey(KeyCode.S)) move.z -= 1f;
                if (Input.GetKey(KeyCode.D)) move.x += 1f;
                if (Input.GetKey(KeyCode.A)) move.x -= 1f;
            }
            float speed = Input.GetKey(KeyCode.LeftShift) ? 4.6f : 2.3f;
            var flat = Quaternion.Euler(0f, _yaw, 0f) * move.normalized * speed * dt;
            var p = transform.position;
            // slide along the water's edge and the map's edge: keep whichever part of the step stays walkable
            if (move != Vector3.zero)
            {
                var lake = World.Lake;
                if (lake.IsWalkable(p.x + flat.x, p.z + flat.z))
                    p += flat;
                else if (lake.IsWalkable(p.x + flat.x, p.z))
                    p.x += flat.x;
                else if (lake.IsWalkable(p.x, p.z + flat.z))
                    p.z += flat.z;
                _bob += flat.magnitude * 2.2f;
            }
            float floor = FloorAt(p.x, p.z);
            _ground = Mathf.Lerp(_ground, floor, 1f - Mathf.Exp(-dt * 14f));
            p.y = _ground;
            transform.position = p;
            Apply();
        }

        private void Apply()
        {
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            Camera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            Camera.transform.localPosition = new Vector3(0f, EyeHeight + Mathf.Sin(_bob * 2f) * 0.025f, 0f);
        }
    }
}
