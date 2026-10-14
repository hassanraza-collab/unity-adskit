using System.Collections.Generic;
using System.IO;
using NineDTech.AdsKit.Ads;
using NineDTech.AdsKit.Analytics;
using NineDTech.AdsKit.Json;
using UnityEditor;
using UnityEngine;

namespace NineDTech.AdsKit.Editor
{
    /// <summary>
    /// Two-way bridge between the AdsSettings assets and the <c>ads_config</c> Remote Config JSON:
    /// build/update all assets from a JSON file, or export the assets as the JSON to paste into Firebase.
    /// </summary>
    public static class AdsConfigJson
    {
        private const string DefaultFolder = "Assets/AdsKitConfig";

        // ------------------------------------------------------------------ import

        [MenuItem("Tools/AdsKit/Import ads_config JSON...", priority = 10)]
        private static void ImportMenu()
        {
            var path = EditorUtility.OpenFilePanel("ads_config JSON", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path)) return;
            var folder = EditorUtility.SaveFolderPanel("Config folder (inside Assets)", Application.dataPath, "AdsKitConfig");
            if (string.IsNullOrEmpty(folder)) return;
            folder = ToAssetPath(folder);
            if (folder == null)
            {
                EditorUtility.DisplayDialog("AdsKit", "The folder must be inside this project's Assets folder.", "OK");
                return;
            }
            var settings = Import(File.ReadAllText(path), folder);
            if (settings != null)
            {
                Selection.activeObject = settings;
                EditorGUIUtility.PingObject(settings);
            }
        }

        /// <summary>Create or update every asset described by the JSON inside <paramref name="folder"/>. Returns the AdsSettings.</summary>
        public static AdsSettings Import(string json, string folder = DefaultFolder)
        {
            var root = MiniJson.TryParseObject(json, out var error);
            if (root == null)
            {
                EditorUtility.DisplayDialog("AdsKit", "Invalid JSON: " + error, "OK");
                return null;
            }
            EnsureFolder(folder);
            EnsureFolder(folder + "/AdUnits");
            EnsureFolder(folder + "/Placements");

            var settings = LoadOrCreate<AdsSettings>(folder + "/AdsSettings.asset");
            if (LoadOrCreate<AnalyticsSettings>(folder + "/AnalyticsSettings.asset") == null) return null;

            root.Read("ads_enabled", ref settings.adsEnabled);
            ReadSettingsBlocks(root, settings);

            // Ad units: every key under ad_units.<provider>
            var units = new Dictionary<string, AdUnitDefinition>();
            var unitsRoot = root.GetObject("ad_units");
            if (unitsRoot != null)
                foreach (var provider in unitsRoot)
                {
                    if (!(provider.Value is Dictionary<string, object> ids)) continue;
                    foreach (var pair in ids)
                    {
                        var unit = Unit(units, folder, pair.Key);
                        unit.EditorSetId(provider.Key, pair.Value as string ?? "");
                        EditorUtility.SetDirty(unit);
                    }
                }

            // Splash
            var splashJson = root.GetObject("splash");
            if (splashJson != null)
            {
                var s = LoadOrCreate<SplashAdPlacement>(folder + "/Placements/SplashAd.asset");
                s.EditorSetId(AdsIds.PlacementSplash);
                ReadBase(splashJson, s);
                var format = "";
                splashJson.Read("format", ref format);
                s.format = format == "interstitial" ? SplashAdFormat.Interstitial : SplashAdFormat.AppOpen;
                splashJson.Read("trigger_progress", ref s.triggerProgress);
                splashJson.Read("max_wait_sec", ref s.maxWaitSec);
                s.appOpenUnit = FindUnit(units, folder, "splash_app_open", AdFormat.AppOpen);
                s.interstitialUnit = FindUnit(units, folder, "splash_interstitial", AdFormat.Interstitial);
                EditorUtility.SetDirty(s);
                settings.splash = s;
            }

            // Banner
            var bannerJson = root.GetObject("banner");
            if (bannerJson != null)
            {
                var b = LoadOrCreate<BannerPlacement>(folder + "/Placements/Banner.asset");
                b.EditorSetId(AdsIds.PlacementBanner);
                ReadBase(bannerJson, b);
                var type = "";
                bannerJson.Read("banner_type", ref type);
                b.bannerType = type == "standard" ? BannerType.Standard : BannerType.Adaptive;
                var position = "";
                bannerJson.Read("position", ref position);
                b.position = position == "top" ? BannerPosition.Top : BannerPosition.Bottom;
                bannerJson.Read("pause_refresh_during_fullscreen", ref b.pauseRefreshDuringFullscreen);
                b.standardUnit = FindUnit(units, folder, "banner_standard", AdFormat.Banner);
                b.adaptiveUnit = FindUnit(units, folder, "banner_adaptive", AdFormat.Banner);
                EditorUtility.SetDirty(b);
                settings.banner = b;
            }

            // App open on resume
            var resumeJson = root.GetObject("app_open_resume");
            if (resumeJson != null)
            {
                var r = LoadOrCreate<AppOpenResumePlacement>(folder + "/Placements/AppOpenResume.asset");
                r.EditorSetId(AdsIds.PlacementAppOpenResume);
                ReadBase(resumeJson, r);
                resumeJson.Read("min_background_sec", ref r.minBackgroundSec);
                r.adUnit = FindUnit(units, folder, "app_open_resume", AdFormat.AppOpen)
                           ?? FindUnit(units, folder, "splash_app_open", AdFormat.AppOpen);
                EditorUtility.SetDirty(r);
                settings.appOpenResume = r;
            }

            // Rewarded: one default placement using the "rewarded" unit (games add more placements by hand)
            var rewardedJson = root.GetObject("rewarded");
            if (rewardedJson != null)
            {
                rewardedJson.Read("load_wait_sec", ref settings.rewardedLoadWaitSec);
                if (settings.rewarded.Count == 0)
                {
                    var rw = LoadOrCreate<RewardedPlacement>(folder + "/Placements/Rewarded_default.asset");
                    rw.EditorSetId("rewarded_default");
                    rw.adUnit = FindUnit(units, folder, "rewarded", AdFormat.Rewarded);
                    EditorUtility.SetDirty(rw);
                    settings.rewarded.Add(rw);
                }
                foreach (var rw in settings.rewarded)
                {
                    if (rw == null) continue;
                    ReadBase(rewardedJson, rw);
                    EditorUtility.SetDirty(rw);
                }
            }

            // Interstitials: one asset per id, unit inter_<id>
            var inters = root.GetObject("interstitials");
            if (inters != null)
                foreach (var pair in inters)
                {
                    var obj = pair.Value as Dictionary<string, object>;
                    var p = LoadOrCreate<InterstitialPlacement>(folder + "/Placements/Inter_" + pair.Key + ".asset");
                    p.EditorSetId(pair.Key);
                    ReadBase(obj, p);
                    obj.Read("cooldown_sec", ref p.cooldownSec);
                    p.adUnit = FindUnit(units, folder, "inter_" + pair.Key, AdFormat.Interstitial);
                    EditorUtility.SetDirty(p);
                    if (!settings.interstitials.Contains(p)) settings.interstitials.Add(p);
                }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[AdsKit] Imported ads_config into " + folder + " (" + units.Count + " ad units, " + settings.interstitials.Count + " interstitial placements)");
            return settings;
        }

        private static void ReadSettingsBlocks(Dictionary<string, object> root, AdsSettings s)
        {
            root.GetObject("session").Read("session_timeout_minutes", ref s.session.sessionTimeoutMinutes);

            var f = root.GetObject("frequency");
            f.Read("global_interstitial_cooldown_sec", ref s.frequency.globalInterstitialCooldownSec);
            f.Read("cooldown_after_rewarded_sec", ref s.frequency.cooldownAfterRewardedSec);
            f.Read("cooldown_after_splash_ad_sec", ref s.frequency.cooldownAfterSplashAdSec);
            f.Read("first_interstitial_min_session_sec", ref s.frequency.firstInterstitialMinSessionSec);
            f.Read("max_interstitials_per_session", ref s.frequency.maxInterstitialsPerSession);

            var l = root.GetObject("loading");
            l.Read("sdk_init_max_wait_sec", ref s.loading.sdkInitMaxWaitSec);
            l.Read("max_concurrent_loads", ref s.loading.maxConcurrentLoads);
            l.Read("load_stagger_sec", ref s.loading.loadStaggerSec);
            l.Read("retry_max_backoff_sec", ref s.loading.retryMaxBackoffSec);
            l.Read("retry_max_attempts", ref s.loading.retryMaxAttempts);
            l.Read("max_cached_interstitials", ref s.loading.maxCachedInterstitials);
            l.Read("low_ram_threshold_mb", ref s.loading.lowRamThresholdMb);
            l.Read("low_ram_max_cached_interstitials", ref s.loading.lowRamMaxCachedInterstitials);
            l.Read("refresh_only_when_needed", ref s.loading.refreshOnlyWhenNeeded);
            l.Read("admob_inter_rewarded_refresh_min", ref s.loading.admobInterRewardedRefreshMin);
            l.Read("admob_app_open_refresh_min", ref s.loading.admobAppOpenRefreshMin);
            l.Read("admob_use_preload_api", ref s.loading.admobUsePreloadApi);
            l.Read("log_load_failures", ref s.loading.logLoadFailures);

            var priority = root.GetObject("cache_priority");
            if (priority != null)
            {
                s.cachePriority.Clear();
                foreach (var pair in priority)
                    s.cachePriority.Add(new ContextCachePriority { context = pair.Key, placements = priority.ReadStringList(pair.Key) ?? new List<string>() });
            }

            var tiers = root.ReadNumberList("revenue_tiers_usd");
            if (tiers != null)
            {
                s.revenueTiersUsd.Clear();
                foreach (var t in tiers) s.revenueTiersUsd.Add((float)t);
            }
        }

        private static void ReadBase(Dictionary<string, object> obj, AdPlacement p)
        {
            obj.Read("enabled", ref p.enabled);
            obj.Read("min_sessions", ref p.minSessions);
            obj.Read("min_level", ref p.minLevel);
        }

        private static AdUnitDefinition Unit(Dictionary<string, AdUnitDefinition> units, string folder, string key)
        {
            if (units.TryGetValue(key, out var unit)) return unit;
            unit = LoadOrCreate<AdUnitDefinition>(folder + "/AdUnits/" + key + ".asset");
            unit.EditorSetup(key, GuessFormat(key));
            units[key] = unit;
            return unit;
        }

        private static AdUnitDefinition FindUnit(Dictionary<string, AdUnitDefinition> units, string folder, string key, AdFormat format)
        {
            if (units.TryGetValue(key, out var unit)) return unit;
            var path = folder + "/AdUnits/" + key + ".asset";
            unit = AssetDatabase.LoadAssetAtPath<AdUnitDefinition>(path);
            if (unit == null) return null;
            unit.EditorSetup(key, format);
            units[key] = unit;
            return unit;
        }

        private static AdFormat GuessFormat(string key)
        {
            if (key.StartsWith("banner")) return AdFormat.Banner;
            if (key.StartsWith("rewarded")) return AdFormat.Rewarded;
            if (key.Contains("app_open")) return AdFormat.AppOpen;
            return AdFormat.Interstitial;
        }

        // ------------------------------------------------------------------ export

        [MenuItem("Tools/AdsKit/Export ads_config JSON...", priority = 11)]
        private static void ExportMenu()
        {
            var settings = Selection.activeObject as AdsSettings ?? FindSettings();
            if (settings == null)
            {
                EditorUtility.DisplayDialog("AdsKit", "No AdsSettings asset found. Select one first.", "OK");
                return;
            }
            var path = EditorUtility.SaveFilePanel("Export ads_config", Application.dataPath, "ads_config", "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, Export(settings));
            Debug.Log("[AdsKit] Exported " + settings.name + " to " + path);
        }

        /// <summary>The Remote Config JSON matching the assets (paste as the ads_config default value).</summary>
        public static string Export(AdsSettings s)
        {
            var root = new Dictionary<string, object>
            {
                ["schema_version"] = 2,
                ["ads_enabled"] = s.adsEnabled,
                ["session"] = new Dictionary<string, object> { ["session_timeout_minutes"] = s.session.sessionTimeoutMinutes },
                ["frequency"] = new Dictionary<string, object>
                {
                    ["global_interstitial_cooldown_sec"] = s.frequency.globalInterstitialCooldownSec,
                    ["cooldown_after_rewarded_sec"] = s.frequency.cooldownAfterRewardedSec,
                    ["cooldown_after_splash_ad_sec"] = s.frequency.cooldownAfterSplashAdSec,
                    ["first_interstitial_min_session_sec"] = s.frequency.firstInterstitialMinSessionSec,
                    ["max_interstitials_per_session"] = s.frequency.maxInterstitialsPerSession
                },
                ["loading"] = new Dictionary<string, object>
                {
                    ["sdk_init_max_wait_sec"] = s.loading.sdkInitMaxWaitSec,
                    ["max_concurrent_loads"] = s.loading.maxConcurrentLoads,
                    ["load_stagger_sec"] = s.loading.loadStaggerSec,
                    ["retry_max_backoff_sec"] = s.loading.retryMaxBackoffSec,
                    ["retry_max_attempts"] = s.loading.retryMaxAttempts,
                    ["max_cached_interstitials"] = s.loading.maxCachedInterstitials,
                    ["low_ram_threshold_mb"] = s.loading.lowRamThresholdMb,
                    ["low_ram_max_cached_interstitials"] = s.loading.lowRamMaxCachedInterstitials,
                    ["refresh_only_when_needed"] = s.loading.refreshOnlyWhenNeeded,
                    ["admob_inter_rewarded_refresh_min"] = s.loading.admobInterRewardedRefreshMin,
                    ["admob_app_open_refresh_min"] = s.loading.admobAppOpenRefreshMin,
                    ["admob_use_preload_api"] = s.loading.admobUsePreloadApi,
                    ["log_load_failures"] = s.loading.logLoadFailures
                }
            };

            var priority = new Dictionary<string, object>();
            foreach (var entry in s.cachePriority)
                if (entry != null) priority[entry.context] = new List<object>(entry.placements);
            root["cache_priority"] = priority;
            root["revenue_tiers_usd"] = new List<object>(s.revenueTiersUsd.ConvertAll(t => (object)t));

            var units = new Dictionary<string, AdUnitDefinition>();
            if (s.splash != null)
            {
                root["splash"] = Base(s.splash, new Dictionary<string, object>
                {
                    ["format"] = s.splash.format == SplashAdFormat.AppOpen ? "app_open" : "interstitial",
                    ["trigger_progress"] = s.splash.triggerProgress,
                    ["max_wait_sec"] = s.splash.maxWaitSec
                });
                Collect(units, s.splash.appOpenUnit);
                Collect(units, s.splash.interstitialUnit);
            }
            if (s.appOpenResume != null)
            {
                root["app_open_resume"] = Base(s.appOpenResume, new Dictionary<string, object> { ["min_background_sec"] = s.appOpenResume.minBackgroundSec });
                Collect(units, s.appOpenResume.adUnit);
            }
            if (s.banner != null)
            {
                root["banner"] = Base(s.banner, new Dictionary<string, object>
                {
                    ["banner_type"] = s.banner.bannerType == BannerType.Standard ? "standard" : "adaptive",
                    ["position"] = s.banner.position == BannerPosition.Top ? "top" : "bottom",
                    ["pause_refresh_during_fullscreen"] = s.banner.pauseRefreshDuringFullscreen
                });
                Collect(units, s.banner.standardUnit);
                Collect(units, s.banner.adaptiveUnit);
            }

            var rewarded = new Dictionary<string, object> { ["enabled"] = s.rewarded.Exists(r => r != null && r.enabled), ["load_wait_sec"] = s.rewardedLoadWaitSec };
            var rewardedPlacements = new Dictionary<string, object>();
            foreach (var r in s.rewarded)
            {
                if (r == null) continue;
                rewardedPlacements[r.Id] = Base(r, new Dictionary<string, object>());
                Collect(units, r.adUnit);
            }
            if (rewardedPlacements.Count > 1) rewarded["placements"] = rewardedPlacements;
            root["rewarded"] = rewarded;

            var inters = new Dictionary<string, object>();
            foreach (var i in s.interstitials)
            {
                if (i == null) continue;
                inters[i.Id] = Base(i, new Dictionary<string, object> { ["cooldown_sec"] = i.cooldownSec });
                Collect(units, i.adUnit);
            }
            root["interstitials"] = inters;

            var adUnits = new Dictionary<string, object>();
            foreach (var unit in units.Values)
                foreach (var id in unit.Ids)
                {
                    if (string.IsNullOrEmpty(id.provider) || id.provider == AdsIds.Simulator) continue;
                    if (!adUnits.TryGetValue(id.provider, out var p)) adUnits[id.provider] = p = new Dictionary<string, object>();
                    ((Dictionary<string, object>)p)[unit.Key] = id.androidId ?? "";
                }
            root["ad_units"] = adUnits;
            return MiniJson.Serialize(root);
        }

        private static Dictionary<string, object> Base(AdPlacement p, Dictionary<string, object> extra)
        {
            var d = new Dictionary<string, object> { ["enabled"] = p.enabled };
            foreach (var pair in extra) d[pair.Key] = pair.Value;
            d["min_sessions"] = p.minSessions;
            d["min_level"] = p.minLevel;
            return d;
        }

        private static void Collect(Dictionary<string, AdUnitDefinition> units, AdUnitDefinition unit)
        {
            if (unit != null) units[unit.Key] = unit;
        }

        // ------------------------------------------------------------------ helpers

        internal static AdsSettings FindSettings()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(AdsSettings));
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<AdsSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string ToAssetPath(string absolute)
        {
            absolute = absolute.Replace('\\', '/');
            var data = Application.dataPath.Replace('\\', '/');
            if (absolute == data) return "Assets";
            return absolute.StartsWith(data + "/") ? "Assets" + absolute.Substring(data.Length) : null;
        }
    }
}
