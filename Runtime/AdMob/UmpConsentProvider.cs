using System;
using System.Collections.Generic;
using GoogleMobileAds.Ump.Api;
using NineDTech.AdsKit.Ads;
using UnityEngine;
using KitAnalytics = NineDTech.AdsKit.Analytics.Analytics;

namespace NineDTech.AdsKit.AdMob
{
    /// <summary>
    /// Google User Messaging Platform consent (GDPR/TCF). Runs before either mediation SDK initializes and is used
    /// for both A/B arms: MAX and the mediated networks read the TCF string UMP writes. The result is mapped from
    /// TCF purpose 1 (store/access information on a device). UMP calls back on the Android UI thread (it follows
    /// RaiseAdEventsOnUnityMainThread, which AdsKit leaves off), so every callback hops to the Unity main thread.
    /// </summary>
    public sealed class UmpConsentProvider : IAdsConsentProvider
    {
        public string Name => "ump";

        public bool CanRequestAds => ConsentInformation.CanRequestAds();

        public bool PrivacyOptionsRequired =>
            ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required;

        public void Gather(Action<AdsConsentStatus> onComplete)
        {
            var request = new ConsentRequestParameters();
            if (Debug.isDebugBuild && (AdsConsent.DebugForceEea || AdsConsent.DebugTestDeviceHashedIds.Count > 0))
            {
                request.ConsentDebugSettings = new ConsentDebugSettings
                {
                    DebugGeography = AdsConsent.DebugForceEea ? DebugGeography.EEA : DebugGeography.Disabled,
                    TestDeviceHashedIds = new List<string>(AdsConsent.DebugTestDeviceHashedIds)
                };
            }

            ConsentInformation.Update(request, updateError => KitRunner.Dispatch(() =>
            {
                if (updateError != null)
                {
                    // Offline or misconfigured: the stored consent from an earlier session still applies.
                    KitLog.Warn("UMP update failed: " + updateError.Message);
                    onComplete?.Invoke(Map());
                    return;
                }
                if (ConsentInformation.ConsentStatus == ConsentStatus.Required)
                    KitAnalytics.Event(Analytics.KitEvents.ConsentShown).Log();
                ConsentForm.LoadAndShowConsentFormIfRequired(formError => KitRunner.Dispatch(() =>
                {
                    if (formError != null) KitLog.Warn("UMP form error: " + formError.Message);
                    onComplete?.Invoke(Map());
                }));
            }));
        }

        public void ShowPrivacyOptions(Action onClosed)
        {
            ConsentForm.ShowPrivacyOptionsForm(error => KitRunner.Dispatch(() =>
            {
                if (error != null) KitLog.Warn("UMP privacy options: " + error.Message);
                AdsConsent.Report(Map());
                onClosed?.Invoke();
            }));
        }

        private static AdsConsentStatus Map()
        {
            if (ConsentInformation.ConsentStatus == ConsentStatus.NotRequired) return AdsConsentStatus.NotRequired;
            var tcf = TcfData.Read();
            if (!tcf.GdprApplies)
                return ConsentInformation.ConsentStatus == ConsentStatus.Obtained ? AdsConsentStatus.NotRequired : AdsConsentStatus.Unknown;
            return tcf.Purpose1 ? AdsConsentStatus.Granted : AdsConsentStatus.Denied;
        }
    }
}
