using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.UnityLinker;
using UnityEngine;

namespace NineDTech.AdsKit.Editor
{
    /// <summary>
    /// Keeps the AdsKit provider assemblies in IL2CPP builds. Nothing references them directly (they register
    /// themselves from [RuntimeInitializeOnLoadMethod]), so managed stripping would otherwise drop them entirely
    /// and the build would silently fall back to the simulator. Unity ignores link.xml inside packages, hence
    /// this generated one. Only assemblies that exist in the build (SDK installed) are listed.
    /// </summary>
    public sealed class AdsKitLinkerProcessor : IUnityLinkerProcessor
    {
        private static readonly string[] Providers =
        {
            "NineDTech.AdsKit.Core",
            "NineDTech.AdsKit.Ads",
            "NineDTech.AdsKit.Firebase",
            "NineDTech.AdsKit.Max",
            "NineDTech.AdsKit.AdMob"
        };

        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var present = new HashSet<string>();
            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies))
                present.Add(assembly.name);

            var sb = new StringBuilder();
            sb.AppendLine("<linker>");
            foreach (var name in Providers)
                if (present.Contains(name))
                    sb.AppendLine("  <assembly fullname=\"" + name + "\" preserve=\"all\" />");
            sb.AppendLine("</linker>");

            var dir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Library", "AdsKit");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "link.xml");
            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }
}
