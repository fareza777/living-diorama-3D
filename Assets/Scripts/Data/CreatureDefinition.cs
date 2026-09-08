using System;
using UnityEngine;

namespace LivingDiorama.Data
{
    /// <summary>
    /// Everything the game knows about a species. Adding a creature to the game is
    /// meant to be exactly this: drop a GLB into StreamingAssets/Creatures (or assign
    /// a prefab), create one of these assets, and add it to a box pool. No code.
    /// </summary>
    [CreateAssetMenu(menuName = "Living Diorama/Creature", fileName = "Creature_")]
    public sealed class CreatureDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used by save files. Never rename after release.")]
        public string id = "new_creature";
        public string displayName = "New Creature";
        [TextArea(2, 4)] public string flavourText = "";
        public Rarity rarity = Rarity.Common;
        public Sprite icon;

        [Header("Model")]
        [Tooltip("Optional authored prefab. Takes priority over the GLB path below.")]
        public GameObject modelPrefab;

        [Tooltip("File name (with extension) inside StreamingAssets/Creatures, e.g. 'goblin.glb'. " +
                 "Loaded at runtime with glTFast so new creatures can ship without a rebuild.")]
        public string streamingModelFile = "";

        [Tooltip("Rescale the imported model so its height matches bodyHeight. Leave on for " +
                 "generated assets, whose scale is arbitrary; turn off for authored prefabs.")]
        public bool autoFitToBodyHeight = true;

        [Tooltip("Uniform scale applied to the imported model. Multiplied on top of the " +
                 "auto-fit scale when auto-fit is enabled.")]
        public float modelScale = 1f;

        [Tooltip("Lift the model so its feet sit on the ground after scaling.")]
        public float groundOffset = 0f;

        [Tooltip("Extra rotation applied to the model, for assets that do not face +Z.")]
        public Vector3 modelEuler = Vector3.zero;

        [Header("Silhouette")]
        public BodySize bodySize = BodySize.Small;
        [Tooltip("Used for spacing, perception and collision avoidance. World units.")]
        public float bodyRadius = 0.28f;
        [Tooltip("Roughly how tall the creature stands. Used to place emote bubbles.")]
        public float bodyHeight = 0.7f;

        [Header("Movement")]
        public LocomotionStyle locomotion = LocomotionStyle.Walk;
        [Min(0f)] public float walkSpeed = 0.7f;
        [Min(0f)] public float runSpeed = 1.9f;
        [Min(1f)] public float turnSpeedDeg = 360f;
        [Tooltip("Vertical bob height of the procedural gait, in body heights.")]
        [Range(0f, 0.5f)] public float gaitBob = 0.09f;
        [Tooltip("Steps per second at walk speed. Drives footfall audio and squash timing.")]
        [Min(0.1f)] public float gaitFrequency = 2.2f;

        [Header("Ecology")]
        public Diet diet = Diet.Omnivore;
        public ActivityCycle activity = ActivityCycle.Diurnal;

        [Tooltip("Free-form traits. Relations between species are derived from these, " +
                 "so a new creature inherits sensible behaviour just by tagging it.")]
        public string[] tags = Array.Empty<string>();

        [Tooltip("Biome ids this creature is happiest in. Affects mood and reward rate.")]
        public string[] preferredBiomes = Array.Empty<string>();

        [Header("Temperament (0..1)")]
        [Range(0f, 1f)] public float aggression = 0.2f;
        [Range(0f, 1f)] public float bravery = 0.5f;
        [Range(0f, 1f)] public float sociability = 0.5f;
        [Range(0f, 1f)] public float curiosity = 0.5f;
        [Tooltip("Likelihood of stealing food and generally causing trouble.")]
        [Range(0f, 1f)] public float mischief = 0.1f;
        [Tooltip("How strongly this creature is drawn to water features.")]
        [Range(0f, 1f)] public float lovesWater = 0.2f;

        [Header("Needs (units per in-game hour)")]
        [Min(0f)] public float hungerRate = 0.09f;
        [Min(0f)] public float energyRate = 0.07f;
        [Min(0f)] public float socialRate = 0.05f;

        [Header("Combat")]
        [Min(1f)] public float maxHealth = 20f;
        [Min(0f)] public float attackDamage = 4f;
        [Min(0.1f)] public float attackInterval = 1.4f;
        [Min(0.1f)] public float attackRange = 0.9f;

        [Header("Economy")]
        [Tooltip("Base coins produced per in-game hour while content.")]
        [Min(0f)] public float coinsPerHour = 6f;
        [Tooltip("Essence awarded the first time this species is collected.")]
        [Min(0f)] public float discoveryEssence = 25f;

        [Header("Audio")]
        public string voiceClipKey = "";
        public string footstepClipKey = "";

        /// <summary>Case-insensitive tag test used everywhere in the simulation.</summary>
        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tags == null) return false;
            for (int i = 0; i < tags.Length; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public bool PrefersBiome(string biomeId)
        {
            if (string.IsNullOrEmpty(biomeId) || preferredBiomes == null) return false;
            for (int i = 0; i < preferredBiomes.Length; i++)
            {
                if (string.Equals(preferredBiomes[i], biomeId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Bigger creatures intimidate smaller ones; used by flee scoring.</summary>
        public int SizeRank => (int)bodySize;

        void OnValidate()
        {
            if (runSpeed < walkSpeed) runSpeed = walkSpeed;
            if (string.IsNullOrWhiteSpace(id)) id = name.ToLowerInvariant();
        }
    }
}
