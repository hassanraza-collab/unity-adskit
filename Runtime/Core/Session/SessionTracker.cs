using System;
using UnityEngine;

namespace NineDTech.AdsKit
{
    /// <summary>
    /// Counts sessions: every cold start, plus every return from the background after
    /// <see cref="TimeoutMinutes"/> or more. The count persists in PlayerPrefs.
    /// </summary>
    public static class SessionTracker
    {
        private const string CountKey = "adskit.session_count";
        private const string InstallKey = "adskit.install_utc";

        private static bool _started;
        private static DateTime? _pausedAtUtc;

        /// <summary>1-based session number of the current session (0 before <see cref="Begin"/>).</summary>
        public static int SessionCount { get; private set; }

        /// <summary><see cref="KitRunner.Now"/> when the current session started.</summary>
        public static double SessionStartTime { get; private set; }

        public static float TimeoutMinutes { get; set; } = 30f;

        public static double SecondsInSession => _started ? KitRunner.Now - SessionStartTime : 0;

        /// <summary>Days since the first launch with AdsKit (0 on install day).</summary>
        public static int DaysSinceInstall
        {
            get
            {
                var raw = PlayerPrefs.GetString(InstallKey, "");
                if (!long.TryParse(raw, out var ticks)) return 0;
                return Math.Max(0, (int)(DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalDays);
            }
        }

        /// <summary>Fires on every new session (cold start and timed-out resume) with the new count.</summary>
        public static event Action<int> SessionStarted;

        /// <summary>Fires when the app returns from the background, with the seconds spent away.</summary>
        public static event Action<double> Resumed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _started = false;
            _pausedAtUtc = null;
            SessionCount = 0;
            SessionStarted = null;
            Resumed = null;
        }

        /// <summary>Start counting. Safe to call more than once (later calls only update the timeout).</summary>
        public static void Begin(float timeoutMinutes)
        {
            TimeoutMinutes = timeoutMinutes;
            if (_started) return;
            _started = true;
            KitRunner.Ensure();
            KitRunner.PauseChanged += OnPauseChanged;
            if (!PlayerPrefs.HasKey(InstallKey))
                PlayerPrefs.SetString(InstallKey, DateTime.UtcNow.Ticks.ToString());
            StartNewSession();
        }

        private static void OnPauseChanged(bool paused)
        {
            if (paused)
            {
                _pausedAtUtc = DateTime.UtcNow;
                PlayerPrefs.Save();
                return;
            }
            if (!_pausedAtUtc.HasValue) return;
            var away = Math.Max(0, (DateTime.UtcNow - _pausedAtUtc.Value).TotalSeconds);
            _pausedAtUtc = null;
            if (away >= TimeoutMinutes * 60.0) StartNewSession();
            Resumed?.Invoke(away);
        }

        private static void StartNewSession()
        {
            SessionCount = PlayerPrefs.GetInt(CountKey, 0) + 1;
            PlayerPrefs.SetInt(CountKey, SessionCount);
            PlayerPrefs.Save();
            SessionStartTime = KitRunner.Now;
            KitLog.Info("Session " + SessionCount + " started");
            SessionStarted?.Invoke(SessionCount);
        }
    }
}
