using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace HttpMonitor
{
    /// <summary>
    /// Sits between an <see cref="HttpClient"/> and its real handler and records every exchange.
    /// Installed by the woven <c>new HttpClient(...)</c> rewrite or by the manual API.
    /// Runs on thread-pool threads: the session is locked, and session events fire on this thread.
    ///
    /// Bodies: a request body is read only when its Content-Length is known and within the cap
    /// (StringContent, ByteArrayContent, FormUrlEncodedContent). A response body is buffered when its
    /// length is known and within the cap, or when unknown and
    /// <see cref="HttpMonitorOptions.BufferUnknownLengthResponses"/> allows it. Everything else is
    /// recorded by size only, and the exchange itself is never altered.
    /// </summary>
    internal sealed class MonitorHandler : DelegatingHandler
    {
        private HttpCaptureSource _source;

        public MonitorHandler(HttpMessageHandler innerHandler, HttpCaptureSource source) : base(innerHandler)
        {
            _source = source;
        }

        public HttpCaptureSource Source => _source;

        public void AddSource(HttpCaptureSource source)
        {
            _source |= source;
        }

        /// <summary>Walks a DelegatingHandler chain looking for an already-installed monitor.</summary>
        public static MonitorHandler FindInChain(HttpMessageHandler handler)
        {
            while (handler != null)
            {
                if (handler is MonitorHandler monitor)
                    return monitor;

                if (!(handler is DelegatingHandler delegating))
                    return null;

                handler = delegating.InnerHandler;
            }

            return null;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var session = HttpMonitorSession.Current;

            if (!session.IsRecording)
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var startedAtTicks = Stopwatch.GetTimestamp();
            var record = await TryBeginAsync(session, request).ConfigureAwait(false);

            HttpResponseMessage response;

            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Finish(session, record, HttpRecordState.Aborted, "cancelled", startedAtTicks);
                throw;
            }
            catch (Exception e)
            {
                Finish(session, record, HttpRecordState.Failed, e.GetType().Name + ": " + e.Message, startedAtTicks);
                throw;
            }

            if (record != null)
                await TryCompleteAsync(session, record, request, response, startedAtTicks).ConfigureAwait(false);

            return response;
        }

        private async Task<HttpRecord> TryBeginAsync(HttpMonitorSession session, HttpRequestMessage request)
        {
            try
            {
                var body = await ReadRequestBodyAsync(request.Content, session.Options).ConfigureAwait(false);
                var headers = CollectHeaders(request.Headers, request.Content?.Headers);
                var url = request.RequestUri != null ? request.RequestUri.ToString() : string.Empty;

                return session.Begin(HttpClientKind.HttpClient, _source, request.Method.Method, url, headers, body);
            }
            catch (Exception e)
            {
                HttpMonitorLog.Warning($"HttpClient request capture failed: {e.GetType().Name}: {e.Message}");

                return null;
            }
        }

        private static async Task TryCompleteAsync(HttpMonitorSession session, HttpRecord record, HttpRequestMessage request,
            HttpResponseMessage response, long startedAtTicks)
        {
            try
            {
                var body = await ReadResponseBodyAsync(response.Content, session.Options).ConfigureAwait(false);
                var contentLength = response.Content?.Headers.ContentLength;

                session.Finish(record, new HttpRecordOutcome
                {
                    State = HttpRecordState.Completed,
                    DurationMs = ElapsedMs(startedAtTicks),
                    StatusCode = (long)response.StatusCode,
                    ResponseHeaders = CollectHeaders(response.Headers, response.Content?.Headers),
                    ResponseBody = body,
                    DownloadedBytes = body != null ? body.Length : contentLength ?? 0,
                    UploadedBytes = request.Content?.Headers.ContentLength ?? 0,
                });
            }
            catch (Exception e)
            {
                Finish(session, record, HttpRecordState.Incomplete, "response unreadable: " + e.Message, startedAtTicks);
                HttpMonitorLog.Warning($"HttpClient response capture failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Finish(HttpMonitorSession session, HttpRecord record, HttpRecordState state, string error, long startedAtTicks)
        {
            if (record == null)
                return;

            try
            {
                session.Finish(record, new HttpRecordOutcome
                {
                    State = state,
                    DurationMs = ElapsedMs(startedAtTicks),
                    Error = error,
                });
            }
            catch (Exception e)
            {
                HttpMonitorLog.Warning($"HttpClient record finish failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private static async Task<byte[]> ReadRequestBodyAsync(HttpContent content, HttpMonitorOptions options)
        {
            if (content == null || !options.CaptureBodies)
                return null;

            var length = content.Headers.ContentLength;

            if (length == null || length > options.MaxBodyBytes)
                return null;

            // Buffers the content; HttpClient sends the buffered copy, so the exchange is unchanged.
            return await content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        private static async Task<byte[]> ReadResponseBodyAsync(HttpContent content, HttpMonitorOptions options)
        {
            if (content == null || !options.CaptureBodies)
                return null;

            var length = content.Headers.ContentLength;

            if (length == null ? !options.BufferUnknownLengthResponses : length > options.MaxBodyBytes)
                return null;

            return await content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <summary>One entry per value, so repeated headers such as Set-Cookie are all kept.</summary>
        private static IReadOnlyList<HttpHeader> CollectHeaders(HttpHeaders headers, HttpHeaders contentHeaders)
        {
            var list = new List<HttpHeader>();

            Append(list, headers);
            Append(list, contentHeaders);

            return list.Count > 0 ? list : null;
        }

        private static void Append(List<HttpHeader> list, HttpHeaders headers)
        {
            if (headers == null)
                return;

            foreach (var pair in headers)
            {
                foreach (var value in pair.Value)
                    list.Add(new HttpHeader(pair.Key, value));
            }
        }

        private static double ElapsedMs(long startedAtTicks)
        {
            return (Stopwatch.GetTimestamp() - startedAtTicks) * 1000.0 / Stopwatch.Frequency;
        }
    }
}
