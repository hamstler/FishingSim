using System;
using UnityEngine;
using Mathf = UnityEngine.Mathf;

namespace FishingSim.Game
{
    /// <summary>Graphics/Gulls: a few gulls circling over the lake by day, flapping in bursts and gliding; no physics.</summary>
    public sealed class Gulls : MonoBehaviour
    {
        private sealed class Bird
        {
            public Transform Body = null!, Left = null!, Right = null!;
            public float Radius, Height, Speed, Phase, Flap;
            public Vector3 Centre;
        }

        private readonly Bird[] _birds = new Bird[7];
        private float _time;
        public DayCycle? Day { get; set; }

        private void Start()
        {
            var rng = new System.Random(99);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var mat = Materials.Lit(0.2f, 0f);
            mat.SetFloat("_Cull", 0f);
            var white = Col.Lin(0.97f, 0.97f, 0.95f);
            var grey = Col.Lin(0.55f, 0.58f, 0.62f);
            Mesh Box(Vector3 size, Vector3 at, Color c)
            {
                var k = new MeshKit();
                k.Box(at, size, Quaternion.identity, c);
                return k.ToMesh("Gull");
            }
            Transform Part(Transform parent, Mesh mesh)
            {
                var go = new GameObject("Part");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return go.transform;
            }
            var body = Box(new Vector3(0.12f, 0.1f, 0.45f), Vector3.zero, white);
            var wingL = new MeshKit();
            wingL.Box(new Vector3(-0.3f, 0f, 0f), new Vector3(0.55f, 0.02f, 0.2f), Quaternion.identity, white);
            wingL.Box(new Vector3(-0.62f, 0f, -0.02f), new Vector3(0.18f, 0.02f, 0.14f), Quaternion.identity, grey);
            var wingR = new MeshKit();
            wingR.Box(new Vector3(0.3f, 0f, 0f), new Vector3(0.55f, 0.02f, 0.2f), Quaternion.identity, white);
            wingR.Box(new Vector3(0.62f, 0f, -0.02f), new Vector3(0.18f, 0.02f, 0.14f), Quaternion.identity, grey);
            Mesh left = wingL.ToMesh("WingL"), right = wingR.ToMesh("WingR");
            for (int i = 0; i < _birds.Length; i++)
            {
                var go = new GameObject("Gull");
                go.transform.SetParent(transform, false);
                var b = new Bird
                {
                    Body = go.transform,
                    Radius = R(12f, 38f),
                    Height = R(14f, 26f),
                    Speed = R(0.12f, 0.22f) * (rng.NextDouble() < 0.5 ? 1f : -1f),
                    Phase = R(0f, Mathf.PI * 2f),
                    Flap = R(0f, 10f),
                    Centre = new Vector3(R(-25f, 25f), 0f, R(-70f, -25f)),
                };
                Part(b.Body, body);
                b.Left = Part(b.Body, left);
                b.Right = Part(b.Body, right);
                _birds[i] = b;
            }
        }

        private void Update()
        {
            if (_birds[0] == null)
                return;
            _time += Time.deltaTime;
            float day = Day != null ? 1f - Day.Night : 1f;
            bool show = day > 0.15f;
            foreach (var b in _birds)
            {
                if (b.Body.gameObject.activeSelf != show)
                    b.Body.gameObject.SetActive(show);
                if (!show)
                    continue;
                float a = b.Phase + _time * b.Speed;
                b.Body.position = b.Centre + new Vector3(MathF.Cos(a) * b.Radius, b.Height + MathF.Sin(_time * 0.3f + b.Phase) * 1.5f, MathF.Sin(a) * b.Radius);
                float heading = a + (b.Speed > 0f ? MathF.PI : 0f);
                b.Body.rotation = Quaternion.Euler(0f, heading * Mathf.Rad2Deg, -MathF.Sign(b.Speed) * 0.25f * Mathf.Rad2Deg);
                float burst = MathF.Max(0f, MathF.Sin(_time * 0.7f + b.Flap));
                float flap = (MathF.Sin(_time * 9f + b.Flap) * 0.55f * burst + 0.15f) * Mathf.Rad2Deg;
                b.Left.localRotation = Quaternion.Euler(0f, 0f, flap);
                b.Right.localRotation = Quaternion.Euler(0f, 0f, -flap);
            }
        }
    }
}
