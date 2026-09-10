using System.Collections.Generic;
using LivingDiorama.Data;
using LivingDiorama.Presentation;
using UnityEngine;

namespace LivingDiorama.Simulation
{
    /// <summary>
    /// Owns the living world: who exists, what they can see, and when their brains run.
    /// Movement integrates every frame so the diorama looks smooth; decisions run at a
    /// fixed low rate spread across buckets so the frame cost stays flat as the
    /// population grows.
    /// </summary>
    public sealed class EcosystemSimulation : MonoBehaviour, UI.IWorldSurfaceProvider
    {
        readonly List<CreatureAgent> _agents = new(48);
        readonly List<FoodNode> _food = new(32);
        readonly List<CreatureAgent> _neighbourScratch = new(16);
        readonly List<CreatureAgent> _separationScratch = new(8);

        SpatialHash _hash;
        CreatureBrain _brain;
        GameDatabase _db;
        CreatureFactory _factory;
        float _tickAccumulator;
        int _nextBucket;
        double _lastFoodRegrowHours;

        public WorldClock Clock { get; private set; }
        public IWorldSurface Surface { get; private set; }

        /// <summary>What the player has built. Null until the diorama hands it over,
        /// and null forever in the flat stub the brain is tested against.</summary>
        public Diorama.PlacementService Placements { get; set; }
        public SimulationSettings Settings { get; private set; }
        public IReadOnlyList<CreatureAgent> Agents => _agents;
        public bool IsRunning { get; private set; }

        /// <summary>Set false while a modal screen is up so the world holds still.</summary>
        public bool Paused { get; set; }

        public void Initialise(GameDatabase db, IWorldSurface surface, WorldClock clock,
                               CreatureFactory factory)
        {
            _db = db;
            Surface = surface;
            Clock = clock;
            _factory = factory;
            Settings = db.simulation;
            _hash = new SpatialHash(Settings.spatialCellSize);
            _brain = CreatureBrain.CreateDefault();
            _lastFoodRegrowHours = clock.TotalHours;
            IsRunning = true;
        }

        public void Shutdown()
        {
            IsRunning = false;
            for (int i = _agents.Count - 1; i >= 0; i--) Despawn(_agents[i]);
            _agents.Clear();
            _food.Clear();
        }

        // ---- population -----------------------------------------------------

        public int Population => _agents.Count;

        public CreatureAgent Spawn(CreatureDefinition definition, string instanceId, Vector3 position)
        {
            if (definition == null) return null;

            var go = new GameObject($"Creature_{definition.id}_{instanceId}");
            go.transform.SetParent(transform, false);
            if (Surface != null)
            {
                // Do not put anyone down inside a tree; a creature that starts overlapping
                // the scenery stands in it until it happens to walk out.
                position = Surface.ResolveObstacles(position, definition.bodyRadius);
                position.y = Surface.SampleHeight(position) + definition.groundOffset;
            }
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            // Picking only. Movement and separation are handled analytically, so this
            // collider never participates in simulation -- it exists so a tap can find
            // the creature under the player's finger.
            var picker = go.AddComponent<SphereCollider>();
            picker.isTrigger = true;
            picker.radius = Mathf.Max(definition.bodyRadius, definition.bodyHeight * 0.5f);
            picker.center = new Vector3(0f, definition.bodyHeight * 0.5f, 0f);

            var view = go.AddComponent<CreatureView>();
            var agent = go.AddComponent<CreatureAgent>();

            int bucket = _agents.Count % Mathf.Max(1, Settings.brainBuckets);
            agent.Initialise(definition, instanceId, this, view, bucket);
            view.Bind(agent, _factory);

            _agents.Add(agent);
            SimEventBus.Publish(new SimEvent(SimEventKind.Spawned, agent, null, position, Clock.TotalHours));
            return agent;
        }

        public void Despawn(CreatureAgent agent)
        {
            if (agent == null) return;
            _agents.Remove(agent);
            agent.Deactivate();
            Destroy(agent.gameObject);
        }

        public CreatureAgent FindByInstanceId(string instanceId)
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                if (_agents[i].InstanceId == instanceId) return _agents[i];
            }
            return null;
        }

        // ---- food -----------------------------------------------------------

        public void RegisterFood(FoodNode node)
        {
            if (node != null && !_food.Contains(node)) _food.Add(node);
        }

        public void UnregisterFood(FoodNode node) => _food.Remove(node);

        public void ClearFood() => _food.Clear();

        public FoodNode FindFood(CreatureAgent agent, out float distance)
        {
            distance = float.MaxValue;
            FoodNode best = null;
            Diet diet = agent.Definition.diet;
            if (diet == Diet.None) return null;

            for (int i = 0; i < _food.Count; i++)
            {
                FoodNode n = _food[i];
                if (n == null || !n.HasFood || !n.Satisfies(diet)) continue;

                float d = agent.DistanceTo(n.Position);
                if (d < distance)
                {
                    distance = d;
                    best = n;
                }
            }
            return best;
        }

        public FoodNode FindStockpile(Vector3 from, out float distance)
        {
            distance = float.MaxValue;
            FoodNode best = null;
            for (int i = 0; i < _food.Count; i++)
            {
                FoodNode n = _food[i];
                if (n == null || !n.isStockpile || !n.HasFood) continue;

                Vector3 d = n.Position - from;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < distance)
                {
                    distance = dist;
                    best = n;
                }
            }
            return best;
        }

        // ---- loop -----------------------------------------------------------

        void Update()
        {
            if (!IsRunning || Paused) return;

            float dt = Time.deltaTime;
            Clock.Advance(dt);
            _hash.Rebuild(_agents);

            RunBrainTicks(dt);
            RunMovement(dt);
            RegrowFood();
        }

        void RunBrainTicks(float dt)
        {
            float interval = 1f / Mathf.Max(1f, Settings.brainTicksPerSecond);
            _tickAccumulator += dt;

            int buckets = Mathf.Max(1, Settings.brainBuckets);
            int guard = 0;

            while (_tickAccumulator >= interval / buckets && guard++ < buckets * 2)
            {
                _tickAccumulator -= interval / buckets;
                TickBucket(_nextBucket, interval);
                _nextBucket = (_nextBucket + 1) % buckets;
            }
        }

        void TickBucket(int bucket, float tickDelta)
        {
            // Buckets stagger *which* agents think on a given step, not how often any one
            // of them does: every agent still gets exactly one tick per brain interval, so
            // needs advance by that interval and nothing else.
            float inGameHours = tickDelta * Clock.HoursPerRealSecond;

            for (int i = 0; i < _agents.Count; i++)
            {
                CreatureAgent agent = _agents[i];
                if (agent == null || !agent.IsActive || agent.BrainBucket != bucket) continue;

                float activity = Settings.ActivityFor(agent.Definition.activity, Clock.NormalisedTime);
                agent.TickNeeds(inGameHours, activity);

                // Growing up runs on real seconds, not on the world clock. The diorama's
                // day is twelve minutes long; if growth followed it a creature would be
                // fully grown before lunch.
                agent.TickGrowth(tickDelta);

                if (agent.IsKnockedOut) continue;

                BehaviourContext ctx = BuildContext(agent, activity);
                CreatureBehaviour next = _brain.Select(in ctx);
                if (next != agent.Current) agent.SwitchTo(next, in ctx);
                agent.Current?.Tick(agent, in ctx, tickDelta);
            }
        }

        BehaviourContext BuildContext(CreatureAgent self, float activity)
        {
            CreatureDefinition def = self.Definition;
            float sight = Settings.baseSightRadius * (1f + def.SizeRank * 0.1f);

            _hash.Query(self.Position, sight, self, _neighbourScratch, Settings.maxNeighbours);

            CreatureAgent threat = null, quarry = null, companion = null;
            float threatDist = float.MaxValue, quarryDist = float.MaxValue, companionDist = float.MaxValue;
            Stance threatStance = Stance.Neutral, quarryStance = Stance.Neutral;

            RelationRuleSet rules = _db.relations;

            for (int i = 0; i < _neighbourScratch.Count; i++)
            {
                CreatureAgent other = _neighbourScratch[i];
                if (other.IsKnockedOut) continue;

                float dist = self.DistanceTo(other);
                Stance mine = rules.Resolve(def, self.RuntimeTags, other.Definition, other.RuntimeTags);
                Stance theirs = rules.Resolve(other.Definition, other.RuntimeTags, def, self.RuntimeTags);

                if (RelationRuleSet.IsAggressive(mine))
                {
                    if (dist < quarryDist)
                    {
                        quarry = other;
                        quarryDist = dist;
                        quarryStance = mine;
                    }
                }
                else if (RelationRuleSet.IsAvoidant(mine) || RelationRuleSet.IsAggressive(theirs))
                {
                    if (dist < threatDist)
                    {
                        threat = other;
                        threatDist = dist;
                        // Being hunted reads as a bigger deal than merely disliking someone.
                        threatStance = RelationRuleSet.IsAggressive(theirs) ? Stance.Predatory : mine;
                    }
                }
                else if (mine is Stance.Friendly or Stance.Curious)
                {
                    if (dist < companionDist)
                    {
                        companion = other;
                        companionDist = dist;
                    }
                }
            }

            FoodNode food = FindFood(self, out float foodDist);

            bool hasWater = false;
            Vector3 waterPoint = Vector3.zero;
            float waterDist = float.MaxValue;
            if (def.lovesWater > 0.05f && Surface != null &&
                Surface.TryFindWaterEdge(self.Position, sight * 1.8f, out waterPoint))
            {
                hasWater = true;
                waterDist = self.DistanceTo(waterPoint);
            }

            return new BehaviourContext(
                self, this, Surface, Settings, activity, Clock.Daylight, _neighbourScratch,
                threat, threatDist, threatStance,
                quarry, quarryDist, quarryStance,
                companion, companionDist,
                food, foodDist,
                hasWater, waterPoint, waterDist, Placements);
        }

        void RunMovement(float dt)
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                CreatureAgent agent = _agents[i];
                if (agent == null || !agent.IsActive) continue;

                Vector3 separation = ComputeSeparation(agent);
                agent.TickMovement(dt, separation, Surface);
            }
        }

        /// <summary>Gentle push away from anyone standing too close, so creatures never
        /// interpenetrate without needing rigidbodies.</summary>
        Vector3 ComputeSeparation(CreatureAgent agent)
        {
            float radius = agent.Definition.bodyRadius * Settings.separationMultiplier;
            int count = _hash.Query(agent.Position, radius, agent, _separationScratch, 6);
            if (count == 0) return Vector3.zero;

            Vector3 push = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                CreatureAgent other = _separationScratch[i];
                Vector3 away = agent.Position - other.Position;
                away.y = 0f;

                float dist = away.magnitude;
                float minDist = agent.Definition.bodyRadius + other.Definition.bodyRadius;
                if (dist < 0.001f)
                {
                    push += new Vector3(Random.value - 0.5f, 0f, Random.value - 0.5f);
                    continue;
                }
                if (dist >= minDist * Settings.separationMultiplier) continue;

                push += away / dist * (1f - dist / (minDist * Settings.separationMultiplier));
            }

            return push * Settings.separationStrength;
        }

        void RegrowFood()
        {
            // Once per in-game quarter hour is plenty; this does not need to be per frame.
            if (Clock.TotalHours - _lastFoodRegrowHours < 0.25) return;
            _lastFoodRegrowHours = Clock.TotalHours;

            for (int i = 0; i < _food.Count; i++)
            {
                if (_food[i] != null) _food[i].Regrow(Clock.TotalHours);
            }
        }
    }
}
