using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace HttpMonitor
{
    /// <summary>
    /// Returned by <see cref="HttpMonitorCapture.Begin"/> for clients the SDK cannot see into.
    /// Call exactly one of <see cref="Complete"/>, <see cref="Fail"/> or <see cref="Abort"/>; later
    /// calls are ignored. Safe from any thread; never throws.
    /// </summary>
    public sealed class HttpCaptureHandle
    {
        private readonly HttpMonitorSession _session;
        private readonly long _startedAtTicks;
        private readonly long _requestBodyLength;
        private int _finished;

        public HttpRecord Record { get; }

        internal HttpCaptureHandle(HttpMonitorSession session, HttpRecord record, long requestBodyLength)
        {
            _session = session;
            _startedAtTicks = Stopwatch.GetTimestamp();
            _requestBodyLength = requestBodyLength;
            Record = record;
        }

        public bool IsFinished => _finished != 0;

        /// <summary>A response arrived, with any status code.</summary>
        /// <param name="downloadedBytes">Full response size; defaults to the body length.</param>
        /// <param name="uploadedBytes">Full request size; defaults to the request body length given to Begin.</param>
        public void Complete(long statusCode, IReadOnlyList<HttpHeader> responseHeaders = null, byte[] responseBody = null,
            long downloadedBytes = -1, long uploadedBytes = -1)
        {
            Finish(new HttpRecordOutcome
            {
                State = HttpRecordState.Completed,
                StatusCode = statusCode,
                ResponseHeaders = responseHeaders,
                ResponseBody = responseBody,
                DownloadedBytes = downloadedBytes >= 0 ? downloadedBytes : responseBody?.Length ?? 0,
                UploadedBytes = uploadedBytes >= 0 ? uploadedBytes : _requestBodyLength,
            });
        }

        /// <summary>No response: DNS, connection, TLS or similar.</summary>
        public void Fail(string error)
        {
            Finish(new HttpRecordOutcome
            {
                State = HttpRecordState.Failed,
                Error = string.IsNullOrEmpty(error) ? "failed" : error,
            });
        }

        /// <summary>Cancelled by the game before a response arrived.</summary>
        public void Abort(string reason = null)
        {
            Finish(new HttpRecordOutcome
            {
                State = HttpRecordState.Aborted,
                Error = string.IsNullOrEmpty(reason) ? "aborted" : reason,
            });
        }

        private void Finish(HttpRecordOutcome outcome)
        {
            if (Interlocked.Exchange(ref _finished, 1) != 0)
                return;

            try
            {
                outcome.DurationMs = (Stopwatch.GetTimestamp() - _startedAtTicks) * 1000.0 / Stopwatch.Frequency;
                _session.Finish(Record, outcome);
            }
            catch (Exception e)
            {
                HttpMonitorLog.Warning($"manual capture finish failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
