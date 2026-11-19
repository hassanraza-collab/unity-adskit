---
name: appupdate-integrate
description: Add soft / hard app update prompts (release management, forced update; our popup or Google Play in-app update, switchable remotely) to the current Unity game with AdsKit's AppUpdate module, driven by the Firebase Remote Config parameter app_update_config. First fetches and pulls the latest game and AdsKit code. Use when the user asks for update prompts, force update, soft/hard update, minimum version or release management. Requires AdsKit to be integrated first.
---

# Add app update prompts (AdsKit AppUpdate) to this game

AdsKit's `NineDTech.AdsKit.Update.AppUpdate` (Core assembly) does the logic:
- it reads the Remote Config JSON `app_update_config` and compares versions with `Application.version`;
- it picks soft or hard, and applies the soft cooldown and max shows;
- it escalates soft to hard, opens the store, and logs `app_update_prompt`.

**The game only draws the popup** (`IAppUpdatePresenter`). If AdsKit is not integrated yet, run `adskit-integrate` first.

Git: use `git`. On Windows, if it is not on PATH, use GitHub Desktop's
`%LOCALAPPDATA%\GitHubDesktop\app-*\resources\app\git\cmd\git.exe`.

## 1. Get the latest code (always first)
1. **This game's repo:**
   - run `git status`;
   - if there are uncommitted changes, tell the user and ask before going on; never stash or discard them yourself;
   - then `git fetch --all --prune` and `git pull --ff-only` on the current branch;
   - if fast-forward fails, stop and report; don't merge or rebase without the user's OK.
2. **AdsKit:** find `com.ninedtech.adskit` in `Packages/manifest.json`.
   - **`file:` path (local checkout):** in that folder, run `git status` (same rule for uncommitted changes), then `git fetch --all --prune` and `git pull --ff-only`. Report the branch and the new HEAD.
   - **Git URL `...unity-adskit.git#<ref>`:**
     - list the tags with `git ls-remote --tags https://github.com/hassanraza-collab/unity-adskit.git`;
     - in a temporary clone, check for AppUpdate at the newest tag: `git show <tag>:Runtime/Core/Update/AppUpdate.cs`;
     - if no tag has it, it is only on `develop`: ask the user whether to point the manifest at `#develop` (or a commit hash) or to wait for a release tag;
     - when you change the ref, also read `CHANGELOG.md` / `MIGRATION.md` for the versions in between, and apply their steps as the `adskit-update` skill does;
     - remove the `com.ninedtech.adskit` entry from `Packages/packages-lock.json` only if Unity keeps the old hash.
3. Confirm that `Runtime/Core/Update/AppUpdate.cs` exists in the package (in the `file:` path or in `Library/PackageCache/com.ninedtech.adskit@*/`). If it doesn't, stop and tell the user.

## 2. Read the module
Read these completely before writing code:
- the package's `Documentation~/APP_UPDATE.md`;
- `Runtime/Core/Update/AppUpdate.cs`.

## 3. Survey the game
Find:
- how popups are built, opened and closed, and how the "closed" callback works;
- a similar blocking popup to copy patterns from (no internet, rate us, consent);
- the localization system and the languages it supports;
- how the game knows the home / menu screen is showing, that no popup is open, and that no level is in progress;
- the module/service architecture and the boot order (AppUpdate must initialize after the UI can open popups);
- any local remote-config / QA override wrapper;
- the debug menu or popup gallery.

Summarize the plan in a few lines (files, gate rule, strings) and **get the user's OK before editing**.

## 4. Implement the presenter
Write the presenter in the game's own UI style and architecture.

**Interface members:**
- `IsShowing`: true while the popup is on screen, including its close animation;
- `Show(AppUpdatePrompt p)`;
- `Hide()`.

**Content:**
- title: `p.Kind` Soft → "UPDATE AVAILABLE", Hard → "UPDATE REQUIRED";
- message: `p.CustomMessage ?? <localized default>`;
- soft: "Later" and "Update" buttons, plus the close X;
- hard (`!p.CanDismiss`): only "Update"; no X, no backdrop close, Back does nothing; keep it above every other popup (also ones opened later).

**Wiring:**
- the buttons call `p.ChooseUpdate()` / `p.ChooseLater()`;
- the popup's closed callback calls `p.ChooseLater()` (safe: it is ignored if the prompt was already answered or hidden by AdsKit);
- **don't** open the store or log analytics yourself: AdsKit does both, and calls `Hide()` for soft prompts;
- a hard prompt stays open after Update;
- if the popup can't open, leave `IsShowing` false and AdsKit retries.

**Initialize once at startup, after the UI is ready:**

```csharp
AppUpdate.CanShowSoft = () => /* home screen shown && no popup open && not in a level */;
AppUpdate.ConfigSource = key => /* game QA override, or null */;   // optional
AppUpdate.Initialize(presenter, new AppUpdateConfig());            // defaults: disabled until Remote Config says otherwise
```

**Debug:** hook `AppUpdate.ShowPreview(AppUpdateKind.Soft)` and `(AppUpdateKind.Hard)` into the debug menu or popup gallery.

**Strings:** add them for **every language the game already supports**, reusing existing translations ("Later", "Update") where they exist:
- "UPDATE AVAILABLE";
- "UPDATE REQUIRED";
- "A new version is available with improvements and fixes. Update now?";
- "This version is no longer supported. Please update to continue playing.";
- "Update";
- "Later".

## 5. Firebase Remote Config
Give the user the JSON. **Never publish Remote Config changes without asking first.**

Safe Android default (prompts nobody):

```json
{"enabled":true,"latest_version":"<current Application.version>","min_version":"","soft_cooldown_hours":24,"soft_max_shows":3,"store_url":"market://details?id=<android bundle id>","update_ui":"custom","soft_message":"","hard_message":""}
```

- **`update_ui`** (Android): `"custom"` uses the game's popup; `"google"` uses Google Play in-app update (hard = immediate, soft = flexible), with the game's popup as the fallback. Ask the user which one they want. It can be switched remotely at any time.
- **Google mode needs** `com.google.android.play:app-update` in the Gradle build. AdsKit's `Editor/AdsKitAppUpdateDependencies.xml` adds it through the External Dependency Manager. After updating AdsKit, have the user run **Android Resolver > Force Resolve** (or add the line yourself next to the other `implementation` lines in `Assets/Plugins/Android/mainTemplate.gradle`). Confirm the line is there.
- Play compares **versionCode**: remind the user to bump it with the version name every release.

- **iOS:** add a platform condition value with `"enabled":false` until the App Store ID exists. Then set `"store_url":"itms-apps://apps.apple.com/app/id<APPID>"`, the iOS `latest_version`, and `enabled` to true.
- Old builds without AppUpdate ignore the parameter, so creating it is harmless.
- **Release rule:** set `latest_version` to N once build N is live (soft prompt). Raise `min_version` only for breaking changes. **Never set it above the version that is live in that store.**

## 6. Docs and verification
1. Add a short doc in the game's docs folder:
   - the remote fields (link to AdsKit `Documentation~/APP_UPDATE.md`);
   - this game's soft-prompt gate;
   - the live Firebase values;
   - the QA steps.
2. Compile: check the Unity console, or `Editor.log` (`%LOCALAPPDATA%\Unity\Editor\Editor.log` on Windows), for `error CS`.
3. Test both previews:
   - **soft:** Later, X and Update each close it;
   - **hard:** no X, Back does nothing, stays on top;
   - if possible, a QA override with `latest_version` above the current version: the soft prompt shows once, on the home screen only.
   - Google mode can't be tested in the editor or on sideloaded builds (they fall back to the game's popup). Tell the user to test it through internal app sharing or an internal test track: install the older build from Play, then upload a newer one.
4. Analytics: `app_update_prompt` (`type`, `choice`, `ui`, `app_version`, `target_version`) is logged by AdsKit. Add it to the game's analytics doc if it keeps one.
5. **Don't commit until the user approves.**

Report:
- the pulled commits (game and AdsKit) and the AdsKit ref now used;
- the files changed;
- the soft gate rule;
- what is untested;
- the Firebase JSON.
