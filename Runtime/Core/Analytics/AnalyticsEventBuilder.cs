using System.Collections.Generic;

namespace NineDTech.AdsKit.Analytics
{
    /// <summary>
    /// Fluent, pooled event builder: <c>Analytics.Event("level_start").Add("level", 3).Log();</c>
    /// Always finish with <see cref="Log"/>; the builder returns to the pool and must not be reused after.
    /// </summary>
    public sealed class AnalyticsEventBuilder
    {
        internal readonly List<AnalyticsParam> Params = new List<AnalyticsParam>(12);

        public string Name { get; private set; }

        internal void Begin(string name)
        {
            Name = name;
            Params.Clear();
        }

        public AnalyticsEventBuilder Add(string name, string value) =>
            Set(AnalyticsParam.Of(name, Truncate(value)), true);

        public AnalyticsEventBuilder Add(string name, long value) => Set(AnalyticsParam.Of(name, value), true);
        public AnalyticsEventBuilder Add(string name, int value) => Set(AnalyticsParam.Of(name, (long)value), true);
        public AnalyticsEventBuilder Add(string name, bool value) => Set(AnalyticsParam.Of(name, value ? 1L : 0L), true);
        public AnalyticsEventBuilder Add(string name, double value) => Set(AnalyticsParam.Of(name, value), true);
        public AnalyticsEventBuilder Add(string name, float value) => Set(AnalyticsParam.Of(name, (double)value), true);

        /// <summary>Add only when no parameter with this name exists yet (used by common-parameter enrichers).</summary>
        public AnalyticsEventBuilder AddIfMissing(AnalyticsParam param) => Set(param, false);

        public bool Has(string name)
        {
            for (var i = 0; i < Params.Count; i++)
                if (Params[i].Name == name) return true;
            return false;
        }

        public void Log() => Analytics.Dispatch(this);

        private AnalyticsEventBuilder Set(AnalyticsParam param, bool overwrite)
        {
            if (!AnalyticsNames.IsValidIdentifier(param.Name, AnalyticsNames.MaxParamNameLength))
            {
                KitLog.Warn("Analytics: dropped invalid parameter name '" + param.Name + "' on event " + Name);
                return this;
            }
            for (var i = 0; i < Params.Count; i++)
            {
                if (Params[i].Name != param.Name) continue;
                if (overwrite) Params[i] = param;
                return this;
            }
            Params.Add(param);
            return this;
        }

        private static string Truncate(string value)
        {
            if (value == null) return "";
            return value.Length <= AnalyticsNames.MaxStringValueLength
                ? value
                : value.Substring(0, AnalyticsNames.MaxStringValueLength);
        }
    }
}
