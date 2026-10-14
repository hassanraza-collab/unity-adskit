namespace NineDTech.AdsKit.Ads
{
    public enum AdFormat
    {
        Banner,
        Interstitial,
        Rewarded,
        AppOpen,
        /// <summary>300x250 medium rectangle (exit panel); positioned by the game.</summary>
        Mrec
    }

    public enum BannerType
    {
        /// <summary>Fixed 320x50.</summary>
        Standard,
        /// <summary>Full-width anchored adaptive.</summary>
        Adaptive
    }

    public enum BannerPosition
    {
        Bottom,
        Top
    }

    public enum SplashAdFormat
    {
        AppOpen,
        Interstitial
    }

    /// <summary>Well-known ids used by AdsKit. Games may add their own context names freely.</summary>
    public static class AdsIds
    {
        // Mediation providers
        public const string Max = "max";
        public const string AdMob = "admob";
        public const string Simulator = "simulator";

        // App contexts that drive predictive caching (cache_priority keys)
        public const string ContextSplash = "splash";
        public const string ContextHome = "home";
        public const string ContextGameplay = "gameplay";

        // Fixed placement ids
        public const string PlacementSplash = "splash";
        public const string PlacementBanner = "banner";
        public const string PlacementAppOpenResume = "app_open_resume";
        public const string PlacementMrec = "mrec";
    }
}
