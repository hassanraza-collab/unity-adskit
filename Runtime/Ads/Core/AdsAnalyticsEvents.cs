using NineDTech.AdsKit.Analytics;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>Ad event names logged by AdsKit (see Documentation~/ANALYTICS_EVENTS.md).</summary>
    public static class AdsAnalyticsEvents
    {
        public const string Opportunity = "ad_opportunity";
        public const string Show = "ad_show";
        public const string ShowFail = "ad_show_fail";
        public const string Clicked = "ad_clicked";
        public const string Close = "ad_close";
        public const string RewardedOffer = "rewarded_offer";
        public const string RewardedTap = "rewarded_tap";
        public const string RewardedUnavailable = "rewarded_unavailable";
        public const string Rewarded = "ad_rewarded";
        public const string Impression = "ad_impression";
        public const string KitImpression = "adskit_impression";
        public const string LoadFail = "ad_load_fail";
        public const string RevenueTierPrefix = "ad_revenue_tier_";

        public const string ParamPlacement = "placement";
        public const string ParamFormat = "ad_format";
        public const string ParamMediation = "med";

        public static string FormatName(AdFormat format)
        {
            switch (format)
            {
                case AdFormat.Banner: return "banner";
                case AdFormat.Interstitial: return "interstitial";
                case AdFormat.Rewarded: return "rewarded";
                default: return "app_open";
            }
        }

        public static string PlatformName(string mediation)
        {
            switch (mediation)
            {
                case AdsIds.Max: return "AppLovin";
                case AdsIds.AdMob: return "AdMob";
                default: return "AdsKitSimulator";
            }
        }

        internal static AnalyticsEventBuilder Begin(string name, string placement, AdFormat format) =>
            Analytics.Analytics.Event(name)
                .Add(ParamPlacement, placement ?? "")
                .Add(ParamFormat, FormatName(format));
    }
}
