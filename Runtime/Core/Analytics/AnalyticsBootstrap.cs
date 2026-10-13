using System.Collections.Generic;
using NineDTech.AdsKit.Json;
using NineDTech.AdsKit.Remote;
using UnityEngine;

namespace NineDTech.AdsKit.Analytics
{
    /// <summary>
    /// Wires the analytics layer from an <see cref="AnalyticsSettings"/> asset: console sink, session parameter,
    /// disabled events and level milestones, then keeps them in sync with the Remote Config JSON.
    /// Backend sinks (Firebase) add themselves from their own assemblies.
    /// </summary>
    public static class AnalyticsBootstrap
    {
        private static AnalyticsSettings _settings;
        private static bool _initialized;

        public static bool IsInitialized => _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _settings = null;
            _initialized = false;
        }

        public static void Initialize(AnalyticsSettings settings)
        {
            if (_initialized) return;
            if (settings == null)
            {
                KitLog.Error("AnalyticsBootstrap.Initialize: settings asset is null");
                return;
            }
            _initialized = true;
            _settings = settings;

            var console = Application.isEditor ? settings.logToConsoleInEditor
                : Debug.isDebugBuild && settings.logToConsoleInDevelopmentBuilds;
            if (console) Analytics.AddSink(new DebugLogAnalyticsSink());

            if (settings.addSessionParam)
                Analytics.AddCommonParams(b =>
                {
                    if (SessionTracker.SessionCount > 0)
                        b.AddIfMissing(AnalyticsParam.Of(KitEvents.ParamSession, (long)SessionTracker.SessionCount));
                });

            ApplyDefaults();
            RemoteConfigService.Activated += ApplyRemote;
            if (RemoteConfigService.State == RemoteFetchState.Completed || RemoteConfigService.State == RemoteFetchState.TimedOut)
                ApplyRemote();
        }

        private static void ApplyDefaults()
        {
            Analytics.Enabled = _settings.enabled;
            Analytics.SetDisabledEvents(_settings.disabledEvents);
            LevelAnalytics.SetMilestones(_settings.levelMilestones);
        }

        private static void ApplyRemote()
        {
            if (_settings == null || string.IsNullOrEmpty(_settings.remoteConfigKey)) return;
            if (!RemoteConfigService.TryGetRemoteString(_settings.remoteConfigKey, out var json)) return;
            var root = MiniJson.TryParseObject(json, out var error);
            if (root == null)
            {
                KitLog.Error("analytics_config is not valid JSON: " + error);
                return;
            }

            ApplyDefaults();
            var enabled = Analytics.Enabled;
            root.Read("enabled", ref enabled);
            Analytics.Enabled = enabled;

            var disabled = root.ReadStringList("disabled_events");
            if (disabled != null) Analytics.SetDisabledEvents(disabled);

            var milestones = root.ReadNumberList("level_milestones");
            if (milestones != null)
            {
                var list = new List<int>(milestones.Count);
                foreach (var m in milestones) list.Add((int)m);
                LevelAnalytics.SetMilestones(list);
            }
            KitLog.Info("analytics_config applied");
        }
    }
}
