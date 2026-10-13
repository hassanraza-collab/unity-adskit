namespace NineDTech.AdsKit.Ads
{
    internal enum AdSlotState
    {
        Idle,
        Loading,
        Ready,
        Showing
    }

    /// <summary>Cache state of one full-screen ad unit.</summary>
    internal sealed class AdSlot
    {
        public string Key;
        public AdFormat Format;
        public string UnitId;
        public AdSlotState State;
        public double LoadedAt;
        public int Attempts;
        public double NextRetryAt;
        /// <summary>Hit retry_max_attempts; waits for a context change or the network to come back.</summary>
        public bool Exhausted;
        public string Network;
        /// <summary>Incremented per request so stale callbacks are ignored.</summary>
        public int RequestId;
    }
}
