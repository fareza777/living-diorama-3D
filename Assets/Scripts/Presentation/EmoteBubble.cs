using LivingDiorama.Simulation;
using UnityEngine;

namespace LivingDiorama.Presentation
{
    /// <summary>
    /// A little billboard above a creature's head showing what it is feeling. This is
    /// how the player reads the simulation at a glance without any UI clutter.
    /// </summary>
    public sealed class EmoteBubble : MonoBehaviour
    {
        static Mesh _quad;
        static Shader _shader;

        MeshRenderer _renderer;
        Material _material;
        Transform _camera;

        float _remaining;
        float _visible;          // eased 0..1
        float _visibleVelocity;
        float _bobPhase;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public static EmoteBubble Create(Transform parent, float height)
        {
            var go = new GameObject("Emote");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);

            var bubble = go.AddComponent<EmoteBubble>();
            bubble.Build();
            return bubble;
        }

        void Build()
        {
            _quad = _quad != null ? _quad : BuildQuad();
            _shader = _shader != null ? _shader : Shader.Find("Living Diorama/Emote");

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _quad;

            // Each bubble owns its material rather than sharing one and overriding the
            // icon per renderer.
            //
            // A property block was the obvious way to do this and it silently does
            // nothing here: the shader declares a UnityPerMaterial buffer, which makes it
            // SRP Batcher compatible, and the batcher ignores per-renderer property
            // blocks. Every creature therefore drew the shader's default white texture --
            // a blank square over its head, whatever it was actually feeling.
            _material = new Material(_shader != null ? _shader : Shader.Find("Sprites/Default"))
            {
                name = "EmoteIcon",
            };

            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;

            if (Camera.main != null) _camera = Camera.main.transform;
        }

        static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "EmoteQuad" };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
            });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f),
            });
            mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Show(Mood mood, float seconds)
        {
            if (mood == Mood.Content)
            {
                Hide();
                return;
            }

            _remaining = Mathf.Max(_remaining, seconds);
            _renderer.enabled = true;

            _material.SetTexture(MainTexId, EmoteIcons.For(mood));
            _material.SetColor(ColorId, Color.white);
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public void Hide() => _remaining = 0f;

        public void Tick(float dt)
        {
            _remaining = Mathf.Max(0f, _remaining - dt);

            float target = _remaining > 0f ? 1f : 0f;
            _visible = Mathf.SmoothDamp(_visible, target, ref _visibleVelocity, 0.14f);

            if (_visible < 0.01f)
            {
                if (_renderer.enabled) _renderer.enabled = false;
                return;
            }

            _bobPhase += dt * 2.4f;

            // Pop in with a slight overshoot, then settle and bob gently.
            float overshoot = 1f + Mathf.Sin(Mathf.Clamp01(_visible) * Mathf.PI) * 0.22f;
            // Large enough to read at arm's length on a phone.
            float scale = 0.46f * _visible * overshoot;
            transform.localScale = Vector3.one * scale;

            Vector3 p = transform.localPosition;
            p.y += Mathf.Sin(_bobPhase) * 0.004f;
            transform.localPosition = p;

            if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
            if (_camera != null)
            {
                // Billboard, but keep the icon upright rather than fully camera-aligned.
                Vector3 toCamera = _camera.position - transform.position;
                toCamera.y = 0f;
                if (toCamera.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                }
            }
        }
    }
}
