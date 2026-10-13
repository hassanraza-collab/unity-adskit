# AdsKit analytics events

**Firebase rules:**
- Event names have at most 40 characters; there are at most 25 parameters per event.
- String values are cut at 100 characters.
- User property names have at most 24 characters and values at most 36.
- Reserved names such as `ad_click`, `ad_reward`, `error` and `session_start` must not be used. That's why AdsKit uses `ad_clicked` and `ad_rewarded`.

## Common parameters (added automatically)
| Parameter | Added by | Meaning |
|---|---|---|
| `sess` | AnalyticsBootstrap | Session number: every cold start, plus every return after 30 min or more in the background |
| `med` | AdsService | Mediation group: `max`, `admob` or `simulator` |
| `lvl` | the game (via `Analytics.AddCommonParams`) | Highest level reached |

## User properties (set by AdsKit)
`ad_mediation`, `consent`, `banner_type`. Games usually add `max_level_bucket`, `session_bucket` and `install_version`.

## Logged automatically by AdsKit
| Event | Parameters |
|---|---|
| `remote_config_ready` | `result` (completed/timedout/failed), `source`, `fetch_ms` |
| `consent_result` | `status` |
| `ad_opportunity` | `placement`, `ad_format`, `ready` (0/1), `blocked` (none / disabled / ads_removed / session_gate / level_gate / session_start / session_cap / after_splash / after_rewarded / global_cooldown / cooldown / busy / offline / not_ready / not_initialized / unknown) |
| `ad_show` | `placement`, `ad_format`, `ad_source` |
| `ad_show_fail` | `placement`, `ad_format`, `error` |
| `ad_clicked` | `placement`, `ad_format` |
| `ad_close` | `placement`, `ad_format`, `view_sec` |
| `rewarded_offer` | `placement`, `ready` (call `Ads.NotifyRewardedOffer`) |
| `rewarded_tap` | `placement` |
| `rewarded_unavailable` | `placement`, `waited_sec` |
| `ad_rewarded` | `placement` |
| `ad_impression` | `ad_platform`, `ad_source`, `ad_format`, `ad_unit_name`, `currency`, `value`. Logged only when the SDK does not already log it for Firebase (MAX and the simulator yes, AdMob no). |
| `adskit_impression` | the same as above + `placement`, `precision`. Logged for every provider, for a clean comparison in BigQuery. |
| `ad_load_fail` | `placement` (= ad unit key), `ad_format`, `error`, `attempt`, `load_ms` (switch: `loading.log_load_failures`) |
| `ad_revenue_tier_<N>` | `value`, `currency`. Logged once when the total ad revenue passes `revenue_tiers_usd[N-1]`. |

## Logged through helpers the game calls
| Event | Helper | Parameters |
|---|---|---|
| `level_start` | `LevelAnalytics.LevelStart` | `level`, `level_name`, `attempt`, `is_replay` |
| `level_end` | `LevelAnalytics.LevelEnd` | `level`, `level_name`, `success`=1, `duration_sec`, `moves`, `attempt`, `boosters_used`, `score` |
| `level_restart` | `LevelAnalytics.LevelRestart` | `level`, `level_name`, `duration_sec`, `moves`, `attempt` |
| `level_quit` | `LevelAnalytics.LevelQuit` | `level`, `level_name`, `duration_sec`, `moves` |
| `level_milestone_<N>` | automatic, inside `LevelEnd` | `level`, `days_since_install` |

## Recommended game events (log with `Analytics.Event`)
| Event | Parameters |
|---|---|
| `loading_start` / `loading_complete` | `load_ms`, `splash_ad_shown` |
| `tutorial_begin` / `tutorial_complete` | `tutorial_id` |
| `screen_view` | `screen_name`, `screen_class` |
| `booster_unlocked` / `booster_used` / `booster_empty_tap` | `booster`, `level`, `source` |
| `earn_virtual_currency` / `spend_virtual_currency` | `virtual_currency_name`, `value`, `source` |
| `setting_changed` | `setting`, `value` |

## Remote control (`analytics_config` JSON)
```json
{ "enabled": true, "disabled_events": ["ad_load_fail"], "level_milestones": [1,2,3,5,10,20,30,50,100] }
```

## Funnels
- **Onboarding:** `first_open` → `loading_complete` → `level_milestone_1` → `_3` → `_5` → `_10` → `_20`
- **Interstitial health (per placement):** `ad_opportunity` → `ad_show` → `ad_close`. Break down by `blocked`.
- **Rewarded:** `rewarded_offer` → `rewarded_tap` → `ad_show` → `ad_rewarded`
- **Difficulty:** `level_start` vs `level_restart` vs `level_end`, by `level`

Register the custom dimensions in GA4: `placement`, `ad_format`, `ad_source`, `blocked`, `med`, `level`, `booster`, `screen_name`, `source`. Also register the metrics: `duration_sec`, `moves`, `value`.
