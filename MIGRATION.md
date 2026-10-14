# Migration guide

Steps needed in a game when updating AdsKit across versions. Breaking Remote Config schema changes ship under a new parameter name.

## 0.4.x -> 0.5.0
- Optional: replace hand-copied adapter files with **Tools > AdsKit > Install Mediation Adapters** (same versions).

## 0.3.0 -> 0.4.0
- If the game has a settings screen, add a "Privacy options" button shown only when `Ads.PrivacyOptionsRequired` is true; it calls `Ads.ShowPrivacyOptions()` (GDPR requirement).
- Optional: fill test device ids / consent debug fields in AdsSettings for device testing.

## 0.2.0 -> 0.3.0
No code changes. If Firebase is installed, open the editor once so `ADSKIT_FIREBASE` defines are added (or run Tools/AdsKit/Refresh SDK Defines).

## 0.1.0 -> 0.2.0
No migration. 0.2.0 is the first functional release (phase 1).
