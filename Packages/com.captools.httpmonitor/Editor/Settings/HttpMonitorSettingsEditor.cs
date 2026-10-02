using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HttpMonitor.CodeGen;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Editor-side lifecycle of the <see cref="HttpMonitorSettings"/> asset: finding or creating it,
    /// keeping it in the player's preloaded assets so it ships, applying it after domain reloads,
    /// and mirroring its weaving fields into the file the post-processor reads.
    /// </summary>
    public static class HttpMonitorSettingsEditor
    {
        /// <summary>Fired after the asset was created, replaced, or its weaving mirror rewritten.</summary>
        public static event Action Changed;

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            // Delayed: the AssetDatabase is not ready inside InitializeOnLoad on a cold start.
            EditorApplication.delayCall += () => EnsureApplied(requestRecompileIfMirrorDiffers: true);
        }

        /// <summary>The project's settings asset, or null. Warns once when there is more than one.</summary>
        public static HttpMonitorSettings Find()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(HttpMonitorSettings));

            if (guids.Length == 0)
                return null;

            if (guids.Length > 1)
                Debug.LogWarning($"[HttpMonitor] {guids.Length} HttpMonitorSettings assets found; using {AssetDatabase.GUIDToAssetPath(guids[0])}. Delete the others.");

            return AssetDatabase.LoadAssetAtPath<HttpMonitorSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>Creates the asset at the default path when the project has none.</summary>
        public static HttpMonitorSettings GetOrCreate()
        {
            var existing = Find();

            if (existing != null)
                return existing;

            var directory = Path.GetDirectoryName(HttpMonitorSettings.DefaultAssetPath);

            if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
                Directory.CreateDirectory(directory);

            var settings = ScriptableObject.CreateInstance<HttpMonitorSettings>();
            AssetDatabase.CreateAsset(settings, HttpMonitorSettings.DefaultAssetPath);
            AssetDatabase.SaveAssets();

            EnsureApplied(requestRecompileIfMirrorDiffers: false);
            Changed?.Invoke();

            return settings;
        }

        /// <summary>
        /// Loads the asset (which applies it to the session through OnEnable), registers it as a
        /// preloaded asset, and rewrites the weaver mirror. When the mirror on disk said something
        /// else than the asset, the last compilation used stale weaving settings, so a recompile is
        /// requested once.
        /// </summary>
        public static void EnsureApplied(bool requestRecompileIfMirrorDiffers)
        {
            var settings = Find();

            if (settings == null)
            {
                HttpMonitorSession.Current.Options.ResetToDefaults();

                return;
            }

            settings.ApplyTo(HttpMonitorSession.Current.Options);
            EnsurePreloaded(settings);

            if (WriteWeaverMirror(settings) && requestRecompileIfMirrorDiffers)
                RequestRecompile();
        }

        /// <summary>Writes the weaving fields to the mirror file. Returns true when the file changed.</summary>
        public static bool WriteWeaverMirror(HttpMonitorSettings settings)
        {
            var config = ToWeaverConfig(settings);
            var text = config.Serialize();
            var path = Path.Combine(Directory.GetCurrentDirectory(), WeaverConfig.RelativePath);

            try
            {
                if (File.Exists(path) && File.ReadAllText(path) == text)
                    return false;

                File.WriteAllText(path, text);
                Changed?.Invoke();

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HttpMonitor] could not write {WeaverConfig.RelativePath}: {e.Message}");

                return false;
            }
        }

        public static WeaverConfig ToWeaverConfig(HttpMonitorSettings settings)
        {
            return new WeaverConfig
            {
                WeavingEnabled = settings.WeavingEnabled,
                WeaveReleaseBuilds = settings.WeaveReleaseBuilds,
                ExcludedAssemblies = (settings.ExcludedAssemblies ?? Array.Empty<string>())
                    .Select(n => n?.Trim())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList(),
            };
        }

        /// <summary>Weaving changes only take effect when assemblies recompile; this forces a full pass.</summary>
        public static void RequestRecompile()
        {
            CompilationPipeline.RequestScriptCompilation();
        }

        /// <summary>Preloaded assets load before the first scene in players, which is how the settings reach a build.</summary>
        public static void EnsurePreloaded(HttpMonitorSettings settings)
        {
            var preloaded = PlayerSettings.GetPreloadedAssets() ?? Array.Empty<UnityEngine.Object>();

            if (preloaded.Contains(settings))
                return;

            var list = new List<UnityEngine.Object>(preloaded.Where(o => o != null && !(o is HttpMonitorSettings))) { settings };
            PlayerSettings.SetPreloadedAssets(list.ToArray());
        }
    }
}
