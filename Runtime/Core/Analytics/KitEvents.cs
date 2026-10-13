namespace NineDTech.AdsKit.Analytics
{
    /// <summary>Event and parameter names shared by every game using AdsKit (see Documentation~/ANALYTICS_EVENTS.md).</summary>
    public static class KitEvents
    {
        // Startup
        public const string LoadingStart = "loading_start";
        public const string LoadingComplete = "loading_complete";
        public const string RemoteConfigReady = "remote_config_ready";
        public const string ConsentShown = "consent_shown";
        public const string ConsentResult = "consent_result";
        public const string TutorialBegin = "tutorial_begin";
        public const string TutorialComplete = "tutorial_complete";
        public const string ScreenView = "screen_view";

        // Progression
        public const string LevelStart = "level_start";
        public const string LevelEnd = "level_end";
        public const string LevelRestart = "level_restart";
        public const string LevelQuit = "level_quit";
        public const string LevelMilestonePrefix = "level_milestone_";

        // Common parameters
        public const string ParamSession = "sess";
        public const string ParamLevel = "level";
        public const string ParamLevelName = "level_name";
        public const string ParamSuccess = "success";
        public const string ParamDuration = "duration_sec";
        public const string ParamMoves = "moves";
        public const string ParamAttempt = "attempt";
    }
}
