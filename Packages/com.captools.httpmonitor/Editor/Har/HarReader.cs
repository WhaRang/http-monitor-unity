using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Reads HAR 1.2 into <see cref="EditorRecord"/>s. Our own exports round-trip completely through
    /// the <c>_httpMonitor</c> extension; files from other tools map onto the closest state
    /// (status > 0 is Completed, otherwise Failed) with client Custom and no capture source.
    /// </summary>
    internal static class HarReader
    {
        /// <exception cref="FormatException">Not JSON, or JSON without a <c>log.entries</c> array.</exception>
        public static List<EditorRecord> Read(string json)
        {
            var root = MiniJson.AsObject(MiniJson.Parse(json));
            var log = MiniJson.GetObject(root, "log");
            var entries = MiniJson.GetArray(log, "entries");

            if (entries == null)
                throw new FormatException("Not a HAR file: no log.entries array");

            var records = new List<EditorRecord>(entries.Count);

            foreach (var item in entries)
            {
                var entry = MiniJson.AsObject(item);

                if (entry != null)
                    records.Add(ReadEntry(entry));
            }

            return records;
        }

        private static EditorRecord ReadEntry(Dictionary<string, object> entry)
        {
            var request = MiniJson.GetObject(entry, "request");
            var response = MiniJson.GetObject(entry, "response");
            var content = MiniJson.GetObject(response, "content");
            var postData = MiniJson.GetObject(request, "postData");
            var extension = MiniJson.GetObject(entry, "_httpMonitor");
            var status = (long)MiniJson.GetNumber(response, "status");
            var time = MiniJson.GetNumber(entry, "time", -1);

            var record = new EditorRecord
            {
                Imported = true,
                RuntimeId = (long)MiniJson.GetNumber(extension, "id"),
                Method = MiniJson.GetString(request, "method", "GET"),
                Url = MiniJson.GetString(request, "url", string.Empty),
                StartedAtUtcTicks = ParseStarted(MiniJson.GetString(entry, "startedDateTime")),
                RequestHeaders = ReadHeaders(MiniJson.GetArray(request, "headers")),
                RequestBody = ReadBody(postData, "_encoding"),
                UploadedBytes = (long)MiniJson.GetNumber(request, "bodySize"),
                StatusCode = status,
                ResponseHeaders = ReadHeaders(MiniJson.GetArray(response, "headers")),
                ResponseBody = ReadBody(content, "encoding"),
                DownloadedBytes = (long)Math.Max(MiniJson.GetNumber(content, "size"), MiniJson.GetNumber(response, "bodySize")),
                DurationMs = Math.Max(0, time),
            };

            if (extension != null)
            {
                record.Client = ParseEnum(MiniJson.GetString(extension, "client"), HttpClientKind.Custom);
                record.Source = ParseEnum(MiniJson.GetString(extension, "source"), HttpCaptureSource.None);
                record.State = ParseEnum(MiniJson.GetString(extension, "state"), status > 0 ? HttpRecordState.Completed : HttpRecordState.Failed);
                record.Error = MiniJson.GetString(extension, "error");
                record.RequestBodyTruncated = MiniJson.GetBool(extension, "requestBodyTruncated");
                record.ResponseBodyTruncated = MiniJson.GetBool(extension, "responseBodyTruncated");
            }
            else
            {
                record.Client = HttpClientKind.Custom;
                record.Source = HttpCaptureSource.None;
                record.State = status > 0 ? HttpRecordState.Completed : HttpRecordState.Failed;
                record.Error = status > 0 ? null : FirstNonEmpty(MiniJson.GetString(response, "statusText"), MiniJson.GetString(response, "_error"), "no response in the HAR file");
            }

            if (record.State == HttpRecordState.Pending)
                record.State = HttpRecordState.Incomplete; // a pending entry can never finish after import

            if (record.UploadedBytes < 0)
                record.UploadedBytes = record.RequestBody?.Length ?? 0;

            if (record.DownloadedBytes < 0)
                record.DownloadedBytes = record.ResponseBody?.Length ?? 0;

            return record;
        }

        private static long ParseStarted(string iso)
        {
            if (!string.IsNullOrEmpty(iso) && DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind | DateTimeStyles.AdjustToUniversal, out var started))
                return started.Ticks;

            return DateTime.UtcNow.Ticks;
        }

        private static EditorHeader[] ReadHeaders(List<object> headers)
        {
            if (headers == null)
                return new EditorHeader[0];

            var result = new List<EditorHeader>(headers.Count);

            foreach (var item in headers)
            {
                var header = MiniJson.AsObject(item);
                var name = MiniJson.GetString(header, "name");

                if (!string.IsNullOrEmpty(name))
                    result.Add(new EditorHeader(name, MiniJson.GetString(header, "value", string.Empty)));
            }

            return result.ToArray();
        }

        private static byte[] ReadBody(Dictionary<string, object> container, string encodingKey)
        {
            var text = MiniJson.GetString(container, "text");

            if (text == null)
                return null;

            if (string.Equals(MiniJson.GetString(container, encodingKey), "base64", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return Convert.FromBase64String(text);
                }
                catch (FormatException)
                {
                    return Encoding.UTF8.GetBytes(text);
                }
            }

            return Encoding.UTF8.GetBytes(text);
        }

        private static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            return !string.IsNullOrEmpty(value) && Enum.TryParse(value, true, out T parsed) ? parsed : fallback;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return null;
        }
    }
}
