using System;
using System.Collections.Generic;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// What a replay sends: a plain, editable description of a request. Built from a captured
    /// record (<see cref="From"/>) or by hand in the composer. Redacted header values are never in
    /// a record, so they arrive empty and are listed in <see cref="RedactedHeaderNames"/> for the
    /// UI to ask for.
    /// </summary>
    public sealed class ReplayRequest
    {
        public const int DefaultTimeoutSeconds = 30;

        public string Method = "GET";
        public string Url = string.Empty;
        public List<EditorHeader> Headers = new List<EditorHeader>();
        public byte[] Body;
        public bool FollowRedirects = true;
        public int TimeoutSeconds = DefaultTimeoutSeconds;

        /// <summary>Editor id of the record this request was built from; 0 when composed from scratch.</summary>
        public long OriginalId;

        /// <summary>Header names whose values were redacted in the original and are therefore blank here.</summary>
        public List<string> RedactedHeaderNames = new List<string>();

        public bool HasRedactedHeaders => RedactedHeaderNames.Count > 0;

        public bool HasBody => Body != null && Body.Length > 0;

        /// <summary>True for methods that normally carry no side effects; the UI asks before replaying the others.</summary>
        public bool IsSafeMethod => string.Equals(Method, "GET", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Method, "HEAD", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Method, "OPTIONS", StringComparison.OrdinalIgnoreCase);

        public static ReplayRequest From(EditorRecord record, string redactedValue)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            var request = new ReplayRequest
            {
                Method = string.IsNullOrEmpty(record.Method) ? "GET" : record.Method,
                Url = record.Url ?? string.Empty,
                Body = record.RequestBody == null ? null : (byte[])record.RequestBody.Clone(),
                OriginalId = record.Id,
            };

            foreach (var header in record.RequestHeaders ?? Array.Empty<EditorHeader>())
            {
                if (header.Value == redactedValue)
                {
                    request.Headers.Add(new EditorHeader(header.Name, string.Empty));
                    request.RedactedHeaderNames.Add(header.Name);
                }
                else
                {
                    request.Headers.Add(header);
                }
            }

            return request;
        }

        /// <summary>Fills in a value the user supplied for a redacted header; returns false when no such header is pending.</summary>
        public bool SupplyRedactedValue(string name, string value)
        {
            var index = RedactedHeaderNames.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
                return false;

            for (var i = 0; i < Headers.Count; i++)
            {
                if (string.Equals(Headers[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    Headers[i] = new EditorHeader(Headers[i].Name, value ?? string.Empty);
            }

            RedactedHeaderNames.RemoveAt(index);

            return true;
        }
    }
}
