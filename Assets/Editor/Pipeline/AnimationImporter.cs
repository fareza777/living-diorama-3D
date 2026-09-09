using System.Collections.Generic;
using System.IO;
using System.Linq;
using LivingDiorama.Data;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LivingDiorama.EditorTools
{
    /// <summary>
    /// Turns the rigged FBX exports from Meshy into something the game can play.
    ///
    /// Each export is a whole character -- mesh, textures and one clip -- at around
    /// seven megabytes, so eleven clips per creature is a hundred and fifty megabytes of
    /// duplicated mesh. Only one of them is kept as the model; the rest are mined for
    /// their animation and then referenced by nothing, which keeps them out of the build
    /// entirely while the extracted clips stay a few hundred kilobytes each.
    ///
    /// Creatures without a rig are simply skipped. The wolf, the slime and the dragon
    /// keep the procedural animator, which is a better result than forcing a quadruped
    /// onto a humanoid skeleton.
    /// </summary>
    public static class AnimationImporter
    {
        const string CreaturesDir = "Assets/Art/Creatures";
        const string ClipsDir = "Assets/Data/Animations";
        const string ControllersDir = "Assets/Data/Animators";

        /// <summary>The clip used as the model and avatar source. Any of them would do;
        /// idle is the one a creature spends most of its life in.</summary>
        const string RigClip = "idle";

        /// <summary>Clips that must loop. Everything else plays once and holds.</summary>
        static readonly HashSet<string> Looping = new()
        {
            "idle", "walk", "run", "sleep", "sneak",
        };

        [MenuItem("Living Diorama/Import Creature Animations", priority = 3)]
        public static void ImportAll()
        {
            Directory.CreateDirectory(ClipsDir);
            Directory.CreateDirectory(ControllersDir);

            int imported = 0;

            foreach (string dir in Directory.GetDirectories(CreaturesDir))
            {
                string creature = Path.GetFileName(dir);
                string animationsDir = Path.Combine(dir, "Animations").Replace('\\', '/');
                if (!Directory.Exists(animationsDir)) continue;

                if (Import(creature, animationsDir)) imported++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[AnimationImporter] {imported} creature(s) now animate from clips");
        }

        static bool Import(string creature, string animationsDir)
        {
            string[] sources = Directory.GetFiles(animationsDir, "*.fbx")
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !p.EndsWith("_rig.fbx"))
                .ToArray();

            if (sources.Length == 0) return false;

            string rigPath = sources.FirstOrDefault(p => ClipName(p) == RigClip) ?? sources[0];

            ConfigureRig(rigPath);
            var rigModel = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
            Avatar avatar = FindAvatar(rigPath);

            if (rigModel == null)
            {
                Debug.LogError($"[AnimationImporter] {creature}: could not load '{rigPath}'");
                return false;
            }

            var clips = new Dictionary<string, AnimationClip>();
            string outDir = $"{ClipsDir}/{creature}";
            Directory.CreateDirectory(outDir);

            foreach (string source in sources)
            {
                string name = ClipName(source);
                ConfigureClipSource(source, source == rigPath, avatar);

                AnimationClip extracted = Extract(source, $"{outDir}/{name}.anim", Looping.Contains(name));
                if (extracted != null) clips[name] = extracted;
            }

            if (clips.Count == 0)
            {
                Debug.LogWarning($"[AnimationImporter] {creature}: no clips could be extracted");
                return false;
            }

            AnimatorController controller = BuildController(creature, clips);
            Assign(creature, rigModel, controller);

            Debug.Log($"[AnimationImporter] {creature}: {clips.Count} clips ({string.Join(", ", clips.Keys)})");
            return true;
        }

        /// <summary>"goblin@walk.fbx" -> "walk".</summary>
        static string ClipName(string path)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            int at = file.IndexOf('@');
            return at >= 0 ? file[(at + 1)..] : file;
        }

        // ---- import settings -------------------------------------------------

        static void ConfigureRig(string path)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer) return;

            // Generic rather than Humanoid: these skeletons are not standard humanoid
            // proportions, and generic keeps the bones exactly as authored, which is what
            // lets clips from sibling exports bind by path.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.importNormals = ModelImporterNormals.Import;
            importer.optimizeGameObjects = false;
            importer.SaveAndReimport();
        }

        static void ConfigureClipSource(string path, bool isRig, Avatar avatar)
        {
            if (isRig) return;
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer) return;

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
            importer.importAnimation = true;

            // Nothing but the animation is wanted from these files.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
        }

        static Avatar FindAvatar(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        }

        // ---- extraction ------------------------------------------------------

        /// <summary>
        /// Copy the clip out of the FBX into a standalone asset.
        ///
        /// A clip left inside its FBX drags the whole seven megabyte model into the build
        /// with it. Copied out, the model files end up referenced by nothing and are
        /// stripped, and the loop flag can be set per clip without fighting the importer.
        /// </summary>
        static AnimationClip Extract(string source, string destination, bool loop)
        {
            AnimationClip original = AssetDatabase.LoadAllAssetsAtPath(source)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

            if (original == null)
            {
                Debug.LogWarning($"[AnimationImporter] no clip inside '{source}'");
                return null;
            }

            var copy = Object.Instantiate(original);
            copy.name = Path.GetFileNameWithoutExtension(destination);

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = loop;
            // Meshy's clips travel forward in world space. Anchoring the root stops a
            // walking creature sliding away from where the simulation thinks it is.
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalPositionXZ = false;
            AnimationUtility.SetAnimationClipSettings(copy, settings);

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);
            if (existing != null)
            {
                EditorUtility.CopySerialized(copy, existing);
                Object.DestroyImmediate(copy);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(copy, destination);
            return copy;
        }

        // ---- controller ------------------------------------------------------

        /// <summary>
        /// One state per behaviour, no transitions.
        ///
        /// Transitions in a controller would need a parameter per behaviour and a web of
        /// conditions to maintain in lockstep with the utility AI. Naming each state after
        /// the behaviour and cross-fading to it by name from code keeps the two in sync by
        /// construction: add a behaviour, add a clip with the same id, and it just plays.
        /// </summary>
        static AnimatorController BuildController(string creature, Dictionary<string, AnimationClip> clips)
        {
            string path = $"{ControllersDir}/{creature}.controller";

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            }

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            foreach (ChildAnimatorState child in machine.states.ToArray())
            {
                machine.RemoveState(child.state);
            }

            foreach (KeyValuePair<string, AnimationClip> pair in clips)
            {
                AnimatorState state = machine.AddState(pair.Key);
                state.motion = pair.Value;
                state.writeDefaultValues = false;

                if (pair.Key == RigClip) machine.defaultState = state;
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        static void Assign(string creature, GameObject rigModel, AnimatorController controller)
        {
            string path = $"Assets/Data/Creatures/Creature_{creature}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<CreatureDefinition>(path);

            if (definition == null)
            {
                Debug.LogWarning($"[AnimationImporter] no creature asset at '{path}'");
                return;
            }

            definition.riggedPrefab = rigModel;
            definition.animatorController = controller;
            EditorUtility.SetDirty(definition);
        }
    }
}
