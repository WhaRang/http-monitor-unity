using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace HttpMonitor.Tests
{
    /// <summary>
    /// End-to-end: this assembly is woven like any user assembly, so plain UnityWebRequest calls
    /// below must show up in <see cref="HttpMonitorSession.Current"/>.
    /// </summary>
    public class UnityWebRequestCaptureTests
    {
        private static HttpRecord Find(string url)
        {
            return Array.Find(HttpMonitorSession.Current.Snapshot(), r => r.Url == url);
        }

        private static IEnumerator WaitUntilFinished(string url, float timeoutSeconds = 10f)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;

            while (Time.realtimeSinceStartup < deadline)
            {
                var record = Find(url);

                if (record != null && record.IsFinished)
                    yield break;

                yield return null;
            }
        }

        private static void AssertCompleted200(HttpRecord record, string url)
        {
            Assert.NotNull(record, "no record for " + url);
            Assert.AreEqual(HttpClientKind.UnityWebRequest, record.Client);
            Assert.AreEqual(HttpCaptureSource.Woven, record.Source);
            Assert.AreEqual("GET", record.Method);
            Assert.AreEqual(HttpRecordState.Completed, record.State);
            Assert.AreEqual(200, record.StatusCode);
            Assert.IsNull(record.Error);
            Assert.Greater(record.DurationMs, 0);
            Assert.Greater(record.DownloadedBytes, 0);
            Assert.That(record.ResponseHeaders, Has.Some.Matches<HttpHeader>(h => h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)));
            Assert.That(record.StartedAtUtc, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(1)));
        }

        [SetUp]
        public void EnableRecording()
        {
            HttpMonitorSession.Current.IsRecording = true;
        }

        [UnityTest]
        public IEnumerator Coroutine_WithUsing_RecordsCompletedResponse()
        {
            var url = TestServer.Shared.Url("echo?from=using");

            using (var request = UnityWebRequest.Get(url))
                yield return request.SendWebRequest();

            AssertCompleted200(Find(url), url);
        }

        [UnityTest]
        public IEnumerator Coroutine_WithExplicitDispose_RecordsCompletedResponse()
        {
            var url = TestServer.Shared.Url("echo?from=explicit-dispose");
            var request = UnityWebRequest.Get(url);
            request.SendWebRequest();

            while (!request.isDone)
                yield return null;

            request.Dispose();

            AssertCompleted200(Find(url), url);
        }

        [UnityTest]
        public IEnumerator Coroutine_NeverDisposed_RecordsViaCompletedEvent()
        {
            var url = TestServer.Shared.Url("echo?from=leaked");
            var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();
            yield return WaitUntilFinished(url);

            AssertCompleted200(Find(url), url);
        }

        [UnityTest]
        public IEnumerator Async_WithUsing_RecordsCompletedResponse()
        {
            var url = TestServer.Shared.Url("echo?from=async");
            var task = SendAsync(url);

            while (!task.IsCompleted)
                yield return null;

            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());
            AssertCompleted200(Find(url), url);
        }

        [UnityTest]
        public IEnumerator DisposedBeforeCompletion_RecordsAborted()
        {
            var url = TestServer.Shared.Url("delay/2000?from=aborted");
            var request = UnityWebRequest.Get(url);
            request.SendWebRequest();
            request.Dispose();

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual(HttpRecordState.Aborted, record.State);
            Assert.AreEqual(0, record.StatusCode);
            Assert.That(record.Error, Does.Contain("disposed"));

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConnectionRefused_RecordsFailedWithError()
        {
            var url = TestServer.UnreachableUrl + "?from=failed";

            using (var request = UnityWebRequest.Get(url))
                yield return request.SendWebRequest();

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual(HttpRecordState.Failed, record.State);
            Assert.AreEqual(0, record.StatusCode);
            Assert.IsNotEmpty(record.Error);
        }

        [UnityTest]
        public IEnumerator HttpErrorStatus_IsStillCompleted_WithNoTransportError()
        {
            var url = TestServer.Shared.Url("status/404?from=protocol-error");

            using (var request = UnityWebRequest.Get(url))
                yield return request.SendWebRequest();

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual(HttpRecordState.Completed, record.State);
            Assert.AreEqual(404, record.StatusCode);
            Assert.IsNull(record.Error);
        }

        [UnityTest]
        public IEnumerator Events_FirePendingThenFinal_ForARealRequest()
        {
            var url = TestServer.Shared.Url("echo?from=events");
            var seen = new List<string>();
            Action<HttpRecord> added = r => { if (r.Url == url) seen.Add("added:" + r.State); };
            Action<HttpRecord> updated = r => { if (r.Url == url) seen.Add("updated:" + r.State); };
            HttpMonitorSession.Current.RecordAdded += added;
            HttpMonitorSession.Current.RecordUpdated += updated;

            try
            {
                using (var request = UnityWebRequest.Get(url))
                    yield return request.SendWebRequest();
            }
            finally
            {
                HttpMonitorSession.Current.RecordAdded -= added;
                HttpMonitorSession.Current.RecordUpdated -= updated;
            }

            Assert.AreEqual(new[] { "added:Pending", "updated:Completed" }, seen.ToArray());
        }

        [UnityTest]
        public IEnumerator WhenNotRecording_RequestPassesThrough_AndNothingIsRecorded()
        {
            var url = TestServer.Shared.Url("echo?from=paused");
            HttpMonitorSession.Current.IsRecording = false;

            try
            {
                using (var request = UnityWebRequest.Get(url))
                {
                    yield return request.SendWebRequest();
                    Assert.AreEqual(UnityWebRequest.Result.Success, request.result, request.error);
                }
            }
            finally
            {
                HttpMonitorSession.Current.IsRecording = true;
            }

            Assert.IsNull(Find(url));
        }

        private static async Task SendAsync(string url)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                var operation = request.SendWebRequest();
                var completion = new TaskCompletionSource<bool>();
                operation.completed += _ => completion.TrySetResult(true);
                await completion.Task;
            }
        }
    }
}
