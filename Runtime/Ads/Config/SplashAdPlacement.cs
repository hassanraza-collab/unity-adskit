using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>The cold-start ad shown on the loading screen (app-open or interstitial, chosen remotely).</summary>
    [CreateAssetMenu(menuName = "AdsKit/Placements/Splash Ad", fileName = "SplashAd")]
    public sealed class SplashAdPlacement : AdPlacement
    {
        [Tooltip("Remote key: format (app_open | interstitial)")]
        public SplashAdFormat format = SplashAdFormat.AppOpen;

        public AdUnitDefinition appOpenUnit;
        public AdUnitDefinition interstitialUnit;

        [Tooltip("Loading-bar progress (0-1) at which the game calls Ads.ShowSplashAd. Remote key: trigger_progress")]
        [Range(0f, 1f)] public float triggerProgress = 0.7f;

        [Tooltip("How long the loading bar may wait for the ad to finish loading. Remote key: max_wait_sec")]
        [Min(0)] public float maxWaitSec = 3f;

        public override AdFormat Format => format == SplashAdFormat.AppOpen ? AdFormat.AppOpen : AdFormat.Interstitial;
    }
}
