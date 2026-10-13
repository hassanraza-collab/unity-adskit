namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// Everything AdsKit needs from the game. Implement once per game (or derive from <see cref="AdsGameHooksBase"/>).
    /// All members are called on the main thread.
    /// </summary>
    public interface IAdsGameHooks
    {
        /// <summary>Highest level the player has reached (used by min_level gates).</summary>
        int CurrentLevel { get; }

        /// <summary>True when the player bought "remove ads" (blocks interstitial, app-open and banner; never rewarded).</summary>
        bool AdsRemoved { get; }

        /// <summary>A full-screen ad is about to cover the game: pause gameplay, timers and audio.</summary>
        void OnFullscreenAdOpened();

        /// <summary>The full-screen ad closed (or failed): resume.</summary>
        void OnFullscreenAdClosed();

        /// <summary>Show/hide a small "loading ad" spinner while a rewarded request waits for an ad.</summary>
        void SetAdLoadingIndicator(bool visible);

        /// <summary>A rewarded ad could not be shown; tell the player (e.g. a "No ad available" toast).</summary>
        void OnRewardedUnavailable(string placementId);
    }
}
