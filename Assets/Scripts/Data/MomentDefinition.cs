using UnityEngine;

namespace LivingDiorama.Data
{
    /// <summary>When a moment is allowed to happen.</summary>
    public enum TimeRequirement
    {
        Any,
        Day,
        Night,
    }

    /// <summary>
    /// A named thing that can happen in the diorama, and the conditions that produce it.
    ///
    /// This is the piece that turns the simulation into a game. Creatures were already
    /// stealing, hunting and befriending each other, but nothing said which of those was
    /// worth seeing, so watching had no shape: the player collected creatures and then
    /// had no reason to care which ones lived together. A moment names a combination --
    /// a wolf catching a thief, a slime dancing in a puddle -- and the Chronicle turns
    /// the set of them into something to go looking for.
    ///
    /// Deliberately data rather than code: a new moment is an asset, not a branch.
    /// </summary>
    [CreateAssetMenu(menuName = "Living Diorama/Moment", fileName = "Moment")]
    public sealed class MomentDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string title;

        [TextArea] public string flavour;

        /// <summary>Shown while the moment is still undiscovered. Should say enough to
        /// suggest an experiment and not enough to remove the surprise.</summary>
        [TextArea] public string hint;

        [Header("Conditions")]
        public Simulation.SimEventKind trigger;

        [Tooltip("Species that must be doing it. Empty means anyone.")]
        public string actorSpecies;

        [Tooltip("Species it must be done to. Empty means anyone.")]
        public string targetSpecies;

        public TimeRequirement time = TimeRequirement.Any;

        [Header("Reward")]
        public int essence = 20;
        public Rarity prestige = Rarity.Common;

        /// <summary>Does this event, at this hour, count as this moment?</summary>
        public bool Matches(in Simulation.SimEvent e, bool isNight)
        {
            if (e.Kind != trigger) return false;

            if (!string.IsNullOrEmpty(actorSpecies))
            {
                if (e.Actor == null || e.Actor.Definition == null) return false;
                if (e.Actor.Definition.id != actorSpecies) return false;
            }

            if (!string.IsNullOrEmpty(targetSpecies))
            {
                if (e.Target == null || e.Target.Definition == null) return false;
                if (e.Target.Definition.id != targetSpecies) return false;
            }

            return time switch
            {
                TimeRequirement.Day => !isNight,
                TimeRequirement.Night => isNight,
                _ => true,
            };
        }
    }
}
