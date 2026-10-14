using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NineDTech.AdsKit.Analytics;
using NineDTech.AdsKit.Remote;
using UnityEngine;
using KitAnalytics = NineDTech.AdsKit.Analytics.Analytics;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// The ads engine: Remote Config, A/B mediation assignment, consent, SDK init, the predictive load
    /// scheduler, frequency gates, full-screen show flow, banner and revenue logging.
    /// Use it through the static <see cref="Ads"/> facade.
    /// </summary>
    public sealed class AdsService
    {
        public enum InitState
        {
            NotStarted,
            FetchingRemote,
            WaitingForAssignment,
            GatheringConsent,
            InitializingSdk,
            Ready,
            Failed
        }

        private enum ShowKind
        {
            Interstitial,
            Rewarded,
            Splash,
            Resume
        }

        private struct Waiter
        {
            public string UnitKey;
            public double Deadline;
            public Action<bool> Callback;
        }

        private const string MediationLockKey = "adskit.mediation";
        private const string RevenueTotalKey = "adskit.ad_revenue_usd";
        private const string RevenueTierKey = "adskit.ad_revenue_tier";
        private const float PumpInterval = 0.25f;
        private const float LoadTimeoutSec = 60f;
        private const float OpenTimeoutSec = 6f;
        private const float InitTimeoutSec = 30f;
        private const double NegativeInfinity = -1e9;

        private readonly AdsSettings _settings;
        private readonly IAdsGameHooks _hooks;
        private readonly Dictionary<string, AdSlot> _slots = new Dictionary<string, AdSlot>();
        private readonly List<string> _desired = new List<string>();
        private readonly HashSet<string> _desiredSet = new HashSet<string>();
        private readonly List<string> _eligibleInters = new List<string>();
        private readonly Dictionary<string, double> _placementClose = new Dictionary<string, double>();
        private readonly List<Waiter> _waiters = new List<Waiter>();

        private AdsRuntimeConfig _config;
        private IMediationProvider _provider;
        private BannerController _banner;
        private FullscreenSession _active;

        private string _context = AdsIds.ContextSplash;
        private bool _splashDone;
        private int _loadingCount;
        private double _nextLoadAt;
        private float _pumpTimer;
        private bool _pumpNow;
        private bool _wasOnline = true;
        private bool _lowMemory;
        private bool _lowRamDevice;

        private double _lastInterstitialClose = NegativeInfinity;
        private double _lastRewardedClose = NegativeInfinity;
        private double _lastSplashClose = NegativeInfinity;
        private double _lastFullscreenClose = NegativeInfinity;
        private int _interstitialsThisSession;

        private int _initAttempt;
        private double _revenueTotal;
        private int _revenueTier;

        public AdsService(AdsSettings settings, IAdsGameHooks hooks)
        {
            _settings = settings;
            _hooks = hooks;
        }

        public InitState State { get; private set; }
        public string MediationId { get; private set; }
        public IMediationProvider Provider => _provider;
        public AdsRuntimeConfig Config => _config;
        public string Context => _context;
        public float BannerHeightPixels => _banner != null ? _banner.HeightPixels : 0f;

        /// <summary>Banner height in screen pixels changed (0 when hidden). Shift UI with it.</summary>
        public event Action<float> BannerHeightChanged;

        /// <summary>Fires once when the SDK finished initializing.</summary>
        public event Action Ready;

        private int PlayerLevel => _hooks != null ? _hooks.CurrentLevel : 0;
        private bool AdsRemoved => _hooks != null && _hooks.AdsRemoved;
        private static bool Online => Application.internetReachability != NetworkReachability.NotReachable;

        // ------------------------------------------------------------------ init

        public void Initialize()
        {
            if (State != InitState.NotStarted) return;
            KitLog.Verbose = _settings.verboseLogs;
            KitRunner.Ensure();

            _config = AdsRuntimeConfig.FromSettings(_settings);
            if (_settings.simulator != null) SimulatorMediationProvider.Settings = _settings.simulator;
            SessionTracker.Begin(_config.Session.sessionTimeoutMinutes);
            SessionTracker.SessionStarted += OnSessionStarted;
            SessionTracker.Resumed += OnResumed;
            KitRunner.Tick += OnTick;
            KitRunner.LowMemory += OnLowMemory;

            _revenueTotal = double.TryParse(PlayerPrefs.GetString(RevenueTotalKey, "0"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var total) ? total : 0;
            _revenueTier = PlayerPrefs.GetInt(RevenueTierKey, 0);

            KitAnalytics.AddCommonParams(AddMediationParam);

#if UNITY_EDITOR
            if (_settings.editorRemoteJson != null && RemoteConfigService.State == RemoteFetchState.NotStarted)
            {
                // An Editor test payload wins over any backend (Firebase) for this play session.
                var local = new LocalRemoteConfigSource();
                local.Set(_settings.remoteConfigKey, _settings.editorRemoteJson.text);
                RemoteConfigService.OverrideSource(local);
                KitLog.Info("Editor: using " + _settings.editorRemoteJson.name + " as remote " + _settings.remoteConfigKey);
            }
#endif

            if (_settings.showDebugHud && (Application.isEditor || Debug.isDebugBuild))
                AdsDebugHud.Create(this);

            State = InitState.FetchingRemote;
            RemoteConfigService.Activated += OnRemoteActivated;
            RemoteConfigService.Fetch(_settings.remoteFetchTimeoutSec, _ => ContinueInit());
        }

        private void OnRemoteActivated()
        {
            ApplyRemote();
            if (State == InitState.WaitingForAssignment) ContinueInit();
        }

        private void ApplyRemote()
        {
            var config = AdsRuntimeConfig.FromSettings(_settings);
            if (RemoteConfigService.TryGetRemoteString(_settings.remoteConfigKey, out var json))
            {
                if (AdsRemoteConfigApplier.Apply(config, json, out var error))
                    KitLog.Info("ads_config applied (schema " + config.SchemaVersion + ")");
                else
                    KitLog.Error("ads_config is not valid JSON, keeping defaults: " + error);
            }
            _config = config;
            SessionTracker.TimeoutMinutes = config.Session.sessionTimeoutMinutes;
            var ram = SystemInfo.systemMemorySize;
            _lowRamDevice = ram > 0 && ram < config.Loading.lowRamThresholdMb;
            if (config.Banner != null)
                KitAnalytics.SetUserProperty("banner_type", config.Banner.BannerType == BannerType.Standard ? "standard" : "adaptive");
            _pumpNow = true;
        }

        private void ContinueInit()
        {
            if (State != InitState.FetchingRemote && State != InitState.WaitingForAssignment) return;
            ApplyRemote();

            var id = ResolveMediation();
            if (id == null)
            {
                State = InitState.WaitingForAssignment;
                KitLog.Info("Waiting for the mediation A/B assignment from Remote Config");
                return;
            }

            MediationId = id;
            KitAnalytics.SetUserProperty("ad_mediation", id);
            _provider = MediationProviderRegistry.Create(id);
            if (_provider == null)
            {
                State = InitState.Failed;
                KitLog.Error("Mediation provider '" + id + "' is not installed. Installed: " + string.Join(", ", MediationProviderRegistry.Ids));
                return;
            }
            _provider.RevenuePaid += e => KitRunner.Dispatch(() => OnRevenuePaid(e));

            var debug = Debug.isDebugBuild;
            AdsConsent.DebugForceEea = debug && _settings.consentDebugForceEea;
            AdsConsent.DebugTestDeviceHashedIds.Clear();
            if (debug) AdsConsent.DebugTestDeviceHashedIds.AddRange(_settings.consentTestDeviceHashedIds);
            GatherConsent();
        }

        private void GatherConsent()
        {
            State = InitState.GatheringConsent;
            var consent = AdsConsent.Provider;
            KitLog.Info("Gathering consent (" + consent.Name + ")");
            consent.Gather(status => KitRunner.Dispatch(() =>
            {
                AdsConsent.Status = status;
                var name = status.ToString().ToLowerInvariant();
                KitAnalytics.SetUserProperty("consent", name);
                KitAnalytics.Event(KitEvents.ConsentResult).Add("status", name).Log();
                if (!consent.CanRequestAds)
                {
                    // Google: never request ads before consent info is known. Try again later (usually offline).
                    KitLog.Warn("Consent not resolved yet; ads paused, retrying in 60s");
                    KitRunner.After(60f, GatherConsent);
                    return;
                }
                InitProvider();
            }));
        }

        /// <summary>Show the GDPR privacy options form (add a button in settings when <see cref="PrivacyOptionsRequired"/>).</summary>
        public bool PrivacyOptionsRequired => AdsConsent.Provider.PrivacyOptionsRequired;

        private string ResolveMediation()
        {
            if (Application.isEditor && _settings.useSimulatorInEditor) return AdsIds.Simulator;
            if (!_settings.abTestMediation) return Installed(_settings.defaultMediation);

            var locked = PlayerPrefs.GetString(MediationLockKey, "");
            if (!string.IsNullOrEmpty(locked)) return Installed(locked);

            if (!RemoteConfigService.TryGetRemoteString(_settings.mediationRemoteKey, out var value)) return null;
            value = value.Trim().ToLowerInvariant();
            PlayerPrefs.SetString(MediationLockKey, value);
            PlayerPrefs.Save();
            KitLog.Info("Mediation A/B group locked: " + value);
            return Installed(value);
        }

        private string Installed(string id)
        {
            if (MediationProviderRegistry.IsRegistered(id)) return id;
            KitLog.Error("Assigned mediation '" + id + "' is not installed in this build");
            if (MediationProviderRegistry.IsRegistered(_settings.defaultMediation)) return _settings.defaultMediation;
            if (Debug.isDebugBuild && MediationProviderRegistry.IsRegistered(AdsIds.Simulator)) return AdsIds.Simulator;
            return id;
        }

        private void InitProvider()
        {
            State = InitState.InitializingSdk;
            var attempt = ++_initAttempt;
            var started = KitRunner.Now;
            var testMode = Debug.isDebugBuild && _settings.testModeInDevelopmentBuilds;
            var args = new MediationInitArgs
            {
                AdUnitIds = _config.GetAllUnitIds(MediationId),
                TestMode = testMode,
                TestDeviceIds = !testMode ? new List<string>()
                    : MediationId == AdsIds.Max ? _settings.maxTestDeviceAdvertisingIds : _settings.admobTestDeviceIds,
                VerboseLogs = _settings.verboseLogs,
                Consent = AdsConsent.Status
            };
            KitLog.Info("Initializing " + MediationId + " with " + args.AdUnitIds.Count + " ad units");

            void Fail(string why)
            {
                if (State != InitState.InitializingSdk || attempt != _initAttempt) return;
                _initAttempt++;
                KitLog.Error(MediationId + " " + why + "; retrying in 30s");
                KitRunner.After(30f, InitProvider);
            }

            // Some SDKs never call back when misconfigured (e.g. MAX without an SDK key).
            KitRunner.After(InitTimeoutSec, () => Fail("did not finish initializing within " + InitTimeoutSec + "s"));
            _provider.Initialize(args, ok => KitRunner.Dispatch(() =>
            {
                if (State != InitState.InitializingSdk || attempt != _initAttempt) return;
                if (!ok)
                {
                    Fail("failed to initialize");
                    return;
                }
                State = InitState.Ready;
                _banner = new BannerController(this);
                _pumpNow = true;
                KitLog.Info(MediationId + " ready in " + Mathf.RoundToInt((float)((KitRunner.Now - started) * 1000)) + " ms");
                Ready?.Invoke();
            }));
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Tell AdsKit where the player is (splash / home / gameplay / custom); drives which ads stay cached.</summary>
        public void SetContext(string context)
        {
            if (string.IsNullOrEmpty(context) || context == _context) return;
            _context = context;
            if (context != AdsIds.ContextSplash) _splashDone = true;
            foreach (var slot in _slots.Values)
                if (slot.Exhausted) ResetRetry(slot);
            _pumpNow = true;
            KitLog.Info("Context: " + context);
        }

        public void ShowInterstitial(string placementId, Action<AdShowResult> onDone)
        {
            PlacementRuntime p = null;
            _config?.Interstitials.TryGetValue(placementId ?? "", out p);
            var reason = CheckGates(p, false);
            if (reason == AdBlockReason.None) reason = CheckInterstitialPacing(p);
            if (reason == AdBlockReason.None) reason = CheckReady(p.UnitKey);
            LogOpportunity(placementId, AdFormat.Interstitial, reason);
            if (reason != AdBlockReason.None)
            {
                Complete(onDone, AdShowResult.Blocked(reason));
                return;
            }
            BeginFullscreen(GetSlot(p.UnitKey), p.Id, ShowKind.Interstitial, null, onDone);
        }

        public void ShowRewarded(string placementId, Action onReward, Action<AdShowResult> onDone)
        {
            PlacementRuntime p = null;
            _config?.Rewarded.TryGetValue(placementId ?? "", out p);
            AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.RewardedTap, placementId, AdFormat.Rewarded).Log();

            var reason = CheckGates(p, true);
            if (reason == AdBlockReason.None) reason = CheckReady(p.UnitKey);
            LogOpportunity(placementId, AdFormat.Rewarded, reason);

            if (reason == AdBlockReason.None)
            {
                BeginFullscreen(GetSlot(p.UnitKey), p.Id, ShowKind.Rewarded, onReward, onDone);
                return;
            }

            if (reason == AdBlockReason.NotReady && _config.RewardedLoadWaitSec > 0f)
            {
                // Not cached: load with priority behind a short spinner.
                var waitStarted = KitRunner.Now;
                _hooks?.SetAdLoadingIndicator(true);
                ForceLoad(p.UnitKey);
                AddWaiter(p.UnitKey, _config.RewardedLoadWaitSec, ready =>
                {
                    _hooks?.SetAdLoadingIndicator(false);
                    if (ready && _active == null)
                    {
                        BeginFullscreen(GetSlot(p.UnitKey), p.Id, ShowKind.Rewarded, onReward, onDone);
                        return;
                    }
                    RewardedUnavailable(p.Id, KitRunner.Now - waitStarted);
                    Complete(onDone, AdShowResult.Blocked(_active != null ? AdBlockReason.Busy : AdBlockReason.NotReady));
                });
                return;
            }

            RewardedUnavailable(placementId, 0);
            Complete(onDone, AdShowResult.Blocked(reason));
        }

        public bool IsRewardedReady(string placementId)
        {
            PlacementRuntime p = null;
            _config?.Rewarded.TryGetValue(placementId ?? "", out p);
            return CheckGates(p, true) == AdBlockReason.None && CheckReady(p.UnitKey) == AdBlockReason.None;
        }

        /// <summary>Log that a rewarded offer (button) became visible; feeds the rewarded funnel.</summary>
        public void NotifyRewardedOffer(string placementId)
        {
            AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.RewardedOffer, placementId, AdFormat.Rewarded)
                .Add("ready", IsRewardedReady(placementId))
                .Log();
        }

        /// <summary>
        /// Show the cold-start ad. Call when the loading bar reaches <see cref="PlacementRuntime.TriggerProgress"/>;
        /// waits up to max_wait_sec for the ad to finish loading. Always completes.
        /// </summary>
        public void ShowSplashAd(Action<AdShowResult> onDone)
        {
            var p = _config?.Splash;
            if (p == null || _splashDone)
            {
                _splashDone = true;
                Complete(onDone, AdShowResult.Blocked(p == null ? AdBlockReason.PlacementUnknown : AdBlockReason.Busy));
                return;
            }

            var reason = State == InitState.Failed ? AdBlockReason.NotInitialized : CheckRules(p, false);
            if (reason != AdBlockReason.None)
            {
                _splashDone = true;
                LogOpportunity(p.Id, p.ActiveFormat, reason);
                Complete(onDone, AdShowResult.Blocked(reason));
                return;
            }

            var unitKey = p.ActiveUnitKey;
            AddWaiter(unitKey, p.MaxWaitSec, ready =>
            {
                _splashDone = true;
                if (ready && _active == null && State == InitState.Ready)
                {
                    LogOpportunity(p.Id, p.ActiveFormat, AdBlockReason.None);
                    BeginFullscreen(GetSlot(unitKey), p.Id, ShowKind.Splash, null, onDone);
                    return;
                }
                var why = State != InitState.Ready ? AdBlockReason.NotInitialized : AdBlockReason.NotReady;
                LogOpportunity(p.Id, p.ActiveFormat, why);
                Complete(onDone, AdShowResult.Blocked(why));
            });
        }

        public void SetBannerSuppressed(bool suppressed)
        {
            if (_banner != null) _banner.Suppressed = suppressed;
            else _bannerSuppressedEarly = suppressed;
        }

        private bool _bannerSuppressedEarly;
        internal bool BannerSuppressedEarly => _bannerSuppressedEarly;

        public void OpenDebugger() => _provider?.OpenDebugger();

        // ------------------------------------------------------------------ gates

        /// <summary>Placement switches and audience gates (no pacing, no readiness).</summary>
        private AdBlockReason CheckGates(PlacementRuntime p, bool isRewarded)
        {
            if (p == null) return _config == null ? AdBlockReason.NotInitialized : AdBlockReason.PlacementUnknown;
            if (State != InitState.Ready) return AdBlockReason.NotInitialized;
            return CheckRules(p, isRewarded);
        }

        private AdBlockReason CheckRules(PlacementRuntime p, bool isRewarded)
        {
            if (!_config.AdsEnabled) return AdBlockReason.AdsDisabled;
            if (!p.Enabled) return AdBlockReason.PlacementDisabled;
            if (!isRewarded && AdsRemoved) return AdBlockReason.AdsRemoved;
            if (SessionTracker.SessionCount < p.MinSessions) return AdBlockReason.SessionGate;
            if (PlayerLevel < p.MinLevel) return AdBlockReason.LevelGate;
            return AdBlockReason.None;
        }

        private AdBlockReason CheckInterstitialPacing(PlacementRuntime p)
        {
            var f = _config.Frequency;
            var now = KitRunner.Now;
            if (SessionTracker.SecondsInSession < f.firstInterstitialMinSessionSec) return AdBlockReason.SessionStartGrace;
            if (f.maxInterstitialsPerSession > 0 && _interstitialsThisSession >= f.maxInterstitialsPerSession) return AdBlockReason.SessionCap;
            if (now - _lastSplashClose < f.cooldownAfterSplashAdSec) return AdBlockReason.AfterSplashCooldown;
            if (now - _lastRewardedClose < f.cooldownAfterRewardedSec) return AdBlockReason.AfterRewardedCooldown;
            if (now - _lastInterstitialClose < f.globalInterstitialCooldownSec) return AdBlockReason.GlobalCooldown;
            if (_placementClose.TryGetValue(p.Id, out var last) && now - last < p.CooldownSec) return AdBlockReason.PlacementCooldown;
            return AdBlockReason.None;
        }

        private AdBlockReason CheckReady(string unitKey)
        {
            if (_active != null) return AdBlockReason.Busy;
            if (IsSlotReady(GetSlot(unitKey))) return AdBlockReason.None;
            return Online ? AdBlockReason.NotReady : AdBlockReason.Offline;
        }

        private bool IsSlotReady(AdSlot slot)
        {
            if (slot == null || slot.State != AdSlotState.Ready) return false;
            if (_provider != null && _provider.IsReady(slot.Format, slot.UnitId)) return true;
            slot.State = AdSlotState.Idle; // the SDK dropped it (expired / consumed)
            return false;
        }

        /// <summary>Would this placement be allowed to show this session (ignoring timers and readiness)?</summary>
        private bool WorthCaching(PlacementRuntime p, bool isRewarded)
        {
            if (p == null || string.IsNullOrEmpty(p.ActiveUnitKey)) return false;
            if (CheckRules(p, isRewarded) != AdBlockReason.None) return false;
            if (!isRewarded && p.Format == AdFormat.Interstitial)
            {
                var cap = _config.Frequency.maxInterstitialsPerSession;
                if (cap > 0 && _interstitialsThisSession >= cap) return false;
            }
            return !string.IsNullOrEmpty(_config.GetUnitId(MediationId, p.ActiveUnitKey));
        }

        internal bool CanShowBanner(PlacementRuntime p) =>
            p != null && State == InitState.Ready && CheckRules(p, false) == AdBlockReason.None;

        // ------------------------------------------------------------------ scheduler

        private void OnTick(float dt)
        {
            if (State == InitState.Ready)
            {
                var online = Online;
                if (online && !_wasOnline)
                {
                    foreach (var slot in _slots.Values) ResetRetry(slot);
                    _pumpNow = true;
                    KitLog.Info("Network back; retrying loads");
                }
                _wasOnline = online;

                _pumpTimer -= dt;
                if (_pumpNow || _pumpTimer <= 0f)
                {
                    _pumpNow = false;
                    _pumpTimer = PumpInterval;
                    if (_active == null) Pump(online);
                }
                _banner?.Tick(online);
            }
            ProcessWaiters();
        }

        private void Pump(bool online)
        {
            BuildDesired();
            RefreshExpired();
            if (!online) return;
            if (_loadingCount >= Mathf.Max(1, _config.Loading.maxConcurrentLoads)) return;
            if (KitRunner.Now < _nextLoadAt) return;

            foreach (var key in _desired)
            {
                var slot = GetOrCreateSlot(key);
                if (slot == null || slot.State != AdSlotState.Idle || slot.Exhausted || KitRunner.Now < slot.NextRetryAt) continue;
                if (slot.Format == AdFormat.Interstitial && !MakeRoomForInterstitial()) continue;
                StartLoad(slot);
                return;
            }
        }

        /// <summary>Ordered unit keys to keep loaded right now: most likely next first.</summary>
        private void BuildDesired()
        {
            _desired.Clear();
            _desiredSet.Clear();

            if (_context == AdsIds.ContextSplash)
            {
                // Cold start: only the splash ad, nothing else competes with it.
                if (!_splashDone && WorthCaching(_config.Splash, false)) AddDesired(_config.Splash.ActiveUnitKey);
                return;
            }

            _eligibleInters.Clear();
            var cap = EffectiveInterstitialCap;
            if (_config.CachePriority.TryGetValue(_context, out var priorities))
                foreach (var id in priorities)
                {
                    if (_eligibleInters.Count >= cap) break;
                    if (_config.Interstitials.TryGetValue(id, out var p) && WorthCaching(p, false) && !_eligibleInters.Contains(p.UnitKey))
                        _eligibleInters.Add(p.UnitKey);
                }

            // Most likely interstitial first, then rewarded (needed any time), then the rest.
            if (_eligibleInters.Count > 0) AddDesired(_eligibleInters[0]);
            foreach (var r in _config.Rewarded.Values)
                if (WorthCaching(r, true)) AddDesired(r.UnitKey);
            for (var i = 1; i < _eligibleInters.Count; i++) AddDesired(_eligibleInters[i]);
            if (WorthCaching(_config.AppOpenResume, false)) AddDesired(_config.AppOpenResume.UnitKey);
        }

        private void AddDesired(string key)
        {
            if (string.IsNullOrEmpty(key) || !_desiredSet.Add(key)) return;
            _desired.Add(key);
        }

        private int EffectiveInterstitialCap
        {
            get
            {
                var l = _config.Loading;
                return Mathf.Max(1, _lowMemory || _lowRamDevice ? l.lowRamMaxCachedInterstitials : l.maxCachedInterstitials);
            }
        }

        /// <summary>
        /// Interstitials in memory may be the current context's cap plus one left over from the previous context
        /// (so walking home -> gameplay -> home does not throw ads away). Beyond that, drop an unneeded one.
        /// </summary>
        private bool MakeRoomForInterstitial()
        {
            var hardCap = EffectiveInterstitialCap + (_lowMemory ? 0 : 1);
            var inMemory = 0;
            AdSlot evictable = null;
            foreach (var s in _slots.Values)
            {
                if (s.Format != AdFormat.Interstitial) continue;
                if (s.State == AdSlotState.Ready || s.State == AdSlotState.Loading)
                {
                    inMemory++;
                    if (s.State == AdSlotState.Ready && !_desiredSet.Contains(s.Key) && (evictable == null || s.LoadedAt < evictable.LoadedAt))
                        evictable = s;
                }
            }
            if (inMemory < hardCap) return true;
            if (evictable == null) return false;
            KitLog.Info("Cache full; dropping unneeded " + evictable.Key);
            Discard(evictable);
            return true;
        }

        private void RefreshExpired()
        {
            if (_provider == null || _provider.ManagesExpiry) return;
            var now = KitRunner.Now;
            foreach (var slot in _slots.Values)
            {
                if (slot.State != AdSlotState.Ready) continue;
                var maxMinutes = slot.Format == AdFormat.AppOpen
                    ? _config.Loading.admobAppOpenRefreshMin
                    : _config.Loading.admobInterRewardedRefreshMin;
                if (now - slot.LoadedAt < maxMinutes * 60.0) continue;
                // Desired slots reload on the next pump; others stay idle (refresh_only_when_needed).
                if (_config.Loading.refreshOnlyWhenNeeded && !_desiredSet.Contains(slot.Key))
                    KitLog.Info(slot.Key + " expired; not needed now, not reloading");
                else
                    KitLog.Info(slot.Key + " about to expire; reloading");
                Discard(slot);
            }
        }

        private AdSlot GetSlot(string key) => key != null && _slots.TryGetValue(key, out var slot) ? slot : null;

        private AdSlot GetOrCreateSlot(string key)
        {
            var unitId = _config.GetUnitId(MediationId, key);
            if (string.IsNullOrEmpty(unitId)) return null;
            if (!_slots.TryGetValue(key, out var slot))
            {
                if (!_config.UnitFormats.TryGetValue(key, out var format)) return null;
                slot = new AdSlot { Key = key, Format = format, UnitId = unitId };
                _slots[key] = slot;
            }
            else if (slot.UnitId != unitId && slot.State != AdSlotState.Showing)
            {
                KitLog.Info(key + " ad unit id changed remotely");
                Discard(slot);
                slot.UnitId = unitId;
                ResetRetry(slot);
            }
            return slot;
        }

        private void StartLoad(AdSlot slot)
        {
            slot.State = AdSlotState.Loading;
            var requestId = ++slot.RequestId;
            var unitId = slot.UnitId;
            var started = KitRunner.Now;
            _loadingCount++;
            KitLog.Info("Loading " + slot.Key + " (" + slot.Format + ")" + (slot.Attempts > 0 ? " retry " + slot.Attempts : ""));
            KitRunner.After(LoadTimeoutSec, () => OnLoaded(slot, requestId, AdLoadResult.Fail("timeout"), started));
            _provider.Load(slot.Format, unitId, r => KitRunner.Dispatch(() => OnLoaded(slot, requestId, r, started)));
        }

        private void OnLoaded(AdSlot slot, int requestId, AdLoadResult result, double started)
        {
            if (slot.RequestId != requestId || slot.State != AdSlotState.Loading) return;
            _loadingCount = Mathf.Max(0, _loadingCount - 1);
            var now = KitRunner.Now;
            _nextLoadAt = now + _config.Loading.loadStaggerSec;
            _pumpNow = true;
            var ms = (long)((now - started) * 1000);

            if (result.Success)
            {
                slot.State = AdSlotState.Ready;
                slot.LoadedAt = now;
                slot.Network = result.Network;
                ResetRetry(slot);
                KitLog.Info("Loaded " + slot.Key + " in " + ms + " ms" + (string.IsNullOrEmpty(result.Network) ? "" : " (" + result.Network + ")"));
                return;
            }

            slot.State = AdSlotState.Idle;
            slot.Attempts++;
            var delay = Math.Min(Math.Pow(2, Math.Min(6, slot.Attempts)), _config.Loading.retryMaxBackoffSec);
            slot.NextRetryAt = now + delay;
            slot.Exhausted = slot.Attempts >= _config.Loading.retryMaxAttempts;
            KitLog.Warn("Load failed " + slot.Key + ": " + result.Error + (slot.Exhausted ? " (pausing until context/network change)" : " (retry in " + delay + "s)"));
            if (_config.Loading.logLoadFailures)
                AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.LoadFail, slot.Key, slot.Format)
                    .Add("error", result.Error ?? "")
                    .Add(KitEvents.ParamAttempt, slot.Attempts)
                    .Add("load_ms", ms)
                    .Log();
        }

        private void ForceLoad(string key)
        {
            var slot = GetOrCreateSlot(key);
            if (slot == null || slot.State != AdSlotState.Idle || !Online) return;
            ResetRetry(slot);
            StartLoad(slot);
        }

        private void Discard(AdSlot slot)
        {
            if (slot.State == AdSlotState.Ready) _provider?.Discard(slot.Format, slot.UnitId);
            if (slot.State == AdSlotState.Loading) _loadingCount = Mathf.Max(0, _loadingCount - 1);
            slot.RequestId++;
            slot.State = AdSlotState.Idle;
            slot.LoadedAt = 0;
        }

        private static void ResetRetry(AdSlot slot)
        {
            slot.Attempts = 0;
            slot.NextRetryAt = 0;
            slot.Exhausted = false;
        }

        private void AddWaiter(string unitKey, float seconds, Action<bool> callback)
        {
            _waiters.Add(new Waiter { UnitKey = unitKey, Deadline = KitRunner.Now + seconds, Callback = callback });
        }

        private void ProcessWaiters()
        {
            if (_waiters.Count == 0) return;
            var now = KitRunner.Now;
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                var w = _waiters[i];
                var ready = State == InitState.Ready && _active == null && IsSlotReady(GetSlot(w.UnitKey));
                if (!ready && now < w.Deadline) continue;
                _waiters.RemoveAt(i);
                try { w.Callback(ready); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        // ------------------------------------------------------------------ events

        private void OnSessionStarted(int count)
        {
            _interstitialsThisSession = 0;
            _pumpNow = true;
        }

        private void OnLowMemory()
        {
            if (_lowMemory) return;
            _lowMemory = true;
            KitLog.Warn("Low memory: shrinking the ad cache");
            BuildDesired();
            foreach (var slot in _slots.Values)
                if (slot.State == AdSlotState.Ready && slot.Format == AdFormat.Interstitial && !_desiredSet.Contains(slot.Key))
                    Discard(slot);
        }

        private void OnResumed(double awaySeconds)
        {
            if (State != InitState.Ready || _active != null || _context == AdsIds.ContextSplash) return;
            if (KitRunner.Now - _lastFullscreenClose < 3.0) return; // the pause was our own ad
            var p = _config.AppOpenResume;
            if (p == null || !p.Enabled || awaySeconds < p.MinBackgroundSec) return;

            var reason = CheckGates(p, false);
            if (reason == AdBlockReason.None && KitRunner.Now - _lastInterstitialClose < _config.Frequency.globalInterstitialCooldownSec)
                reason = AdBlockReason.GlobalCooldown;
            if (reason == AdBlockReason.None) reason = CheckReady(p.UnitKey);
            LogOpportunity(p.Id, AdFormat.AppOpen, reason);
            if (reason == AdBlockReason.None) BeginFullscreen(GetSlot(p.UnitKey), p.Id, ShowKind.Resume, null, null);
        }

        private void OnRevenuePaid(AdRevenueEvent e)
        {
            var unitKey = e.AdUnitId;
            foreach (var slot in _slots.Values)
                if (slot.UnitId == e.AdUnitId) { unitKey = slot.Key; break; }
            var placement = !string.IsNullOrEmpty(e.Placement) ? e.Placement
                : _active != null ? _active.PlacementId
                : e.Format == AdFormat.Banner ? AdsIds.PlacementBanner : "";
            var currency = string.IsNullOrEmpty(e.Currency) ? "USD" : e.Currency;
            var format = AdsAnalyticsEvents.FormatName(e.Format);
            var platform = AdsAnalyticsEvents.PlatformName(MediationId);

            if (_provider != null && !_provider.AutoLogsFirebaseImpressions)
                KitAnalytics.Event(AdsAnalyticsEvents.Impression)
                    .Add("ad_platform", platform)
                    .Add("ad_source", e.Network ?? "")
                    .Add("ad_format", format)
                    .Add("ad_unit_name", unitKey ?? "")
                    .Add("currency", currency)
                    .Add("value", e.Value)
                    .Log();

            KitAnalytics.Event(AdsAnalyticsEvents.KitImpression)
                .Add("ad_platform", platform)
                .Add("ad_source", e.Network ?? "")
                .Add("ad_format", format)
                .Add("ad_unit_name", unitKey ?? "")
                .Add(AdsAnalyticsEvents.ParamPlacement, placement)
                .Add("precision", e.Precision ?? "unknown")
                .Add("currency", currency)
                .Add("value", e.Value)
                .Log();

            if (currency != "USD" || e.Value <= 0) return;
            _revenueTotal += e.Value;
            PlayerPrefs.SetString(RevenueTotalKey, _revenueTotal.ToString("R", CultureInfo.InvariantCulture));
            var tiers = _config.RevenueTiersUsd;
            while (_revenueTier < tiers.Count && _revenueTotal >= tiers[_revenueTier])
            {
                _revenueTier++;
                PlayerPrefs.SetInt(RevenueTierKey, _revenueTier);
                KitAnalytics.Event(AdsAnalyticsEvents.RevenueTierPrefix + _revenueTier)
                    .Add("value", tiers[_revenueTier - 1])
                    .Add("currency", "USD")
                    .Log();
            }
        }

        private void AddMediationParam(AnalyticsEventBuilder b)
        {
            if (!string.IsNullOrEmpty(MediationId))
                b.AddIfMissing(AnalyticsParam.Of(AdsAnalyticsEvents.ParamMediation, MediationId));
        }

        internal void RaiseBannerHeight(float pixels) => BannerHeightChanged?.Invoke(pixels);

        // ------------------------------------------------------------------ full-screen show

        private void BeginFullscreen(AdSlot slot, string placementId, ShowKind kind, Action onReward, Action<AdShowResult> onDone)
        {
            var session = new FullscreenSession(this, slot, placementId, kind, onReward, onDone);
            _active = session;
            slot.State = AdSlotState.Showing;
            _hooks?.OnFullscreenAdOpened();
            _banner?.OnFullscreen(true);
            KitLog.Info("Showing " + slot.Key + " for " + placementId);
            KitRunner.After(OpenTimeoutSec, session.CheckOpenTimeout);
            _provider.Show(slot.Format, slot.UnitId, placementId, session);
        }

        private void OnFullscreenOpened(FullscreenSession s, string network)
        {
            AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.Show, s.PlacementId, s.Slot.Format)
                .Add("ad_source", network ?? s.Slot.Network ?? "")
                .Log();
        }

        private void OnFullscreenFinished(FullscreenSession s, bool shown, string error)
        {
            if (_active == s) _active = null;
            var now = KitRunner.Now;
            var slot = s.Slot;
            slot.State = AdSlotState.Idle;
            slot.LoadedAt = 0;
            ResetRetry(slot);
            _lastFullscreenClose = now;
            _pumpNow = true;

            try { _hooks?.OnFullscreenAdClosed(); }
            catch (Exception e) { Debug.LogException(e); }
            _banner?.OnFullscreen(false);

            if (!shown)
            {
                KitLog.Warn("Show failed " + slot.Key + ": " + error);
                AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.ShowFail, s.PlacementId, slot.Format).Add("error", error ?? "").Log();
                Complete(s.OnDone, AdShowResult.Failed(error));
                return;
            }

            switch (s.Kind)
            {
                case ShowKind.Interstitial:
                    _lastInterstitialClose = now;
                    _placementClose[s.PlacementId] = now;
                    _interstitialsThisSession++;
                    break;
                case ShowKind.Rewarded:
                    _lastRewardedClose = now;
                    break;
                default:
                    _lastSplashClose = now;
                    break;
            }

            AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.Close, s.PlacementId, slot.Format)
                .Add("view_sec", Math.Round(s.OpenedAt > 0 ? now - s.OpenedAt : 0, 1))
                .Log();

            if (s.Kind == ShowKind.Rewarded && s.RewardEarned)
            {
                AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.Rewarded, s.PlacementId, slot.Format).Log();
                try { s.OnReward?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            Complete(s.OnDone, AdShowResult.ShownResult);
        }

        private void RewardedUnavailable(string placementId, double waited)
        {
            AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.RewardedUnavailable, placementId, AdFormat.Rewarded)
                .Add("waited_sec", Math.Round(waited, 1))
                .Log();
            try { _hooks?.OnRewardedUnavailable(placementId); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void LogOpportunity(string placementId, AdFormat format, AdBlockReason reason)
        {
            AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.Opportunity, placementId, format)
                .Add("ready", reason == AdBlockReason.None)
                .Add("blocked", AdShowResult.ReasonName(reason))
                .Log();
        }

        private static void Complete(Action<AdShowResult> onDone, AdShowResult result)
        {
            if (onDone == null) return;
            try { onDone(result); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>One full-screen show. Thread-safe entry points; completes exactly once.</summary>
        private sealed class FullscreenSession : IFullscreenAdListener
        {
            private readonly AdsService _service;
            private bool _opened;
            private bool _finished;

            public readonly AdSlot Slot;
            public readonly string PlacementId;
            public readonly ShowKind Kind;
            public readonly Action OnReward;
            public readonly Action<AdShowResult> OnDone;
            public bool RewardEarned;
            public double OpenedAt;

            public FullscreenSession(AdsService service, AdSlot slot, string placementId, ShowKind kind, Action onReward, Action<AdShowResult> onDone)
            {
                _service = service;
                Slot = slot;
                PlacementId = placementId;
                Kind = kind;
                OnReward = onReward;
                OnDone = onDone;
            }

            public void OnOpened(string network) => KitRunner.Dispatch(() =>
            {
                if (_opened || _finished) return;
                _opened = true;
                OpenedAt = KitRunner.Now;
                _service.OnFullscreenOpened(this, network);
            });

            public void OnFailedToShow(string error) => KitRunner.Dispatch(() => Finish(false, error));

            public void OnClicked() => KitRunner.Dispatch(() =>
                AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.Clicked, PlacementId, Slot.Format).Log());

            public void OnRewardEarned() => KitRunner.Dispatch(() => RewardEarned = true);

            public void OnClosed() => KitRunner.Dispatch(() => Finish(true, null));

            public void CheckOpenTimeout()
            {
                if (!_opened && !_finished) Finish(false, "open_timeout");
            }

            private void Finish(bool shown, string error)
            {
                if (_finished) return;
                _finished = true;
                _service.OnFullscreenFinished(this, shown, error);
            }
        }

        // ------------------------------------------------------------------ debug

        internal void AppendDebugText(StringBuilder sb)
        {
            var now = KitRunner.Now;
            sb.Append("AdsKit ").Append(State).Append(" | ").Append(MediationId ?? "-")
              .Append(" | ctx ").Append(_context)
              .Append(" | sess ").Append(SessionTracker.SessionCount)
              .Append(" | lvl ").Append(PlayerLevel).Append('\n');
            if (_config == null) return;
            var f = _config.Frequency;
            sb.Append("inter cd ").Append(Remaining(_lastInterstitialClose, f.globalInterstitialCooldownSec, now))
              .Append("s  after-rw ").Append(Remaining(_lastRewardedClose, f.cooldownAfterRewardedSec, now))
              .Append("s  after-splash ").Append(Remaining(_lastSplashClose, f.cooldownAfterSplashAdSec, now))
              .Append("s  shown ").Append(_interstitialsThisSession).Append('/').Append(f.maxInterstitialsPerSession)
              .Append(_lowMemory || _lowRamDevice ? "  LOW-RAM" : "").Append('\n');
            sb.Append("desired: ").Append(string.Join(", ", _desired)).Append('\n');
            foreach (var s in _slots.Values)
            {
                sb.Append("  ").Append(s.Key).Append(": ").Append(s.State);
                if (s.State == AdSlotState.Ready) sb.Append(' ').Append((int)(now - s.LoadedAt)).Append("s old");
                if (s.Attempts > 0) sb.Append(" fails ").Append(s.Attempts);
                if (s.Exhausted) sb.Append(" EXHAUSTED");
                sb.Append('\n');
            }
            if (_banner != null) sb.Append("banner: ").Append(_banner.DebugState).Append('\n');
        }

        private static int Remaining(double last, float cooldown, double now) => Mathf.Max(0, (int)(cooldown - (now - last)));
    }
}
