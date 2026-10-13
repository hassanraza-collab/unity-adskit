using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>The bottom banner. Standard or adaptive is chosen remotely; each type has its own ad unit.</summary>
    [CreateAssetMenu(menuName = "AdsKit/Placements/Banner", fileName = "Banner")]
    public sealed class BannerPlacement : AdPlacement
    {
        public AdUnitDefinition standardUnit;
        public AdUnitDefinition adaptiveUnit;

        [Tooltip("Remote key: banner_type (standard | adaptive)")]
        public BannerType bannerType = BannerType.Adaptive;

        [Tooltip("Remote key: position (bottom | top)")]
        public BannerPosition position = BannerPosition.Bottom;

        [Tooltip("Stop banner refresh while a full-screen ad is open. Remote key: pause_refresh_during_fullscreen")]
        public bool pauseRefreshDuringFullscreen = true;

        public override AdFormat Format => AdFormat.Banner;
    }
}
