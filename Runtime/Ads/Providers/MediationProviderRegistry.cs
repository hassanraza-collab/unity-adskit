using System;
using System.Collections.Generic;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// Provider factories by id. Each provider assembly registers itself from a
    /// <c>[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)]</c>, so only installed SDKs are available.
    /// </summary>
    public static class MediationProviderRegistry
    {
        private static readonly Dictionary<string, Func<IMediationProvider>> Factories =
            new Dictionary<string, Func<IMediationProvider>>();

        public static void Register(string id, Func<IMediationProvider> factory)
        {
            if (string.IsNullOrEmpty(id) || factory == null) return;
            Factories[id] = factory;
        }

        public static bool IsRegistered(string id) => !string.IsNullOrEmpty(id) && Factories.ContainsKey(id);

        public static IEnumerable<string> Ids => Factories.Keys;

        public static IMediationProvider Create(string id)
        {
            return id != null && Factories.TryGetValue(id, out var factory) ? factory() : null;
        }
    }
}
