# AdsKit: Quick Start (Android)

## 1. Add the package
In `Packages/manifest.json`:
```json
"com.ninedtech.adskit": "https://github.com/hassanraza-collab/unity-adskit.git#v0.5.0"
```

## 2. Import the SDKs
- Firebase: App, Analytics, Remote Config, Crashlytics.
- AppLovin MAX.
- Google Mobile Ads.

In every import dialog, **untick `ExternalDependencyManager`** if you already have it. Keep exactly one copy.

## 3. Settings
- Put `google-services.json` in `Assets/`.
- Enter the AdMob App ID in **both** places:
  - Assets → Google Mobile Ads → Settings;
  - AppLovin → Integration Manager.
- Enter the MAX SDK key in the Integration Manager.
- Player Settings: minimum API level 24.

## 4. Adapters
Run **Tools → AdsKit → Install Mediation Adapters**, then **Assets → External Dependency Manager → Android Resolver → Force Resolve**.

⚠️ Never click **Upgrade** in the AppLovin Integration Manager.

## 5. Config
1. Write your `ads_config` JSON (start from `Documentation~/ads_config.example.json`).
2. Run **Tools → AdsKit → Import ads_config JSON...** and pick a folder.
3. Add one **Rewarded** placement asset per rewarded button (Create → AdsKit → Placements → Rewarded) and add it to `AdsSettings`.
4. Run **Tools → AdsKit → Validate Settings**.

## 6. Code
In Package Manager → AdsKit → Samples, import **Starter**. Then:

| Where | Call |
|---|---|
| First scene | Add `AdsKitStarter` and assign both settings assets |
| Hooks | Fill in the TODOs in `MyGameAdsHooks` |
| Loading bar at 70% | `Ads.ShowSplashAd(_ => ContinueLoading())` |
| Home / gameplay shown | `Ads.SetContext(AdsIds.ContextHome)` / `Ads.SetContext(AdsIds.ContextGameplay)` |
| Interstitial | `Ads.ShowInterstitial("play_button", _ => StartLevel())` (continue **inside** the callback) |
| Rewarded | `Ads.ShowRewarded("booster", GiveReward)` |
| Bottom UI | Add `AdsBannerInset` to each screen's root |
| Settings | Show a "Privacy Settings" button when `Ads.PrivacyOptionsRequired`; on tap call `Ads.ShowPrivacyOptions()` |
| Levels | `LevelAnalytics.LevelStart` / `LevelEnd` / `LevelRestart` / `LevelQuit` |

Using Claude? Run `/adskit-integrate` and it does steps 5–6 for you.

## 7. Firebase Remote Config
Create these parameters and publish:

| Parameter | Type | Value |
|---|---|---|
| `ads_mediation` | String | `max` or `admob` |
| `ads_config` | JSON | your config |
| `analytics_config` | JSON | `{"enabled": true}` |

## 8. Test
- **Editor:** press Play. Fake ads appear. Turn on `AdsSettings.showDebugHud` to watch the cache.
- **Device:** make a development build and run `adb logcat -s Unity`. To see every ad right away, publish a test `ads_config` with all gates set to 0.

## Updating
Change the `#vX.Y.Z` tag in `manifest.json` (or run `/adskit-update`), then follow `MIGRATION.md`.
