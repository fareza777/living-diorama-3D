using System.Collections.Generic;
using LivingDiorama.Data;
using UnityEngine;

namespace LivingDiorama.Simulation
{
    public enum Mood
    {
        Content,
        Hungry,
        Sleepy,
        Scared,
        Angry,
        Playful,
        Social,
        KnockedOut,
    }

    /// <summary>
    /// One living creature. Holds needs, steering state and whatever the current
    /// behaviour is doing. Behaviours themselves are stateless singletons that read and
    /// write the small scratch fields here, so the hot path allocates nothing.
    /// </summary>
    public sealed class CreatureAgent : MonoBehaviour
    {
        struct TimedTag
        {
            public string Tag;
            public double ExpiresAtHours;
        }

        // ---- identity -------------------------------------------------------
        public CreatureDefinition Definition { get; private set; }
        public string InstanceId { get; private set; }
        public string DisplayName => Definition != null ? Definition.displayName : "Creature";
        public bool IsActive { get; private set; }

        // ---- needs (1 = fully satisfied) ------------------------------------
        public float Fullness { get; set; } = 1f;
        public float Energy { get; set; } = 1f;
        public float Social { get; set; } = 0.7f;
        public float Fun { get; set; } = 0.7f;
        public float Health { get; private set; }

        public bool IsKnockedOut { get; private set; }
        float _knockoutRemaining;

        // ---- movement -------------------------------------------------------
        public Vector3 Position => transform.position;
        public Vector3 Velocity { get; private set; }
        public float PlanarSpeed { get; private set; }
        public Vector3 Forward => transform.forward;

        Vector3 _desiredVelocity;
        Vector3 _steerTarget;
        bool _hasSteerTarget;
        float _speedCap;

        // ---- behaviour scratch ---------------------------------------------
        public CreatureBehaviour Current { get; private set; }
        public float BehaviourTime { get; private set; }
        public float BehaviourTimer;              // free-form countdown for the active behaviour
        public CreatureAgent TargetAgent;
        public FoodNode TargetFood;
        public Vector3 TargetPoint;
        public bool CarryingStolenFood;
        public float ActionCooldown;              // shared attack / interact cooldown

        readonly Dictionary<string, float> _behaviourCooldowns = new(8);
        readonly List<TimedTag> _runtimeTags = new(4);
        readonly List<string> _runtimeTagNames = new(4);

        // ---- wiring ---------------------------------------------------------
        public EcosystemSimulation World { get; private set; }
        public Presentation.CreatureView View { get; private set; }
        public int BrainBucket { get; private set; }

        public IReadOnlyList<string> RuntimeTags => _runtimeTagNames;

        public void Initialise(CreatureDefinition definition, string instanceId,
                               EcosystemSimulation world, Presentation.CreatureView view, int bucket)
        {
            Definition = definition;
            InstanceId = instanceId;
            World = world;
            View = view;
            BrainBucket = bucket;
            Health = definition.maxHealth;
            _speedCap = definition.walkSpeed;
            IsActive = true;

            // Stagger starting needs so a freshly loaded diorama does not have every
            // creature getting hungry in perfect unison.
            Fullness = Random.Range(0.55f, 1f);
            Energy = Random.Range(0.5f, 1f);
            Social = Random.Range(0.4f, 0.9f);
            Fun = Random.Range(0.4f, 0.9f);
        }

        public void Deactivate()
        {
            IsActive = false;
            Current?.OnExit(this);
            Current = null;
            gameObject.SetActive(false);
        }

        // ---- needs ----------------------------------------------------------

        /// <summary>Advance needs by an amount of in-game hours.</summary>
        public void TickNeeds(float inGameHours, float activity)
        {
            CreatureDefinition d = Definition;
            Fullness = Mathf.Clamp01(Fullness - d.hungerRate * inGameHours);

            bool sleeping = Current is Behaviours.SleepBehaviour;
            if (sleeping)
            {
                Energy = Mathf.Clamp01(Energy + World.Settings.sleepRecoveryRate * inGameHours);
            }
            else
            {
                // Being awake against your natural cycle is tiring.
                float drain = d.energyRate * Mathf.Lerp(1.6f, 0.8f, activity);
                Energy = Mathf.Clamp01(Energy - drain * inGameHours);
            }

            Social = Mathf.Clamp01(Social - d.socialRate * inGameHours);
            Fun = Mathf.Clamp01(Fun - d.socialRate * 0.7f * inGameHours);

            if (Health < d.maxHealth && !IsKnockedOut)
            {
                Health = Mathf.Min(d.maxHealth, Health + d.maxHealth * 0.15f * inGameHours);
            }
        }

        public void Feed(float amount)
        {
            Fullness = Mathf.Clamp01(Fullness + amount);
        }

        public void AddFun(float amount) => Fun = Mathf.Clamp01(Fun + amount);
        public void AddSocial(float amount) => Social = Mathf.Clamp01(Social + amount);

        // ---- combat ---------------------------------------------------------

        public void TakeDamage(float amount, CreatureAgent from)
        {
            if (IsKnockedOut) return;

            Health -= amount;
            View?.PlayHitReaction((Position - (from != null ? from.Position : Position)).normalized);

            if (Health <= 0f)
            {
                KnockOut(from);
            }
        }

        void KnockOut(CreatureAgent by)
        {
            IsKnockedOut = true;
            Health = 0f;
            _knockoutRemaining = World.Settings.knockoutSeconds;
            Current?.OnExit(this);
            Current = null;
            View?.PlayKnockOut();
            SimEventBus.Publish(new SimEvent(SimEventKind.KnockedOut, by, this, Position, World.Clock.TotalHours));
        }

        void TickKnockout(float dt)
        {
            _knockoutRemaining -= dt;
            if (_knockoutRemaining > 0f) return;

            IsKnockedOut = false;
            Health = Definition.maxHealth * 0.6f;
            View?.PlayRecover();
            AddRuntimeTag("shaken", 0.5);
            SimEventBus.Publish(new SimEvent(SimEventKind.Recovered, this, null, Position, World.Clock.TotalHours));
        }

        // ---- runtime tags ---------------------------------------------------

        /// <summary>Attach a transient trait, e.g. "thief" for half an in-game hour.
        /// Relation rules read these alongside the species tags.</summary>
        public void AddRuntimeTag(string tag, double durationHours)
        {
            double expiry = World.Clock.TotalHours + durationHours;
            for (int i = 0; i < _runtimeTags.Count; i++)
            {
                if (_runtimeTags[i].Tag == tag)
                {
                    TimedTag existing = _runtimeTags[i];
                    existing.ExpiresAtHours = System.Math.Max(existing.ExpiresAtHours, expiry);
                    _runtimeTags[i] = existing;
                    return;
                }
            }
            _runtimeTags.Add(new TimedTag { Tag = tag, ExpiresAtHours = expiry });
            _runtimeTagNames.Add(tag);
        }

        public bool HasRuntimeTag(string tag) => _runtimeTagNames.Contains(tag);

        void ExpireTags(double nowHours)
        {
            for (int i = _runtimeTags.Count - 1; i >= 0; i--)
            {
                if (_runtimeTags[i].ExpiresAtHours > nowHours) continue;
                _runtimeTagNames.Remove(_runtimeTags[i].Tag);
                _runtimeTags.RemoveAt(i);
            }
        }

        // ---- behaviour ------------------------------------------------------

        public void SwitchTo(CreatureBehaviour next, in BehaviourContext ctx)
        {
            if (next == Current) return;

            if (Current != null)
            {
                _behaviourCooldowns[Current.Id] = Current.CooldownAfterExit;
                Current.OnExit(this);
            }

            Current = next;
            BehaviourTime = 0f;
            BehaviourTimer = 0f;

            // Rigged creatures pick their animation from the behaviour id, so the two
            // vocabularies are the same words on purpose.
            if (next != null) View?.SetBehaviour(next.Id);

            next?.OnEnter(this, ctx);
        }

        public float CooldownFor(CreatureBehaviour behaviour)
            => _behaviourCooldowns.GetValueOrDefault(behaviour.Id, 0f);

        void TickCooldowns(float dt)
        {
            if (_behaviourCooldowns.Count == 0) return;

            // Iterating a dictionary while writing needs a snapshot of the keys; the map is
            // tiny (one entry per behaviour the creature has actually used) so this is cheap.
            using var e = _behaviourCooldowns.GetEnumerator();
            _cooldownScratch.Clear();
            while (e.MoveNext())
            {
                if (e.Current.Value > 0f) _cooldownScratch.Add(e.Current.Key);
            }
            for (int i = 0; i < _cooldownScratch.Count; i++)
            {
                _behaviourCooldowns[_cooldownScratch[i]] =
                    Mathf.Max(0f, _behaviourCooldowns[_cooldownScratch[i]] - dt);
            }
        }

        static readonly List<string> _cooldownScratch = new(8);

        // ---- steering -------------------------------------------------------

        public void MoveTowards(Vector3 worldTarget, float speed)
        {
            _steerTarget = worldTarget;
            _hasSteerTarget = true;
            _speedCap = Mathf.Max(0.01f, speed);
        }

        public void Halt()
        {
            _hasSteerTarget = false;
            _speedCap = 0f;
        }

        /// <summary>Keep facing a point while standing still. Applied every frame so eating
        /// and squaring up to a fight do not look stepped at the 8Hz brain rate.</summary>
        public void FaceWhileStill(Vector3 point)
        {
            _faceTarget = point;
            _hasFaceTarget = true;
        }

        public void ClearFacing() => _hasFaceTarget = false;

        Vector3 _faceTarget;
        bool _hasFaceTarget;

        public bool AtTarget(float tolerance)
        {
            if (!_hasSteerTarget) return true;
            Vector3 d = _steerTarget - Position;
            d.y = 0f;
            return d.sqrMagnitude <= tolerance * tolerance;
        }

        public float DistanceTo(Vector3 point)
        {
            Vector3 d = point - Position;
            d.y = 0f;
            return d.magnitude;
        }

        public float DistanceTo(CreatureAgent other) => other == null ? float.MaxValue : DistanceTo(other.Position);

        /// <summary>Per-frame integration. Separate from the brain tick so movement stays
        /// smooth at 60fps while decisions run at 8Hz.</summary>
        public void TickMovement(float dt, Vector3 separation, IWorldSurface surface)
        {
            if (!IsActive) return;

            ActionCooldown = Mathf.Max(0f, ActionCooldown - dt);
            BehaviourTime += dt;
            BehaviourTimer = Mathf.Max(0f, BehaviourTimer - dt);
            TickCooldowns(dt);
            ExpireTags(World.Clock.TotalHours);

            if (IsKnockedOut)
            {
                TickKnockout(dt);
                Velocity = Vector3.Lerp(Velocity, Vector3.zero, dt * 8f);
                PlanarSpeed = 0f;
                return;
            }

            Vector3 desired = Vector3.zero;
            if (_hasSteerTarget && _speedCap > 0.01f)
            {
                Vector3 toTarget = _steerTarget - Position;
                toTarget.y = 0f;
                float dist = toTarget.magnitude;
                if (dist > 0.001f)
                {
                    // Ease into the last half metre so creatures do not skid to a stop.
                    float arrive = Mathf.Clamp01(dist / 0.5f);
                    desired = toTarget / dist * _speedCap * arrive;
                }
            }

            desired += separation;
            _desiredVelocity = Vector3.ClampMagnitude(desired, Mathf.Max(_speedCap, separation.magnitude));

            float accel = World.Settings.steeringAcceleration;
            Velocity = Vector3.MoveTowards(Velocity, _desiredVelocity, accel * dt);

            Vector3 next = Position + Velocity * dt;
            if (surface != null && !surface.Contains(next))
            {
                next = surface.ClampInside(next);
                Velocity *= 0.3f;
            }

            if (surface != null)
            {
                // Walk around the trees rather than through them, then settle onto the
                // ground at wherever that leaves us.
                next = surface.ResolveObstacles(next, Definition.bodyRadius);
                next.y = surface.SampleHeight(next) + Definition.groundOffset;
            }

            transform.position = next;

            Vector3 planar = new(Velocity.x, 0f, Velocity.z);
            PlanarSpeed = planar.magnitude;

            if (PlanarSpeed > 0.05f)
            {
                Quaternion look = Quaternion.LookRotation(planar, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, look, Definition.turnSpeedDeg * dt);
            }
            else if (_hasFaceTarget)
            {
                Vector3 d = _faceTarget - next;
                d.y = 0f;
                if (d.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation, Quaternion.LookRotation(d, Vector3.up),
                        Definition.turnSpeedDeg * dt);
                }
            }
        }

        // ---- presentation ---------------------------------------------------

        public Mood CurrentMood
        {
            get
            {
                if (IsKnockedOut) return Mood.KnockedOut;
                if (Current is Behaviours.FleeBehaviour) return Mood.Scared;
                if (Current is Behaviours.AttackBehaviour or Behaviours.ChaseBehaviour) return Mood.Angry;
                if (Current is Behaviours.SleepBehaviour) return Mood.Sleepy;
                if (Current is Behaviours.PlayInWaterBehaviour) return Mood.Playful;
                if (Current is Behaviours.SocialiseBehaviour) return Mood.Social;
                if (Fullness < World.Settings.hungerSeekThreshold) return Mood.Hungry;
                if (Energy < World.Settings.energySleepThreshold) return Mood.Sleepy;
                return Mood.Content;
            }
        }

        /// <summary>0..1 wellbeing, used for the coin multiplier and the collection screen.</summary>
        public float Wellbeing => Mathf.Clamp01(
            Fullness * 0.35f + Energy * 0.25f + Social * 0.15f + Fun * 0.15f +
            (Health / Definition.maxHealth) * 0.10f);
    }
}
