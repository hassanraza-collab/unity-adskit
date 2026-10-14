namespace NineDTech.AdsKit.Ads
{
    /// <summary>Runtime copy of a placement asset after Remote Config overrides. Never written back to the asset.</summary>
    public sealed class PlacementRuntime
    {
        public string Id;
        public AdFormat Format;
        public bool Enabled;
        public int MinSessions;
        public int MinLevel;

        // Interstitial / rewarded / app-open resume
        public string UnitKey;
        public float CooldownSec;
        public bool Priority;
        public float MinBackgroundSec;
        public float WelcomeBackMinSec;
        public float TimeoutSec;

        // Banner
        public string StandardUnitKey;
        public string AdaptiveUnitKey;
        public BannerType BannerType;
        public BannerPosition Position;
        public bool PauseRefreshDuringFullscreen;

        // Splash
        public bool IsSplash;
        public SplashAdFormat SplashFormat;
        public string AppOpenUnitKey;
        public string InterstitialUnitKey;
        public float TriggerProgress;
        public float MaxWaitSec;

        /// <summary>The ad unit this placement uses right now (resolves banner type and splash format).</summary>
        public string ActiveUnitKey
        {
            get
            {
                if (Format == AdFormat.Banner) return BannerType == BannerType.Standard ? StandardUnitKey : AdaptiveUnitKey;
                if (IsSplash) return SplashFormat == SplashAdFormat.AppOpen ? AppOpenUnitKey : InterstitialUnitKey;
                return UnitKey;
            }
        }

        /// <summary>The format shown right now (splash resolves to app-open or interstitial).</summary>
        public AdFormat ActiveFormat
        {
            get
            {
                if (IsSplash) return SplashFormat == SplashAdFormat.AppOpen ? AdFormat.AppOpen : AdFormat.Interstitial;
                return Format;
            }
        }
    }
}
