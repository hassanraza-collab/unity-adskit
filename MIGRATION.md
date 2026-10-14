# Migration guide

Steps needed in a game when updating AdsKit across versions. Breaking Remote Config schema changes ship under a new parameter name.

## 0.5.0 -> 0.6.0
- Behaviour change: the app-open ad on resume no longer waits for the interstitial cooldown, and closing it restarts that cooldown. Remote `app_open_resume` gained `welcome_back_min_sec` / `timeout_sec`.
- Optional, recommended (release checklist): override `ShowWelcomeBack()` / `HideWelcomeBack()` in your hooks to show a Welcome Back screen; mark win/level-select interstitials `priority`; call `Ads.SuppressNextResume()` before opening the store page, purchase sheet, share or permission dialogs.
- Exit-panel MREC: add an `Mrec` placement (ad unit format `Mrec`) to AdsSettings, then `Ads.ShowMrec(anchorRect)` / `Ads.HideMrec()`.
- Custom `IMediationProvider` implementations must add `AdViewClicked`, `LoadMrec`, `ShowMrec`, `HideMrec`, `DestroyMrec`.

## 0.4.x -> 0.5.0
- Optional: replace hand-copied adapter files with **Tools > AdsKit > Install Mediation Adapters** (same versions).

## 0.3.0 -> 0.4.0
- If the game has a settings screen, add a "Privacy options" button shown only when `Ads.PrivacyOptionsRequired` is true; it calls `Ads.ShowPrivacyOptions()` (GDPR requirement).
- Optional: fill test device ids / consent debug fields in AdsSettings for device testing.

## 0.2.0 -> 0.3.0
No code changes. If Firebase is installed, open the editor once so `ADSKIT_FIREBASE` defines are added (or run Tools/AdsKit/Refresh SDK Defines).

## 0.1.0 -> 0.2.0
No migration. 0.2.0 is the first functional release (phase 1).
