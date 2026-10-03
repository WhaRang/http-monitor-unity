using System;
using System.Collections.Generic;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Values the user typed for redacted headers, kept in memory for the Editor session only.
    /// Keyed by host and header name, so a token pasted once serves every replay to that host.
    /// A static dictionary with no serialization: a domain reload or Editor restart forgets it,
    /// which is the point. Nothing here ever reaches disk, a record, or a HAR file.
    /// </summary>
    internal static class ReplaySecrets
    {
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void Remember(string host, string headerName, string value)
        {
            if (string.IsNullOrEmpty(headerName))
                return;

            if (string.IsNullOrEmpty(value))
                Values.Remove(Key(host, headerName));
            else
                Values[Key(host, headerName)] = value;
        }

        public static bool TryGet(string host, string headerName, out string value)
        {
            return Values.TryGetValue(Key(host, headerName), out value);
        }

        /// <summary>Supplies every remembered value the request is missing. Returns true when none remain redacted.</summary>
        public static bool Fill(ReplayRequest request)
        {
            var host = request.Host;

            foreach (var name in new List<string>(request.RedactedHeaderNames))
            {
                if (TryGet(host, name, out var value))
                    request.SupplyRedactedValue(name, value);
            }

            return !request.HasRedactedHeaders;
        }

        public static void Forget()
        {
            Values.Clear();
        }

        public static int Count => Values.Count;

        private static string Key(string host, string headerName)
        {
            return (host ?? string.Empty) + "\n" + headerName;
        }
    }
}
