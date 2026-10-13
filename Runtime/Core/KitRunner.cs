using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace NineDTech.AdsKit
{
    /// <summary>
    /// Hidden DontDestroyOnLoad driver shared by all AdsKit services: per-frame tick, app pause/quit,
    /// low-memory signal, delayed calls and a thread-safe main-thread queue (SDK callbacks may arrive
    /// on background threads; everything AdsKit does runs on the Unity main thread).
    /// </summary>
    public sealed class KitRunner : MonoBehaviour
    {
        private struct Timer
        {
            public double At;
            public Action Action;
        }

        private static KitRunner _instance;
        private static int _mainThreadId;
        private static readonly object QueueLock = new object();
        private static readonly List<Action> Pending = new List<Action>();
        private static readonly List<Timer> Timers = new List<Timer>();

        private readonly List<Action> _running = new List<Action>();
        private readonly List<Timer> _dueTimers = new List<Timer>();

        /// <summary>Unscaled delta time; fires every frame after queued actions ran.</summary>
        public static event Action<float> Tick;

        /// <summary>True when the app goes to the background, false when it returns.</summary>
        public static event Action<bool> PauseChanged;

        public static event Action LowMemory;
        public static event Action Quitting;

        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        /// <summary>Realtime clock (seconds) that ignores timeScale; use for every cooldown.</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            Tick = null;
            PauseChanged = null;
            LowMemory = null;
            Quitting = null;
            lock (QueueLock) Pending.Clear();
            Timers.Clear();
        }

        public static KitRunner Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("[AdsKit]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<KitRunner>();
            return _instance;
        }

        /// <summary>Queue an action for the next main-thread frame. Safe from any thread.</summary>
        public static void RunOnMainThread(Action action)
        {
            if (action == null) return;
            lock (QueueLock) Pending.Add(action);
        }

        /// <summary>Run immediately when already on the main thread, otherwise queue.</summary>
        public static void Dispatch(Action action)
        {
            if (action == null) return;
            if (IsMainThread) action();
            else RunOnMainThread(action);
        }

        /// <summary>Call <paramref name="action"/> after <paramref name="seconds"/> of real time (main thread only).</summary>
        public static void After(float seconds, Action action)
        {
            if (action == null) return;
            Ensure();
            Timers.Add(new Timer { At = Now + seconds, Action = action });
        }

        private void Awake()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            Application.lowMemory += OnLowMemory;
        }

        private void OnDestroy()
        {
            Application.lowMemory -= OnLowMemory;
            if (_instance == this) _instance = null;
        }

        private static void OnLowMemory() => LowMemory?.Invoke();

        private void Update()
        {
            lock (QueueLock)
            {
                _running.AddRange(Pending);
                Pending.Clear();
            }
            for (var i = 0; i < _running.Count; i++) SafeInvoke(_running[i]);
            _running.Clear();

            if (Timers.Count > 0)
            {
                var now = Now;
                for (var i = Timers.Count - 1; i >= 0; i--)
                {
                    if (Timers[i].At > now) continue;
                    _dueTimers.Add(Timers[i]);
                    Timers.RemoveAt(i);
                }
                for (var i = _dueTimers.Count - 1; i >= 0; i--) SafeInvoke(_dueTimers[i].Action);
                _dueTimers.Clear();
            }

            Tick?.Invoke(Time.unscaledDeltaTime);
        }

        private void OnApplicationPause(bool paused) => PauseChanged?.Invoke(paused);

        private void OnApplicationQuit() => Quitting?.Invoke();

        private static void SafeInvoke(Action action)
        {
            try { action(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
