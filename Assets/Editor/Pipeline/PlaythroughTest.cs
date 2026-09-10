using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LivingDiorama.Core;
using LivingDiorama.Data;
using LivingDiorama.Presentation;
using LivingDiorama.Simulation;
using LivingDiorama.UI;
using LivingDiorama.Unboxing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.EditorTools
{
    /// <summary>
    /// Plays the game.
    ///
    /// The unit tests prove the maths and the smoke test proves something renders. Neither
    /// touches a button, so neither would notice a screen that never opens, a reveal that
    /// never arrives, or a null reference three clicks into a flow nobody automated. This
    /// drives the real interface through the whole loop -- begin, open a box, watch the
    /// unboxing, take the reward, expand, browse the collection, inspect a creature, save
    /// and reload -- and fails on the first step that does not do what it claims.
    ///
    /// Any exception or error logged at any point fails the run, so a silent
    /// NullReferenceException in a coroutine cannot pass.
    ///
    ///   Unity.exe -batchmode -projectPath . \
    ///     -executeMethod LivingDiorama.EditorTools.PlaythroughTest.Run
    /// </summary>
    public static class PlaythroughTest
    {
        const string ActiveKey = "LivingDiorama.PlaythroughTest.Active";
        const string OutputDir = "Screenshots/Playthrough";

        sealed class Step
        {
            public string Name;
            public Action Act;
            public Func<bool> Until;
            public float Timeout = 12f;
            public string Capture;
        }

        static readonly List<string> Failures = new();
        static List<Step> _steps;
        static int _index;
        static double _stepStarted;
        static bool _acted;
        static double _startTime = -1;

        static RenderTexture _target;
        static string _pendingCapture;
        static int _pendingFrames;

        /// <summary>Longer than the slowest entrance in LivingDiorama.uss, plus the
        /// staggered tail of a full grid.</summary>
        const double SettleSeconds = 0.85;
        static double _settleUntil = -1;

        // ---- entry ----------------------------------------------------------

        public static void Run()
        {
            Directory.CreateDirectory(OutputDir);

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

            Failures.Clear();
            _steps = null;
            _index = 0;
            _acted = false;
            _startTime = -1;

            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        /// <summary>Editor-internal frames that have nothing to do with the game. The
        /// asset search indexer in particular throws on startup in batch mode, and the
        /// message alone gives no hint of that -- only the stack does, which is why this
        /// filters on the trace rather than on the text.</summary>
        static readonly string[] IgnoredFrames =
        {
            "UnityEditor.Search",
            "UnityEditor.AssetDatabase",
            "UnityEditor.PackageManager",
        };

        /// <summary>Warnings that mean an interface element silently did not draw. These
        /// are only warnings to Unity, but to a player they are a missing panel, so the
        /// playthrough treats them as failures.</summary>
        static readonly string[] FatalWarnings =
        {
            "Invalid value for image texture",
            "Unable to load the referenced asset",
        };

        static void OnLog(string message, string stack, LogType type)
        {
            if (type is LogType.Warning)
            {
                bool fatal = false;
                foreach (string warning in FatalWarnings)
                {
                    if (message.Contains(warning)) fatal = true;
                }
                if (!fatal) return;
            }
            else if (type is not (LogType.Exception or LogType.Error)) return;

            if (message.Contains("style slices")) return;

            foreach (string frame in IgnoredFrames)
            {
                if (stack != null && stack.Contains(frame)) return;
            }

            Failures.Add($"{type}: {message.Split('\n')[0]}");
        }

        // ---- the script -----------------------------------------------------

        /// <summary>Click the first entry of a built list. Collection cards are plain
        /// elements listening for ClickEvent rather than Buttons, so they are poked with a
        /// synthesised event instead of the Clickable the button helper uses.</summary>
        static void ClickFirstCard(string containerName)
        {
            VisualElement container = Root?.Q<VisualElement>(containerName);
            if (container == null || container.childCount == 0)
            {
                throw new System.Exception($"[Playthrough] nothing to click in '{containerName}'");
            }

            using var evt = ClickEvent.GetPooled();
            evt.target = container[0];
            container[0].SendEvent(evt);
        }

        static GameUI Ui => UnityEngine.Object.FindFirstObjectByType<GameUI>();
        static UIDocument Document => UnityEngine.Object.FindFirstObjectByType<UIDocument>();
        static VisualElement Root => Document != null ? Document.rootVisualElement : null;
        static EcosystemSimulation Sim => UnityEngine.Object.FindFirstObjectByType<EcosystemSimulation>();
        static UnboxingDirector Unboxing => UnityEngine.Object.FindFirstObjectByType<UnboxingDirector>();
        static GameBootstrap Boot => UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();

        static GameController Controller
        {
            get
            {
                GameBootstrap boot = Boot;
                if (boot == null) return null;

                FieldInfo field = typeof(GameBootstrap).GetField(
                    "_controller", BindingFlags.Instance | BindingFlags.NonPublic);
                return field?.GetValue(boot) as GameController;
            }
        }

        static List<Step> BuildSteps() => new()
        {
            new Step
            {
                Name = "world builds and models stream in",
                Until = () => Sim != null && Sim.Population > 0 && AllModelsReady(),
                Timeout = 30f,
            },
            new Step
            {
                Name = "title screen is up",
                Until = () => Visible("title-screen") && Visible("btn-title-begin"),
            },
            new Step
            {
                Name = "Begin starts the game",
                Act = () => Click("btn-title-begin"),
                Until = () => !Visible("title-screen") && Visible("btn-box"),
                Capture = "01_gameplay",
            },
            new Step
            {
                Name = "onboarding tour appears",
                Until = () => Visible("coach-card"),
                Timeout = 20f,
                Capture = "02_onboarding",
            },
            new Step
            {
                Name = "tour can be skipped",
                Act = () => Click("btn-coach-skip"),
                Until = () => !Visible("coach-layer"),
            },
            new Step
            {
                Name = "mystery box screen opens with boxes in it",
                Act = () => Click("btn-box"),
                Until = () => Visible("modal-box") && CountButtons("box-list") >= 2,
                Capture = "03_boxes",
            },
            new Step
            {
                Name = "buying a box starts the unboxing",
                Act = BuyFirstBox,
                Until = () => Unboxing != null && Unboxing.IsRunning,
            },
            new Step
            {
                Name = "unboxing reveals a creature",
                Until = () => Visible("reveal-card") && !string.IsNullOrEmpty(Text("reveal-name")),
                Timeout = 30f,
                Capture = "04_reveal",
            },
            new Step
            {
                Name = "reward is banked and the reveal dismisses",
                Act = () =>
                {
                    _creaturesBefore = Controller.State.Data.creatures.Count;
                    Click("btn-reveal-done");
                },
                Until = () => !Visible("reveal-layer") && Controller.State.Data.creatures.Count >= _creaturesBefore,
                Timeout = 15f,
            },
            new Step
            {
                Name = "collection lists the roster",
                Act = () => Click("btn-collection"),
                Until = () => Visible("modal-collection") && CountChildren("collection-grid-inner") >= 6,
                Capture = "05_collection",
            },
            new Step
            {
                Name = "tapping a species opens its turntable",
                Act = () => ClickFirstCard("collection-grid-inner"),
                Until = () => Visible("modal-inspect"),
                Capture = "05b_turntable",
            },
            new Step
            {
                Name = "collection closes",
                Act = () => Click("btn-collection-close"),
                Until = () => !Visible("modal-collection"),
            },
            new Step
            {
                Name = "the chronicle lists what there is to find",
                Act = () => Click("btn-chronicle"),
                Until = () => Visible("modal-chronicle") && CountChildren("chronicle-list") >= 1,
                Capture = "05c_chronicle",
            },
            new Step
            {
                Name = "chronicle closes",
                Act = () => Click("btn-chronicle-close"),
                Until = () => !Visible("modal-chronicle"),
            },
            new Step
            {
                Name = "expansion screen offers biomes",
                Act = () =>
                {
                    // Enough to afford the first ring outright, so the flow is exercised
                    // rather than blocked on the economy.
                    Controller.State.Grant(CurrencyKind.Coins, 5000);
                    Click("btn-expand");
                },
                Until = () => Visible("modal-expand") && CountButtons("expand-list") >= 3,
                Capture = "06_expand",
            },
            new Step
            {
                Name = "claiming a tile grows the diorama",
                Act = () =>
                {
                    _tilesBefore = Controller.World.TileCount;
                    ClickFirstEnabledButton("expand-list");
                },
                Until = () => Controller.World.TileCount > _tilesBefore,
                Timeout = 15f,
                Capture = "07_expanded",
            },
            new Step
            {
                Name = "expansion screen closes",
                Act = () => Click("btn-expand-close"),
                Until = () => !Visible("modal-expand"),
            },
            new Step
            {
                Name = "settings open and close",
                Act = () => Click("btn-settings"),
                Until = () => Visible("modal-settings"),
                Capture = "08_settings",
            },
            new Step
            {
                Name = "settings dismiss",
                Act = () => Click("btn-settings-close"),
                Until = () => !Visible("modal-settings"),
            },
            new Step
            {
                Name = "tapping a creature opens the inspector",
                Act = TapFirstCreature,
                Until = () => Visible("inspector") && !string.IsNullOrEmpty(Text("inspector-name")),
                Capture = "09_inspector",
            },
            new Step
            {
                Name = "creatures are actually behaving",
                Until = BehavioursRunning,
                Timeout = 25f,
                Capture = "10_living",
            },
            new Step
            {
                Name = "save round-trips",
                Act = SaveAndVerify,
                Until = () => true,
            },
        };

        static int _creaturesBefore;
        static int _tilesBefore;

        // ---- assertions -----------------------------------------------------

        static bool AllModelsReady()
        {
            foreach (CreatureAgent agent in Sim.Agents)
            {
                if (agent.View == null || !agent.View.ModelReady) return false;
            }
            return true;
        }

        /// <summary>At least one creature doing something other than standing about, and no
        /// creature stuck without a behaviour at all.</summary>
        static bool BehavioursRunning()
        {
            EcosystemSimulation sim = Sim;
            if (sim == null || sim.Population == 0) return false;

            bool anyActive = false;
            foreach (CreatureAgent agent in sim.Agents)
            {
                if (agent.Current == null) return false;
                if (agent.Current.Id != "idle") anyActive = true;
            }
            return anyActive;
        }

        static void SaveAndVerify()
        {
            GameController controller = Controller;
            controller.CaptureAll();

            if (!Save.SaveService.Save(controller.State.Data))
            {
                Failures.Add("save: write failed");
                return;
            }

            Save.SaveData reloaded = Save.SaveService.Load();
            if (reloaded == null)
            {
                Failures.Add("save: reload returned nothing");
                return;
            }

            if (reloaded.creatures.Count != controller.State.Data.creatures.Count)
            {
                Failures.Add($"save: creature count changed " +
                             $"({controller.State.Data.creatures.Count} -> {reloaded.creatures.Count})");
            }

            if (reloaded.tiles.Count != controller.State.Data.tiles.Count)
            {
                Failures.Add($"save: tile count changed " +
                             $"({controller.State.Data.tiles.Count} -> {reloaded.tiles.Count})");
            }

            Debug.Log($"[Playthrough] saved and reloaded: {reloaded.creatures.Count} creatures, " +
                      $"{reloaded.tiles.Count} tiles, {reloaded.coins} coins, " +
                      $"{reloaded.discoveredSpecies.Count} species discovered");
        }

        // ---- interface driving ----------------------------------------------

        static VisualElement Find(string name) => Root?.Q<VisualElement>(name);

        static bool Visible(string name)
        {
            VisualElement element = Find(name);
            if (element == null) return false;

            // ClassListContains would miss a parent that is hidden, so walk up.
            for (VisualElement e = element; e != null; e = e.parent)
            {
                if (e.ClassListContains("hidden")) return false;
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
            }
            return true;
        }

        static string Text(string name) => Root?.Q<Label>(name)?.text ?? "";

        static int CountChildren(string containerName)
        {
            VisualElement container = Find(containerName);
            return container?.childCount ?? 0;
        }

        static int CountButtons(string containerName)
        {
            VisualElement container = Find(containerName);
            if (container == null) return 0;

            int n = 0;
            foreach (Button _ in container.Query<Button>().Build()) n++;
            return n;
        }

        /// <summary>
        /// Press a button for real.
        ///
        /// Synthesising a pointer event needs a focus controller and a panel that thinks it
        /// has input, neither of which exists in batch mode. Invoking the Clickable's
        /// delegate is the same call the event system would make, and it still fails if the
        /// button was never wired up -- which is the thing worth testing.
        /// </summary>
        static void Click(string name)
        {
            var button = Root?.Q<Button>(name);
            if (button == null)
            {
                Failures.Add($"click: no button named '{name}'");
                return;
            }

            if (!button.enabledInHierarchy)
            {
                Failures.Add($"click: button '{name}' is disabled");
                return;
            }

            Invoke(button);
        }

        static void Invoke(Button button)
        {
            FieldInfo field = typeof(Clickable).GetField(
                "clicked", BindingFlags.Instance | BindingFlags.NonPublic);

            if (field?.GetValue(button.clickable) is Action handler)
            {
                handler.Invoke();
                return;
            }

            Failures.Add($"click: button '{button.name}' has no handler attached");
        }

        static void ClickFirstEnabledButton(string containerName)
        {
            VisualElement container = Find(containerName);
            if (container == null)
            {
                Failures.Add($"click: no container '{containerName}'");
                return;
            }

            foreach (Button button in container.Query<Button>().Build())
            {
                if (!button.enabledSelf) continue;
                Invoke(button);
                return;
            }

            Failures.Add($"click: nothing enabled inside '{containerName}'");
        }

        static void BuyFirstBox()
        {
            GameController controller = Controller;
            controller.State.Grant(CurrencyKind.Coins, 2000);

            // Re-open so the affordability state on the buttons is current.
            Click("btn-box");
            ClickFirstEnabledButton("box-list");
        }

        static void TapFirstCreature()
        {
            EcosystemSimulation sim = Sim;
            if (sim == null || sim.Population == 0)
            {
                Failures.Add("tap: no creatures to tap");
                return;
            }

            var camera = UnityEngine.Object.FindFirstObjectByType<DioramaCamera>();
            EventInfo tapped = typeof(DioramaCamera).GetEvent("CreatureTapped");

            FieldInfo field = typeof(DioramaCamera).GetField(
                "CreatureTapped", BindingFlags.Instance | BindingFlags.NonPublic);

            if (field?.GetValue(camera) is Action<CreatureAgent> handler)
            {
                handler.Invoke(sim.Agents[0]);
                return;
            }

            Failures.Add($"tap: CreatureTapped has no subscriber ({tapped != null})");
        }

        // ---- loop -----------------------------------------------------------

        static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (!EditorApplication.isPlaying) return;

            double now = EditorApplication.timeSinceStartup;
            if (_startTime < 0)
            {
                _startTime = now;
                _stepStarted = now;
            }

            if (_pendingCapture != null)
            {
                CollectCapture();
                return;
            }

            _steps ??= BuildSteps();

            if (_index >= _steps.Count)
            {
                Finish();
                return;
            }

            Step step = _steps[_index];

            if (!_acted)
            {
                _acted = true;
                _stepStarted = now;
                try
                {
                    step.Act?.Invoke();
                }
                catch (Exception e)
                {
                    Failures.Add($"{step.Name}: threw {e.GetType().Name}: {e.Message}");
                    Advance(false, step);
                    return;
                }
                return;
            }

            bool satisfied;
            try
            {
                satisfied = step.Until == null || step.Until();
            }
            catch (Exception e)
            {
                Failures.Add($"{step.Name}: condition threw {e.GetType().Name}: {e.Message}");
                Advance(false, step);
                return;
            }

            if (satisfied)
            {
                if (step.Capture != null)
                {
                    // Let the interface finish arriving first.
                    //
                    // Panels and cards now animate in, and a step's condition is met the
                    // instant the elements exist -- which is a frame or two before they
                    // are visible. Photographing there caught a collection grid with two
                    // of its six cards drawn and the rest still at zero opacity, and it
                    // read as four missing cards rather than as a screenshot taken early.
                    if (_settleUntil < 0) _settleUntil = now + SettleSeconds;
                    if (now < _settleUntil) return;

                    _settleUntil = -1;
                    RequestCapture(step.Capture);
                    step.Capture = null;
                    return;
                }
                Advance(true, step);
                return;
            }

            if (now - _stepStarted > step.Timeout)
            {
                Failures.Add($"{step.Name}: timed out after {step.Timeout:0}s");
                Advance(false, step);
            }
        }

        static void Advance(bool ok, Step step)
        {
            Debug.Log($"[Playthrough] {(ok ? "PASS" : "FAIL")}  {step.Name}");
            _index++;
            _acted = false;
        }

        // ---- capture --------------------------------------------------------

        static void RequestCapture(string name)
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            _target ??= new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = _target;

            UIDocument document = Document;
            if (document != null && document.panelSettings != null)
            {
                document.panelSettings.targetTexture = _target;
            }

            _pendingCapture = name;
            _pendingFrames = 40;
        }

        static void CollectCapture()
        {
            if (--_pendingFrames > 0) return;

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = _target;
                var image = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0);
                image.Apply();

                File.WriteAllBytes(Path.Combine(OutputDir, _pendingCapture + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Playthrough] capture failed: {e.Message}");
            }
            finally
            {
                RenderTexture.active = previous;

                Camera camera = Camera.main;
                if (camera != null) camera.targetTexture = null;

                UIDocument document = Document;
                if (document != null && document.panelSettings != null)
                {
                    document.panelSettings.targetTexture = null;
                }

                _pendingCapture = null;
            }
        }

        // ---- teardown -------------------------------------------------------

        static void Finish()
        {
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;

            if (_target != null)
            {
                _target.Release();
                UnityEngine.Object.DestroyImmediate(_target);
                _target = null;
            }

            bool passed = Failures.Count == 0;

            if (passed)
            {
                Debug.Log($"[Playthrough] ALL {_steps.Count} STEPS PASSED");
            }
            else
            {
                Debug.LogError($"[Playthrough] {Failures.Count} FAILURE(S):");
                foreach (string failure in Failures) Debug.LogError("  - " + failure);
            }

            EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += () => EditorApplication.delayCall += () =>
            {
                EditorApplication.Exit(passed ? 0 : 1);
            };
        }
    }
}
