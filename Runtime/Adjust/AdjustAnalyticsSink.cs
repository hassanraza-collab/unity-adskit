using System;
using System.Collections.Generic;
using NineDTech.AdsKit.Analytics;
using NineDTech.AdsKit.Attribution;

namespace NineDTech.AdsKit.Adjust
{
    /// <summary>
    /// Forwards only the analytics events listed in <see cref="AttributionSettings.events"/> to Adjust (by event token).
    /// Events logged before Adjust starts wait in the analytics queue.
    /// </summary>
    public sealed class AdjustAnalyticsSink : IAnalyticsSink
    {
        private readonly Dictionary<string, string> _tokens = new Dictionary<string, string>();
        private readonly Action<string, string> _track;

        public AdjustAnalyticsSink(IEnumerable<AttributionSettings.EventMapping> mappings, Action<string, string> track)
        {
            _track = track;
            if (mappings == null) return;
            foreach (var m in mappings)
            {
                if (m == null || string.IsNullOrEmpty(m.analyticsEvent) || string.IsNullOrEmpty(m.eventToken)) continue;
                _tokens[m.analyticsEvent.Trim()] = m.eventToken.Trim();
            }
        }

        public string Id => "adjust";

        public bool IsReady => AdjustAttributionProvider.Started;

        public void LogEvent(string name, IReadOnlyList<AnalyticsParam> parameters)
        {
            if (_tokens.TryGetValue(name, out var token)) _track(token, name);
        }

        public void SetUserProperty(string name, string value) { }
    }
}
