using UnityEngine;

namespace NineDTech.AdsKit.AdMob
{
    /// <summary>
    /// Reads the IAB TCF v2 values UMP stores in Android's default SharedPreferences
    /// (Unity's PlayerPrefs uses a different file, so JNI is required).
    /// </summary>
    internal readonly struct TcfData
    {
        public readonly bool GdprApplies;
        /// <summary>Purpose 1: store and/or access information on a device.</summary>
        public readonly bool Purpose1;

        private TcfData(bool gdprApplies, bool purpose1)
        {
            GdprApplies = gdprApplies;
            Purpose1 = purpose1;
        }

        public static TcfData Read()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var manager = new AndroidJavaClass("android.preference.PreferenceManager"))
                using (var prefs = manager.CallStatic<AndroidJavaObject>("getDefaultSharedPreferences", activity))
                {
                    var gdpr = prefs.Call<int>("getInt", "IABTCF_gdprApplies", 0) == 1;
                    var purposes = prefs.Call<string>("getString", "IABTCF_PurposeConsents", "");
                    return new TcfData(gdpr, !string.IsNullOrEmpty(purposes) && purposes[0] == '1');
                }
            }
            catch (System.Exception e)
            {
                KitLog.Warn("TCF read failed: " + e.Message);
            }
#endif
            return new TcfData(false, false);
        }
    }
}
