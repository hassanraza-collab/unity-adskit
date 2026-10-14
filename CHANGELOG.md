# Changelog

All notable changes to this package are documented here. Versions follow [Semantic Versioning](https://semver.org).

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
