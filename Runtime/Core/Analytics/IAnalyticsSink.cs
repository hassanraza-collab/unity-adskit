using System.Collections.Generic;

namespace NineDTech.AdsKit.Analytics
{
    /// <summary>
    /// A destination for analytics (Firebase, a console logger, later AppsFlyer/Adjust).
    /// Events logged before <see cref="IsReady"/> are queued by <see cref="Analytics"/> and flushed later.
    /// </summary>
    public interface IAnalyticsSink
    {
        string Id { get; }
        bool IsReady { get; }
        void LogEvent(string name, IReadOnlyList<AnalyticsParam> parameters);
        void SetUserProperty(string name, string value);
    }
}
