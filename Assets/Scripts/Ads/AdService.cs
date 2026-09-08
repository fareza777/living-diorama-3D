using System;
using UnityEngine;

namespace LivingDiorama.Ads
{
    public enum AdPlacement
    {
        FreeBoxOpen,
        DoubleOfflineEarnings,
        CoinTopUp,
    }

    /// <summary>
    /// Everything the game knows about advertising. Deliberately tiny: the game asks for
    /// a rewarded view and is told whether the reward was earned. Whether that came from
    /// AdMob, a stub, or a future mediation layer is not the game's problem.
    /// </summary>
    public interface IAdService
    {
        bool RewardedReady { get; }
        bool InterstitialReady { get; }

        void Initialise();

        /// <summary>Callback receives true only if the user watched far enough to earn it.</summary>
        void ShowRewarded(AdPlacement placement, Action<bool> onFinished);

        void ShowInterstitial(Action onClosed);
    }

    /// <summary>
    /// Used in the editor and in any build without the AdMob plugin. Grants rewards
    /// immediately so the reward flows stay testable without a network or a real ad.
    /// </summary>
    public sealed class StubAdService : IAdService
    {
        public bool RewardedReady => true;
        public bool InterstitialReady => true;

        public void Initialise() =>
            Debug.Log("[Ads] stub service active -- rewards granted without showing an ad");

        public void ShowRewarded(AdPlacement placement, Action<bool> onFinished)
        {
            Debug.Log($"[Ads] stub rewarded for {placement}");
            onFinished?.Invoke(true);
        }

        public void ShowInterstitial(Action onClosed)
        {
            Debug.Log("[Ads] stub interstitial");
            onClosed?.Invoke();
        }
    }

    /// <summary>Global access point. Set once during bootstrap. Named AdHub rather
    /// than Ads so it never shadows the LivingDiorama.Ads namespace at a call site.</summary>
    public static class AdHub
    {
        static IAdService _current;

        public static IAdService Current
        {
            get => _current ??= new StubAdService();
            set => _current = value;
        }

        public static void Reset() => _current = null;
    }
}
