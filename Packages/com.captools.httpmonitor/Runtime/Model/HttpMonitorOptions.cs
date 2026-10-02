using System;
using System.Collections.Generic;

namespace HttpMonitor
{
    /// <summary>
    /// Capture limits and privacy rules for one <see cref="HttpMonitorSession"/>. Read by the SDK
    /// on every capture without locking, so set these at startup rather than while requests fly.
    /// </summary>
    public sealed class HttpMonitorOptions
    {
        public const int DefaultMaxBodyBytes = 1024 * 1024;
        public const long DefaultMaxTotalBodyBytes = 64L * 1024 * 1024;
        public const string DefaultRedactedValue = "<redacted>";

        /// <summary>When false, no request or response body is read or stored. Default true.</summary>
        public bool CaptureBodies { get; set; } = true;

        /// <summary>Per-body cap. Longer bodies keep their first bytes and are flagged truncated. Default 1 MB.</summary>
        public int MaxBodyBytes { get; set; } = DefaultMaxBodyBytes;

        /// <summary>Cap on all stored bodies in a session. Oldest records are evicted to stay under it. Default 64 MB.</summary>
        public long MaxTotalBodyBytes { get; set; } = DefaultMaxTotalBodyBytes;

        /// <summary>
        /// HttpClient only: a response with no Content-Length (chunked) can only be captured by buffering
        /// it fully, which defeats streaming consumers. Default true; set false for streaming APIs.
        /// </summary>
        public bool BufferUnknownLengthResponses { get; set; } = true;

        /// <summary>Replacement written in place of a redacted header value.</summary>
        public string RedactedValue { get; set; } = DefaultRedactedValue;

        /// <summary>
        /// Header names whose values are never stored, request or response side. Case-insensitive.
        /// Defaults: Authorization, Proxy-Authorization, Cookie, Set-Cookie.
        /// </summary>
        public ISet<string> RedactedHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization",
            "Proxy-Authorization",
            "Cookie",
            "Set-Cookie",
        };

        internal bool IsRedacted(string headerName)
        {
            return headerName != null && RedactedHeaders.Contains(headerName);
        }

        /// <summary>Back to the shipped values; what applies when the project has no settings asset.</summary>
        public void ResetToDefaults()
        {
            CaptureBodies = true;
            MaxBodyBytes = DefaultMaxBodyBytes;
            MaxTotalBodyBytes = DefaultMaxTotalBodyBytes;
            BufferUnknownLengthResponses = true;
            RedactedValue = DefaultRedactedValue;
            RedactedHeaders.Clear();

            foreach (var name in HttpMonitorSettings.DefaultRedactedHeaders)
                RedactedHeaders.Add(name);
        }
    }
}
