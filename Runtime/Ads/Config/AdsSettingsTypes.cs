using System;
using System.Collections.Generic;
using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>Remote block: <c>session</c>.</summary>
    [Serializable]
    public class SessionSettings
    {
        [Tooltip("A return from the background after this many minutes counts as a new session.")]
        [Min(1)] public float sessionTimeoutMinutes = 30f;
    }

    /// <summary>Remote block: <c>frequency</c>. All times count from when the previous ad closed.</summary>
    [Serializable]
    public class FrequencySettings
    {
        [Tooltip("Minimum seconds between any two interstitials (all placements).")]
        [Min(0)] public float globalInterstitialCooldownSec = 60f;

        [Tooltip("No interstitial for this long after a rewarded ad closed.")]
        [Min(0)] public float cooldownAfterRewardedSec = 45f;

        [Tooltip("No interstitial for this long after the splash / app-open ad closed.")]
        [Min(0)] public float cooldownAfterSplashAdSec = 60f;

        [Tooltip("No interstitial in the first seconds of a session.")]
        [Min(0)] public float firstInterstitialMinSessionSec = 30f;

        [Tooltip("Interstitials per session at most (0 = unlimited).")]
        [Min(0)] public int maxInterstitialsPerSession = 8;
    }

    /// <summary>Remote block: <c>loading</c>. Controls the background ad cache.</summary>
    [Serializable]
    public class LoadingSettings
    {
        [Tooltip("Start requesting ads after this many seconds even if the SDK has not reported every mediation adapter as started (AdMob waits for all of them, up to ~30 s). 0 = always wait.")]
        [Min(0)] public float sdkInitMaxWaitSec = 5f;

        [Tooltip("Ad requests in flight at the same time.")]
        [Min(1)] public int maxConcurrentLoads = 1;

        [Tooltip("Pause after a load finishes before the next request starts.")]
        [Min(0)] public float loadStaggerSec = 1f;

        [Tooltip("Retry delays double from 2s up to this cap.")]
        [Min(1)] public float retryMaxBackoffSec = 64f;

        [Tooltip("Failed loads in a row before the unit pauses until the context changes or the network returns.")]
        [Min(1)] public int retryMaxAttempts = 6;

        [Tooltip("Interstitials kept loaded for the current context (one extra from the previous context may be kept).")]
        [Min(1)] public int maxCachedInterstitials = 3;

        [Tooltip("Devices with less RAM (MB) use the low-RAM cache size.")]
        public int lowRamThresholdMb = 3072;

        [Min(1)] public int lowRamMaxCachedInterstitials = 1;

        [Tooltip("Replace an expiring ad only when its placement is likely to be needed (protects show rate).")]
        public bool refreshOnlyWhenNeeded = true;

        [Tooltip("Providers that do not manage expiry (AdMob): refresh interstitial/rewarded after this many minutes.")]
        [Min(1)] public float admobInterRewardedRefreshMin = 55f;

        [Tooltip("Providers that do not manage expiry (AdMob): refresh app-open after this many minutes.")]
        [Min(1)] public float admobAppOpenRefreshMin = 235f;

        [Tooltip("Use the AdMob preloading API (beta) instead of AdsKit's own cache in the AdMob arm.")]
        public bool admobUsePreloadApi;

        [Tooltip("Log ad_load_fail analytics events.")]
        public bool logLoadFailures = true;
    }

    /// <summary>Remote block: <c>cache_priority</c>. Placements to keep loaded per app context, most likely first.</summary>
    [Serializable]
    public class ContextCachePriority
    {
        public string context = AdsIds.ContextHome;
        public List<string> placements = new List<string>();
    }

    /// <summary>Fake ads for the Editor and for testing without SDKs.</summary>
    [Serializable]
    public class SimulatorSettings
    {
        [Min(0)] public float minLoadSec = 0.5f;
        [Min(0)] public float maxLoadSec = 2f;

        [Range(0f, 1f)] public float fillRate = 0.9f;

        [Tooltip("Fake eCPM used for simulated revenue events (USD per 1000 impressions).")]
        [Min(0)] public float fakeEcpmUsd = 10f;

        [Tooltip("Seconds before the simulated full-screen ad can be closed.")]
        [Min(0)] public float closeDelaySec = 1.5f;
    }
}
