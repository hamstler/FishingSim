using System;
using FishingSim.Data;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>Renders the selected tackle independently of the player's camera.</summary>
    internal sealed class TacklePreview : IDisposable
    {
        private const int Layer = 30;
        private readonly GameObject _stage = new("Inventory preview");
        private readonly Camera _camera;
        private GameObject? _model;
        private RenderTexture? _target;
        private TackleItem? _item;
        private string _modelId = "";
        private Quaternion _turn;
        private Bounds _bounds;
        private float _zoom;
        private bool _dirty;
        private static readonly int ControlHint = "TacklePreview".GetHashCode();
        private static readonly string[] TintProperties = { "baseColorFactor", "_BaseColor", "_Color" };

        public TacklePreview()
        {
            _stage.transform.position = new Vector3(0f, -1000f, 0f);
            _camera = new GameObject("Camera").AddComponent<Camera>();
            _camera.transform.SetParent(_stage.transform, false);
            _camera.enabled = false;
            _camera.cullingMask = 1 << Layer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.09f, 0.15f, 0.16f);
            _camera.orthographic = true;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 40f;
            _camera.allowHDR = false;
            AddLight(new Vector3(28f, -35f, 0f), new Color(1f, .92f, .77f), 1.9f);
            AddLight(new Vector3(-32f, 148f, 0f), new Color(.55f, .8f, 1f), 1.5f);
        }

        private void AddLight(Vector3 angles, Color color, float intensity)
        {
            var light = new GameObject("Studio light").AddComponent<Light>();
            light.transform.SetParent(_stage.transform, false);
            light.transform.localEulerAngles = angles;
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.cullingMask = 1 << Layer;
        }

        public bool Loading => _modelId.Length != 0 && !ModelLibrary.Settled(_modelId);

        public bool Draw(Rect area, TackleItem item)
        {
            if (!ReferenceEquals(_item, item))
                Select(item);
            if (_model == null)
            {
                if (_modelId.Length == 0 || !ModelLibrary.Has(_modelId) || !PrepareModel(item))
                    return false;
            }

            Orbit(area);
            if (Event.current.type != EventType.Repaint)
                return true;
            int width = Mathf.Max(1, Mathf.RoundToInt(area.width * 1.5f));
            int height = Mathf.Max(1, Mathf.RoundToInt(area.height * 1.5f));
            if (_target == null || _target.width != width || _target.height != height)
            {
                ReleaseTarget();
                _target = new RenderTexture(width, height, 16) { antiAliasing = 2, name = "Tackle preview" };
                _camera.targetTexture = _target;
                _dirty = true;
            }
            if (_dirty)
            {
                Render(width / (float)height);
                _dirty = false;
            }
            GUI.DrawTexture(area, _target, ScaleMode.StretchToFill);
            return true;
        }

        private void Select(TackleItem item)
        {
            if (_model != null)
            {
                _model.SetActive(false);
                UnityEngine.Object.Destroy(_model);
            }
            _model = null;
            _item = item;
            _modelId = item switch
            {
                RodData rod => string.IsNullOrEmpty(rod.Model) ? "rods/float_ostoja" : rod.Model,
                ReelData reel => string.IsNullOrEmpty(reel.Model) ? "reels/small_hokuto" : reel.Model,
                _ => "",
            };
            if (_modelId.Length != 0)
                ModelLibrary.Start(_modelId);
            _turn = item is RodData ? Quaternion.Euler(0f, -15f, -62f) : Quaternion.Euler(18f, -35f, -14f);
            _zoom = 1f;
        }

        private bool PrepareModel(TackleItem item)
        {
            var model = ModelLibrary.Spawn(_modelId, _stage.transform);
            if (model == null)
                return false;
            _model = model;

            Color tint = Color.white;
            if (item is RodData rod)
            {
                float length = rod.Length > 0f ? rod.Length : 3.58f;
                float section = (length - Fishing.BlankStart + Fishing.ButtY) / 10f;
                for (int i = 0; i < 10; i++)
                    if (ModelLibrary.Find(model.transform, $"Seg{i}") is { } segment)
                    {
                        segment.SetParent(model.transform, false);
                        segment.localPosition = new Vector3(0f, Fishing.BlankStart + i * section, 0f);
                        segment.localRotation = Quaternion.identity;
                        segment.localScale = new Vector3(1f, section / Fishing.SegmentLength, 1f);
                    }
                tint = new Color(rod.ModelTint.R, rod.ModelTint.G, rod.ModelTint.B);
            }
            else if (item is ReelData reel)
            {
                if (ModelLibrary.Json(_modelId) is { } json && JsonUtility.FromJson<Fishing.ReelFrame>(json) is { } frames)
                    Fishing.AssembleReel(model.transform, frames);
                tint = new Color(reel.ModelTint.R, reel.ModelTint.G, reel.ModelTint.B);
            }

            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                UnityEngine.Object.Destroy(model);
                _model = null;
                _modelId = "";
                return false;
            }
            _bounds = renderers[0].bounds;
            var properties = new MaterialPropertyBlock();
            foreach (var renderer in renderers)
            {
                renderer.gameObject.layer = Layer;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _bounds.Encapsulate(renderer.bounds);
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null)
                        continue;
                    foreach (var property in TintProperties)
                        if (material.HasProperty(property))
                        {
                            properties.Clear();
                            renderer.GetPropertyBlock(properties, i);
                            properties.SetColor(property, material.GetColor(property) * tint);
                            renderer.SetPropertyBlock(properties, i);
                            break;
                        }
                }
            }
            _bounds.center -= _stage.transform.position;
            _dirty = true;
            return true;
        }

        private void Orbit(Rect area)
        {
            var input = Event.current;
            int control = GUIUtility.GetControlID(ControlHint, FocusType.Passive, area);
            switch (input.GetTypeForControl(control))
            {
                case EventType.MouseDown when input.button == 0 && area.Contains(input.mousePosition):
                    GUIUtility.hotControl = control;
                    input.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == control:
                    _turn = Quaternion.Euler(input.delta.y * .5f, -input.delta.x * .5f, 0f) * _turn;
                    _dirty = true;
                    input.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == control:
                    GUIUtility.hotControl = 0;
                    input.Use();
                    break;
                case EventType.ScrollWheel when area.Contains(input.mousePosition):
                    _zoom = Mathf.Clamp(_zoom * Mathf.Exp(-input.delta.y * .08f), .75f, 3f);
                    _dirty = true;
                    input.Use();
                    break;
            }
        }

        private void Render(float aspect)
        {
            var model = _model!.transform;
            model.localRotation = _turn;
            model.localPosition = -(_turn * _bounds.center);
            Vector3 extent = _bounds.extents;
            Vector3 x = _turn * new Vector3(extent.x, 0f, 0f);
            Vector3 y = _turn * new Vector3(0f, extent.y, 0f);
            Vector3 z = _turn * new Vector3(0f, 0f, extent.z);
            float horizontal = Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x);
            float vertical = Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y);
            _camera.aspect = aspect;
            _camera.orthographicSize = Mathf.Max(.025f, Mathf.Max(horizontal / aspect, vertical) * 1.18f / _zoom);
            _camera.transform.localPosition = new Vector3(0f, 0f, -Mathf.Max(2f, extent.magnitude * 3f));
            _camera.Render();
        }

        private void ReleaseTarget()
        {
            if (_target == null)
                return;
            _camera.targetTexture = null;
            _target.Release();
            UnityEngine.Object.Destroy(_target);
            _target = null;
        }

        public void Dispose()
        {
            ReleaseTarget();
            UnityEngine.Object.Destroy(_stage);
        }
    }
}
