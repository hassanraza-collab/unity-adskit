using System;
using System.Collections.Generic;
using UnityEngine;

namespace NineDTech.AdsKit.Attribution
{
    /// <summary>
    /// Adjust (MMP) setup. Passed to <see cref="Attribution.Initialize"/>; used only when the Adjust SDK is installed
    /// (ADSKIT_ADJUST). App and event tokens are not secrets: they come from the Adjust dashboard.
    /// </summary>
    [CreateAssetMenu(menuName = "AdsKit/Attribution Settings", fileName = "AttributionSettings")]
    public sealed class AttributionSettings : ScriptableObject
    {
        public enum EnvironmentMode
        {
            /// <summary>Sandbox in the Editor and development builds, Production in release builds.</summary>
            Auto,
            Sandbox,
            Production
        }

        [Serializable]
        public sealed class EventMapping
        {
            [Tooltip("AdsKit analytics event name, e.g. level_milestone_10 or tutorial_complete.")]
            public string analyticsEvent;

            [Tooltip("Adjust event token (6 characters) from the dashboard.")]
            public string eventToken;
        }

        [Tooltip("Master switch.")]
        public bool enabled = true;

        [Tooltip("Adjust app token from the dashboard (12 characters).")]
        public string appToken;

        public EnvironmentMode environment = EnvironmentMode.Auto;

        [Tooltip("Verbose Adjust logs in the Editor and development builds (Info otherwise; Suppress in release).")]
        public bool verboseLogsInDevelopment = true;

        [Tooltip("Seconds to wait for the consent result (UMP) before starting Adjust without it. Google DMA consent " +
                 "is sent before the first session when it arrives in time, and as an update otherwise.")]
        public float consentWaitSeconds = 10f;

        [Header("Ad revenue")]
        [Tooltip("Forward every paid impression (MAX / AdMob) to Adjust ad revenue.")]
        public bool trackAdRevenue = true;

        [Header("In-app purchases")]
        [Tooltip("Studio standard 'purchase': revenue event token for every purchase (consumables, Remove Ads, VIP). " +
                 "Empty = purchases not tracked.")]
        public string purchaseEventToken;

        [Tooltip("Studio standard 'first_purchase': revenue event sent once per player, on the first paid purchase. " +
                 "Empty = not sent.")]
        public string firstPurchaseEventToken;

        [Tooltip("Studio standard 'remove_ads_purchase': revenue event for Remove Ads or VIP purchases " +
                 "(AttributionPurchase.RemovesAds). Empty = not sent.")]
        public string removeAdsPurchaseEventToken;

        [Tooltip("Legacy, leave empty: the studio standard sends subscriptions as 'purchase' too. When set, " +
                 "subscriptions go to this token instead of purchaseEventToken.")]
        public string subscriptionEventToken;

        [Tooltip("Also track purchases the store redelivers without a purchase started this session " +
                 "(interrupted / deferred purchases). Off avoids counting restores on a reinstall twice.")]
        public bool trackRedeliveredPurchases;

        [Tooltip("Use Adjust Purchase Verification (paid add-on, enable it in the dashboard first): the revenue " +
                 "event is sent only after Adjust validates the receipt with the store.")]
        public bool verifyPurchases;

        [Header("Custom events")]
        [Tooltip("Analytics events forwarded to Adjust as events. Only names listed here are sent.")]
        public List<EventMapping> events = new List<EventMapping>();
    }
}
