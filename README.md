# AdsKit (`com.ninedtech.adskit`)

AdsKit is an exportable Unity package. It provides:

- **Ads mediation:** AppLovin MAX and Google AdMob, with A/B-testable provider selection.
- **Remote control:** placements, cooldowns, gates and ad unit IDs are driven by Firebase Remote Config.
- **Analytics:** ads, level progression and meta-game funnels.

> Status: **in development (0.x)**. The API is not stable yet.

**New project? Read [Documentation~/QUICK_START.md](Documentation~/QUICK_START.md) (one page).**

## Install

Add the package to the game's `Packages/manifest.json`. Pin a release tag:

```json
"com.ninedtech.adskit": "https://github.com/hassanraza-collab/unity-adskit.git#v0.5.0"
```

For local development, point to the folder instead:

```json
"com.ninedtech.adskit": "file:../../unity-adskit"
```

The ad SDKs (AppLovin MAX, Google Mobile Ads, Firebase) are **not bundled**. Install them in the game project. AdsKit detects each one automatically.

## Quick start

1. Run **Tools → AdsKit → Import ads_config JSON...** with your Remote Config JSON. `Documentation~/ads_config.example.json` is an example.
2. Run **Tools → AdsKit → Validate Settings**.
3. In code:
   ```csharp
   AnalyticsBootstrap.Initialize(analyticsSettings);
   Ads.Initialize(adsSettings, new MyHooks());      // MyHooks : AdsGameHooksBase
   Ads.SetContext(AdsIds.ContextHome);
   Ads.ShowInterstitial("play_button", _ => StartLevel());
   Ads.ShowRewarded("booster_hammer", () => GrantBooster());
   ```
4. Press Play. In the Editor, the **simulator** provider shows fake ads. Turn on `showDebugHud` to see the cache state.

## Docs

- `Documentation~/AI_INTEGRATION.md`: step-by-step integration (written for Claude, readable by humans).
- `Documentation~/ANALYTICS_EVENTS.md`: every event, parameter and funnel.
- `ADSKIT_DEVELOPMENT.md`: development status, architecture and the version lock table.
- `CHANGELOG.md` / `MIGRATION.md`: release notes and upgrade steps.

## Claude Code plugin

This repo is also a Claude Code plugin marketplace with the `adskit-integrate` and `adskit-update` skills. Install it once per machine:

```
/plugin marketplace add hassanraza-collab/unity-adskit
/plugin install adskit@ninedtech-adskit
```
