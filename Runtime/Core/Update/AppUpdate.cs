using System;
using NineDTech.AdsKit.Remote;
using UnityEngine;

namespace NineDTech.AdsKit.Update
{
    public enum AppUpdateKind { None, Soft, Hard }

    /// <summary>
    /// Remote JSON shape of <see cref="AppUpdate.RemoteKey"/>; field names are the JSON keys. Missing fields keep the
    /// defaults passed to <see cref="AppUpdate.Initialize"/>.
    /// </summary>
    [Serializable]
    public sealed class AppUpdateConfig
    {
        [Tooltip("Master switch. Off = never prompt.")]
        public bool enabled = false;
        [Tooltip("Builds below this version get the soft (dismissible) prompt, e.g. 1.4.0. Empty = no soft prompt.")]
        public string latest_version = "";
        [Tooltip("Builds below this version get the hard (blocking) prompt, e.g. 1.2.0. Empty = no hard prompt.")]
        public string min_version = "";
        [Tooltip("Hours between soft prompts.")]
        public float soft_cooldown_hours = 24f;
        [Tooltip("Soft prompts per latest_version (0 = unlimited, still respecting the cooldown).")]
        public int soft_max_shows = 3;
        [Tooltip("Store page. Empty on Android = market://details?id=<bundle id>. iOS needs it: itms-apps://apps.apple.com/app/id<APPID>.")]
        public string store_url = "";
        [Tooltip("Optional message override (not translated). Empty = the game's localized default.")]
        public string soft_message = "";
        public string hard_message = "";

        public AppUpdateConfig Clone() => (AppUpdateConfig)MemberwiseClone();
    }

    /// <summary>One prompt on screen. The presenter shows it and reports the player's choice back.</summary>
    public sealed class AppUpdatePrompt
    {
        public AppUpdateKind Kind { get; }
        /// <summary>Remote message override, or null: show the game's own (localized) text.</summary>
        public string CustomMessage { get; }
        public string CurrentVersion { get; }
        public string TargetVersion { get; }
        /// <summary>Opened from a debug gallery: choices only close it (no store, no analytics, no limits).</summary>
        public bool IsPreview { get; }
        /// <summary>Hard prompts must not offer a close / later option.</summary>
        public bool CanDismiss => Kind == AppUpdateKind.Soft;

        internal bool Done;

        internal AppUpdatePrompt(AppUpdateKind kind, string customMessage, string target, bool preview)
        {
            Kind = kind;
            CustomMessage = string.IsNullOrWhiteSpace(customMessage) ? null : customMessage;
            CurrentVersion = Application.version;
            TargetVersion = target ?? "";
            IsPreview = preview;
        }

        /// <summary>"Update" pressed: opens the store. A soft prompt closes; a hard one stays on screen.</summary>
        public void ChooseUpdate() => AppUpdate.OnChoice(this, true);

        /// <summary>"Later", the X, the backdrop or Back on a soft prompt. Ignored for hard prompts.</summary>
        public void ChooseLater() => AppUpdate.OnChoice(this, false);
    }

    /// <summary>The game's popup. AdsKit decides when; the presenter only draws and reports choices.</summary>
    public interface IAppUpdatePresenter
    {
        /// <summary>Still on screen (including a closing animation). AdsKit waits until false before showing another.</summary>
        bool IsShowing { get; }
        /// <summary>Show the prompt. Hard: no close button, Back does nothing, keep it above every other popup.</summary>
        void Show(AppUpdatePrompt prompt);
        /// <summary>Close it without a choice (requirement removed remotely, or a soft prompt escalating to hard).</summary>
        void Hide();
    }

    /// <summary>
    /// Release management: soft and hard (forced) update prompts from Firebase Remote Config parameter
    /// <see cref="RemoteKey"/> (JSON, see <see cref="AppUpdateConfig"/>). Use Remote Config platform conditions for
    /// different Android / iOS values.
    ///  - Hard: Application.version &lt; min_version. Shown as soon as known; Update opens the store and the prompt stays.
    ///  - Soft: Application.version &lt; latest_version. Shown only when <see cref="CanShowSoft"/> allows (e.g. home screen
    ///    with no other popup), once per session, at most soft_max_shows times per latest_version, cooldown apart.
    /// Remote values fetched mid-session apply on the next check. Analytics: app_update_prompt (type, choice, app_version,
    /// target_version). Call <see cref="Initialize"/> once with the game's presenter.
    /// </summary>
    public static class AppUpdate
    {
        public const string RemoteKey = "app_update_config";

        private const string PrefsShows = "adskit.update.soft_shows";     // count for PrefsVersion
        private const string PrefsVersion = "adskit.update.soft_version"; // latest_version the count belongs to
        private const string PrefsLast = "adskit.update.soft_last";       // unix seconds of the last soft prompt

        private static IAppUpdatePresenter _presenter;
        private static AppUpdateConfig _defaults = new AppUpdateConfig();
        private static AppUpdateConfig _config;
        private static string _json;
        private static AppUpdatePrompt _current;
        private static double _nextCheck;
        private static bool _softShownThisSession;
        private static bool _initialized;

        /// <summary>Seconds between checks.</summary>
        public static float CheckInterval = 1f;

        /// <summary>
        /// Game gate for soft prompts (e.g. home screen shown and no popup open). Null = always allowed.
        /// Hard prompts ignore it.
        /// </summary>
        public static Func<bool> CanShowSoft;

        /// <summary>
        /// Optional raw JSON source (e.g. a game wrapper with QA overrides). Return null to fall back to
        /// <see cref="RemoteConfigService"/>.
        /// </summary>
        public static Func<string, string> ConfigSource;

        /// <summary>Raised right before the store opens (Ads uses it to skip the app-open ad on return).</summary>
        public static event Action OpeningStore;

        /// <summary>Raised for every analytics-worthy step: kind, choice ("shown" / "update" / "later").</summary>
        public static event Action<AppUpdateKind, string> PromptEvent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _presenter = null;
            _defaults = new AppUpdateConfig();
            _config = null;
            _json = null;
            _current = null;
            _nextCheck = 0;
            _softShownThisSession = false;
            _initialized = false;
            CheckInterval = 1f;
            CanShowSoft = null;
            ConfigSource = null;
            OpeningStore = null;
            PromptEvent = null;
        }

        public static bool IsInitialized => _initialized;

        /// <summary>A prompt is on screen.</summary>
        public static bool Showing => _current != null;

        /// <summary>Asset / code defaults overlaid with the remote JSON.</summary>
        public static AppUpdateConfig Config
        {
            get
            {
                string json = ConfigSource?.Invoke(RemoteKey);
                if (string.IsNullOrWhiteSpace(json)) json = RemoteConfigService.GetString(RemoteKey);
                if (_config != null && json == _json) return _config;
                _json = json;
                _config = _defaults.Clone();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    try { JsonUtility.FromJsonOverwrite(json, _config); }
                    catch (Exception ex)
                    {
                        KitLog.Warn($"AppUpdate: bad {RemoteKey} JSON, using defaults ({ex.Message})");
                        _config = _defaults.Clone();
                    }
                }
                return _config;
            }
        }

        /// <summary>What this build needs right now according to remote config.</summary>
        public static AppUpdateKind Required
        {
            get
            {
                var cfg = Config;
                if (!cfg.enabled) return AppUpdateKind.None;
                if (IsOlder(Application.version, cfg.min_version)) return AppUpdateKind.Hard;
                if (IsOlder(Application.version, cfg.latest_version)) return AppUpdateKind.Soft;
                return AppUpdateKind.None;
            }
        }

        /// <summary>Start checking. Call once, after the presenter's UI can open popups.</summary>
        public static void Initialize(IAppUpdatePresenter presenter, AppUpdateConfig defaults = null)
        {
            if (presenter == null) { KitLog.Error("AppUpdate.Initialize: presenter is null"); return; }
            _presenter = presenter;
            _defaults = defaults != null ? defaults.Clone() : new AppUpdateConfig();
            _config = null;
            if (_initialized) return;
            _initialized = true;
            _nextCheck = KitRunner.Now + 1.0; // let the first frame settle
            KitRunner.Ensure();
            KitRunner.Tick += OnTick;
        }

        /// <summary>Debug gallery: shows the prompt regardless of versions or the remote switch; any choice closes it.</summary>
        public static void ShowPreview(AppUpdateKind kind)
        {
            if (_presenter == null || _current != null || _presenter.IsShowing || kind == AppUpdateKind.None) return;
            var cfg = Config;
            Present(new AppUpdatePrompt(kind, null, kind == AppUpdateKind.Hard ? cfg.min_version : cfg.latest_version, true));
        }

        /// <summary>Opens the store page from <see cref="AppUpdateConfig.store_url"/> (Android falls back to market://).</summary>
        public static void OpenStore()
        {
            var url = Config.store_url;
#if !UNITY_IOS
            if (string.IsNullOrWhiteSpace(url)) url = "market://details?id=" + Application.identifier;
#endif
            if (string.IsNullOrWhiteSpace(url))
            {
                KitLog.Warn($"AppUpdate: no store_url in {RemoteKey} (required on iOS).");
                return;
            }
            OpeningStore?.Invoke();
            Application.OpenURL(url);
        }

        private static void OnTick(float _)
        {
            if (_presenter == null || KitRunner.Now < _nextCheck) return;
            _nextCheck = KitRunner.Now + Mathf.Max(0.25f, CheckInterval);

            if (_current != null)
            {
                if (_current.IsPreview) return;
                var need = Required;
                // Remote changed while showing: a hard prompt no longer required closes; a soft one escalates to hard
                // (the hard prompt opens on a later check, once the presenter has closed this one).
                if (_current.Kind == AppUpdateKind.Hard && need != AppUpdateKind.Hard) Dismiss();
                else if (_current.Kind == AppUpdateKind.Soft && need == AppUpdateKind.Hard) Dismiss();
                return;
            }
            if (_presenter.IsShowing) return;

            var required = Required;
            var cfg = Config;
            if (required == AppUpdateKind.Hard)
            {
                Present(new AppUpdatePrompt(AppUpdateKind.Hard, cfg.hard_message, cfg.min_version, false));
            }
            else if (required == AppUpdateKind.Soft && SoftAllowed(cfg))
            {
                if (Present(new AppUpdatePrompt(AppUpdateKind.Soft, cfg.soft_message, cfg.latest_version, false)))
                    RecordSoftShow(cfg);
            }
        }

        private static bool Present(AppUpdatePrompt prompt)
        {
            _current = prompt;
            _presenter.Show(prompt);
            if (!_presenter.IsShowing)
            {
                // The game could not open it (e.g. UI not ready): try again on a later check.
                prompt.Done = true;
                if (_current == prompt) _current = null;
                return false;
            }
            if (!prompt.IsPreview) Log(prompt, "shown");
            return true;
        }

        private static void Dismiss()
        {
            if (_current == null) return;
            _current.Done = true;
            _current = null;
            _presenter?.Hide();
        }

        internal static void OnChoice(AppUpdatePrompt prompt, bool update)
        {
            if (prompt == null || prompt.Done) return;
            if (prompt.IsPreview) { if (prompt == _current) Dismiss(); else prompt.Done = true; return; }
            if (!update && prompt.Kind == AppUpdateKind.Hard) return; // hard prompts can't be dismissed

            Log(prompt, update ? "update" : "later");
            if (update) OpenStore();
            if (prompt.Kind == AppUpdateKind.Soft)
            {
                if (prompt == _current) Dismiss();
                else prompt.Done = true;
            }
        }

        private static bool SoftAllowed(AppUpdateConfig cfg)
        {
            if (_softShownThisSession) return false;
            if (CanShowSoft != null && !CanShowSoft()) return false;
            var version = cfg.latest_version ?? "";
            if (PlayerPrefs.GetString(PrefsVersion, "") != version) return true; // new target version: fresh count
            if (cfg.soft_max_shows > 0 && PlayerPrefs.GetInt(PrefsShows, 0) >= cfg.soft_max_shows) return false;
            var last = long.TryParse(PlayerPrefs.GetString(PrefsLast, "0"), out var l) ? l : 0L;
            return UnixNow() - last >= (long)(Mathf.Max(0f, cfg.soft_cooldown_hours) * 3600f);
        }

        private static void RecordSoftShow(AppUpdateConfig cfg)
        {
            _softShownThisSession = true;
            var version = cfg.latest_version ?? "";
            int shows = PlayerPrefs.GetString(PrefsVersion, "") == version ? PlayerPrefs.GetInt(PrefsShows, 0) : 0;
            PlayerPrefs.SetString(PrefsVersion, version);
            PlayerPrefs.SetInt(PrefsShows, shows + 1);
            PlayerPrefs.SetString(PrefsLast, UnixNow().ToString());
            PlayerPrefs.Save();
        }

        private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static void Log(AppUpdatePrompt prompt, string choice)
        {
            PromptEvent?.Invoke(prompt.Kind, choice);
            Analytics.Analytics.Event("app_update_prompt")
                .Add("type", prompt.Kind == AppUpdateKind.Hard ? "hard" : "soft")
                .Add("choice", choice)
                .Add("app_version", prompt.CurrentVersion)
                .Add("target_version", prompt.TargetVersion)
                .Log();
        }

        /// <summary>
        /// True when <paramref name="current"/> is lower than <paramref name="target"/>. Dotted numeric versions
        /// (1.2.10 &gt; 1.2.9); missing parts count as 0; non-digit suffixes are ignored. Empty target = false.
        /// </summary>
        public static bool IsOlder(string current, string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return false;
            var a = Parse(current);
            var b = Parse(target);
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
                if (x != y) return x < y;
            }
            return false;
        }

        private static int[] Parse(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return Array.Empty<int>();
            var parts = v.Trim().Split('.');
            var r = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                int k = 0, value = 0;
                var p = parts[i];
                while (k < p.Length && char.IsDigit(p[k])) { value = value * 10 + (p[k] - '0'); k++; }
                r[i] = value;
            }
            return r;
        }
    }
}
