namespace LivingDiorama.Ads
{
    /// <summary>
    /// AdMob unit ids.
    ///
    /// These are Google's public test ids. They are safe to commit and correct for
    /// development. Shipping them to production serves test ads and earns nothing;
    /// shipping live ids in a debug build gets an account flagged for invalid traffic.
    /// Swap them here as part of the release checklist and keep the live ids out of
    /// version control.
    /// </summary>
    public static class AdIds
    {
        public const string RewardedUnitId = "ca-app-pub-3940256099942544/5224354917";
        public const string InterstitialUnitId = "ca-app-pub-3940256099942544/1033173712";
    }
}
