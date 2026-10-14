using System;
using Firebase.Extensions;
using Firebase.RemoteConfig;
using NineDTech.AdsKit.Remote;
using UnityEngine;

namespace NineDTech.AdsKit.Firebase
{
    /// <summary>
    /// Firebase Remote Config behind <see cref="IRemoteConfigSource"/>: one fetch-and-activate per launch
    /// (values activated in earlier sessions stay active offline), then real-time updates.
    /// </summary>
    public sealed class FirebaseRemoteConfigSource : IRemoteConfigSource
    {
        /// <summary>Release builds: minimum time between fetches. Real-time updates still arrive in between.</summary>
        public static long MinimumFetchIntervalMs = 60L * 60L * 1000L;

        private bool _listening;

        public string Name => "firebase";

        public event Action Updated;

        public void FetchAndActivate(Action<bool> onComplete)
        {
            FirebaseBootstrap.WhenCompleted(() =>
            {
                if (!FirebaseBootstrap.IsReady)
                {
                    onComplete?.Invoke(false);
                    return;
                }
                var rc = FirebaseRemoteConfig.DefaultInstance;
                var settings = new ConfigSettings
                {
                    // Development builds fetch every launch so console edits show up immediately.
                    MinimumFetchIntervalInMilliseconds = Debug.isDebugBuild ? 0UL : (ulong)MinimumFetchIntervalMs,
                    FetchTimeoutInMilliseconds = 10000UL
                };
                rc.SetConfigSettingsAsync(settings).ContinueWithOnMainThread(_ =>
                    rc.FetchAndActivateAsync().ContinueWithOnMainThread(task =>
                    {
                        var ok = !task.IsFaulted && !task.IsCanceled;
                        if (!ok)
                            KitLog.Warn("Remote Config fetch failed (" + rc.Info.LastFetchStatus + "); using previously activated values");
                        Listen(rc);
                        onComplete?.Invoke(ok);
                    }));
            });
        }

        public bool TryGetString(string key, out string value, out bool isRemote)
        {
            value = null;
            isRemote = false;
            if (!FirebaseBootstrap.IsReady) return false;
            var v = FirebaseRemoteConfig.DefaultInstance.GetValue(key);
            if (v.Source == ValueSource.StaticValue) return false;
            value = v.StringValue;
            isRemote = v.Source == ValueSource.RemoteValue;
            return true;
        }

        private void Listen(FirebaseRemoteConfig rc)
        {
            if (_listening) return;
            _listening = true;
            rc.OnConfigUpdateListener += (sender, args) =>
            {
                if (args.Error != RemoteConfigError.None)
                {
                    KitLog.Warn("Remote Config real-time error: " + args.Error);
                    return;
                }
                rc.ActivateAsync().ContinueWithOnMainThread(_ =>
                {
                    KitLog.Info("Remote Config real-time update activated");
                    Updated?.Invoke();
                });
            };
        }
    }
}
