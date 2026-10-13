using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>App-open ad when the player returns from the background. Disabled by default.</summary>
    [CreateAssetMenu(menuName = "AdsKit/Placements/App Open (Resume)", fileName = "AppOpenResume")]
    public sealed class AppOpenResumePlacement : AdPlacement
    {
        public AdUnitDefinition adUnit;

        [Tooltip("Minimum seconds in the background before the ad may show. Remote key: min_background_sec")]
        [Min(0)] public float minBackgroundSec = 30f;

        public override AdFormat Format => AdFormat.AppOpen;

        private void Reset() => enabled = false;
    }
}
