using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace NineDTech.AdsKit.Editor
{
    /// <summary>
    /// Google Consent Mode v2 defaults for Firebase Analytics: every consent type starts denied in the generated
    /// Android manifest, so nothing (auto events, events queued before the UMP form is answered) is sent as granted.
    /// FirebaseConsentMode grants them as soon as UMP reports not required / granted; Firebase remembers that.
    /// </summary>
    public sealed class AdsKitConsentDefaultsProcessor : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";

        private static readonly string[] DeniedByDefault =
        {
            "google_analytics_default_allow_analytics_storage",
            "google_analytics_default_allow_ad_storage",
            "google_analytics_default_allow_ad_user_data",
            "google_analytics_default_allow_ad_personalization_signals"
        };

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // path = <gradle project>/unityLibrary
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning("AdsKit: AndroidManifest.xml not found; consent mode defaults not added (" + manifestPath + ")");
                return;
            }

            var doc = new XmlDocument();
            doc.Load(manifestPath);
            var application = doc.SelectSingleNode("/manifest/application") as XmlElement;
            if (application == null)
            {
                application = doc.CreateElement("application");
                doc.DocumentElement?.AppendChild(application);
            }

            foreach (var name in DeniedByDefault)
            {
                XmlElement meta = null;
                foreach (XmlNode node in application.SelectNodes("meta-data"))
                    if (node is XmlElement e && e.GetAttribute("name", AndroidNs) == name) { meta = e; break; }
                if (meta == null)
                {
                    meta = doc.CreateElement("meta-data");
                    application.AppendChild(meta);
                    meta.SetAttribute("name", AndroidNs, name);
                }
                meta.SetAttribute("value", AndroidNs, "false");
            }
            doc.Save(manifestPath);
        }
    }
}
