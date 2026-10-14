using NineDTech.AdsKit.Ads;
using UnityEngine;

// AdsKit starter: copy into your game and fill in the TODOs.
// AdsGameHooksBase already pauses Time.timeScale and AudioListener while a full-screen ad is open.
public sealed class MyGameAdsHooks : AdsGameHooksBase
{
    // TODO: return the highest level the player has reached (used by min_level gates).
    public override int CurrentLevel => PlayerPrefs.GetInt("level", 1);

    // TODO: return true once the player bought "remove ads" (rewarded ads still work).
    public override bool AdsRemoved => false;

    // TODO: show/hide a small "Loading ad..." spinner (rewarded ad still loading, max a few seconds).
    public override void SetAdLoadingIndicator(bool visible) { }

    // TODO: tell the player no rewarded ad is available right now (e.g. a toast).
    public override void OnRewardedUnavailable(string placementId)
    {
        Debug.Log("No ad available, try again later");
    }
}
