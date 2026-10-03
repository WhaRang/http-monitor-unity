using System;
using System.Globalization;
using System.Text;

namespace HttpMonitor.Editor
{
    /// <summary>Pure formatting helpers for the window. No UI types, so they are unit-testable.</summary>
    internal static class RecordFormat
    {
        public static string StatusText(EditorRecord record)
        {
            switch (record.State)
            {
                case HttpRecordState.Pending: return "…";
                case HttpRecordState.Failed: return "failed";
                case HttpRecordState.Aborted: return "aborted";
                case HttpRecordState.Incomplete: return "?";
                default: return record.StatusCode.ToString();
            }
        }

        public static string StatusClass(EditorRecord record)
        {
            switch (record.State)
            {
                case HttpRecordState.Pending: return "hm-status-dot--pending";
                case HttpRecordState.Failed: return "hm-status-dot--failed";
                case HttpRecordState.Aborted:
                case HttpRecordState.Incomplete: return "hm-status-dot--aborted";
            }

            if (record.StatusCode >= 500) return "hm-status-dot--5xx";
            if (record.StatusCode >= 400) return "hm-status-dot--4xx";
            if (record.StatusCode >= 300) return "hm-status-dot--3xx";
            if (record.StatusCode >= 200) return "hm-status-dot--2xx";

            return string.Empty;
        }

        /// <summary>Tooltip for the status cell: the reason for a failure, or the reason phrase for a status code.</summary>
        public static string StatusTooltip(EditorRecord record)
        {
            switch (record.State)
            {
                case HttpRecordState.Pending: return "Waiting for a response";
                case HttpRecordState.Failed: return "No response: " + record.Error;
                case HttpRecordState.Aborted: return "Aborted: " + record.Error;
                case HttpRecordState.Incomplete: return "Response could not be read: " + record.Error;
                default: return record.StatusCode + " " + ReasonPhrase(record.StatusCode);
            }
        }

        public static string SourceText(HttpCaptureSource source)
        {
            switch (source)
            {
                case HttpCaptureSource.Woven: return "automatic";
                case HttpCaptureSource.Manual: return "manual";
                case HttpCaptureSource.Woven | HttpCaptureSource.Manual: return "automatic + manual";
                default: return "unknown source";
            }
        }

        /// <summary>Badge letters for the source column.</summary>
        public static string SourceBadge(HttpCaptureSource source)
        {
            switch (source)
            {
                case HttpCaptureSource.Woven: return "A";
                case HttpCaptureSource.Manual: return "M";
                case HttpCaptureSource.Woven | HttpCaptureSource.Manual: return "A+M";
                default: return "?";
            }
        }

        /// <summary>USS class carrying the badge colour: green for automatic, blue for manual, purple for both.</summary>
        public static string SourceClass(HttpCaptureSource source)
        {
            switch (source)
            {
                case HttpCaptureSource.Woven: return "hm-source-badge--automatic";
                case HttpCaptureSource.Manual: return "hm-source-badge--manual";
                case HttpCaptureSource.Woven | HttpCaptureSource.Manual: return "hm-source-badge--both";
                default: return "hm-source-badge--unknown";
            }
        }

        /// <summary>Badge for a record: "R" for a replay, "HAR" for an import with no source of its own, else the capture source.</summary>
        public static string SourceBadge(EditorRecord record)
        {
            if (record.IsReplay)
                return "R";

            return record.Imported && record.Source == HttpCaptureSource.None ? "HAR" : SourceBadge(record.Source);
        }

        public static string SourceClass(EditorRecord record)
        {
            if (record.IsReplay)
                return "hm-source-badge--replay";

            return record.Imported && record.Source == HttpCaptureSource.None ? "hm-source-badge--imported" : SourceClass(record.Source);
        }

        public static string SourceText(EditorRecord record)
        {
            if (record.IsReplay)
                return $"replay of #{record.ReplayOfId}, sent from the Editor";

            var text = record.Source == HttpCaptureSource.None ? "no capture source" : SourceText(record.Source);

            return record.Imported ? text + ", imported from HAR" : text;
        }

        public static string ClientText(HttpClientKind client)
        {
            switch (client)
            {
                case HttpClientKind.UnityWebRequest: return "UnityWebRequest";
                case HttpClientKind.HttpClient: return "HttpClient";
                default: return "Custom";
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "—";
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture) + " KB";

            return (bytes / (1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture) + " MB";
        }

        public static string FormatDuration(EditorRecord record)
        {
            if (!record.IsFinished)
                return "…";

            var ms = record.DurationMs;

            if (ms < 1000)
                return ms.ToString("F0", CultureInfo.InvariantCulture) + " ms";

            return (ms / 1000.0).ToString("F2", CultureInfo.InvariantCulture) + " s";
        }

        public static string FormatStarted(EditorRecord record)
        {
            return record.StartedAtUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        /// <summary>Path plus query, like the Name column in browser dev tools. Falls back to the whole URL.</summary>
        public static string Name(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            {
                var name = uri.PathAndQuery;

                return string.IsNullOrEmpty(name) ? "/" : name;
            }

            return url ?? string.Empty;
        }

        public static string Host(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) ? uri.Host : string.Empty;
        }

        /// <summary>Short type from the response Content-Type ("json", "html", "png", "octet-stream").</summary>
        public static string ShortType(EditorRecord record)
        {
            var contentType = HeaderValue(record.ResponseHeaders, "Content-Type");

            if (string.IsNullOrEmpty(contentType))
                return record.IsFinished ? "—" : string.Empty;

            var slash = contentType.IndexOf('/');
            var semicolon = contentType.IndexOf(';');
            var end = semicolon < 0 ? contentType.Length : semicolon;
            var subtype = slash < 0 ? contentType.Substring(0, end) : contentType.Substring(slash + 1, end - slash - 1);
            var plus = subtype.IndexOf('+'); // application/problem+json → json

            if (plus >= 0)
                subtype = subtype.Substring(plus + 1);

            if (subtype.StartsWith("x-", StringComparison.OrdinalIgnoreCase))
                subtype = subtype.Substring(2);

            return subtype.Trim().ToLowerInvariant();
        }

        public static string HeaderValue(EditorHeader[] headers, string name)
        {
            if (headers == null)
                return null;

            foreach (var header in headers)
            {
                if (string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))
                    return header.Value;
            }

            return null;
        }

        /// <summary>
        /// A curl command line reproducing the request. Redacted header values are emitted as shell
        /// variables so the command is honest about what it does not know.
        /// </summary>
        public static string ToCurl(EditorRecord record, string redactedValue)
        {
            var sb = new StringBuilder("curl");

            if (!string.Equals(record.Method, "GET", StringComparison.OrdinalIgnoreCase))
                sb.Append(" -X ").Append(record.Method);

            sb.Append(" '").Append((record.Url ?? string.Empty).Replace("'", "'\\''")).Append('\'');

            foreach (var header in record.RequestHeaders ?? Array.Empty<EditorHeader>())
            {
                var value = header.Value == redactedValue
                    ? "$" + header.Name.ToUpperInvariant().Replace('-', '_')
                    : header.Value;

                sb.Append(" \\\n  -H '").Append(header.Name).Append(": ").Append(value.Replace("'", "'\\''")).Append('\'');
            }

            if (record.RequestBody != null && record.RequestBody.Length > 0)
            {
                if (LooksLikeText(record.RequestBody))
                    sb.Append(" \\\n  --data-raw '").Append(Encoding.UTF8.GetString(record.RequestBody).Replace("'", "'\\''")).Append('\'');
                else
                    sb.Append(" \\\n  --data-binary @request-body.bin  # binary body, ").Append(record.RequestBody.Length).Append(" bytes");
            }

            return sb.ToString();
        }

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// Text means: no control bytes other than tab, newline and carriage return, and valid UTF-8,
        /// judged on the first 512 bytes. The UTF-8 check is what separates a PNG (0x89 'P' 'N' 'G')
        /// from prose; a multibyte sequence cut by the 512-byte window is not held against the body.
        /// </summary>
        public static bool LooksLikeText(byte[] bytes)
        {
            if (bytes == null)
                return false;

            var limit = Math.Min(bytes.Length, 512);

            for (var i = 0; i < limit; i++)
            {
                var b = bytes[i];

                if (b == 0 || (b < 32 && b != 9 && b != 10 && b != 13))
                    return false;
            }

            try
            {
                StrictUtf8.GetString(bytes, 0, limit);

                return true;
            }
            catch (DecoderFallbackException e)
            {
                // Only a sequence that runs past the sampled window is forgivable.
                return limit < bytes.Length && e.Index >= limit - 3;
            }
        }

        public static string ReasonPhrase(long status)
        {
            switch (status)
            {
                case 100: return "Continue";
                case 101: return "Switching Protocols";
                case 200: return "OK";
                case 201: return "Created";
                case 202: return "Accepted";
                case 204: return "No Content";
                case 206: return "Partial Content";
                case 301: return "Moved Permanently";
                case 302: return "Found";
                case 303: return "See Other";
                case 304: return "Not Modified";
                case 307: return "Temporary Redirect";
                case 308: return "Permanent Redirect";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 402: return "Payment Required";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 408: return "Request Timeout";
                case 409: return "Conflict";
                case 410: return "Gone";
                case 412: return "Precondition Failed";
                case 413: return "Payload Too Large";
                case 415: return "Unsupported Media Type";
                case 418: return "I'm a teapot";
                case 422: return "Unprocessable Entity";
                case 429: return "Too Many Requests";
                case 500: return "Internal Server Error";
                case 501: return "Not Implemented";
                case 502: return "Bad Gateway";
                case 503: return "Service Unavailable";
                case 504: return "Gateway Timeout";
                default: return string.Empty;
            }
        }
    }
}
