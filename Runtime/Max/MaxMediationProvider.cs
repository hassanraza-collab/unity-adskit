using System;
using System.Collections.Generic;
using NineDTech.AdsKit.Ads;
using UnityEngine;

namespace NineDTech.AdsKit.Max
{
    /// <summary>
    /// AppLovin MAX behind <see cref="IMediationProvider"/>. Compiled only with ADSKIT_MAX.
    /// The SDK key comes from AppLovin's Integration Manager (AppLovinSettings). MAX reloads expired ads itself.
    /// </summary>
    public sealed class MaxMediationProvider : IMediationProvider
    {
        private const float StandardBannerDp = 50f;
        private const float TabletBannerDp = 90f;
        private const float MrecWidthDp = 300f;
        private const float MrecHeightDp = 250f;

        private readonly Dictionary<string, Action<AdLoadResult>> _pendingLoads = new Dictionary<string, Action<AdLoadResult>>();
        private readonly Dictionary<string, IFullscreenAdListener> _showing = new Dictionary<string, IFullscreenAdListener>();

        private bool _subscribed;
        private Action<bool> _initCallback;
        private string _bannerId;
        private BannerType _bannerType;
        private Action<AdLoadResult> _pendingBanner;
        private string _mrecId;
        private Action<AdLoadResult> _pendingMrec;

        public string Id => AdsIds.Max;
        public bool ManagesExpiry => true;
        public bool AutoLogsFirebaseImpressions => false;

        public event Action<AdRevenueEvent> RevenuePaid;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            MediationProviderRegistry.Register(AdsIds.Max, () => new MaxMediationProvider());
        }

        public void Initialize(MediationInitArgs args, Action<bool> onComplete)
        {
            Subscribe();
            _initCallback = onComplete;
            MaxSdk.SetVerboseLogging(args.VerboseLogs);
            if (args.Consent == AdsConsentStatus.Granted || args.Consent == AdsConsentStatus.NotRequired)
                MaxSdk.SetHasUserConsent(true);
            else if (args.Consent == AdsConsentStatus.Denied)
                MaxSdk.SetHasUserConsent(false);
            if (args.TestMode && args.TestDeviceIds != null && args.TestDeviceIds.Count > 0)
                MaxSdk.SetTestDeviceAdvertisingIdentifiers(ToArray(args.TestDeviceIds));
            MaxSdk.InitializeSdk(args.AdUnitIds != null && args.AdUnitIds.Count > 0 ? ToArray(args.AdUnitIds) : null);
        }

        public void Load(AdFormat format, string adUnitId, Action<AdLoadResult> onComplete)
        {
            _pendingLoads[adUnitId] = onComplete;
            switch (format)
            {
                case AdFormat.Interstitial: MaxSdk.LoadInterstitial(adUnitId); break;
                case AdFormat.Rewarded: MaxSdk.LoadRewardedAd(adUnitId); break;
                case AdFormat.AppOpen: MaxSdk.LoadAppOpenAd(adUnitId); break;
                default:
                    _pendingLoads.Remove(adUnitId);
                    onComplete?.Invoke(AdLoadResult.Fail("unsupported format " + format));
                    break;
            }
        }

        public bool IsReady(AdFormat format, string adUnitId)
        {
            switch (format)
            {
                case AdFormat.Interstitial: return MaxSdk.IsInterstitialReady(adUnitId);
                case AdFormat.Rewarded: return MaxSdk.IsRewardedAdReady(adUnitId);
                case AdFormat.AppOpen: return MaxSdk.IsAppOpenAdReady(adUnitId);
                default: return false;
            }
        }

        public void Show(AdFormat format, string adUnitId, string placement, IFullscreenAdListener listener)
        {
            _showing[adUnitId] = listener;
            switch (format)
            {
                case AdFormat.Interstitial: MaxSdk.ShowInterstitial(adUnitId, placement); break;
                case AdFormat.Rewarded: MaxSdk.ShowRewardedAd(adUnitId, placement); break;
                case AdFormat.AppOpen: MaxSdk.ShowAppOpenAd(adUnitId, placement); break;
                default:
                    _showing.Remove(adUnitId);
                    listener.OnFailedToShow("unsupported format " + format);
                    break;
            }
        }

        // MAX keeps one ad per unit and has no API to drop it; the next Load call is a no-op while one is ready.
        public void Discard(AdFormat format, string adUnitId) => _pendingLoads.Remove(adUnitId);

        public void LoadBanner(string adUnitId, BannerRequest request, Action<AdLoadResult> onComplete)
        {
            DestroyBanner();
            _bannerId = adUnitId;
            _bannerType = request.Type;
            _pendingBanner = onComplete;
            var position = request.Position == BannerPosition.Top
                ? MaxSdkBase.AdViewPosition.TopCenter
                : MaxSdkBase.AdViewPosition.BottomCenter;
            // Banners start hidden and load (and refresh) automatically after creation.
            MaxSdk.CreateBanner(adUnitId, new MaxSdkBase.AdViewConfiguration(position) { IsAdaptive = request.Type == BannerType.Adaptive });
            MaxSdk.SetBannerBackgroundColor(adUnitId, Color.black);
        }

        public void SetBannerVisible(bool visible)
        {
            if (_bannerId == null) return;
            if (visible) MaxSdk.ShowBanner(_bannerId);
            else MaxSdk.HideBanner(_bannerId);
        }

        public void SetBannerAutoRefresh(bool enabled)
        {
            if (_bannerId == null) return;
            if (enabled) MaxSdk.StartBannerAutoRefresh(_bannerId);
            else MaxSdk.StopBannerAutoRefresh(_bannerId);
        }

        public void DestroyBanner()
        {
            if (_bannerId == null) return;
            MaxSdk.DestroyBanner(_bannerId);
            _bannerId = null;
            _pendingBanner = null;
        }

        public float BannerHeightPixels
        {
            get
            {
                if (_bannerId == null) return 0f;
                float dp;
                if (_bannerType == BannerType.Adaptive) dp = MaxSdkUtils.GetAdaptiveBannerHeight();
                else dp = MaxSdkUtils.IsTablet() ? TabletBannerDp : StandardBannerDp;
                return dp * MaxSdkUtils.GetScreenDensity();
            }
        }

        public event Action AdViewClicked;

        public void LoadMrec(string adUnitId, Action<AdLoadResult> onComplete)
        {
            DestroyMrec();
            _mrecId = adUnitId;
            _pendingMrec = onComplete;
            // Like banners, an MREC starts hidden and loads (and refreshes) automatically after creation.
            MaxSdk.CreateMRec(adUnitId, new MaxSdkBase.AdViewConfiguration(MaxSdkBase.AdViewPosition.Centered));
        }

        public void ShowMrec(Vector2 centerPixels)
        {
            if (_mrecId == null) return;
            var density = Mathf.Max(0.1f, MaxSdkUtils.GetScreenDensity());
            MaxSdk.UpdateMRecPosition(_mrecId, centerPixels.x / density - MrecWidthDp * 0.5f, centerPixels.y / density - MrecHeightDp * 0.5f);
            MaxSdk.ShowMRec(_mrecId);
        }

        public void HideMrec()
        {
            if (_mrecId != null) MaxSdk.HideMRec(_mrecId);
        }

        public void DestroyMrec()
        {
            if (_mrecId == null) return;
            MaxSdk.DestroyMRec(_mrecId);
            _mrecId = null;
            _pendingMrec = null;
        }

        public void OpenDebugger() => MaxSdk.ShowMediationDebugger();

        // ------------------------------------------------------------------ callbacks

        private void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;

            MaxSdkCallbacks.OnSdkInitializedEvent += config =>
            {
                var cb = _initCallback;
                _initCallback = null;
                cb?.Invoke(config != null && config.IsSuccessfullyInitialized);
            };

            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnLoaded;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnLoadFailed;
            MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += OnDisplayed;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnDisplayFailed;
            MaxSdkCallbacks.Interstitial.OnAdClickedEvent += OnClicked;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += OnHidden;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += (id, info) => Pay(AdFormat.Interstitial, id, info);

            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += OnLoadFailed;
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += OnDisplayed;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += OnDisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdClickedEvent += OnClicked;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += OnHidden;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (id, reward, info) =>
            {
                if (_showing.TryGetValue(id, out var l)) l.OnRewardEarned();
            };
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += (id, info) => Pay(AdFormat.Rewarded, id, info);

            MaxSdkCallbacks.AppOpen.OnAdLoadedEvent += OnLoaded;
            MaxSdkCallbacks.AppOpen.OnAdLoadFailedEvent += OnLoadFailed;
            MaxSdkCallbacks.AppOpen.OnAdDisplayedEvent += OnDisplayed;
            MaxSdkCallbacks.AppOpen.OnAdDisplayFailedEvent += OnDisplayFailed;
            MaxSdkCallbacks.AppOpen.OnAdClickedEvent += OnClicked;
            MaxSdkCallbacks.AppOpen.OnAdHiddenEvent += OnHidden;
            MaxSdkCallbacks.AppOpen.OnAdRevenuePaidEvent += (id, info) => Pay(AdFormat.AppOpen, id, info);

            MaxSdkCallbacks.Banner.OnAdLoadedEvent += (id, info) =>
            {
                if (id != _bannerId || _pendingBanner == null) return;
                var cb = _pendingBanner;
                _pendingBanner = null;
                cb(AdLoadResult.Ok(info?.NetworkName));
            };
            MaxSdkCallbacks.Banner.OnAdLoadFailedEvent += (id, error) =>
            {
                if (id != _bannerId || _pendingBanner == null) return; // later refresh failures: MAX keeps retrying itself
                var cb = _pendingBanner;
                DestroyBanner();
                cb(AdLoadResult.Fail(Describe(error)));
            };
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent += (id, info) => Pay(AdFormat.Banner, id, info);
            MaxSdkCallbacks.Banner.OnAdClickedEvent += (id, info) => AdViewClicked?.Invoke();

            MaxSdkCallbacks.MRec.OnAdLoadedEvent += (id, info) =>
            {
                if (id != _mrecId || _pendingMrec == null) return;
                var cb = _pendingMrec;
                _pendingMrec = null;
                cb(AdLoadResult.Ok(info?.NetworkName));
            };
            MaxSdkCallbacks.MRec.OnAdLoadFailedEvent += (id, error) =>
            {
                if (id != _mrecId || _pendingMrec == null) return; // later refresh failures: MAX keeps retrying itself
                var cb = _pendingMrec;
                DestroyMrec();
                cb(AdLoadResult.Fail(Describe(error)));
            };
            MaxSdkCallbacks.MRec.OnAdRevenuePaidEvent += (id, info) => Pay(AdFormat.Mrec, id, info);
            MaxSdkCallbacks.MRec.OnAdClickedEvent += (id, info) => AdViewClicked?.Invoke();
        }

        private void OnLoaded(string id, MaxSdkBase.AdInfo info)
        {
            if (!_pendingLoads.TryGetValue(id, out var cb)) return;
            _pendingLoads.Remove(id);
            cb?.Invoke(AdLoadResult.Ok(info?.NetworkName));
        }

        private void OnLoadFailed(string id, MaxSdkBase.ErrorInfo error)
        {
            if (!_pendingLoads.TryGetValue(id, out var cb)) return;
            _pendingLoads.Remove(id);
            cb?.Invoke(AdLoadResult.Fail(Describe(error)));
        }

        private void OnDisplayed(string id, MaxSdkBase.AdInfo info)
        {
            if (_showing.TryGetValue(id, out var l)) l.OnOpened(info?.NetworkName);
        }

        private void OnDisplayFailed(string id, MaxSdkBase.ErrorInfo error, MaxSdkBase.AdInfo info)
        {
            if (!_showing.TryGetValue(id, out var l)) return;
            _showing.Remove(id);
            l.OnFailedToShow(Describe(error));
        }

        private void OnClicked(string id, MaxSdkBase.AdInfo info)
        {
            if (_showing.TryGetValue(id, out var l)) l.OnClicked();
        }

        private void OnHidden(string id, MaxSdkBase.AdInfo info)
        {
            if (!_showing.TryGetValue(id, out var l)) return;
            _showing.Remove(id);
            l.OnClosed();
        }

        private void Pay(AdFormat format, string id, MaxSdkBase.AdInfo info)
        {
            if (info == null) return;
            RevenuePaid?.Invoke(new AdRevenueEvent
            {
                Format = format,
                AdUnitId = id,
                Network = info.NetworkName,
                Value = Math.Max(0, info.Revenue), // MAX reports -1 when revenue is unavailable
                Currency = "USD",
                Precision = info.RevenuePrecision,
                Placement = info.Placement
            });
        }

        private static string Describe(MaxSdkBase.ErrorInfo error) =>
            error == null ? "unknown" : error.Code + ": " + error.Message;

        private static string[] ToArray(IReadOnlyList<string> list)
        {
            var array = new string[list.Count];
            for (var i = 0; i < list.Count; i++) array[i] = list[i];
            return array;
        }
    }
}
