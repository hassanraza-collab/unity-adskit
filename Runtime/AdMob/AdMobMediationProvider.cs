using System;
using System.Collections.Generic;
using GoogleMobileAds.Api;
using NineDTech.AdsKit.Ads;
using UnityEngine;
using AdFormat = NineDTech.AdsKit.Ads.AdFormat;

namespace NineDTech.AdsKit.AdMob
{
    /// <summary>
    /// Google Mobile Ads (classic SDK) mediation behind <see cref="IMediationProvider"/>. Compiled only with ADSKIT_ADMOB.
    /// Never sets MobileAds.RaiseAdEventsOnUnityMainThread (it delays/drops paid events); AdsKit marshals callbacks.
    /// AdsKit refreshes loaded ads before Google's expiry (1 h, app-open 4 h).
    /// </summary>
    public sealed class AdMobMediationProvider : IMediationProvider
    {
        private readonly Dictionary<string, InterstitialAd> _interstitials = new Dictionary<string, InterstitialAd>();
        private readonly Dictionary<string, RewardedAd> _rewarded = new Dictionary<string, RewardedAd>();
        private readonly Dictionary<string, AppOpenAd> _appOpen = new Dictionary<string, AppOpenAd>();

        private bool _initialized;
        private BannerView _banner;
        private string _bannerId;
        private Action<AdLoadResult> _pendingBanner;
        private BannerView _mrec;
        private Action<AdLoadResult> _pendingMrec;

        public string Id => AdsIds.AdMob;
        public bool ManagesExpiry => false;

        /// <summary>With AdMob linked to Firebase, Firebase logs ad_impression itself; AdsKit must not double-count.</summary>
        public bool AutoLogsFirebaseImpressions => true;

        public event Action<AdRevenueEvent> RevenuePaid;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            MediationProviderRegistry.Register(AdsIds.AdMob, () => new AdMobMediationProvider());
            AdsConsent.Provider = new UmpConsentProvider();
        }

        public void Initialize(MediationInitArgs args, Action<bool> onComplete)
        {
            if (args.TestMode && args.TestDeviceIds != null && args.TestDeviceIds.Count > 0)
                MobileAds.SetRequestConfiguration(new RequestConfiguration { TestDeviceIds = new List<string>(args.TestDeviceIds) });
            if (_initialized)
            {
                onComplete?.Invoke(true);
                return;
            }
            MobileAds.Initialize(status =>
            {
                _initialized = true;
                if (status != null && args.VerboseLogs)
                    foreach (var pair in status.getAdapterStatusMap())
                        KitLog.Info("AdMob adapter " + pair.Key + ": " + pair.Value.InitializationState + " " + pair.Value.Description);
                onComplete?.Invoke(true);
            });
        }

        public void Load(AdFormat format, string adUnitId, Action<AdLoadResult> onComplete)
        {
            switch (format)
            {
                case AdFormat.Interstitial:
                    InterstitialAd.Load(adUnitId, new AdRequest(), (ad, error) => KitRunner.Dispatch(() =>
                    {
                        if (ad == null || error != null) { onComplete?.Invoke(AdLoadResult.Fail(Describe(error))); return; }
                        Replace(_interstitials, adUnitId, ad, a => a.Destroy());
                        ad.OnAdPaid += v => Pay(AdFormat.Interstitial, adUnitId, v, ad.GetResponseInfo());
                        onComplete?.Invoke(AdLoadResult.Ok(Network(ad.GetResponseInfo())));
                    }));
                    break;
                case AdFormat.Rewarded:
                    RewardedAd.Load(adUnitId, new AdRequest(), (ad, error) => KitRunner.Dispatch(() =>
                    {
                        if (ad == null || error != null) { onComplete?.Invoke(AdLoadResult.Fail(Describe(error))); return; }
                        Replace(_rewarded, adUnitId, ad, a => a.Destroy());
                        ad.OnAdPaid += v => Pay(AdFormat.Rewarded, adUnitId, v, ad.GetResponseInfo());
                        onComplete?.Invoke(AdLoadResult.Ok(Network(ad.GetResponseInfo())));
                    }));
                    break;
                case AdFormat.AppOpen:
                    AppOpenAd.Load(adUnitId, new AdRequest(), (ad, error) => KitRunner.Dispatch(() =>
                    {
                        if (ad == null || error != null) { onComplete?.Invoke(AdLoadResult.Fail(Describe(error))); return; }
                        Replace(_appOpen, adUnitId, ad, a => a.Destroy());
                        ad.OnAdPaid += v => Pay(AdFormat.AppOpen, adUnitId, v, ad.GetResponseInfo());
                        onComplete?.Invoke(AdLoadResult.Ok(Network(ad.GetResponseInfo())));
                    }));
                    break;
                default:
                    onComplete?.Invoke(AdLoadResult.Fail("unsupported format " + format));
                    break;
            }
        }

        public bool IsReady(AdFormat format, string adUnitId)
        {
            switch (format)
            {
                case AdFormat.Interstitial: return _interstitials.TryGetValue(adUnitId, out var i) && i.CanShowAd();
                case AdFormat.Rewarded: return _rewarded.TryGetValue(adUnitId, out var r) && r.CanShowAd();
                case AdFormat.AppOpen: return _appOpen.TryGetValue(adUnitId, out var a) && a.CanShowAd();
                default: return false;
            }
        }

        public void Show(AdFormat format, string adUnitId, string placement, IFullscreenAdListener listener)
        {
            switch (format)
            {
                case AdFormat.Interstitial when _interstitials.TryGetValue(adUnitId, out var i):
                    _interstitials.Remove(adUnitId);
                    i.OnAdFullScreenContentOpened += () => KitRunner.Dispatch(() => listener.OnOpened(Network(i.GetResponseInfo())));
                    i.OnAdClicked += () => KitRunner.Dispatch(listener.OnClicked);
                    i.OnAdFullScreenContentFailed += e => KitRunner.Dispatch(() => { listener.OnFailedToShow(Describe(e)); i.Destroy(); });
                    i.OnAdFullScreenContentClosed += () => KitRunner.Dispatch(() => { listener.OnClosed(); i.Destroy(); });
                    i.Show();
                    break;
                case AdFormat.Rewarded when _rewarded.TryGetValue(adUnitId, out var r):
                    _rewarded.Remove(adUnitId);
                    r.OnAdFullScreenContentOpened += () => KitRunner.Dispatch(() => listener.OnOpened(Network(r.GetResponseInfo())));
                    r.OnAdClicked += () => KitRunner.Dispatch(listener.OnClicked);
                    r.OnAdFullScreenContentFailed += e => KitRunner.Dispatch(() => { listener.OnFailedToShow(Describe(e)); r.Destroy(); });
                    r.OnAdFullScreenContentClosed += () => KitRunner.Dispatch(() => { listener.OnClosed(); r.Destroy(); });
                    r.Show(_ => KitRunner.Dispatch(listener.OnRewardEarned));
                    break;
                case AdFormat.AppOpen when _appOpen.TryGetValue(adUnitId, out var a):
                    _appOpen.Remove(adUnitId);
                    a.OnAdFullScreenContentOpened += () => KitRunner.Dispatch(() => listener.OnOpened(Network(a.GetResponseInfo())));
                    a.OnAdClicked += () => KitRunner.Dispatch(listener.OnClicked);
                    a.OnAdFullScreenContentFailed += e => KitRunner.Dispatch(() => { listener.OnFailedToShow(Describe(e)); a.Destroy(); });
                    a.OnAdFullScreenContentClosed += () => KitRunner.Dispatch(() => { listener.OnClosed(); a.Destroy(); });
                    a.Show();
                    break;
                default:
                    listener.OnFailedToShow("not_loaded");
                    break;
            }
        }

        public void Discard(AdFormat format, string adUnitId)
        {
            switch (format)
            {
                case AdFormat.Interstitial: Remove(_interstitials, adUnitId, a => a.Destroy()); break;
                case AdFormat.Rewarded: Remove(_rewarded, adUnitId, a => a.Destroy()); break;
                case AdFormat.AppOpen: Remove(_appOpen, adUnitId, a => a.Destroy()); break;
            }
        }

        public void LoadBanner(string adUnitId, BannerRequest request, Action<AdLoadResult> onComplete)
        {
            DestroyBanner();
            var size = request.Type == BannerType.Standard
                ? AdSize.Banner
                : AdSize.GetCurrentOrientationAnchoredAdaptiveBannerAdSizeWithWidth(AdSize.FullWidth);
            var position = request.Position == BannerPosition.Top ? AdPosition.Top : AdPosition.Bottom;
            var banner = new BannerView(adUnitId, size, position);
            _banner = banner;
            _bannerId = adUnitId;
            _pendingBanner = onComplete;
            banner.OnBannerAdLoaded += () => KitRunner.Dispatch(() =>
            {
                if (_banner != banner || _pendingBanner == null) return;
                var cb = _pendingBanner;
                _pendingBanner = null;
                cb(AdLoadResult.Ok(Network(banner.GetResponseInfo())));
            });
            banner.OnBannerAdLoadFailed += error => KitRunner.Dispatch(() =>
            {
                if (_banner != banner || _pendingBanner == null) return; // refresh failures: Google retries on its own
                var cb = _pendingBanner;
                DestroyBanner();
                cb(AdLoadResult.Fail(Describe(error)));
            });
            banner.OnAdPaid += v => Pay(AdFormat.Banner, adUnitId, v, banner.GetResponseInfo());
            banner.OnAdClicked += () => KitRunner.Dispatch(() => AdViewClicked?.Invoke());
            banner.Hide(); // AdsKit decides when it is visible
            banner.LoadAd(new AdRequest());
        }

        public void SetBannerVisible(bool visible)
        {
            if (_banner == null) return;
            if (visible) _banner.Show();
            else _banner.Hide();
        }

        // Google controls banner refresh from the AdMob console; there is no client API to pause it.
        public void SetBannerAutoRefresh(bool enabled) { }

        public void DestroyBanner()
        {
            if (_banner == null) return;
            _banner.Destroy();
            _banner = null;
            _bannerId = null;
            _pendingBanner = null;
        }

        public float BannerHeightPixels => _banner != null ? _banner.GetHeightInPixels() : 0f;

        public event Action AdViewClicked;

        public void LoadMrec(string adUnitId, Action<AdLoadResult> onComplete)
        {
            DestroyMrec();
            var mrec = new BannerView(adUnitId, AdSize.MediumRectangle, AdPosition.Center);
            _mrec = mrec;
            _pendingMrec = onComplete;
            mrec.OnBannerAdLoaded += () => KitRunner.Dispatch(() =>
            {
                if (_mrec != mrec || _pendingMrec == null) return;
                var cb = _pendingMrec;
                _pendingMrec = null;
                cb(AdLoadResult.Ok(Network(mrec.GetResponseInfo())));
            });
            mrec.OnBannerAdLoadFailed += error => KitRunner.Dispatch(() =>
            {
                if (_mrec != mrec || _pendingMrec == null) return;
                var cb = _pendingMrec;
                DestroyMrec();
                cb(AdLoadResult.Fail(Describe(error)));
            });
            mrec.OnAdPaid += v => Pay(AdFormat.Mrec, adUnitId, v, mrec.GetResponseInfo());
            mrec.OnAdClicked += () => KitRunner.Dispatch(() => AdViewClicked?.Invoke());
            mrec.Hide(); // AdsKit decides when it is visible
            mrec.LoadAd(new AdRequest());
        }

        public void ShowMrec(Vector2 centerPixels)
        {
            if (_mrec == null) return;
            var scale = Mathf.Max(0.1f, MobileAds.Utils.GetDeviceScale());
            _mrec.SetPosition(Mathf.RoundToInt(centerPixels.x / scale - 150f), Mathf.RoundToInt(centerPixels.y / scale - 125f));
            _mrec.Show();
        }

        public void HideMrec() => _mrec?.Hide();

        public void DestroyMrec()
        {
            if (_mrec == null) return;
            _mrec.Destroy();
            _mrec = null;
            _pendingMrec = null;
        }

        public void OpenDebugger()
        {
            MobileAds.OpenAdInspector(error =>
            {
                if (error != null) KitLog.Warn("Ad Inspector: " + error.GetMessage());
            });
        }

        // ------------------------------------------------------------------ helpers

        private void Pay(AdFormat format, string adUnitId, AdValue value, ResponseInfo info)
        {
            if (value == null) return;
            RevenuePaid?.Invoke(new AdRevenueEvent
            {
                Format = format,
                AdUnitId = adUnitId,
                Network = Network(info),
                Value = value.Value / 1000000.0, // micros
                Currency = value.CurrencyCode,
                Precision = PrecisionName(value.Precision)
            });
        }

        private static string PrecisionName(AdValue.PrecisionType precision)
        {
            switch (precision)
            {
                case AdValue.PrecisionType.Precise: return "exact";
                case AdValue.PrecisionType.Estimated: return "estimated";
                case AdValue.PrecisionType.PublisherProvided: return "publisher_defined";
                default: return "unknown";
            }
        }

        private static string Network(ResponseInfo info)
        {
            if (info == null) return null;
            var adapter = info.GetLoadedAdapterResponseInfo();
            return adapter != null ? adapter.AdSourceName : info.GetMediationAdapterClassName();
        }

        private static string Describe(AdError error) => error == null ? "unknown" : error.GetCode() + ": " + error.GetMessage();

        private static void Replace<T>(Dictionary<string, T> map, string id, T ad, Action<T> destroy) where T : class
        {
            if (map.TryGetValue(id, out var old) && old != ad) destroy(old);
            map[id] = ad;
        }

        private static void Remove<T>(Dictionary<string, T> map, string id, Action<T> destroy) where T : class
        {
            if (!map.TryGetValue(id, out var ad)) return;
            map.Remove(id);
            destroy(ad);
        }
    }
}
