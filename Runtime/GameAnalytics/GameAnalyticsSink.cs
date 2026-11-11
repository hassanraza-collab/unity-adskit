using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameAnalyticsSDK;
using NineDTech.AdsKit.Analytics;
using GA = GameAnalyticsSDK.GameAnalytics;

namespace NineDTech.AdsKit.GameAnalytics
{
    /// <summary>
    /// Sends AdsKit analytics to GameAnalytics under the same names as Firebase (studio standard): every event is a GA
    /// design event with its exact Firebase name, and these also get GA's own event type:
    /// <list type="bullet">
    /// <item>level_start / level_end / level_restart / level_quit / level_abandon / level_fail → progression events
    /// "level_0001" (4 digits; Start / Complete, or Fail with reason restart / quit / abandon / the fail reason).</item>
    /// <item>ad_show / ad_show_fail / ad_clicked / ad_rewarded → ad events.</item>
    /// <item>iap_purchase_success → business event (amount in cents, itemType = item_type vip / no_ads / currency / item,
    /// item = product id, cart = placement).</item>
    /// </list>
    /// Event parameters go along as custom fields; user properties become global custom fields, and ab_group /
    /// player_type / max_level also set custom dimensions 01-03 (<see cref="DimensionProperties"/>).
    /// Custom fields appear only in GA data export, not in the GA dashboard.
    /// Skipped: ad_impression / adskit_impression (ad revenue is a separate GA ILRD plugin).
    /// </summary>
    public sealed class GameAnalyticsSink : IAnalyticsSink
    {
        private const int MaxFields = 50;
        private const int MaxPartLength = 64;
        private const int MaxStringValue = 256;

        private readonly Dictionary<string, object> _global = new Dictionary<string, object>();

        public string Id => "gameanalytics";

        public bool IsReady => GA.Initialized;

        public void LogEvent(string name, IReadOnlyList<AnalyticsParam> parameters)
        {
            switch (name)
            {
                case "ad_impression":
                case "adskit_impression":
                    return; // ad revenue: GA ILRD plugin, not one event per impression
                case KitEvents.LevelStart:
                    Progression(GAProgressionStatus.Start, parameters, null);
                    break;
                case KitEvents.LevelEnd:
                    Progression(GetLong(parameters, KitEvents.ParamSuccess, 1) != 0
                        ? GAProgressionStatus.Complete : GAProgressionStatus.Fail, parameters, null);
                    break;
                case KitEvents.LevelRestart:
                    Progression(GAProgressionStatus.Fail, parameters, "restart");
                    break;
                case KitEvents.LevelQuit:
                    Progression(GAProgressionStatus.Fail, parameters, "quit");
                    break;
                case "level_abandon":
                    Progression(GAProgressionStatus.Fail, parameters, "abandon");
                    break;
                case "level_fail":
                    Progression(GAProgressionStatus.Fail, parameters, GetString(parameters, "reason") ?? "fail");
                    break;
                case "ad_show": Ad(GAAdAction.Show, parameters); break;
                case "ad_show_fail": Ad(GAAdAction.FailedShow, parameters); break;
                case "ad_clicked": Ad(GAAdAction.Clicked, parameters); break;
                case "ad_rewarded": Ad(GAAdAction.RewardReceived, parameters); break;
                case "iap_purchase_success": Business(parameters); break;
            }
            // Studio standard: every event also goes out as a design event with its exact Firebase name, so the same
            // funnels can be built in both tools. The GA progression / ad / business events above are extra.
            GA.NewDesignEvent(Part(name), Fields(parameters, null));
        }

        /// <summary>
        /// User properties that also set a GA custom dimension (the player groups every GA report can filter by).
        /// Every value must be listed in the GA settings asset first, or GA drops events that carry it.
        /// </summary>
        public static readonly Dictionary<string, int> DimensionProperties = new Dictionary<string, int>
        {
            { "ab_group", 1 },
            { "player_type", 2 },
            { "max_level", 3 },
        };

        public void SetUserProperty(string name, string value)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (value == null) _global.Remove(name);
            else _global[name] = Truncate(value, MaxStringValue);
            GA.SetGlobalCustomEventFields(new Dictionary<string, object>(_global));

            if (!DimensionProperties.TryGetValue(name, out var slot)) return;
            var dim = value ?? "";
            switch (slot)
            {
                case 1: GA.SetCustomDimension01(dim); break;
                case 2: GA.SetCustomDimension02(dim); break;
                case 3: GA.SetCustomDimension03(dim); break;
            }
        }

        // ------------------------------------------------------------------ mapped events

        private void Progression(GAProgressionStatus status, IReadOnlyList<AnalyticsParam> p, string reason)
        {
            var level = GetLong(p, KitEvents.ParamLevel, 0);
            var id = "level_" + level.ToString("D4", CultureInfo.InvariantCulture);
            GA.NewProgressionEvent(status, id, Fields(p, reason));
        }

        private void Ad(GAAdAction action, IReadOnlyList<AnalyticsParam> p)
        {
            var type = AdType(GetString(p, "ad_format"));
            if (type == GAAdType.Undefined) return; // the design event with the Firebase name still goes out
            var network = Part(GetString(p, "med") ?? "unknown");
            var placement = Part(GetString(p, "placement") ?? "default");
            if (action == GAAdAction.FailedShow)
                GA.NewAdEvent(action, type, network, placement, GAAdError.Unknown, Fields(p, null));
            else
                GA.NewAdEvent(action, type, network, placement, Fields(p, null));
        }

        private bool Business(IReadOnlyList<AnalyticsParam> p)
        {
            var currency = GetString(p, "currency");
            var value = GetDouble(p, "value");
            if (string.IsNullOrEmpty(currency) || currency.Length != 3 || value <= 0) return false;
            var item = Part(GetString(p, "product_id") ?? GetString(p, "product") ?? "unknown");
            var cart = Truncate(Part(GetString(p, "placement") ?? "shop"), 32);
            var cents = (int)Math.Round(value * 100.0);
            // Studio standard itemType (IapKit item_type): vip, no_ads, currency or item.
            var rawType = GetString(p, "item_type");
            var itemType = string.IsNullOrEmpty(rawType) ? "item" : Part(rawType);
            GA.NewBusinessEvent(currency.ToUpperInvariant(), cents, itemType, item, cart, Fields(p, null));
            return true;
        }

        private static GAAdType AdType(string format)
        {
            switch (format)
            {
                case "rewarded": return GAAdType.RewardedVideo;
                case "interstitial": return GAAdType.Interstitial;
                case "banner":
                case "mrec": return GAAdType.Banner;
                case "app_open": return GAAdType.AppOpen;
                default: return GAAdType.Undefined;
            }
        }

        // ------------------------------------------------------------------ helpers

        private Dictionary<string, object> Fields(IReadOnlyList<AnalyticsParam> p, string reason)
        {
            var fields = new Dictionary<string, object>();
            var limit = Math.Max(0, MaxFields - _global.Count);
            if (reason != null && limit > 0) fields["reason"] = reason;
            for (var i = 0; i < p.Count && fields.Count < limit; i++)
            {
                var a = p[i];
                if (string.IsNullOrEmpty(a.Name)) continue;
                switch (a.Type)
                {
                    case AnalyticsParamType.String: fields[a.Name] = Truncate(a.StringValue ?? "", MaxStringValue); break;
                    case AnalyticsParamType.Long: fields[a.Name] = a.LongValue; break;
                    default: fields[a.Name] = a.DoubleValue; break;
                }
            }
            return fields;
        }

        private static long GetLong(IReadOnlyList<AnalyticsParam> p, string name, long fallback)
        {
            for (var i = 0; i < p.Count; i++)
            {
                if (p[i].Name != name) continue;
                switch (p[i].Type)
                {
                    case AnalyticsParamType.Long: return p[i].LongValue;
                    case AnalyticsParamType.Double: return (long)p[i].DoubleValue;
                    default: return long.TryParse(p[i].StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
                }
            }
            return fallback;
        }

        private static double GetDouble(IReadOnlyList<AnalyticsParam> p, string name)
        {
            for (var i = 0; i < p.Count; i++)
            {
                if (p[i].Name != name) continue;
                switch (p[i].Type)
                {
                    case AnalyticsParamType.Long: return p[i].LongValue;
                    case AnalyticsParamType.Double: return p[i].DoubleValue;
                    default: return double.TryParse(p[i].StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
                }
            }
            return 0;
        }

        private static string GetString(IReadOnlyList<AnalyticsParam> p, string name)
        {
            for (var i = 0; i < p.Count; i++)
                if (p[i].Name == name && p[i].Type == AnalyticsParamType.String && !string.IsNullOrEmpty(p[i].StringValue))
                    return p[i].StringValue;
            return null;
        }

        /// <summary>One GA id part: A-Z a-z 0-9 - _ . ( ) ! ? and space, at most 64 characters.</summary>
        private static string Part(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            var sb = new StringBuilder(Math.Min(s.Length, MaxPartLength));
            foreach (var c in s)
            {
                if (sb.Length == MaxPartLength) break;
                var ok = c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' ||
                         c == '-' || c == '_' || c == '.' || c == '(' || c == ')' || c == '!' || c == '?' || c == ' ';
                sb.Append(ok ? c : '_');
            }
            return sb.Length == 0 ? "unknown" : sb.ToString();
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }
}
