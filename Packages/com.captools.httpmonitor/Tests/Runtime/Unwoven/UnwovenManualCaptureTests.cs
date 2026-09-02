using System;
using System.Collections;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace HttpMonitor.Tests.Unwoven
{
    /// <summary>
    /// Nothing in this assembly is woven ([assembly: DoNotWeave]), so every record here must come
    /// from the manual API alone. Also the end-to-end proof that the opt-out attribute works.
    /// </summary>
    public class UnwovenManualCaptureTests
    {
        private static HttpRecord[] FindAll(string url)
        {
            return HttpMonitorSession.Current.Snapshot().Where(r => r.Url == url).ToArray();
        }

        [SetUp]
        public void EnableRecording()
        {
            HttpMonitorSession.Current.IsRecording = true;
        }

        [UnityTest]
        public IEnumerator WithoutTrack_NothingIsRecorded()
        {
            var url = TestServer.Shared.Url("echo?from=unwoven-plain");

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("X-Unseen", "1");
                yield return request.SendWebRequest();
                Assert.AreEqual(UnityWebRequest.Result.Success, request.result, request.error);
            }

            Assert.IsEmpty(FindAll(url), "DoNotWeave must keep the weaver out of this assembly");
        }

        [UnityTest]
        public IEnumerator Track_AfterCompletion_RecordsEverythingInOneCall()
        {
            var url = TestServer.Shared.Url("echo?from=unwoven-track-after");
            const string payload = "{\"manual\":true}";
            HttpRecord record;

            using (var request = UnityWebRequest.Post(url, payload, "application/json"))
            {
                yield return request.SendWebRequest();
                record = HttpMonitorCapture.Track(request);
            }

            Assert.NotNull(record);
            Assert.AreEqual(1, FindAll(url).Length);
            Assert.AreEqual(HttpCaptureSource.Manual, record.Source);
            Assert.AreEqual(HttpClientKind.UnityWebRequest, record.Client);
            Assert.AreEqual(HttpRecordState.Completed, record.State);
            Assert.AreEqual(200, record.StatusCode);
            Assert.AreEqual("POST", record.Method);
            Assert.AreEqual(payload, Encoding.UTF8.GetString(record.RequestBody));
            Assert.AreEqual(payload, Encoding.UTF8.GetString(record.ResponseBody));
            Assert.AreEqual("application/json", record.RequestHeaders.Single(h => h.Name == "Content-Type").Value, "the upload handler's content type is still visible");
            Assert.GreaterOrEqual(record.DurationMs, 0, "start time unknown when tracked after the fact");
        }

        [UnityTest]
        public IEnumerator Track_BeforeSend_ThenAfter_MeasuresDuration()
        {
            var url = TestServer.Shared.Url("delay/100?from=unwoven-track-both");
            HttpRecord record;

            using (var request = UnityWebRequest.Get(url))
            {
                Assert.IsNull(HttpMonitorCapture.Track(request));
                yield return request.SendWebRequest();
                record = HttpMonitorCapture.Track(request);
            }

            Assert.NotNull(record);
            Assert.AreEqual(1, FindAll(url).Length);
            Assert.AreEqual(HttpRecordState.Completed, record.State);
            Assert.GreaterOrEqual(record.DurationMs, 100);
        }

        [UnityTest]
        public IEnumerator Track_Twice_AfterCompletion_DoesNotDuplicate()
        {
            var url = TestServer.Shared.Url("echo?from=unwoven-track-twice");

            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                var first = HttpMonitorCapture.Track(request);
                var second = HttpMonitorCapture.Track(request);
                Assert.AreSame(first, second);
            }

            Assert.AreEqual(1, FindAll(url).Length);
        }

        [UnityTest]
        public IEnumerator TrackOperation_FinishesViaCompletedEvent_WhenNotDisposedFirst()
        {
            var url = TestServer.Shared.Url("echo?from=unwoven-track-operation");
            var request = UnityWebRequest.Get(url);

            yield return HttpMonitorCapture.Track(request.SendWebRequest());
            yield return null; // completed fires after the coroutine resumed

            var records = FindAll(url);

            Assert.AreEqual(1, records.Length);
            Assert.AreEqual(HttpCaptureSource.Manual, records[0].Source);
            Assert.AreEqual(HttpRecordState.Completed, records[0].State);
            Assert.AreEqual(200, records[0].StatusCode);
            Assert.IsNotNull(records[0].ResponseBody);
            Assert.Greater(records[0].DurationMs, 0);

            request.Dispose();
        }

        [UnityTest]
        public IEnumerator Track_InFlight_ThenAfter_FinishesOnSecondCall()
        {
            var url = TestServer.Shared.Url("delay/100?from=unwoven-in-flight");
            var request = UnityWebRequest.Get(url);
            request.SendWebRequest();

            var pending = HttpMonitorCapture.Track(request);
            Assert.NotNull(pending);
            Assert.AreEqual(HttpRecordState.Pending, pending.State);

            while (!request.isDone)
                yield return null;

            var finished = HttpMonitorCapture.Track(request);
            request.Dispose();

            Assert.AreSame(pending, finished);
            Assert.AreEqual(HttpRecordState.Completed, finished.State);
            Assert.AreEqual(1, FindAll(url).Length);
        }
    }
}
