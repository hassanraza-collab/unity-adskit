namespace NineDTech.AdsKit.Ads
{
    /// <summary>Why an ad did not show. Logged as the <c>blocked</c> parameter of <c>ad_opportunity</c>.</summary>
    public enum AdBlockReason
    {
        None,
        NotInitialized,
        AdsDisabled,
        PlacementUnknown,
        PlacementDisabled,
        AdsRemoved,
        SessionGate,
        LevelGate,
        SessionStartGrace,
        SessionCap,
        AfterSplashCooldown,
        AfterRewardedCooldown,
        GlobalCooldown,
        PlacementCooldown,
        Busy,
        Offline,
        NotReady
    }

    public enum AdShowOutcome
    {
        /// <summary>The ad was displayed and closed.</summary>
        Shown,
        /// <summary>Rules or readiness prevented the ad (see <see cref="AdShowResult.Reason"/>).</summary>
        Blocked,
        /// <summary>The SDK failed to display a loaded ad.</summary>
        Failed
    }

    /// <summary>Result passed to every show callback. The callback always fires exactly once.</summary>
    public readonly struct AdShowResult
    {
        public readonly AdShowOutcome Outcome;
        public readonly AdBlockReason Reason;
        public readonly string Error;

        private AdShowResult(AdShowOutcome outcome, AdBlockReason reason, string error)
        {
            Outcome = outcome;
            Reason = reason;
            Error = error;
        }

        public bool Shown => Outcome == AdShowOutcome.Shown;

        public static AdShowResult ShownResult => new AdShowResult(AdShowOutcome.Shown, AdBlockReason.None, null);
        public static AdShowResult Blocked(AdBlockReason reason) => new AdShowResult(AdShowOutcome.Blocked, reason, null);
        public static AdShowResult Failed(string error) => new AdShowResult(AdShowOutcome.Failed, AdBlockReason.None, error);

        public override string ToString() =>
            Outcome == AdShowOutcome.Blocked ? "Blocked(" + Reason + ")" : Outcome == AdShowOutcome.Failed ? "Failed(" + Error + ")" : "Shown";

        /// <summary>snake_case name for analytics.</summary>
        public static string ReasonName(AdBlockReason reason)
        {
            switch (reason)
            {
                case AdBlockReason.None: return "none";
                case AdBlockReason.NotInitialized: return "not_initialized";
                case AdBlockReason.AdsDisabled: return "ads_disabled";
                case AdBlockReason.PlacementUnknown: return "unknown";
                case AdBlockReason.PlacementDisabled: return "disabled";
                case AdBlockReason.AdsRemoved: return "ads_removed";
                case AdBlockReason.SessionGate: return "session_gate";
                case AdBlockReason.LevelGate: return "level_gate";
                case AdBlockReason.SessionStartGrace: return "session_start";
                case AdBlockReason.SessionCap: return "session_cap";
                case AdBlockReason.AfterSplashCooldown: return "after_splash";
                case AdBlockReason.AfterRewardedCooldown: return "after_rewarded";
                case AdBlockReason.GlobalCooldown: return "global_cooldown";
                case AdBlockReason.PlacementCooldown: return "cooldown";
                case AdBlockReason.Busy: return "busy";
                case AdBlockReason.Offline: return "offline";
                case AdBlockReason.NotReady: return "not_ready";
                default: return reason.ToString().ToLowerInvariant();
            }
        }
    }
}
