using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LivingDiorama.Core
{
    /// <summary>
    /// Picks a rendering budget for the device at hand.
    ///
    /// A diorama full of animated creatures on a five-year-old phone and on a flagship
    /// are very different problems, and the honest fix is to render fewer pixels rather
    /// than to drop the art direction. Render scale is the first thing to give.
    /// </summary>
    public static class QualityTuner
    {
        public enum Tier { Low, Medium, High }

        public static Tier CurrentTier { get; private set; } = Tier.Medium;

        public static void Apply()
        {
            CurrentTier = DetectTier();

            Application.targetFrameRate = CurrentTier == Tier.Low ? 30 : 60;
            QualitySettings.vSyncCount = 0;

            // Watching a diorama involves long stretches without touching the screen.
            Screen.sleepTimeout = SleepTimeout.SystemSetting;

            QualitySettings.shadowDistance = CurrentTier switch
            {
                Tier.Low => 18f,
                Tier.Medium => 28f,
                _ => 40f,
            };

            QualitySettings.shadowCascades = 1;
            QualitySettings.skinWeights = SkinWeights.TwoBones;
            QualitySettings.softParticles = false;

            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = CurrentTier switch
                {
                    Tier.Low => 0.7f,
                    Tier.Medium => 0.85f,
                    _ => 1f,
                };

                urp.msaaSampleCount = CurrentTier == Tier.Low ? 1 : 4;
                urp.shadowDistance = QualitySettings.shadowDistance;
                urp.supportsHDR = CurrentTier != Tier.Low;
            }

            Debug.Log($"[QualityTuner] tier={CurrentTier} " +
                      $"mem={SystemInfo.systemMemorySize}MB gpu={SystemInfo.graphicsDeviceName}");
        }

        static Tier DetectTier()
        {
#if UNITY_EDITOR
            return Tier.High;
#else
            int memory = SystemInfo.systemMemorySize;
            int cores = SystemInfo.processorCount;

            if (memory <= 0) return Tier.Medium;              // unknown: assume mid
            if (memory < 3000 || cores <= 4) return Tier.Low;
            if (memory < 6000) return Tier.Medium;
            return Tier.High;
#endif
        }
    }
}
