# Changelog

All notable changes to this package are documented here. Versions follow [Semantic Versioning](https://semver.org).

## [0.6.1] - 2026-10-14
Start-up and threading safety fixes from the SDK init audit.
### Fixed
- **AdMob callbacks now run on the Unity main thread.** Load (interstitial, rewarded, app open), show (opened, clicked, failed, closed, reward) and banner/MREC (loaded, failed, clicked) callbacks used to run on the Android UI thread. There they changed the loaded-ad dictionaries and destroyed banners while the main thread read them. `OnAdPaid` stays immediate (revenue is already dispatched).
- **UMP callbacks** (consent info update, consent form, privacy options form) now run on the Unity main thread.
- **Analytics** `Event(...).Log()` from any thread hops to the main thread *before* the common-param enrichers run, because enrichers may call Unity APIs such as PlayerPrefs. The builder pool is main-thread only.
- **MAX no longer becomes "ready" before its init callback.** The `sdk_init_max_wait_sec` "load anyway" fallback now applies to AdMob only.
### Added
- `AdsKitConsentDefaultsProcessor` (Editor): writes Google Consent Mode v2 default-denied keys for Firebase Analytics into the generated Android manifest: `google_analytics_default_allow_analytics_storage`, `_ad_storage`, `_ad_user_data`, `_ad_personalization_signals` = false. `FirebaseConsentMode` grants them once UMP reports not required or granted.
## [0.6.0] - 2026-10-14
Release-checklist ads rules (cooldown, priority places, Welcome Back + app open, exit-panel MREC).
### Added
- **Priority interstitials**: `InterstitialPlacement.priority` / remote `interstitials.<id>.priority`. A priority place (win/fail, level select) shows even while any cooldown is running (session grace, after-splash, after-rewarded, global, per-placement); the session cap and audience gates still apply, and closing it restarts the cooldowns.
- **Welcome Back flow** for the app-open ad on resume: `IAdsGameHooks.ShowWelcomeBack()` / `HideWelcomeBack()` (default no-op). The screen shows at least `welcome_back_min_sec` (2) and at most `timeout_sec` (14) while the ad loads, closes at once when the load fails, and the game continues on timeout. Events `welcome_back_shown`, `welcome_back_closed` (`reason` = ad_shown | ad_failed | timeout, `shown_sec`).
- `Ads.SuppressNextResume()`: call before purchase / store / share / permission flows; the next return shows no Welcome Back or app-open ad. Banner and MREC clicks set it automatically (new `IMediationProvider.AdViewClicked`).
- **MREC** (300x250): `AdFormat.Mrec`, `MrecPlacement` (`AdsSettings.mrec`, remote block `mrec`), `Ads.PreloadMrec()`, `Ads.ShowMrec(RectTransform | Vector2)`, `Ads.HideMrec()`, `Ads.IsMrecReady`. MAX, AdMob and simulator implementations (simulator draws a clickable rectangle).
- Debug HUD buttons: **Resume** (runs the resume flow) and **MREC** (toggle at screen centre). Simulated banner/MREC are clickable (counts as an ad click).
### Changed
- The app-open ad on resume **no longer waits for the interstitial cooldown**, and closing it **restarts the interstitial cooldown** (it used to start the after-splash cooldown). A failed or timed-out app-open ad does not touch the timer.
- `KitRunner.Now` keeps counting while the app is in the background on platforms where `realtimeSinceStartup` stands still, so cooldowns run in the background.
- `IMediationProvider` gained `AdViewClicked`, `LoadMrec`, `ShowMrec`, `HideMrec`, `DestroyMrec` (custom providers must implement them).

## [0.5.0] - 2026-10-14
First release for use in other projects.
### Added
- **Tools > AdsKit > Install Mediation Adapters**: writes the version-locked adapter files for BOTH stacks (MAX: Google, Pangle, Mintegral, Liftoff, Unity Ads; AdMob: AppLovin, Pangle, Mintegral, Liftoff, Unity Ads) incl. Integration Manager labels. The lock lives in `AdsKitAdapterInstaller`.
- `AdsBannerInset` component: keeps a UI rect clear of the banner.
- Samples~/Starter: `MyGameAdsHooks` template + `AdsKitStarter` bootstrap with every placement call.
- Documentation~/QUICK_START.md.

## [0.4.5] - 2026-10-14
### Changed
- Cold start: when the mediation provider is already known (A/B group locked, or no A/B test), consent + SDK init start immediately in parallel with the Remote Config fetch instead of after it (device test: the splash window closed before init finished).
- `sdk_init_max_wait_sec` default 5 -> 3.

## [0.4.4] - 2026-10-14
### Fixed (found in the first device test, BlueStacks + real AdMob test ads)
- SDK init: AdMob only calls back after every mediation adapter started or timed out (~30 s), which cost the splash ad. New `loading.sdk_init_max_wait_sec` (default 5): ads start loading after that even if adapters are still starting; a late callback is a no-op. The old 30 s timeout no longer discards a slow-but-valid init.
- Placements sharing one ad unit id now share one cached ad (slots keyed by unit id). Before, a shown ad left the other placements wrongly "ready".
- `view_sec` measured from SDK callback times (Unity is paused under full-screen ads, so both callbacks used to arrive in the same frame).
- Banner revenue is always attributed to the `banner` placement, also while a full-screen ad is open; `ad_unit_name` resolves the readable unit key for banners.
### Changed
- `max_cached_interstitials` default 2 -> 3 so every gameplay interstitial placement is preloaded.

## [0.4.3] - 2026-10-14
### Fixed
- IL2CPP builds stripped the Firebase/MAX/AdMob provider assemblies (nothing references them; they self-register), so device builds fell back to the simulator. `AdsKitLinkerProcessor` now generates a link.xml preserving every installed AdsKit assembly (Unity ignores link.xml inside packages).

## [0.4.2] - 2026-10-14
### Fixed
- A/B mediation: when Remote Config has no `ads_mediation` value after the fetch (parameter not published yet / first launch offline), the session runs on `defaultMediation` without locking, instead of never initializing ads.

## [0.4.1] - 2026-10-14
### Fixed
- Splash ad: while the loading bar waits, a failed first load retries immediately instead of waiting out its backoff (found in the Rotate Rings editor test).

## [0.4.0] - 2026-10-14
### Added
- `NineDTech.AdsKit.Max`: AppLovin MAX provider (interstitial, rewarded, app-open, standard/adaptive banner, revenue, Mediation Debugger).
- `NineDTech.AdsKit.AdMob`: Google Mobile Ads provider (same formats, revenue in USD from micros, Ad Inspector) and the Google UMP consent provider used by both arms.
- Firebase Consent Mode v2 from the consent result.
- `Ads.PrivacyOptionsRequired` / `Ads.ShowPrivacyOptions` for the GDPR settings button.
- AdsSettings: AdMob/MAX test device ids, consent debug (force EEA, UMP test devices).
### Changed
- No ad SDK initializes until consent allows ad requests; SDK init has a 30 s timeout with retry.

## [0.3.0] - 2026-10-14
### Added
- Firebase provider assembly (`NineDTech.AdsKit.Firebase`, compiles only with `ADSKIT_FIREBASE`): Remote Config source with real-time updates, Analytics sink, user properties mirrored to Crashlytics keys, fatal uncaught exceptions.
- Editor: automatic SDK scripting defines (`Tools/AdsKit/Refresh SDK Defines`).
### Changed
- `AdsSettings.editorRemoteJson` overrides any backend, including Firebase, in the Editor.

## [0.2.0] - 2026-10-13
### Added
- Core: KitRunner (main thread, timers), SessionTracker, RemoteConfigService, MiniJson.
- Analytics: sinks with per-sink queues, Firebase name/limit validation, common params, LevelAnalytics with milestones, remote analytics_config.
- Ads: AdsService with A/B mediation lock, consent step, predictive load scheduler, gates/cooldowns, splash/app-open/interstitial/rewarded/banner flows, revenue logging and tiers.
- Simulator provider and debug HUD.
- Editor: import/export ads_config JSON, validator, reset local state.
- Docs: AI_INTEGRATION.md, ANALYTICS_EVENTS.md, example config; Claude Code plugin (adskit-integrate, adskit-update).

## [0.1.0] - 2026-10-13
### Added
- Package skeleton: `package.json`, README, changelog.
