---
name: adskit-integrate
description: Integrate the AdsKit Unity package (com.ninedtech.adskit; ads mediation MAX/AdMob, Firebase Remote Config, analytics) into the current Unity game. Use when the user asks to add ads, AdsKit, monetization or ads/level analytics to a game, or to wire placements (interstitial, rewarded, banner, splash/app-open).
---

# Integrate AdsKit into this game

1. **Find the package and read its guide.** Locate AdsKit:
   - `Packages/manifest.json` should list `com.ninedtech.adskit`. If it doesn't, ask the user for the version tag, then add `"com.ninedtech.adskit": "https://github.com/hassanraza-collab/unity-adskit.git#<tag>"`.
   - The package files are in `Packages/com.ninedtech.adskit/` (embedded), the `file:` path from the manifest, or `Library/PackageCache/com.ninedtech.adskit@*/`.

   Read `Documentation~/AI_INTEGRATION.md` **completely** before writing code. Also read `Documentation~/ANALYTICS_EVENTS.md`.

2. **Survey the game** (step 1 of the guide):
   - boot/splash flow and loading bar;
   - level storage;
   - pause and audio;
   - every placement trigger;
   - Home/Gameplay screen entry points;
   - bottom UI that a banner would cover;
   - existing event system.

   Show the user a short mapping table (placement id → code location) and **confirm it before editing**.

3. **Config:**
   - Get the game's `ads_config` JSON from the user (or start from `Documentation~/ads_config.example.json`).
   - Tell the user to run **Tools → AdsKit → Import ads_config JSON** and **Validate Settings** in Unity. You cannot click Unity menus yourself unless a batch method exists.

4. **Glue code, in the game's `Assets/`, following its architecture** (for example a module or service of its framework):
   - an `IAdsGameHooks` implementation (usually derived from `AdsGameHooksBase`);
   - init on the splash: `AnalyticsBootstrap.Initialize`, then `Ads.Initialize`;
   - the splash ad at `Ads.SplashTriggerProgress`;
   - `Ads.SetContext` on screen changes;
   - each placement wrapped so the flow continues in the callback;
   - rewarded with `Ads.NotifyRewardedOffer`;
   - the banner offset through `Ads.BannerHeightChanged`;
   - `LevelAnalytics` calls and the `lvl` common parameter;
   - a Welcome Back screen through the hooks' `ShowWelcomeBack` / `HideWelcomeBack` (the app-open ad on resume);
   - `Ads.SuppressNextResume()` right before store, purchase, share or permission flows;
   - `priority: true` on the win/fail and level-select interstitials;
   - an exit panel (device Back) with `Ads.ShowMrec(anchor)` / `Ads.HideMrec()`, preloaded on home with `Ads.PreloadMrec()`.

5. **Hard rules:**
   - Never edit the package from a game.
   - Never call SDKs directly.
   - Never add your own ad cooldowns or counters.
   - Never log `ad_impression`.
   - Never set `RaiseAdEventsOnUnityMainThread`.

6. **Verify:** compile, then walk the user through the checklist in section 9 of the guide (simulator + debug HUD). Report what was and was not verified.
