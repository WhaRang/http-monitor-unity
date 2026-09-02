using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.Networking;

namespace HttpMonitor
{
    /// <summary>
    /// Entry points targeted by the weaver. The signatures are load-bearing: the weaver rewrites
    /// call sites in user code to these exact names and parameter lists, so renaming anything here
    /// means updating <c>Unity.HttpMonitor.CodeGen</c> too.
    ///
    /// Rule: nothing in here may throw into user code. Capture failures degrade to a warning.
    ///
    /// Response timing (M0 finding): when a coroutine does <c>yield return request.SendWebRequest()</c>,
    /// native completion resumes the coroutine synchronously and the managed <c>completed</c> event
    /// fires only afterwards. A <c>using</c> block has therefore already disposed the request by the
    /// time <c>completed</c> runs. So the response is captured by whichever comes first:
    /// the woven <see cref="Dispose"/> call, or the <c>completed</c> event.
    /// </summary>
    public static class Interceptor
    {
        private sealed class Pending
        {
            public UnityWebRequest Request;
            public HttpMonitorSession Session;
            public HttpRecord Record;
            public long StartedAtTicks;
            public bool Claimed;
        }

        private static readonly List<Pending> InFlight = new List<Pending>();

        /// <summary>
        /// Replaces <c>request.SendWebRequest()</c> at woven call sites.
        /// Stack shape is identical to the instance call: one UnityWebRequest in, one async operation out.
        /// </summary>
        public static UnityWebRequestAsyncOperation SendWebRequest(UnityWebRequest request)
        {
            var pending = TryBegin(request);

            // Deliberately outside any try: the user's exception behaviour must be untouched.
            var operation = request.SendWebRequest();

            if (pending != null)
            {
                try
                {
                    operation.completed += _ => Complete(pending, "completed");
                }
                catch (Exception e)
                {
                    Warn("completion hook", e);
                }
            }

            return operation;
        }

        /// <summary>
        /// Replaces <c>request.Dispose()</c> and the <c>IDisposable.Dispose()</c> call a <c>using</c>
        /// block emits. Captures the response first if the request finished, then disposes for real.
        /// </summary>
        public static void Dispose(UnityWebRequest request)
        {
            try
            {
                var pending = FindPending(request);

                if (pending != null)
                {
                    if (request.isDone)
                        Complete(pending, "dispose");
                    else
                        Abort(pending, "disposed before completion");
                }
            }
            catch (Exception e)
            {
                Warn("capture on dispose", e);
            }

            // Outside any try: a null request throws NullReferenceException exactly as before weaving.
            request.Dispose();
        }

        private static Pending TryBegin(UnityWebRequest request)
        {
            try
            {
                var session = HttpMonitorSession.Current;

                if (!session.IsRecording)
                    return null;

                var record = session.Begin(HttpClientKind.UnityWebRequest, request.method, request.url, null);

                var pending = new Pending
                {
                    Request = request,
                    Session = session,
                    Record = record,
                    StartedAtTicks = Stopwatch.GetTimestamp(),
                };

                lock (InFlight)
                    InFlight.Add(pending);

                return pending;
            }
            catch (Exception e)
            {
                Warn("request capture", e);

                return null;
            }
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
                Warn($"response capture via {source}", e);
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
                Warn("record finish", e);
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

        private static void Warn(string stage, Exception e)
        {
            HttpMonitorLog.Warning($"{stage} failed: {e.GetType().Name}: {e.Message}");
        }
    }
}
