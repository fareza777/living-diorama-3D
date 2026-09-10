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

        /// <summary>
        /// Clips where only the settled part is wanted, and the rest is the character
        /// getting into or out of it.
        ///
        /// Meshy's "Sleep" is a performance: the goblin lies down, then sits back up after
        /// a second and a half and stays up for the remaining four seconds. Looped, that
        /// is a creature that goes to bed and gets straight back out of it, over and over,
        /// which is exactly how it read in the diorama. Only the lying-down part is sleep.
        /// </summary>
        static readonly HashSet<string> RestOnly = new() { "sleep" };

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

                AnimationClip extracted = Extract(source, $"{outDir}/{name}.anim",
                    Looping.Contains(name), RestOnly.Contains(name));
                if (extracted != null) clips[name] = extracted;
            }

            // The extracted clips are the shipped asset; the carrier FBXs are only their
            // source and are not kept in version control, because each one drags a whole
            // seven megabyte copy of the model with it. A clone therefore has the clips
            // but not the files they came out of, and must still get a full controller.
            foreach (string existing in Directory.GetFiles(outDir, "*.anim"))
            {
                string name = Path.GetFileNameWithoutExtension(existing);
                if (clips.ContainsKey(name)) continue;

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(existing.Replace(Path.DirectorySeparatorChar, '/'));
                if (clip != null) clips[name] = clip;
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
        static AnimationClip Extract(string source, string destination, bool loop, bool restOnly)
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

            if (restOnly && TrimToRestingPose(copy))
            {
                Debug.Log($"[AnimationImporter] {copy.name}: trimmed to its resting pose ({copy.length:0.00}s)");
            }

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

        /// <summary>
        /// Cut a clip down to the stretch where the character is actually on the ground.
        ///
        /// The hip height tells us where that is without having to know anything about
        /// the clip: it is near its minimum while the creature is lying down and well
        /// above it while the creature is upright. Keeping the longest run of keys near
        /// the floor and rebasing it to start at zero turns a lie-down-and-get-up
        /// performance into a sleep that loops on itself.
        ///
        /// Returns false and leaves the clip alone when it cannot tell -- no hip curve,
        /// or a clip that never leaves the floor in the first place.
        /// </summary>
        static bool TrimToRestingPose(AnimationClip clip)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);

            AnimationCurve height = null;
            foreach (EditorCurveBinding binding in bindings)
            {
                if (binding.propertyName != "m_LocalPosition.y") continue;
                if (!binding.path.EndsWith("Hips", System.StringComparison.OrdinalIgnoreCase)) continue;

                height = AnimationUtility.GetEditorCurve(clip, binding);
                break;
            }

            if (height == null || height.length < 4) return false;

            float min = float.MaxValue, max = float.MinValue;
            foreach (Keyframe key in height.keys)
            {
                min = Mathf.Min(min, key.value);
                max = Mathf.Max(max, key.value);
            }

            // Everything at one height: the creature never gets up, so there is nothing
            // here that is not already rest.
            if (max - min < 0.05f) return false;

            float ceiling = min + (max - min) * 0.25f;
            if (!LongestRunBelow(height, ceiling, out float from, out float to)) return false;
            if (to - from < 0.3f) return false;

            foreach (EditorCurveBinding binding in bindings)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;

                AnimationUtility.SetEditorCurve(clip, binding, Crop(curve, from, to));
            }

            return true;
        }

        /// <summary>The longest stretch of the curve that stays under a value, as a time
        /// range. Longest rather than first: a clip that stands up before it lies down is
        /// just as plausible as one that does it the other way round.</summary>
        static bool LongestRunBelow(AnimationCurve curve, float ceiling, out float from, out float to)
        {
            from = to = 0f;
            float best = 0f;
            float runStart = float.NaN;

            Keyframe[] keys = curve.keys;
            for (int i = 0; i <= keys.Length; i++)
            {
                bool low = i < keys.Length && keys[i].value <= ceiling;

                if (low && float.IsNaN(runStart)) runStart = keys[i].time;

                if (!low && !float.IsNaN(runStart))
                {
                    float runEnd = keys[i - 1].time;
                    if (runEnd - runStart > best)
                    {
                        best = runEnd - runStart;
                        from = runStart;
                        to = runEnd;
                    }
                    runStart = float.NaN;
                }
            }

            return best > 0f;
        }

        /// <summary>
        /// A curve limited to a time window and rebased so it starts at zero.
        ///
        /// The closing key repeats the opening one rather than sampling the far end of
        /// the window. A resting loop that ends a few centimetres from where it began
        /// twitches once a cycle, and a twitch is exactly what a sleeping creature must
        /// not do; ending where it started costs a little drift nobody can see and buys a
        /// loop with no seam in it.
        /// </summary>
        static AnimationCurve Crop(AnimationCurve curve, float from, float to)
        {
            var keys = new List<Keyframe>(curve.length + 2)
            {
                new(0f, curve.Evaluate(from)),
            };

            foreach (Keyframe key in curve.keys)
            {
                if (key.time <= from + 1e-4f || key.time >= to - 1e-4f) continue;

                Keyframe shifted = key;
                shifted.time = key.time - from;
                keys.Add(shifted);
            }

            keys.Add(new Keyframe(to - from, curve.Evaluate(from)));
            return new AnimationCurve(keys.ToArray());
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
