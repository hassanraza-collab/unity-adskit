using System.Collections.Generic;
using System.Globalization;
using AdjustSdk;
using NineDTech.AdsKit.Ads;
using NineDTech.AdsKit.Attribution;
using NineDTech.AdsKit.Json;
using UnityEngine;
using KitAnalytics = NineDTech.AdsKit.Analytics.Analytics;

namespace NineDTech.AdsKit.Adjust
{
    /// <summary>
    /// Adjust SDK v5 provider: install attribution, Google DMA consent from UMP, ad revenue from <see cref="Ads.Ads.RevenuePaid"/>,
    /// IAP revenue events (optionally verified) and mapped analytics events (<see cref="AdjustAnalyticsSink"/>).
    /// Start order: <see cref="Attribution.Attribution.Initialize"/> → wait for the consent result (or the timeout) →
    /// send DMA consent → InitSdk. Everything tracked before the start is queued and flushed right after it.
    /// In the Editor the SDK is a no-op, so calls are only logged.
    /// </summary>
    public sealed class AdjustAttributionProvider : IAttributionProvider
    {
        private const int MaxQueued = 100;
        private const string SourceMax = "applovin_max_sdk";
        private const string SourceAdMob = "admob_sdk";

        private static AdjustAttributionProvider _instance;

        private AttributionSettings _settings;
        private bool _waiting;
        private bool _started;
        private AdsConsentStatus _sentConsent = AdsConsentStatus.Unknown;
        private readonly List<System.Action> _queue = new List<System.Action>();

        public string Name => "adjust";

        internal static bool Started => _instance != null && _instance._started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            _instance = new AdjustAttributionProvider();
            Attribution.Attribution.RegisterProvider(_instance);
        }

        public void Initialize(AttributionSettings settings)
        {
            if (_settings != null) return;
            if (string.IsNullOrEmpty(settings.appToken))
            {
                KitLog.Error("Adjust: AttributionSettings.appToken is empty; Adjust not started.");
                return;
            }
            _settings = settings;
            KitRunner.Ensure();

            if (settings.trackAdRevenue) Ads.Ads.RevenuePaid += OnAdRevenue;
            KitAnalytics.AddSink(new AdjustAnalyticsSink(settings.events, TrackEvent));
            AdsConsent.StatusChanged += OnConsentChanged;

            if (AdsConsent.Status != AdsConsentStatus.Unknown) Start("consent ready");
            else
            {
                _waiting = true;
                KitRunner.After(Mathf.Max(0f, settings.consentWaitSeconds), () => Start("consent timeout"));
            }
        }

        // ------------------------------------------------------------------ start + consent

        private void OnConsentChanged(AdsConsentStatus status)
        {
            KitRunner.RunOnMainThread(() =>
            {
                if (_settings == null) return;
                if (!_started) { if (_waiting && status != AdsConsentStatus.Unknown) Start("consent ready"); }
                else SendConsent(status);
            });
        }

        private void Start(string reason)
        {
            if (_started || _settings == null) return;
            _started = true;
            _waiting = false;

            // Sent before InitSdk so the first session already carries it (Adjust DMA guidance).
            SendConsent(AdsConsent.Status);

            var sandbox = _settings.environment == AttributionSettings.EnvironmentMode.Sandbox ||
                          _settings.environment == AttributionSettings.EnvironmentMode.Auto && Debug.isDebugBuild;
            var config = new AdjustConfig(_settings.appToken,
                sandbox ? AdjustEnvironment.Sandbox : AdjustEnvironment.Production, !sandbox);
            config.LogLevel = !Debug.isDebugBuild ? AdjustLogLevel.Suppress
                : _settings.verboseLogsInDevelopment ? AdjustLogLevel.Verbose : AdjustLogLevel.Info;
            config.AttributionChangedDelegate = a =>
                KitLog.Info("Adjust attribution: network=" + a?.Network + " campaign=" + a?.Campaign);

            Call("InitSdk (" + (sandbox ? "sandbox" : "production") + ", " + reason + ", consent " + AdsConsent.Status + ")",
                () => AdjustSdk.Adjust.InitSdk(config));

            var queued = _queue.ToArray();
            _queue.Clear();
            foreach (var action in queued) action();
        }

        /// <summary>Google DMA: eea 0 outside the EEA; inside it ad_user_data / ad_personalization follow UMP.</summary>
        private void SendConsent(AdsConsentStatus status)
        {
            if (status == AdsConsentStatus.Unknown || status == _sentConsent) return;
            _sentConsent = status;
            var sharing = new AdjustThirdPartySharing(null);
            if (status == AdsConsentStatus.NotRequired) sharing.AddGranularOption("google_dma", "eea", "0");
            else
            {
                var granted = status == AdsConsentStatus.Granted ? "1" : "0";
                sharing.AddGranularOption("google_dma", "eea", "1");
                sharing.AddGranularOption("google_dma", "ad_personalization", granted);
                sharing.AddGranularOption("google_dma", "ad_user_data", granted);
            }
            Call("DMA consent " + status, () => AdjustSdk.Adjust.TrackThirdPartySharing(sharing));
        }

        // ------------------------------------------------------------------ ad revenue

        private void OnAdRevenue(AdRevenueEvent e)
        {
            if (e.Value <= 0) return;
            var mediation = Ads.Ads.MediationId;
            var source = mediation == AdsIds.Max ? SourceMax : mediation == AdsIds.AdMob ? SourceAdMob : null;
            if (source == null) return; // simulator
            WhenStarted(() =>
            {
                var revenue = new AdjustAdRevenue(source);
                revenue.SetRevenue(e.Value, string.IsNullOrEmpty(e.Currency) ? "USD" : e.Currency);
                revenue.AdImpressionsCount = 1;
                if (!string.IsNullOrEmpty(e.Network)) revenue.AdRevenueNetwork = e.Network;
                if (!string.IsNullOrEmpty(e.AdUnitId)) revenue.AdRevenueUnit = e.AdUnitId;
                if (!string.IsNullOrEmpty(e.Placement)) revenue.AdRevenuePlacement = e.Placement;
                Call("ad revenue " + source + " " + e.Value.ToString("R", CultureInfo.InvariantCulture) + " " + e.Placement,
                    () => AdjustSdk.Adjust.TrackAdRevenue(revenue));
            });
        }

        // ------------------------------------------------------------------ IAP revenue

        public void TrackPurchase(AttributionPurchase p)
        {
            KitRunner.RunOnMainThread(() =>
            {
                if (_settings == null) return;
                if (p.Redelivered && !_settings.trackRedeliveredPurchases) return;
                if (p.FreeTrial || p.Price <= 0m || string.IsNullOrEmpty(p.Currency)) return; // no revenue yet
                var token = p.IsSubscription && !string.IsNullOrEmpty(_settings.subscriptionEventToken)
                    ? _settings.subscriptionEventToken : _settings.purchaseEventToken;
                if (string.IsNullOrEmpty(token)) return;
                WhenStarted(() => SendPurchase(p, token));
            });
        }

        private void SendPurchase(AttributionPurchase p, string token)
        {
            var e = new AdjustEvent(token);
            e.SetRevenue((double)p.Price, p.Currency);
            e.ProductId = p.ProductId;
            if (!string.IsNullOrEmpty(p.TransactionId)) e.DeduplicationId = p.TransactionId;
            e.AddCallbackParameter("product_id", p.ProductId ?? "");
            ReadGooglePlayReceipt(p.Receipt, out var purchaseToken, out var orderId);

            if (Application.platform == RuntimePlatform.Android)
            {
                e.PurchaseToken = purchaseToken ?? p.TransactionId;
                if (!string.IsNullOrEmpty(orderId)) e.TransactionId = orderId;
                if (_settings.verifyPurchases)
                {
                    Call("verify+track Play purchase " + p.ProductId, () => AdjustSdk.Adjust.VerifyAndTrackPlayStorePurchase(e,
                        r => KitLog.Info("Adjust purchase verification: " + r?.VerificationStatus + " (" + r?.Code + ")")));
                    return;
                }
            }
            else if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                e.TransactionId = p.TransactionId;
                if (_settings.verifyPurchases)
                {
                    Call("verify+track App Store purchase " + p.ProductId, () => AdjustSdk.Adjust.VerifyAndTrackAppStorePurchase(e,
                        r => KitLog.Info("Adjust purchase verification: " + r?.VerificationStatus + " (" + r?.Code + ")")));
                    return;
                }
            }
            Call("purchase " + p.ProductId + " " + p.Price.ToString(CultureInfo.InvariantCulture) + " " + p.Currency,
                () => AdjustSdk.Adjust.TrackEvent(e));
        }

        /// <summary>Unity IAP receipt: {"Payload": "{\"json\": \"{...purchaseToken, orderId...}\", ...}"} on Google Play.</summary>
        private static void ReadGooglePlayReceipt(string receipt, out string purchaseToken, out string orderId)
        {
            purchaseToken = orderId = null;
            if (string.IsNullOrEmpty(receipt)) return;
            var root = MiniJson.TryParseObject(receipt, out _);
            if (root == null || !(root.TryGetValue("Payload", out var payloadObj) && payloadObj is string payload)) return;
            var outer = MiniJson.TryParseObject(payload, out _);
            if (outer == null || !(outer.TryGetValue("json", out var jsonObj) && jsonObj is string json)) return;
            var data = MiniJson.TryParseObject(json, out _);
            if (data == null) return;
            data.Read("purchaseToken", ref purchaseToken);
            data.Read("orderId", ref orderId);
        }

        // ------------------------------------------------------------------ events

        private void TrackEvent(string token, string name)
        {
            var e = new AdjustEvent(token);
            Call("event " + name + " → " + token, () => AdjustSdk.Adjust.TrackEvent(e));
        }

        // ------------------------------------------------------------------ helpers

        private void WhenStarted(System.Action action)
        {
            if (_started) { action(); return; }
            if (_queue.Count < MaxQueued) _queue.Add(action);
        }

        private static void Call(string what, System.Action action)
        {
#if UNITY_EDITOR
            KitLog.Info("Adjust (Editor, not sent): " + what);
#else
            KitLog.Info("Adjust: " + what);
            try { action(); }
            catch (System.Exception ex) { Debug.LogException(ex); }
#endif
        }
    }
}
