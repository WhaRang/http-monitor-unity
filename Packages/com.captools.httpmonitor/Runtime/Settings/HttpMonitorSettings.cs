using System;
using UnityEngine;

namespace HttpMonitor
{
    /// <summary>
    /// Project-wide settings, stored as an asset so a team shares them through version control and
    /// so they ship inside builds (the Editor registers the asset as a preloaded asset). Loading the
    /// asset applies it to the runtime session; without an asset every value is at its default.
    ///
    /// Two groups with different lifetimes:
    /// - Weaving fields are consumed at compile time by the IL post-processor, which runs in a
    ///   separate process and reads a mirror of them from <c>ProjectSettings/HttpMonitorWeaver.cfg</c>
    ///   written by the Editor. Changing them takes effect on the next script compilation.
    /// - Capture fields are applied live to <see cref="HttpMonitorOptions"/>.
    /// </summary>
    public sealed class HttpMonitorSettings : ScriptableObject
    {
        public const string DefaultAssetPath = "Assets/HttpMonitor/HttpMonitorSettings.asset";

        public static readonly string[] DefaultRedactedHeaders = { "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie" };

        [Header("Automatic capture (weaving)")]
        [Tooltip("Rewrite UnityWebRequest and HttpClient call sites at compile time so requests are captured with no code changes. Off: only the manual API captures. Takes effect on the next script compilation.")]
        public bool WeavingEnabled = true;

        [Tooltip("Also weave release (non-development) player builds. Off by default: a shipped game carries no capture code on its request path.")]
        public bool WeaveReleaseBuilds;

        [Tooltip("Assembly names the weaver leaves untouched, one per entry (e.g. ThirdParty.Networking). [assembly: HttpMonitor.DoNotWeave] does the same from code.")]
        public string[] ExcludedAssemblies = Array.Empty<string>();

        [Header("Bodies")]
        [Tooltip("Off: no request or response body is read or stored.")]
        public bool CaptureBodies = true;

        [Tooltip("Longer bodies keep their first bytes and are flagged truncated.")]
        [Min(0)] public int MaxBodyKilobytes = HttpMonitorOptions.DefaultMaxBodyBytes / 1024;

        [Tooltip("Cap on all stored bodies in the runtime session; oldest records are evicted to stay under it.")]
        [Min(0)] public int MaxTotalBodyMegabytes = (int)(HttpMonitorOptions.DefaultMaxTotalBodyBytes / (1024 * 1024));

        [Tooltip("HttpClient only. A response with no Content-Length can only be captured by buffering it fully, which defeats streaming consumers. Turn off for streaming APIs.")]
        public bool BufferUnknownLengthResponses = true;

        [Header("Privacy")]
        [Tooltip("Values of these headers are never stored, on either side. Case-insensitive.")]
        public string[] RedactedHeaders = (string[])DefaultRedactedHeaders.Clone();

        [Tooltip("What is stored in place of a redacted value.")]
        public string RedactedValue = HttpMonitorOptions.DefaultRedactedValue;

        /// <summary>The loaded settings asset, or null when the project has none (defaults apply).</summary>
        public static HttpMonitorSettings Current { get; private set; }

        private void OnEnable()
        {
            Current = this;
            ApplyTo(HttpMonitorSession.Current.Options);
        }

        private void OnDisable()
        {
            if (Current == this)
                Current = null;
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(RedactedValue))
                RedactedValue = HttpMonitorOptions.DefaultRedactedValue;

            ApplyTo(HttpMonitorSession.Current.Options);
        }

        /// <summary>Copies the capture fields onto a live options object. Weaving fields are not involved.</summary>
        public void ApplyTo(HttpMonitorOptions options)
        {
            if (options == null)
                return;

            options.CaptureBodies = CaptureBodies;
            options.MaxBodyBytes = Math.Max(0, MaxBodyKilobytes) * 1024;
            options.MaxTotalBodyBytes = Math.Max(0, MaxTotalBodyMegabytes) * 1024L * 1024L;
            options.BufferUnknownLengthResponses = BufferUnknownLengthResponses;
            options.RedactedValue = string.IsNullOrEmpty(RedactedValue) ? HttpMonitorOptions.DefaultRedactedValue : RedactedValue;
            options.RedactedHeaders.Clear();

            foreach (var header in RedactedHeaders ?? Array.Empty<string>())
            {
                var trimmed = header?.Trim();

                if (!string.IsNullOrEmpty(trimmed))
                    options.RedactedHeaders.Add(trimmed);
            }
        }

        public void ResetToDefaults()
        {
            WeavingEnabled = true;
            WeaveReleaseBuilds = false;
            ExcludedAssemblies = Array.Empty<string>();
            CaptureBodies = true;
            MaxBodyKilobytes = HttpMonitorOptions.DefaultMaxBodyBytes / 1024;
            MaxTotalBodyMegabytes = (int)(HttpMonitorOptions.DefaultMaxTotalBodyBytes / (1024 * 1024));
            BufferUnknownLengthResponses = true;
            RedactedHeaders = (string[])DefaultRedactedHeaders.Clone();
            RedactedValue = HttpMonitorOptions.DefaultRedactedValue;
            ApplyTo(HttpMonitorSession.Current.Options);
        }
    }
}
