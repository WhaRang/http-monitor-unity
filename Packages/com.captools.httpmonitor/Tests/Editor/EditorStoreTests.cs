using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HttpMonitor.Editor;
using NUnit.Framework;

namespace HttpMonitor.Tests.Editor
{
    /// <summary>
    /// Buffer and bridge on private instances, so the real store (and the user's captured traffic)
    /// is never touched. Domain-reload survival is a manual check: capture, recompile, records stay.
    /// </summary>
    public class EditorStoreTests
    {
        private static HttpRecord Begin(HttpMonitorSession session, string url, byte[] body = null)
        {
            return session.Begin(HttpClientKind.UnityWebRequest, HttpCaptureSource.Woven, "GET", url, null, body);
        }

        private static void Finish(HttpMonitorSession session, HttpRecord record, long status, byte[] body = null)
        {
            session.Finish(record, new HttpRecordOutcome { State = HttpRecordState.Completed, StatusCode = status, ResponseBody = body, DurationMs = 5 });
        }

        [Test]
        public void Add_CopiesTheRequestPart_AndUpdate_CopiesTheOutcome()
        {
            var session = new HttpMonitorSession(10);
            var buffer = new EditorRecordBuffer();

            var runtime = session.Begin(HttpClientKind.HttpClient, HttpCaptureSource.Manual, "POST", "http://x/",
                new List<HttpHeader> { new HttpHeader("X-A", "1") }, new byte[] { 1, 2, 3 });
            var copy = buffer.Add(runtime);

            Assert.AreEqual(1, copy.Id);
            Assert.AreEqual(runtime.Id, copy.RuntimeId);
            Assert.AreEqual("POST", copy.Method);
            Assert.AreEqual(HttpClientKind.HttpClient, copy.Client);
            Assert.AreEqual(HttpCaptureSource.Manual, copy.Source);
            Assert.AreEqual("X-A: 1", copy.RequestHeaders[0].ToString());
            Assert.AreEqual(new byte[] { 1, 2, 3 }, copy.RequestBody);
            Assert.AreEqual(HttpRecordState.Pending, copy.State);
            Assert.AreSame(runtime, copy.Runtime);
            var revision = copy.Revision;

            session.Finish(runtime, new HttpRecordOutcome
            {
                State = HttpRecordState.Completed,
                StatusCode = 201,
                ResponseHeaders = new List<HttpHeader> { new HttpHeader("Content-Type", "text/plain") },
                ResponseBody = new byte[] { 9 },
                DurationMs = 42,
                DownloadedBytes = 1,
            });
            runtime.AddSource(HttpCaptureSource.Woven);

            Assert.AreSame(copy, buffer.Update(runtime));
            Assert.AreEqual(HttpRecordState.Completed, copy.State);
            Assert.AreEqual(201, copy.StatusCode);
            Assert.AreEqual("Content-Type: text/plain", copy.ResponseHeaders[0].ToString());
            Assert.AreEqual(new byte[] { 9 }, copy.ResponseBody);
            Assert.AreEqual(42, copy.DurationMs);
            Assert.AreEqual(HttpCaptureSource.Manual | HttpCaptureSource.Woven, copy.Source, "source bits gained later are picked up");
            Assert.Greater(copy.Revision, revision);
            Assert.AreEqual(4, buffer.StoredBodyBytes);
        }

        [Test]
        public void Update_ForAnUnknownRecord_ReturnsNull()
        {
            var session = new HttpMonitorSession(10);
            var buffer = new EditorRecordBuffer();

            Assert.IsNull(buffer.Update(Begin(session, "http://never-added/")));
        }

        [Test]
        public void Capacity_EvictsOldest_AndDropsTheirRuntimeLink()
        {
            var session = new HttpMonitorSession(10);
            var buffer = new EditorRecordBuffer { Capacity = 2 };

            var first = Begin(session, "http://1/");
            buffer.Add(first);
            buffer.Add(Begin(session, "http://2/"));
            buffer.Add(Begin(session, "http://3/"));

            Assert.AreEqual(new[] { "http://2/", "http://3/" }, buffer.Records.Select(r => r.Url).ToArray());
            Assert.AreEqual(new long[] { 2, 3 }, buffer.Records.Select(r => r.Id).ToArray());
            Assert.IsNull(buffer.Update(first), "evicted records are no longer tracked");
        }

        [Test]
        public void BodyBudget_EvictsOldest_ButNeverTheRecordJustWritten()
        {
            var session = new HttpMonitorSession(10);
            var buffer = new EditorRecordBuffer { MaxTotalBodyBytes = 5 };

            var a = buffer.Add(Begin(session, "http://a/", new byte[3]));
            var b = buffer.Add(Begin(session, "http://b/", new byte[3])); // 6 > 5: a goes

            Assert.AreEqual(new[] { b }, buffer.Records.ToArray());
            Assert.AreEqual(3, buffer.StoredBodyBytes);

            Finish(session, b.Runtime, 200, new byte[10]);
            buffer.Update(b.Runtime); // 13 > 5, but b is the only and just-written record: stays

            Assert.AreEqual(new[] { b }, buffer.Records.ToArray());
            Assert.AreEqual(13, buffer.StoredBodyBytes);
            Assert.IsNull(a.Runtime == null ? null : buffer.Update(a.Runtime));
        }

        [Test]
        public void Clear_EmptiesEverything_ButIdsKeepGrowing()
        {
            var session = new HttpMonitorSession(10);
            var buffer = new EditorRecordBuffer();
            var changed = 0;
            buffer.Changed += () => changed++;

            buffer.Add(Begin(session, "http://1/"));
            buffer.Clear();
            var next = buffer.Add(Begin(session, "http://2/"));

            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(2, next.Id);
            Assert.AreEqual(0, buffer.StoredBodyBytes);
            Assert.AreEqual(3, changed);
        }

        [Test]
        public void OnDomainReloaded_SettlesPendingRecords_AndForgetsRuntimeLinks()
        {
            var session = new HttpMonitorSession(10);
            var buffer = new EditorRecordBuffer();

            var done = Begin(session, "http://done/");
            Finish(session, done, 200);
            buffer.Add(done);
            var pending = buffer.Add(Begin(session, "http://pending/"));

            buffer.OnDomainReloaded("reload");

            Assert.AreEqual(HttpRecordState.Completed, buffer.Records[0].State);
            Assert.AreEqual(HttpRecordState.Incomplete, pending.State);
            Assert.AreEqual("reload", pending.Error);
            Assert.IsNull(pending.Runtime);
            Assert.IsNull(buffer.Update(done), "links are gone after a reload");
        }

        [Test]
        public void Bridge_AppliesEventsInOrder_OnDrain_EvenWhenRaisedFromAnotherThread()
        {
            var session = new HttpMonitorSession(10);
            var buffer = new EditorRecordBuffer();
            var seen = new List<string>();
            buffer.RecordAdded += r => seen.Add("added:" + r.Url);
            buffer.RecordUpdated += r => seen.Add("updated:" + r.State);

            using (var bridge = new SessionBridge(session, buffer))
            {
                var worker = new Thread(() =>
                {
                    var record = Begin(session, "http://worker/");
                    Finish(session, record, 204);
                });
                worker.Start();
                worker.Join();

                Assert.AreEqual(2, bridge.Pending);
                Assert.AreEqual(0, buffer.Count, "nothing applied before Drain");

                Assert.AreEqual(2, bridge.Drain());
            }

            Assert.AreEqual(new[] { "added:http://worker/", "updated:Completed" }, seen.ToArray());
            Assert.AreEqual(204, buffer.Records[0].StatusCode);
        }

        [Test]
        public void Bridge_StopsListening_WhenDisposed()
        {
            var session = new HttpMonitorSession(10);
            var buffer = new EditorRecordBuffer();
            var bridge = new SessionBridge(session, buffer);
            bridge.Dispose();

            Begin(session, "http://after-dispose/");

            Assert.AreEqual(0, bridge.Pending);
            Assert.AreEqual(0, bridge.Drain());
        }

        [Test]
        public void Bridge_AddThatArrivesAlreadyFinished_IsCopiedFinished()
        {
            var session = new HttpMonitorSession(10);
            var buffer = new EditorRecordBuffer();

            using (var bridge = new SessionBridge(session, buffer))
            {
                var record = Begin(session, "http://fast/");
                Finish(session, record, 200);
                bridge.Drain();
            }

            Assert.AreEqual(HttpRecordState.Completed, buffer.Records[0].State);
            Assert.AreEqual(200, buffer.Records[0].StatusCode);
        }
    }
}
