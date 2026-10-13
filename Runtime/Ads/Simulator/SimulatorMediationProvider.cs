using System;
using System.Collections.Generic;
using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// Fake mediation for the Editor and SDK-less testing: random load times, configurable fill rate,
    /// on-screen full-screen ads and banner, and fake revenue events so analytics can be checked end to end.
    /// </summary>
    public sealed class SimulatorMediationProvider : IMediationProvider
    {
        private const string Network = "SimNetwork";
        private const float BannerRefreshSec = 30f;

        /// <summary>Set by <see cref="AdsService"/> from AdsSettings.simulator.</summary>
        public static SimulatorSettings Settings = new SimulatorSettings();

        private readonly HashSet<string> _ready = new HashSet<string>();
        private string _bannerUnitId;
        private BannerType _bannerType;
        private BannerPosition _bannerPosition;
        private bool _bannerVisible;
        private bool _bannerRefresh = true;
        private double _nextBannerImpressionAt;

        public string Id => AdsIds.Simulator;
        public bool ManagesExpiry => false;
        public bool AutoLogsFirebaseImpressions => false;

        public event Action<AdRevenueEvent> RevenuePaid;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            MediationProviderRegistry.Register(AdsIds.Simulator, () => new SimulatorMediationProvider());
        }

        public void Initialize(MediationInitArgs args, Action<bool> onComplete)
        {
            KitRunner.Tick += OnTick;
            KitRunner.After(0.3f, () => onComplete?.Invoke(true));
        }

        public void Load(AdFormat format, string adUnitId, Action<AdLoadResult> onComplete)
        {
            KitRunner.After(LoadDelay(), () =>
            {
                if (UnityEngine.Random.value <= Settings.fillRate)
                {
                    _ready.Add(Key(format, adUnitId));
                    onComplete?.Invoke(AdLoadResult.Ok(Network));
                }
                else
                {
                    onComplete?.Invoke(AdLoadResult.Fail("no_fill (simulated)"));
                }
            });
        }

        public bool IsReady(AdFormat format, string adUnitId) => _ready.Contains(Key(format, adUnitId));

        public void Show(AdFormat format, string adUnitId, string placement, IFullscreenAdListener listener)
        {
            if (!_ready.Remove(Key(format, adUnitId)))
            {
                listener.OnFailedToShow("not_ready");
                return;
            }
            SimulatorAdOverlay.Instance.ShowFullscreen(format, placement, Settings.closeDelaySec, rewarded =>
            {
                if (rewarded) listener.OnRewardEarned();
                listener.OnClosed();
            });
            listener.OnOpened(Network);
            Pay(format, adUnitId, placement);
        }

        public void Discard(AdFormat format, string adUnitId) => _ready.Remove(Key(format, adUnitId));

        public void LoadBanner(string adUnitId, BannerRequest request, Action<AdLoadResult> onComplete)
        {
            KitRunner.After(LoadDelay(), () =>
            {
                if (UnityEngine.Random.value > Settings.fillRate)
                {
                    onComplete?.Invoke(AdLoadResult.Fail("no_fill (simulated)"));
                    return;
                }
                _bannerUnitId = adUnitId;
                _bannerType = request.Type;
                _bannerPosition = request.Position;
                SyncBanner();
                onComplete?.Invoke(AdLoadResult.Ok(Network));
            });
        }

        public void SetBannerVisible(bool visible)
        {
            _bannerVisible = visible;
            if (visible) _nextBannerImpressionAt = KitRunner.Now;
            SyncBanner();
        }

        public void SetBannerAutoRefresh(bool enabled) => _bannerRefresh = enabled;

        public void DestroyBanner()
        {
            _bannerUnitId = null;
            _bannerVisible = false;
            SyncBanner();
        }

        public float BannerHeightPixels
        {
            get
            {
                if (_bannerUnitId == null) return 0f;
                var scale = Screen.dpi > 0 ? Screen.dpi / 160f : 1f;
                var widthDp = Screen.width / scale;
                var heightDp = _bannerType == BannerType.Standard ? 50f : widthDp >= 600f ? 90f : 62f;
                return heightDp * scale;
            }
        }

        public void OpenDebugger() => KitLog.Info("Simulator: no mediation debugger (this is the fake provider)");

        private void OnTick(float dt)
        {
            if (_bannerUnitId == null || !_bannerVisible || !_bannerRefresh) return;
            if (KitRunner.Now < _nextBannerImpressionAt) return;
            _nextBannerImpressionAt = KitRunner.Now + BannerRefreshSec;
            Pay(AdFormat.Banner, _bannerUnitId, AdsIds.PlacementBanner);
        }

        private void SyncBanner()
        {
            SimulatorAdOverlay.Instance.SetBanner(_bannerUnitId != null && _bannerVisible, BannerHeightPixels,
                _bannerPosition, _bannerType == BannerType.Standard ? "standard" : "adaptive");
        }

        private void Pay(AdFormat format, string adUnitId, string placement)
        {
            var ecpm = Settings.fakeEcpmUsd * (format == AdFormat.Banner ? 0.05f : format == AdFormat.Rewarded ? 1.5f : 1f);
            RevenuePaid?.Invoke(new AdRevenueEvent
            {
                Format = format,
                AdUnitId = adUnitId,
                Network = Network,
                Value = ecpm / 1000.0,
                Currency = "USD",
                Precision = "estimated",
                Placement = placement
            });
        }

        private static float LoadDelay() => UnityEngine.Random.Range(Settings.minLoadSec, Mathf.Max(Settings.minLoadSec, Settings.maxLoadSec));

        private static string Key(AdFormat format, string adUnitId) => (int)format + ":" + adUnitId;
    }
}
