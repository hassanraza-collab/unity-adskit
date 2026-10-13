using UnityEngine;

namespace NineDTech.AdsKit
{
    /// <summary>Tagged logging for every AdsKit assembly. Warnings and errors always print; info only when <see cref="Verbose"/>.</summary>
    public static class KitLog
    {
        private const string Tag = "[AdsKit] ";

        public static bool Verbose = true;

        public static void Info(string message)
        {
            if (Verbose) Debug.Log(Tag + message);
        }

        public static void Warn(string message) => Debug.LogWarning(Tag + message);

        public static void Error(string message) => Debug.LogError(Tag + message);
    }
}
