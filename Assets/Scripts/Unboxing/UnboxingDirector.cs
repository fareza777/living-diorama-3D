using System;
using System.Collections;
using System.Threading.Tasks;
using LivingDiorama.Audio;
using LivingDiorama.Data;
using LivingDiorama.Meta;
using LivingDiorama.Presentation;
using UnityEngine;

namespace LivingDiorama.Unboxing
{
    /// <summary>
    /// Directs the unboxing as a shot, not an animation.
    ///
    /// The beats are anticipation, break, and reveal, in that order and with real pauses
    /// between them. Three escalating shakes before the lid goes is what makes the lid
    /// going feel like something; cutting straight to the creature would be faster and
    /// would be worth nothing. Rarity changes the colour, the light, the number of sparks
    /// and the length of the hold, so a Legendary is felt before it is read.
    /// </summary>
    public sealed class UnboxingDirector : MonoBehaviour
    {
        UnboxingStage _stage;
        DioramaCamera _camera;
        CreatureFactory _factory;

        GameObject _revealModel;
        (Vector3 pivot, float yaw, float pitch, float distance) _cameraSnapshot;
        bool _running;

        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        /// <summary>Full-screen flash: colour and duration. The UI layer draws it.</summary>
        public event Action<Color, float> FlashRequested;

        /// <summary>Fired once the creature is fully revealed, so the name card can appear.</summary>
        public event Action<LootRoller.Result> Revealed;

        public bool IsRunning => _running;

        public static UnboxingDirector Create(Transform parent, DioramaCamera camera,
                                              CreatureFactory factory, Vector3 stagePosition)
        {
            var go = new GameObject("Unboxing");
            go.transform.SetParent(parent, false);

            var director = go.AddComponent<UnboxingDirector>();
            director._camera = camera;
            director._factory = factory;
            director._stage = UnboxingStage.Create(go.transform, stagePosition);
            return director;
        }

        /// <summary>Run the whole sequence. Safe to call while one is already playing --
        /// the request is simply ignored rather than stacking two reveals.</summary>
        public void Play(LootRoller.Result result)
        {
            if (_running || !result.IsValid) return;
            StartCoroutine(Sequence(result));
        }

        /// <summary>Tear the set down and hand the camera back. Called when the player
        /// dismisses the reveal card.</summary>
        public void Dismiss()
        {
            if (!_running) return;
            StartCoroutine(Exit());
        }

        IEnumerator Sequence(LootRoller.Result result)
        {
            _running = true;

            AudioDirector audio = AudioDirector.Instance;
            Rarity rarity = result.Rarity;

            // ---- set up ------------------------------------------------------
            _stage.SetVisible(true);
            _stage.ApplyRarity(rarity);
            _stage.SetSeamGlow(0f);
            _stage.SetBeam(0f);
            _stage.ChestRoot.localScale = Vector3.zero;
            _stage.ChestRoot.localRotation = Quaternion.identity;
            _stage.ResetLid();

            _cameraSnapshot = _camera.Snapshot();
            _camera.SetMode(DioramaCamera.Mode.Scripted);
            _camera.SetScriptedShot(_stage.StagePosition, _cameraSnapshot.yaw + 25f, 14f, 3.4f);

            // Let the camera actually travel; cutting instantly would lose the sense of
            // going somewhere else for this.
            yield return new WaitForSecondsRealtime(0.55f);

            // ---- the box arrives ---------------------------------------------
            audio?.PlaySfx("creature_spawn", 0.9f);
            yield return ScaleIn(_stage.ChestRoot, 0.45f);
            _stage.StartMotes();

            yield return Hover(0.5f);

            // ---- anticipation ------------------------------------------------
            // Three knocks, each harder than the last, each with the seam a little
            // brighter. By the third the box is visibly losing the argument.
            const int knocks = 3;
            for (int i = 0; i < knocks; i++)
            {
                float strength = 0.35f + i * 0.32f;

                audio?.PlaySfx("creature_hurt", 0.5f + i * 0.2f, 0.10f);
                _camera.Shake(0.035f * strength, 0.22f);

                yield return Knock(strength, 0.22f);
                yield return GlowTo((i + 1) / (float)knocks * 2.4f, 0.20f);
                yield return new WaitForSecondsRealtime(0.30f - i * 0.06f);
            }

            // A held beat before the break. Silence is doing the work here.
            yield return new WaitForSecondsRealtime(0.22f);

            // ---- the break ---------------------------------------------------
            audio?.PlaySfx("box_open", 1f);
            audio?.PlaySfx(RevealSfx(rarity), 1f);

            _stage.EmitBurst();
            _stage.SetBeam(RarityBeam(rarity));
            _camera.Shake(0.16f + (int)rarity * 0.03f, 0.5f);
            FlashRequested?.Invoke(UnboxingStage.RarityColour(rarity), 0.34f);

            StartCoroutine(FlingLid());
            yield return GlowTo(6f, 0.12f);

            // ---- the reveal --------------------------------------------------
            yield return BuildRevealModel(result.Creature);

            // Pull back and lift as the creature rises: the shot opens up to make room for
            // it, rather than the creature growing inside a fixed frame.
            _camera.SetScriptedShot(_stage.StagePosition + Vector3.up * 0.55f,
                _cameraSnapshot.yaw + 25f, 17f, 4.4f);
            yield return RiseAndMaterialise(result.Creature, 1.0f);

            yield return GlowTo(0f, 0.55f);
            StartCoroutine(FadeBeam(0.9f));

            // Legendary pulls hold longer before the card lands. The pause is the reward.
            yield return new WaitForSecondsRealtime(rarity >= Rarity.Epic ? 0.7f : 0.35f);

            Revealed?.Invoke(result);
        }

        static string RevealSfx(Rarity rarity) => rarity switch
        {
            Rarity.Legendary or Rarity.Epic => "reveal_legendary",
            Rarity.Rare or Rarity.Uncommon => "reveal_rare",
            _ => "reveal_common",
        };

        static float RarityBeam(Rarity rarity) => 1.1f + (int)rarity * 0.55f;

        // ---- beats ----------------------------------------------------------

        IEnumerator ScaleIn(Transform target, float duration)
        {
            float rest = _stage.ChestScale;
            float t = 0f;

            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                // Overshoot then settle, so the box lands with weight.
                float s = 1f + Mathf.Sin(k * Mathf.PI) * 0.18f;
                target.localScale = Vector3.one * (Mathf.SmoothStep(0f, 1f, k) * s * rest);
                yield return null;
            }

            target.localScale = Vector3.one * rest;
        }

        IEnumerator Hover(float duration)
        {
            float t = 0f;
            Vector3 basePosition = _stage.ChestRoot.localPosition;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _stage.ChestRoot.localPosition = basePosition + Vector3.up * (Mathf.Sin(t * 2.4f) * 0.02f);
                _stage.ChestRoot.Rotate(Vector3.up, 22f * Time.unscaledDeltaTime, Space.Self);
                yield return null;
            }
        }

        IEnumerator Knock(float strength, float duration)
        {
            Quaternion start = _stage.ChestRoot.localRotation;
            Vector3 basePosition = _stage.ChestRoot.localPosition;
            float t = 0f;

            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = t / duration;
                // One sharp impulse that decays, rather than a symmetric wobble.
                float impulse = Mathf.Sin(k * Mathf.PI * 3f) * (1f - k);

                _stage.ChestRoot.localRotation = start * Quaternion.Euler(
                    impulse * 9f * strength, 0f, impulse * 11f * strength);
                _stage.ChestRoot.localPosition = basePosition +
                    Vector3.up * (Mathf.Abs(impulse) * 0.07f * strength);

                yield return null;
            }

            _stage.ChestRoot.localRotation = start;
            _stage.ChestRoot.localPosition = basePosition;
        }

        IEnumerator GlowTo(float target, float duration)
        {
            float start = _currentGlow;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _currentGlow = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / duration));
                _stage.SetSeamGlow(_currentGlow);
                yield return null;
            }
            _currentGlow = target;
            _stage.SetSeamGlow(target);
        }

        float _currentGlow;

        /// <summary>
        /// The lid swings on its hinge first, then tears off.
        ///
        /// A lid that simply flies away reads as a box exploding; a lid that hinges open
        /// and only then gives way reads as a chest being opened, which is the thing the
        /// player paid for. The pivot is the real hinge on the model, so this works the
        /// same on the generated chest and the modelled one.
        /// </summary>
        IEnumerator FlingLid()
        {
            Transform lid = _stage.LidRoot;
            Vector3 start = lid.localPosition;
            Quaternion closed = lid.localRotation;

            // --- swing ---------------------------------------------------------
            const float swing = 0.42f;
            float t = 0f;
            while (t < swing)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / swing);
                // Fast off the latch, easing as it reaches the top of its arc.
                float eased = 1f - Mathf.Pow(1f - k, 3f);
                lid.localRotation = closed * Quaternion.Euler(-118f * eased, 0f, 0f);
                yield return null;
            }

            // --- tear off ------------------------------------------------------
            Vector3 spinAxis = new Vector3(UnityEngine.Random.Range(-1f, 1f), 0.4f, 1f).normalized;
            const float flight = 0.85f;
            t = 0f;

            while (t < flight)
            {
                t += Time.unscaledDeltaTime;
                float k = t / flight;

                float height = 1.7f * k - 2.4f * k * k;
                lid.localPosition = start + new Vector3(0.3f * k, height, -0.45f * k);
                lid.Rotate(spinAxis, 540f * Time.unscaledDeltaTime, Space.Self);

                yield return null;
            }

            lid.gameObject.SetActive(false);
        }

        IEnumerator FadeBeam(float duration)
        {
            float t = 0f;
            float start = _stageBeam;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _stageBeam = Mathf.Lerp(start, 0f, t / duration);
                _stage.SetBeam(_stageBeam);
                yield return null;
            }
            _stage.SetBeam(0f);
        }

        float _stageBeam;

        // ---- the creature ---------------------------------------------------

        IEnumerator BuildRevealModel(CreatureDefinition definition)
        {
            if (_revealModel != null) Destroy(_revealModel);

            Task<GameObject> task = _factory.CreateAsync(definition, _stage.CreatureAnchor);
            while (!task.IsCompleted) yield return null;

            _revealModel = task.IsCompletedSuccessfully ? task.Result : null;
            if (_revealModel == null) yield break;

            // Present every species at one height, whatever its size in the diorama, so a
            // slime and a dragon get the same portrait framing. The factory has already
            // normalised the model to its body height, so this scales from there.
            //
            // The number matters: the reveal shot sits about four units back with a 42
            // degree field of view, which is roughly three units of visible height. At
            // 1.15 the creature fills a little over a third of the frame and the chest
            // stays in shot beneath it. Larger and the camera ends up inside the model.
            const float portraitHeight = 1.15f;
            float scale = portraitHeight / Mathf.Max(0.2f, definition.bodyHeight);
            _revealModel.transform.localScale *= scale;
            SetDissolve(1f);
        }

        IEnumerator RiseAndMaterialise(CreatureDefinition definition, float duration)
        {
            if (_revealModel == null)
            {
                yield return new WaitForSecondsRealtime(duration);
                yield break;
            }

            Transform model = _revealModel.transform;
            Vector3 start = new(0f, -0.25f, 0f);
            Vector3 end = new(0f, 0.30f, 0f);
            float t = 0f;

            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                float eased = 1f - Mathf.Pow(1f - k, 3f);

                model.localPosition = Vector3.Lerp(start, end, eased);
                // A full turn on the way up, landing back where it started so the creature
                // ends the shot facing the player rather than at whatever angle the sweep
                // happened to stop on.
                model.localRotation = Quaternion.Euler(0f, 180f + eased * 360f, 0f);

                // Materialise a touch ahead of the rise so it is solid before it settles.
                SetDissolve(Mathf.Clamp01(1f - k * 1.35f));
                yield return null;
            }

            model.localPosition = end;
            SetDissolve(0f);
        }

        void SetDissolve(float amount)
        {
            if (_revealModel == null) return;

            var renderers = _revealModel.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m != null && m.HasProperty(DissolveId)) m.SetFloat(DissolveId, amount);
                }
            }
        }

        // ---- teardown -------------------------------------------------------

        IEnumerator Exit()
        {
            const float duration = 0.45f;
            float t = 0f;

            _stage.StopMotes();

            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = t / duration;
                SetDissolve(k);
                _stage.ChestRoot.localScale = Vector3.one * (_stage.ChestScale * (1f - k));
                yield return null;
            }

            if (_revealModel != null)
            {
                Destroy(_revealModel);
                _revealModel = null;
            }

            _stage.LidRoot.gameObject.SetActive(true);
            _stage.SetBeam(0f);
            _stage.SetSeamGlow(0f);
            _currentGlow = 0f;
            _stageBeam = 0f;
            _stage.SetVisible(false);

            _camera.Restore(_cameraSnapshot);
            _camera.SetMode(DioramaCamera.Mode.Interactive);

            _running = false;
        }
    }
}
