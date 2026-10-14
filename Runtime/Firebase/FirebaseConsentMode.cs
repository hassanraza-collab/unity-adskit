using System.Collections.Generic;
using Firebase.Analytics;
using NineDTech.AdsKit.Ads;
using UnityEngine;

namespace NineDTech.AdsKit.Firebase
{
    /// <summary>
    /// Google Consent Mode v2 for Firebase Analytics, driven by the AdsKit consent result (UMP / TCF purpose 1).
    /// Not required or granted: everything granted. Denied: ad storage, ad user data, ad personalization and
    /// analytics storage denied (Firebase then sends cookieless pings only).
    /// </summary>
    public static class FirebaseConsentMode
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            AdsConsent.StatusChanged += status => FirebaseBootstrap.WhenCompleted(() => Apply(status));
        }

        private static void Apply(AdsConsentStatus status)
        {
            if (!FirebaseBootstrap.IsReady || status == AdsConsentStatus.Unknown) return;
            var granted = status != AdsConsentStatus.Denied ? ConsentStatus.Granted : ConsentStatus.Denied;
            FirebaseAnalytics.SetConsent(new Dictionary<ConsentType, ConsentStatus>
            {
                { ConsentType.AnalyticsStorage, granted },
                { ConsentType.AdStorage, granted },
                { ConsentType.AdUserData, granted },
                { ConsentType.AdPersonalization, granted }
            });
            KitLog.Info("Firebase consent mode: " + granted);
        }
    }
}
