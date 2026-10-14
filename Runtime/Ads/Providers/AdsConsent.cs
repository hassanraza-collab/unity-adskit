using System;
using System.Collections.Generic;

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

        /// <summary>False while ads must not be requested (e.g. consent info could not be obtained yet).</summary>
        bool CanRequestAds { get; }

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
            public bool CanRequestAds => true;
            public bool PrivacyOptionsRequired => false;
            public void Gather(Action<AdsConsentStatus> onComplete) => onComplete?.Invoke(AdsConsentStatus.NotRequired);
            public void ShowPrivacyOptions(Action onClosed) => onClosed?.Invoke();
        }

        private static IAdsConsentProvider _provider;
        private static AdsConsentStatus _status = AdsConsentStatus.Unknown;

        public static IAdsConsentProvider Provider
        {
            get => _provider ?? (_provider = new NotRequiredProvider());
            set => _provider = value;
        }

        public static AdsConsentStatus Status
        {
            get => _status;
            internal set
            {
                if (_status == value) return;
                _status = value;
                StatusChanged?.Invoke(value);
            }
        }

        /// <summary>Raised when the consent result changes (Firebase consent mode listens to this).</summary>
        public static event Action<AdsConsentStatus> StatusChanged;

        /// <summary>Development builds: pretend the device is in the EEA so the consent form always appears.</summary>
        public static bool DebugForceEea;

        /// <summary>Development builds: hashed device ids for consent debugging (printed by UMP in logcat).</summary>
        public static readonly List<string> DebugTestDeviceHashedIds = new List<string>();

        /// <summary>Consent providers call this when the user changes their choice later (privacy options form).</summary>
        public static void Report(AdsConsentStatus status) => Status = status;

        /// <summary>Show the privacy options form (settings screen button). Only when <see cref="IAdsConsentProvider.PrivacyOptionsRequired"/>.</summary>
        public static void ShowPrivacyOptions(Action onClosed = null) => Provider.ShowPrivacyOptions(onClosed);
    }
}
