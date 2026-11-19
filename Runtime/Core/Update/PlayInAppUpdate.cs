using System;
using UnityEngine;

namespace NineDTech.AdsKit.Update
{
    /// <summary>Outcome of a Google Play in-app update request (reported on the Unity main thread).</summary>
    public enum PlayUpdateResult
    {
        /// <summary>The player accepted (immediate: installing; flexible: downloading in the background).</summary>
        Accepted,
        /// <summary>The player closed Play's update screen.</summary>
        Canceled,
        /// <summary>Play reported the flow failed.</summary>
        Failed,
        /// <summary>Play has no update for this install (not newer on the track, not installed from Play, or the type is not allowed).</summary>
        NotAvailable,
        /// <summary>A flexible update is already downloaded and waits for <see cref="PlayInAppUpdate.CompleteUpdate"/>.</summary>
        Downloaded,
        /// <summary>Play Core missing or threw (no app-update library, no Play Store, editor).</summary>
        Error
    }

    /// <summary>
    /// Thin JNI bridge to Google Play In-App Updates (com.google.android.play:app-update 2.x, added by
    /// Editor/AdsKitAppUpdateDependencies.xml through the External Dependency Manager). Android device builds only;
    /// everywhere else <see cref="IsSupported"/> is false. Used by <see cref="AppUpdate"/> when the remote
    /// <c>update_ui</c> is "google".
    /// </summary>
    public static class PlayInAppUpdate
    {
        // AppUpdateType / UpdateAvailability / InstallStatus / activity result constants from the Play Core API.
        private const int TypeFlexible = 0, TypeImmediate = 1;
        private const int AvailabilityAvailable = 2, AvailabilityInProgress = 3;
        private const int StatusDownloaded = 11;
        private const int ResultOk = -1, ResultCanceled = 0;

        /// <summary>Install status changes of a flexible update (InstallStatus int, e.g. 2 downloading, 11 downloaded).</summary>
#pragma warning disable CS0414 // only raised on Android device builds
        public static event Action<int> InstallStatusChanged;
#pragma warning restore CS0414

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            InstallStatusChanged = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            _manager = null;
            _supported = null;
            _listener = null;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject _manager;
        private static bool? _supported;
        private static StateListener _listener;

        public static bool IsSupported
        {
            get
            {
                if (_supported.HasValue) return _supported.Value;
                try { _supported = Manager != null; }
                catch (Exception ex)
                {
                    KitLog.Warn("PlayInAppUpdate: Play Core app-update not available (" + ex.Message + ")");
                    _supported = false;
                }
                return _supported.Value;
            }
        }

        private static AndroidJavaObject Activity
        {
            get
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    return player.GetStatic<AndroidJavaObject>("currentActivity");
            }
        }

        private static AndroidJavaObject Manager
        {
            get
            {
                if (_manager != null) return _manager;
                using (var factory = new AndroidJavaClass("com.google.android.play.core.appupdate.AppUpdateManagerFactory"))
                    _manager = factory.CallStatic<AndroidJavaObject>("create", Activity);
                return _manager;
            }
        }

        /// <summary>
        /// Asks Play for the update and, if one of the requested type is allowed, starts Play's update screen.
        /// <paramref name="onLaunched"/> runs when Play's screen is about to open; <paramref name="onDone"/> always runs once.
        /// </summary>
        public static void StartFlow(bool immediate, Action onLaunched, Action<PlayUpdateResult> onDone)
        {
            void Finish(PlayUpdateResult r) => KitRunner.RunOnMainThread(() => onDone?.Invoke(r));
            if (!IsSupported) { Finish(PlayUpdateResult.Error); return; }
            try
            {
                EnsureListener();
                var task = Manager.Call<AndroidJavaObject>("getAppUpdateInfo");
                task.Call<AndroidJavaObject>("addOnSuccessListener", new SuccessListener(info =>
                {
                    try
                    {
                        int availability = info.Call<int>("updateAvailability");
                        int status = info.Call<int>("installStatus");
                        int type = immediate ? TypeImmediate : TypeFlexible;
                        if (!immediate && status == StatusDownloaded) { Finish(PlayUpdateResult.Downloaded); return; }
                        bool canStart = info.Call<bool>("isUpdateTypeAllowed", type)
                            && (availability == AvailabilityAvailable || (immediate && availability == AvailabilityInProgress));
                        if (!canStart) { Finish(PlayUpdateResult.NotAvailable); return; }

                        KitRunner.RunOnMainThread(() => onLaunched?.Invoke());
                        AndroidJavaObject options;
                        using (var optionsClass = new AndroidJavaClass("com.google.android.play.core.appupdate.AppUpdateOptions"))
                            options = optionsClass.CallStatic<AndroidJavaObject>("defaultOptions", type);
                        var flow = Manager.Call<AndroidJavaObject>("startUpdateFlow", info, Activity, options);
                        flow.Call<AndroidJavaObject>("addOnSuccessListener", new SuccessListener(code =>
                        {
                            int result = code != null ? code.Call<int>("intValue") : ResultCanceled;
                            Finish(result == ResultOk ? PlayUpdateResult.Accepted
                                : result == ResultCanceled ? PlayUpdateResult.Canceled
                                : PlayUpdateResult.Failed);
                        }));
                        flow.Call<AndroidJavaObject>("addOnFailureListener", new FailureListener(e =>
                        {
                            KitLog.Warn("PlayInAppUpdate: update flow failed (" + e + ")");
                            Finish(PlayUpdateResult.Failed);
                        }));
                    }
                    catch (Exception ex)
                    {
                        KitLog.Warn("PlayInAppUpdate: " + ex.Message);
                        Finish(PlayUpdateResult.Error);
                    }
                }));
                task.Call<AndroidJavaObject>("addOnFailureListener", new FailureListener(e =>
                {
                    // Typical: ERROR_APP_NOT_OWNED / API_NOT_AVAILABLE (not installed from Play, no Play Store).
                    KitLog.Warn("PlayInAppUpdate: getAppUpdateInfo failed (" + e + ")");
                    Finish(PlayUpdateResult.NotAvailable);
                }));
            }
            catch (Exception ex)
            {
                KitLog.Warn("PlayInAppUpdate: " + ex.Message);
                Finish(PlayUpdateResult.Error);
            }
        }

        /// <summary>Installs a downloaded flexible update. Play shows its install screen and restarts the app.</summary>
        public static void CompleteUpdate()
        {
            if (!IsSupported) return;
            try { Manager.Call<AndroidJavaObject>("completeUpdate"); }
            catch (Exception ex) { KitLog.Warn("PlayInAppUpdate: completeUpdate failed (" + ex.Message + ")"); }
        }

        private static void EnsureListener()
        {
            if (_listener != null) return;
            _listener = new StateListener();
            Manager.Call("registerListener", _listener);
        }

        private sealed class SuccessListener : AndroidJavaProxy
        {
            private readonly Action<AndroidJavaObject> _onSuccess;
            public SuccessListener(Action<AndroidJavaObject> onSuccess) : base("com.google.android.gms.tasks.OnSuccessListener") => _onSuccess = onSuccess;
            [UnityEngine.Scripting.Preserve]
            public void onSuccess(AndroidJavaObject result) => _onSuccess(result);
        }

        private sealed class FailureListener : AndroidJavaProxy
        {
            private readonly Action<string> _onFailure;
            public FailureListener(Action<string> onFailure) : base("com.google.android.gms.tasks.OnFailureListener") => _onFailure = onFailure;
            [UnityEngine.Scripting.Preserve]
            public void onFailure(AndroidJavaObject exception) =>
                _onFailure(exception != null ? exception.Call<string>("toString") : "unknown");
        }

        private sealed class StateListener : AndroidJavaProxy
        {
            public StateListener() : base("com.google.android.play.core.install.InstallStateUpdatedListener") { }
            [UnityEngine.Scripting.Preserve]
            public void onStateUpdate(AndroidJavaObject state)
            {
                int status = state != null ? state.Call<int>("installStatus") : 0;
                KitRunner.RunOnMainThread(() => InstallStatusChanged?.Invoke(status));
            }
        }
#else
        /// <summary>False outside Android device builds.</summary>
        public static bool IsSupported => false;

        public static void StartFlow(bool immediate, Action onLaunched, Action<PlayUpdateResult> onDone) =>
            KitRunner.RunOnMainThread(() => onDone?.Invoke(PlayUpdateResult.Error));

        public static void CompleteUpdate() { }
#endif
    }
}
