using System;
using System.Net.Http;
using UnityEngine.Networking;

namespace HttpMonitor
{
    /// <summary>
    /// Entry points targeted by the weaver. The signatures are load-bearing: the weaver rewrites
    /// call sites in user code to these exact names and parameter lists, so renaming anything here
    /// means updating <c>Unity.HttpMonitor.CodeGen</c> too.
    ///
    /// Rule: nothing in here may throw into user code. Capture failures degrade to a warning.
    /// Every real UnityWebRequest or HttpClient call happens outside any try, so the game's exception
    /// behaviour is exactly what it was before weaving.
    /// </summary>
    public static class Interceptor
    {
        // ---------------------------------------------------------------- UnityWebRequest

        /// <summary>
        /// Replaces <c>request.SetRequestHeader(name, value)</c> at woven call sites.
        /// Stack shape: request, name, value in; nothing out.
        /// </summary>
        public static void SetRequestHeader(UnityWebRequest request, string name, string value)
        {
            // Real call first: an invalid header, or a request already sent, throws exactly as
            // before, and nothing gets recorded for it.
            request.SetRequestHeader(name, value);

            try
            {
                UnityWebRequestCapture.RecordHeader(request, name, value);
            }
            catch (Exception e)
            {
                Warn("header capture", e);
            }
        }

        /// <summary>
        /// Replaces <c>request.SendWebRequest()</c> at woven call sites.
        /// Stack shape is identical to the instance call: one UnityWebRequest in, one async operation out.
        /// </summary>
        public static UnityWebRequestAsyncOperation SendWebRequest(UnityWebRequest request)
        {
            UnityWebRequestCapture.Pending pending = null;

            try
            {
                pending = UnityWebRequestCapture.BeginWoven(request);
            }
            catch (Exception e)
            {
                Warn("request capture", e);
            }

            var operation = request.SendWebRequest();

            if (pending != null)
            {
                try
                {
                    UnityWebRequestCapture.HookCompletion(pending, operation);
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
                UnityWebRequestCapture.OnDisposing(request);
            }
            catch (Exception e)
            {
                Warn("capture on dispose", e);
            }

            // A null request throws NullReferenceException exactly as before weaving.
            request.Dispose();
        }

        // ---------------------------------------------------------------- HttpClient

        /// <summary>Replaces <c>new HttpClient()</c> at woven call sites.</summary>
        public static HttpClient CreateHttpClient()
        {
            return CreateHttpClient(new HttpClientHandler(), true);
        }

        /// <summary>Replaces <c>new HttpClient(handler)</c> at woven call sites.</summary>
        public static HttpClient CreateHttpClient(HttpMessageHandler handler)
        {
            return CreateHttpClient(handler, true);
        }

        /// <summary>
        /// Replaces <c>new HttpClient(handler, disposeHandler)</c> at woven call sites. The monitor
        /// handler wraps the user's handler; disposal semantics are unchanged because disposing the
        /// wrapper disposes the inner handler exactly when HttpClient would have disposed it directly.
        /// A chain that already contains a monitor handler (from the manual API) is left as is and
        /// only gains the Woven bit, so nothing is ever recorded twice.
        /// </summary>
        public static HttpClient CreateHttpClient(HttpMessageHandler handler, bool disposeHandler)
        {
            MonitorHandler monitor = null;

            try
            {
                var existing = MonitorHandler.FindInChain(handler);

                if (existing != null)
                    existing.AddSource(HttpCaptureSource.Woven);
                else if (handler != null)
                    monitor = new MonitorHandler(handler, HttpCaptureSource.Woven);
            }
            catch (Exception e)
            {
                Warn("HttpClient instrumentation", e);
            }

            // A null handler throws ArgumentNullException exactly as before weaving.
            return monitor != null ? new HttpClient(monitor, disposeHandler) : new HttpClient(handler, disposeHandler);
        }

        private static void Warn(string stage, Exception e)
        {
            HttpMonitorLog.Warning($"{stage} failed: {e.GetType().Name}: {e.Message}");
        }
    }
}
