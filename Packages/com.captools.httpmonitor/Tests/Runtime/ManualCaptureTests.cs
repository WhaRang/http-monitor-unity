using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace HttpMonitor.Tests
{
    /// <summary>
    /// Manual API used from a woven assembly: the interesting property is that nothing is ever
    /// recorded twice, and records gain the Manual bit on top of Woven.
    /// The unwoven paths are covered in HttpMonitor.Tests.Unwoven.
    /// </summary>
    public class ManualCaptureTests
    {
        private static HttpRecord[] FindAll(string url)
        {
            return HttpMonitorSession.Current.Snapshot().Where(r => r.Url == url).ToArray();
        }

        private static IEnumerator Await(Task task, float timeoutSeconds = 20f)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (!task.IsCompleted && DateTime.UtcNow < deadline)
                yield return null;

            Assert.IsTrue(task.IsCompleted, "timed out");
        }

        [SetUp]
        public void EnableRecording()
        {
            HttpMonitorSession.Current.IsRecording = true;
        }

        [UnityTest]
        public IEnumerator Track_AfterYield_ReturnsTheWovenRecord_WithBothBits()
        {
            var url = TestServer.Shared.Url("echo?from=track-after");
            HttpRecord tracked;

            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                tracked = HttpMonitorCapture.Track(request);
            }

            var records = FindAll(url);

            Assert.AreEqual(1, records.Length, "no duplicate");
            Assert.AreSame(records[0], tracked);
            Assert.AreEqual(HttpCaptureSource.Woven | HttpCaptureSource.Manual, tracked.Source);
            Assert.AreEqual(HttpRecordState.Completed, tracked.State);
            Assert.AreEqual(200, tracked.StatusCode);
        }

        [UnityTest]
        public IEnumerator Track_BeforeSend_MarksTheWovenRecordManual()
        {
            var url = TestServer.Shared.Url("echo?from=track-before");

            using (var request = UnityWebRequest.Get(url))
            {
                Assert.IsNull(HttpMonitorCapture.Track(request), "nothing recorded before send");
                yield return request.SendWebRequest();
            }

            var records = FindAll(url);

            Assert.AreEqual(1, records.Length);
            Assert.AreEqual(HttpCaptureSource.Woven | HttpCaptureSource.Manual, records[0].Source);
            Assert.AreEqual(HttpRecordState.Completed, records[0].State);
        }

        [UnityTest]
        public IEnumerator TrackOperation_OnAWovenSend_AddsManualBit_NoDuplicate()
        {
            var url = TestServer.Shared.Url("echo?from=track-operation");

            using (var request = UnityWebRequest.Get(url))
                yield return HttpMonitorCapture.Track(request.SendWebRequest());

            var records = FindAll(url);

            Assert.AreEqual(1, records.Length);
            Assert.AreEqual(HttpCaptureSource.Woven | HttpCaptureSource.Manual, records[0].Source);
            Assert.AreEqual(HttpRecordState.Completed, records[0].State);
            Assert.AreEqual(200, records[0].StatusCode);
        }

        [Test]
        public void Track_Null_ReturnsNull()
        {
            Assert.IsNull(HttpMonitorCapture.Track((UnityWebRequest)null));
            Assert.IsNull(HttpMonitorCapture.Track((UnityWebRequestAsyncOperation)null));
        }

        [Test]
        public void Begin_Complete_RecordsACustomClient()
        {
            var body = Encoding.UTF8.GetBytes("{\"q\":1}");
            var handle = HttpMonitorCapture.Begin("PUT", "custom://service/items/1",
                new List<HttpHeader> { new HttpHeader("Authorization", "token"), new HttpHeader("X-Trace", "abc") }, body);

            Assert.NotNull(handle);
            Assert.IsFalse(handle.IsFinished);
            Assert.AreEqual(HttpRecordState.Pending, handle.Record.State);
            Assert.AreEqual(HttpClientKind.Custom, handle.Record.Client);
            Assert.AreEqual(HttpCaptureSource.Manual, handle.Record.Source);
            Assert.AreEqual("<redacted>", handle.Record.RequestHeaders[0].Value);
            Assert.AreEqual("abc", handle.Record.RequestHeaders[1].Value);

            handle.Complete(201, new List<HttpHeader> { new HttpHeader("Location", "/items/1") }, Encoding.UTF8.GetBytes("ok"));

            Assert.IsTrue(handle.IsFinished);
            Assert.AreEqual(HttpRecordState.Completed, handle.Record.State);
            Assert.AreEqual(201, handle.Record.StatusCode);
            Assert.AreEqual("Location", handle.Record.ResponseHeaders[0].Name);
            Assert.AreEqual("ok", Encoding.UTF8.GetString(handle.Record.ResponseBody));
            Assert.AreEqual(2, handle.Record.DownloadedBytes);
            Assert.AreEqual(body.Length, handle.Record.UploadedBytes);
            Assert.GreaterOrEqual(handle.Record.DurationMs, 0);

            handle.Fail("too late");
            Assert.AreEqual(HttpRecordState.Completed, handle.Record.State, "only the first finish counts");
        }

        [Test]
        public void Begin_Fail_And_Abort()
        {
            var failed = HttpMonitorCapture.Begin("GET", "custom://a");
            failed.Fail("dns");
            Assert.AreEqual(HttpRecordState.Failed, failed.Record.State);
            Assert.AreEqual("dns", failed.Record.Error);

            var aborted = HttpMonitorCapture.Begin("GET", "custom://b");
            aborted.Abort();
            Assert.AreEqual(HttpRecordState.Aborted, aborted.Record.State);
            Assert.AreEqual("aborted", aborted.Record.Error);
        }

        [Test]
        public void Begin_WhenPaused_ReturnsNull()
        {
            HttpMonitorSession.Current.IsRecording = false;

            try
            {
                Assert.IsNull(HttpMonitorCapture.Begin("GET", "custom://paused"));
            }
            finally
            {
                HttpMonitorSession.Current.IsRecording = true;
            }
        }

        [UnityTest]
        public IEnumerator CreateHandler_InsideWovenConstructor_IsNotDoubleWrapped()
        {
            var url = TestServer.Shared.Url("echo?from=manual-handler-woven");

            var task = Task.Run(async () =>
            {
                // The `new HttpClient(handler)` here is woven; the handler is already a monitor.
                using (var client = new HttpClient(HttpMonitorCapture.CreateHandler()))
                    await client.GetStringAsync(url);
            });

            yield return Await(task);
            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());

            var records = FindAll(url);

            Assert.AreEqual(1, records.Length, "exactly one record despite two capture layers");
            Assert.AreEqual(HttpCaptureSource.Woven | HttpCaptureSource.Manual, records[0].Source);
            Assert.AreEqual(200, records[0].StatusCode);
        }

        [UnityTest]
        public IEnumerator CreateClient_RecordsManualOnly()
        {
            var url = TestServer.Shared.Url("echo?from=manual-client");

            var task = Task.Run(async () =>
            {
                using (var client = HttpMonitorCapture.CreateClient())
                    await client.GetStringAsync(url);
            });

            yield return Await(task);
            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());

            var records = FindAll(url);

            Assert.AreEqual(1, records.Length);
            Assert.AreEqual(HttpCaptureSource.Manual, records[0].Source);
            Assert.AreEqual(HttpClientKind.HttpClient, records[0].Client);
        }
    }
}
