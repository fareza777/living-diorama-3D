using System.Collections.Generic;
using System.IO;
using LivingDiorama.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace LivingDiorama.EditorTools
{
    /// <summary>
    /// Configures the project itself: the render pipeline, the UI panel, the scene and
    /// the Android player settings.
    ///
    /// All of it is done in code for the same reason the content is: a fresh clone can
    /// reach a buildable state with one menu item, and every setting that matters is
    /// visible as a reviewable line rather than buried in a binary asset nobody opens.
    /// </summary>
    public static class ProjectConfigurator
    {
        const string RenderingDir = "Assets/Settings";
        const string ScenePath = "Assets/Scenes/Diorama.unity";
        const string PanelSettingsPath = "Assets/Resources/UI/PanelSettings.asset";
        const string ThemePath = "Assets/Resources/UI/DioramaTheme.tss";
        const string UiArtDir = "Assets/Resources/UI/Art";

        [MenuItem("Living Diorama/Configure Project", priority = 1)]
        public static void ConfigureProject()
        {
            EnsureFolders();
            UniversalRenderPipelineAsset urp = ConfigureRenderPipeline();
            ConfigurePanelSettings();
            ConfigureUiArtImport();
            ConfigurePlayerSettings();
            ConfigureScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ProjectConfigurator] configured. URP asset: {AssetDatabase.GetAssetPath(urp)}");
        }

        /// <summary>Everything, in the order a clean checkout needs it.</summary>
        [MenuItem("Living Diorama/Rebuild Everything", priority = 2)]
        public static void RebuildEverything()
        {
            ContentBuilder.RebuildContent();
            ConfigureProject();
        }

        static void EnsureFolders()
        {
            foreach (string path in new[] { RenderingDir, "Assets/Scenes", "Assets/Resources/UI", UiArtDir })
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            }
            AssetDatabase.Refresh();
        }

        // ---- rendering ------------------------------------------------------

        static UniversalRenderPipelineAsset ConfigureRenderPipeline()
        {
            string rendererPath = $"{RenderingDir}/DioramaRenderer.asset";
            string urpPath = $"{RenderingDir}/DioramaURP.asset";

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }

            // Forward rather than deferred: the scene is a handful of materials with no
            // light count worth deferring for, and forward is the cheaper path on mobile.
            renderer.renderingMode = RenderingMode.Forward;
            // The water shader samples scene depth, and the creature dissolve wants it too.
            renderer.depthPrimingMode = DepthPrimingMode.Disabled;
            EditorUtility.SetDirty(renderer);

            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(urpPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(urp, urpPath);
            }

            urp.supportsHDR = true;
            urp.msaaSampleCount = 4;
            urp.renderScale = 1f;

            urp.shadowDistance = 34f;
            urp.shadowCascadeCount = 1;
            urp.shadowDepthBias = 0.6f;
            urp.shadowNormalBias = 0.9f;
            urp.maxAdditionalLightsCount = 4;
            urp.colorGradingMode = ColorGradingMode.LowDynamicRange;
            urp.colorGradingLutSize = 32;
            urp.useSRPBatcher = true;

            // Several lighting toggles expose only a getter, so they have to be written
            // through the serialised object. The field names are part of URP's asset
            // format rather than its API, hence the guard on each one.
            var serialized = new SerializedObject(urp);
            SetBool(serialized, "m_MainLightShadowsSupported", true);
            SetBool(serialized, "m_SoftShadowsSupported", true);
            SetBool(serialized, "m_AdditionalLightShadowsSupported", false);
            SetEnum(serialized, "m_MainLightRenderingMode", (int)LightRenderingMode.PerPixel);
            // The unboxing stage adds two point lights; four is headroom, not ambition.
            SetEnum(serialized, "m_AdditionalLightsRenderingMode", (int)LightRenderingMode.PerPixel);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.renderPipeline = urp;

            return urp;
        }

        static void SetBool(SerializedObject serialized, string field, bool value)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"[ProjectConfigurator] URP field '{field}' not found; " +
                                 "the asset format may have changed");
                return;
            }
            property.boolValue = value;
        }

        static void SetEnum(SerializedObject serialized, string field, int value)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"[ProjectConfigurator] URP field '{field}' not found; " +
                                 "the asset format may have changed");
                return;
            }
            property.enumValueIndex = value;
        }

        // ---- UI -------------------------------------------------------------

        static void ConfigurePanelSettings()
        {
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            }

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null)
            {
                Debug.LogError($"[ProjectConfigurator] missing runtime theme at {ThemePath}; " +
                               "the UI will render unstyled");
            }
            panel.themeStyleSheet = theme;

            // Authored against a tall phone. Matching on width keeps the layout stable
            // across the enormous range of Android aspect ratios.
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1080, 1920);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.35f;
            panel.clearColor = false;

            EditorUtility.SetDirty(panel);
        }

        /// <summary>
        /// Import the generated interface art. Cutout icons become plain sprites; the
        /// plates get nine-slice borders so one 1024px image stretches to any button
        /// without the ornament smearing.
        /// </summary>
        static void ConfigureUiArtImport()
        {
            if (!Directory.Exists(UiArtDir)) return;

            // Border insets as a fraction of the image, per plate. Buttons are wide and
            // shallow so their vertical border has to be nearly the whole height.
            var plateBorders = new Dictionary<string, Vector4>
            {
                ["panel_frame"] = new(0.17f, 0.20f, 0.17f, 0.20f),
                ["button_primary"] = new(0.22f, 0.30f, 0.22f, 0.30f),
                ["button_ghost"] = new(0.22f, 0.30f, 0.22f, 0.30f),
                ["chip_plate"] = new(0.24f, 0.32f, 0.24f, 0.32f),
            };

            foreach (string file in Directory.GetFiles(UiArtDir, "*.png"))
            {
                string path = file.Replace('\\', '/');
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;

                string key = Path.GetFileNameWithoutExtension(path);
                bool isPlate = plateBorders.ContainsKey(key);

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = !isPlate;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = isPlate ? 512 : 256;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;

                if (isPlate)
                {
                    Vector4 fraction = plateBorders[key];
                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);

                    // Borders are in pixels, so they have to be derived from the actual
                    // imported size rather than assumed.
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (texture != null)
                    {
                        settings.spriteBorder = new Vector4(
                            texture.width * fraction.x,
                            texture.height * fraction.y,
                            texture.width * fraction.z,
                            texture.height * fraction.w);
                        importer.SetTextureSettings(settings);
                    }
                }

                importer.SaveAndReimport();
            }
        }

        // ---- player ---------------------------------------------------------

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "Living Diorama";
            PlayerSettings.productName = "Living Diorama";
            PlayerSettings.bundleVersion = "0.1.0";

            PlayerSettings.SetApplicationIdentifier(
                NamedBuildTarget.Android, "com.livingdiorama.game");

            // Linear colour is not optional here: the toon ramp, the wrapped subsurface
            // and the additive glows all assume it.
            PlayerSettings.colorSpace = ColorSpace.Linear;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // 25 is the floor this editor still accepts; going lower is now an error.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = false;
            PlayerSettings.Android.forceSDCardPermission = false;

            // Vulkan first, GLES3 as the fallback for devices with poor Vulkan drivers.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

            PlayerSettings.gpuSkinning = false;   // nothing here is skinned
            PlayerSettings.MTRendering = true;
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);

            ApplyAppIcon();
        }

        /// <summary>Use the generated emblem as the app icon when it exists.</summary>
        static void ApplyAppIcon()
        {
            var emblem = AssetDatabase.LoadAssetAtPath<Texture2D>($"{UiArtDir}/title_emblem.png");
            if (emblem == null) return;

            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(emblem)) is TextureImporter importer &&
                !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Android, IconKind.Application);
            if (sizes == null || sizes.Length == 0) return;

            var icons = new Texture2D[sizes.Length];
            for (int i = 0; i < icons.Length; i++) icons[i] = emblem;

            PlayerSettings.SetIcons(NamedBuildTarget.Android, icons, IconKind.Application);
        }

        // ---- scene ----------------------------------------------------------

        static void ConfigureScene()
        {
            Scene scene;

            if (File.Exists(ScenePath))
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            // The scene is deliberately almost empty: one object that builds everything
            // else. Nothing to accidentally unwire, nothing to merge-conflict over.
            var existing = Object.FindFirstObjectByType<GameBootstrap>();
            if (existing == null)
            {
                var go = new GameObject("Game");
                go.AddComponent<GameBootstrap>();
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.skybox = null;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
