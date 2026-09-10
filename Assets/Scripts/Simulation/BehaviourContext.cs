using System.Collections.Generic;
using LivingDiorama.Data;
using UnityEngine;

namespace LivingDiorama.Simulation
{
    /// <summary>
    /// Everything a behaviour needs to score itself, gathered once per brain tick so
    /// ten behaviours do not each run their own neighbour query.
    /// </summary>
    public readonly struct BehaviourContext
    {
        public readonly CreatureAgent Self;
        public readonly EcosystemSimulation World;
        public readonly IWorldSurface Surface;
        public readonly SimulationSettings Settings;

        /// <summary>How awake this creature should be right now, from its activity cycle.</summary>
        public readonly float Activity;
        public readonly float Daylight;

        public readonly List<CreatureAgent> Neighbours;

        /// <summary>Nearest creature this one wants to get away from.</summary>
        public readonly CreatureAgent Threat;
        public readonly float ThreatDistance;
        public readonly Stance ThreatStance;

        /// <summary>Nearest creature this one wants to chase or fight.</summary>
        public readonly CreatureAgent Quarry;
        public readonly float QuarryDistance;
        public readonly Stance QuarryStance;

        /// <summary>Nearest creature this one is happy to be around.</summary>
        public readonly CreatureAgent Companion;
        public readonly float CompanionDistance;

        /// <summary>What the player has built, and where. Null in the flat stub the
        /// brain is unit tested against.</summary>
        public readonly Diorama.PlacementService Placements;

        public readonly FoodNode Food;
        public readonly float FoodDistance;

        public readonly bool HasWater;
        public readonly Vector3 WaterPoint;
        public readonly float WaterDistance;

        public BehaviourContext(
            CreatureAgent self, EcosystemSimulation world, IWorldSurface surface,
            SimulationSettings settings, float activity, float daylight,
            List<CreatureAgent> neighbours,
            CreatureAgent threat, float threatDistance, Stance threatStance,
            CreatureAgent quarry, float quarryDistance, Stance quarryStance,
            CreatureAgent companion, float companionDistance,
            FoodNode food, float foodDistance,
            bool hasWater, Vector3 waterPoint, float waterDistance,
            Diorama.PlacementService placements = null)
        {
            Self = self;
            World = world;
            Surface = surface;
            Settings = settings;
            Activity = activity;
            Daylight = daylight;
            Neighbours = neighbours;
            Threat = threat;
            ThreatDistance = threatDistance;
            ThreatStance = threatStance;
            Quarry = quarry;
            QuarryDistance = quarryDistance;
            QuarryStance = quarryStance;
            Companion = companion;
            CompanionDistance = companionDistance;
            Food = food;
            FoodDistance = foodDistance;
            HasWater = hasWater;
            WaterPoint = waterPoint;
            WaterDistance = waterDistance;
            Placements = placements;
        }

        public CreatureDefinition Def => Self.Definition;

        /// <summary>Falls off from 1 at zero distance to 0 at <paramref name="range"/>.</summary>
        public static float Proximity(float distance, float range)
        {
            if (range <= 0.001f) return 0f;
            return Mathf.Clamp01(1f - distance / range);
        }
    }

    /// <summary>
    /// A single thing a creature can be doing. Behaviours are stateless singletons:
    /// all mutable state lives on the agent, so one instance serves every creature and
    /// the simulation never allocates while running.
    /// </summary>
    public abstract class CreatureBehaviour
    {
        public abstract string Id { get; }

        /// <summary>Seconds this behaviour is suppressed for after it ends. Stops a creature
        /// immediately re-entering the thing it just gave up on.</summary>
        public virtual float CooldownAfterExit => 0f;

        /// <summary>Utility in 0..1. The brain picks the highest, with hysteresis.</summary>
        public abstract float Score(in BehaviourContext ctx);

        public virtual void OnEnter(CreatureAgent self, in BehaviourContext ctx) { }

        /// <summary>Runs at the brain tick rate, not per frame.</summary>
        public abstract void Tick(CreatureAgent self, in BehaviourContext ctx, float dt);

        public virtual void OnExit(CreatureAgent self) { }
    }
}
