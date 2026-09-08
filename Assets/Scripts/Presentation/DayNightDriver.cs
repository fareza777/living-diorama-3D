using LivingDiorama.Data;
using LivingDiorama.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace LivingDiorama.Presentation
{
    /// <summary>
    /// Drives the sun, the ambient light and the fog from the world clock.
    ///
    /// The day/night cycle is not decoration: nocturnal creatures wake when it gets dark,
    /// so the player has to be able to read the time of day at a glance from the lighting
    /// alone. Everything is interpolated rather than switched, so dusk actually feels
    /// like dusk.
    /// </summary>
    public sealed class DayNightDriver : MonoBehaviour
    {
        [Header("Sun colour")]
        [SerializeField] Gradient _sunColour;
        [SerializeField] AnimationCurve _sunIntensity = AnimationCurve.Linear(0f, 0.15f, 1f, 1.35f);

        [Header("Ambient")]
        [SerializeField] Gradient _ambientSky;
        [SerializeField] Gradient _ambientGround;

        [Header("Fog")]
        [SerializeField] Gradient _fogColour;

        Light _sun;
        WorldClock _clock;
        SimulationSettings _settings;
        BiomeDefinition _dominantBiome;

        public void Initialise(Light sun, WorldClock clock, SimulationSettings settings)
        {
            _sun = sun;
            _clock = clock;
            _settings = settings;

            _sunColour ??= BuildDefaultSunGradient();
            _ambientSky ??= BuildDefaultSkyGradient();
            _ambientGround ??= BuildDefaultGroundGradient();
            _fogColour ??= BuildDefaultFogGradient();

            Apply();
        }

        /// <summary>The biome under the camera tints the atmosphere, so moving from forest
        /// to snow reads as a change of place, not just a change of ground colour.</summary>
        public void SetDominantBiome(BiomeDefinition biome) => _dominantBiome = biome;

        void LateUpdate()
        {
            if (_clock == null || _sun == null) return;
            Apply();
        }

        void Apply()
        {
            float t = _clock.NormalisedTime;
            float daylight = _settings.DaylightAt(t);

            _sun.transform.rotation = _clock.SunRotation;
            _sun.color = _sunColour.Evaluate(t);
            _sun.intensity = _sunIntensity.Evaluate(daylight);

            // Moonlit nights stay blue and legible rather than going pitch black.
            if (_dominantBiome != null)
            {
                _sun.color = Color.Lerp(
                    _dominantBiome.sunTintNight * _sun.color,
                    _dominantBiome.sunTintDay * _sun.color,
                    daylight);
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            Color sky = _ambientSky.Evaluate(t);
            Color ground = _ambientGround.Evaluate(t);

            if (_dominantBiome != null && _dominantBiome.ambientLift > 0f)
            {
                sky = Color.Lerp(sky, Color.white, _dominantBiome.ambientLift * 0.4f);
                ground = Color.Lerp(ground, Color.white, _dominantBiome.ambientLift * 0.25f);
            }

            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = Color.Lerp(sky, ground, 0.5f);
            RenderSettings.ambientGroundColor = ground;

            Color fog = _fogColour.Evaluate(t);
            if (_dominantBiome != null) fog = Color.Lerp(fog, _dominantBiome.fogColour, 0.5f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = _dominantBiome != null
                ? Mathf.Lerp(_dominantBiome.fogDensity * 1.7f, _dominantBiome.fogDensity, daylight)
                : Mathf.Lerp(0.028f, 0.014f, daylight);

            Camera main = Camera.main;
            if (main != null) main.backgroundColor = fog;
        }

        // ---- default gradients ---------------------------------------------
        // Authored in code so the game looks right from a fresh checkout with no
        // scene set-up. They can still be overridden in the inspector.

        static Gradient Build(params (float time, Color colour)[] keys)
        {
            var gradient = new Gradient();
            var colourKeys = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                colourKeys[i] = new GradientColorKey(keys[i].colour, keys[i].time);
            }
            gradient.SetKeys(colourKeys, new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f),
            });
            return gradient;
        }

        static Gradient BuildDefaultSunGradient() => Build(
            (0.00f, new Color(0.42f, 0.50f, 0.86f)),   // deep night
            (0.22f, new Color(0.72f, 0.55f, 0.62f)),   // pre-dawn
            (0.28f, new Color(1.00f, 0.76f, 0.55f)),   // sunrise
            (0.45f, new Color(1.00f, 0.97f, 0.90f)),   // late morning
            (0.55f, new Color(1.00f, 0.98f, 0.93f)),   // noon
            (0.74f, new Color(1.00f, 0.72f, 0.46f)),   // golden hour
            (0.82f, new Color(0.66f, 0.50f, 0.72f)),   // dusk
            (1.00f, new Color(0.42f, 0.50f, 0.86f)));

        static Gradient BuildDefaultSkyGradient() => Build(
            (0.00f, new Color(0.10f, 0.13f, 0.26f)),
            (0.27f, new Color(0.36f, 0.34f, 0.46f)),
            (0.50f, new Color(0.55f, 0.68f, 0.86f)),
            (0.78f, new Color(0.44f, 0.38f, 0.50f)),
            (1.00f, new Color(0.10f, 0.13f, 0.26f)));

        static Gradient BuildDefaultGroundGradient() => Build(
            (0.00f, new Color(0.06f, 0.07f, 0.13f)),
            (0.50f, new Color(0.30f, 0.29f, 0.24f)),
            (1.00f, new Color(0.06f, 0.07f, 0.13f)));

        static Gradient BuildDefaultFogGradient() => Build(
            (0.00f, new Color(0.07f, 0.09f, 0.18f)),
            (0.27f, new Color(0.52f, 0.44f, 0.50f)),
            (0.50f, new Color(0.70f, 0.80f, 0.90f)),
            (0.79f, new Color(0.60f, 0.44f, 0.44f)),
            (1.00f, new Color(0.07f, 0.09f, 0.18f)));
    }
}
