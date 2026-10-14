using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEngine;

namespace NineDTech.AdsKit.Editor
{
    /// <summary>
    /// Detects which SDKs are installed (as .unitypackage or UPM) and keeps the matching scripting defines in sync,
    /// so each AdsKit provider assembly compiles only when its SDK exists:
    /// ADSKIT_FIREBASE, ADSKIT_FIREBASE_CRASHLYTICS, ADSKIT_MAX, ADSKIT_ADMOB.
    /// </summary>
    [InitializeOnLoad]
    public static class AdsKitSdkDefines
    {
        private struct Sdk
        {
            public string Define;
            public string[] Assemblies;
        }

        private static readonly Sdk[] Sdks =
        {
            new Sdk { Define = "ADSKIT_FIREBASE", Assemblies = new[] { "Firebase.App", "Firebase.Analytics", "Firebase.RemoteConfig" } },
            new Sdk { Define = "ADSKIT_FIREBASE_CRASHLYTICS", Assemblies = new[] { "Firebase.App", "Firebase.Crashlytics" } },
            new Sdk { Define = "ADSKIT_MAX", Assemblies = new[] { "MaxSdk.Scripts" } },
            new Sdk { Define = "ADSKIT_ADMOB", Assemblies = new[] { "GoogleMobileAds" } }
        };

        private static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Android, NamedBuildTarget.Standalone };

        static AdsKitSdkDefines()
        {
            EditorApplication.delayCall += () => Refresh(false);
        }

        [MenuItem("Tools/AdsKit/Refresh SDK Defines", priority = 30)]
        private static void RefreshMenu() => Refresh(true);

        public static void Refresh(bool log)
        {
            var present = InstalledAssemblyNames();
            var wanted = new HashSet<string>();
            foreach (var sdk in Sdks)
                if (sdk.Assemblies.All(present.Contains))
                    wanted.Add(sdk.Define);

            foreach (var target in Targets)
            {
                var current = PlayerSettings.GetScriptingDefineSymbols(target)
                    .Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                var next = current.Where(d => !Sdks.Any(s => s.Define == d)).ToList();
                next.AddRange(Sdks.Select(s => s.Define).Where(wanted.Contains));
                if (next.SequenceEqual(current)) continue;
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", next));
                Debug.Log("[AdsKit] " + target.TargetName + " SDK defines: " + (wanted.Count == 0 ? "(none)" : string.Join(", ", wanted)));
            }
            if (log) Debug.Log("[AdsKit] Installed SDKs: " + (wanted.Count == 0 ? "none (simulator only)" : string.Join(", ", wanted)));
        }

        private static HashSet<string> InstalledAssemblyNames()
        {
            var names = new HashSet<string>();
            foreach (var path in CompilationPipeline.GetPrecompiledAssemblyPaths(CompilationPipeline.PrecompiledAssemblySources.UserAssembly))
                names.Add(Path.GetFileNameWithoutExtension(path));
            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies))
                names.Add(assembly.name);
            return names;
        }
    }
}
