using System;
using System.Collections.Generic;

namespace HttpMonitor.Editor
{
    /// <summary>Where a captured header goes when the request is rebuilt for HttpClient.</summary>
    internal enum HeaderPlacement
    {
        /// <summary>Not resent: hop-by-hop, or owned by the client (Host, Content-Length).</summary>
        Drop,

        /// <summary>On the request message.</summary>
        Request,

        /// <summary>On the content; only meaningful when there is a body.</summary>
        Content,
    }

    /// <summary>
    /// Pure classification of headers for replay. HttpClient computes Host and Content-Length
    /// itself and rejects them when set by hand; hop-by-hop headers describe the original
    /// connection, not the request; Content-* headers must sit on the content object.
    /// </summary>
    internal static class ReplayHeaders
    {
        private static readonly HashSet<string> Dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Host",
            "Content-Length",
            "Connection",
            "Keep-Alive",
            "Proxy-Connection",
            "Transfer-Encoding",
            "TE",
            "Trailer",
            "Upgrade",
            "Expect",
        };

        /// <summary>Headers that belong to the content even though they do not start with "Content-".</summary>
        private static readonly HashSet<string> ContentOthers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Allow",
            "Expires",
            "Last-Modified",
        };

        public static HeaderPlacement Classify(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Dropped.Contains(name))
                return HeaderPlacement.Drop;

            if (name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) || ContentOthers.Contains(name))
                return HeaderPlacement.Content;

            return HeaderPlacement.Request;
        }

        /// <summary>The Content-Type from a header list, or null.</summary>
        public static string ContentType(IEnumerable<EditorHeader> headers)
        {
            foreach (var header in headers)
            {
                if (string.Equals(header.Name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    return header.Value;
            }

            return null;
        }
    }
}
