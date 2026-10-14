using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>An interstitial trigger point (play button, level complete, ...). Shows only its own ad unit.</summary>
    [CreateAssetMenu(menuName = "AdsKit/Placements/Interstitial", fileName = "Interstitial")]
    public sealed class InterstitialPlacement : AdPlacement
    {
        public AdUnitDefinition adUnit;

        [Tooltip("Seconds after this placement's last ad closed before it may show again. Remote key: cooldown_sec")]
        [Min(0)] public float cooldownSec = 60f;

        [Tooltip("Priority place (e.g. win/fail, level select): shows even while any cooldown timer is still running. " +
                 "Session cap and audience gates still apply; closing it restarts the cooldowns. Remote key: priority")]
        public bool priority;

        public override AdFormat Format => AdFormat.Interstitial;
    }
}
