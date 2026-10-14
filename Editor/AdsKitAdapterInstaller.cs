using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NineDTech.AdsKit.Editor
{
    /// <summary>
    /// Tools > AdsKit > Install Mediation Adapters: writes the pinned EDM4U dependency files for every network
    /// in BOTH stacks (AppLovin MAX and Google AdMob). Each network SDK is locked to the same version on both
    /// sides, because one APK can only contain one version of each SDK. Update the lock here, in one place.
    /// Never upgrade single adapters from the AppLovin Integration Manager.
    /// </summary>
    public static class AdsKitAdapterInstaller
    {
        private const string PangleRepo = "https://artifact.bytedance.com/repository/pangle/";
        private const string MintegralRepo = "https://dl-maven-android.mintegral.com/repository/mbridge_android_sdk_oversea";

        private struct Adapter
        {
            public string Folder;   // folder name under the SDK's Mediation directory
            public string FileName;
            public string[] Specs;  // Maven specs, exact versions
            public string Repo;     // extra Maven repository, if any
        }

        // ---- Version lock (verified 2026-10-14 against MAX 8.6.6 / applovin-sdk 13.6.4 and GMA Unity 11.5.0 / play-services-ads 25.4.0)
        private static readonly Adapter[] MaxAdapters =
        {
            new Adapter { Folder = "Google",    Specs = new[] { "com.applovin.mediation:google-adapter:[25.4.0.0]" } },
            new Adapter { Folder = "ByteDance", Specs = new[] { "com.applovin.mediation:bytedance-adapter:[8.3.0.3.0]" }, Repo = PangleRepo },
            new Adapter { Folder = "Mintegral", Specs = new[] { "com.applovin.mediation:mintegral-adapter:[17.1.81.0]" }, Repo = MintegralRepo },
            new Adapter { Folder = "Vungle",    Specs = new[] { "com.applovin.mediation:vungle-adapter:[7.7.8.1]" } },
            new Adapter { Folder = "UnityAds",  Specs = new[] { "com.applovin.mediation:unityads-adapter:[4.20.1.1]" } }
        };

        private static readonly Adapter[] AdMobAdapters =
        {
            new Adapter { Folder = "AppLovin",        FileName = "AppLovinMediationDependencies.xml",        Specs = new[] { "com.google.ads.mediation:applovin:[13.6.4.2]" } },
            new Adapter { Folder = "Pangle",          FileName = "PangleMediationDependencies.xml",          Specs = new[] { "com.google.ads.mediation:pangle:[8.3.0.3.0]" }, Repo = PangleRepo },
            new Adapter { Folder = "Mintegral",       FileName = "MintegralMediationDependencies.xml",       Specs = new[] { "com.google.ads.mediation:mintegral:[17.1.81.1]" }, Repo = MintegralRepo },
            new Adapter { Folder = "LiftoffMonetize", FileName = "LiftoffMonetizeMediationDependencies.xml", Specs = new[] { "com.google.ads.mediation:vungle:[7.7.8.1]" } },
            new Adapter { Folder = "UnityAds",        FileName = "UnityAdsMediationDependencies.xml",        Specs = new[] { "com.google.ads.mediation:unity:[4.20.1.0]", "com.unity3d.ads:unity-ads:[4.20.1]" } }
        };

        [MenuItem("Tools/AdsKit/Install Mediation Adapters", priority = 5)]
        private static void InstallMenu()
        {
            var maxRoot = FindFolder("MaxSdk");
            var gmaRoot = FindFolder("GoogleMobileAds");
            if (maxRoot == null && gmaRoot == null)
            {
                EditorUtility.DisplayDialog("AdsKit", "Import the AppLovin MAX and/or Google Mobile Ads plugin first.", "OK");
                return;
            }
            var written = Install(maxRoot, gmaRoot);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("AdsKit",
                written + " adapter files written (version-locked).\n\nNow run: Assets > External Dependency Manager > Android Resolver > Force Resolve.\n\nDo NOT click Upgrade in the AppLovin Integration Manager.",
                "OK");
        }

        public static int Install(string maxRoot, string gmaRoot)
        {
            var count = 0;
            if (maxRoot != null)
                foreach (var a in MaxAdapters)
                {
                    var exportPath = "MaxSdk/Mediation/" + a.Folder + "/Editor/Dependencies.xml";
                    var path = maxRoot + "/Mediation/" + a.Folder + "/Editor/Dependencies.xml";
                    Write(path, a);
                    // The Integration Manager finds installed adapters by these labels.
                    Label(path, "al_max", "al_max_export_path-" + exportPath);
                    count++;
                }
            if (gmaRoot != null)
                foreach (var a in AdMobAdapters)
                {
                    Write(gmaRoot + "/Mediation/" + a.Folder + "/Editor/" + a.FileName, a);
                    count++;
                }
            Debug.Log("[AdsKit] Installed " + count + " version-locked mediation adapter files");
            return count;
        }

        private static void Write(string assetPath, Adapter a)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<!-- Written by AdsKit (Tools > AdsKit > Install Mediation Adapters). Version-locked: do not edit or upgrade by hand. -->");
            sb.AppendLine("<dependencies>");
            sb.AppendLine("    <androidPackages>");
            foreach (var spec in a.Specs)
            {
                if (a.Repo == null || !spec.Contains("mediation"))
                {
                    sb.AppendLine("        <androidPackage spec=\"" + spec + "\" />");
                    continue;
                }
                sb.AppendLine("        <androidPackage spec=\"" + spec + "\">");
                sb.AppendLine("            <repositories>");
                sb.AppendLine("                <repository>" + a.Repo + "</repository>");
                sb.AppendLine("            </repositories>");
                sb.AppendLine("        </androidPackage>");
            }
            sb.AppendLine("    </androidPackages>");
            sb.AppendLine("</dependencies>");

            var full = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
            File.WriteAllText(full, sb.ToString());
        }

        private static void Label(string assetPath, params string[] labels)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset != null) AssetDatabase.SetLabels(asset, labels);
        }

        /// <summary>Finds an SDK root folder like "Assets/MaxSdk" (also when moved into a subfolder).</summary>
        private static string FindFolder(string name)
        {
            if (AssetDatabase.IsValidFolder("Assets/" + name)) return "Assets/" + name;
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Folder"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == name) return path;
            }
            return null;
        }
    }
}
