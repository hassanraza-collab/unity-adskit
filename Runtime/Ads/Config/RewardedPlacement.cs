using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>A rewarded entry point (booster refill, claim x2, ...). Several placements may share one ad unit.</summary>
    [CreateAssetMenu(menuName = "AdsKit/Placements/Rewarded", fileName = "Rewarded")]
    public sealed class RewardedPlacement : AdPlacement
    {
        public AdUnitDefinition adUnit;

        public override AdFormat Format => AdFormat.Rewarded;
    }
}
