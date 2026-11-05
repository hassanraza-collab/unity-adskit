using UnityEngine;

namespace NineDTech.AdsKit.Attribution
{
    /// <summary>A completed store purchase, reported by IapKit (or the game) for MMP revenue tracking.</summary>
    public struct AttributionPurchase
    {
        /// <summary>Store product ID (SKU).</summary>
        public string ProductId;
        public decimal Price;
        /// <summary>ISO 4217 code from the store.</summary>
        public string Currency;
        public string TransactionId;
        /// <summary>Unity IAP receipt JSON (Play purchase token and order ID are read from its payload).</summary>
        public string Receipt;
        public bool IsSubscription;
        public bool FreeTrial;
        /// <summary>True when the store delivered it without a purchase started this session.</summary>
        public bool Redelivered;
    }

    /// <summary>Implemented by an MMP provider assembly (NineDTech.AdsKit.Adjust).</summary>
    public interface IAttributionProvider
    {
        string Name { get; }
        void Initialize(AttributionSettings settings);
        void TrackPurchase(AttributionPurchase purchase);
    }

    /// <summary>
    /// Static entry point for attribution (Adjust). Call <see cref="Initialize"/> on the splash, before
    /// <c>Ads.Initialize</c>, so the provider sees the consent result. Without the SDK installed every call is a no-op.
    /// </summary>
    public static class Attribution
    {
        private static IAttributionProvider _provider;
        private static bool _initialized;

        public static bool HasProvider => _provider != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _provider = null;
            _initialized = false;
        }

        /// <summary>Called by the provider assembly from [RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)].</summary>
        public static void RegisterProvider(IAttributionProvider provider) => _provider = provider;

        public static void Initialize(AttributionSettings settings)
        {
            if (_initialized) return;
            _initialized = true;
            if (settings == null || !settings.enabled)
            {
                KitLog.Info("Attribution disabled (no AttributionSettings or not enabled).");
                return;
            }
            if (_provider == null)
            {
                KitLog.Warn("Attribution: AttributionSettings assigned but the Adjust SDK is not installed (no ADSKIT_ADJUST).");
                return;
            }
            _provider.Initialize(settings);
        }

        public static void TrackPurchase(AttributionPurchase purchase)
        {
            if (_initialized) _provider?.TrackPurchase(purchase);
        }
    }
}
