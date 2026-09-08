using System.Threading.Tasks;
using LivingDiorama.Data;
using LivingDiorama.Simulation;
using UnityEngine;

namespace LivingDiorama.Presentation
{
    /// <summary>
    /// Everything the player actually sees of a creature.
    ///
    /// The generated meshes have no skeleton and no animation clips, so all motion is
    /// procedural: a gait cycle drives bob, squash-and-stretch, lean and roll, and
    /// one-shot impulses layer reactions on top. The upshot is that any new GLB dropped
    /// into the game moves believably on arrival, with no rigging step at all.
    /// </summary>
    public sealed class CreatureView : MonoBehaviour
    {
        enum Reaction { None, Eat, Attack, Startle, Hit, Steal, Splash }

        const float Tau = Mathf.PI * 2f;

        CreatureAgent _agent;
        CreatureDefinition _def;
        Transform _modelRoot;
        GameObject _model;
        EmoteBubble _emote;

        float _phase;
        float _gaitTarget;
        float _gait;                    // smoothed 0..2 speed factor
        float _gaitVelocity;

        bool _sleeping;
        float _sleepBlend;
        float _sleepVelocity;

        float _knockBlend;
        float _knockVelocity;

        Reaction _reaction;
        float _reactionTime;
        float _reactionDuration = 1f;
        Vector3 _reactionDirection = Vector3.forward;

        // Smoothed output state, so nothing ever pops.
        Vector3 _offset, _offsetVelocity;
        Vector3 _euler, _eulerVelocity;
        Vector3 _scale = Vector3.one, _scaleVelocity;

        public bool ModelReady { get; private set; }

        public void Bind(CreatureAgent agent, CreatureFactory factory)
        {
            _agent = agent;
            _def = agent.Definition;

            var rootGo = new GameObject("ModelRoot");
            _modelRoot = rootGo.transform;
            _modelRoot.SetParent(transform, false);

            _emote = EmoteBubble.Create(transform, _def.bodyHeight * 1.35f);

            _ = LoadModel(factory);
        }

        async Task LoadModel(CreatureFactory factory)
        {
            GameObject model = factory != null ? await factory.CreateAsync(_def, _modelRoot) : null;

            if (model == null)
            {
                // A missing asset must never take the diorama down; a tinted capsule keeps
                // the simulation legible while the real problem gets fixed.
                model = BuildPlaceholder();
            }

            _model = model;
            ModelReady = true;
            PlaySpawn();
        }

        GameObject BuildPlaceholder()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"Placeholder_{_def.id}";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_modelRoot, false);
            go.transform.localScale = new Vector3(_def.bodyRadius * 2f, _def.bodyHeight * 0.5f, _def.bodyRadius * 2f);
            go.transform.localPosition = new Vector3(0f, _def.bodyHeight * 0.5f, 0f);

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(mr.sharedMaterial) { color = new Color(0.8f, 0.3f, 0.7f) };
            return go;
        }

        // ---- API used by behaviours -----------------------------------------

        /// <summary>Speed factor relative to walk speed. 0 stands still, 1 walks, 2 sprints.</summary>
        public void SetGait(float speedFactor) => _gaitTarget = Mathf.Clamp(speedFactor, 0f, 2.5f);

        public void SetSleeping(bool sleeping) => _sleeping = sleeping;

        public void PlayEat() => Trigger(Reaction.Eat, 0.85f);
        public void PlayAttack() => Trigger(Reaction.Attack, 0.45f);
        public void PlayStartle() => Trigger(Reaction.Startle, 0.55f);
        public void PlaySteal() => Trigger(Reaction.Steal, 0.6f);
        public void PlaySplash() => Trigger(Reaction.Splash, 0.7f);
        public void PlaySpawn() => Trigger(Reaction.Startle, 0.8f);

        public void PlayHitReaction(Vector3 fromDirection)
        {
            _reactionDirection = fromDirection.sqrMagnitude > 0.001f ? fromDirection.normalized : -transform.forward;
            Trigger(Reaction.Hit, 0.4f);
        }

        public void PlayKnockOut() => _emote?.Show(Mood.KnockedOut, 4f);

        public void PlayRecover()
        {
            Trigger(Reaction.Startle, 0.6f);
            _emote?.Hide();
        }

        public void PlayEmote(Mood mood) => _emote?.Show(mood, 2.5f);

        void Trigger(Reaction reaction, float duration)
        {
            _reaction = reaction;
            _reactionTime = 0f;
            _reactionDuration = Mathf.Max(0.05f, duration);
        }

        // ---- animation ------------------------------------------------------

        void LateUpdate()
        {
            if (_agent == null || _modelRoot == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Track the agent's real speed rather than trusting the behaviour's hint alone;
            // a creature blocked by separation should not keep cycling its legs at full tilt.
            float actual = _def.walkSpeed > 0.01f ? _agent.PlanarSpeed / _def.walkSpeed : 0f;
            float target = Mathf.Min(_gaitTarget, actual + 0.1f);
            _gait = Mathf.SmoothDamp(_gait, target, ref _gaitVelocity, 0.12f);

            _sleepBlend = Mathf.SmoothDamp(_sleepBlend, _sleeping ? 1f : 0f, ref _sleepVelocity, 0.35f);
            _knockBlend = Mathf.SmoothDamp(_knockBlend, _agent.IsKnockedOut ? 1f : 0f, ref _knockVelocity, 0.25f);

            _phase += GaitFrequency() * dt * Tau;
            if (_phase > Tau * 1024f) _phase -= Tau * 1024f;

            EvaluateGait(out Vector3 offset, out Vector3 euler, out Vector3 scale);
            ApplyIdleBreath(ref scale);
            ApplyReaction(dt, ref offset, ref euler, ref scale);
            ApplySleep(ref offset, ref euler, ref scale);
            ApplyKnockOut(ref offset, ref euler, ref scale);

            // Smoothing pass. Everything above computes a target; this is what makes the
            // result read as animation rather than a per-frame jitter of formulas.
            _offset = Vector3.SmoothDamp(_offset, offset, ref _offsetVelocity, 0.06f);
            _euler = Vector3.SmoothDamp(_euler, euler, ref _eulerVelocity, 0.07f);
            _scale = Vector3.SmoothDamp(_scale, scale, ref _scaleVelocity, 0.07f);

            _modelRoot.localPosition = _offset;
            _modelRoot.localRotation = Quaternion.Euler(_euler);
            _modelRoot.localScale = _scale;

            if (_emote != null) _emote.Tick(dt);
        }

        float GaitFrequency()
        {
            float styleMultiplier = _def.locomotion switch
            {
                LocomotionStyle.Scurry => 1.7f,
                LocomotionStyle.Stomp => 0.65f,
                LocomotionStyle.Hop => 0.8f,
                LocomotionStyle.Float => 0.45f,
                LocomotionStyle.Slither => 1.1f,
                _ => 1f,
            };
            // Idle keeps a slow cycle so breathing and hovering never freeze.
            return _def.gaitFrequency * styleMultiplier * (0.35f + _gait * 0.9f);
        }

        void EvaluateGait(out Vector3 offset, out Vector3 euler, out Vector3 scale)
        {
            float h = _def.bodyHeight;
            float amp = _def.gaitBob * h;
            float g = _gait;

            offset = Vector3.zero;
            euler = Vector3.zero;
            float squash = 0f;

            switch (_def.locomotion)
            {
                case LocomotionStyle.Walk:
                    offset.y = Mathf.Abs(Mathf.Sin(_phase)) * amp * g;
                    euler.z = Mathf.Sin(_phase) * 4f * g;
                    euler.x = 6f * g;
                    squash = -Mathf.Sin(_phase * 2f) * 0.04f * g;
                    break;

                case LocomotionStyle.Scurry:
                    offset.y = Mathf.Abs(Mathf.Sin(_phase)) * amp * 0.7f * g;
                    euler.y = Mathf.Sin(_phase * 2f) * 5f * g;
                    euler.z = Mathf.Sin(_phase) * 3f * g;
                    euler.x = 9f * g;
                    squash = -Mathf.Sin(_phase * 2f) * 0.05f * g;
                    break;

                case LocomotionStyle.Hop:
                {
                    float t = Mathf.Repeat(_phase / Tau, 1f);
                    float arc = 4f * t * (1f - t);                   // 0 at contact, 1 at apex
                    float drive = Mathf.Max(g, 0.3f);
                    offset.y = arc * amp * 3.5f * drive;
                    // Compressed on the ground, stretched at the top: the classic blob read.
                    squash = Mathf.Lerp(-0.26f, 0.16f, arc) * drive;
                    euler.x = Mathf.Lerp(-6f, 8f, arc) * g;
                    break;
                }

                case LocomotionStyle.Trot:
                    offset.y = Mathf.Abs(Mathf.Sin(_phase)) * amp * 0.8f * g;
                    euler.z = Mathf.Sin(_phase * 0.5f) * 6f * g;
                    euler.x = 4f * g;
                    squash = -Mathf.Sin(_phase * 2f) * 0.03f * g;
                    break;

                case LocomotionStyle.Stomp:
                {
                    float lift = Mathf.Abs(Mathf.Sin(_phase));
                    offset.y = lift * amp * 1.2f * g;
                    // Weight lands hard: squash spikes as the foot hits.
                    squash = -Mathf.Pow(1f - lift, 6f) * 0.12f * g;
                    euler.z = Mathf.Sin(_phase) * 3f * g;
                    euler.x = 3f * g;
                    break;
                }

                case LocomotionStyle.Float:
                    offset.y = h * 0.14f + (Mathf.Sin(_phase * 0.5f) * 0.5f + 0.5f) * amp * 2f;
                    euler.z = Mathf.Sin(_phase * 0.33f) * 5f;
                    euler.x = 4f * g;
                    squash = Mathf.Sin(_phase * 0.5f) * 0.05f;
                    break;

                case LocomotionStyle.Slither:
                    offset.x = Mathf.Sin(_phase) * h * 0.07f * g;
                    offset.y = Mathf.Abs(Mathf.Sin(_phase * 2f)) * amp * 0.3f * g;
                    euler.y = Mathf.Cos(_phase) * 11f * g;
                    break;
            }

            scale = SquashToScale(squash);
        }

        /// <summary>Volume-preserving squash: what goes down in Y comes out in XZ.</summary>
        static Vector3 SquashToScale(float squash)
        {
            float y = 1f + squash;
            if (y < 0.05f) y = 0.05f;
            float xz = 1f / Mathf.Sqrt(y);
            return new Vector3(xz, y, xz);
        }

        void ApplyIdleBreath(ref Vector3 scale)
        {
            if (_gait > 0.15f) return;

            float breath = Mathf.Sin(Time.time * 1.5f + _phase * 0.1f) * 0.022f * (1f - _gait / 0.15f);
            Vector3 b = SquashToScale(breath);
            scale = new Vector3(scale.x * b.x, scale.y * b.y, scale.z * b.z);
        }

        void ApplyReaction(float dt, ref Vector3 offset, ref Vector3 euler, ref Vector3 scale)
        {
            if (_reaction == Reaction.None) return;

            _reactionTime += dt;
            float t = _reactionTime / _reactionDuration;
            if (t >= 1f)
            {
                _reaction = Reaction.None;
                return;
            }

            // Fast attack, slow settle -- reads as intent followed by recovery.
            float pulse = Mathf.Sin(Mathf.Pow(t, 0.55f) * Mathf.PI);
            float h = _def.bodyHeight;

            switch (_reaction)
            {
                case Reaction.Eat:
                    euler.x += Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)) * 26f;
                    offset.y -= Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)) * h * 0.06f;
                    break;

                case Reaction.Attack:
                    offset += Vector3.forward * (pulse * h * 0.32f);
                    euler.x -= pulse * 16f;
                    Multiply(ref scale, SquashToScale(pulse * 0.10f));
                    break;

                case Reaction.Startle:
                    offset.y += pulse * h * 0.3f;
                    Multiply(ref scale, SquashToScale(pulse * 0.14f));
                    euler.x -= pulse * 8f;
                    break;

                case Reaction.Hit:
                {
                    Vector3 local = transform.InverseTransformDirection(_reactionDirection);
                    offset += local * (pulse * h * 0.22f);
                    euler.x += pulse * 18f;
                    Multiply(ref scale, SquashToScale(-pulse * 0.16f));
                    break;
                }

                case Reaction.Steal:
                    // Crouch, then bolt upright with the loot.
                    offset.y -= Mathf.Sin(t * Mathf.PI) * h * 0.12f;
                    Multiply(ref scale, SquashToScale(-Mathf.Sin(t * Mathf.PI) * 0.18f));
                    euler.y += Mathf.Sin(t * Mathf.PI * 2f) * 14f;
                    break;

                case Reaction.Splash:
                    offset.y += Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * h * 0.22f;
                    Multiply(ref scale, SquashToScale(Mathf.Sin(t * Mathf.PI * 2f) * 0.12f));
                    break;
            }
        }

        void ApplySleep(ref Vector3 offset, ref Vector3 euler, ref Vector3 scale)
        {
            if (_sleepBlend < 0.01f) return;

            float h = _def.bodyHeight;
            offset.y = Mathf.Lerp(offset.y, -h * 0.04f, _sleepBlend);
            euler.x = Mathf.Lerp(euler.x, 6f, _sleepBlend);

            // Slow, deep breathing.
            float breath = Mathf.Sin(Time.time * 0.9f) * 0.05f;
            Vector3 settled = SquashToScale(-0.12f + breath);
            scale = Vector3.Lerp(scale, settled, _sleepBlend);
        }

        void ApplyKnockOut(ref Vector3 offset, ref Vector3 euler, ref Vector3 scale)
        {
            if (_knockBlend < 0.01f) return;

            euler.z = Mathf.Lerp(euler.z, 78f, _knockBlend);
            offset.y = Mathf.Lerp(offset.y, -_def.bodyHeight * 0.08f, _knockBlend);
            scale = Vector3.Lerp(scale, SquashToScale(-0.08f), _knockBlend);
        }

        static void Multiply(ref Vector3 a, Vector3 b)
        {
            a = new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        }

        void OnDestroy()
        {
            if (_model != null) Destroy(_model);
        }
    }
}
