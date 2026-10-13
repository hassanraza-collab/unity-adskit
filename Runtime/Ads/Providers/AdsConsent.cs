using System;

namespace NineDTech.AdsKit.Ads
{
    public enum AdsConsentStatus
    {
        Unknown,
        /// <summary>Consent not required (outside GDPR/UK/CH regions).</summary>
        NotRequired,
        Granted,
        Denied
    }

    /// <summary>Gathers privacy consent before any ad SDK initializes (Google UMP in production).</summary>
    public interface IAdsConsentProvider
    {
        string Name { get; }

        /// <summary>Show the consent form if needed, then report the result (main thread or any thread).</summary>
        void Gather(Action<AdsConsentStatus> onComplete);

        /// <summary>True when a privacy-options entry point must be offered in settings (GDPR).</summary>
        bool PrivacyOptionsRequired { get; }

        void ShowPrivacyOptions(Action onClosed);
    }

    /// <summary>Holds the consent provider. Without one, consent is treated as not required (simulator / tests).</summary>
    public static class AdsConsent
    {
        private sealed class NotRequiredProvider : IAdsConsentProvider
        {
            public string Name => "none";
            public bool PrivacyOptionsRequired => false;
            public void Gather(Action<AdsConsentStatus> onComplete) => onComplete?.Invoke(AdsConsentStatus.NotRequired);
            public void ShowPrivacyOptions(Action onClosed) => onClosed?.Invoke();
        }

        private static IAdsConsentProvider _provider;

        public static IAdsConsentProvider Provider
        {
            get => _provider ?? (_provider = new NotRequiredProvider());
            set => _provider = value;
        }

        public static AdsConsentStatus Status { get; internal set; } = AdsConsentStatus.Unknown;
    }
}
