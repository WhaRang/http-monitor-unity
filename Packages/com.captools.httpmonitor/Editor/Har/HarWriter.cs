using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Serializes records as HAR 1.2, the format Chrome, Firefox, Charles and Proxyman all open.
    /// Standard fields carry what HAR can express; an <c>_httpMonitor</c> object on each entry
    /// carries what it cannot (client, capture source, state, error, truncation), which
    /// <see cref="HarReader"/> restores on import and other tools ignore.
    /// </summary>
    internal static class HarWriter
    {
        public const string CreatorName = "HTTP Monitor for Unity";

        public static string Write(IReadOnlyList<EditorRecord> records, string creatorVersion)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\"log\":{\"version\":\"1.2\",\"creator\":{\"name\":");
            MiniJson.WriteString(sb, CreatorName);
            sb.Append(",\"version\":");
            MiniJson.WriteString(sb, creatorVersion ?? "0.0.0");
            sb.Append("},\"entries\":[");

            for (var i = 0; i < records.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');

                WriteEntry(sb, records[i]);
            }

            sb.Append("]}}");

            return sb.ToString();
        }

        private static void WriteEntry(StringBuilder sb, EditorRecord record)
        {
            var time = record.IsFinished ? Math.Round(record.DurationMs, 3) : -1;

            sb.Append("{\"startedDateTime\":");
            MiniJson.WriteString(sb, record.StartedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            sb.Append(",\"time\":");
            MiniJson.WriteNumber(sb, time);

            WriteRequest(sb, record);
            WriteResponse(sb, record);

            sb.Append(",\"cache\":{},\"timings\":{\"blocked\":-1,\"dns\":-1,\"connect\":-1,\"send\":0,\"wait\":");
            MiniJson.WriteNumber(sb, Math.Max(0, time));
            sb.Append(",\"receive\":0,\"ssl\":-1}");

            sb.Append(",\"_httpMonitor\":{\"id\":");
            MiniJson.WriteNumber(sb, record.Id);
            sb.Append(",\"client\":");
            MiniJson.WriteString(sb, record.Client.ToString());
            sb.Append(",\"source\":");
            MiniJson.WriteString(sb, record.Source.ToString());
            sb.Append(",\"state\":");
            MiniJson.WriteString(sb, record.State.ToString());
            sb.Append(",\"error\":");
            MiniJson.WriteString(sb, record.Error);
            sb.Append(",\"requestBodyTruncated\":").Append(record.RequestBodyTruncated ? "true" : "false");
            sb.Append(",\"responseBodyTruncated\":").Append(record.ResponseBodyTruncated ? "true" : "false");
            sb.Append(",\"replayOf\":");
            MiniJson.WriteNumber(sb, record.ReplayOfId);
            sb.Append("}}");
        }

        private static void WriteRequest(StringBuilder sb, EditorRecord record)
        {
            sb.Append(",\"request\":{\"method\":");
            MiniJson.WriteString(sb, record.Method);
            sb.Append(",\"url\":");
            MiniJson.WriteString(sb, record.Url);
            sb.Append(",\"httpVersion\":\"HTTP/1.1\",\"cookies\":[],\"headers\":");
            WriteHeaders(sb, record.RequestHeaders);
            sb.Append(",\"queryString\":");
            WriteQueryString(sb, record.Url);

            if (record.RequestBody != null)
            {
                sb.Append(",\"postData\":{\"mimeType\":");
                MiniJson.WriteString(sb, RecordFormat.HeaderValue(record.RequestHeaders, "Content-Type") ?? "application/octet-stream");
                WriteBodyText(sb, record.RequestBody, "_encoding");
                sb.Append('}');
            }

            sb.Append(",\"headersSize\":-1,\"bodySize\":");
            MiniJson.WriteNumber(sb, record.UploadedBytes);
            sb.Append('}');
        }

        private static void WriteResponse(StringBuilder sb, EditorRecord record)
        {
            var statusText = record.State == HttpRecordState.Completed
                ? RecordFormat.ReasonPhrase(record.StatusCode)
                : record.Error ?? record.State.ToString();

            sb.Append(",\"response\":{\"status\":");
            MiniJson.WriteNumber(sb, record.StatusCode);
            sb.Append(",\"statusText\":");
            MiniJson.WriteString(sb, statusText ?? string.Empty);
            sb.Append(",\"httpVersion\":\"HTTP/1.1\",\"cookies\":[],\"headers\":");
            WriteHeaders(sb, record.ResponseHeaders);

            sb.Append(",\"content\":{\"size\":");
            MiniJson.WriteNumber(sb, record.DownloadedBytes);
            sb.Append(",\"mimeType\":");
            MiniJson.WriteString(sb, RecordFormat.HeaderValue(record.ResponseHeaders, "Content-Type") ?? string.Empty);

            if (record.ResponseBody != null)
                WriteBodyText(sb, record.ResponseBody, "encoding");

            sb.Append("},\"redirectURL\":");
            MiniJson.WriteString(sb, RecordFormat.HeaderValue(record.ResponseHeaders, "Location") ?? string.Empty);
            sb.Append(",\"headersSize\":-1,\"bodySize\":");
            MiniJson.WriteNumber(sb, record.DownloadedBytes);
            sb.Append('}');
        }

        /// <summary>Text bodies as text; binary bodies as base64 with the encoding flag HAR defines for responses (and our own name for requests).</summary>
        private static void WriteBodyText(StringBuilder sb, byte[] body, string encodingKey)
        {
            sb.Append(",\"text\":");

            if (RecordFormat.LooksLikeText(body))
            {
                MiniJson.WriteString(sb, Encoding.UTF8.GetString(body));
            }
            else
            {
                MiniJson.WriteString(sb, Convert.ToBase64String(body));
                sb.Append(",\"").Append(encodingKey).Append("\":\"base64\"");
            }
        }

        private static void WriteHeaders(StringBuilder sb, EditorHeader[] headers)
        {
            sb.Append('[');

            for (var i = 0; headers != null && i < headers.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');

                sb.Append("{\"name\":");
                MiniJson.WriteString(sb, headers[i].Name);
                sb.Append(",\"value\":");
                MiniJson.WriteString(sb, headers[i].Value);
                sb.Append('}');
            }

            sb.Append(']');
        }

        private static void WriteQueryString(StringBuilder sb, string url)
        {
            sb.Append('[');
            var first = true;

            foreach (var pair in QueryPairs(url))
            {
                if (!first)
                    sb.Append(',');

                first = false;
                sb.Append("{\"name\":");
                MiniJson.WriteString(sb, pair.Key);
                sb.Append(",\"value\":");
                MiniJson.WriteString(sb, pair.Value);
                sb.Append('}');
            }

            sb.Append(']');
        }

        internal static IEnumerable<KeyValuePair<string, string>> QueryPairs(string url)
        {
            var q = url?.IndexOf('?') ?? -1;

            if (q < 0 || q == url.Length - 1)
                yield break;

            var query = url.Substring(q + 1);
            var hash = query.IndexOf('#');

            if (hash >= 0)
                query = query.Substring(0, hash);

            foreach (var part in query.Split('&'))
            {
                if (part.Length == 0)
                    continue;

                var eq = part.IndexOf('=');
                var name = eq < 0 ? part : part.Substring(0, eq);
                var value = eq < 0 ? string.Empty : part.Substring(eq + 1);

                yield return new KeyValuePair<string, string>(Unescape(name), Unescape(value));
            }
        }

        private static string Unescape(string s)
        {
            try
            {
                return Uri.UnescapeDataString(s.Replace('+', ' '));
            }
            catch (Exception)
            {
                return s;
            }
        }
    }
}
