using System.Collections.Generic;
using System.IO;
using LivingDiorama.Data;
using UnityEditor;
using UnityEngine;

namespace LivingDiorama.EditorTools
{
    /// <summary>
    /// Authors every content asset from code.
    ///
    /// ScriptableObjects are the right runtime shape -- designers can open and tweak them
    /// -- but hand-created .asset files are unreviewable YAML and drift silently. Keeping
    /// the canonical values here means content changes show up as readable diffs, and a
    /// fresh checkout can rebuild the entire game database with one menu item.
    ///
    /// Re-running is safe: existing assets are updated in place, so ids and references
    /// survive and nobody's save file breaks.
    /// </summary>
    public static class ContentBuilder
    {
        const string DataRoot = "Assets/Data";
        const string ResourceRoot = "Assets/Resources";

        [MenuItem("Living Diorama/Rebuild Content", priority = 0)]
        public static void RebuildContent()
        {
            EnsureFolders();

            SimulationSettings simulation = BuildSimulationSettings();
            ProgressionSettings progression = BuildProgressionSettings();
            RelationRuleSet relations = BuildRelationRules();

            Dictionary<string, CreatureDefinition> creatures = BuildCreatures();
            List<BiomeDefinition> biomes = BuildBiomes();
            List<MysteryBoxDefinition> boxes = BuildBoxes(creatures);

            GameDatabase db = CreateOrLoad<GameDatabase>($"{ResourceRoot}/GameDatabase.asset");
            db.simulation = simulation;
            db.progression = progression;
            db.relations = relations;
            db.creatures = new List<CreatureDefinition>(creatures.Values);
            db.biomes = biomes;
            db.boxes = boxes;
            db.BuildIndex();
            EditorUtility.SetDirty(db);

            // The starting loadout has to reference creatures, so it is filled in last.
            progression.startingCreatures = new[]
            {
                new ProgressionSettings.StartingGrant { creatureId = "goblin", count = 1 },
                new ProgressionSettings.StartingGrant { creatureId = "slime", count = 2 },
            };
            EditorUtility.SetDirty(progression);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ContentBuilder] rebuilt: {db.creatures.Count} creatures, " +
                      $"{db.biomes.Count} biomes, {db.boxes.Count} boxes");
        }

        // ---- helpers --------------------------------------------------------

        static void EnsureFolders()
        {
            foreach (string path in new[]
                     {
                         DataRoot,
                         $"{DataRoot}/Creatures",
                         $"{DataRoot}/Biomes",
                         $"{DataRoot}/Boxes",
                         ResourceRoot,
                         $"{ResourceRoot}/UI",
                         "Assets/StreamingAssets/Creatures",
                     })
            {
                if (Directory.Exists(path)) continue;
                Directory.CreateDirectory(path);
            }
            AssetDatabase.Refresh();
        }

        static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        /// <summary>Import the Meshy render thumbnail as a sprite so the collection grid
        /// has real portraits without anyone opening the import settings.</summary>
        static Sprite LoadPortrait(string creatureId)
        {
            string path = $"Assets/Art/Creatures/{creatureId}/{creatureId}_thumb.png";
            if (!File.Exists(path)) return null;

            if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 256;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>The colour map lifted out of the creature's GLB by
        /// Tools/extract_glb_textures.py. Without it a rigged model renders grey.</summary>
        static Texture2D LoadAlbedo(string creatureId)
        {
            foreach (string extension in new[] { "jpg", "png" })
            {
                string path = $"Assets/Art/Creatures/{creatureId}/{creatureId}_albedo.{extension}";
                if (!File.Exists(path)) continue;

                if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                    importer.textureType != TextureImporterType.Default)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.mipmapEnabled = true;
                    importer.maxTextureSize = 1024;
                    importer.SaveAndReimport();
                }

                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }

        static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

        // ---- settings -------------------------------------------------------

        static SimulationSettings BuildSimulationSettings()
        {
            SimulationSettings s = CreateOrLoad<SimulationSettings>($"{DataRoot}/SimulationSettings.asset");

            // Twelve real minutes per in-game day: long enough that day and night both
            // feel like states you sit inside, short enough to see both in one session.
            s.minutesPerDay = 12f;
            s.dawn = 0.24f;
            s.dusk = 0.78f;

            s.brainTicksPerSecond = 8f;
            s.brainBuckets = 4;

            s.baseSightRadius = 4.5f;
            s.spatialCellSize = 2f;
            s.maxNeighbours = 8;

            s.switchHysteresis = 0.12f;
            s.scoreNoise = 0.06f;

            s.separationMultiplier = 2.2f;
            s.separationStrength = 1.4f;
            s.steeringAcceleration = 6f;

            s.hungerSeekThreshold = 0.45f;
            s.energySleepThreshold = 0.30f;
            s.sleepRecoveryRate = 0.40f;
            s.foodRestoreAmount = 0.55f;

            s.maxFightDuration = 12f;
            s.knockoutSeconds = 20f;
            s.fleeSafeDistance = 6f;

            s.interactionCoinReward = 4;
            s.rewardCooldownSeconds = 45f;

            s.offlineCapHours = 8f;
            s.offlineEfficiency = 0.5f;

            EditorUtility.SetDirty(s);
            return s;
        }

        static ProgressionSettings BuildProgressionSettings()
        {
            ProgressionSettings p = CreateOrLoad<ProgressionSettings>($"{DataRoot}/ProgressionSettings.asset");

            p.tileSize = 6f;
            p.startingTilesPerSide = 1;
            p.maxRingRadius = 3;

            p.baseTileCost = 250;
            p.tileCostRingMultiplier = 1.85f;

            p.creaturesPerTile = 5;
            p.hardPopulationCap = 32;

            p.baseXpPerLevel = 120;
            p.xpCurveExponent = 1.35f;
            p.maxLevel = 60;

            p.xpPerDiscovery = 40;
            p.xpPerBoxOpen = 8;
            p.xpPerInteraction = 2;
            p.xpPerTileUnlock = 60;

            p.startingCoins = 350;
            p.startingEssence = 0;
            p.startingBoxKeys = 1;

            p.interstitialEveryNBoxOpens = 6;
            p.interstitialMinSecondsBetween = 240;
            p.rewardedAdCoins = 150;
            p.rewardedOfflineMultiplier = 2f;

            EditorUtility.SetDirty(p);
            return p;
        }

        // ---- relations ------------------------------------------------------

        /// <summary>
        /// The social rules of the whole game, expressed against traits rather than
        /// species. Every scripted moment in the design brief falls out of these:
        /// the wolf hunts the goblin because goblins are tagged mischief and wolves are
        /// tagged predator, and the knight attacks whoever most recently stole food
        /// because theft grants a temporary "thief" tag.
        /// </summary>
        static RelationRuleSet BuildRelationRules()
        {
            RelationRuleSet r = CreateOrLoad<RelationRuleSet>($"{DataRoot}/RelationRules.asset");

            r.defaultStance = Stance.Neutral;
            r.intimidationSizeGap = 2;
            r.overrides.Clear();
            r.rules = new List<RelationRuleSet.Rule>
            {
                // --- predation ------------------------------------------------
                Rule("predator", "critter", Stance.Predatory, 12),
                Rule("predator", "mischief", Stance.Predatory, 10),

                // --- law and order --------------------------------------------
                // Theft is the strongest signal in the game: it overrides everything.
                Rule("lawful", "thief", Stance.Hostile, 40),
                Rule("lawful", "undead", Stance.Hostile, 25),
                Rule("lawful", "mischief", Stance.Hostile, 14, minAggression: 0.4f),

                // --- self preservation ----------------------------------------
                Rule("mischief", "lawful", Stance.Fearful, 12),
                Rule("mischief", "predator", Stance.Fearful, 12),
                Rule("critter", "predator", Stance.Fearful, 14),
                Rule("", "apex", Stance.Fearful, 18),

                // --- the restless dead ----------------------------------------
                Rule("undead", "living", Stance.Hostile, 10, minAggression: 0.3f),
                Rule("living", "undead", Stance.Wary, 6),

                // --- company ---------------------------------------------------
                Rule("critter", "critter", Stance.Friendly, 4),
                Rule("pack", "pack", Stance.Friendly, 6),
                Rule("humanoid", "humanoid", Stance.Curious, 2),
            };

            EditorUtility.SetDirty(r);
            return r;
        }

        static RelationRuleSet.Rule Rule(string self, string other, Stance stance, int priority,
                                         float minBravery = 0f, float minAggression = 0f) => new()
        {
            selfTag = self,
            otherTag = other,
            stance = stance,
            priority = priority,
            minBravery = minBravery,
            minAggression = minAggression,
        };

        // ---- creatures ------------------------------------------------------

        static Dictionary<string, CreatureDefinition> BuildCreatures()
        {
            var map = new Dictionary<string, CreatureDefinition>();

            map["goblin"] = Creature("goblin", "Goblin", Rarity.Common,
                "Small, quick, and utterly without shame. Leave food out at your peril.",
                tags: new[] { "humanoid", "mischief", "small", "living" },
                locomotion: LocomotionStyle.Scurry, size: BodySize.Small,
                bodyRadius: 0.26f, bodyHeight: 0.72f,
                walk: 0.85f, run: 2.3f, gaitBob: 0.11f, gaitFrequency: 2.9f,
                diet: Diet.Omnivore, activity: ActivityCycle.Diurnal,
                aggression: 0.30f, bravery: 0.35f, sociability: 0.55f,
                curiosity: 0.85f, mischief: 0.90f, lovesWater: 0.15f,
                health: 16f, damage: 3f, attackRange: 0.75f,
                coinsPerHour: 7f, discoveryEssence: 20f,
                biomes: new[] { "forest", "cave" });

            map["slime"] = Creature("slime", "Slime", Rarity.Common,
                "A contented blob. Its ambitions extend no further than the nearest puddle.",
                tags: new[] { "blob", "critter", "small", "living", "harmless" },
                locomotion: LocomotionStyle.Hop, size: BodySize.Tiny,
                bodyRadius: 0.22f, bodyHeight: 0.44f,
                walk: 0.5f, run: 1.1f, gaitBob: 0.16f, gaitFrequency: 1.5f,
                diet: Diet.Mineral, activity: ActivityCycle.Always,
                aggression: 0.03f, bravery: 0.25f, sociability: 0.80f,
                curiosity: 0.60f, mischief: 0.05f, lovesWater: 0.95f,
                health: 12f, damage: 1f, attackRange: 0.5f,
                coinsPerHour: 5f, discoveryEssence: 15f,
                biomes: new[] { "forest", "river" });

            map["wolf"] = Creature("wolf", "Timber Wolf", Rarity.Uncommon,
                "Patient, focused, and always aware of exactly where the goblin is.",
                tags: new[] { "beast", "predator", "pack", "living" },
                locomotion: LocomotionStyle.Trot, size: BodySize.Medium,
                bodyRadius: 0.34f, bodyHeight: 0.78f,
                walk: 0.95f, run: 2.9f, gaitBob: 0.07f, gaitFrequency: 2.4f,
                diet: Diet.Carnivore, activity: ActivityCycle.Crepuscular,
                aggression: 0.72f, bravery: 0.80f, sociability: 0.65f,
                curiosity: 0.55f, mischief: 0.10f, lovesWater: 0.30f,
                health: 34f, damage: 7f, attackRange: 0.95f,
                coinsPerHour: 12f, discoveryEssence: 45f,
                biomes: new[] { "forest", "snow" });

            map["knight"] = Creature("knight", "Knight", Rarity.Rare,
                "Sworn to keep the peace. Interprets 'the peace' quite broadly.",
                tags: new[] { "humanoid", "lawful", "guardian", "armoured", "living" },
                locomotion: LocomotionStyle.Stomp, size: BodySize.Medium,
                bodyRadius: 0.36f, bodyHeight: 0.95f,
                walk: 0.65f, run: 1.7f, gaitBob: 0.06f, gaitFrequency: 1.7f,
                diet: Diet.Omnivore, activity: ActivityCycle.Diurnal,
                aggression: 0.48f, bravery: 0.95f, sociability: 0.45f,
                curiosity: 0.35f, mischief: 0.0f, lovesWater: 0.10f,
                health: 55f, damage: 9f, attackRange: 1.1f,
                coinsPerHour: 18f, discoveryEssence: 90f,
                biomes: new[] { "forest", "dungeon" });

            map["skeleton"] = Creature("skeleton", "Skeleton", Rarity.Rare,
                "Sleeps through the day out of habit rather than need. Lively after dark.",
                tags: new[] { "undead", "humanoid", "bones" },
                locomotion: LocomotionStyle.Walk, size: BodySize.Medium,
                bodyRadius: 0.30f, bodyHeight: 0.88f,
                walk: 0.72f, run: 1.9f, gaitBob: 0.09f, gaitFrequency: 2.0f,
                diet: Diet.Souls, activity: ActivityCycle.Nocturnal,
                aggression: 0.55f, bravery: 0.70f, sociability: 0.25f,
                curiosity: 0.40f, mischief: 0.25f, lovesWater: 0.0f,
                health: 30f, damage: 6f, attackRange: 0.9f,
                coinsPerHour: 16f, discoveryEssence: 95f,
                biomes: new[] { "graveyard", "dungeon", "cave" });

            map["dragon"] = Creature("dragon", "Dragon Whelp", Rarity.Legendary,
                "Barely bigger than the knight, and the entire diorama defers to it anyway.",
                tags: new[] { "dragon", "apex", "beast", "winged", "living" },
                locomotion: LocomotionStyle.Float, size: BodySize.Large,
                bodyRadius: 0.42f, bodyHeight: 1.05f,
                walk: 0.8f, run: 2.2f, gaitBob: 0.10f, gaitFrequency: 1.2f,
                diet: Diet.Carnivore, activity: ActivityCycle.Always,
                aggression: 0.60f, bravery: 1.0f, sociability: 0.20f,
                curiosity: 0.50f, mischief: 0.30f, lovesWater: 0.20f,
                health: 90f, damage: 14f, attackRange: 1.3f,
                coinsPerHour: 40f, discoveryEssence: 300f,
                biomes: new[] { "lava", "cave" });

            return map;
        }

        static CreatureDefinition Creature(
            string id, string displayName, Rarity rarity, string flavour,
            string[] tags, LocomotionStyle locomotion, BodySize size,
            float bodyRadius, float bodyHeight, float walk, float run,
            float gaitBob, float gaitFrequency, Diet diet, ActivityCycle activity,
            float aggression, float bravery, float sociability, float curiosity,
            float mischief, float lovesWater, float health, float damage, float attackRange,
            float coinsPerHour, float discoveryEssence, string[] biomes)
        {
            CreatureDefinition c = CreateOrLoad<CreatureDefinition>($"{DataRoot}/Creatures/Creature_{id}.asset");

            c.id = id;
            c.displayName = displayName;
            c.rarity = rarity;
            c.flavourText = flavour;
            c.icon = LoadPortrait(id);
            c.albedo = LoadAlbedo(id);

            // Models are loaded from StreamingAssets rather than referenced as prefabs, so
            // a new creature is a file drop plus one asset -- no rebuild, no code.
            c.modelPrefab = null;
            c.streamingModelFile = $"{id}.glb";
            c.autoFitToBodyHeight = true;
            c.modelScale = 1f;
            c.groundOffset = 0f;
            c.modelEuler = Vector3.zero;

            c.bodySize = size;
            c.bodyRadius = bodyRadius;
            c.bodyHeight = bodyHeight;

            c.locomotion = locomotion;
            c.walkSpeed = walk;
            c.runSpeed = run;
            c.turnSpeedDeg = 420f;
            c.gaitBob = gaitBob;
            c.gaitFrequency = gaitFrequency;

            c.diet = diet;
            c.activity = activity;
            c.tags = tags;
            c.preferredBiomes = biomes;

            c.aggression = aggression;
            c.bravery = bravery;
            c.sociability = sociability;
            c.curiosity = curiosity;
            c.mischief = mischief;
            c.lovesWater = lovesWater;

            c.hungerRate = 0.09f;
            c.energyRate = 0.07f;
            c.socialRate = 0.05f;

            c.maxHealth = health;
            c.attackDamage = damage;
            c.attackInterval = 1.4f;
            c.attackRange = attackRange;

            c.coinsPerHour = coinsPerHour;
            c.discoveryEssence = discoveryEssence;

            EditorUtility.SetDirty(c);
            return c;
        }

        // ---- biomes ---------------------------------------------------------

        static List<BiomeDefinition> BuildBiomes()
        {
            var list = new List<BiomeDefinition>
            {
                Biome("forest", "Forest Glade", 1, 0,
                    "Mossy ground, a slow stream, and more berries than anyone needs.",
                    low: Rgb(56, 96, 48), high: Rgb(104, 148, 68), cliff: Rgb(92, 84, 74),
                    relief: 0.26f, reliefScale: 0.42f,
                    water: true, waterLevel: -0.14f,
                    shallow: new Color(0.36f, 0.76f, 0.72f, 0.62f),
                    deep: new Color(0.08f, 0.30f, 0.40f, 0.90f),
                    fog: Rgb(178, 202, 214), fogDensity: 0.014f,
                    foodNodes: 4, diets: new[] { Diet.Herbivore, Diet.Omnivore, Diet.Carnivore },
                    scatter: new[]
                    {
                        Scatter(BiomeDefinition.PropKind.PineTree, 0.045f, 0.85f, 1.25f, 1.7f,
                                Rgb(48, 104, 62), Rgb(84, 62, 44), wind: true),
                        Scatter(BiomeDefinition.PropKind.BroadleafTree, 0.030f, 0.9f, 1.3f, 1.9f,
                                Rgb(86, 150, 66), Rgb(94, 70, 48), wind: true),
                        Scatter(BiomeDefinition.PropKind.Bush, 0.10f, 0.7f, 1.2f, 0.7f,
                                Rgb(64, 122, 58), Rgb(52, 100, 48), wind: true),
                        Scatter(BiomeDefinition.PropKind.GrassTuft, 0.55f, 0.6f, 1.15f, 0.25f,
                                Rgb(94, 150, 62), Rgb(150, 190, 88), wind: true),
                        Scatter(BiomeDefinition.PropKind.Rock, 0.05f, 0.6f, 1.3f, 0.8f,
                                Rgb(112, 106, 98), Rgb(88, 84, 78)),
                        Scatter(BiomeDefinition.PropKind.Mushroom, 0.055f, 0.8f, 1.3f, 0.45f,
                                Rgb(196, 76, 66), Rgb(226, 214, 196)),
                    }),

                Biome("river", "River Bend", 2, 200,
                    "Wide shallows and smooth stones. Slimes will not leave.",
                    low: Rgb(72, 106, 62), high: Rgb(126, 158, 90), cliff: Rgb(104, 100, 92),
                    relief: 0.20f, reliefScale: 0.36f,
                    water: true, waterLevel: -0.08f,
                    shallow: new Color(0.42f, 0.82f, 0.80f, 0.58f),
                    deep: new Color(0.06f, 0.34f, 0.46f, 0.92f),
                    fog: Rgb(190, 212, 222), fogDensity: 0.012f,
                    foodNodes: 3, diets: new[] { Diet.Herbivore, Diet.Omnivore, Diet.Mineral },
                    scatter: new[]
                    {
                        Scatter(BiomeDefinition.PropKind.GrassTuft, 0.75f, 0.7f, 1.3f, 0.22f,
                                Rgb(104, 158, 74), Rgb(168, 202, 104), wind: true),
                        Scatter(BiomeDefinition.PropKind.Rock, 0.16f, 0.5f, 1.4f, 0.55f,
                                Rgb(126, 122, 116), Rgb(96, 94, 90), allowInWater: true),
                        Scatter(BiomeDefinition.PropKind.BroadleafTree, 0.016f, 1.0f, 1.4f, 2.2f,
                                Rgb(96, 156, 72), Rgb(96, 72, 50), wind: true),
                        Scatter(BiomeDefinition.PropKind.Bush, 0.07f, 0.7f, 1.1f, 0.8f,
                                Rgb(74, 132, 62), Rgb(60, 110, 54), wind: true),
                    }),

                Biome("cave", "Crystal Cave", 4, 900,
                    "Cool, quiet, and glittering. Something down here eats the minerals.",
                    low: Rgb(48, 46, 60), high: Rgb(78, 74, 92), cliff: Rgb(58, 56, 68),
                    relief: 0.34f, reliefScale: 0.55f,
                    water: false, waterLevel: -0.2f,
                    shallow: new Color(0.3f, 0.6f, 0.7f, 0.7f),
                    deep: new Color(0.05f, 0.18f, 0.30f, 0.95f),
                    fog: Rgb(46, 48, 66), fogDensity: 0.05f,
                    foodNodes: 3, diets: new[] { Diet.Mineral, Diet.Carnivore, Diet.Souls },
                    ambientLift: 0.35f, nocturnalAffinity: 0.5f,
                    scatter: new[]
                    {
                        Scatter(BiomeDefinition.PropKind.Rock, 0.22f, 0.7f, 1.8f, 0.6f,
                                Rgb(74, 72, 84), Rgb(56, 54, 66)),
                        Scatter(BiomeDefinition.PropKind.Crystal, 0.12f, 0.8f, 1.8f, 0.75f,
                                Rgb(126, 186, 226), Rgb(88, 140, 200)),
                        Scatter(BiomeDefinition.PropKind.Mushroom, 0.09f, 0.9f, 1.6f, 0.5f,
                                Rgb(112, 196, 178), Rgb(214, 220, 226)),
                    }),

                Biome("graveyard", "Old Graveyard", 6, 1800,
                    "Nothing happens here all day. Then the sun goes down.",
                    low: Rgb(62, 66, 58), high: Rgb(92, 96, 84), cliff: Rgb(86, 86, 82),
                    relief: 0.22f, reliefScale: 0.4f,
                    water: false, waterLevel: -0.2f,
                    shallow: new Color(0.3f, 0.5f, 0.5f, 0.7f),
                    deep: new Color(0.05f, 0.18f, 0.20f, 0.95f),
                    fog: Rgb(112, 122, 118), fogDensity: 0.042f,
                    foodNodes: 2, diets: new[] { Diet.Souls, Diet.Omnivore },
                    nocturnalAffinity: 0.9f,
                    scatter: new[]
                    {
                        Scatter(BiomeDefinition.PropKind.Rock, 0.26f, 0.6f, 1.5f, 0.5f,
                                Rgb(138, 138, 132), Rgb(108, 108, 104)),
                        Scatter(BiomeDefinition.PropKind.PineTree, 0.02f, 0.9f, 1.4f, 2.0f,
                                Rgb(52, 62, 52), Rgb(58, 50, 44), wind: true),
                        Scatter(BiomeDefinition.PropKind.GrassTuft, 0.32f, 0.5f, 1.0f, 0.3f,
                                Rgb(84, 92, 70), Rgb(120, 126, 96), wind: true),
                    }),

                Biome("dungeon", "Sunken Dungeon", 8, 3200,
                    "Flagstones, old iron, and a draught from somewhere below.",
                    low: Rgb(64, 62, 66), high: Rgb(96, 94, 98), cliff: Rgb(72, 70, 74),
                    relief: 0.10f, reliefScale: 0.7f,
                    water: false, waterLevel: -0.2f,
                    shallow: new Color(0.3f, 0.5f, 0.5f, 0.7f),
                    deep: new Color(0.05f, 0.15f, 0.20f, 0.95f),
                    fog: Rgb(58, 58, 68), fogDensity: 0.048f,
                    foodNodes: 2, diets: new[] { Diet.Souls, Diet.Carnivore, Diet.Omnivore },
                    ambientLift: 0.28f, nocturnalAffinity: 0.6f,
                    scatter: new[]
                    {
                        Scatter(BiomeDefinition.PropKind.Rock, 0.30f, 0.5f, 1.2f, 0.5f,
                                Rgb(104, 102, 108), Rgb(82, 80, 86)),
                        Scatter(BiomeDefinition.PropKind.Crystal, 0.05f, 0.6f, 1.1f, 0.9f,
                                Rgb(206, 150, 96), Rgb(160, 108, 70)),
                    }),

                Biome("snow", "Snowfield", 10, 4500,
                    "Bright, silent, and colder than anything here is dressed for.",
                    low: Rgb(206, 216, 232), high: Rgb(238, 244, 252), cliff: Rgb(150, 160, 178),
                    relief: 0.30f, reliefScale: 0.38f,
                    water: false, waterLevel: -0.2f,
                    shallow: new Color(0.6f, 0.82f, 0.88f, 0.6f),
                    deep: new Color(0.16f, 0.40f, 0.56f, 0.92f),
                    fog: Rgb(214, 228, 240), fogDensity: 0.030f,
                    foodNodes: 2, diets: new[] { Diet.Carnivore, Diet.Omnivore },
                    scatter: new[]
                    {
                        Scatter(BiomeDefinition.PropKind.PineTree, 0.05f, 0.9f, 1.5f, 1.8f,
                                Rgb(64, 100, 84), Rgb(78, 66, 58), wind: true),
                        Scatter(BiomeDefinition.PropKind.Rock, 0.09f, 0.6f, 1.4f, 0.8f,
                                Rgb(178, 188, 204), Rgb(140, 150, 168)),
                    }),

                Biome("lava", "Ember Flats", 12, 7500,
                    "The ground is warm. The dragon considers this a feature.",
                    low: Rgb(52, 34, 32), high: Rgb(92, 52, 42), cliff: Rgb(44, 30, 28),
                    relief: 0.36f, reliefScale: 0.5f,
                    water: true, waterLevel: -0.16f,
                    shallow: new Color(1.0f, 0.62f, 0.20f, 0.90f),
                    deep: new Color(0.92f, 0.24f, 0.08f, 1.0f),
                    fog: Rgb(96, 52, 40), fogDensity: 0.038f,
                    foodNodes: 2, diets: new[] { Diet.Carnivore, Diet.Mineral },
                    ambientLift: 0.20f,
                    scatter: new[]
                    {
                        Scatter(BiomeDefinition.PropKind.Rock, 0.24f, 0.6f, 1.6f, 0.6f,
                                Rgb(56, 40, 38), Rgb(38, 28, 26)),
                        Scatter(BiomeDefinition.PropKind.Crystal, 0.08f, 0.7f, 1.5f, 0.85f,
                                Rgb(252, 132, 52), Rgb(198, 72, 30)),
                    }),
            };

            return list;
        }

        static BiomeDefinition Biome(
            string id, string displayName, int requiredLevel, int unlockCost, string description,
            Color low, Color high, Color cliff, float relief, float reliefScale,
            bool water, float waterLevel, Color shallow, Color deep,
            Color fog, float fogDensity, int foodNodes, Diet[] diets,
            BiomeDefinition.ScatterEntry[] scatter,
            float ambientLift = 0f, float nocturnalAffinity = 0f)
        {
            BiomeDefinition b = CreateOrLoad<BiomeDefinition>($"{DataRoot}/Biomes/Biome_{id}.asset");

            b.id = id;
            b.displayName = displayName;
            b.description = description;
            b.requiredLevel = requiredLevel;
            b.unlockCurrency = CurrencyKind.Coins;
            b.unlockCost = unlockCost;

            b.groundLow = low;
            b.groundHigh = high;
            b.cliffColour = cliff;
            b.reliefHeight = relief;
            b.reliefScale = reliefScale;

            b.hasWater = water;
            b.waterLevel = waterLevel;
            b.waterShallow = shallow;
            b.waterDeep = deep;

            b.sunTintDay = Color.white;
            b.sunTintNight = new Color(0.46f, 0.56f, 0.90f);
            b.fogColour = fog;
            b.fogDensity = fogDensity;
            b.ambientLift = ambientLift;

            b.scatter = scatter;
            b.foodNodes = foodNodes;
            b.foodDiets = diets;
            b.nocturnalAffinity = nocturnalAffinity;
            b.comfortCoinMultiplier = 1.25f;

            EditorUtility.SetDirty(b);
            return b;
        }

        static BiomeDefinition.ScatterEntry Scatter(
            BiomeDefinition.PropKind kind, float density, float minScale, float maxScale,
            float spacing, Color primary, Color secondary,
            bool wind = false, bool allowInWater = false) => new()
        {
            kind = kind,
            density = density,
            scaleRange = new Vector2(minScale, maxScale),
            minSpacing = spacing,
            primary = primary,
            secondary = secondary,
            windSwept = wind,
            allowInWater = allowInWater,
        };

        // ---- boxes ----------------------------------------------------------

        static List<MysteryBoxDefinition> BuildBoxes(Dictionary<string, CreatureDefinition> creatures)
        {
            MysteryBoxDefinition wooden = CreateOrLoad<MysteryBoxDefinition>($"{DataRoot}/Boxes/Box_wooden.asset");
            wooden.id = "wooden_box";
            wooden.displayName = "Wooden Box";
            wooden.description = "Whatever was wandering nearby when the lid went on.";
            wooden.accentColour = Rgb(184, 132, 72);
            wooden.costCurrency = CurrencyKind.Coins;
            wooden.cost = 120;
            wooden.rewardedAdEligible = true;
            wooden.rewardedAdCooldownSeconds = 900;
            wooden.duplicateEssence = 12;
            wooden.duplicatesGrantCopy = true;
            wooden.pityRareAfter = 10;
            wooden.pityEpicAfter = 0;
            wooden.weights = new List<MysteryBoxDefinition.RarityWeight>
            {
                Weight(Rarity.Common, 68f),
                Weight(Rarity.Uncommon, 24f),
                Weight(Rarity.Rare, 8f),
            };
            wooden.pool = new List<CreatureDefinition>
            {
                creatures["goblin"], creatures["slime"], creatures["wolf"], creatures["knight"],
            };
            EditorUtility.SetDirty(wooden);

            MysteryBoxDefinition arcane = CreateOrLoad<MysteryBoxDefinition>($"{DataRoot}/Boxes/Box_arcane.asset");
            arcane.id = "arcane_box";
            arcane.displayName = "Arcane Box";
            arcane.description = "Hums faintly. Something in here does not like being boxed.";
            arcane.accentColour = Rgb(150, 108, 220);
            arcane.costCurrency = CurrencyKind.Coins;
            arcane.cost = 850;
            arcane.rewardedAdEligible = false;
            arcane.duplicateEssence = 45;
            arcane.duplicatesGrantCopy = true;
            arcane.pityRareAfter = 4;
            arcane.pityEpicAfter = 20;
            arcane.weights = new List<MysteryBoxDefinition.RarityWeight>
            {
                Weight(Rarity.Common, 22f),
                Weight(Rarity.Uncommon, 34f),
                Weight(Rarity.Rare, 34f),
                Weight(Rarity.Epic, 8f),
                Weight(Rarity.Legendary, 2f),
            };
            arcane.pool = new List<CreatureDefinition>
            {
                creatures["goblin"], creatures["slime"], creatures["wolf"],
                creatures["knight"], creatures["skeleton"], creatures["dragon"],
            };
            EditorUtility.SetDirty(arcane);

            return new List<MysteryBoxDefinition> { wooden, arcane };
        }

        static MysteryBoxDefinition.RarityWeight Weight(Rarity rarity, float weight) =>
            new() { rarity = rarity, weight = weight };
    }
}
