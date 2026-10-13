using System;
using System.Collections.Generic;

namespace NineDTech.AdsKit.Remote
{
    /// <summary>
    /// In-memory Remote Config used when no backend is installed, and in the Editor to test JSON payloads.
    /// Values set with <see cref="Set"/> count as "remote" so they exercise the same code paths.
    /// </summary>
    public sealed class LocalRemoteConfigSource : IRemoteConfigSource
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        public string Name => "local";

        public event Action Updated;

        public void Set(string key, string value, bool notify = false)
        {
            if (value == null) _values.Remove(key);
            else _values[key] = value;
            if (notify) Updated?.Invoke();
        }

        public void FetchAndActivate(Action<bool> onComplete) => onComplete?.Invoke(true);

        public bool TryGetString(string key, out string value, out bool isRemote)
        {
            isRemote = _values.TryGetValue(key, out value);
            return isRemote;
        }
    }
}
