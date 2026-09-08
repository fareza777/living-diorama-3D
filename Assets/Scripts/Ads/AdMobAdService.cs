// The real AdMob implementation.
//
// Compiled only when LD_ADMOB is defined, because it needs the Google Mobile Ads Unity
// plugin, which is not a UPM package and therefore cannot be a project dependency:
//
//   1. download the plugin from
//      https://github.com/googleads/googleads-mobile-unity/releases
//   2. import the .unitypackage
//   3. set the app id under Assets > Google Mobile Ads > Settings
//   4. add LD_ADMOB to Scripting Define Symbols for Android
//
// Until then the game runs on StubAdService and every reward flow still works.

#if LD_ADMOB
using System;
using GoogleMobileAds.Api;
using UnityEngine;

namespace LivingDiorama.Ads
{
    public sealed class AdMobAdService : IAdService
    {
        readonly string _rewardedUnitId;
        readonly string _interstitialUnitId;

        RewardedAd _rewarded;
        InterstitialAd _interstitial;

        public AdMobAdService(string rewardedUnitId, string interstitialUnitId)
        {
            _rewardedUnitId = rewardedUnitId;
            _interstitialUnitId = interstitialUnitId;
        }

        public bool RewardedReady => _rewarded != null && _rewarded.CanShowAd();
        public bool InterstitialReady => _interstitial != null && _interstitial.CanShowAd();

        public void Initialise()
        {
            MobileAds.Initialize(_ =>
            {
                LoadRewarded();
                LoadInterstitial();
            });
        }

        void LoadRewarded()
        {
            _rewarded?.Destroy();
            _rewarded = null;

            RewardedAd.Load(_rewardedUnitId, new AdRequest(), (ad, error) =>
            {
                if (error != null || ad == null)
                {
                    Debug.LogWarning($"[Ads] rewarded load failed: {error}");
                    return;
                }

                _rewarded = ad;
                // Always queue the next one as soon as this one is spent, so the button is
                // rarely greyed out when the player wants it.
                ad.OnAdFullScreenContentClosed += LoadRewarded;
                ad.OnAdFullScreenContentFailed += _ => LoadRewarded();
            });
        }

        void LoadInterstitial()
        {
            _interstitial?.Destroy();
            _interstitial = null;

            InterstitialAd.Load(_interstitialUnitId, new AdRequest(), (ad, error) =>
            {
                if (error != null || ad == null)
                {
                    Debug.LogWarning($"[Ads] interstitial load failed: {error}");
                    return;
                }

                _interstitial = ad;
                ad.OnAdFullScreenContentClosed += LoadInterstitial;
                ad.OnAdFullScreenContentFailed += _ => LoadInterstitial();
            });
        }

        public void ShowRewarded(AdPlacement placement, Action<bool> onFinished)
        {
            if (!RewardedReady)
            {
                onFinished?.Invoke(false);
                LoadRewarded();
                return;
            }

            bool earned = false;
            _rewarded.OnAdFullScreenContentClosed += () => onFinished?.Invoke(earned);
            _rewarded.Show(_ => earned = true);
        }

        public void ShowInterstitial(Action onClosed)
        {
            if (!InterstitialReady)
            {
                onClosed?.Invoke();
                LoadInterstitial();
                return;
            }

            _interstitial.OnAdFullScreenContentClosed += () => onClosed?.Invoke();
            _interstitial.Show();
        }
    }
}
#endif
