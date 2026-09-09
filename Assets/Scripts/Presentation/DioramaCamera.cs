using System;
using LivingDiorama.Simulation;
using UnityEngine;

namespace LivingDiorama.Presentation
{
    /// <summary>
    /// Orbit camera for looking into the diorama.
    ///
    /// Everything is critically damped towards a target, so no input ever moves the
    /// camera directly. That is what makes a touchscreen orbit feel expensive rather
    /// than twitchy, and it means the same code handles a player drag, a snap-to-creature
    /// and an auto-reframe after an expansion without any of them fighting.
    /// </summary>
    public sealed class DioramaCamera : MonoBehaviour
    {
        [Header("Framing")]
        [SerializeField] float _minDistance = 5f;
        [SerializeField] float _maxDistance = 26f;
        [SerializeField] float _minPitch = 14f;
        [SerializeField] float _maxPitch = 72f;

        [Header("Feel")]
        [SerializeField] float _orbitSensitivity = 0.22f;
        [SerializeField] float _pitchSensitivity = 0.16f;
        [SerializeField] float _panSensitivity = 0.012f;
        [SerializeField] float _zoomSensitivity = 0.02f;
        [SerializeField] float _smoothTime = 0.14f;

        [Header("Idle drift")]
        [Tooltip("Seconds of no input before the camera starts a slow orbit by itself. " +
                 "The diorama is meant to be watched, so it presents itself when left alone.")]
        [SerializeField] float _idleDelay = 12f;
        [SerializeField] float _idleOrbitSpeed = 1.6f;

        /// <summary>Looking slightly down at a table-top object, the way you would at a
        /// real diorama on a stand.</summary>
        /// <summary>Resting camera angle.
        ///
        /// A phone is tall and narrow and the diorama is wide and flat, which is the worst
        /// possible match: framed from low down, the slab's cut wall fills the screen and
        /// the top surface -- where everything actually happens -- is squashed to a
        /// sliver. Looking further down foreshortens the width, stands the footprint up
        /// the tall axis of the screen, and puts the creatures in view.</summary>
        const float DefaultPitch = 50f;

        float _yaw = 35f, _targetYaw = 35f;
        float _pitch = 42f, _targetPitch = 42f;
        float _distance = 14f, _targetDistance = 14f;
        Vector3 _pivot, _targetPivot;

        float _yawVelocity, _pitchVelocity, _distanceVelocity;
        Vector3 _pivotVelocity;

        float _idleTimer;
        bool _dragging;
        Vector2 _dragStart;
        float _dragDistance;
        int _activeTouches;

        Camera _camera;
        Bounds _bounds;

        /// <summary>Raised when the player taps a creature. Null when they tap empty ground.</summary>
        public event Action<CreatureAgent> CreatureTapped;

        /// <summary>Set true while a modal panel is open so drags do not move the world.</summary>
        public bool InputBlocked { get; set; }

        public enum Mode
        {
            /// <summary>The player drives: orbit, pinch, pan, tap.</summary>
            Interactive,

            /// <summary>Slow presentation orbit for the title sequence.</summary>
            Cinematic,

            /// <summary>Targets are driven from outside, e.g. by the unboxing director.</summary>
            Scripted,
        }

        Mode _mode = Mode.Interactive;
        float _cinematicTime;
        float _cinematicBaseDistance;

        public Mode CurrentMode => _mode;

        public void SetMode(Mode mode)
        {
            Mode previous = _mode;
            _mode = mode;

            if (mode == Mode.Cinematic)
            {
                _cinematicTime = 0f;
                _cinematicBaseDistance = _targetDistance;
            }
            else if (previous == Mode.Cinematic && mode == Mode.Interactive)
            {
                // Hand back the resting framing.
                //
                // The title screen's orbit drives the pitch down to about seventeen
                // degrees, which is the right look for a slow presentation move and the
                // wrong one to play on: from there the slab's cut wall fills the screen
                // and the surface everything happens on is a sliver. Interactive mode only
                // changes pitch when the player drags, so whatever the cinematic left
                // behind simply stayed -- every session began at the title's camera angle
                // and never recovered.
                Frame(_bounds);
            }

            _idleTimer = 0f;
        }

        /// <summary>Continuous slow rotation, the way a real exhibition turntable works.
        /// Toggled from the HUD; also what the idle timer falls back into.</summary>
        public bool Turntable { get; set; }

        /// <summary>
        /// Aim the camera at a specific presentation setup and let the existing damping
        /// carry it there. Used by the unboxing sequence.
        ///
        /// The height is honoured here, unlike everywhere else. Interactive framing pins
        /// the pivot to the ground because the diorama is on the ground; the reveal stage
        /// is not, and flattening its pivot pointed the camera at empty terrain fourteen
        /// units below the chest.
        /// </summary>
        public void SetScriptedShot(Vector3 pivot, float yaw, float pitch, float distance)
        {
            _targetPivot = pivot;
            _targetYaw = yaw;
            _targetPitch = Mathf.Clamp(pitch, _minPitch, _maxPitch);
            _targetDistance = Mathf.Clamp(distance, 1.5f, _maxDistance);
        }

        /// <summary>Current framing, so a caller can restore it after a scripted sequence.</summary>
        public (Vector3 pivot, float yaw, float pitch, float distance) Snapshot()
            => (_targetPivot, _targetYaw, _targetPitch, _targetDistance);

        public void Restore((Vector3 pivot, float yaw, float pitch, float distance) shot)
        {
            _targetPivot = shot.pivot;
            _targetYaw = shot.yaw;
            _targetPitch = shot.pitch;
            _targetDistance = shot.distance;
        }

        void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null) _camera = gameObject.AddComponent<Camera>();
        }

        /// <summary>
        /// Fit the whole diorama in frame. Called after every expansion, and by the
        /// double tap that puts a lost player back where they started.
        ///
        /// The pivot is lifted off the ground on purpose. Orbiting a point at y = 0 puts
        /// the terrain in the lower half of the screen with empty sky above it, which is
        /// the "hard to find the middle" feeling; aiming at the height the creatures
        /// actually stand at centres the thing the player is looking at.
        /// </summary>
        public void Frame(Bounds bounds, bool immediate = false)
        {
            _bounds = bounds;

            // Aim at the middle of the object itself, plinth included. Aiming at ground
            // level puts everything below the horizon line and leaves a third of the
            // screen as empty sky, which is what "hard to find the middle" looks like.
            _targetPivot = bounds.center;
            _targetPitch = DefaultPitch;
            _targetYaw = Mathf.Round(_targetYaw / 90f) * 90f + 35f;

            // Fit both axes, not just the vertical one.
            //
            // A phone is tall and narrow, so its horizontal field of view is far smaller
            // than its vertical one. Fitting only the height put the camera close enough
            // that the diorama ran off both edges while sky and floor sat empty above and
            // below it -- the whole subject visible in neither direction.
            float halfV = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfH = Mathf.Atan(Mathf.Tan(halfV) * Mathf.Max(_camera.aspect, 0.05f));

            // Seen from an angle the square slab is as wide as its diagonal.
            float width = new Vector2(bounds.size.x, bounds.size.z).magnitude;

            float pitch = DefaultPitch * Mathf.Deg2Rad;
            float height = bounds.size.y * Mathf.Cos(pitch) + width * Mathf.Sin(pitch);

            float fitWidth = width * 0.5f / Mathf.Tan(halfH);
            float fitHeight = height * 0.5f / Mathf.Tan(halfV);

            // Only enough margin to keep the corners off the edge. More than this and the
            // diorama shrinks into the middle of a tall screen with nothing around it.
            float fitDistance = Mathf.Max(fitWidth, fitHeight) * 1.08f;
            _targetDistance = Mathf.Clamp(fitDistance, _minDistance, _maxDistance);

            if (immediate)
            {
                _pivot = _targetPivot;
                _distance = _targetDistance;
                _yaw = _targetYaw;
                _pitch = _targetPitch;
                Apply();
            }
        }

        public void FocusOn(Vector3 worldPoint, float distance = 6.5f)
        {
            _targetPivot = new Vector3(worldPoint.x, worldPoint.y, worldPoint.z);
            _targetDistance = Mathf.Clamp(distance, _minDistance, _maxDistance);
            _idleTimer = 0f;
        }

        void Update()
        {
            switch (_mode)
            {
                case Mode.Interactive:
                    if (!InputBlocked) ReadInput();
                    IdleDrift();
                    break;

                case Mode.Cinematic:
                    CinematicUpdate();
                    break;

                case Mode.Scripted:
                    // Targets come from SetScriptedShot; only the damping runs.
                    break;
            }

            Smooth();
            Apply();
        }

        /// <summary>The title-screen move: a low, slow orbit that breathes in and out, so
        /// the diorama presents itself the way a turntable in a display case would.</summary>
        void CinematicUpdate()
        {
            _cinematicTime += Time.deltaTime;

            _targetYaw += 3.2f * Time.deltaTime;
            _targetPitch = 17f + Mathf.Sin(_cinematicTime * 0.21f) * 5.5f;

            float breathe = 1f + Mathf.Sin(_cinematicTime * 0.16f) * 0.07f;
            _targetDistance = Mathf.Clamp(_cinematicBaseDistance * breathe, _minDistance, _maxDistance);
        }

        void ReadInput()
        {
            if (Input.touchCount > 0) ReadTouch();
            else ReadMouse();
        }

        void ReadTouch()
        {
            _activeTouches = Input.touchCount;

            if (Input.touchCount == 1)
            {
                Touch t = Input.GetTouch(0);
                switch (t.phase)
                {
                    case TouchPhase.Began:
                        BeginDrag(t.position);
                        break;
                    case TouchPhase.Moved:
                        Orbit(t.deltaPosition);
                        break;
                    case TouchPhase.Ended when _dragDistance < 18f:
                        // A short press is a tap, not a failed orbit.
                        if (!ConsumeDoubleTap()) Pick(t.position);
                        break;
                }
                return;
            }

            if (Input.touchCount >= 2)
            {
                Touch a = Input.GetTouch(0);
                Touch b = Input.GetTouch(1);

                float previous = ((a.position - a.deltaPosition) - (b.position - b.deltaPosition)).magnitude;
                float current = (a.position - b.position).magnitude;
                Zoom((previous - current) * _zoomSensitivity);

                // The shared component of both fingers is a pan.
                Vector2 shared = (a.deltaPosition + b.deltaPosition) * 0.5f;
                Pan(shared);

                _dragDistance = 999f;   // never treat a pinch as a tap
            }
        }

        void ReadMouse()
        {
            _activeTouches = 0;

            if (Input.GetMouseButtonDown(0)) BeginDrag(Input.mousePosition);
            if (Input.GetMouseButton(0))
            {
                Orbit(new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 12f);
            }
            if (Input.GetMouseButtonUp(0) && _dragDistance < 8f && !ConsumeDoubleTap())
            {
                Pick(Input.mousePosition);
            }

            if (Input.GetMouseButton(2))
            {
                Pan(new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 12f);
            }

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f) Zoom(-wheel * 0.6f);
        }

        float _lastTapTime = -10f;

        void BeginDrag(Vector2 screenPosition)
        {
            _dragging = true;
            _dragStart = screenPosition;
            _dragDistance = 0f;
            _idleTimer = 0f;
        }

        /// <summary>Two quick taps reframe the whole diorama. Panning and orbiting a 3D
        /// view will always eventually lose someone; this is the way back.</summary>
        bool ConsumeDoubleTap()
        {
            float now = Time.unscaledTime;
            bool isDouble = now - _lastTapTime < 0.35f;
            _lastTapTime = isDouble ? -10f : now;

            if (isDouble) Frame(_bounds);
            return isDouble;
        }

        void Orbit(Vector2 delta)
        {
            if (delta.sqrMagnitude < 0.0001f) return;

            _dragDistance += delta.magnitude;
            _targetYaw -= delta.x * _orbitSensitivity;
            _targetPitch = Mathf.Clamp(_targetPitch + delta.y * _pitchSensitivity, _minPitch, _maxPitch);
            _idleTimer = 0f;
        }

        void Pan(Vector2 delta)
        {
            // Pan in the camera's ground plane so dragging feels like moving the table.
            Vector3 right = transform.right;
            Vector3 forward = Vector3.Cross(right, Vector3.up);

            float scale = _panSensitivity * _distance;
            _targetPivot -= (right * delta.x + forward * delta.y) * scale;
            ClampPivot();
            _idleTimer = 0f;
        }

        void Zoom(float amount)
        {
            _targetDistance = Mathf.Clamp(_targetDistance + amount * _distance * 0.12f,
                                          _minDistance, _maxDistance);
            _idleTimer = 0f;
        }

        void ClampPivot()
        {
            if (_bounds.size.sqrMagnitude < 0.001f) return;

            // Keep the pivot near the diorama; drifting off into empty space is never useful.
            Vector3 c = _bounds.center;
            float limitX = _bounds.extents.x + 3f;
            float limitZ = _bounds.extents.z + 3f;

            _targetPivot.x = Mathf.Clamp(_targetPivot.x, c.x - limitX, c.x + limitX);
            _targetPivot.z = Mathf.Clamp(_targetPivot.z, c.z - limitZ, c.z + limitZ);
            _targetPivot.y = 0f;
        }

        void Pick(Vector2 screenPosition)
        {
            _dragging = false;
            if (_camera == null) return;

            Ray ray = _camera.ScreenPointToRay(screenPosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Collide))
            {
                var agent = hit.collider.GetComponentInParent<CreatureAgent>();
                CreatureTapped?.Invoke(agent);
                return;
            }

            CreatureTapped?.Invoke(null);
        }

        void IdleDrift()
        {
            bool touching = _dragging && (_activeTouches > 0 || Input.GetMouseButton(0));

            if (Turntable)
            {
                // An explicit turntable keeps spinning even between touches; it only pauses
                // while a finger is actually down, so the player can nudge it and let go.
                if (!touching) _targetYaw += _idleOrbitSpeed * 1.6f * Time.deltaTime;
                return;
            }

            if (touching) return;

            _idleTimer += Time.deltaTime;
            if (_idleTimer < _idleDelay) return;

            // Ease the drift in so it never starts with a jolt.
            float ramp = Mathf.Clamp01((_idleTimer - _idleDelay) / 2.5f);
            _targetYaw += _idleOrbitSpeed * ramp * Time.deltaTime;
        }

        void Smooth()
        {
            _yaw = Mathf.SmoothDampAngle(_yaw, _targetYaw, ref _yawVelocity, _smoothTime);
            _pitch = Mathf.SmoothDamp(_pitch, _targetPitch, ref _pitchVelocity, _smoothTime);
            _distance = Mathf.SmoothDamp(_distance, _targetDistance, ref _distanceVelocity, _smoothTime);
            _pivot = Vector3.SmoothDamp(_pivot, _targetPivot, ref _pivotVelocity, _smoothTime);
        }

        void Apply()
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 position = _pivot - rotation * Vector3.forward * _distance;

            if (_shakeTime > 0f)
            {
                _shakeTime -= Time.deltaTime;
                float k = Mathf.Clamp01(_shakeTime / Mathf.Max(0.0001f, _shakeDuration));
                // Decay the amplitude but keep the frequency, so the hit lands hard and
                // settles quickly rather than wobbling.
                float amplitude = _shakeAmount * k * k;
                float t = Time.time * 34f;

                position += rotation * new Vector3(
                    (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f * amplitude,
                    (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f * amplitude,
                    0f);
            }

            transform.rotation = rotation;
            transform.position = position;
        }

        float _shakeAmount, _shakeDuration, _shakeTime;

        /// <summary>Kick the camera. Amount is in world units at the current distance.</summary>
        public void Shake(float amount, float duration = 0.35f)
        {
            // Never let a small shake cut a big one short.
            if (_shakeTime > 0f && amount < _shakeAmount) return;

            _shakeAmount = amount;
            _shakeDuration = duration;
            _shakeTime = duration;
        }
    }
}
