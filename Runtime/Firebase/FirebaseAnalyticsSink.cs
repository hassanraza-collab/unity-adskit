using System.Collections.Generic;
using Firebase.Analytics;
using NineDTech.AdsKit.Analytics;

namespace NineDTech.AdsKit.Firebase
{
    /// <summary>
    /// Sends AdsKit analytics to Firebase Analytics. User properties are mirrored to Crashlytics custom keys
    /// (e.g. ad_mediation), so crashes and ANRs can be split by A/B group.
    /// </summary>
    public sealed class FirebaseAnalyticsSink : IAnalyticsSink
    {
        private readonly List<Parameter> _buffer = new List<Parameter>(25);

        public string Id => "firebase";

        public bool IsReady => FirebaseBootstrap.IsReady;

        public void LogEvent(string name, IReadOnlyList<AnalyticsParam> parameters)
        {
            if (parameters.Count == 0)
            {
                FirebaseAnalytics.LogEvent(name);
                return;
            }
            _buffer.Clear();
            for (var i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                switch (p.Type)
                {
                    case AnalyticsParamType.String: _buffer.Add(new Parameter(p.Name, p.StringValue)); break;
                    case AnalyticsParamType.Long: _buffer.Add(new Parameter(p.Name, p.LongValue)); break;
                    default: _buffer.Add(new Parameter(p.Name, p.DoubleValue)); break;
                }
            }
            FirebaseAnalytics.LogEvent(name, _buffer.ToArray());
        }

        public void SetUserProperty(string name, string value)
        {
            FirebaseAnalytics.SetUserProperty(name, value);
#if ADSKIT_FIREBASE_CRASHLYTICS
            global::Firebase.Crashlytics.Crashlytics.SetCustomKey(name, value ?? "");
#endif
        }
    }
}
