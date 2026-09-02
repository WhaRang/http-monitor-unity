using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace HttpMonitor.Tests
{
    /// <summary>Pure in-memory tests of <see cref="HttpMonitorSession"/>; no requests involved.</summary>
    public class SessionRingBufferTests
    {
        private static HttpRecord Begin(HttpMonitorSession session, int n)
        {
            return session.Begin(HttpClientKind.Manual, "GET", "https://example.com/" + n, null);
        }

        [Test]
        public void Capacity_MustBePositive()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new HttpMonitorSession(0));
        }

        [Test]
        public void Snapshot_IsOldestFirst_WithIncreasingIds()
        {
            var session = new HttpMonitorSession(10);

            for (var i = 1; i <= 3; i++)
                Begin(session, i);

            var snapshot = session.Snapshot();

            Assert.AreEqual(3, session.Count);
            Assert.AreEqual(new long[] { 1, 2, 3 }, snapshot.Select(r => r.Id).ToArray());
            Assert.AreEqual("https://example.com/1", snapshot[0].Url);
        }

        [Test]
        public void WhenFull_OldestIsEvicted_AndOrderIsKept()
        {
            var session = new HttpMonitorSession(3);

            for (var i = 1; i <= 5; i++)
                Begin(session, i);

            Assert.AreEqual(3, session.Count);
            Assert.AreEqual(3, session.Capacity);
            Assert.AreEqual(new long[] { 3, 4, 5 }, session.Snapshot().Select(r => r.Id).ToArray());
        }

        [Test]
        public void Eviction_KeepsWorkingAcrossManyWraps()
        {
            var session = new HttpMonitorSession(4);

            for (var i = 1; i <= 23; i++)
                Begin(session, i);

            Assert.AreEqual(new long[] { 20, 21, 22, 23 }, session.Snapshot().Select(r => r.Id).ToArray());
        }

        [Test]
        public void Clear_EmptiesTheBuffer_FiresCleared_AndIdsKeepIncreasing()
        {
            var session = new HttpMonitorSession(3);
            var clearedCount = 0;
            session.Cleared += () => clearedCount++;

            Begin(session, 1);
            Begin(session, 2);
            session.Clear();

            Assert.AreEqual(0, session.Count);
            Assert.IsEmpty(session.Snapshot());
            Assert.AreEqual(1, clearedCount);

            var next = Begin(session, 3);

            Assert.AreEqual(3, next.Id);
            Assert.AreEqual(1, session.Count);
        }

        [Test]
        public void NewRecord_IsPending_AndFinishWritesOutcomeOnce()
        {
            var session = new HttpMonitorSession(3);
            var record = Begin(session, 1);

            Assert.AreEqual(HttpRecordState.Pending, record.State);
            Assert.IsFalse(record.IsFinished);
            Assert.AreEqual(0, record.StatusCode);
            Assert.IsNull(record.Error);
            Assert.IsEmpty(record.ResponseHeaders);

            session.Finish(record, new HttpRecordOutcome
            {
                State = HttpRecordState.Completed,
                StatusCode = 204,
                DurationMs = 12.5,
                ResponseHeaders = new List<HttpHeader> { new HttpHeader("X-Test", "1") },
                DownloadedBytes = 7,
                UploadedBytes = 3,
            });

            Assert.IsTrue(record.IsFinished);
            Assert.AreEqual(HttpRecordState.Completed, record.State);
            Assert.AreEqual(204, record.StatusCode);
            Assert.AreEqual(12.5, record.DurationMs);
            Assert.AreEqual("X-Test: 1", record.ResponseHeaders[0].ToString());
            Assert.AreEqual(7, record.DownloadedBytes);
            Assert.AreEqual(3, record.UploadedBytes);
        }

        [Test]
        public void Events_FireInOrder_WithExpectedStates()
        {
            var session = new HttpMonitorSession(3);
            var seen = new List<string>();
            session.RecordAdded += r => seen.Add("added:" + r.State);
            session.RecordUpdated += r => seen.Add("updated:" + r.State);

            var record = Begin(session, 1);
            session.Finish(record, new HttpRecordOutcome { State = HttpRecordState.Failed, Error = "x" });

            Assert.AreEqual(new[] { "added:Pending", "updated:Failed" }, seen.ToArray());
        }
    }
}
