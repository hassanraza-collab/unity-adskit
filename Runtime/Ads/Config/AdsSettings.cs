using System.Collections.Generic;
using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// Root ads configuration asset. Holds the in-app defaults; the Remote Config JSON
    /// (<see cref="remoteConfigKey"/>) overrides any of them at runtime without touching the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "AdsKit/Ads Settings", fileName = "AdsSettings")]
    public sealed class AdsSettings : ScriptableObject
    {
        [Header("General")]
        [Tooltip("Kill switch for every ad. Remote key: ads_enabled")]
        public bool adsEnabled = true;

        public bool verboseLogs = true;

        [Tooltip("Show the on-screen ads debug panel (slots, cooldowns) in the Editor and development builds.")]
        public bool showDebugHud;

        [Header("Remote Config")]
        [Tooltip("Remote Config parameter holding the ads JSON.")]
        public string remoteConfigKey = "ads_config";

        [Tooltip("Seconds the first Remote Config fetch may take before cached/default values are used.")]
        [Min(0.5f)] public float remoteFetchTimeoutSec = 3f;

        [Tooltip("Editor only: JSON applied as if it came from Remote Config (test payloads before publishing).")]
        public TextAsset editorRemoteJson;

        [Header("Mediation")]
        [Tooltip("Pick the mediation provider from the A/B parameter below (locked per install on first value).")]
        public bool abTestMediation = true;

        [Tooltip("Remote Config string parameter with the provider id (max | admob).")]
        public string mediationRemoteKey = "ads_mediation";

        [Tooltip("Provider used when no A/B test runs, or when the assigned provider is not installed.")]
        public string defaultMediation = AdsIds.Max;

        [Tooltip("Use the simulator provider when running in the Editor.")]
        public bool useSimulatorInEditor = true;

        [Tooltip("Pass test-mode flags to the SDKs (development builds only).")]
        public bool testModeInDevelopmentBuilds = true;

        [Tooltip("Development builds: AdMob test device hashed ids (logcat: 'Use RequestConfiguration...setTestDeviceIds').")]
        public List<string> admobTestDeviceIds = new List<string>();

        [Tooltip("Development builds: MAX test devices (Google advertising ids).")]
        public List<string> maxTestDeviceAdvertisingIds = new List<string>();

        [Header("Consent (Google UMP)")]
        [Tooltip("Development builds: force the EEA consent form on every device for testing.")]
        public bool consentDebugForceEea;

        [Tooltip("Development builds: UMP test device hashed ids (logcat: 'Use new ConsentDebugSettings.Builder().addTestDeviceHashedId').")]
        public List<string> consentTestDeviceHashedIds = new List<string>();

        [Header("Frequency / sessions / loading")]
        public SessionSettings session = new SessionSettings();
        public FrequencySettings frequency = new FrequencySettings();
        public LoadingSettings loading = new LoadingSettings();

        [Tooltip("Seconds a rewarded request may wait for an ad to load (spinner). Remote: rewarded.load_wait_sec")]
        [Min(0)] public float rewardedLoadWaitSec = 3f;

        [Tooltip("Placements to keep loaded per app context, most likely first.")]
        public List<ContextCachePriority> cachePriority = new List<ContextCachePriority>();

        [Tooltip("Total ad revenue (USD) thresholds that log ad_revenue_tier_N once each. Remote: revenue_tiers_usd")]
        public List<float> revenueTiersUsd = new List<float> { 0.01f, 0.05f, 0.1f, 0.25f, 0.5f, 1f };

        [Header("Placements")]
        public SplashAdPlacement splash;
        public BannerPlacement banner;
        public AppOpenResumePlacement appOpenResume;
        public List<RewardedPlacement> rewarded = new List<RewardedPlacement>();
        public List<InterstitialPlacement> interstitials = new List<InterstitialPlacement>();

        [Header("Simulator")]
        public SimulatorSettings simulator = new SimulatorSettings();
    }
}
