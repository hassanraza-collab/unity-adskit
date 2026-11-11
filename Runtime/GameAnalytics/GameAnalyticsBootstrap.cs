using GameAnalyticsSDK.Setup;
using UnityEngine;
using GA = GameAnalyticsSDK.GameAnalytics;

namespace NineDTech.AdsKit.GameAnalytics
{
    /// <summary>
    /// Compiled only when the GameAnalytics SDK is installed (ADSKIT_GAMEANALYTICS, set by the AdsKit SDK detector).
    /// Before the first scene loads: creates the GameAnalytics object (if the scene has none), initializes the SDK
    /// and adds <see cref="GameAnalyticsSink"/>, so every AdsKit analytics event also reaches GameAnalytics.
    /// Keys come from the SDK's own settings asset (Window > GameAnalytics > Select Settings, saved to
    /// Resources/GameAnalytics/Settings). Without that asset nothing starts. The SDK sends nothing from the Editor.
    /// </summary>
    public static class GameAnalyticsBootstrap
    {
        public static bool Started { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Started = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (Started) return;
            if (Resources.Load<Settings>("GameAnalytics/Settings") == null)
            {
                KitLog.Warn("GameAnalytics: SDK installed but no settings asset (Window > GameAnalytics > Select Settings). Not started.");
                return;
            }
            Started = true;

            if (Object.FindAnyObjectByType<GA>() == null)
            {
                var go = new GameObject("GameAnalytics");
                go.AddComponent<GA>(); // Awake marks it DontDestroyOnLoad
            }

            Analytics.Analytics.AddSink(new GameAnalyticsSink());
            GA.Initialize();
            KitLog.Info("GameAnalytics initialized");
        }
    }
}
