using UnityEngine;

namespace LivingDiorama.Data
{
    /// <summary>
    /// Global knobs for the living ecosystem. Kept in one asset so the feel of the
    /// whole diorama can be tuned without recompiling or hunting through prefabs.
    /// </summary>
    [CreateAssetMenu(menuName = "Living Diorama/Simulation Settings", fileName = "SimulationSettings")]
    public sealed class SimulationSettings : ScriptableObject
    {
        [Header("Clock")]
        [Tooltip("Real-time minutes for one full in-game day.")]
        [Min(0.5f)] public float minutesPerDay = 12f;

        [Tooltip("Normalised time of day the sun rises (0 = midnight, 0.5 = noon).")]
        [Range(0f, 1f)] public float dawn = 0.25f;

        [Range(0f, 1f)] public float dusk = 0.78f;

        [Header("Ticking")]
        [Tooltip("Decisions per second. Movement is still smooth every frame; only the "
                 + "brain runs at this rate, which is what keeps 40 creatures cheap.")]
        [Range(2f, 20f)] public float brainTicksPerSecond = 8f;

        [Tooltip("Brains are spread across frames in this many buckets to avoid spikes.")]
        [Range(1, 16)] public int brainBuckets = 4;

        [Header("Perception")]
        [Tooltip("How far a creature can notice another, before per-species modifiers.")]
        [Min(0.5f)] public float baseSightRadius = 4.5f;

        [Tooltip("Cell size of the uniform spatial hash used for neighbour queries.")]
        [Min(0.5f)] public float spatialCellSize = 2f;

        [Tooltip("Maximum neighbours considered per brain tick. Keeps worst case bounded.")]
        [Range(2, 24)] public int maxNeighbours = 8;

        [Header("Decision making")]
        [Tooltip("A new action must beat the current one by this much to interrupt it. "
                 + "Prevents twitchy flip-flopping between near-equal choices.")]
        [Range(0f, 0.5f)] public float switchHysteresis = 0.12f;

        [Tooltip("Random jitter added to every utility score, so identical creatures "
                 + "in identical situations still do different things.")]
        [Range(0f, 0.3f)] public float scoreNoise = 0.06f;

        [Header("Movement")]
        [Tooltip("Repulsion radius multiplier so creatures do not stand inside each other.")]
        [Min(1f)] public float separationMultiplier = 2.2f;

        [Min(0f)] public float separationStrength = 1.4f;

        [Tooltip("Steering acceleration. Higher is snappier, lower is floatier.")]
        [Min(0.5f)] public float steeringAcceleration = 6f;

        [Header("Needs")]
        [Tooltip("Below this the creature actively looks for food.")]
        [Range(0f, 1f)] public float hungerSeekThreshold = 0.45f;

        [Tooltip("Below this the creature looks for somewhere to sleep.")]
        [Range(0f, 1f)] public float energySleepThreshold = 0.3f;

        [Tooltip("Energy restored per in-game hour of sleep.")]
        [Min(0f)] public float sleepRecoveryRate = 0.4f;

        [Tooltip("Hunger restored by one food node.")]
        [Range(0f, 1f)] public float foodRestoreAmount = 0.55f;

        [Header("Conflict")]
        [Tooltip("Seconds a fight can last before both sides disengage.")]
        [Min(1f)] public float maxFightDuration = 12f;

        [Tooltip("A creature that loses a fight is knocked out for this long, then "
                 + "recovers. Nothing ever dies permanently -- this is a cosy game.")]
        [Min(1f)] public float knockoutSeconds = 20f;

        [Tooltip("Distance a fleeing creature tries to put between itself and the threat.")]
        [Min(1f)] public float fleeSafeDistance = 6f;

        [Header("Rewards")]
        [Tooltip("Coins granted for a notable interaction the player might have watched.")]
        [Min(0)] public int interactionCoinReward = 4;

        [Tooltip("Minimum seconds between reward payouts from the same pair of creatures, "
                 + "so two creatures cannot farm the player by bickering forever.")]
        [Min(0f)] public float rewardCooldownSeconds = 45f;

        [Header("Offline")]
        [Tooltip("Hours of away-time that still earn coins. Caps runaway idle income.")]
        [Min(0f)] public float offlineCapHours = 8f;

        [Tooltip("Fraction of normal earnings granted while the game is closed.")]
        [Range(0f, 1f)] public float offlineEfficiency = 0.5f;

        public float SecondsPerDay => Mathf.Max(30f, minutesPerDay * 60f);

        /// <summary>Normalised daylight strength at a given time of day, smoothed at the edges.</summary>
        public float DaylightAt(float normalisedTime)
        {
            float t = Mathf.Repeat(normalisedTime, 1f);
            const float blend = 0.06f;
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(dawn - blend, dawn + blend, t));
            float fall = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(dusk - blend, dusk + blend, t));
            return Mathf.Clamp01(Mathf.Min(rise, fall));
        }

        public bool IsNight(float normalisedTime) => DaylightAt(normalisedTime) < 0.35f;

        /// <summary>How awake a creature of the given cycle should be right now (0..1).</summary>
        public float ActivityFor(ActivityCycle cycle, float normalisedTime)
        {
            float day = DaylightAt(normalisedTime);
            return cycle switch
            {
                ActivityCycle.Diurnal => Mathf.Lerp(0.15f, 1f, day),
                ActivityCycle.Nocturnal => Mathf.Lerp(0.15f, 1f, 1f - day),
                ActivityCycle.Crepuscular => Mathf.Lerp(0.25f, 1f, 1f - Mathf.Abs(day - 0.5f) * 2f),
                _ => 1f,
            };
        }

        void OnValidate()
        {
            if (dusk <= dawn) dusk = Mathf.Min(1f, dawn + 0.4f);
        }
    }
}
