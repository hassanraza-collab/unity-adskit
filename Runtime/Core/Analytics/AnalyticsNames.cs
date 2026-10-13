using System.Collections.Generic;

namespace NineDTech.AdsKit.Analytics
{
    /// <summary>Firebase / GA4 naming rules and limits. Checked at runtime and by the Editor validator.</summary>
    public static class AnalyticsNames
    {
        public const int MaxEventNameLength = 40;
        public const int MaxParamNameLength = 40;
        public const int MaxParamsPerEvent = 25;
        public const int MaxStringValueLength = 100;
        public const int MaxUserPropertyNameLength = 24;
        public const int MaxUserPropertyValueLength = 36;

        private static readonly string[] ReservedPrefixes = { "firebase_", "google_", "ga_" };

        /// <summary>Event names Firebase reserves (logging them is silently dropped).</summary>
        public static readonly HashSet<string> ReservedEvents = new HashSet<string>
        {
            "ad_activeview", "ad_click", "ad_exposure", "ad_query", "ad_reward", "adunit_exposure",
            "app_background", "app_clear_data", "app_exception", "app_remove", "app_store_refund",
            "app_store_subscription_cancel", "app_store_subscription_convert", "app_store_subscription_renew",
            "app_update", "app_upgrade", "dynamic_link_app_open", "dynamic_link_app_update",
            "dynamic_link_first_open", "error", "first_open", "first_visit", "in_app_purchase",
            "notification_dismiss", "notification_foreground", "notification_open", "notification_receive",
            "os_update", "session_start", "session_start_with_rollout", "user_engagement"
        };

        /// <summary>Letters, digits and underscores; starts with a letter; within the length limit.</summary>
        public static bool IsValidIdentifier(string name, int maxLength)
        {
            if (string.IsNullOrEmpty(name) || name.Length > maxLength) return false;
            if (!IsAsciiLetter(name[0])) return false;
            for (var i = 1; i < name.Length; i++)
            {
                var c = name[i];
                if (!IsAsciiLetter(c) && !(c >= '0' && c <= '9') && c != '_') return false;
            }
            foreach (var prefix in ReservedPrefixes)
                if (name.StartsWith(prefix, System.StringComparison.Ordinal)) return false;
            return true;
        }

        public static string ValidateEventName(string name)
        {
            if (!IsValidIdentifier(name, MaxEventNameLength))
                return "invalid event name '" + name + "' (letters/digits/_, starts with a letter, <= 40 chars, no firebase_/google_/ga_ prefix)";
            if (ReservedEvents.Contains(name)) return "event name '" + name + "' is reserved by Firebase";
            return null;
        }

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }
}
