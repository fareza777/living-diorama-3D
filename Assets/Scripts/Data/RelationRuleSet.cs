using System;
using System.Collections.Generic;
using UnityEngine;

namespace LivingDiorama.Data
{
    /// <summary>
    /// Tag-driven social rules. Instead of an N x N species matrix that has to be
    /// re-authored every time a creature is added, stances are derived from traits:
    /// "anything tagged predator hunts anything tagged prey" covers the wolf hunting
    /// a goblin today and hunting a rabbit that ships next month.
    /// </summary>
    [CreateAssetMenu(menuName = "Living Diorama/Relation Rules", fileName = "RelationRules")]
    public sealed class RelationRuleSet : ScriptableObject
    {
        [Serializable]
        public struct Rule
        {
            [Tooltip("Tag the observer must have. Empty means 'any'.")]
            public string selfTag;

            [Tooltip("Tag the observed creature must have. Empty means 'any'.")]
            public string otherTag;

            public Stance stance;

            [Tooltip("Higher priority wins when several rules match. Ties go to the later rule.")]
            public int priority;

            [Tooltip("Skip this rule unless the observer is at least this brave (0..1).")]
            [Range(0f, 1f)] public float minBravery;

            [Tooltip("Skip this rule unless the observer is at least this aggressive (0..1).")]
            [Range(0f, 1f)] public float minAggression;
        }

        [Serializable]
        public struct Override
        {
            public string selfSpeciesId;
            public string otherSpeciesId;
            public Stance stance;
        }

        [Tooltip("Evaluated in order; highest priority match wins.")]
        public List<Rule> rules = new();

        [Tooltip("Explicit species-to-species results that beat every rule. Use sparingly, " +
                 "only for signature moments the designer wants guaranteed.")]
        public List<Override> overrides = new();

        [Tooltip("Fallback when nothing matches.")]
        public Stance defaultStance = Stance.Neutral;

        [Tooltip("A creature this many size ranks larger is feared regardless of tags.")]
        [Min(1)] public int intimidationSizeGap = 2;

        /// <summary>
        /// Resolve how <paramref name="self"/> regards <paramref name="other"/>.
        /// Pure function of the two definitions, so it is trivially unit testable.
        /// </summary>
        public Stance Resolve(CreatureDefinition self, CreatureDefinition other)
            => Resolve(self, null, other, null);

        static bool Tagged(CreatureDefinition def, IReadOnlyList<string> extra, string tag)
        {
            if (def != null && def.HasTag(tag)) return true;
            if (extra == null) return false;
            for (int i = 0; i < extra.Count; i++)
            {
                if (string.Equals(extra[i], tag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Stance resolution including transient runtime tags. A goblin that just stole
        /// food picks up a temporary "thief" tag, which is what turns the knight hostile
        /// without any of that being hard-coded against goblins specifically.
        /// </summary>
        public Stance Resolve(CreatureDefinition self, IReadOnlyList<string> selfRuntimeTags,
                              CreatureDefinition other, IReadOnlyList<string> otherRuntimeTags)
        {
            if (self == null || other == null) return defaultStance;

            for (int i = 0; i < overrides.Count; i++)
            {
                Override o = overrides[i];
                if (string.Equals(o.selfSpeciesId, self.id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(o.otherSpeciesId, other.id, StringComparison.OrdinalIgnoreCase))
                {
                    return o.stance;
                }
            }

            Stance best = defaultStance;
            int bestPriority = int.MinValue;
            bool matched = false;

            for (int i = 0; i < rules.Count; i++)
            {
                Rule r = rules[i];
                if (self.bravery < r.minBravery) continue;
                if (self.aggression < r.minAggression) continue;
                if (!string.IsNullOrEmpty(r.selfTag) && !Tagged(self, selfRuntimeTags, r.selfTag)) continue;
                if (!string.IsNullOrEmpty(r.otherTag) && !Tagged(other, otherRuntimeTags, r.otherTag)) continue;

                if (r.priority >= bestPriority)
                {
                    bestPriority = r.priority;
                    best = r.stance;
                    matched = true;
                }
            }

            // Raw size intimidation. A dragon does not need a rule to scare a slime.
            int gap = other.SizeRank - self.SizeRank;
            if (gap >= intimidationSizeGap && best != Stance.Predatory && best != Stance.Hostile)
            {
                // Brave creatures downgrade fear to wariness instead of bolting.
                return self.bravery >= 0.75f ? Stance.Wary : Stance.Fearful;
            }

            return matched ? best : defaultStance;
        }

        /// <summary>True when the stance makes the observer want to close distance aggressively.</summary>
        public static bool IsAggressive(Stance s) => s == Stance.Hostile || s == Stance.Predatory;

        /// <summary>True when the stance makes the observer want to increase distance.</summary>
        public static bool IsAvoidant(Stance s) => s == Stance.Fearful || s == Stance.Wary;
    }
}
