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
    /// M0 finding on response timing: when a coroutine does <c>yield return request.SendWebRequest()</c>,
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
            public long StartedAtTicks;
            public bool Done;
        }

        private static readonly List<Pending> InFlight = new List<Pending>();

        /// <summary>
        /// Replaces <c>request.SendWebRequest()</c> at woven call sites.
        /// Stack shape is identical to the instance call: one UnityWebRequest in, one async operation out.
        /// </summary>
        public static UnityWebRequestAsyncOperation SendWebRequest(UnityWebRequest request)
        {
            Pending pending = null;

            try
            {
                pending = new Pending { Request = request, StartedAtTicks = Stopwatch.GetTimestamp() };

                lock (InFlight)
                    InFlight.Add(pending);

                HttpMonitorLog.Info($"-> {request.method} {request.url}");
            }
            catch (Exception e)
            {
                Warn("request capture", e);
            }

            // Deliberately outside any try: the user's exception behaviour must be untouched.
            var operation = request.SendWebRequest();

            try
            {
                if (pending != null)
                    operation.completed += _ => Complete(pending, "completed");
            }
            catch (Exception e)
            {
                Warn("completion hook", e);
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
                        Abandon(pending, "disposed before completion");
                }
            }
            catch (Exception e)
            {
                Warn("capture on dispose", e);
            }

            // Outside any try: a null request throws NullReferenceException exactly as before weaving.
            request.Dispose();
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
            try
            {
                if (!TryFinish(pending))
                    return;

                var request = pending.Request;
                var elapsedMs = (Stopwatch.GetTimestamp() - pending.StartedAtTicks) * 1000.0 / Stopwatch.Frequency;

                HttpMonitorLog.Info($"<- {request.responseCode} {request.result} {request.url} " +
                                    $"({request.downloadedBytes} B, {elapsedMs:F0} ms) via {source}");
            }
            catch (Exception e)
            {
                // Typical cause: the request was disposed through a path the weaver does not cover.
                Warn($"response capture via {source}", e);
            }
        }

        private static void Abandon(Pending pending, string reason)
        {
            if (!TryFinish(pending))
                return;

            HttpMonitorLog.Info($"<- (no response) {pending.Request.url}: {reason}");
        }

        /// <returns>true for the first caller to finish this record; false for everyone after.</returns>
        private static bool TryFinish(Pending pending)
        {
            lock (InFlight)
            {
                if (pending.Done)
                    return false;

                pending.Done = true;
                InFlight.Remove(pending);

                return true;
            }
        }

        private static void Warn(string stage, Exception e)
        {
            HttpMonitorLog.Warning($"{stage} failed: {e.GetType().Name}: {e.Message}");
        }
    }
}
