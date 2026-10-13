---
name: adskit-update
description: Update the AdsKit Unity package (com.ninedtech.adskit) in the current game to a newer version and apply its migration steps. Use when the user asks to update/upgrade AdsKit or bump the ads package.
---

# Update AdsKit in this game

1. **Read the current version.** In `Packages/manifest.json`, find `com.ninedtech.adskit`:
   - a `#vX.Y.Z` git tag;
   - or a `file:` path, which means local development and nothing to bump.
2. **List the newer releases:** `git ls-remote --tags https://github.com/hassanraza-collab/unity-adskit.git`. Pick the latest tag, or the one the user names.
3. **Read what changed.** Read `CHANGELOG.md` and `MIGRATION.md` at that tag. Fetch them with `git show <tag>:CHANGELOG.md` from a temporary clone, or read the package cache after updating.
4. **Bump the version.** Change only the tag in `manifest.json`. If `packages-lock.json` pins the old hash, Unity re-resolves it on the next focus.
5. **Apply every migration step** listed for the versions in between, in the game's glue code only.
6. **Check the remote config.** If `ads_config` schema keys changed, tell the user which Remote Config JSON changes are needed. Breaking changes use a new parameter name.
7. **Verify.** Compile, then run the verification checklist in `Documentation~/AI_INTEGRATION.md` section 9. Report the old version, the new version and the steps applied.
