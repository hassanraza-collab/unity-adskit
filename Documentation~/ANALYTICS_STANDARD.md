# Studio Analytics Standard

**Owner:** Hassan Raza (approves every change) · **Master copy:** https://claude.ai/artifact/2Mb5fsbXofsR4qCw6zsB2j · **Version:** 1.0 (2026-11-05)

This file ships with AdsKit so every game that uses AdsKit follows the same analytics rules. The online document above is the master copy; this file is updated from it with each AdsKit release. Suggest changes as comments on the online document.

Every game we make records the same player actions under the same names. That way one set of reports works for every game, and we can compare games side by side.

---

## Part 1: Overview (for everyone)

### Analytics in one minute

When something happens in a game, the game sends a tiny note to Firebase, our analytics service. Each note is called an **event**.

| Word | What it means | Example |
| --- | --- | --- |
| Event | Something that happened | "Player finished a level" |
| Details | Extra facts sent with the event | "Level 12, took 48 seconds, used 1 booster" |
| Player label | A tag on the player that stays | "Has paid", "Reached level 26–50" |

Thousands of these notes add up to answers: where players get stuck, which ads earn the most, why people stop playing.

### Why every game must use the same names

- **One dashboard for all games.** A report built for Rotate Rings works for the next game on day one.
- **Fair comparisons.** "Level completed" means the same thing everywhere, so game A vs game B is a real comparison.
- **Less work.** AdsKit already sends most events, so a new game only adds the few that are about its own gameplay.

### What we track, step by step

| Player step | What we record | The question it answers | Who sends it |
| --- | --- | --- | --- |
| Opens the game | Loading time, privacy choice, which screen they see | Is the game slow to start? Do players leave before playing? | AdsKit + game |
| Learns to play | Tutorial started and finished | Do new players understand the game? | Game |
| Plays levels | Level started, won, lost, restarted, quit, closed mid-way | Which levels are too hard? Where do players give up? | AdsKit + game |
| Sees ads | Ad offered, shown, watched, clicked, money earned | How much does each ad spot earn? Are we showing too many? | AdsKit (automatic) |
| Buys something | Shop opened, purchase started, finished or failed | Which offers sell? Where do purchases fail? | IapKit (automatic) |
| Uses helpers | Boosters used, given, run out | Do boosters help or are they ignored? | Game |
| Stays engaged | Notifications, rating popup, settings changed | What brings players back? Do they like the game? | Game |
| Ends the session | How long they played, how many levels, how many ads | How long is a typical visit? | Game |

On top of this, every player carries a few **labels**: how far they got, whether they paid, how many ads they tolerate, and which test group they are in. Reports can split any number by these labels.

### The five golden rules

1. **Same thing, same name.** If two games measure the same action, they use the exact same event name.
2. **Never rename.** Once a name is live, it stays. Renaming breaks every report that used it.
3. **No personal data.** Never send names, emails, phone numbers or anything a player typed.
4. **AdsKit first.** If AdsKit or IapKit already records it, the game does not record it again.
5. **New events are fine, just ask.** A game can add events about its own gameplay. Write them down before release.

### Starting a new game

1. Add AdsKit (and IapKit if the game sells anything). Ads, purchases and level tracking start working.
2. The developer adds the game's own events, following Part 2.
3. QA runs the release checklist at the end of Part 2 before every release.
4. The owner checks the first build's data in Firebase and signs it off.

### Changing the standard

- **Owner:** Hassan Raza approves every change.
- **To suggest a change:** leave a comment on the online document.
- **Adding** an event is easy. **Renaming or removing** one needs a plan, so old reports keep working for one release.

### What every game reports

The same numbers, built from the same events, for every game: day 1, 7 and 30 retention; how far new players get in their first levels; level difficulty; session length; ad earnings per player; rewarded ad uptake; and purchase conversion.

---

## Part 2: Developer reference

The exact event names, parameters and setup steps. Everything here is logged through AdsKit's `Analytics` wrapper, which sends to Firebase (GA4) and the BigQuery export, and under the same names to GameAnalytics and (mapped events only) Adjust; see "One standard in Firebase, GameAnalytics and Adjust". The RR column is the status in Rotate Rings, the reference game. Per-event detail for AdsKit's automatic events is in `ANALYTICS_EVENTS.md`.

### Naming rules

| Rule | Standard | Example |
| --- | --- | --- |
| Event name | `object_action`, snake_case, ≤ 40 chars | `level_start` |
| Parameter | snake_case, ≤ 40 chars, ≤ 25 per event | `duration_sec` |
| User property | name ≤ 24 chars, value ≤ 36 chars | `player_type` = `payer` |
| String value | lowercase ids, ≤ 100 chars, no free text, no personal data | `source` = `store` |
| Yes/no | int 0/1 | `success` = 1 |
| Units | suffix `_sec`, `_ms`, `_pct` | `load_ms` |
| Money | `value` (double) + `currency` (ISO 4217) | 0.99, `USD` |
| Level | 1-based, as the player sees it | `level` = 12 |

Reserved by Firebase, never use: `ad_click`, `ad_reward`, `error`, `session_start`, `first_open`, `app_update`, `in_app_purchase`, `user_engagement`, and any `firebase_`, `google_`, `ga_` prefix. No game prefix on names (`streak_break`, not `rr_streak_break`).

### Common parameters (on every event)

| Param | Set by | Meaning |
| --- | --- | --- |
| `sess` | AdsKit | Session number (cold start, or return after ≥ 30 min) |
| `med` | AdsKit | Mediation: `max`, `admob`, `simulator` |
| `lvl` | Game, `Analytics.AddCommonParams` | Highest level reached |

### User properties

| Property | Values | Set by |
| --- | --- | --- |
| `ad_mediation` | `max` / `admob` | AdsKit |
| `consent` | `granted` / `denied` / `notrequired` / `unknown` | AdsKit |
| `banner_type` | `standard` / `adaptive` | AdsKit |
| `vip` | subscription state | IapKit |
| `no_ads` | 0 / 1 | IapKit |
| `install_version` | app version at first open | Game |
| `max_level` | `1-10`, `11-25`, `26-50`, `51-100`, `100+` | Game |
| `player_type` | `non_payer` / `payer` / `vip` | Game |
| `avg_levels_per_session` | `1` / `2-3` / `4-6` / `7+` | Game |
| `ad_tolerance` | `low` / `mid` / `high` | Game |
| `level_set` | Remote Config cohort | Game |
| `ab_group` | active A/B variant, e.g. `eco_baseline` | Game |

Keep the bucket edges identical in every game.

### Events: boot and session

| Event | Params | Source | RR |
| --- | --- | --- | --- |
| `loading_start` / `loading_complete` | `load_ms`, `splash_ad_shown` | Game | live |
| `remote_config_ready` | `result`, `source`, `fetch_ms` | AdsKit auto | live |
| `consent_result` | `status` | AdsKit auto | live |
| `session_summary` | `levels_started`, `levels_completed`, `session_sec`, `gameplay_sec`, `ads_inter_shown`, `ads_rewarded_shown`, `boosters_used`, `ended_in_level`, `part` | Game, on background | live |
| `screen_view` | `screen_name`, `screen_class`, `from` | Game | live |
| `tutorial_begin` / `tutorial_complete` | `tutorial`, `step` | Game | live |

### Events: levels

| Event | Params | Source | RR |
| --- | --- | --- | --- |
| `level_start` | `level`, `level_name`, `attempt`, `is_replay` | `LevelAnalytics.LevelStart` | live |
| `level_end` | `level`, `level_name`, `success`, `duration_sec`, `moves`, `attempt`, `boosters_used`, `score` | `LevelAnalytics.LevelEnd` | live |
| `level_fail` | `level`, `level_name`, `duration_sec`, `moves`, `attempt`, `reason` | Game (only games that can be lost) | n/a |
| `level_restart` | `level`, `level_name`, `duration_sec`, `moves`, `attempt` | `LevelAnalytics.LevelRestart` | live |
| `level_quit` | `level`, `level_name`, `duration_sec`, `moves` | `LevelAnalytics.LevelQuit` | live |
| `level_abandon` | `level`, `progress_pct` | Game (app closed mid-level) | live |
| `level_milestone_<N>` | `level`, `days_since_install` | AdsKit, inside `LevelEnd` | live |

### Events: ads (all automatic in AdsKit)

| Event | Params |
| --- | --- |
| `ad_opportunity` | `placement`, `ad_format`, `ready`, `blocked` |
| `ad_show` / `ad_show_fail` | `placement`, `ad_format`, `ad_source` / `error` |
| `ad_clicked` / `ad_close` | `placement`, `ad_format`, `view_sec` |
| `rewarded_offer` / `rewarded_tap` / `ad_rewarded` | `placement`, `ready` (offer needs `Ads.NotifyRewardedOffer`) |
| `rewarded_unavailable` | `placement`, `waited_sec` |
| `ad_impression` / `adskit_impression` | `ad_platform`, `ad_source`, `ad_format`, `ad_unit_name`, `value`, `currency` (+ `placement`, `precision`) |
| `ad_load_fail` | `placement`, `ad_format`, `error`, `attempt`, `load_ms` |
| `ad_revenue_tier_<N>` | `value`, `currency` |
| `welcome_back_shown` / `welcome_back_closed` | `placement`, `ad_format`, `reason`, `shown_sec` |

### Events: purchases (all automatic in IapKit)

| Event | Params |
| --- | --- |
| `iap_store_ready` | `result`, `reason`, `backend` |
| `iap_screen_shown` | `screen`, `placement`, `lvl` (game calls `IapApi.LogScreenShown`) |
| `iap_close` | `screen`, `placement`, `lvl`, `secs` (closed without buying; `IapApi.LogScreenClosed`) |
| `iap_purchase_start` | `product`, `product_id`, `placement`, `type`, `item_type` (`vip` / `no_ads` / `currency` / `item`), `price`, `lvl` |
| `iap_purchase_success` | as start + `value`, `currency`, `trial`, `restored`, `secs` |
| `iap_purchase_pending` | as start |
| `iap_purchase_fail` | as start + `reason` |
| `iap_restore` | `result`, `reason` |
| `iap_sub_state` | `product`, `state` |
| `iap_manage_subscription` | `product` |

Offers and the store are "screens": every offer popup logs `iap_screen_shown` / `iap_close` with its own `screen` id. Firebase's own `in_app_purchase` stays on.

### Events: boosters, economy, engagement

| Event | Params | RR |
| --- | --- | --- |
| `booster_used` | `booster`, `level`, `source` | live |
| `booster_granted` | `booster`, `count`, `source` | live |
| `booster_out` | `booster`, `level` | live |
| `booster_offer_result` | `booster`, `choice` | live |
| `earn_virtual_currency` / `spend_virtual_currency` | `virtual_currency_name`, `value`, `source` | n/a (no coins) |
| `notification_permission` | `result`, `source` | live |
| `notification_soft_ask` | `choice`, `level` | live |
| `notification_open` | `type`, `remote` | live |
| `notification_toggle` | `on` | live |
| `rate_prompt` | `choice` (shown / rate / later), `source` (post_win / exit_panel), `level`, `stars` | added, untested |
| `setting_changed` | `setting` (music / sound / vibration / voice / language), `value` | added, untested |

### One standard in Firebase, GameAnalytics and Adjust

The game logs each event once through `Analytics`; AdsKit sends it to every tool. Firebase names are the master. GameAnalytics and Adjust never get names of their own, so the same funnel can be built in Firebase and in GameAnalytics.

**GameAnalytics** (`NineDTech.AdsKit.GameAnalytics`, automatic when the GA SDK is installed):

| Firebase | GameAnalytics |
| --- | --- |
| every event | design event, same name (e.g. `booster_used`); params as custom fields (data export only) |
| `level_start` | + progression Start `level_0001` |
| `level_end` | + progression Complete (Fail if `success` = 0) `level_0001` |
| `level_restart` / `level_quit` / `level_abandon` / `level_fail` | + progression Fail `level_0001`, field `reason` = `restart` / `quit` / `abandon` / the fail reason |
| `ad_show` / `ad_show_fail` / `ad_clicked` / `ad_rewarded` | + ad event (type from `ad_format`, network from `med`, `placement`) |
| `iap_purchase_success` | + business event: amount in cents, itemType = `item_type`, item = `product_id`, cart = `placement` |
| `ad_impression` / `adskit_impression` | not sent (ad revenue goes through the GA ad revenue plugin) |
| user property `ab_group` / `player_type` / `max_level` | + custom dimension 01 / 02 / 03 (all user properties are also global custom fields) |

- Level ids always have 4 digits (`level_0001`), so they sort correctly up to level 9999. Games with modes put the mode first: `{mode}_level_0001`.
- Type every custom dimension value into the GA settings asset before launch (`player_type`: `non_payer`, `payer`, `vip`; `max_level`: the buckets above; `ab_group`: each live variant). GA drops values that are not listed.

**Adjust** (`NineDTech.AdsKit.Adjust`): only the events mapped in `AttributionSettings.events` are sent, each under a token whose dashboard name is the Adjust name.

| Adjust | From |
| --- | --- |
| `tutorial_complete` | `tutorial_complete` |
| `level_5` … `level_50` | `level_milestone_5` … `level_milestone_50` |
| `purchase` / `first_purchase` / `remove_ads_purchase` | automatic (IapKit purchase, `purchaseEventToken` / `firstPurchaseEventToken` / `removeAdsPurchaseEventToken`) |
| ad revenue | automatic (`trackAdRevenue`) |

### Adding game-only events

1. Reuse first: add a parameter to a standard event before making a new one.
2. Stay at ≤ 25 parameters per event, common ones included.
3. Follow the naming rules; no game prefix.
4. List it on the game's own event page before it ships.
5. When a second game needs it, it moves into this standard under the same name.

Rotate Rings examples: `level_first_move`, `streak_break`, `vip_daily_boosters`, `rewarded_vip_skip`, `store_ad_offer_claim`.

### Code

```csharp
using NineDTech.AdsKit.Analytics;

LevelAnalytics.LevelStart(level, levelName, attempt, isReplay);

Analytics.Event("booster_used")
    .Add("booster", "bomb")
    .Add("level", level)
    .Add("source", "inventory")
    .Log();

Analytics.SetUserProperty("player_type", "payer");
Analytics.AddCommonParams(b => b.Add("lvl", maxLevel));
```

Never call `FirebaseAnalytics.LogEvent` directly. Template: Rotate Rings' `GameAnalyticsModule` (listens to game events, never changes play).

### Setup per game

1. Add `com.ninedtech.adskit` (and `com.ninedtech.iapkit` if the game sells anything) plus Firebase Analytics and Crashlytics.
2. Turn on the BigQuery export on day one.
3. Publish `analytics_config` in Remote Config: `{ "enabled": true, "disabled_events": [], "level_milestones": [1,2,3,5,10,20,30,50,100] }`.
4. Register GA4 custom dimensions `placement`, `ad_format`, `ad_source`, `blocked`, `med`, `level`, `booster`, `screen_name`, `source`, `product`, and metrics `duration_sec`, `moves`, `value`.

### Release checklist

- [ ] Firebase DebugView shows every standard event in one play-through (`adb shell setprop debug.firebase.analytics.app <package>`).
- [ ] Every event carries `sess`, `med`, `lvl`.
- [ ] User properties are set after the first session.
- [ ] `level_start` ≈ `level_end` + `level_restart` + `level_quit` + `level_abandon`.
- [ ] Per placement: `ad_opportunity` ≥ `ad_show` ≥ `ad_close`.
- [ ] Ad revenue shows in Firebase and Adjust.
- [ ] GameAnalytics Realtime shows the same design event names as Firebase DebugView, plus progression `level_0001`.
- [ ] A test purchase logs `iap_purchase_start` → `iap_purchase_success` with `value` and `currency`.
- [ ] No name breaks the limits or uses a reserved name.
