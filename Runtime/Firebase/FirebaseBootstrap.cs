using System;
using Firebase;
using Firebase.Extensions;
using NineDTech.AdsKit.Remote;
using UnityEngine;

namespace NineDTech.AdsKit.Firebase
{
    /// <summary>
    /// Compiled only when the Firebase SDK is installed (ADSKIT_FIREBASE, set by the AdsKit SDK detector).
    /// Registers the Firebase Remote Config source and Analytics sink before the first scene loads, then runs
    /// the Firebase dependency check. Events logged earlier are queued until <see cref="IsReady"/>.
    /// </summary>
    public static class FirebaseBootstrap
    {
        private static bool _checking;

        public static bool IsReady { get; private set; }
        public static bool Failed { get; private set; }

        /// <summary>Raised on the main thread once Firebase is usable (or failed: check <see cref="Failed"/>).</summary>
        public static event Action Completed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _checking = false;
            IsReady = false;
            Failed = false;
            Completed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            RemoteConfigService.RegisterSourceFactory(() => new FirebaseRemoteConfigSource());
            Analytics.Analytics.AddSink(new FirebaseAnalyticsSink());
            Initialize();
        }

        /// <summary>Starts the dependency check (idempotent). Called automatically at startup.</summary>
        public static void Initialize()
        {
            if (_checking || IsReady) return;
            _checking = true;
            KitRunner.Ensure();
            var started = KitRunner.Now;
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                _checking = false;
                var status = task.IsFaulted || task.IsCanceled ? DependencyStatus.UnavailableOther : task.Result;
                if (status == DependencyStatus.Available)
                {
                    IsReady = true;
#if ADSKIT_FIREBASE_CRASHLYTICS
                    global::Firebase.Crashlytics.Crashlytics.ReportUncaughtExceptionsAsFatal = true;
#endif
                    KitLog.Info("Firebase ready in " + Mathf.RoundToInt((float)((KitRunner.Now - started) * 1000)) + " ms");
                }
                else
                {
                    Failed = true;
                    KitLog.Error("Firebase unavailable: " + status + (task.Exception != null ? " " + task.Exception.GetBaseException().Message : ""));
                }
                Completed?.Invoke();
            });
        }

        /// <summary>Run <paramref name="action"/> once Firebase finished its check (immediately if already done).</summary>
        public static void WhenCompleted(Action action)
        {
            if (IsReady || Failed)
            {
                action();
                return;
            }
            Completed += action;
            Initialize();
        }
    }
}
