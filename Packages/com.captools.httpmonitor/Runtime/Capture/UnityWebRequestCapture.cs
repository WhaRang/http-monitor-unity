using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine.Networking;

namespace HttpMonitor
{
    /// <summary>
    /// Everything the SDK knows about UnityWebRequest instances, shared by the woven entry points in
    /// <see cref="Interceptor"/> and the manual API in <see cref="HttpMonitorCapture"/>. Methods here
    /// may throw; the public callers wrap them and degrade to a warning.
    ///
    /// Response timing (M0 finding): when a coroutine does <c>yield return request.SendWebRequest()</c>,
    /// native completion resumes the coroutine synchronously and the managed <c>completed</c> event
    /// fires only afterwards. A <c>using</c> block has therefore already disposed the request by the
    /// time <c>completed</c> runs. So the response is captured by whichever comes first: the woven
    /// dispose, a manual Track call, or the <c>completed</c> event.
    /// </summary>
    internal static class UnityWebRequestCapture
    {
        internal sealed class Pending
        {
            public UnityWebRequest Request;
            public HttpMonitorSession Session;
            public HttpRecord Record;
            public long StartedAtTicks;
            public bool Claimed;
        }

        /// <summary>What we learn about a request before it is sent.</summary>
        private sealed class PreSend
        {
            public readonly List<HttpHeader> Headers = new List<HttpHeader>();
            public bool ManuallyTracked;
            public long TrackedAtTicks;
        }

        /// <summary>
        /// UnityWebRequest cannot enumerate its own request headers, so we remember what user code
        /// sets. Weak keys: a request that is never sent costs nothing once it is collected.
        /// </summary>
        private static readonly ConditionalWeakTable<UnityWebRequest, PreSend> PreSendByRequest = new ConditionalWeakTable<UnityWebRequest, PreSend>();

        /// <summary>One record per request, for as long as the request object lives.</summary>
        private static readonly ConditionalWeakTable<UnityWebRequest, HttpRecord> RecordByRequest = new ConditionalWeakTable<UnityWebRequest, HttpRecord>();

        private static readonly List<Pending> InFlight = new List<Pending>();

        // ---------------------------------------------------------------- woven entry points

        public static void RecordHeader(UnityWebRequest request, string name, string value)
        {
            var preSend = PreSendByRequest.GetValue(request, _ => new PreSend());

            lock (preSend)
                SetOrReplace(preSend.Headers, name, value);
        }

        /// <summary>Called by the woven send, before the real call. Null when not recording.</summary>
        public static Pending BeginWoven(UnityWebRequest request)
        {
            var session = HttpMonitorSession.Current;

            if (!session.IsRecording)
                return null;

            var preSend = TakePreSend(request);
            var source = HttpCaptureSource.Woven;

            if (preSend != null && preSend.ManuallyTracked)
                source |= HttpCaptureSource.Manual;

            return Begin(session, request, preSend, source, Stopwatch.GetTimestamp());
        }

        public static void HookCompletion(Pending pending, UnityWebRequestAsyncOperation operation)
        {
            operation.completed += _ => Complete(pending, "completed");
        }

        public static void OnDisposing(UnityWebRequest request)
        {
            var pending = FindPending(request);

            if (pending == null)
                return;

            if (request.isDone)
                Complete(pending, "dispose");
            else
                Abort(pending, "disposed before completion");
        }

        // ---------------------------------------------------------------- manual API

        /// <summary>
        /// Manual tracking at any point in the request's life:
        /// already captured (woven or earlier Track) → adds the Manual bit, finishes it if it is done;
        /// not sent yet → remembered, so the send (woven or a later Track) records it as manual;
        /// sent through an unwoven path → recorded now, finished now if done, else by <see cref="TrackOperation"/> or a later Track.
        /// </summary>
        public static HttpRecord Track(UnityWebRequest request)
        {
            if (RecordByRequest.TryGetValue(request, out var existing))
            {
                existing.AddSource(HttpCaptureSource.Manual);

                var claimed = FindPending(request);

                if (claimed != null && IsDone(request))
                    Complete(claimed, "track");

                return existing;
            }

            var session = HttpMonitorSession.Current;

            if (!session.IsRecording)
                return null;

            if (request.isModifiable)
            {
                var preSend = PreSendByRequest.GetValue(request, _ => new PreSend());

                lock (preSend)
                {
                    preSend.ManuallyTracked = true;
                    preSend.TrackedAtTicks = Stopwatch.GetTimestamp();
                }

                return null;
            }

            var taken = TakePreSend(request);
            var startedAt = taken != null && taken.ManuallyTracked ? taken.TrackedAtTicks : Stopwatch.GetTimestamp();
            var pending = Begin(session, request, taken, HttpCaptureSource.Manual, startedAt);

            if (IsDone(request))
                Complete(pending, "track");

            return pending.Record;
        }

        /// <summary>Track plus a completion hook, for <c>yield return Track(request.SendWebRequest())</c>.</summary>
        public static HttpRecord TrackOperation(UnityWebRequestAsyncOperation operation)
        {
            var request = operation.webRequest;
            var record = Track(request);

            operation.completed += _ =>
            {
                var pending = FindPending(request);

                if (pending != null)
                    Complete(pending, "completed");
            };

            return record;
        }

        // ---------------------------------------------------------------- internals

        private static Pending Begin(HttpMonitorSession session, UnityWebRequest request, PreSend preSend, HttpCaptureSource source, long startedAtTicks)
        {
            var headers = CollectRequestHeaders(request, preSend);
            var body = ReadRequestBody(request, session.Options);
            var record = session.Begin(HttpClientKind.UnityWebRequest, source, request.method, request.url, headers, body);

            RecordByRequest.Remove(request);
            RecordByRequest.Add(request, record);

            var pending = new Pending
            {
                Request = request,
                Session = session,
                Record = record,
                StartedAtTicks = startedAtTicks,
            };

            lock (InFlight)
                InFlight.Add(pending);

            return pending;
        }

        private static PreSend TakePreSend(UnityWebRequest request)
        {
            if (!PreSendByRequest.TryGetValue(request, out var preSend))
                return null;

            PreSendByRequest.Remove(request);

            return preSend;
        }

        /// <summary>
        /// Headers user code set, plus the Content-Type the upload handler carries when user code did
        /// not set one itself (Unity applies it natively at send time, so it is not visible through
        /// GetRequestHeader before that). Anything Unity adds on its own beyond that (User-Agent,
        /// Accept-Encoding, Content-Length, ...) is invisible to us and is not recorded.
        /// </summary>
        private static IReadOnlyList<HttpHeader> CollectRequestHeaders(UnityWebRequest request, PreSend preSend)
        {
            List<HttpHeader> headers = null;

            if (preSend != null)
            {
                lock (preSend)
                {
                    if (preSend.Headers.Count > 0)
                        headers = new List<HttpHeader>(preSend.Headers);
                }
            }

            var uploadHandler = request.uploadHandler;

            if (uploadHandler != null && FindHeader(headers, "Content-Type") < 0)
            {
                var contentType = uploadHandler.contentType;

                if (!string.IsNullOrEmpty(contentType))
                    (headers ?? (headers = new List<HttpHeader>(1))).Add(new HttpHeader("Content-Type", contentType));
            }

            return headers;
        }

        /// <summary>Only a raw upload handler exposes its bytes; file and custom handlers are recorded by size.</summary>
        private static byte[] ReadRequestBody(UnityWebRequest request, HttpMonitorOptions options)
        {
            if (!options.CaptureBodies)
                return null;

            return request.uploadHandler is UploadHandlerRaw raw ? raw.data : null;
        }

        /// <summary>
        /// Only a buffer download handler exposes its bytes. File, texture, audio, asset-bundle and
        /// script handlers either throw on <c>data</c> or hold something that is not the wire body.
        /// </summary>
        private static byte[] ReadResponseBody(UnityWebRequest request, HttpMonitorOptions options)
        {
            if (!options.CaptureBodies)
                return null;

            return request.downloadHandler is DownloadHandlerBuffer buffer ? buffer.data : null;
        }

        /// <summary>UnityWebRequest replaces on repeated SetRequestHeader; mirror that, case-insensitively.</summary>
        private static void SetOrReplace(List<HttpHeader> headers, string name, string value)
        {
            var index = FindHeader(headers, name);
            var header = new HttpHeader(name, value);

            if (index < 0)
                headers.Add(header);
            else
                headers[index] = header;
        }

        private static int FindHeader(List<HttpHeader> headers, string name)
        {
            if (headers == null)
                return -1;

            for (var i = 0; i < headers.Count; i++)
            {
                if (string.Equals(headers[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private static Pending FindPending(UnityWebRequest request)
        {
            lock (InFlight)
            {
                for (var i = 0; i < InFlight.Count; i++)
                {
                    if (ReferenceEquals(InFlight[i].Request, request))
                        return InFlight[i];
                }
            }

            return null;
        }

        /// <summary>isDone without throwing on a disposed request.</summary>
        private static bool IsDone(UnityWebRequest request)
        {
            try
            {
                return request.isDone;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Complete(Pending pending, string source)
        {
            if (!TryClaim(pending))
                return;

            var request = pending.Request;

            try
            {
                var outcome = new HttpRecordOutcome
                {
                    State = StateFor(request.result),
                    DurationMs = ElapsedMs(pending),
                    StatusCode = request.responseCode,
                    ResponseHeaders = ToHeaders(request.GetResponseHeaders()),
                    ResponseBody = ReadResponseBody(request, pending.Session.Options),
                    UploadedBytes = (long)request.uploadedBytes,
                    DownloadedBytes = (long)request.downloadedBytes,
                };

                if (outcome.State == HttpRecordState.Failed)
                    outcome.Error = request.error;

                pending.Session.Finish(pending.Record, outcome);
            }
            catch (Exception e)
            {
                // Typical cause: the request was disposed through a path the weaver does not cover.
                FinishSafely(pending, HttpRecordState.Incomplete, "response unreadable: " + e.Message);
                HttpMonitorLog.Warning($"response capture via {source} failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Abort(Pending pending, string reason)
        {
            if (!TryClaim(pending))
                return;

            FinishSafely(pending, HttpRecordState.Aborted, reason);
        }

        private static void FinishSafely(Pending pending, HttpRecordState state, string error)
        {
            try
            {
                pending.Session.Finish(pending.Record, new HttpRecordOutcome
                {
                    State = state,
                    DurationMs = ElapsedMs(pending),
                    Error = error,
                });
            }
            catch (Exception e)
            {
                HttpMonitorLog.Warning($"record finish failed: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <returns>true for the first caller to claim this request; false for everyone after.</returns>
        private static bool TryClaim(Pending pending)
        {
            lock (InFlight)
            {
                if (pending.Claimed)
                    return false;

                pending.Claimed = true;
                InFlight.Remove(pending);

                return true;
            }
        }

        private static HttpRecordState StateFor(UnityWebRequest.Result result)
        {
            switch (result)
            {
                case UnityWebRequest.Result.Success:
                case UnityWebRequest.Result.ProtocolError: // 4xx/5xx is still a response
                    return HttpRecordState.Completed;
                default:
                    return HttpRecordState.Failed;
            }
        }

        private static IReadOnlyList<HttpHeader> ToHeaders(Dictionary<string, string> headers)
        {
            if (headers == null || headers.Count == 0)
                return null;

            var list = new List<HttpHeader>(headers.Count);

            foreach (var pair in headers)
                list.Add(new HttpHeader(pair.Key, pair.Value));

            return list;
        }

        private static double ElapsedMs(Pending pending)
        {
            return (Stopwatch.GetTimestamp() - pending.StartedAtTicks) * 1000.0 / Stopwatch.Frequency;
        }
    }
}
