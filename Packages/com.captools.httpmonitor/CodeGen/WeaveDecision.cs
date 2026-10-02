using System;
using System.Collections.Generic;
using System.Linq;

namespace HttpMonitor.CodeGen
{
    /// <summary>
    /// Whether an assembly should be woven at all, decided from its name, the compilation's
    /// scripting defines and the project's <see cref="WeaverConfig"/>. Pure, so it is unit-tested
    /// without a compilation pipeline.
    /// </summary>
    public static class WeaveDecision
    {
        /// <summary>Add this scripting define to turn the weaver off for a build target regardless of settings.</summary>
        public const string DisableDefine = "HTTP_MONITOR_DISABLE";

        private static readonly string[] SkipPrefixes =
        {
            "Unity.", "UnityEngine.", "UnityEditor.",
            "Mono.", "System.", "mscorlib", "netstandard", "nunit.",
        };

        /// <summary>SDK own code calls the real SendWebRequest/Dispose and must never be rewritten.</summary>
        private static readonly string[] SkipExact =
        {
            "HttpMonitor.Runtime",
            "HttpMonitor.Editor",
        };

        public static bool ShouldWeave(string assemblyName, IReadOnlyCollection<string> defines, WeaverConfig config)
        {
            if (string.IsNullOrEmpty(assemblyName))
                return false;

            if (SkipExact.Contains(assemblyName))
                return false;

            if (SkipPrefixes.Any(p => assemblyName.StartsWith(p, StringComparison.Ordinal)))
                return false;

            if (defines != null && defines.Contains(DisableDefine))
                return false;

            config = config ?? WeaverConfig.Default;

            if (!config.WeavingEnabled)
                return false;

            if (config.IsExcluded(assemblyName))
                return false;

            // Player builds: a release build (no DEVELOPMENT_BUILD) is woven only when the project opted in.
            // No defines at all is treated the same way: with no evidence of an Editor or development
            // context, the safe failure is capture visibly off rather than capture code silently shipped.
            var isEditor = defines != null && defines.Contains("UNITY_EDITOR");
            var isDevelopment = defines != null && defines.Contains("DEVELOPMENT_BUILD");

            if (!isEditor && !isDevelopment && !config.WeaveReleaseBuilds)
                return false;

            return true;
        }
    }
}
