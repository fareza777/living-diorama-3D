using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using LivingDiorama.Presentation;
using LivingDiorama.Simulation;
using LivingDiorama.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.EditorTools
{
    /// <summary>
    /// Headless smoke test: runs the game for a minute and photographs it.
    ///
    /// Compiling and even building prove nothing about whether the diorama actually
    /// appears. This enters play mode, waits for the models to stream in, skips the title
    /// card, and renders the live camera to PNGs, so a reviewer can see the real thing and
    /// a broken shader or a failed model load shows up as a picture rather than as a
    /// silent fallback.
    ///
    ///   Unity.exe -batchmode -projectPath . \
    ///     -executeMethod LivingDiorama.EditorTools.PlaymodeCapture.Run
    ///
    /// Note the absence of -nographics and -quit: it needs a graphics device to render,
    /// and it exits itself once the last frame is captured.
    /// </summary>
    public static class PlaymodeCapture
    {
        const string OutputDir = "Screenshots";
        const int Width = 1080;
        const int Height = 1920;

        static RenderTexture _target;
        static string _pendingName;
        static int _pendingFrames;

        /// <summary>Seconds into the run, and what the shot is meant to show.</summary>
        static readonly (float at, string name)[] Schedule =
        {
            (4f, "01_title"),
            (11f, "02_diorama"),
            (18f, "03_creatures"),
            (21f, "03b_emotes"),
            (26f, "04_turntable"),
            (34f, "05_night"),
            (44f, "06_roster"),
            (58f, "07_built"),
        };

        /// <summary>Entering play mode reloads the domain, which wipes statics and the
        /// update subscription. The flag lives in SessionState so the harness can find its
        /// way back after the reload.</summary>
        const string ActiveKey = "LivingDiorama.PlaymodeCapture.Active";

        static double _startTime = -1;
        static int _next;
        static bool _begun;

        public static void Run()
        {
            Directory.CreateDirectory(OutputDir);

            // Always start from a fresh save so the capture shows a first run rather than
            // whatever state the last one happened to leave behind.
            Save.SaveService.Delete();
            PlayerPrefs.DeleteKey("onboarding.completed");
            PlayerPrefs.Save();

            EditorSceneManager.OpenScene("Assets/Scenes/Diorama.unity", OpenSceneMode.Single);

            SessionState.SetBool(ActiveKey, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;

            _startTime = -1;
            _next = 0;
            _begun = false;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;

            if (!EditorApplication.isPlaying)
            {
                // Still entering play mode.
                return;
            }

            // Editor-loop callbacks are not frame-locked, so time comes from the editor
            // clock rather than from Time.deltaTime.
            if (_startTime < 0) _startTime = EditorApplication.timeSinceStartup;
            var _elapsed = (float)(EditorApplication.timeSinceStartup - _startTime);

            // Skip the title once the world has had a moment to build, so most of the
            // schedule is spent on the thing worth looking at.
            if (!_begun && _elapsed > 5f)
            {
                _begun = true;
                PressBegin();
            }

            // Turntable on for the later shots so they are not all the same angle.
            if (_elapsed > 20f && _next < Schedule.Length - 1) SetTurntable(true);

            // A capture spans several frames, so nothing else is scheduled until it lands.
            if (_pendingName != null)
            {
                CollectCapture();
                return;
            }

            if (_next < Schedule.Length && _elapsed >= Schedule[_next].at)
            {
                if (Schedule[_next].name == "03b_emotes") ForceEmotes();
                if (Schedule[_next].name == "06_roster") SpawnRoster();
                if (Schedule[_next].name == "07_built") BuildFurniture();

                RequestCapture(Schedule[_next].name);
                _next++;
                return;
            }

            if (_next >= Schedule.Length) Finish();
        }

        /// <summary>The title screen waits for a tap. Reflection keeps the capture harness
        /// out of the shipping code rather than adding a back door to TitleScreen.</summary>
        static void PressBegin()
        {
            var title = UnityEngine.Object.FindFirstObjectByType<TitleScreen>();
            if (title == null)
            {
                Debug.LogWarning("[PlaymodeCapture] no TitleScreen found; carrying on");
                return;
            }

            MethodInfo begin = typeof(TitleScreen).GetMethod(
                "Begin", BindingFlags.Instance | BindingFlags.NonPublic);

            if (begin == null)
            {
                Debug.LogWarning("[PlaymodeCapture] TitleScreen.Begin is gone; carrying on");
                return;
            }

            begin.Invoke(title, null);
            Debug.Log("[PlaymodeCapture] pressed Begin");
        }

        /// <summary>
        /// Put one of every species on the ground at once.
        ///
        /// A fresh save starts with a goblin and two slimes, so four of the six creatures
        /// were never in a single picture -- which meant the rigs fitted to the wolf, the
        /// dragon and the skeleton could not be looked at without playing the game for an
        /// hour first. This is the shot that shows all of them.
        /// </summary>
        static void SpawnRoster()
        {
            var sim = UnityEngine.Object.FindFirstObjectByType<EcosystemSimulation>();
            LivingDiorama.Data.GameDatabase db = LivingDiorama.Data.GameDatabase.Instance;
            if (sim == null || sim.Surface == null || db == null) return;

            // Somewhere dry, near a creature that is already standing somewhere valid.
            // Guessing at world coordinates does not work: the first tile is not centred
            // on the origin, so a scan around zero found nothing at all.
            Vector3 anchor = Vector3.zero;
            foreach (CreatureAgent existing in sim.Agents)
            {
                anchor = existing.Position;
                break;
            }

            var spots = new List<Vector3>(6);
            for (float z = -5f; z <= 5f && spots.Count < 6; z += 0.3f)
            {
                for (float x = -5f; x <= 5f && spots.Count < 6; x += 0.3f)
                {
                    var spot = new Vector3(anchor.x + x, 0f, anchor.z + z);
                    if (Submerged(sim, ref spot) || !sim.Surface.Contains(spot)) continue;
                    if ((sim.Surface.ResolveObstacles(spot, 0.3f) - spot).sqrMagnitude > 0.0001f) continue;

                    bool crowded = false;
                    foreach (Vector3 taken in spots)
                    {
                        if ((taken - spot).sqrMagnitude < 0.85f * 0.85f) crowded = true;
                    }
                    if (!crowded) spots.Add(spot);
                }
            }

            if (spots.Count == 0)
            {
                Debug.LogWarning("[PlaymodeCapture] nowhere dry to put the roster");
                return;
            }

            int placed = 0;
            var centre = Vector3.zero;

            foreach (LivingDiorama.Data.CreatureDefinition def in db.creatures)
            {
                if (def == null) continue;

                Vector3 spot = spots[placed % spots.Count];
                centre += spot;
                sim.Spawn(def, $"capture_{def.id}", spot);
                placed++;
            }

            centre /= Mathf.Max(1, placed);

            // Low and close, so the ground line under their feet is actually visible.
            var camera = UnityEngine.Object.FindFirstObjectByType<DioramaCamera>();
            if (camera != null)
            {
                camera.Turntable = false;
                camera.SetScriptedShot(centre + Vector3.up * 0.35f, 28f, 16f, 5.6f);
            }

            Debug.Log($"[PlaymodeCapture] spawned {placed} species across {spots.Count} spots around {centre}");
        }

        /// <summary>
        /// Put one of everything down near a creature, so the shot shows the thing the
        /// build mode exists for: a goblin actually walking over and using it.
        ///
        /// Placed by the same service the player's taps go through, so what this proves
        /// is the real path -- validity checks, obstacles and all -- rather than a
        /// debug spawn that skips them.
        /// </summary>
        static void BuildFurniture()
        {
            var sim = UnityEngine.Object.FindFirstObjectByType<EcosystemSimulation>();
            LivingDiorama.Diorama.PlacementService placements = sim != null ? sim.Placements : null;
            if (placements == null || !placements.IsReady) return;

            Vector3 anchor = Vector3.zero;
            foreach (CreatureAgent existing in sim.Agents)
            {
                anchor = existing.Position;
                break;
            }

            int built = 0;
            var centre = Vector3.zero;

            foreach (LivingDiorama.Diorama.Placeable placeable in LivingDiorama.Diorama.Placeables.All)
            {
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    float angle = built * 1.57f + attempt * 0.31f;
                    float radius = 1.0f + attempt * 0.035f;
                    var spot = new Vector3(
                        anchor.x + Mathf.Cos(angle) * radius, 0f, anchor.z + Mathf.Sin(angle) * radius);
                    spot.y = sim.Surface.SampleHeight(spot);

                    if (!placements.CanPlaceAt(spot, placeable.Radius, out string _)) continue;

                    // Turned to face the middle of the group, so the shot shows their
                    // fronts rather than four backs.
                    float facing = Mathf.Atan2(anchor.x - spot.x, anchor.z - spot.z) * Mathf.Rad2Deg;
                    placements.Place(placeable.Id, spot, facing);

                    centre += spot;
                    built++;
                    break;
                }
            }

            if (built > 0) centre /= built;

            var camera = UnityEngine.Object.FindFirstObjectByType<DioramaCamera>();
            if (camera != null)
            {
                camera.Turntable = false;
                camera.SetScriptedShot(centre + Vector3.up * 0.25f, 25f, 22f, 4.4f);
            }

            Debug.Log($"[PlaymodeCapture] built {built} placeable(s) around {centre}");
        }

        static bool Submerged(EcosystemSimulation sim, ref Vector3 spot)
        {
            spot.y = sim.Surface.SampleHeight(spot);
            return sim.Surface.TryGetWater(spot, out float water) && spot.y <= water + 0.03f;
        }

        static void SetTurntable(bool on)
        {
            var camera = UnityEngine.Object.FindFirstObjectByType<DioramaCamera>();
            if (camera != null) camera.Turntable = on;
        }

        /// <summary>
        /// Photograph a real frame, interface included.
        ///
        /// Two false starts are worth recording. ScreenCapture writes nothing in batch
        /// mode, because there is no game view to capture. Calling camera.Render() by hand
        /// does produce an image, but it renders outside the normal frame -- so the depth
        /// texture is missing (which turns the water into a sheet of white foam) and the
        /// interface is absent entirely, since UI Toolkit composites its panel separately.
        ///
        /// Pointing both the camera and the UI panel at one RenderTexture and then letting
        /// the game render a normal frame into it gets everything, correctly lit, in one
        /// picture. It just cannot be done synchronously, hence the request/collect pair.
        /// </summary>
        static void RequestCapture(string name)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Debug.LogError("[PlaymodeCapture] no main camera");
                return;
            }

            _target ??= new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);

            // Two independent render paths writing one texture across frames composite
            // unreliably in batch mode, and the result can show graphics from an earlier
            // moment. Capturing one layer at a time is the only way to trust the picture,
            // so which layers are captured is a switch: -cameraOnly for the world,
            // -uiOnly for the interface, neither for a best-effort composite.
            string[] args = Environment.GetCommandLineArgs();
            bool uiOnly = args.Contains("-uiOnly");
            bool cameraOnly = args.Contains("-cameraOnly");

            if (uiOnly)
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _target;
                GL.Clear(true, true, Color.black);
                RenderTexture.active = previous;
            }
            else
            {
                camera.targetTexture = _target;
            }

            if (!cameraOnly)
            {
                var document = UnityEngine.Object.FindFirstObjectByType<UIDocument>();
                if (document != null && document.panelSettings != null)
                {
                    document.panelSettings.targetTexture = _target;
                }
            }

            _pendingName = name;
            // Editor update ticks are not player frames. In batch mode the two rates can
            // diverge badly, and reading the texture too early yields a black image with
            // no error, so this waits generously rather than cleverly.
            _pendingFrames = 40;
        }

        static void CollectCapture()
        {
            if (--_pendingFrames > 0) return;

            RenderTexture previousActive = RenderTexture.active;

            try
            {
                RenderTexture.active = _target;

                var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();

                string path = Path.Combine(OutputDir, $"{_pendingName}.png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);

                Debug.Log($"[PlaymodeCapture] wrote {path}  ({Report()})");
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlaymodeCapture] capture '{_pendingName}' failed: {e.Message}");
            }
            finally
            {
                RenderTexture.active = previousActive;
                ReleaseTargets();
                _pendingName = null;
            }
        }

        /// <summary>Hand the screen back so the game keeps presenting normally.</summary>
        static void ReleaseTargets()
        {
            Camera camera = Camera.main;
            if (camera != null) camera.targetTexture = null;

            var document = UnityEngine.Object.FindFirstObjectByType<UIDocument>();
            if (document != null && document.panelSettings != null)
            {
                document.panelSettings.targetTexture = null;
            }
        }

        /// <summary>A one-line health check printed beside every shot, so a blank image can
        /// be told apart from an empty world.</summary>
        /// <summary>
        /// Put a mood over every creature's head.
        ///
        /// The bubbles are the one part of the presentation that appears only when the
        /// simulation feels like it, so nothing automated ever looked at them -- which is
        /// how they shipped twice showing a blank square. Forcing one gives the shot
        /// something to photograph.
        /// </summary>
        static void ForceEmotes()
        {
            var sim = UnityEngine.Object.FindFirstObjectByType<EcosystemSimulation>();
            if (sim == null) return;

            var moods = new[] { Mood.Social, Mood.Hungry, Mood.Sleepy, Mood.Playful, Mood.Angry };
            int i = 0;

            foreach (CreatureAgent agent in sim.Agents)
            {
                agent.View?.PlayEmote(moods[i % moods.Length]);
                i++;
            }
        }

        static string Report()
        {
            var sim = UnityEngine.Object.FindFirstObjectByType<EcosystemSimulation>();
            if (sim == null) return "no simulation";

            int ready = 0;
            var behaviours = new List<string>();

            foreach (CreatureAgent agent in sim.Agents)
            {
                if (agent.View != null && agent.View.ModelReady) ready++;
                if (agent.Current != null) behaviours.Add(agent.Current.Id);
            }

            return $"{sim.Population} creatures, {ready} models loaded, " +
                   $"clock {sim.Clock.Label}, doing [{string.Join(", ", behaviours)}]" +
                   $"{ClearanceReport(sim)}{EmoteReport()}";
        }

        /// <summary>
        /// How far each creature's lowest point sits above the ground beneath it.
        ///
        /// Creatures standing slightly off the terrain is the kind of fault that survives
        /// every other check: nothing throws, the screenshot looks broadly right, and it
        /// only reads as wrong once you know to look. A number per creature makes it
        /// obvious, and distinguishes a real hover from a hop caught mid-arc.
        /// </summary>
        static string ClearanceReport(EcosystemSimulation sim)
        {
            var parts = new List<string>();

            foreach (CreatureAgent agent in sim.Agents)
            {
                if (agent.View == null || !agent.View.ModelReady) continue;

                // Measured from the posed mesh, not from Renderer.bounds: on a skinned
                // creature that box is padded so no animation can escape it, and a
                // padded box reports hover that is not there.
                if (!agent.View.TryGetModelBounds(out Bounds model)) continue;
                float lowest = model.min.y;

                // Ground height where the creature stands. Worth knowing what this number
                // is not: the model's lowest point is a corner of a box, and on sloping
                // ground the terrain under that corner is not the terrain under the
                // creature's feet. Anything inside about a tenth of a unit is that
                // difference rather than a creature off the floor -- taking the lowest
                // ground under the whole footprint instead just swaps the error for the
                // opposite one, and reported a skeleton standing on a bank as six
                // tenths of a unit airborne.
                float ground = sim.Surface.SampleHeight(agent.transform.position);

                // How far the scenery had to push this creature back out of itself. A
                // number here every frame means it is standing inside a tree.
                Vector3 resolved = sim.Surface.ResolveObstacles(
                    agent.transform.position, agent.Definition.bodyRadius);
                float intrusion = (resolved - agent.transform.position).magnitude;

                string tail = intrusion > 0.005f ? $" in-prop {intrusion:0.00}" : "";
                parts.Add($"{agent.Definition.id} {(lowest - ground):+0.00;-0.00}{tail}");
            }

            return parts.Count == 0 ? "" : $", clearance [{string.Join(", ", parts)}]";
        }

        /// <summary>
        /// The biggest visible interface elements that paint something.
        ///
        /// A stray full-screen graphic is invisible to every other check -- it compiles,
        /// it does not throw, and the layout is "correct" -- so the only way to catch one
        /// is to ask what is actually large and on screen.
        /// </summary>
        static string EmoteReport()
        {
            var document = UnityEngine.Object.FindFirstObjectByType<UIDocument>();
            if (document == null || document.rootVisualElement == null) return " | no ui";

            var found = new List<(float area, string description)>();
            Walk(document.rootVisualElement);

            found.Sort((a, b) => b.area.CompareTo(a.area));

            var lines = new List<string>();
            for (int i = 0; i < Mathf.Min(6, found.Count); i++) lines.Add(found[i].description);

            return " | painted ui: " + string.Join("; ", lines);

            void Walk(VisualElement element)
            {
                bool visible = element.resolvedStyle.display != DisplayStyle.None
                               && element.resolvedStyle.opacity > 0.01f
                               && element.visible;

                if (visible)
                {
                    Rect bounds = element.worldBound;
                    bool paints = element.resolvedStyle.backgroundImage.texture != null
                                  || element.resolvedStyle.backgroundImage.sprite != null
                                  || element.resolvedStyle.backgroundColor.a > 0.02f;

                    if (paints && bounds.width > 1f && bounds.height > 1f)
                    {
                        string id = string.IsNullOrEmpty(element.name)
                            ? element.GetType().Name
                            : element.name;

                        found.Add((bounds.width * bounds.height,
                            $"{id}[{string.Join(".", element.GetClasses())}] " +
                            $"{bounds.width:0}x{bounds.height:0}@{bounds.x:0},{bounds.y:0}"));
                    }

                    foreach (VisualElement child in element.Children()) Walk(child);
                }
            }
        }

        static void Finish()
        {
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;

            ReleaseTargets();
            if (_target != null)
            {
                _target.Release();
                UnityEngine.Object.DestroyImmediate(_target);
                _target = null;
            }
            EditorApplication.ExitPlaymode();

            // Give the editor a couple of frames to leave play mode before quitting, or
            // the exit races the teardown and the log ends mid-sentence.
            EditorApplication.delayCall += () => EditorApplication.delayCall += () =>
            {
                Debug.Log("[PlaymodeCapture] done");
                EditorApplication.Exit(0);
            };
        }
    }
}
