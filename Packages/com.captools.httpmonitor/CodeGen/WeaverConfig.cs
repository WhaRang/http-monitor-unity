using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HttpMonitor.CodeGen
{
    /// <summary>
    /// What the post-processor knows about the project's settings. The Editor mirrors the weaving
    /// fields of <c>HttpMonitorSettings</c> into <c>ProjectSettings/HttpMonitorWeaver.cfg</c>, a flat
    /// key=value file, because this code runs in Unity's compilation process where neither the
    /// AssetDatabase nor UnityEngine serialization is available. Missing file means defaults.
    /// </summary>
    public sealed class WeaverConfig
    {
        public const string RelativePath = "ProjectSettings/HttpMonitorWeaver.cfg";

        public bool WeavingEnabled = true;
        public bool WeaveReleaseBuilds;
        public List<string> ExcludedAssemblies = new List<string>();

        public static WeaverConfig Default => new WeaverConfig();

        /// <summary>Reads the mirror file next to the project; defaults when it is missing or unreadable.</summary>
        public static WeaverConfig Load(string projectRoot = null)
        {
            try
            {
                var path = Path.Combine(projectRoot ?? Directory.GetCurrentDirectory(), RelativePath);

                return File.Exists(path) ? Parse(File.ReadAllText(path)) : Default;
            }
            catch (Exception)
            {
                return Default;
            }
        }

        public static WeaverConfig Parse(string text)
        {
            var config = new WeaverConfig();

            if (string.IsNullOrEmpty(text))
                return config;

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();

                if (line.Length == 0 || line[0] == '#')
                    continue;

                var eq = line.IndexOf('=');

                if (eq < 0)
                    continue;

                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "weavingEnabled":
                        config.WeavingEnabled = ParseBool(value, true);

                        break;
                    case "weaveReleaseBuilds":
                        config.WeaveReleaseBuilds = ParseBool(value, false);

                        break;
                    case "excludedAssemblies":
                        config.ExcludedAssemblies.Clear();

                        foreach (var name in value.Split(';'))
                        {
                            var trimmed = name.Trim();

                            if (trimmed.Length > 0)
                                config.ExcludedAssemblies.Add(trimmed);
                        }

                        break;
                }
            }

            return config;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("# Written by HTTP Monitor from the project's HttpMonitorSettings asset. Edit the asset, not this file.\n");
            sb.Append("weavingEnabled=").Append(WeavingEnabled ? "true" : "false").Append('\n');
            sb.Append("weaveReleaseBuilds=").Append(WeaveReleaseBuilds ? "true" : "false").Append('\n');
            sb.Append("excludedAssemblies=").Append(string.Join(";", ExcludedAssemblies)).Append('\n');

            return sb.ToString();
        }

        public bool IsExcluded(string assemblyName)
        {
            foreach (var excluded in ExcludedAssemblies)
            {
                if (string.Equals(excluded, assemblyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;

            return fallback;
        }
    }
}
