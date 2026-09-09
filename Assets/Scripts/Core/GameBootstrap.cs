using System.Collections;
using LivingDiorama.Ads;
using LivingDiorama.Data;
using LivingDiorama.Diorama;
using LivingDiorama.Meta;
using LivingDiorama.Presentation;
using LivingDiorama.Save;
using LivingDiorama.Simulation;
using LivingDiorama.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace LivingDiorama.Core
{
    /// <summary>
    /// Builds the whole game at runtime.
    ///
    /// The scene asset contains a single object with this component on it. Everything
    /// else -- camera, lighting, terrain, creatures, UI -- is constructed here from data.
    /// That keeps the scene file trivial to review and impossible to break by dragging
    /// something in the editor, and it means the game can be reasoned about by reading
    /// this one method rather than clicking through a hierarchy.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("Autosave")]
        [SerializeField] float _autosaveSeconds = 30f;

        [Header("Debug")]
        [Tooltip("Wipe the save on start. Editor only -- never enable in a build.")]
        [SerializeField] bool _resetSaveOnStart;

        GameDatabase _db;
        GameState _state;
        GameController _controller;
        DioramaWorld _world;
        EcosystemSimulation _sim;
        EconomyService _economy;
        CreatureFactory _factory;
        DioramaCamera _camera;
        DayNightDriver _dayNight;
        GameUI _ui;
        WorldClock _clock;
        Audio.AudioDirector _audio;
        Unboxing.UnboxingDirector _unboxing;

        float _saveTimer;
        bool _ready;

        void Awake()
        {
            QualityTuner.Apply();

            _db = GameDatabase.Instance;
            if (_db == null)
            {
                enabled = false;
                return;
            }

            SimEventBus.Reset();
            AdHub.Current = BuildAdService();
            AdHub.Current.Initialise();
        }

        static IAdService BuildAdService()
        {
#if LD_ADMOB && !UNITY_EDITOR
            return new AdMobAdService(AdIds.RewardedUnitId, AdIds.InterstitialUnitId);
#else
            return new StubAdService();
#endif
        }

        IEnumerator Start()
        {
            if (_db == null) yield break;

#if UNITY_EDITOR
            if (_resetSaveOnStart) SaveService.Delete();
#endif

            SaveData data = SaveService.Load();
            bool freshGame = data == null;
            if (freshGame)
            {
                data = GameState.CreateNewGame(_db, Random.Range(int.MinValue, int.MaxValue));
            }

            _state = new GameState(data, _db);
            _clock = new WorldClock(_db.simulation, data.clockHours);
            _audio = Audio.AudioDirector.Create(transform);

            BuildLighting();
            BuildCamera();
            BuildWorld(data);
            BuildSimulation();
            BuildUI();

            // Give glTFast a frame to get the first models in before the camera settles,
            // so the opening shot is not a diorama full of placeholders.
            yield return null;

            SpawnSavedCreatures();
            _camera.Frame(_world.WorldBounds, true);

            // The world runs behind the title screen but the clock and the economy hold
            // still until the player has actually stepped in.
            _sim.Paused = true;

            if (!freshGame)
            {
                OfflineProgress.Report report = OfflineProgress.Compute(
                    data, _db, System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                if (report.WorthShowing) _ui.QueueWelcomeBack(report);
                else OfflineProgress.Apply(data, _db, report, _state);

                _clock.SetTotalHours(data.clockHours);
            }

            _ready = true;
            yield return _ui.RunOpening();
        }

        // ---- construction ---------------------------------------------------

        void BuildLighting()
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);

            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = QualityTuner.CurrentTier == QualityTuner.Tier.Low
                ? LightShadows.Hard
                : LightShadows.Soft;
            sun.shadowStrength = 0.72f;
            // Generated meshes are not always watertight; a small bias avoids acne
            // without floating the contact shadows off the ground.
            sun.shadowBias = 0.03f;
            sun.shadowNormalBias = 0.25f;

            _dayNight = sunGo.AddComponent<DayNightDriver>();
            _dayNight.Initialise(sun, _clock, _db.simulation);
        }

        void BuildCamera()
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(transform, false);

            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.68f, 0.82f);
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120f;

            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = QualityTuner.CurrentTier != QualityTuner.Tier.Low;
            camData.antialiasing = QualityTuner.CurrentTier == QualityTuner.Tier.High
                ? AntialiasingMode.SubpixelMorphologicalAntiAliasing
                : AntialiasingMode.None;
            camData.requiresDepthTexture = true;   // the water shader needs the depth buffer

            camGo.AddComponent<AudioListener>();
            _camera = camGo.AddComponent<DioramaCamera>();

            if (camData.renderPostProcessing) BuildPostProcessing(camGo.transform);
        }

        /// <summary>A restrained grade: enough bloom to make the water sparkle read, a
        /// warm filmic curve, and a vignette to hold the eye on the diorama.</summary>
        void BuildPostProcessing(Transform parent)
        {
            var volumeGo = new GameObject("PostProcessing");
            volumeGo.transform.SetParent(parent, false);

            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "DioramaGrade";

            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.55f);
            bloom.threshold.Override(1.15f);
            bloom.scatter.Override(0.62f);
            bloom.tint.Override(new Color(1f, 0.96f, 0.88f));

            var grade = profile.Add<ColorAdjustments>(true);
            grade.postExposure.Override(-0.05f);
            grade.contrast.Override(11f);
            grade.saturation.Override(14f);

            var curves = profile.Add<ShadowsMidtonesHighlights>(true);
            // Cool the shadows and warm the highlights: the cheapest way to make a
            // flat-shaded scene look lit rather than coloured in.
            curves.shadows.Override(new Vector4(0.94f, 0.97f, 1.08f, 0f));
            curves.highlights.Override(new Vector4(1.05f, 1.01f, 0.94f, 0f));

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.26f);
            vignette.smoothness.Override(0.5f);

            volume.profile = profile;
        }

        void BuildWorld(SaveData data)
        {
            var worldGo = new GameObject("Diorama");
            worldGo.transform.SetParent(transform, false);
            _world = worldGo.AddComponent<DioramaWorld>();

            Material ground = MakeMaterial("Living Diorama/Ground", "GroundMaterial");
            Material prop = MakeMaterial("Living Diorama/Ground", "PropMaterial");
            Material foliage = MakeMaterial("Living Diorama/Foliage", "FoliageMaterial");
            Material water = MakeMaterial("Living Diorama/Water", "WaterMaterial");

            // Props are not tiles, so they must not get the tile-seam darkening.
            if (prop.HasProperty("_EdgeDarken")) prop.SetFloat("_EdgeDarken", 0f);
            if (prop.HasProperty("_DetailStrength")) prop.SetFloat("_DetailStrength", 0.04f);
            if (prop.HasProperty("_CliffStart")) prop.SetFloat("_CliffStart", 1.1f);

            _world.Initialise(_db, data.worldSeed, ground, prop, foliage, water);

            foreach (SavedTile tile in data.tiles)
            {
                BiomeDefinition biome = _db.GetBiome(tile.biomeId) ??
                                        (_db.biomes.Count > 0 ? _db.biomes[0] : null);
                _world.BuildTile(new Vector2Int(tile.x, tile.y), biome);
            }

            if (data.tiles.Count > 0)
            {
                _dayNight.SetDominantBiome(_db.GetBiome(data.tiles[0].biomeId));
            }
        }

        /// <summary>
        /// Build a material, surviving a missing shader.
        ///
        /// Shader.Find returns null for anything stripped from the build, and passing null
        /// to the Material constructor throws -- which used to take the whole world build
        /// down with it, leaving a black screen and no diorama. The project configurator
        /// keeps these shaders in the always-included list precisely so this cannot
        /// happen, but a hard crash is far too steep a price for a stripping mistake.
        /// </summary>
        static Material MakeMaterial(string shaderName, string name)
        {
            Shader shader = Shader.Find(shaderName);

            if (shader == null)
            {
                Debug.LogError($"[GameBootstrap] shader '{shaderName}' is missing from this " +
                               "build; run Living Diorama > Configure Project to restore it");

                foreach (string fallback in new[] { "Universal Render Pipeline/Lit", "Sprites/Default" })
                {
                    shader = Shader.Find(fallback);
                    if (shader != null) break;
                }
            }

            return shader != null
                ? new Material(shader) { name = name }
                : null;
        }

        void BuildSimulation()
        {
            var simGo = new GameObject("Ecosystem");
            simGo.transform.SetParent(transform, false);

            _sim = simGo.AddComponent<EcosystemSimulation>();
            _factory = new CreatureFactory(Shader.Find("Living Diorama/Creature"));
            _sim.Initialise(_db, _world, _clock, _factory);

            foreach (var kv in _world.Tiles)
            {
                foreach (FoodNode node in kv.Value.Food) _sim.RegisterFood(node);
            }

            _economy = simGo.AddComponent<EconomyService>();

            var pacing = new AdPacing(_db.progression);
            pacing.SuppressForSeconds(90);   // never interrupt the first minute and a half

            _controller = new GameController(_db, _state, _world, _sim, pacing);
            _controller.SaveRequested += SaveNow;

            _economy.Initialise(_state, _db, _sim);
        }

        void SpawnSavedCreatures()
        {
            foreach (SavedCreature entry in _state.Data.creatures)
            {
                if (entry.placed) _controller.SpawnAgent(entry);
            }
        }

        void BuildUI()
        {
            var uiGo = new GameObject("UI");
            uiGo.transform.SetParent(transform, false);

            var document = uiGo.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("UI/PanelSettings");
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>("UI/GameUI");

            if (document.panelSettings == null || document.visualTreeAsset == null)
            {
                Debug.LogError("[GameBootstrap] UI assets missing from Resources/UI. " +
                               "Run Living Diorama/Rebuild Content.");
                return;
            }

            // The reveal stage sits well above the diorama so the unboxing camera move
            // reads as going somewhere rather than zooming in on the terrain.
            Bounds bounds = _world.WorldBounds;
            var stagePosition = new Vector3(bounds.center.x, 14f, bounds.center.z);
            _unboxing = Unboxing.UnboxingDirector.Create(transform, _camera, _factory, stagePosition);

            _ui = uiGo.AddComponent<GameUI>();
            _ui.Initialise(document, _controller, _economy, _sim, _camera, _audio, _unboxing, EraseSave);

            _world.TileBuilt += OnTileBuilt;
        }

        void OnTileBuilt(Diorama.DioramaTile tile)
        {
            _camera.Frame(_world.WorldBounds);
            _audio.PlaySfx("expand_tile", 0.9f);
        }

        /// <summary>Wipe everything and start over. Reached only through the two-tap
        /// confirmation in the settings screen.</summary>
        void EraseSave()
        {
            _ready = false;
            SaveService.Delete();
            OnboardingDirector.Completed = false;
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        // ---- persistence ----------------------------------------------------

        void Update()
        {
            if (!_ready) return;

            _saveTimer -= Time.unscaledDeltaTime;
            if (_saveTimer <= 0f)
            {
                _saveTimer = _autosaveSeconds;
                SaveNow();
            }
        }

        void SaveNow()
        {
            if (_state == null || _controller == null) return;
            _controller.CaptureAll();
            SaveService.Save(_state.Data);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && _ready) SaveNow();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && _ready) SaveNow();
        }

        void OnApplicationQuit()
        {
            if (_ready) SaveNow();
        }

        void OnDestroy()
        {
            _factory?.Dispose();
            SimEventBus.Reset();
        }
    }
}
