# App update (soft / hard) — release management

`NineDTech.AdsKit.Update.AppUpdate` (Core assembly) prompts players on old builds to update, driven by one
Firebase Remote Config parameter. AdsKit decides **when**; the game supplies the popup (`IAppUpdatePresenter`).

## Remote Config: `app_update_config` (JSON)
See `app_update_config.example.json`.

| Field | Meaning |
|---|---|
| `enabled` | Master switch. Off = never prompt. |
| `latest_version` | Builds **below** this get the **soft** prompt (Update / Later). Empty = none. |
| `min_version` | Builds **below** this get the **hard** prompt (blocking, only Update). Empty = none. |
| `soft_cooldown_hours` | Minimum hours between soft prompts (default 24). |
| `soft_max_shows` | Soft prompts per `latest_version` (default 3, 0 = unlimited). Resets when `latest_version` changes. |
| `store_url` | Android empty = `market://details?id=<bundle id>`. **iOS must set it**: `itms-apps://apps.apple.com/app/id<APPID>`. |
| `update_ui` | Android only. `"custom"` (default) = the game's popup. `"google"` = Google Play in-app update, with the game's popup as fallback (see below). Ignored on iOS. |
| `soft_message` / `hard_message` | Optional text override (not translated). Empty = the game's localized text. |

Versions compare as dotted numbers against `Application.version` (`1.2.10 > 1.2.9`).
Use a Remote Config **platform condition** (e.g. `iOS`) for different Android / iOS values. Keep iOS `enabled: false`
until the App Store ID exists.

## Behaviour
- **Hard**: shown as soon as known, over everything. Update opens the store and the prompt stays, so returning
  without updating still blocks. Removing the requirement remotely closes it.
- **Soft**: only when `AppUpdate.CanShowSoft` allows, once per session, within cooldown / max shows.
- A soft prompt on screen escalates to hard if `min_version` is raised mid-session.
- When `Ads` is initialized, opening the store calls `Ads.SuppressNextResume()` (no app-open ad on return).
- Analytics: `app_update_prompt` — `type` (soft|hard), `choice` (shown|update|later, plus failed|unavailable|install for
  Google), `ui` (custom|google), `app_version`, `target_version`.

## Google Play in-app update (`update_ui: "google"`, Android)
AdsKit still decides **when** (same versions, gate, cooldown and max shows); only the UI changes:
- **Hard → Immediate update**: Play's full-screen download + install, then the app restarts.
- **Soft → Flexible update**: Play's dialog; the download runs in the background while the player keeps playing. When it
  finishes, AdsKit installs it (`completeUpdate`, Play's install screen + restart) at the next `CanShowSoft` moment.
- **Fallback to the game's popup** (same session): Play reports no update (the store build isn't newer than this
  install's versionCode yet, staged rollout not reaching this user), Play isn't available (sideloaded / editor / no Play
  Store / library missing), the flow fails, or the player **cancels a hard update** (the game's blocking popup then
  appears; its Update button tries Play again, else opens the store).
- Play compares **versionCode**, AdsKit compares **versionName** (`Application.version`): bump both every release.
- Dependency: `Editor/AdsKitAppUpdateDependencies.xml` adds `com.google.android.play:app-update:2.1.0` through the
  External Dependency Manager. After updating AdsKit, run **Assets > External Dependency Manager > Android Resolver >
  Force Resolve** and check the line is in `mainTemplate.gradle`.
- **Testing** needs a build installed from Google Play: use internal app sharing or an internal test track, install the
  older build from Play, then upload a newer one. Debug / sideloaded builds always fall back to the game's popup.

## Integration
```csharp
using NineDTech.AdsKit.Update;

public sealed class UpdatePopup : MonoBehaviour, IAppUpdatePresenter
{
    AppUpdatePrompt _prompt;
    public bool IsShowing => _prompt != null;            // true while on screen (and while closing)

    void Start()
    {
        AppUpdate.CanShowSoft = () => HomeScreen.IsShown && !Popups.AnyOpen;   // your own gate
        AppUpdate.Initialize(this);                        // optional 2nd arg: AppUpdateConfig defaults
    }

    public void Show(AppUpdatePrompt p)
    {
        _prompt = p;
        // Title/text: p.Kind (Soft/Hard), p.CustomMessage ?? your localized text.
        // Hard (!p.CanDismiss): no close button, Back does nothing, keep it on top.
        // Update button -> p.ChooseUpdate();  Later / X -> p.ChooseLater();
    }

    public void Hide() { /* close the popup */ _prompt = null; }
}
```
Rules for the presenter:
- Report choices through the prompt; AdsKit opens the store, logs analytics and calls `Hide()` for soft prompts.
- A hard prompt stays open after `ChooseUpdate()`; `ChooseLater()` is ignored for it.
- Calling `ChooseLater()` from your "popup closed" callback is safe (ignored if already answered).
- If `Show` can't open the popup, leave `IsShowing` false: AdsKit retries on a later check.

Optional: `AppUpdate.ConfigSource = key => myQaOverride(key)` (return null to use Firebase),
`AppUpdate.ShowPreview(AppUpdateKind.Soft|Hard)` for a debug gallery, `AppUpdate.Required`, `AppUpdate.IsOlder(a, b)`.

## Release flow
1. Ship build N and wait until it is live in the store (rollout at 100%).
2. Set `latest_version = N` → older builds get the soft prompt.
3. Only for breaking changes (save format, server API, critical bug): raise `min_version`.
   **Never set `min_version` above the version actually live in that store** — everyone would be blocked.
