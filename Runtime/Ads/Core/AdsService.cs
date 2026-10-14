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
            /// <summary>Resolve (not ready) as soon as the unit is neither loading nor ready, instead of at the deadline.</summary>
            public bool FailFast;
            public Action<bool> Callback;
        }

        private enum MrecState
        {
            Idle,
            Loading,
            Ready
        }

        private const string MediationLockKey = "adskit.mediation";
        private const string RevenueTotalKey = "adskit.ad_revenue_usd";
        private const string RevenueTierKey = "adskit.ad_revenue_tier";
        private const float PumpInterval = 0.25f;
        private const float LoadTimeoutSec = 60f;
        private const float OpenTimeoutSec = 6f;
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

        private bool _skipNextResume;
        private bool _welcomeBackActive;

        private MrecState _mrecState;
        private string _mrecUnitId;
        private int _mrecRequest;
        private int _mrecAttempts;
        private double _mrecNextRetryAt;
        private bool _mrecWanted;
        private bool _mrecVisible;
        private Vector2 _mrecCenter;

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

            // Every session after the first already knows its provider (A/B group locked, or no A/B test): start
            // consent + SDK init now, in parallel with the fetch, so the cold-start (splash) ad has time to load.
            // The fresh Remote Config values are applied whenever they arrive.
            if (State == InitState.FetchingRemote && MediationKnownWithoutFetch())
            {
                KitLog.Info("Mediation already known; initializing without waiting for Remote Config");
                ContinueInit();
            }
        }

        private bool MediationKnownWithoutFetch() =>
            (Application.isEditor && _settings.useSimulatorInEditor)
            || !_settings.abTestMediation
            || !string.IsNullOrEmpty(PlayerPrefs.GetString(MediationLockKey, ""));

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
            _provider.AdViewClicked += () => KitRunner.Dispatch(SuppressNextResume);

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

            if (!RemoteConfigService.TryGetRemoteString(_settings.mediationRemoteKey, out var value))
            {
                // Still fetching: wait. Fetch done but no value (parameter not published yet, or offline on the
                // very first launch): run this session on the default provider WITHOUT locking it, so the A/B
                // group is still assigned the first time Remote Config provides one.
                if (RemoteConfigService.State == RemoteFetchState.Fetching || RemoteConfigService.State == RemoteFetchState.NotStarted)
                    return null;
                KitLog.Warn("No '" + _settings.mediationRemoteKey + "' value from Remote Config; using " + _settings.defaultMediation + " for this session (not locked)");
                return Installed(_settings.defaultMediation);
            }
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

            void BecomeReady(string how)
            {
                if (State != InitState.InitializingSdk || attempt != _initAttempt) return;
                State = InitState.Ready;
                _banner = new BannerController(this);
                _pumpNow = true;
                KitLog.Info(MediationId + " ready (" + how + ") in " + Mathf.RoundToInt((float)((KitRunner.Now - started) * 1000)) + " ms");
                Ready?.Invoke();
            }

            // AdMob only calls back once EVERY mediation adapter started or timed out (~30 s for a slow one), which
            // would cost the splash ad and early placements. After sdk_init_max_wait_sec, start requesting anyway:
            // adapters that are still starting just miss the first auctions. A late callback is then a no-op.
            // AdMob only: MAX must not load anything before OnSdkInitializedEvent (it answers in ~1-2 s anyway).
            var maxWait = Mathf.Max(0f, _config.Loading.sdkInitMaxWaitSec);
            if (maxWait > 0f && MediationId == AdsIds.AdMob)
                KitRunner.After(maxWait, () =>
                {
                    if (State == InitState.InitializingSdk && attempt == _initAttempt)
                        BecomeReady("not all adapters finished after " + maxWait + "s; loading anyway");
                });

            _provider.Initialize(args, ok => KitRunner.Dispatch(() =>
            {
                if (attempt != _initAttempt) return;
                if (State == InitState.Ready)
                {
                    KitLog.Info(MediationId + " initialization finished after " + Mathf.RoundToInt((float)(KitRunner.Now - started)) + " s");
                    return;
                }
                if (State != InitState.InitializingSdk) return;
                if (!ok)
                {
                    Fail("failed to initialize");
                    return;
                }
                BecomeReady("callback");
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
            // The bar is waiting right now: skip any retry backoff so a failed first load gets another chance.
            if (State == InitState.Ready) ForceLoad(unitKey);
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

        /// <summary>
        /// The player is about to leave the app on purpose (purchase sheet, store page, share, permission dialog):
        /// the next return from the background shows no Welcome Back screen and no app-open ad.
        /// Ad clicks (full-screen, banner, MREC) set this automatically.
        /// </summary>
        public void SuppressNextResume()
        {
            if (!_skipNextResume) KitLog.Info("Next resume will not show an app-open ad");
            _skipNextResume = true;
        }

        public bool WelcomeBackActive => _welcomeBackActive;

        /// <summary>Debug: run the return-from-background flow as if the app was away for <paramref name="awaySeconds"/>.</summary>
        public void SimulateResume(double awaySeconds) => OnResumed(awaySeconds);

        // ------------------------------------------------------------------ MREC

        public bool IsMrecReady => _mrecState == MrecState.Ready;

        /// <summary>Load the MREC in advance (e.g. on the home screen) so the exit panel shows it at once.</summary>
        public void PreloadMrec()
        {
            var p = _config?.Mrec;
            if (CheckGates(p, false) == AdBlockReason.None) LoadMrec(p);
        }

        /// <summary>Show the MREC centred on a screen point (pixels, origin top-left). Loads it first when needed.</summary>
        public void ShowMrec(Vector2 centerPixels)
        {
            var p = _config?.Mrec;
            var reason = CheckGates(p, false);
            if (reason != AdBlockReason.None)
            {
                LogOpportunity(AdsIds.PlacementMrec, AdFormat.Mrec, reason);
                return;
            }
            _mrecWanted = true;
            _mrecCenter = centerPixels;
            if (_mrecState == MrecState.Ready) ShowMrecNow();
            else
            {
                LogOpportunity(AdsIds.PlacementMrec, AdFormat.Mrec, Online ? AdBlockReason.NotReady : AdBlockReason.Offline);
                _mrecNextRetryAt = 0; // the panel is open right now: skip any retry backoff
                LoadMrec(p);
            }
        }

        public void HideMrec()
        {
            _mrecWanted = false;
            if (!_mrecVisible) return;
            _mrecVisible = false;
            _provider?.HideMrec();
        }

        private void ShowMrecNow()
        {
            _mrecVisible = true;
            _provider.ShowMrec(_mrecCenter);
            LogOpportunity(AdsIds.PlacementMrec, AdFormat.Mrec, AdBlockReason.None);
            AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.Show, AdsIds.PlacementMrec, AdFormat.Mrec).Log();
        }

        private void LoadMrec(PlacementRuntime p)
        {
            var unitId = _config.GetUnitId(MediationId, p.UnitKey);
            if (string.IsNullOrEmpty(unitId) || _mrecState == MrecState.Loading) return;
            if (_mrecState == MrecState.Ready && _mrecUnitId == unitId) return;
            if (!Online || KitRunner.Now < _mrecNextRetryAt) return;
            _mrecState = MrecState.Loading;
            _mrecUnitId = unitId;
            var request = ++_mrecRequest;
            KitLog.Info("Loading MREC");
            KitRunner.After(LoadTimeoutSec, () => OnMrecLoaded(request, AdLoadResult.Fail("timeout")));
            _provider.LoadMrec(unitId, r => KitRunner.Dispatch(() => OnMrecLoaded(request, r)));
        }

        private void OnMrecLoaded(int request, AdLoadResult result)
        {
            if (request != _mrecRequest || _mrecState != MrecState.Loading) return;
            if (result.Success)
            {
                _mrecState = MrecState.Ready;
                _mrecAttempts = 0;
                KitLog.Info("MREC loaded" + (string.IsNullOrEmpty(result.Network) ? "" : " (" + result.Network + ")"));
                if (_mrecWanted && !_mrecVisible && CheckGates(_config.Mrec, false) == AdBlockReason.None) ShowMrecNow();
                return;
            }
            _mrecState = MrecState.Idle;
            _mrecAttempts++;
            var delay = Math.Min(Math.Pow(2, Math.Min(6, _mrecAttempts)), _config.Loading.retryMaxBackoffSec);
            _mrecNextRetryAt = KitRunner.Now + delay;
            KitLog.Warn("MREC load failed: " + result.Error + " (retry in " + delay + "s)");
            if (_config.Loading.logLoadFailures)
                AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.LoadFail, AdsIds.PlacementMrec, AdFormat.Mrec)
                    .Add("error", result.Error ?? "")
                    .Add(KitEvents.ParamAttempt, _mrecAttempts)
                    .Log();
        }

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
            if (f.maxInterstitialsPerSession > 0 && _interstitialsThisSession >= f.maxInterstitialsPerSession) return AdBlockReason.SessionCap;
            if (p.Priority) return AdBlockReason.None; // priority places ignore every cooldown timer
            if (SessionTracker.SecondsInSession < f.firstInterstitialMinSessionSec) return AdBlockReason.SessionStartGrace;
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
            var id = SlotId(key);
            if (string.IsNullOrEmpty(id) || !_desiredSet.Add(id)) return; // _desiredSet holds unit ids
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
                    if (s.State == AdSlotState.Ready && !_desiredSet.Contains(s.UnitId) && (evictable == null || s.LoadedAt < evictable.LoadedAt))
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
                if (_config.Loading.refreshOnlyWhenNeeded && !_desiredSet.Contains(slot.UnitId))
                    KitLog.Info(slot.Key + " expired; not needed now, not reloading");
                else
                    KitLog.Info(slot.Key + " about to expire; reloading");
                Discard(slot);
            }
        }

        // Slots are keyed by the SDK ad unit id, not the AdsKit unit key: SDKs hold one ad per unit id, so placements
        // configured with the same id must share one cached ad (Google's test ids, or a deliberately shared unit).
        // A unit id changed remotely simply maps to a new slot; the old one is never desired again and gets evicted.
        private string SlotId(string key) => key != null ? _config.GetUnitId(MediationId, key) : null;

        private AdSlot GetSlot(string key)
        {
            var id = SlotId(key);
            return !string.IsNullOrEmpty(id) && _slots.TryGetValue(id, out var slot) ? slot : null;
        }

        private AdSlot GetOrCreateSlot(string key)
        {
            var unitId = SlotId(key);
            if (string.IsNullOrEmpty(unitId)) return null;
            if (_slots.TryGetValue(unitId, out var slot)) return slot;
            if (!_config.UnitFormats.TryGetValue(key, out var format)) return null;
            slot = new AdSlot { Key = key, Format = format, UnitId = unitId };
            _slots[unitId] = slot;
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

        private void AddWaiter(string unitKey, float seconds, Action<bool> callback, bool failFast = false)
        {
            _waiters.Add(new Waiter { UnitKey = unitKey, Deadline = KitRunner.Now + seconds, FailFast = failFast, Callback = callback });
        }

        private void ProcessWaiters()
        {
            if (_waiters.Count == 0) return;
            var now = KitRunner.Now;
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                var w = _waiters[i];
                var slot = GetSlot(w.UnitKey);
                var ready = State == InitState.Ready && _active == null && IsSlotReady(slot);
                var failed = w.FailFast && (State != InitState.Ready || slot == null || slot.State == AdSlotState.Idle);
                if (!ready && !failed && now < w.Deadline) continue;
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
                if (slot.State == AdSlotState.Ready && slot.Format == AdFormat.Interstitial && !_desiredSet.Contains(slot.UnitId))
                    Discard(slot);
        }

        /// <summary>
        /// Return from the background: Welcome Back screen, then the app-open ad. The ad does not wait for the
        /// interstitial cooldown; the screen stays at least welcome_back_min_sec and at most timeout_sec, and closes
        /// at once when the ad fails to load. Never after our own ads, ad clicks or <see cref="SuppressNextResume"/>.
        /// </summary>
        private void OnResumed(double awaySeconds)
        {
            var suppressed = _skipNextResume;
            _skipNextResume = false;
            if (State != InitState.Ready || _active != null || _welcomeBackActive || _context == AdsIds.ContextSplash) return;
            if (suppressed)
            {
                KitLog.Info("Resume: returning from an ad click / external screen; no app-open ad");
                return;
            }
            if (KitRunner.Now - _lastFullscreenClose < 3.0) return; // the pause was our own ad
            var p = _config.AppOpenResume;
            if (p == null || !p.Enabled || awaySeconds < p.MinBackgroundSec) return;

            var reason = CheckGates(p, false);
            if (reason == AdBlockReason.None && string.IsNullOrEmpty(SlotId(p.UnitKey))) reason = AdBlockReason.PlacementUnknown;
            if (reason != AdBlockReason.None)
            {
                LogOpportunity(p.Id, AdFormat.AppOpen, reason);
                return;
            }

            _welcomeBackActive = true;
            var shownAt = KitRunner.Now;
            AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.WelcomeBackShown, p.Id, AdFormat.AppOpen).Log();
            try { _hooks?.ShowWelcomeBack(); }
            catch (Exception e) { Debug.LogException(e); }

            ForceLoad(p.UnitKey);
            AddWaiter(p.UnitKey, Mathf.Max(0.5f, p.TimeoutSec), ready =>
            {
                if (!ready)
                {
                    var timedOut = KitRunner.Now - shownAt >= p.TimeoutSec - 0.05;
                    LogOpportunity(p.Id, AdFormat.AppOpen, _active != null ? AdBlockReason.Busy : Online ? AdBlockReason.NotReady : AdBlockReason.Offline);
                    EndWelcomeBack(p.Id, timedOut ? "timeout" : "ad_failed", shownAt);
                    return;
                }
                var wait = p.WelcomeBackMinSec - (float)(KitRunner.Now - shownAt);
                if (wait > 0f) KitRunner.After(wait, () => ShowResumeAd(p, shownAt));
                else ShowResumeAd(p, shownAt);
            }, failFast: true);
        }

        private void ShowResumeAd(PlacementRuntime p, double shownAt)
        {
            var reason = State != InitState.Ready ? AdBlockReason.NotInitialized : CheckRules(p, false);
            if (reason == AdBlockReason.None) reason = CheckReady(p.UnitKey);
            LogOpportunity(p.Id, AdFormat.AppOpen, reason);
            EndWelcomeBack(p.Id, reason == AdBlockReason.None ? "ad_shown" : "ad_failed", shownAt);
            if (reason == AdBlockReason.None) BeginFullscreen(GetSlot(p.UnitKey), p.Id, ShowKind.Resume, null, null);
        }

        private void EndWelcomeBack(string placementId, string reason, double shownAt)
        {
            if (!_welcomeBackActive) return;
            _welcomeBackActive = false;
            AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.WelcomeBackClosed, placementId, AdFormat.AppOpen)
                .Add("reason", reason)
                .Add("shown_sec", Math.Round(KitRunner.Now - shownAt, 1))
                .Log();
            try { _hooks?.HideWelcomeBack(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void OnRevenuePaid(AdRevenueEvent e)
        {
            var unitKey = e.AdUnitId;
            foreach (var key in _config.UnitFormats.Keys)
                if (_config.GetUnitId(MediationId, key) == e.AdUnitId && _config.UnitFormats[key] == e.Format) { unitKey = key; break; }
            // Banner refreshes keep paying while a full-screen ad is open: never credit them to that placement.
            var placement = e.Format == AdFormat.Banner ? AdsIds.PlacementBanner
                : e.Format == AdFormat.Mrec ? AdsIds.PlacementMrec
                : !string.IsNullOrEmpty(e.Placement) ? e.Placement
                : _active != null ? _active.PlacementId : "";
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
                case ShowKind.Resume:
                    // Closing the app-open ad restarts the interstitial cooldown from zero (fail/timeout does not).
                    _lastInterstitialClose = now;
                    break;
                default:
                    _lastSplashClose = now;
                    break;
            }

            AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.Close, s.PlacementId, slot.Format)
                .Add("view_sec", Math.Round(s.ViewSeconds, 1))
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

            // Unity is paused while a full-screen ad covers it, so the SDK callbacks are only processed after the
            // ad closed. Timestamp them when the SDK fires them (any thread) to measure the real view time.
            private static readonly System.Diagnostics.Stopwatch SdkClock = System.Diagnostics.Stopwatch.StartNew();
            private double _openedAtSdk = -1;
            private double _closedAtSdk = -1;

            public double ViewSeconds => _openedAtSdk >= 0 && _closedAtSdk >= _openedAtSdk ? _closedAtSdk - _openedAtSdk : 0;

            public FullscreenSession(AdsService service, AdSlot slot, string placementId, ShowKind kind, Action onReward, Action<AdShowResult> onDone)
            {
                _service = service;
                Slot = slot;
                PlacementId = placementId;
                Kind = kind;
                OnReward = onReward;
                OnDone = onDone;
            }

            public void OnOpened(string network)
            {
                var at = SdkClock.Elapsed.TotalSeconds;
                KitRunner.Dispatch(() =>
                {
                    if (_opened || _finished) return;
                    _opened = true;
                    _openedAtSdk = at;
                    _service.OnFullscreenOpened(this, network);
                });
            }

            public void OnFailedToShow(string error) => KitRunner.Dispatch(() => Finish(false, error));

            // No SuppressNextResume here: the player returns into the ad, and its callbacks are only processed after
            // Unity resumed, so a flag set now would wrongly swallow the next real resume. _active / the 3 s window cover it.
            public void OnClicked() => KitRunner.Dispatch(() =>
                AdsAnalyticsEvents.Begin(AdsAnalyticsEvents.Clicked, PlacementId, Slot.Format).Log());

            public void OnRewardEarned() => KitRunner.Dispatch(() => RewardEarned = true);

            public void OnClosed()
            {
                var at = SdkClock.Elapsed.TotalSeconds;
                KitRunner.Dispatch(() =>
                {
                    if (_closedAtSdk < 0) _closedAtSdk = at;
                    Finish(true, null);
                });
            }

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
            if (_config.Mrec != null) sb.Append("mrec: ").Append(_mrecState).Append(_mrecVisible ? " visible" : "")
                .Append(_mrecAttempts > 0 ? " fails " + _mrecAttempts : "").Append('\n');
            if (_skipNextResume || _welcomeBackActive)
                sb.Append(_welcomeBackActive ? "WELCOME BACK" : "").Append(_skipNextResume ? " next resume suppressed" : "").Append('\n');
        }

        private static int Remaining(double last, float cooldown, double now) => Mathf.Max(0, (int)(cooldown - (now - last)));
    }
}
