using System.Collections.Generic;
using UnityEngine;

namespace NineDTech.AdsKit.Analytics
{
    /// <summary>Defaults for the analytics layer. The Remote Config JSON (<see cref="remoteConfigKey"/>) overrides them at runtime.</summary>
    [CreateAssetMenu(menuName = "AdsKit/Analytics Settings", fileName = "AnalyticsSettings")]
    public sealed class AnalyticsSettings : ScriptableObject
    {
        [Tooltip("Master switch. Remote key: enabled.")]
        public bool enabled = true;

        [Tooltip("Remote Config parameter holding the analytics JSON.")]
        public string remoteConfigKey = "analytics_config";

        [Tooltip("Print every event to the console in the Editor.")]
        public bool logToConsoleInEditor = true;

        [Tooltip("Print every event to the console in development builds.")]
        public bool logToConsoleInDevelopmentBuilds = true;

        [Tooltip("Add the session number ('sess') to every event.")]
        public bool addSessionParam = true;

        [Tooltip("Events never sent (remote key: disabled_events).")]
        public List<string> disabledEvents = new List<string>();

        [Tooltip("First completion of these levels logs level_milestone_<N> (remote key: level_milestones).")]
        public List<int> levelMilestones = new List<int> { 1, 2, 3, 5, 7, 10, 15, 20, 25, 30, 40, 50, 75, 100, 150, 200 };
    }
}
