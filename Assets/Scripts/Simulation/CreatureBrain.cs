using LivingDiorama.Simulation.Behaviours;
using UnityEngine;

namespace LivingDiorama.Simulation
{
    /// <summary>
    /// Utility selection. Every behaviour scores itself, the best one wins, and the
    /// incumbent gets a bonus so creatures commit to what they are doing instead of
    /// dithering between two near-equal options every tick.
    /// </summary>
    public sealed class CreatureBrain
    {
        readonly CreatureBehaviour[] _behaviours;
        readonly float[] _scores;

        public CreatureBrain(CreatureBehaviour[] behaviours)
        {
            _behaviours = behaviours;
            _scores = new float[behaviours.Length];
        }

        public static CreatureBrain CreateDefault() => new(new CreatureBehaviour[]
        {
            new IdleBehaviour(),
            new WanderBehaviour(),
            new SleepBehaviour(),
            new SeekFoodBehaviour(),
            new FleeBehaviour(),
            new ChaseBehaviour(),
            new AttackBehaviour(),
            new SocialiseBehaviour(),
            new PlayInWaterBehaviour(),
            new StealFoodBehaviour(),

            // Things the player built. They score above their wild equivalents when
            // one is in reach, which is what makes building one worth doing.
            new SleepInBedBehaviour(),
            new EatFromBowlBehaviour(),
            new TrainAtDummyBehaviour(),
            new PlayDrumBehaviour(),
        });

        public int BehaviourCount => _behaviours.Length;
        public CreatureBehaviour BehaviourAt(int i) => _behaviours[i];
        public float ScoreAt(int i) => _scores[i];

        /// <summary>Pick what the creature should do next. Pure apart from the noise term,
        /// so it can be exercised directly in edit-mode tests.</summary>
        public CreatureBehaviour Select(in BehaviourContext ctx)
        {
            CreatureAgent self = ctx.Self;
            float noise = ctx.Settings.scoreNoise;

            CreatureBehaviour best = null;
            float bestScore = float.MinValue;

            for (int i = 0; i < _behaviours.Length; i++)
            {
                CreatureBehaviour b = _behaviours[i];
                float score = b.Score(in ctx);

                if (score > 0f)
                {
                    // Suppressed behaviours can still win if the situation is dire enough,
                    // they just have to earn it.
                    float cooldown = self.CooldownFor(b);
                    if (cooldown > 0f) score *= Mathf.Clamp01(1f - cooldown / (cooldown + 4f));

                    if (noise > 0f) score += Random.Range(-noise, noise);
                }

                _scores[i] = score;

                if (b == self.Current) score += ctx.Settings.switchHysteresis;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }

            return best;
        }
    }
}
