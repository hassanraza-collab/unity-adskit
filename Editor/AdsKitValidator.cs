using System.Collections.Generic;
using System.Text;
using NineDTech.AdsKit.Ads;
using UnityEditor;
using UnityEngine;

namespace NineDTech.AdsKit.Editor
{
    /// <summary>Checks an AdsSettings asset for mistakes that would silently lose ads or revenue.</summary>
    public static class AdsKitValidator
    {
        [MenuItem("Tools/AdsKit/Validate Settings", priority = 20)]
        private static void ValidateMenu()
        {
            var settings = Selection.activeObject as AdsSettings ?? AdsConfigJson.FindSettings();
            if (settings == null)
            {
                EditorUtility.DisplayDialog("AdsKit", "No AdsSettings asset found. Run Tools > AdsKit > Import ads_config JSON first.", "OK");
                return;
            }
            var report = Validate(settings, out var errors, out var warnings);
            var summary = settings.name + ": " + errors + " error(s), " + warnings + " warning(s)";
            if (errors > 0) Debug.LogError("[AdsKit] " + summary + "\n" + report);
            else if (warnings > 0) Debug.LogWarning("[AdsKit] " + summary + "\n" + report);
            else Debug.Log("[AdsKit] " + summary);
            EditorUtility.DisplayDialog("AdsKit validation", summary + (report.Length > 0 ? "\n\nDetails in the Console." : ""), "OK");
        }

        [MenuItem("Tools/AdsKit/Open Mediation Debugger (Play Mode)", priority = 40)]
        private static void OpenDebugger() => Ads.Ads.OpenDebugger();

        [MenuItem("Tools/AdsKit/Reset Local Ads State (PlayerPrefs)", priority = 41)]
        private static void ResetState()
        {
            foreach (var key in new[] { "adskit.session_count", "adskit.install_utc", "adskit.mediation",
                         "adskit.ad_revenue_usd", "adskit.ad_revenue_tier", "adskit.level_milestone_done" })
                PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            Debug.Log("[AdsKit] Local state reset: session count, mediation lock, revenue tiers, milestones");
        }

        public static string Validate(AdsSettings s, out int errors, out int warnings)
        {
            var sb = new StringBuilder();
            var e = 0;
            var w = 0;
            void Error(string m) { e++; sb.Append("ERROR: ").AppendLine(m); }
            void Warn(string m) { w++; sb.Append("warning: ").AppendLine(m); }

            var ids = new HashSet<string>();
            void CheckUnit(string owner, AdUnitDefinition unit, AdFormat expected, bool required)
            {
                if (unit == null)
                {
                    if (required) Error(owner + " has no ad unit");
                    return;
                }
                if (unit.Format != expected) Error(owner + ": unit " + unit.Key + " is " + unit.Format + ", expected " + expected);
                foreach (var provider in new[] { AdsIds.Max, AdsIds.AdMob })
                {
                    var id = unit.GetId(provider);
                    if (string.IsNullOrEmpty(id)) Warn(unit.Key + " has no " + provider + " id (Remote Config must provide it)");
                    else if (id.StartsWith("MAX_") || id.Contains("REPLACE")) Warn(unit.Key + " " + provider + " id is a placeholder: " + id);
                    else if (provider == AdsIds.AdMob && id.StartsWith("ca-app-pub-3940256099942544")) Warn(unit.Key + " uses a Google TEST ad unit id");
                }
            }

            if (s.splash != null)
            {
                CheckUnit("splash (app_open)", s.splash.appOpenUnit, AdFormat.AppOpen, s.splash.format == SplashAdFormat.AppOpen);
                CheckUnit("splash (interstitial)", s.splash.interstitialUnit, AdFormat.Interstitial, s.splash.format == SplashAdFormat.Interstitial);
                if (s.splash.minSessions < 2) Warn("splash ad shows from the first session; Google/AppLovin advise waiting a few sessions");
            }
            if (s.banner != null)
            {
                CheckUnit("banner standard", s.banner.standardUnit, AdFormat.Banner, s.banner.bannerType == BannerType.Standard);
                CheckUnit("banner adaptive", s.banner.adaptiveUnit, AdFormat.Banner, s.banner.bannerType == BannerType.Adaptive);
            }
            if (s.appOpenResume != null && s.appOpenResume.enabled)
                CheckUnit("app_open_resume", s.appOpenResume.adUnit, AdFormat.AppOpen, true);

            foreach (var r in s.rewarded)
            {
                if (r == null) { Error("rewarded list has an empty entry"); continue; }
                if (!ids.Add("r:" + r.Id)) Error("duplicate rewarded placement id " + r.Id);
                CheckUnit("rewarded " + r.Id, r.adUnit, AdFormat.Rewarded, true);
            }

            var interIds = new HashSet<string>();
            foreach (var i in s.interstitials)
            {
                if (i == null) { Error("interstitials list has an empty entry"); continue; }
                if (!interIds.Add(i.Id)) Error("duplicate interstitial placement id " + i.Id);
                CheckUnit("interstitial " + i.Id, i.adUnit, AdFormat.Interstitial, true);
            }

            foreach (var c in s.cachePriority)
            {
                if (c == null) continue;
                foreach (var id in c.placements)
                    if (!interIds.Contains(id)) Error("cache_priority '" + c.context + "' lists unknown interstitial '" + id + "'");
            }
            foreach (var id in interIds)
            {
                var listed = false;
                foreach (var c in s.cachePriority)
                    if (c != null && c.placements.Contains(id)) listed = true;
                if (!listed) Warn("interstitial '" + id + "' is in no cache_priority context, so it will never be preloaded");
            }

            if (s.loading.maxConcurrentLoads > 2) Warn("max_concurrent_loads > 2 risks ANRs on low-end devices");
            if (s.frequency.globalInterstitialCooldownSec < 30) Warn("global interstitial cooldown under 30s hurts retention");

            errors = e;
            warnings = w;
            return sb.ToString();
        }
    }
}
