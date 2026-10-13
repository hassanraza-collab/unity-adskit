using System;
using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// Static entry point for games. Every Show call completes its callback exactly once, also when ads are
    /// not initialized, disabled or blocked, so game flow never waits forever.
    /// </summary>
    /// <example>
    /// Ads.Initialize(settings, new MyHooks());
    /// Ads.SetContext(AdsIds.ContextHome);
    /// Ads.ShowInterstitial("play_button", _ => StartLevel());
    /// Ads.ShowRewarded("booster_hammer", () => GrantHammer());
    /// </example>
    public static class Ads
    {
        public static AdsService Service { get; private set; }

        public static bool IsInitialized => Service != null && Service.State == AdsService.InitState.Ready;

        public static string MediationId => Service?.MediationId;

        /// <summary>Loading-bar progress (0-1) at which to call <see cref="ShowSplashAd"/>.</summary>
        public static float SplashTriggerProgress => Service?.Config?.Splash?.TriggerProgress ?? 0.7f;

        public static float BannerHeightPixels => Service != null ? Service.BannerHeightPixels : 0f;

        /// <summary>Banner height in screen pixels changed (0 = hidden). Subscribe any time, even before init.</summary>
        public static event Action<float> BannerHeightChanged;

        /// <summary>Raised once the mediation SDK is initialized.</summary>
        public static event Action Ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Service = null;
            BannerHeightChanged = null;
            Ready = null;
        }

        /// <summary>Start AdsKit: Remote Config, A/B assignment, consent, SDK init. Call once, as early as possible (splash).</summary>
        public static void Initialize(AdsSettings settings, IAdsGameHooks hooks)
        {
            if (Service != null)
            {
                KitLog.Warn("Ads.Initialize called twice; ignoring");
                return;
            }
            if (settings == null)
            {
                KitLog.Error("Ads.Initialize: settings asset is null");
                return;
            }
            Service = new AdsService(settings, hooks);
            Service.BannerHeightChanged += h => BannerHeightChanged?.Invoke(h);
            Service.Ready += () => Ready?.Invoke();
            Service.Initialize();
        }

        public static void SetContext(string context) => Service?.SetContext(context);

        public static void ShowInterstitial(string placementId, Action<AdShowResult> onDone = null)
        {
            if (Service == null) { onDone?.Invoke(AdShowResult.Blocked(AdBlockReason.NotInitialized)); return; }
            Service.ShowInterstitial(placementId, onDone);
        }

        public static void ShowInterstitial(InterstitialPlacement placement, Action<AdShowResult> onDone = null) =>
            ShowInterstitial(placement != null ? placement.Id : null, onDone);

        /// <summary><paramref name="onReward"/> runs after the ad closes, only when the reward was earned.</summary>
        public static void ShowRewarded(string placementId, Action onReward, Action<AdShowResult> onDone = null)
        {
            if (Service == null) { onDone?.Invoke(AdShowResult.Blocked(AdBlockReason.NotInitialized)); return; }
            Service.ShowRewarded(placementId, onReward, onDone);
        }

        public static void ShowRewarded(RewardedPlacement placement, Action onReward, Action<AdShowResult> onDone = null) =>
            ShowRewarded(placement != null ? placement.Id : null, onReward, onDone);

        public static bool IsRewardedReady(string placementId) => Service != null && Service.IsRewardedReady(placementId);

        /// <summary>Call when a rewarded button/offer becomes visible (analytics funnel).</summary>
        public static void NotifyRewardedOffer(string placementId) => Service?.NotifyRewardedOffer(placementId);

        /// <summary>Call when the loading bar reaches <see cref="SplashTriggerProgress"/>; hold the bar until onDone.</summary>
        public static void ShowSplashAd(Action<AdShowResult> onDone)
        {
            if (Service == null) { onDone?.Invoke(AdShowResult.Blocked(AdBlockReason.NotInitialized)); return; }
            Service.ShowSplashAd(onDone);
        }

        /// <summary>Hide the banner temporarily (it stays loaded), e.g. behind a full-screen menu.</summary>
        public static void SetBannerSuppressed(bool suppressed) => Service?.SetBannerSuppressed(suppressed);

        /// <summary>Open MAX Mediation Debugger / AdMob Ad Inspector.</summary>
        public static void OpenDebugger() => Service?.OpenDebugger();
    }
}
