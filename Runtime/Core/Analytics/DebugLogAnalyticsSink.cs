using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NineDTech.AdsKit.Analytics
{
    /// <summary>Prints every event to the console. Added automatically in the Editor and development builds.</summary>
    public sealed class DebugLogAnalyticsSink : IAnalyticsSink
    {
        private readonly StringBuilder _sb = new StringBuilder();

        public string Id => "debug";
        public bool IsReady => true;

        public void LogEvent(string name, IReadOnlyList<AnalyticsParam> parameters)
        {
            _sb.Clear();
            _sb.Append("[Analytics] ").Append(name);
            if (parameters.Count > 0)
            {
                _sb.Append(" {");
                for (var i = 0; i < parameters.Count; i++)
                {
                    if (i > 0) _sb.Append(", ");
                    _sb.Append(parameters[i]);
                }
                _sb.Append('}');
            }
            Debug.Log(_sb.ToString());
        }

        public void SetUserProperty(string name, string value)
        {
            Debug.Log("[Analytics] user property " + name + " = " + value);
        }
    }
}
