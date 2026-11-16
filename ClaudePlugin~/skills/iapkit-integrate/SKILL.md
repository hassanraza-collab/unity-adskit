---
name: iapkit-integrate
description: Integrate the IapKit Unity package (com.ninedtech.iapkit; Unity IAP 5 in-app purchases, subscriptions/VIP, remove ads, restore, purchase-funnel analytics) into the current Unity game. Use when the user asks to add IAP, in-app purchases, a store/shop, VIP or subscriptions, Remove Ads, or offers to a game. Requires AdsKit to be integrated first.
---

# Integrate IapKit into this game

IapKit rides on AdsKit: AdsKit owns Firebase Remote Config, analytics and `KitRunner`. **If AdsKit is not integrated yet, run the `adskit-integrate` skill first.**

## 1. Find the package and read it
- `Packages/manifest.json` needs:
  - `com.ninedtech.iapkit`: if it is missing, ask the user for the tag and add `"https://github.com/hassanraza-collab/unity-iapkit.git#<tag>"`, or a `file:` path for local development;
  - `com.unity.purchasing` 5.x: without it, IapKit silently runs on the simulator store;
  - `com.ninedtech.adskit` 0.6 or newer.
- The package files are in `Packages/com.ninedtech.iapkit/`, the `file:` path, or `Library/PackageCache/com.ninedtech.iapkit@*/`.
- Read these **completely** before writing code:
  - `README.md`;
  - `Runtime/Core/Iap.cs` (the whole public API);
  - `Runtime/Core/IIapGameHooks.cs`;
  - `Runtime/Core/IapSettings.cs`;
  - `Runtime/Core/IapConfig.cs` and `IapCatalog.cs` (the config JSON schema).

## 2. Survey the game, then agree on a plan
Find:
- the boot/splash flow and the place where AdsKit is initialized;
- the game's currencies, boosters and other inventory (the reward ids);
- the existing `IAdsGameHooks` implementation and its `AdsRemoved`;
- every rewarded-ad call site (needed for a VIP skip);
- the Home and Settings screens, and the win/fail screens;
- the save system, the module/service architecture and the UI style.

Ask the user which **products** they want. Typical ones:
- `remove_ads` (non_consumable);
- `vip` (subscription, optional trial offer);
- coin/booster packs (consumable);
- bundles;
- a starter offer (one-time).

Also ask for:
- the **placements** each product is sold from (splash, home, store, win, fail, booster_empty, settings...);
- the Play Console product ids, or a naming prefix to generate them.

Show a table (product key → type → placement → store id → rewards → UI entry point) and **get the user's OK before editing**.

## 3. Config
- Write the game's default `iap_config` JSON as a TextAsset (e.g. `Assets/<Game>/Settings/Iap/iap_config_default.json`):
  - `schema_version`, `iap_enabled`;
  - `products`: per key, `type` (`consumable` / `non_consumable` / `subscription`), `ids` (placement → store id), `rewards` (reward id → amount), and `trial_offer_id` where needed;
  - any game sections (store layout, offers, `vip_benefits`, `remove_ads_benefits`, screens). IapKit passes these through untouched. Read them with `Iap.Section(name)` or by `JsonUtility` on `Iap.ConfigJson`.
- Create an `IapSettings` asset:
  - `defaultConfigJson` = that file;
  - `vipProductKey`, `removeAdsProductKeys` and `currencyRewardIds` to match the catalog;
  - `googlePlayPublicKey`: ask the user for the Base64 licensing key from Play Console → Monetization setup. It is public and safe to commit;
  - leave `simulator.useInEditor` on for editor testing.
- Give the user the same JSON for Firebase Remote Config parameter `iap_config`. Publishing it is their action unless they ask you to do it.

## 4. Glue code, in the game's `Assets/`, following its architecture
Reference implementation: Rotate Rings, `Assets/_Game/Runtime/Iap/` (`GameIapHooks`, `IapModule`, `VipRewardedAdService`, `StoreScreenModule`, `SubscriptionScreenModule`, `OfferModule`) and `Runtime/Ads/GameAdsHooks.cs`. Copy the **behaviour**, never its visuals: build the screens in the target game's own UI style.

- **Hooks** (derived from `IapGameHooksBase`):
  - `CurrentLevel`: the same value AdsKit hooks report.
  - `GrantRewards`:
    - first **persist** the rewards to a pending queue in PlayerPrefs/save. The store acknowledges right after it returns, and a redelivered purchase can arrive during the splash before gameplay systems exist;
    - then flush the queue into inventory once the game is running, and again at every boot.
  - `BeforeStoreSheet`: `Ads.SuppressNextResume()`.
  - `OnEntitlementsChanged`:
    - cache `Iap.AdsRemoved` to PlayerPrefs;
    - refresh the UI (hide Remove Ads, hide the banner);
    - grant VIP perks.
- **Init:** `Iap.Initialize(iapSettings, hooks)` right **after** `Ads.Initialize`, on the splash.
- **AdsKit link:** the game's `IAdsGameHooks.AdsRemoved` returns `Iap.IsInitialized ? Iap.AdsRemoved : <cached PlayerPrefs flag>`. AdsKit then drops banner, interstitial and app-open by itself. Do not gate ads anywhere else.
- **Rewarded for VIP (optional):** wrap the rewarded service so VIP gets the reward without an ad, logging `rewarded_vip_skip`. Exclude placements listed in the config and any **permanent unlocks**.
- **Buying:**
  - `Iap.Purchase(key, placement, result => ...)`;
  - prices only from `Iap.Price(key, placement, fallback)`;
  - hide or disable with `Iap.CanBuy(key)`;
  - refresh prices on `Iap.StoreReady`;
  - show a short message on `result.Status` failures, but nothing on user cancel.
- **Funnel:** `Iap.LogScreenShown(screen, placement)` when a store, VIP or offer screen opens, and `Iap.LogScreenClosed(screen, placement, seconds)` when it closes without buying.
- **Settings:**
  - a **Restore Purchases** button (`Iap.Restore`), always present on iOS and also wanted on Android;
  - a **Manage subscription** entry (`Iap.OpenSubscriptionManagement(vipKey)`) when the game sells subscriptions;
  - call `Ads.SuppressNextResume()` before external links (terms, privacy).
- **Subscription screens:** the store is the source of truth for price, period and trial text. Show the terms and privacy links and the auto-renew wording that store policy needs.
- **Localization:** if the game has a localization system, add every new string to it.

## 5. Hard rules
- Never edit the package from a game.
- Never call Unity IAP directly: only the `Iap` facade.
- Never confirm or acknowledge purchases yourself.
- Never grant rewards anywhere but `GrantRewards`.
- Never hard-code prices: use the store price with a fallback.
- Never log your own purchase or revenue events. IapKit logs `iap_*`, and AdsKit's sinks forward revenue.
- `ad_free_hours` rewards are handled by IapKit and never reach `GrantRewards`.

## 6. Verify
1. Compile.
2. In the editor, use the simulator store (`IapSettings.simulator`) and **Tools → IapKit**:
   - grant, cancel and expire VIP;
   - grant and revoke Remove Ads;
   - restore;
   - reset.
3. Check:
   - prices show;
   - each product grants once, also after a restart mid-purchase;
   - Remove Ads hides banner, interstitial and app-open, but not rewarded;
   - VIP perks apply and stop on expiry;
   - restore works;
   - the funnel events appear in the AdsKit debug HUD/logs.
4. On device: an internal-track build with license testers. Every store id in `products.*.ids` must exist and be active in Play Console, with **one base plan** per subscription id.

Report what was and was not verified.
