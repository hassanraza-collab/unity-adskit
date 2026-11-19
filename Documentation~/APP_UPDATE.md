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
- Analytics: `app_update_prompt` — `type` (soft|hard), `choice` (shown|update|later), `app_version`, `target_version`.

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
