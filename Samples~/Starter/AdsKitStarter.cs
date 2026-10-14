using NineDTech.AdsKit.Ads;
using NineDTech.AdsKit.Analytics;
using UnityEngine;

// AdsKit starter: put this on a GameObject in your FIRST scene (splash) and assign both settings assets.
// It starts AdsKit as early as possible and shows how to call every placement.
public sealed class AdsKitStarter : MonoBehaviour
{
    [SerializeField] private AdsSettings adsSettings;
    [SerializeField] private AnalyticsSettings analyticsSettings;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        AnalyticsBootstrap.Initialize(analyticsSettings);
        Ads.Initialize(adsSettings, new MyGameAdsHooks());
    }

    // ---- Copy these calls to the right places in your game -------------------------------------

    // Loading screen: when the bar reaches Ads.SplashTriggerProgress (0.7), stop it and call this.
    public static void OnLoadingBarAtTrigger(System.Action continueLoading) =>
        Ads.ShowSplashAd(_ => continueLoading());

    // Screen changes (drives which ads stay preloaded).
    public static void OnHomeShown() => Ads.SetContext(AdsIds.ContextHome);
    public static void OnLevelStarted(int level, string levelName)
    {
        Ads.SetContext(AdsIds.ContextGameplay);
        LevelAnalytics.LevelStart(level, levelName, 1, false);
    }

    // Interstitials: always continue the game INSIDE the callback (it always fires, ad or not).
    public static void OnPlayPressed(System.Action startLevel) => Ads.ShowInterstitial("play_button", _ => startLevel());
    public static void OnNextLevel(System.Action loadNext) => Ads.ShowInterstitial("level_complete", _ => loadNext());

    // Rewarded: the reward runs after the ad closes, only if it was earned.
    public static void OnBoosterAdPressed(System.Action giveReward) => Ads.ShowRewarded("booster", giveReward);
}
