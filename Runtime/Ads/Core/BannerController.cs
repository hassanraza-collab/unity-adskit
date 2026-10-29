using System;
using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// Keeps the single banner in the right state: loaded with the unit for the current banner type,
    /// visible only when allowed, refresh paused during full-screen ads, height reported for UI layout.
    /// </summary>
    internal sealed class BannerController
    {
        private const float LoadTimeoutSec = 60f;

        private readonly AdsService _service;
        private string _loadedUnitId;
        private bool _loading;
        private bool _visible;
        private int _attempts;
        private double _nextRetryAt;
        private int _requestId;
        private float _lastHeight = -1f;
        // Read once per load: the provider height is a native call (AdMob's blocks on the Android UI thread).
        private float _loadedHeight;

        public BannerController(AdsService service)
        {
            _service = service;
            Suppressed = service.BannerSuppressedEarly;
        }

        /// <summary>Game-requested hide (e.g. a full-screen menu). The banner stays loaded.</summary>
        public bool Suppressed { get; set; }

        public float HeightPixels => _visible ? _loadedHeight : 0f;

        public string DebugState =>
            (_visible ? "visible" : "hidden") + (_loading ? " loading" : "") +
            (_loadedUnitId != null ? " loaded" : "") + (_attempts > 0 ? " fails " + _attempts : "");

        public void Tick(bool online)
        {
            var p = _service.Config.Banner;
            var context = _service.Context;
            var want = p != null && !Suppressed && _service.CanShowBanner(p)
                       && context != AdsIds.ContextSplash && !string.IsNullOrEmpty(context);
            var unitId = p != null ? _service.Config.GetUnitId(_service.MediationId, p.ActiveUnitKey) : null;

            if (!want || string.IsNullOrEmpty(unitId))
            {
                SetVisible(false);
                return;
            }

            if (_loadedUnitId != unitId)
            {
                if (_loading) return;
                if (_loadedUnitId != null)
                {
                    // Banner type or unit changed remotely: replace it.
                    SetVisible(false);
                    _service.Provider.DestroyBanner();
                    _loadedUnitId = null;
                }
                if (!online || KitRunner.Now < _nextRetryAt) return;
                Load(unitId, p);
                return;
            }

            SetVisible(true);
        }

        public void OnFullscreen(bool open)
        {
            var p = _service.Config.Banner;
            if (p != null && p.PauseRefreshDuringFullscreen && _loadedUnitId != null)
                _service.Provider.SetBannerAutoRefresh(!open);
        }

        private void Load(string unitId, PlacementRuntime p)
        {
            _loading = true;
            var requestId = ++_requestId;
            KitLog.Info("Loading banner (" + p.BannerType + ")");
            KitRunner.After(LoadTimeoutSec, () => OnLoaded(requestId, unitId, AdLoadResult.Fail("timeout")));
            _service.Provider.LoadBanner(unitId, new BannerRequest { Type = p.BannerType, Position = p.Position },
                r => KitRunner.Dispatch(() => OnLoaded(requestId, unitId, r)));
        }

        private void OnLoaded(int requestId, string unitId, AdLoadResult result)
        {
            if (requestId != _requestId || !_loading) return;
            _loading = false;
            if (result.Success)
            {
                _loadedUnitId = unitId;
                _loadedHeight = _service.Provider != null ? _service.Provider.BannerHeightPixels : 0f;
                _attempts = 0;
                KitLog.Info("Banner loaded" + (string.IsNullOrEmpty(result.Network) ? "" : " (" + result.Network + ")"));
                return;
            }
            _attempts++;
            var delay = Math.Min(Math.Pow(2, Math.Min(6, _attempts)), _service.Config.Loading.retryMaxBackoffSec);
            _nextRetryAt = KitRunner.Now + delay;
            KitLog.Warn("Banner load failed: " + result.Error + " (retry in " + delay + "s)");
        }

        private void SetVisible(bool visible)
        {
            if (visible && _loadedUnitId == null) return;
            if (_visible == visible) return;
            _visible = visible;
            _service.Provider.SetBannerVisible(visible);
            ReportHeight();
        }

        private void ReportHeight()
        {
            var h = HeightPixels;
            if (Mathf.Abs(h - _lastHeight) < 0.5f) return;
            _lastHeight = h;
            _service.RaiseBannerHeight(h);
        }
    }
}
