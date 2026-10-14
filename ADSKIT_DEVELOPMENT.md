# AdsKit development tracker

> Single source of truth for AdsKit's development status. Update it with every change.
> First consumer: Rotate Rings (`game-unity-rapid-rotate-rings`), which references this package through `file:../../unity-adskit`.

## Goal
An exportable, ScriptableObject-driven ads and analytics package:
- **A/B test:** a 50/50 Firebase A/B test between **AppLovin MAX** and **Google AdMob** mediation, in one binary. Only the assigned SDK initializes, and the assignment is locked per install.
- **Networks:**
  - Phase 1: AdMob, AppLovin, Pangle, Mintegral, Liftoff Monetize.
  - Phase 2: InMobi, Unity Ads.
- **Remote control:** everything is set through Firebase Remote Config (`ads_config`, `ads_mediation`, `analytics_config`).
- **Placements:**
  - banner, standard or adaptive (chosen remotely);
  - rewarded;
  - splash app-open or splash interstitial, shown at 70% of the loading bar;
  - interstitials: play_button, level_complete, level_restart, back_to_home.
  - Each placement has its own ad unit, cooldown and gates (`min_sessions`, `min_level`).
- **Loading:** a predictive, staggered cache instead of loading every ad at splash.
- **Platform:** Android only for now.

## Owner decisions (2026-10-13)
- Android only.
- A session is a cold start, or a return after 30 min or more in the background.
- **No fallback between placements:** each placement shows only its own unit.
- A level gate (`min_level`) sits next to the session gate (`min_sessions`).
- App-open on resume exists but is disabled by default.
- Package: `com.ninedtech.adskit`, repo `hassanraza-collab/unity-adskit`, distributed as a git URL with a tag. Developed locally through a `file:` reference.
- **GMA Next-Gen SDK is NOT used.** Its classes clash with MAX's Google adapter (classic `play-services-ads`) in a single binary. We'll revisit later.

## Research rules (sources in the game repo: `Docs/AdsKit/RESEARCH_NOTES.md`)
- **Cache only the likely-next ads.** Unused loaded ads lower AdMob show rate and can get MAX requests throttled.
- **Load serially, splash ad first.** Retry with exponential backoff 2–64 s, capped attempts, paused while offline.
- **AdMob expiry:** interstitial and rewarded 1 h, app-open 4 h. Refresh at 55 / 235 min, only when needed. MAX manages expiry itself.
- **Never `RaiseAdEventsOnUnityMainThread`.** Use our own dispatcher.
- **AdMob arm:** Firebase logs `ad_impression` automatically, so don't log it manually. **MAX arm:** log it manually.
- **Consent:** UMP before either SDK initializes.
- **Android:** minSdk 24, target API 35 or higher.

## Phases
| # | Phase | Status |
|---|---|---|
| 1 | Core, ScriptableObjects, simulator, analytics core, Editor tools, docs, Claude skill | **done 2026-10-13** (compile-checked against Unity 6000.3 DLLs; play-test in the game pending) |
| 2 | Firebase provider assembly: Remote Config source, Analytics sink, Crashlytics keys | **done 2026-10-14** (compile-checked against Firebase 13.17.0; device test pending) |
| 3 | MAX provider, then AdMob provider; UMP consent provider | **done 2026-10-14** (compile-checked vs MAX 8.6.6 / GMA 11.5.0 / UMP; device test pending) |
| 4 | Phase-1 adapters, version lock table, device QA matrix | todo |
| 5 | Game integration (Rotate Rings): splash, placements, banner safe area, analytics glue | **done 2026-10-14** (editor play-test with simulator OK; device test pending) |
| 6 | A/B experiment launch; phase-2 adapters | todo |

## Architecture (phase 1)
```
Runtime/Core   NineDTech.AdsKit.Core
  KitRunner          main-thread queue, Tick, pause/quit/low-memory, After() timers, realtime clock
  SessionTracker     session count (cold start + timeout resume), Resumed(awaySec)
  Remote/            IRemoteConfigSource, LocalRemoteConfigSource, RemoteConfigService (one fetch, timeout, Activated)
  Analytics/         Analytics (sinks, per-sink queue, validation, enrichers), EventBuilder, Names (Firebase limits),
                     AnalyticsSettings + AnalyticsBootstrap (remote analytics_config), LevelAnalytics, KitEvents
  Json/MiniJson      parser/writer for dynamic-key JSON
Runtime/Ads    NineDTech.AdsKit.Ads
  Config/            AdsSettings, AdUnitDefinition, placements (Interstitial, Rewarded, Banner, SplashAd, AppOpenResume)
  Core/AdsService    init (remote → A/B lock → consent → SDK), scheduler, gates, show flow, revenue
  Core/BannerController, AdsRuntimeConfig, AdsRemoteConfigApplier, AdShowResult, IAdsGameHooks, AdsAnalyticsEvents
  Providers/         IMediationProvider, MediationProviderRegistry, AdsConsent
  Simulator/         SimulatorMediationProvider, SimulatorAdOverlay (IMGUI), AdsDebugHud
  Ads.cs             static facade
Runtime/Firebase  NineDTech.AdsKit.Firebase (defineConstraint ADSKIT_FIREBASE)
  FirebaseBootstrap  registers source+sink at AfterAssembliesLoaded, CheckAndFixDependencies, Crashlytics fatal reporting
  FirebaseRemoteConfigSource  fetch+activate (dev: interval 0, release: 1h), real-time updates -> Activated
  FirebaseAnalyticsSink  LogEvent/SetUserProperty; user properties mirrored to Crashlytics custom keys
Runtime/Max    NineDTech.AdsKit.Max (ADSKIT_MAX, refs MaxSdk.Scripts)
  MaxMediationProvider  global MAX callbacks routed per ad unit; selective init with all unit ids; consent -> SetHasUserConsent;
                        banner via CreateBanner(AdViewConfiguration{IsAdaptive}); height = GetAdaptiveBannerHeight/50dp(90 tablet) * density;
                        ManagesExpiry=true; logs ad_impression manually (AutoLogsFirebaseImpressions=false)
Runtime/AdMob  NineDTech.AdsKit.AdMob (ADSKIT_ADMOB)
  AdMobMediationProvider  per-unit InterstitialAd/RewardedAd/AppOpenAd; events wired per show; OnAdPaid micros -> USD;
                          banner AdSize.Banner / anchored adaptive FullWidth, hidden until AdsKit shows it; Ad Inspector;
                          never RaiseAdEventsOnUnityMainThread; AutoLogsFirebaseImpressions=true
  UmpConsentProvider     registered as AdsConsent.Provider (both arms); Update -> LoadAndShowConsentFormIfRequired;
                          status from TCF purpose 1 (TcfData reads default SharedPreferences via JNI); privacy options form
Runtime/Firebase FirebaseConsentMode  AdsConsent.StatusChanged -> FirebaseAnalytics.SetConsent (Consent Mode v2)
Editor         Tools/AdsKit: Import/Export ads_config JSON, Validate, Reset local state, Refresh SDK Defines
  AdsKitSdkDefines   [InitializeOnLoad] detects SDK assemblies -> ADSKIT_FIREBASE / _CRASHLYTICS / ADSKIT_MAX / ADSKIT_ADMOB (Android+Standalone)
```

### Load scheduler (AdsService.Pump, every 0.25 s)
1. **Build the desired list** for the current context:
   - **Splash context:** the splash unit only.
   - **Other contexts:** the first `cache_priority` interstitial, then the rewarded units, then the remaining interstitials (up to the cache size), then app-open-resume.
   - A placement is listed only if it could show this session (switches, gates, session cap) and has a unit id.
2. **Expiry:** for providers without managed expiry, drop ads older than the refresh age. They reload only if desired.
3. **Start a load:** at most one load at a time (`max_concurrent_loads`), with `load_stagger_sec` between loads. Retries back off per slot. After `retry_max_attempts` failures a slot is "exhausted" until the context changes or the network returns.
4. **Interstitial memory:** cache size + 1 (keeps one ad from the previous context). The oldest unneeded ad is evicted when space is needed. Low RAM or a `lowMemory` signal shrinks the cache.

### Show rules (order = analytics `blocked` reason)
| Check | Applies to |
|---|---|
| Initialized | all |
| `ads_enabled` | all |
| Placement `enabled` | all |
| Ads removed | all except rewarded |
| `min_sessions` | all |
| `min_level` | all |
| `first_interstitial_min_session_sec`, session cap, cooldown after splash, cooldown after rewarded, global cooldown, placement cooldown | interstitials |
| Busy (an ad is showing) | all |
| Ready (offline / not ready) | all |

Every show callback fires exactly once. A 6 s open watchdog and a 60 s load watchdog guard against SDKs that never call back.

## Version lock table (phase 4)
| Component | Version | Notes |
|---|---|---|
| Firebase Unity SDK (App, Analytics, Remote Config, Crashlytics) | 13.17.0 | imported as .unitypackage into Assets/Firebase (Rotate Rings) |
| External Dependency Manager (EDM4U) | 1.2.189 | bundled with Firebase; keep this single copy |
| AppLovin MAX Unity plugin | 8.6.6 (applovin-sdk 13.6.4) | .unitypackage in Assets/MaxSdk; no adapters yet; SDK key pending |
| Google Mobile Ads Unity plugin | 11.5.0 (play-services-ads 25.4.0, UMP 4.0.0) | classic SDK, not Next-Gen; Android App ID = Google TEST id until the AdMob account exists |
| Adapters (MAX / GMA) per network | TBD | must resolve to the same network SDK version |

## Known gaps / TODO
- **Simulator input:** the full-screen overlay is IMGUI, so game uGUI under it can still receive taps (the game is paused through the hooks). It's acceptable for the simulator.
- **Sample:** no `Samples~` folder yet. `AI_INTEGRATION.md` holds the integration snippets.
- **Firebase A/B test:** confirm whether "Estimated ad revenue" counts MAX `ad_impression`. Otherwise compare in BigQuery.

## Changelog
- **0.4.0 (2026-10-14), phase 3:** MAX + AdMob providers, UMP consent provider (TCF purpose 1 mapping), Firebase Consent Mode v2, consent gate (no ad requests until UMP CanRequestAds), 30s SDK init timeout with retry, test-device ids + consent debug settings in AdsSettings, Ads.PrivacyOptionsRequired/ShowPrivacyOptions.
- **0.3.0 (2026-10-14), phase 2:** Firebase provider assembly (Remote Config source with real-time updates, Analytics sink, Crashlytics custom keys); SDK define auto-detection; Editor test JSON now overrides Firebase too. TODO phase 3: Analytics consent mode (SetConsent) from UMP.
- **0.2.0 (2026-10-13), phase 1:**
  - Runtime: core runtime, analytics core, ads service with predictive cache, simulator, debug HUD.
  - Editor: JSON import/export, validator.
  - Docs: AI integration guide, analytics events, Claude plugin and skill.
- **0.1.0 (2026-10-13):** package skeleton.
