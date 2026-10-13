using System;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>The ad unit id one mediation provider uses for an <see cref="AdUnitDefinition"/>.</summary>
    [Serializable]
    public struct ProviderAdUnitId
    {
        public string provider;
        public string androidId;
    }
}
