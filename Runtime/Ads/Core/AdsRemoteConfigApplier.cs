using System.Collections.Generic;
using NineDTech.AdsKit.Json;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// Applies the <c>ads_config</c> Remote Config JSON onto an <see cref="AdsRuntimeConfig"/>.
    /// Every key is optional; missing keys keep the asset defaults. Unknown placements are reported, not created.
    /// Breaking schema changes should ship under a new parameter name (AdsSettings.remoteConfigKey).
    /// </summary>
    public static class AdsRemoteConfigApplier
    {
        public static bool Apply(AdsRuntimeConfig c, string json, out string error)
        {
            var root = MiniJson.TryParseObject(json, out error);
            if (root == null) return false;

            root.Read("schema_version", ref c.SchemaVersion);
            root.Read("ads_enabled", ref c.AdsEnabled);

            var session = root.GetObject("session");
            session.Read("session_timeout_minutes", ref c.Session.sessionTimeoutMinutes);

            var f = root.GetObject("frequency");
            f.Read("global_interstitial_cooldown_sec", ref c.Frequency.globalInterstitialCooldownSec);
            f.Read("cooldown_after_rewarded_sec", ref c.Frequency.cooldownAfterRewardedSec);
            f.Read("cooldown_after_splash_ad_sec", ref c.Frequency.cooldownAfterSplashAdSec);
            f.Read("first_interstitial_min_session_sec", ref c.Frequency.firstInterstitialMinSessionSec);
            f.Read("max_interstitials_per_session", ref c.Frequency.maxInterstitialsPerSession);

            var l = root.GetObject("loading");
            l.Read("max_concurrent_loads", ref c.Loading.maxConcurrentLoads);
            l.Read("load_stagger_sec", ref c.Loading.loadStaggerSec);
            l.Read("retry_max_backoff_sec", ref c.Loading.retryMaxBackoffSec);
            l.Read("retry_max_attempts", ref c.Loading.retryMaxAttempts);
            l.Read("max_cached_interstitials", ref c.Loading.maxCachedInterstitials);
            l.Read("low_ram_threshold_mb", ref c.Loading.lowRamThresholdMb);
            l.Read("low_ram_max_cached_interstitials", ref c.Loading.lowRamMaxCachedInterstitials);
            l.Read("refresh_only_when_needed", ref c.Loading.refreshOnlyWhenNeeded);
            l.Read("admob_inter_rewarded_refresh_min", ref c.Loading.admobInterRewardedRefreshMin);
            l.Read("admob_app_open_refresh_min", ref c.Loading.admobAppOpenRefreshMin);
            l.Read("admob_use_preload_api", ref c.Loading.admobUsePreloadApi);
            l.Read("log_load_failures", ref c.Loading.logLoadFailures);

            var priority = root.GetObject("cache_priority");
            if (priority != null)
                foreach (var pair in priority)
                    if (pair.Value is List<object>)
                        c.CachePriority[pair.Key] = priority.ReadStringList(pair.Key);

            var tiers = root.ReadNumberList("revenue_tiers_usd");
            if (tiers != null)
            {
                c.RevenueTiersUsd.Clear();
                c.RevenueTiersUsd.AddRange(tiers);
            }

            var splash = root.GetObject("splash");
            if (splash != null && c.Splash != null)
            {
                ReadBase(splash, c.Splash);
                var format = "";
                splash.Read("format", ref format);
                if (format == "app_open") c.Splash.SplashFormat = SplashAdFormat.AppOpen;
                else if (format == "interstitial") c.Splash.SplashFormat = SplashAdFormat.Interstitial;
                splash.Read("trigger_progress", ref c.Splash.TriggerProgress);
                splash.Read("max_wait_sec", ref c.Splash.MaxWaitSec);
            }

            var resume = root.GetObject("app_open_resume");
            if (resume != null && c.AppOpenResume != null)
            {
                ReadBase(resume, c.AppOpenResume);
                resume.Read("min_background_sec", ref c.AppOpenResume.MinBackgroundSec);
            }

            var banner = root.GetObject("banner");
            if (banner != null && c.Banner != null)
            {
                ReadBase(banner, c.Banner);
                var type = "";
                banner.Read("banner_type", ref type);
                if (type == "standard") c.Banner.BannerType = BannerType.Standard;
                else if (type == "adaptive") c.Banner.BannerType = BannerType.Adaptive;
                var position = "";
                banner.Read("position", ref position);
                if (position == "bottom") c.Banner.Position = BannerPosition.Bottom;
                else if (position == "top") c.Banner.Position = BannerPosition.Top;
                banner.Read("pause_refresh_during_fullscreen", ref c.Banner.PauseRefreshDuringFullscreen);
            }

            var rewarded = root.GetObject("rewarded");
            if (rewarded != null)
            {
                rewarded.Read("load_wait_sec", ref c.RewardedLoadWaitSec);
                foreach (var p in c.Rewarded.Values) ReadBase(rewarded, p);
                var perPlacement = rewarded.GetObject("placements");
                if (perPlacement != null)
                    foreach (var pair in perPlacement)
                    {
                        if (c.Rewarded.TryGetValue(pair.Key, out var p)) ReadBase(pair.Value as Dictionary<string, object>, p);
                        else KitLog.Warn("ads_config: unknown rewarded placement '" + pair.Key + "'");
                    }
            }

            var inters = root.GetObject("interstitials");
            if (inters != null)
                foreach (var pair in inters)
                {
                    if (!c.Interstitials.TryGetValue(pair.Key, out var p))
                    {
                        KitLog.Warn("ads_config: unknown interstitial placement '" + pair.Key + "' (add its asset to AdsSettings)");
                        continue;
                    }
                    var obj = pair.Value as Dictionary<string, object>;
                    ReadBase(obj, p);
                    obj.Read("cooldown_sec", ref p.CooldownSec);
                }

            var units = root.GetObject("ad_units");
            if (units != null)
                foreach (var provider in units)
                {
                    if (!(provider.Value is Dictionary<string, object> ids)) continue;
                    foreach (var unit in ids)
                    {
                        if (!(unit.Value is string id)) continue;
                        if (!c.UnitFormats.ContainsKey(unit.Key))
                            KitLog.Warn("ads_config: ad unit key '" + unit.Key + "' is not used by any placement");
                        c.SetUnitId(provider.Key, unit.Key, id);
                    }
                }

            return true;
        }

        private static void ReadBase(Dictionary<string, object> obj, PlacementRuntime p)
        {
            if (obj == null) return;
            obj.Read("enabled", ref p.Enabled);
            obj.Read("min_sessions", ref p.MinSessions);
            obj.Read("min_level", ref p.MinLevel);
        }
    }
}
