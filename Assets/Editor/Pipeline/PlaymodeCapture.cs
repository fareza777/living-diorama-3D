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
            (26f, "04_turntable"),
            (34f, "05_night"),
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
            if (_elapsed > 20f) SetTurntable(true);

            // A capture spans several frames, so nothing else is scheduled until it lands.
            if (_pendingName != null)
            {
                CollectCapture();
                return;
            }

            if (_next < Schedule.Length && _elapsed >= Schedule[_next].at)
            {
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

                float lowest = float.MaxValue;
                foreach (Renderer r in agent.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer) continue;
                    lowest = Mathf.Min(lowest, r.bounds.min.y);
                }
                if (lowest > float.MaxValue * 0.5f) continue;

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
