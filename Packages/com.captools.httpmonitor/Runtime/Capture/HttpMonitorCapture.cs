using System;
using System.Collections.Generic;
using System.Net.Http;
using UnityEngine.Networking;

namespace HttpMonitor
{
    /// <summary>
    /// Manual capture, for code the weaver cannot reach: precompiled DLLs, assemblies marked
    /// <see cref="DoNotWeaveAttribute"/>, projects with weaving disabled, and clients the SDK does not
    /// know (Best HTTP, raw sockets). Records created here carry <see cref="HttpCaptureSource.Manual"/>;
    /// a request the weaver already captured is never recorded twice, it just gains the Manual bit.
    ///
    /// Nothing here throws. On failure the call is reported through <see cref="HttpMonitorLog"/> and
    /// returns null (or the unwrapped object where one is expected back).
    /// </summary>
    public static class HttpMonitorCapture
    {
        /// <summary>
        /// Tracks a UnityWebRequest at any point in its life. Before send: the request is remembered
        /// and recorded when it is sent (immediately if the send is woven, otherwise on the next Track
        /// call). After send: recorded now; finished now if it is done, otherwise finished when a
        /// later Track call finds it done, or by <see cref="Track(UnityWebRequestAsyncOperation)"/>.
        /// Without weaving, call it once after the request completed and before disposing it.
        /// </summary>
        /// <returns>The record, or null when nothing was recorded yet (not sent, recording paused, failure).</returns>
        public static HttpRecord Track(UnityWebRequest request)
        {
            if (request == null)
                return null;

            try
            {
                return UnityWebRequestCapture.Track(request);
            }
            catch (Exception e)
            {
                Warn("Track(UnityWebRequest)", e);

                return null;
            }
        }

        /// <summary>
        /// One-liner for the send site: <c>yield return HttpMonitorCapture.Track(request.SendWebRequest());</c>.
        /// Records the request and finishes it when the operation completes. Note that in a coroutine the
        /// completion event fires after the coroutine resumed, so with a <c>using</c> block and no weaving
        /// the response is lost; call <see cref="Track(UnityWebRequest)"/> after the yield instead.
        /// </summary>
        /// <returns>The same operation, for yielding or awaiting.</returns>
        public static UnityWebRequestAsyncOperation Track(UnityWebRequestAsyncOperation operation)
        {
            if (operation == null)
                return null;

            try
            {
                UnityWebRequestCapture.TrackOperation(operation);
            }
            catch (Exception e)
            {
                Warn("Track(UnityWebRequestAsyncOperation)", e);
            }

            return operation;
        }

        /// <summary>
        /// A monitoring handler to put in front of an <see cref="HttpClient"/>'s real handler:
        /// <c>new HttpClient(HttpMonitorCapture.CreateHandler())</c>. Disposing the returned handler
        /// disposes the inner one, as HttpClient expects.
        /// </summary>
        /// <param name="innerHandler">The real handler; a default <see cref="HttpClientHandler"/> when null.</param>
        public static HttpMessageHandler CreateHandler(HttpMessageHandler innerHandler = null)
        {
            var inner = innerHandler ?? new HttpClientHandler();

            try
            {
                if (MonitorHandler.FindInChain(inner) is MonitorHandler existing)
                {
                    existing.AddSource(HttpCaptureSource.Manual);

                    return inner;
                }

                return new MonitorHandler(inner, HttpCaptureSource.Manual);
            }
            catch (Exception e)
            {
                Warn("CreateHandler", e);

                return inner;
            }
        }

        /// <summary>A monitored <see cref="HttpClient"/>, equivalent to <c>new HttpClient(CreateHandler(innerHandler), disposeHandler)</c>.</summary>
        public static HttpClient CreateClient(HttpMessageHandler innerHandler = null, bool disposeHandler = true)
        {
            return new HttpClient(CreateHandler(innerHandler), disposeHandler);
        }

        /// <summary>
        /// Records a request made by a client the SDK cannot observe. Finish the returned handle with
        /// Complete, Fail or Abort. Headers and bodies are redacted and capped like everything else.
        /// </summary>
        /// <returns>The handle, or null when recording is paused or the call failed.</returns>
        public static HttpCaptureHandle Begin(string method, string url, IReadOnlyList<HttpHeader> requestHeaders = null, byte[] requestBody = null)
        {
            try
            {
                var session = HttpMonitorSession.Current;

                if (!session.IsRecording)
                    return null;

                var record = session.Begin(HttpClientKind.Custom, HttpCaptureSource.Manual, method, url, requestHeaders, requestBody);

                return new HttpCaptureHandle(session, record, requestBody?.Length ?? 0);
            }
            catch (Exception e)
            {
                Warn("Begin", e);

                return null;
            }
        }

        private static void Warn(string api, Exception e)
        {
            HttpMonitorLog.Warning($"HttpMonitorCapture.{api} failed: {e.GetType().Name}: {e.Message}");
        }
    }
}
