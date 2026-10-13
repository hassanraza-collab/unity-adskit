# AdsKit (`com.ninedtech.adskit`)

AdsKit is an exportable Unity package. It provides:

- **Ads mediation:** AppLovin MAX and Google AdMob, with A/B-testable provider selection.
- **Remote control:** placements, cooldowns, gates and ad unit IDs are driven by Firebase Remote Config.
- **Analytics:** ads, level progression and meta-game funnels.

> Status: **in development (0.x)**. The API is not stable yet.

## Install

Add the package to the game's `Packages/manifest.json`. Pin a release tag:

```json
"com.ninedtech.adskit": "https://github.com/hassanraza-collab/unity-adskit.git#v0.1.0"
```

For local development, point to the folder instead:

```json
"com.ninedtech.adskit": "file:../../unity-adskit"
```

The ad SDKs (AppLovin MAX, Google Mobile Ads, Firebase) are **not bundled**. Install them in the game project. AdsKit detects each one automatically.

## Docs

- `Documentation~/` covers usage and integration, including an AI integration guide.
- `CHANGELOG.md` lists changes per release.
