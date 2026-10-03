using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Sends a <see cref="ReplayRequest"/> from the Editor process and records the exchange like
    /// any other request. The record is opened through the session before sending, so redaction and
    /// body caps apply exactly as for live traffic, and it is recorded even while capture is paused:
    /// the user asked to see this one.
    ///
    /// Honest limit: this is the Editor's HttpClient, not the game's. User-Agent, cookies and
    /// certificate handling differ from what a device sends.
    /// </summary>
    public sealed class ReplayService
    {
        private const int CopyBufferBytes = 64 * 1024;

        private readonly HttpMonitorSession _session;
        private readonly EditorRecordBuffer _buffer;

        /// <summary>Replays into the live session and the Editor store.</summary>
        public static ReplayService Default => new ReplayService(HttpMonitorSession.Current, EditorRecordStore.instance.Buffer);

        public ReplayService(HttpMonitorSession session, EditorRecordBuffer buffer)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _buffer = buffer;
        }

        /// <summary>
        /// Sends the request and returns the runtime record once the exchange has finished (in any
        /// state). The Editor copy is linked to <see cref="ReplayRequest.OriginalId"/> when set.
        /// Never throws for network failures; those end up in the record.
        /// </summary>
        public async Task<HttpRecord> SendAsync(ReplayRequest request, CancellationToken cancellation = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var headers = new List<HttpHeader>(request.Headers.Count);

            foreach (var header in request.Headers)
                headers.Add(new HttpHeader(header.Name, header.Value));

            var record = _session.Begin(HttpClientKind.HttpClient, HttpCaptureSource.Manual, request.Method, request.Url, headers, request.Body);
            var handle = new HttpCaptureHandle(_session, record, request.Body?.Length ?? 0);

            if (request.OriginalId > 0)
                _buffer?.MarkReplayOf(record, request.OriginalId);

            try
            {
                using (var handler = new HttpClientHandler { AllowAutoRedirect = request.FollowRedirects, UseCookies = false, AutomaticDecompression = DecompressionMethods.None })
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Max(1, request.TimeoutSeconds)) })
                using (var message = Build(request))
                using (var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
                {
                    var (prefix, total) = await ReadBoundedAsync(response.Content, _session.Options.MaxBodyBytes, cancellation).ConfigureAwait(false);

                    handle.Complete((long)response.StatusCode, CollectHeaders(response.Headers, response.Content?.Headers), prefix,
                        downloadedBytes: total, uploadedBytes: request.Body?.Length ?? 0);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                handle.Abort("cancelled");
            }
            catch (OperationCanceledException)
            {
                handle.Abort($"timed out after {request.TimeoutSeconds} s");
            }
            catch (HttpRequestException e)
            {
                handle.Fail(Describe(e));
            }
            catch (Exception e)
            {
                handle.Fail(e.GetType().Name + ": " + e.Message);
            }

            return record;
        }

        /// <summary>Builds the HttpClient message. Internal so the header mapping is testable without sending.</summary>
        internal static HttpRequestMessage Build(ReplayRequest request)
        {
            var message = new HttpRequestMessage(new HttpMethod(request.Method.ToUpperInvariant()), request.Url);
            var content = request.HasBody || !request.IsSafeMethod ? new ByteArrayContent(request.Body ?? Array.Empty<byte>()) : null;

            if (content != null)
            {
                content.Headers.ContentType = null; // ByteArrayContent sets none; the captured header, if any, is added below
                message.Content = content;
            }

            foreach (var header in request.Headers)
            {
                switch (ReplayHeaders.Classify(header.Name))
                {
                    case HeaderPlacement.Request:
                        message.Headers.TryAddWithoutValidation(header.Name, header.Value);

                        break;
                    case HeaderPlacement.Content:
                        content?.Headers.TryAddWithoutValidation(header.Name, header.Value);

                        break;
                }
            }

            return message;
        }

        /// <summary>Reads the whole response (so the size is right) but keeps only the first <paramref name="keep"/> bytes.</summary>
        private static async Task<(byte[] prefix, long total)> ReadBoundedAsync(HttpContent content, int keep, CancellationToken cancellation)
        {
            if (content == null)
                return (null, 0);

            using (var stream = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var kept = new MemoryStream())
            {
                var buffer = new byte[CopyBufferBytes];
                long total = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    var room = (int)Math.Max(0, Math.Min(read, keep - kept.Length));

                    if (room > 0)
                        kept.Write(buffer, 0, room);
                }

                return (total == 0 ? Array.Empty<byte>() : kept.ToArray(), total);
            }
        }

        private static IReadOnlyList<HttpHeader> CollectHeaders(HttpHeaders headers, HttpHeaders contentHeaders)
        {
            var list = new List<HttpHeader>();

            foreach (var source in new[] { headers, contentHeaders })
            {
                if (source == null)
                    continue;

                foreach (var pair in source)
                {
                    foreach (var value in pair.Value)
                        list.Add(new HttpHeader(pair.Key, value));
                }
            }

            return list;
        }

        private static string Describe(Exception e)
        {
            var message = e.Message;

            for (var inner = e.InnerException; inner != null; inner = inner.InnerException)
            {
                if (!string.IsNullOrEmpty(inner.Message) && !message.Contains(inner.Message))
                    message += ": " + inner.Message;
            }

            return message;
        }
    }
}
