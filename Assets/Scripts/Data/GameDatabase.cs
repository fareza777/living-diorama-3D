using System.Collections.Generic;
using UnityEngine;

namespace LivingDiorama.Data
{
    /// <summary>
    /// The single asset every system looks content up in. Lives in Resources so the
    /// bootstrapper can load it without a scene reference, and indexes by id so save
    /// files only ever store strings.
    /// </summary>
    [CreateAssetMenu(menuName = "Living Diorama/Game Database", fileName = "GameDatabase")]
    public sealed class GameDatabase : ScriptableObject
    {
        public const string ResourcePath = "GameDatabase";

        [Header("Content")]
        public List<CreatureDefinition> creatures = new();
        public List<BiomeDefinition> biomes = new();
        public List<MysteryBoxDefinition> boxes = new();

        [Header("Rules")]
        public RelationRuleSet relations;
        public SimulationSettings simulation;
        public ProgressionSettings progression;

        Dictionary<string, CreatureDefinition> _creatureIndex;
        Dictionary<string, BiomeDefinition> _biomeIndex;
        Dictionary<string, MysteryBoxDefinition> _boxIndex;

        static GameDatabase _instance;

        public static GameDatabase Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<GameDatabase>(ResourcePath);
                    if (_instance == null)
                    {
                        Debug.LogError($"[GameDatabase] No database at Resources/{ResourcePath}. " +
                                       "Run Living Diorama/Rebuild Content from the editor menu.");
                    }
                    else
                    {
                        _instance.BuildIndex();
                    }
                }
                return _instance;
            }
        }

        /// <summary>Tests and the editor pipeline inject a database without touching Resources.</summary>
        public static void SetInstanceForTesting(GameDatabase db)
        {
            _instance = db;
            if (db != null) db.BuildIndex();
        }

        public void BuildIndex()
        {
            _creatureIndex = new Dictionary<string, CreatureDefinition>(creatures.Count);
            foreach (CreatureDefinition c in creatures)
            {
                if (c == null || string.IsNullOrEmpty(c.id)) continue;
                if (!_creatureIndex.TryAdd(c.id, c))
                {
                    Debug.LogError($"[GameDatabase] duplicate creature id '{c.id}'");
                }
            }

            _biomeIndex = new Dictionary<string, BiomeDefinition>(biomes.Count);
            foreach (BiomeDefinition b in biomes)
            {
                if (b == null || string.IsNullOrEmpty(b.id)) continue;
                if (!_biomeIndex.TryAdd(b.id, b))
                {
                    Debug.LogError($"[GameDatabase] duplicate biome id '{b.id}'");
                }
            }

            _boxIndex = new Dictionary<string, MysteryBoxDefinition>(boxes.Count);
            foreach (MysteryBoxDefinition b in boxes)
            {
                if (b == null || string.IsNullOrEmpty(b.id)) continue;
                if (!_boxIndex.TryAdd(b.id, b))
                {
                    Debug.LogError($"[GameDatabase] duplicate box id '{b.id}'");
                }
            }
        }

        public CreatureDefinition GetCreature(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_creatureIndex == null) BuildIndex();
            return _creatureIndex.GetValueOrDefault(id);
        }

        public BiomeDefinition GetBiome(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_biomeIndex == null) BuildIndex();
            return _biomeIndex.GetValueOrDefault(id);
        }

        public MysteryBoxDefinition GetBox(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_boxIndex == null) BuildIndex();
            return _boxIndex.GetValueOrDefault(id);
        }

        public int CreatureCount => creatures.Count;
    }
}
