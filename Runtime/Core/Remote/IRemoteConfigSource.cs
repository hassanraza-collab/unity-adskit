using System;

namespace NineDTech.AdsKit.Remote
{
    /// <summary>
    /// A Remote Config backend (Firebase in production, <see cref="LocalRemoteConfigSource"/> otherwise).
    /// Callbacks may come from any thread; <see cref="RemoteConfigService"/> marshals them to the main thread.
    /// </summary>
    public interface IRemoteConfigSource
    {
        string Name { get; }

        /// <summary>Fetch and activate. Report true when values were activated (fresh or cached).</summary>
        void FetchAndActivate(Action<bool> onComplete);

        /// <summary>
        /// Read a value. <paramref name="isRemote"/> is true when the value came from the backend
        /// (fetched now or activated earlier and cached), false for in-app defaults.
        /// Returns false when the key has no value at all.
        /// </summary>
        bool TryGetString(string key, out string value, out bool isRemote);

        /// <summary>Raised when new values were activated after the first fetch (e.g. real-time updates).</summary>
        event Action Updated;
    }
}
