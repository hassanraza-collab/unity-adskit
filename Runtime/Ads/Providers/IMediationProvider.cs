using System;
using System.Collections.Generic;

namespace NineDTech.AdsKit.Ads
{
    public struct MediationInitArgs
    {
        /// <summary>Every ad unit id this app may request (MAX selective init).</summary>
        public IReadOnlyList<string> AdUnitIds;
        public bool TestMode;
        public bool VerboseLogs;
        public AdsConsentStatus Consent;
    }

    public struct AdLoadResult
    {
        public bool Success;
        public string Error;
        /// <summary>Winning network, when known.</summary>
        public string Network;

        public static AdLoadResult Ok(string network) => new AdLoadResult { Success = true, Network = network };
        public static AdLoadResult Fail(string error) => new AdLoadResult { Success = false, Error = error };
    }

    public struct BannerRequest
    {
        public BannerType Type;
        public BannerPosition Position;
    }

    /// <summary>Impression-level revenue reported by the SDK.</summary>
    public struct AdRevenueEvent
    {
        public AdFormat Format;
        public string AdUnitId;
        public string Network;
        public double Value;
        public string Currency;
        /// <summary>exact | estimated | publisher_defined | unknown</summary>
        public string Precision;
        /// <summary>Placement passed to Show, if the SDK echoes it (otherwise AdsKit fills it in).</summary>
        public string Placement;
    }

    /// <summary>Callbacks of one full-screen show. AdsKit guarantees each show completes exactly once.</summary>
    public interface IFullscreenAdListener
    {
        void OnOpened(string network);
        void OnFailedToShow(string error);
        void OnClicked();
        void OnRewardEarned();
        void OnClosed();
    }

    /// <summary>
    /// One mediation SDK (MAX, AdMob, simulator). Implementations live in their own assemblies and register in
    /// <see cref="MediationProviderRegistry"/>. Callbacks may arrive on any thread; AdsKit marshals them.
    /// Banner methods act on the single banner AdsKit manages.
    /// </summary>
    public interface IMediationProvider
    {
        string Id { get; }

        /// <summary>True when the SDK reloads expired ads itself (MAX); AdsKit then skips its own expiry refresh.</summary>
        bool ManagesExpiry { get; }

        /// <summary>True when Firebase already logs ad_impression for this SDK (AdMob linked to Firebase).</summary>
        bool AutoLogsFirebaseImpressions { get; }

        event Action<AdRevenueEvent> RevenuePaid;

        void Initialize(MediationInitArgs args, Action<bool> onComplete);

        void Load(AdFormat format, string adUnitId, Action<AdLoadResult> onComplete);
        bool IsReady(AdFormat format, string adUnitId);
        void Show(AdFormat format, string adUnitId, string placement, IFullscreenAdListener listener);
        /// <summary>Drop a loaded (unshown) ad and free its memory.</summary>
        void Discard(AdFormat format, string adUnitId);

        void LoadBanner(string adUnitId, BannerRequest request, Action<AdLoadResult> onComplete);
        void SetBannerVisible(bool visible);
        void SetBannerAutoRefresh(bool enabled);
        void DestroyBanner();
        /// <summary>Height of the current banner in screen pixels (0 when none).</summary>
        float BannerHeightPixels { get; }

        /// <summary>Open the SDK's debugging UI (MAX Mediation Debugger / AdMob Ad Inspector).</summary>
        void OpenDebugger();
    }
}
