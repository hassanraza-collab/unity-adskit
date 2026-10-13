using System.Collections.Generic;
using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// The live ads configuration: built from <see cref="AdsSettings"/> and then patched by the Remote Config JSON
    /// (<see cref="AdsRemoteConfigApplier"/>). Assets are never modified at runtime.
    /// </summary>
    public sealed class AdsRuntimeConfig
    {
        public bool AdsEnabled;
        public int SchemaVersion;
        public SessionSettings Session;
        public FrequencySettings Frequency;
        public LoadingSettings Loading;
        public float RewardedLoadWaitSec;

        public PlacementRuntime Splash;
        public PlacementRuntime Banner;
        public PlacementRuntime AppOpenResume;

        public readonly Dictionary<string, PlacementRuntime> Interstitials = new Dictionary<string, PlacementRuntime>();
        public readonly Dictionary<string, PlacementRuntime> Rewarded = new Dictionary<string, PlacementRuntime>();
        public readonly Dictionary<string, List<string>> CachePriority = new Dictionary<string, List<string>>();
        public readonly List<double> RevenueTiersUsd = new List<double>();

        /// <summary>Ad unit key -> format.</summary>
        public readonly Dictionary<string, AdFormat> UnitFormats = new Dictionary<string, AdFormat>();

        private readonly Dictionary<string, Dictionary<string, string>> _unitIds = new Dictionary<string, Dictionary<string, string>>();

        public string GetUnitId(string provider, string unitKey)
        {
            if (string.IsNullOrEmpty(unitKey)) return null;
            if (_unitIds.TryGetValue(provider, out var ids) && ids.TryGetValue(unitKey, out var id) && !string.IsNullOrEmpty(id))
                return id;
            // The simulator accepts any id, so it never needs real ones.
            return provider == AdsIds.Simulator ? "sim_" + unitKey : null;
        }

        public void SetUnitId(string provider, string unitKey, string id)
        {
            if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(unitKey)) return;
            if (!_unitIds.TryGetValue(provider, out var ids)) _unitIds[provider] = ids = new Dictionary<string, string>();
            ids[unitKey] = id;
        }

        /// <summary>All ad unit ids the provider uses (for selective SDK initialization).</summary>
        public List<string> GetAllUnitIds(string provider)
        {
            var list = new List<string>();
            foreach (var key in UnitFormats.Keys)
            {
                var id = GetUnitId(provider, key);
                if (!string.IsNullOrEmpty(id) && !list.Contains(id)) list.Add(id);
            }
            return list;
        }

        public static AdsRuntimeConfig FromSettings(AdsSettings s)
        {
            var c = new AdsRuntimeConfig
            {
                AdsEnabled = s.adsEnabled,
                Session = Clone(s.session),
                Frequency = Clone(s.frequency),
                Loading = Clone(s.loading),
                RewardedLoadWaitSec = s.rewardedLoadWaitSec
            };

            foreach (var entry in s.cachePriority)
                if (entry != null && !string.IsNullOrEmpty(entry.context))
                    c.CachePriority[entry.context] = new List<string>(entry.placements);
            foreach (var tier in s.revenueTiersUsd) c.RevenueTiersUsd.Add(tier);

            if (s.splash != null)
            {
                var p = Base(s.splash);
                p.IsSplash = true;
                p.Id = AdsIds.PlacementSplash;
                p.SplashFormat = s.splash.format;
                p.AppOpenUnitKey = c.RegisterUnit(s.splash.appOpenUnit);
                p.InterstitialUnitKey = c.RegisterUnit(s.splash.interstitialUnit);
                p.TriggerProgress = s.splash.triggerProgress;
                p.MaxWaitSec = s.splash.maxWaitSec;
                c.Splash = p;
            }

            if (s.banner != null)
            {
                var p = Base(s.banner);
                p.Id = AdsIds.PlacementBanner;
                p.StandardUnitKey = c.RegisterUnit(s.banner.standardUnit);
                p.AdaptiveUnitKey = c.RegisterUnit(s.banner.adaptiveUnit);
                p.BannerType = s.banner.bannerType;
                p.Position = s.banner.position;
                p.PauseRefreshDuringFullscreen = s.banner.pauseRefreshDuringFullscreen;
                c.Banner = p;
            }

            if (s.appOpenResume != null)
            {
                var p = Base(s.appOpenResume);
                p.Id = AdsIds.PlacementAppOpenResume;
                p.UnitKey = c.RegisterUnit(s.appOpenResume.adUnit);
                p.MinBackgroundSec = s.appOpenResume.minBackgroundSec;
                c.AppOpenResume = p;
            }

            foreach (var r in s.rewarded)
            {
                if (r == null) continue;
                var p = Base(r);
                p.UnitKey = c.RegisterUnit(r.adUnit);
                c.Rewarded[p.Id] = p;
            }

            foreach (var i in s.interstitials)
            {
                if (i == null) continue;
                var p = Base(i);
                p.UnitKey = c.RegisterUnit(i.adUnit);
                p.CooldownSec = i.cooldownSec;
                c.Interstitials[p.Id] = p;
            }

            return c;
        }

        private string RegisterUnit(AdUnitDefinition unit)
        {
            if (unit == null) return null;
            UnitFormats[unit.Key] = unit.Format;
            foreach (var entry in unit.Ids) SetUnitId(entry.provider, unit.Key, entry.androidId);
            return unit.Key;
        }

        private static PlacementRuntime Base(AdPlacement a) => new PlacementRuntime
        {
            Id = a.Id,
            Format = a.Format,
            Enabled = a.enabled,
            MinSessions = a.minSessions,
            MinLevel = a.minLevel
        };

        private static T Clone<T>(T source) where T : class, new()
        {
            return source == null ? new T() : JsonUtility.FromJson<T>(JsonUtility.ToJson(source));
        }
    }
}
