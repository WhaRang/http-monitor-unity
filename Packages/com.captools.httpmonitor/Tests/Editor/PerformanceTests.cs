using System;
using System.Diagnostics;
using System.Text;
using HttpMonitor.Editor;
using NUnit.Framework;

namespace HttpMonitor.Tests.Editor
{
    /// <summary>
    /// Budget checks at 5000 records with 1 MB bodies. Generous ceilings (test machines vary); the
    /// point is to catch an accidental O(n²) or a per-row allocation storm, not to benchmark.
    /// </summary>
    public class PerformanceTests
    {
        private const int Count = 5000;

        private static EditorRecord[] Records(int bodyBytes)
        {
            var body = new byte[bodyBytes];

            for (var i = 0; i < body.Length; i++)
                body[i] = (byte)('a' + i % 26);

            var records = new EditorRecord[Count];

            for (var i = 0; i < Count; i++)
            {
                records[i] = new EditorRecord
                {
                    Id = i + 1,
                    Method = i % 3 == 0 ? "POST" : "GET",
                    Url = $"https://api.game.com/v1/things/{i % 97}?page={i}",
                    State = HttpRecordState.Completed,
                    StatusCode = i % 50 == 0 ? 500 : 200,
                    DurationMs = 10 + i % 400,
                    DownloadedBytes = bodyBytes,
                    StartedAtUtcTicks = DateTime.UtcNow.Ticks + i * TimeSpan.TicksPerMillisecond * 7,
                    RequestHeaders = new[] { new EditorHeader("Accept", "application/json") },
                    ResponseHeaders = new[] { new EditorHeader("Content-Type", "application/json") },
                    ResponseBody = body,
                };
            }

            return records;
        }

        private static long Time(Action action)
        {
            var sw = Stopwatch.StartNew();
            action();

            return sw.ElapsedMilliseconds;
        }

        [Test]
        public void Query_FilterAndSort_At5000Records_IsFast()
        {
            var records = Records(64);
            var text = new RecordQuery { Text = "things/4" };
            var sort = new RecordQuery { SortBy = SortColumn.Time, SortDescending = true };

            Assert.Less(Time(() => text.Apply(records)), 200, "text filter");
            Assert.Less(Time(() => sort.Apply(records)), 200, "sort");
        }

        [Test]
        public void ListView_SetRecords_At5000_IsFast()
        {
            var view = new RecordListView();
            var records = Records(64);

            Assert.Less(Time(() => view.SetRecords(records)), 500, "first fill");
            Assert.Less(Time(() => view.SetRecords(records)), 500, "refill");
            Assert.AreEqual(Count, view.Count);
        }

        [Test]
        public void Timeline_Layout_At5000_IsFast()
        {
            var view = new TimelineView();
            var records = Records(64);

            Assert.Less(Time(() => view.SetRecords(records)), 300);
        }

        [Test]
        public void Buffer_Add5000_WithBodyBudget_StaysBounded()
        {
            var buffer = new EditorRecordBuffer { Capacity = Count, MaxTotalBodyBytes = 16L * 1024 * 1024 };
            var session = new HttpMonitorSession(Count);
            var body = new byte[1024 * 1024];

            var elapsed = Time(() =>
            {
                for (var i = 0; i < Count; i++)
                {
                    var record = session.Begin(HttpClientKind.UnityWebRequest, HttpCaptureSource.Woven, "GET", "https://h/" + i, null, i % 100 == 0 ? body : null);
                    buffer.Add(record);
                }
            });

            Assert.Less(elapsed, 2000);
            Assert.LessOrEqual(buffer.StoredBodyBytes, 16L * 1024 * 1024, "the body budget holds");
            Assert.LessOrEqual(buffer.Count, Count);
        }

        [Test]
        public void JsonFormatter_OneMegabyte_IsFast()
        {
            var sb = new StringBuilder("[");

            for (var i = 0; sb.Length < 1024 * 1024; i++)
                sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(i).Append(",\"name\":\"item ").Append(i).Append("\",\"tags\":[\"a\",\"b\"],\"ok\":true}");

            sb.Append(']');
            var json = sb.ToString();

            var elapsed = Time(() => Assert.IsTrue(JsonFormatter.TryFormat(json, out _)));
            Assert.Less(elapsed, 500);
        }

        [Test]
        public void HarWriter_5000Records_IsFast()
        {
            var records = Records(256);

            Assert.Less(Time(() => HarWriter.Write(records, "1.0")), 1500);
        }
    }
}
