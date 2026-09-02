using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace HttpMonitor.Tests
{
    public class BodyAndRedactionTests
    {
        private static HttpRecord Find(string url)
        {
            return Array.Find(HttpMonitorSession.Current.Snapshot(), r => r.Url == url);
        }

        private static HttpRecord Begin(HttpMonitorSession session, IReadOnlyList<HttpHeader> headers = null, byte[] body = null)
        {
            return session.Begin(HttpClientKind.Custom, HttpCaptureSource.Manual, "POST", "https://example.com/", headers, body);
        }

        private static byte[] Bytes(int length)
        {
            var bytes = new byte[length];

            for (var i = 0; i < length; i++)
                bytes[i] = (byte)(i % 251);

            return bytes;
        }

        // ------------------------------------------------------------ in-memory: redaction

        [Test]
        public void DefaultRedactedHeaders_AreReplaced_OnBothSides_CaseInsensitively()
        {
            var session = new HttpMonitorSession(4);
            var record = Begin(session, new List<HttpHeader>
            {
                new HttpHeader("authorization", "Bearer x"),
                new HttpHeader("X-Plain", "keep"),
                new HttpHeader("Cookie", "a=b"),
            });

            session.Finish(record, new HttpRecordOutcome
            {
                State = HttpRecordState.Completed,
                ResponseHeaders = new List<HttpHeader>
                {
                    new HttpHeader("Set-Cookie", "session=1"),
                    new HttpHeader("Set-Cookie", "other=2"),
                    new HttpHeader("Content-Type", "text/plain"),
                },
            });

            Assert.AreEqual(new[] { "authorization", "X-Plain", "Cookie" }, record.RequestHeaders.Select(h => h.Name).ToArray());
            Assert.AreEqual("<redacted>", record.RequestHeaders[0].Value);
            Assert.AreEqual("keep", record.RequestHeaders[1].Value);
            Assert.AreEqual("<redacted>", record.RequestHeaders[2].Value);

            Assert.AreEqual(3, record.ResponseHeaders.Count);
            Assert.AreEqual("<redacted>", record.ResponseHeaders[0].Value);
            Assert.AreEqual("<redacted>", record.ResponseHeaders[1].Value);
            Assert.AreEqual("text/plain", record.ResponseHeaders[2].Value);
        }

        [Test]
        public void RedactionList_IsConfigurable()
        {
            var session = new HttpMonitorSession(4);
            session.Options.RedactedHeaders.Add("X-Api-Key");
            session.Options.RedactedHeaders.Remove("Cookie");
            session.Options.RedactedValue = "***";

            var record = Begin(session, new List<HttpHeader>
            {
                new HttpHeader("x-api-key", "k"),
                new HttpHeader("Cookie", "visible now"),
            });

            Assert.AreEqual("***", record.RequestHeaders[0].Value);
            Assert.AreEqual("visible now", record.RequestHeaders[1].Value);
        }

        // ------------------------------------------------------------ in-memory: body limits

        [Test]
        public void Bodies_AreCopied_NotShared()
        {
            var session = new HttpMonitorSession(4);
            var original = Encoding.UTF8.GetBytes("hello");
            var record = Begin(session, body: original);

            original[0] = (byte)'J';

            Assert.AreEqual("hello", Encoding.UTF8.GetString(record.RequestBody));
            Assert.IsFalse(record.RequestBodyTruncated);
        }

        [Test]
        public void BodyOverCap_KeepsPrefix_AndIsFlaggedTruncated()
        {
            var session = new HttpMonitorSession(4);
            session.Options.MaxBodyBytes = 10;

            var record = Begin(session, body: Bytes(25));

            session.Finish(record, new HttpRecordOutcome { State = HttpRecordState.Completed, ResponseBody = Bytes(10), DownloadedBytes = 10 });

            Assert.AreEqual(10, record.RequestBody.Length);
            Assert.IsTrue(record.RequestBodyTruncated);
            Assert.AreEqual(Bytes(10), record.RequestBody);

            Assert.AreEqual(10, record.ResponseBody.Length);
            Assert.IsFalse(record.ResponseBodyTruncated, "exactly at the cap is not truncated");
        }

        [Test]
        public void CaptureBodiesOff_StoresNothing()
        {
            var session = new HttpMonitorSession(4);
            session.Options.CaptureBodies = false;

            var record = Begin(session, body: Bytes(5));
            session.Finish(record, new HttpRecordOutcome { State = HttpRecordState.Completed, ResponseBody = Bytes(5) });

            Assert.IsNull(record.RequestBody);
            Assert.IsNull(record.ResponseBody);
            Assert.AreEqual(0, session.StoredBodyBytes);
        }

        [Test]
        public void TotalBodyBudget_EvictsOldest_ButNeverTheRecordJustWritten()
        {
            var session = new HttpMonitorSession(10);
            session.Options.MaxTotalBodyBytes = 25;

            var a = Begin(session, body: Bytes(10));
            var b = Begin(session, body: Bytes(10));
            Assert.AreEqual(20, session.StoredBodyBytes);

            var c = Begin(session, body: Bytes(10)); // 30 > 25: a goes
            Assert.AreEqual(new[] { b, c }, session.Snapshot());
            Assert.AreEqual(20, session.StoredBodyBytes);

            session.Finish(b, new HttpRecordOutcome { State = HttpRecordState.Completed, ResponseBody = Bytes(10) }); // 30 > 25, but b is the oldest and just written: stays
            Assert.AreEqual(new[] { b, c }, session.Snapshot());
            Assert.AreEqual(30, session.StoredBodyBytes);

            session.Finish(c, new HttpRecordOutcome { State = HttpRecordState.Completed, ResponseBody = Bytes(10) }); // 40 > 25: b goes
            Assert.AreEqual(new[] { c }, session.Snapshot());
            Assert.AreEqual(20, session.StoredBodyBytes);
        }

        [Test]
        public void FinishingAnEvictedRecord_DoesNotCorruptTheBudget()
        {
            var session = new HttpMonitorSession(2);
            var evicted = Begin(session, body: Bytes(4));
            Begin(session, body: Bytes(4));
            Begin(session, body: Bytes(4)); // capacity 2: evicted is out

            Assert.AreEqual(8, session.StoredBodyBytes);

            session.Finish(evicted, new HttpRecordOutcome { State = HttpRecordState.Completed, ResponseBody = Bytes(100) });

            Assert.AreEqual(HttpRecordState.Completed, evicted.State, "the record itself still finishes for anyone holding it");
            Assert.AreEqual(8, session.StoredBodyBytes);
        }

        [Test]
        public void Clear_ResetsTheBudget()
        {
            var session = new HttpMonitorSession(4);
            Begin(session, body: Bytes(4));
            session.Clear();

            Assert.AreEqual(0, session.StoredBodyBytes);
        }

        // ------------------------------------------------------------ live UnityWebRequest

        [UnityTest]
        public IEnumerator Post_RecordsRequestBody_AndEchoedResponseBody()
        {
            var url = TestServer.Shared.Url("echo?from=bodies");
            const string payload = "{\"hello\":\"world\"}";

            using (var request = UnityWebRequest.Post(url, payload, "application/json"))
                yield return request.SendWebRequest();

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual(payload, Encoding.UTF8.GetString(record.RequestBody));
            Assert.IsFalse(record.RequestBodyTruncated);
            Assert.AreEqual(payload, Encoding.UTF8.GetString(record.ResponseBody));
            Assert.AreEqual(record.ResponseBody.Length, record.DownloadedBytes);
            Assert.AreEqual(payload.Length, record.UploadedBytes);
        }

        [UnityTest]
        public IEnumerator LargeResponse_IsTruncated_ButFullSizeIsKept()
        {
            var url = TestServer.Shared.Url("bytes/5000?from=large");
            var options = HttpMonitorSession.Current.Options;
            var previous = options.MaxBodyBytes;
            options.MaxBodyBytes = 1000;

            try
            {
                using (var request = UnityWebRequest.Get(url))
                    yield return request.SendWebRequest();
            }
            finally
            {
                options.MaxBodyBytes = previous;
            }

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual(1000, record.ResponseBody.Length);
            Assert.IsTrue(record.ResponseBodyTruncated);
            Assert.AreEqual(5000, record.DownloadedBytes);
            Assert.AreEqual(Bytes(1000), record.ResponseBody);
        }

        [UnityTest]
        public IEnumerator FileDownloadHandler_RecordsSizeOnly()
        {
            var url = TestServer.Shared.Url("bytes/300?from=file-handler");
            var path = Path.Combine(Application.temporaryCachePath, "httpmonitor-test.bin");

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(path);

                yield return request.SendWebRequest();
            }

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual(HttpRecordState.Completed, record.State);
            Assert.IsNull(record.ResponseBody);
            Assert.AreEqual(300, record.DownloadedBytes);

            File.Delete(path);
        }
    }
}
