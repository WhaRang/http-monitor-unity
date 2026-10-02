using System;
using System.Collections.Generic;
using UnityEngine;

namespace HttpMonitor.Editor
{
    /// <summary>A header line in a Unity-serializable shape.</summary>
    [Serializable]
    public struct EditorHeader
    {
        public string Name;
        public string Value;

        public EditorHeader(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public override string ToString()
        {
            return Name + ": " + Value;
        }
    }

    /// <summary>
    /// The Editor's own copy of an <see cref="HttpRecord"/>: plain serializable fields so it survives
    /// domain reloads and Play Mode exit. Created from a runtime record on the main thread and
    /// updated in place when the runtime record finishes; <see cref="Revision"/> bumps on every change
    /// so views can refresh only what moved.
    /// </summary>
    [Serializable]
    public sealed class EditorRecord
    {
        private static readonly EditorHeader[] NoHeaders = Array.Empty<EditorHeader>();

        /// <summary>Editor-side id, monotonic across domain reloads. Runtime ids restart with every reload.</summary>
        public long Id;
        public long RuntimeId;
        public HttpClientKind Client;
        public HttpCaptureSource Source;
        public string Method;
        public string Url;
        public long StartedAtUtcTicks;
        public EditorHeader[] RequestHeaders = NoHeaders;
        public byte[] RequestBody;
        public bool RequestBodyTruncated;

        public HttpRecordState State;
        public double DurationMs;
        public long StatusCode;
        public string Error;
        public EditorHeader[] ResponseHeaders = NoHeaders;
        public byte[] ResponseBody;
        public bool ResponseBodyTruncated;
        public long UploadedBytes;
        public long DownloadedBytes;

        public int Revision;

        /// <summary>True for records loaded from a HAR file rather than captured in this Editor.</summary>
        public bool Imported;

        /// <summary>The runtime record this was copied from; null after a domain reload or for imports.</summary>
        [NonSerialized] public HttpRecord Runtime;

        public DateTime StartedAtUtc => new DateTime(StartedAtUtcTicks, DateTimeKind.Utc);

        public bool IsFinished => State != HttpRecordState.Pending;

        public bool IsError => State == HttpRecordState.Failed || State == HttpRecordState.Aborted
            || State == HttpRecordState.Incomplete || StatusCode >= 400;

        public long StoredBodyBytes => (RequestBody?.Length ?? 0) + (ResponseBody?.Length ?? 0);

        internal static EditorRecord From(HttpRecord runtime, long id)
        {
            var record = new EditorRecord
            {
                Id = id,
                RuntimeId = runtime.Id,
                Client = runtime.Client,
                Method = runtime.Method,
                Url = runtime.Url,
                StartedAtUtcTicks = runtime.StartedAtUtc.Ticks,
                RequestHeaders = Copy(runtime.RequestHeaders),
                RequestBody = runtime.RequestBody,
                RequestBodyTruncated = runtime.RequestBodyTruncated,
                Runtime = runtime,
            };

            record.CopyOutcome(runtime);

            return record;
        }

        /// <summary>Copies everything that can change after creation: the outcome and the source bits.</summary>
        internal void CopyOutcome(HttpRecord runtime)
        {
            Source = runtime.Source;
            State = runtime.State;
            DurationMs = runtime.DurationMs;
            StatusCode = runtime.StatusCode;
            Error = runtime.Error;
            ResponseHeaders = Copy(runtime.ResponseHeaders);
            ResponseBody = runtime.ResponseBody;
            ResponseBodyTruncated = runtime.ResponseBodyTruncated;
            UploadedBytes = runtime.UploadedBytes;
            DownloadedBytes = runtime.DownloadedBytes;
            Revision++;
        }

        internal void MarkIncomplete(string reason)
        {
            State = HttpRecordState.Incomplete;
            Error = reason;
            Revision++;
        }

        private static EditorHeader[] Copy(IReadOnlyList<HttpHeader> headers)
        {
            if (headers == null || headers.Count == 0)
                return NoHeaders;

            var copy = new EditorHeader[headers.Count];

            for (var i = 0; i < headers.Count; i++)
                copy[i] = new EditorHeader(headers[i].Name, headers[i].Value);

            return copy;
        }

        public override string ToString()
        {
            var status = StatusCode != 0 ? " " + StatusCode : string.Empty;

            return $"#{Id} {Method} {Url} [{State}{status}]";
        }
    }
}
