using System.Threading.Tasks;
using LivingDiorama.Data;
using LivingDiorama.Presentation;
using UnityEngine;

namespace LivingDiorama.UI
{
    /// <summary>
    /// A little lit turntable that renders one creature into a texture, so the collection
    /// can show the actual model rather than a thumbnail of it.
    ///
    /// Collecting things you can only see as a postage stamp is a poor reward. This is
    /// the same creature the diorama runs, on a plinth, under studio lights, and the
    /// player can turn it over in their hand -- which is the entire point of having built
    /// them in 3D.
    ///
    /// Lives far below the world on its own culling layer so the diorama's camera never
    /// sees it and it never sees the diorama.
    /// </summary>
    public sealed class CreatureStage : MonoBehaviour
    {
        const float StageDepth = -400f;

        // Matches the viewport's shape. A square texture stretched into a wide panel put
        // the creature in the wrong place and the wrong proportions.
        const int Width = 704;
        const int Height = 400;

        Camera _camera;
        Transform _pivot;
        GameObject _current;
        CreatureFactory _factory;
        string _currentId;

        /// <summary>Framing distance for the current model. Held separately because
        /// deriving it from the camera's own position each frame fed back on itself and
        /// crept the camera slowly into the creature's face.</summary>
        float _baseDistance = 2f;

        public RenderTexture Texture { get; private set; }

        /// <summary>Turntable angle in degrees, driven by the player's finger.</summary>
        public float Yaw { get; set; } = 150f;

        public float Pitch { get; set; } = 8f;

        /// <summary>1 is the framing the stage picks for the model; higher is closer.</summary>
        public float Zoom { get; set; } = 1f;

        public static CreatureStage Create(CreatureFactory factory)
        {
            var go = new GameObject("CreatureStage");
            go.transform.position = new Vector3(0f, StageDepth, 0f);

            var stage = go.AddComponent<CreatureStage>();
            stage._factory = factory;
            stage.Build();
            return stage;
        }

        void Build()
        {
            // No MSAA. The pipeline's colour attachment is single sampled, and asking for
            // two here made every frame of the portrait log a render pass mismatch.
            Texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                name = "CreaturePortrait",
                antiAliasing = 1,
            };

            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(transform, false);

            var camGo = new GameObject("PortraitCamera");
            camGo.transform.SetParent(transform, false);

            _camera = camGo.AddComponent<Camera>();
            _camera.targetTexture = Texture;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.05f, 0.055f, 0.09f, 1f);
            _camera.fieldOfView = 32f;
            _camera.aspect = Width / (float)Height;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 20f;
            _camera.enabled = false;   // rendered on demand while the panel is open

            BuildLights();
        }

        void BuildLights()
        {
            var keyGo = new GameObject("Key");
            keyGo.transform.SetParent(transform, false);
            keyGo.transform.localPosition = new Vector3(1.4f, 2.0f, 1.8f);
            keyGo.transform.LookAt(transform.position);

            Light key = keyGo.AddComponent<Light>();
            key.type = LightType.Point;
            key.range = 9f;
            key.intensity = 4.2f;
            key.color = new Color(1f, 0.96f, 0.90f);
            key.shadows = LightShadows.None;

            var rimGo = new GameObject("Rim");
            rimGo.transform.SetParent(transform, false);
            rimGo.transform.localPosition = new Vector3(-1.7f, 1.1f, -1.9f);

            Light rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Point;
            rim.range = 8f;
            rim.intensity = 2.6f;
            rim.color = new Color(0.62f, 0.74f, 1f);
            rim.shadows = LightShadows.None;
        }

        /// <summary>Put a creature on the stand. Repeated calls for the same species are
        /// ignored so reopening the panel does not rebuild the model.</summary>
        public async Task Show(CreatureDefinition def)
        {
            if (def == null || _factory == null) return;
            if (_currentId == def.id && _current != null) return;

            if (_current != null) Destroy(_current);
            _currentId = def.id;

            _current = await _factory.CreateAsync(def, _pivot);
            if (_current == null) return;

            _current.transform.localPosition = Vector3.zero;
            Frame();
        }

        /// <summary>
        /// Fit the model that is actually on the stand.
        ///
        /// Framing from the authored body height assumed the model matched it exactly,
        /// and the result sat a third of the way up an empty panel. Measuring the thing
        /// in front of the camera works whatever the mesh turned out to be.
        /// </summary>
        void Frame()
        {
            // Ask the factory, which knows to bake a skinned pose rather than trust the
            // padded culling box hung off the renderer.
            if (!CreatureFactory.TryGetWorldBounds(_current, out Bounds bounds)) return;

            // Centre the model on the camera's axis.
            _pivot.localPosition -= bounds.center - _pivot.position;

            // Fit the taller of the two axes, since the panel is wider than it is high.
            float half = Mathf.Max(bounds.extents.y, bounds.extents.x / _camera.aspect);
            _baseDistance = half / Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;

            _camera.transform.localPosition = new Vector3(0f, 0f, -_baseDistance);
            _camera.transform.localRotation = Quaternion.identity;
        }

        /// <summary>Render one frame. Called by the panel while it is on screen.</summary>
        public void Render()
        {
            if (_current == null) return;

            _pivot.localRotation = Quaternion.Euler(0f, Yaw, 0f);

            float distance = _baseDistance / Mathf.Max(0.35f, Zoom);
            Quaternion orbit = Quaternion.Euler(Pitch, 0f, 0f);
            _camera.transform.localPosition = orbit * new Vector3(0f, 0f, -distance);
            _camera.transform.localRotation = orbit;

            _camera.Render();
        }

        void OnDestroy()
        {
            if (Texture != null) Texture.Release();
        }
    }
}
