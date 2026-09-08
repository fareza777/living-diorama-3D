using System;
using LivingDiorama.Data;
using UnityEngine;

namespace LivingDiorama.Ads
{
    /// <summary>
    /// Decides when an interstitial is allowed.
    ///
    /// This is a game about sitting and watching a tiny world; an ad that interrupts that
    /// does more damage than the impression is worth. So interstitials only ever appear
    /// after a box opening -- a natural break the player initiated -- and only every Nth
    /// one, with a hard floor on real time between shows.
    /// </summary>
    public sealed class AdPacing
    {
        readonly ProgressionSettings _settings;
        readonly Func<double> _nowSeconds;

        int _opensSinceInterstitial;
        double _lastInterstitialAt = double.NegativeInfinity;

        public AdPacing(ProgressionSettings settings, Func<double> nowSeconds = null)
        {
            _settings = settings;
            _nowSeconds = nowSeconds ?? (() => Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>Call after every box open. Returns true when an interstitial should show.</summary>
        public bool ShouldShowInterstitialAfterBoxOpen()
        {
            _opensSinceInterstitial++;

            int every = _settings.interstitialEveryNBoxOpens;
            if (every <= 0) return false;
            if (_opensSinceInterstitial < every) return false;

            double now = _nowSeconds();
            if (now - _lastInterstitialAt < _settings.interstitialMinSecondsBetween) return false;

            _opensSinceInterstitial = 0;
            _lastInterstitialAt = now;
            return true;
        }

        /// <summary>Never show an interstitial on the very first session moments -- the
        /// first impression of the game should be the game.</summary>
        public void SuppressForSeconds(double seconds)
        {
            _lastInterstitialAt = _nowSeconds() - _settings.interstitialMinSecondsBetween + seconds;
        }
    }
}
