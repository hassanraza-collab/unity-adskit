# Changelog

All notable changes to this package are documented here. Versions follow [Semantic Versioning](https://semver.org).

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
