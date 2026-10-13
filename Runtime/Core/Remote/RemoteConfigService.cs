using System;
using System.Collections.Generic;
using UnityEngine;

namespace NineDTech.AdsKit.Remote
{
    public enum RemoteFetchState
    {
        NotStarted,
        Fetching,
        /// <summary>The source answered (fresh or cached values active).</summary>
        Completed,
        /// <summary>The timeout passed first; a late answer still activates and raises <see cref="RemoteConfigService.Activated"/>.</summary>
        TimedOut,
        Failed
    }

    /// <summary>
    /// One shared fetch for every AdsKit consumer (ads, analytics, the game). Backends register a factory
    /// (the Firebase assembly does this automatically); without one a <see cref="LocalRemoteConfigSource"/> is used.
    /// </summary>
    public static class RemoteConfigService
    {
        private static Func<IRemoteConfigSource> _factory;
        private static IRemoteConfigSource _source;
        private static readonly List<Action<RemoteFetchState>> Waiting = new List<Action<RemoteFetchState>>();
        private static double _fetchStartedAt;

        public static RemoteFetchState State { get; private set; }

        /// <summary>Milliseconds the first fetch took (or until it timed out).</summary>
        public static double FetchMilliseconds { get; private set; }

        /// <summary>Raised on the main thread after the first activation and after every later update.</summary>
        public static event Action Activated;

        public static IRemoteConfigSource Source
        {
            get
            {
                if (_source != null) return _source;
                _source = _factory != null ? _factory() : new LocalRemoteConfigSource();
                _source.Updated += OnSourceUpdated;
                return _source;
            }
        }

        /// <summary>The local source when no backend is active (Editor testing); otherwise null.</summary>
        public static LocalRemoteConfigSource Local => Source as LocalRemoteConfigSource;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _source = null;
            State = RemoteFetchState.NotStarted;
            FetchMilliseconds = 0;
            Waiting.Clear();
            Activated = null;
        }

        /// <summary>Called by backend assemblies (e.g. from a RuntimeInitializeOnLoadMethod).</summary>
        public static void RegisterSourceFactory(Func<IRemoteConfigSource> factory)
        {
            _factory = factory;
        }

        /// <summary>Force a specific source (tests, Editor). Must be called before the first fetch.</summary>
        public static void OverrideSource(IRemoteConfigSource source)
        {
            if (_source != null) _source.Updated -= OnSourceUpdated;
            _source = source;
            if (_source != null) _source.Updated += OnSourceUpdated;
        }

        /// <summary>
        /// Fetch once per launch. <paramref name="onDone"/> runs on the main thread when the source answers
        /// or <paramref name="timeoutSec"/> passes, whichever is first. Later callers get the stored state.
        /// </summary>
        public static void Fetch(float timeoutSec, Action<RemoteFetchState> onDone)
        {
            switch (State)
            {
                case RemoteFetchState.Completed:
                case RemoteFetchState.TimedOut:
                case RemoteFetchState.Failed:
                    onDone?.Invoke(State);
                    return;
            }
            if (onDone != null) Waiting.Add(onDone);
            if (State == RemoteFetchState.Fetching) return;

            State = RemoteFetchState.Fetching;
            _fetchStartedAt = KitRunner.Now;
            KitRunner.Ensure();
            KitRunner.After(timeoutSec, () =>
            {
                if (State != RemoteFetchState.Fetching) return;
                KitLog.Warn("Remote Config fetch timed out after " + timeoutSec + "s; using cached/default values for now");
                Finish(RemoteFetchState.TimedOut);
            });

            var source = Source;
            KitLog.Info("Remote Config fetch started (" + source.Name + ")");
            source.FetchAndActivate(ok => KitRunner.Dispatch(() =>
            {
                if (State == RemoteFetchState.Fetching)
                {
                    Finish(ok ? RemoteFetchState.Completed : RemoteFetchState.Failed);
                }
                else if (ok)
                {
                    // Late answer after the timeout: values are active now.
                    KitLog.Info("Remote Config arrived late; re-applying");
                    Activated?.Invoke();
                }
            }));
        }

        public static string GetString(string key, string fallback = null)
        {
            return Source.TryGetString(key, out var v, out _) && !string.IsNullOrEmpty(v) ? v : fallback;
        }

        public static bool TryGetRemoteString(string key, out string value)
        {
            return Source.TryGetString(key, out value, out var isRemote) && isRemote && !string.IsNullOrEmpty(value);
        }

        private static void Finish(RemoteFetchState state)
        {
            State = state;
            FetchMilliseconds = (KitRunner.Now - _fetchStartedAt) * 1000.0;
            KitLog.Info("Remote Config " + state + " in " + Mathf.RoundToInt((float)FetchMilliseconds) + " ms");
            Analytics.Analytics.Event(Analytics.KitEvents.RemoteConfigReady)
                .Add("result", state.ToString().ToLowerInvariant())
                .Add("source", Source.Name)
                .Add("fetch_ms", (long)FetchMilliseconds)
                .Log();
            var callbacks = Waiting.ToArray();
            Waiting.Clear();
            foreach (var cb in callbacks)
            {
                try { cb(state); }
                catch (Exception e) { Debug.LogException(e); }
            }
            Activated?.Invoke();
        }

        private static void OnSourceUpdated() => KitRunner.Dispatch(() => Activated?.Invoke());
    }
}
