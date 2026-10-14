# AdsKit: AI integration guide

> **Audience:** an AI coding assistant (Claude) integrating AdsKit into a Unity game.
> Follow the steps in order. Don't guess game behaviour: read the game's code, and ask the owner when something is unclear.

## 0. What AdsKit is

`com.ninedtech.adskit` is a UPM package with three assemblies:

| Assembly | Contents |
|---|---|
| `NineDTech.AdsKit.Core` | `KitRunner` (main thread, timers), `SessionTracker`, `RemoteConfigService`, `Analytics` and `LevelAnalytics`, `MiniJson` |
| `NineDTech.AdsKit.Ads` | `Ads` facade, `AdsService`, config ScriptableObjects, providers (simulator built in) |
| `NineDTech.AdsKit.Editor` | `Tools/AdsKit` menu: import/export the ads_config JSON, validate, reset local state |

The SDK providers (MAX, AdMob, Firebase) are separate assemblies. Each one compiles only when its SDK is installed. With no SDK installed, the **simulator** provider shows fake ads, so the game can be integrated and tested in the Editor first.

**Hard rules:**
1. **Never put game code in the package.** Game glue lives in the game's `Assets/`.
2. **Never call SDK APIs directly from game code.** Always go through the `Ads` facade and `Analytics`.
3. **Every `Ads.Show*` callback fires exactly once,** including when an ad is blocked. Game flow must continue inside the callback (e.g. load the next level in `onDone`), never wait on a flag.
4. **Never set `MobileAds.RaiseAdEventsOnUnityMainThread`.** AdsKit marshals SDK callbacks to the main thread itself.
5. **Don't log `ad_impression` yourself.** AdsKit logs it correctly for each mediation provider.
6. **The game's architecture wins.** If the game uses a module, service or event-bus system, write the glue as one of its modules.

## 1. Find these in the game first
Search the game code and list:
- **Boot flow:** the first scene, the loading/splash screen and its progress bar.
- **Level state:** where the current and highest-reached level are stored.
- **Pause:** how gameplay pauses, and how music and SFX are muted.
- **Placements:** the code paths for each ad placement. Typical ones are the play button, level complete, level restart, back to home, and every rewarded entry point (booster refill, double reward, ...).
- **Screens:** where Home and Gameplay start (for `Ads.SetContext`).
- **UI layout:** the bottom UI that a banner could cover (it needs a safe-area offset).
- **Analytics sources:** an existing event system for level start/end/restart/quit, tutorials and screens.

## 1b. Adapters
Run **Tools → AdsKit → Install Mediation Adapters** (version-locked, both stacks) then EDM Force Resolve. Never upgrade adapters in the AppLovin Integration Manager.

## 2. Config assets
1. Get the game's `ads_config` JSON. The owner keeps it as the Firebase Remote Config default value. If there is none yet, start from `Documentation~/ads_config.example.json` and change the placement ids.
2. In Unity, run **Tools → AdsKit → Import ads_config JSON...** and choose a folder such as `Assets/<Game>/Config/AdsKit`. This creates `AdsSettings`, `AnalyticsSettings`, one asset per ad unit and one per placement.
3. Add one `RewardedPlacement` asset per rewarded entry point (`Assets → Create → AdsKit → Placements → Rewarded`). Give each its own id and add them to `AdsSettings.rewarded`. They may share one ad unit.
4. Run **Tools → AdsKit → Validate Settings** and fix every error.
5. Re-export with **Export ads_config JSON...** after any change, and give the JSON to the owner for the Firebase console.

## 3. Game hooks
Implement `IAdsGameHooks`, or derive from `AdsGameHooksBase`, which already pauses `Time.timeScale` and `AudioListener`:

```csharp
public sealed class GameAdsHooks : AdsGameHooksBase
{
    public override int CurrentLevel => SaveData.HighestLevel;          // highest level reached
    public override bool AdsRemoved => Purchases.Owns("remove_ads");    // false if no IAP yet
    public override void SetAdLoadingIndicator(bool v) => UI.Spinner(v);
    public override void OnRewardedUnavailable(string id) => UI.Toast("No ad available, try again later");
}
```
Override `OnFullscreenAdOpened` / `OnFullscreenAdClosed` if the game pauses differently, for example with its own audio manager. In that case, don't call the base methods twice.

## 4. Boot (splash)
Initialize as early as possible, on the splash screen and before gameplay loads:

```csharp
AnalyticsBootstrap.Initialize(analyticsSettings);
Ads.Initialize(adsSettings, new GameAdsHooks());
Analytics.Event(KitEvents.LoadingStart).Log();
```

When the loading bar reaches `Ads.SplashTriggerProgress` (default 0.7):
1. Stop the bar.
2. Call `Ads.ShowSplashAd(_ => ResumeLoading())`.

`ShowSplashAd` waits at most `max_wait_sec` for the ad to load, then always calls back. Don't add your own timeout.

When loading finishes: `Analytics.Event(KitEvents.LoadingComplete).Add("load_ms", ms).Log();`

## 5. Context
Call `Ads.SetContext(...)` whenever the player changes screens. It decides which ads stay preloaded:
- On the Home screen: `Ads.SetContext(AdsIds.ContextHome)`.
- When gameplay starts: `Ads.SetContext(AdsIds.ContextGameplay)`.

Use the same context names as the `cache_priority` keys in the JSON. Custom names are allowed.

## 6. Placements
Wrap each trigger and continue inside the callback:

```csharp
// before starting a level from Home
Ads.ShowInterstitial("play_button", _ => StartLevel());
// after the win screen's continue
Ads.ShowInterstitial("level_complete", _ => GoNextLevel());
// restart
Ads.ShowInterstitial("level_restart", _ => RestartLevel());
// back to home
Ads.ShowInterstitial("back_to_home", _ => LoadHome());

// rewarded (booster refill etc.)
Ads.ShowRewarded("booster_hammer", onReward: () => GrantHammer(1));
// when a rewarded button becomes visible (funnel)
Ads.NotifyRewardedOffer("booster_hammer");
```

Rules:
- Interstitial pacing (cooldowns, gates, session cap) is handled by AdsKit. **Never add your own counters.**
- The rewarded callback runs after the ad closes, and only if the reward was earned.

## 7. Banner
The banner shows automatically in every context except splash, once it passes its gates. Two things to wire:
- **Move bottom UI up** by `Ads.BannerHeightPixels`, using the `Ads.BannerHeightChanged` event (screen pixels; 0 when hidden). Convert to canvas units with `pixels / canvas.scaleFactor`.
- **Hide it behind full-screen menus** with `Ads.SetBannerSuppressed(true)`, if needed.

## 7b. Privacy (GDPR)
In the game's settings screen add a **Privacy options** button, visible only when `Ads.PrivacyOptionsRequired` is true, calling `Ads.ShowPrivacyOptions()`. Consent itself (UMP form) runs automatically inside `Ads.Initialize` before any SDK starts; do not add another CMP.

## 8. Analytics
- **Level progression:** call `LevelAnalytics` (`LevelStart`, `LevelEnd`, `LevelRestart`, `LevelQuit`) from the game's level events. Milestone events are automatic.
- **Other events:** use `Analytics.Event("name").Add(...).Log()`. Follow `Documentation~/ANALYTICS_EVENTS.md` for names and parameters.
- **Level on every event:** add the level as a common parameter once:
  `Analytics.AddCommonParams(b => b.AddIfMissing(AnalyticsParam.Of("lvl", (long)SaveData.HighestLevel)));`
- **Naming rules:** names are letters, digits and `_`, at most 40 characters, at most 25 parameters per event. Never use Firebase-reserved names (`ad_click`, `ad_reward`, `error`, `session_start`, ...). `AnalyticsNames.ValidateEventName` checks this.

## 9. Verify (in the Editor, simulator)
Turn on `AdsSettings.showDebugHud` and press Play. Then check:
- [ ] The console shows `[AdsKit] simulator ready` and `Context: home`.
- [ ] The HUD's `desired:` list matches the context, and slots go `Loading → Ready`.
- [ ] Gates take effect: early sessions or levels block interstitials. Use **Tools → AdsKit → Reset Local Ads State** to start over.
- [ ] Each placement shows the simulated ad. The game pauses and resumes, and the flow continues after close.
- [ ] Rewarded **Skip** gives no reward; **Close** after the timer gives it.
- [ ] The banner appears and the UI moves up.
- [ ] Events appear in the console: `[Analytics] ad_opportunity`, `ad_show`, `adskit_impression`, `level_end`, ...
- [ ] Testing a remote payload: assign a JSON `TextAsset` to `AdsSettings.editorRemoteJson` and check the console shows `ads_config applied`.

## 10. When SDKs are installed (later phases)
- Firebase, MAX and GMA provider assemblies switch on automatically. Don't edit the game glue.
- Android: minSdk 24, target API 35 or higher, one copy of EDM4U. Resolve Android dependencies after every SDK change.
- Development builds: `Ads.OpenDebugger()` opens the MAX Mediation Debugger or the AdMob Ad Inspector. Use it to confirm that every adapter is initialized.
