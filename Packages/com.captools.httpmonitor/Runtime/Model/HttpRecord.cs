using System;
using System.Collections.Generic;

namespace HttpMonitor
{
    /// <summary>
    /// One captured request. The request part is fixed at creation; the response part is written
    /// exactly once by the SDK, under the session lock, before <see cref="HttpMonitorSession.RecordUpdated"/>
    /// fires. User code only reads.
    /// </summary>
    public sealed class HttpRecord
    {
        private static readonly IReadOnlyList<HttpHeader> NoHeaders = Array.Empty<HttpHeader>();

        public long Id { get; }
        public HttpClientKind Client { get; }
        public string Method { get; }
        public string Url { get; }
        public DateTime StartedAtUtc { get; }
        public IReadOnlyList<HttpHeader> RequestHeaders { get; }

        public HttpRecordState State { get; private set; }
        public double DurationMs { get; private set; }

        /// <summary>HTTP status code, or 0 when no response arrived.</summary>
        public long StatusCode { get; private set; }

        /// <summary>Transport-level failure text. Null unless <see cref="State"/> is Failed, Aborted or Incomplete.</summary>
        public string Error { get; private set; }

        public IReadOnlyList<HttpHeader> ResponseHeaders { get; private set; }
        public long UploadedBytes { get; private set; }
        public long DownloadedBytes { get; private set; }

        public bool IsFinished => State != HttpRecordState.Pending;

        internal HttpRecord(long id, HttpClientKind client, string method, string url, IReadOnlyList<HttpHeader> requestHeaders)
        {
            Id = id;
            Client = client;
            Method = method ?? string.Empty;
            Url = url ?? string.Empty;
            StartedAtUtc = DateTime.UtcNow;
            RequestHeaders = requestHeaders ?? NoHeaders;
            State = HttpRecordState.Pending;
            ResponseHeaders = NoHeaders;
        }

        internal void Finish(HttpRecordOutcome outcome)
        {
            State = outcome.State;
            DurationMs = outcome.DurationMs;
            StatusCode = outcome.StatusCode;
            Error = outcome.Error;
            ResponseHeaders = outcome.ResponseHeaders ?? NoHeaders;
            UploadedBytes = outcome.UploadedBytes;
            DownloadedBytes = outcome.DownloadedBytes;
        }

        public override string ToString()
        {
            var status = StatusCode != 0 ? " " + StatusCode : string.Empty;

            return $"#{Id} {Method} {Url} [{State}{status}]";
        }
    }

    /// <summary>Everything the SDK learns when a request ends. Built by the capture code, consumed by <see cref="HttpRecord.Finish"/>.</summary>
    internal struct HttpRecordOutcome
    {
        public HttpRecordState State;
        public double DurationMs;
        public long StatusCode;
        public string Error;
        public IReadOnlyList<HttpHeader> ResponseHeaders;
        public long UploadedBytes;
        public long DownloadedBytes;
    }
}
