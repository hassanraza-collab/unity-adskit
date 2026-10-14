using System;
using System.Collections.Generic;
using UnityEngine;

namespace NineDTech.AdsKit.Analytics
{
    /// <summary>
    /// Analytics entry point. Validates names against Firebase limits, adds common parameters,
    /// fans events out to every <see cref="IAnalyticsSink"/> and queues them per sink until it is ready.
    /// Call from the main thread; calls from other threads are re-dispatched.
    /// </summary>
    public static class Analytics
    {
        private struct QueuedEvent
        {
            public string Name;
            public AnalyticsParam[] Params;
        }

        private sealed class SinkSlot
        {
            public IAnalyticsSink Sink;
            public readonly List<QueuedEvent> Queue = new List<QueuedEvent>();
            public readonly Dictionary<string, string> PendingProperties = new Dictionary<string, string>();
        }

        private const int MaxQueuedPerSink = 300;

        private static readonly List<SinkSlot> Slots = new List<SinkSlot>();
        private static readonly Dictionary<string, string> UserProperties = new Dictionary<string, string>();
        private static readonly HashSet<string> DisabledEvents = new HashSet<string>();
        private static readonly List<Action<AnalyticsEventBuilder>> Enrichers = new List<Action<AnalyticsEventBuilder>>();
        private static readonly Stack<AnalyticsEventBuilder> Pool = new Stack<AnalyticsEventBuilder>();
        private static bool _tickHooked;

        /// <summary>Master switch (remote or settings). When false, events are dropped.</summary>
        public static bool Enabled { get; set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Slots.Clear();
            UserProperties.Clear();
            DisabledEvents.Clear();
            Enrichers.Clear();
            Pool.Clear();
            _tickHooked = false;
            Enabled = true;
        }

        public static void AddSink(IAnalyticsSink sink)
        {
            if (sink == null) return;
            RemoveSink(sink.Id);
            var slot = new SinkSlot { Sink = sink };
            foreach (var pair in UserProperties) slot.PendingProperties[pair.Key] = pair.Value;
            Slots.Add(slot);
            HookTick();
            KitLog.Info("Analytics sink added: " + sink.Id);
        }

        public static void RemoveSink(string id)
        {
            for (var i = Slots.Count - 1; i >= 0; i--)
                if (Slots[i].Sink.Id == id) Slots.RemoveAt(i);
        }

        public static bool HasSink(string id)
        {
            foreach (var slot in Slots)
                if (slot.Sink.Id == id) return true;
            return false;
        }

        /// <summary>Runs on every event before dispatch; use <see cref="AnalyticsEventBuilder.AddIfMissing"/>.</summary>
        public static void AddCommonParams(Action<AnalyticsEventBuilder> enricher)
        {
            if (enricher != null && !Enrichers.Contains(enricher)) Enrichers.Add(enricher);
        }

        public static void RemoveCommonParams(Action<AnalyticsEventBuilder> enricher) => Enrichers.Remove(enricher);

        public static void SetDisabledEvents(IEnumerable<string> names)
        {
            DisabledEvents.Clear();
            if (names == null) return;
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n)) DisabledEvents.Add(n);
        }

        public static bool IsEventEnabled(string name) => Enabled && !DisabledEvents.Contains(name);

        public static AnalyticsEventBuilder Event(string name)
        {
            // The pool is main-thread only; SDK callbacks on other threads get a fresh builder.
            var builder = KitRunner.IsMainThread && Pool.Count > 0 ? Pool.Pop() : new AnalyticsEventBuilder();
            builder.Begin(name);
            return builder;
        }

        public static void Log(string name) => Event(name).Log();

        public static void SetUserProperty(string name, string value)
        {
            if (!KitRunner.IsMainThread)
            {
                KitRunner.RunOnMainThread(() => SetUserProperty(name, value));
                return;
            }
            if (!AnalyticsNames.IsValidIdentifier(name, AnalyticsNames.MaxUserPropertyNameLength))
            {
                KitLog.Error("Analytics: invalid user property name '" + name + "' (<= 24 chars)");
                return;
            }
            if (value != null && value.Length > AnalyticsNames.MaxUserPropertyValueLength)
                value = value.Substring(0, AnalyticsNames.MaxUserPropertyValueLength);
            if (UserProperties.TryGetValue(name, out var current) && current == value) return;
            UserProperties[name] = value;
            foreach (var slot in Slots)
            {
                if (slot.Sink.IsReady) SafeSetProperty(slot.Sink, name, value);
                else slot.PendingProperties[name] = value;
            }
        }

        internal static void Dispatch(AnalyticsEventBuilder builder)
        {
            // Hop first: enrichers (game common params) may use Unity APIs such as PlayerPrefs.
            if (!KitRunner.IsMainThread)
            {
                KitRunner.RunOnMainThread(() => Dispatch(builder));
                return;
            }
            var name = builder.Name;
            if (!IsEventEnabled(name)) { Release(builder); return; }

            var error = AnalyticsNames.ValidateEventName(name);
            if (error != null)
            {
                KitLog.Error("Analytics: " + error);
                Release(builder);
                return;
            }

            for (var i = 0; i < Enrichers.Count; i++)
            {
                try { Enrichers[i](builder); }
                catch (Exception e) { Debug.LogException(e); }
            }

            if (builder.Params.Count > AnalyticsNames.MaxParamsPerEvent)
            {
                KitLog.Warn("Analytics: " + name + " has " + builder.Params.Count + " params; keeping the first 25");
                builder.Params.RemoveRange(AnalyticsNames.MaxParamsPerEvent, builder.Params.Count - AnalyticsNames.MaxParamsPerEvent);
            }

            var parameters = builder.Params.ToArray();
            Release(builder);
            Send(name, parameters);
        }

        private static void Send(string name, AnalyticsParam[] parameters)
        {
            foreach (var slot in Slots)
            {
                if (slot.Sink.IsReady && slot.Queue.Count == 0)
                {
                    SafeLog(slot.Sink, name, parameters);
                    continue;
                }
                if (slot.Queue.Count >= MaxQueuedPerSink) slot.Queue.RemoveAt(0);
                slot.Queue.Add(new QueuedEvent { Name = name, Params = parameters });
            }
        }

        private static void Release(AnalyticsEventBuilder builder)
        {
            builder.Params.Clear();
            if (Pool.Count < 8) Pool.Push(builder);
        }

        private static void HookTick()
        {
            if (_tickHooked) return;
            _tickHooked = true;
            KitRunner.Ensure();
            KitRunner.Tick += Flush;
        }

        private static void Flush(float _)
        {
            for (var s = 0; s < Slots.Count; s++)
            {
                var slot = Slots[s];
                if (!slot.Sink.IsReady) continue;
                if (slot.PendingProperties.Count > 0)
                {
                    foreach (var pair in slot.PendingProperties) SafeSetProperty(slot.Sink, pair.Key, pair.Value);
                    slot.PendingProperties.Clear();
                }
                if (slot.Queue.Count == 0) continue;
                foreach (var e in slot.Queue) SafeLog(slot.Sink, e.Name, e.Params);
                slot.Queue.Clear();
            }
        }

        private static void SafeLog(IAnalyticsSink sink, string name, IReadOnlyList<AnalyticsParam> parameters)
        {
            try { sink.LogEvent(name, parameters); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void SafeSetProperty(IAnalyticsSink sink, string name, string value)
        {
            try { sink.SetUserProperty(name, value); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
