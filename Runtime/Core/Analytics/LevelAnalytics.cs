using System;
using System.Collections.Generic;
using UnityEngine;

namespace NineDTech.AdsKit.Analytics
{
    /// <summary>
    /// Level progression events shared by every level-based game: level_start / level_end (Firebase
    /// recommended events), level_restart, level_quit and one-time level_milestone_N events for console funnels.
    /// </summary>
    public static class LevelAnalytics
    {
        private const string MilestoneKey = "adskit.level_milestone_done";

        private static readonly HashSet<int> Milestones = new HashSet<int>();

        public static void SetMilestones(IEnumerable<int> levels)
        {
            Milestones.Clear();
            if (levels == null) return;
            foreach (var l in levels)
                if (l > 0) Milestones.Add(l);
        }

        public static void LevelStart(int level, string levelName, int attempt, bool isReplay)
        {
            Analytics.Event(KitEvents.LevelStart)
                .Add(KitEvents.ParamLevel, level)
                .Add(KitEvents.ParamLevelName, levelName)
                .Add(KitEvents.ParamAttempt, attempt)
                .Add("is_replay", isReplay)
                .Log();
        }

        /// <summary>Level won. Also logs level_milestone_N the first time a milestone level is completed.</summary>
        public static void LevelEnd(int level, string levelName, double durationSec, int moves, int attempt,
            int boostersUsed, int score)
        {
            Analytics.Event(KitEvents.LevelEnd)
                .Add(KitEvents.ParamLevel, level)
                .Add(KitEvents.ParamLevelName, levelName)
                .Add(KitEvents.ParamSuccess, 1)
                .Add(KitEvents.ParamDuration, Math.Round(durationSec, 1))
                .Add(KitEvents.ParamMoves, moves)
                .Add(KitEvents.ParamAttempt, attempt)
                .Add("boosters_used", boostersUsed)
                .Add("score", score)
                .Log();

            if (!Milestones.Contains(level)) return;
            var done = PlayerPrefs.GetInt(MilestoneKey, 0);
            if (level <= done) return;
            PlayerPrefs.SetInt(MilestoneKey, level);
            Analytics.Event(KitEvents.LevelMilestonePrefix + level)
                .Add(KitEvents.ParamLevel, level)
                .Add("days_since_install", SessionTracker.DaysSinceInstall)
                .Log();
        }

        /// <summary>The player restarted the level (the "fail" signal in games without a fail state).</summary>
        public static void LevelRestart(int level, string levelName, double durationSec, int moves, int attempt)
        {
            Analytics.Event(KitEvents.LevelRestart)
                .Add(KitEvents.ParamLevel, level)
                .Add(KitEvents.ParamLevelName, levelName)
                .Add(KitEvents.ParamDuration, Math.Round(durationSec, 1))
                .Add(KitEvents.ParamMoves, moves)
                .Add(KitEvents.ParamAttempt, attempt)
                .Log();
        }

        /// <summary>The player left the level unfinished (e.g. back to home).</summary>
        public static void LevelQuit(int level, string levelName, double durationSec, int moves)
        {
            Analytics.Event(KitEvents.LevelQuit)
                .Add(KitEvents.ParamLevel, level)
                .Add(KitEvents.ParamLevelName, levelName)
                .Add(KitEvents.ParamDuration, Math.Round(durationSec, 1))
                .Add(KitEvents.ParamMoves, moves)
                .Log();
        }
    }
}
