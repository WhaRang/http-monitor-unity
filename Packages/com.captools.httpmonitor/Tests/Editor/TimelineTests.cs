using System.Linq;
using HttpMonitor.Editor;
using NUnit.Framework;
using UnityEngine;

namespace HttpMonitor.Tests.Editor
{
    public class TimelineTests
    {
        private static EditorRecord R(long id, long startMs, double durationMs, long status = 200)
        {
            return new EditorRecord
            {
                Id = id,
                Method = "GET",
                Url = "http://h/" + id,
                State = HttpRecordState.Completed,
                StatusCode = status,
                DurationMs = durationMs,
                StartedAtUtcTicks = startMs * System.TimeSpan.TicksPerMillisecond,
            };
        }

        [Test]
        public void BarColor_FollowsStatus()
        {
            Color C(HttpRecordState state, long status) => TimelineView.BarColor(new EditorRecord { State = state, StatusCode = status });

            Assert.AreNotEqual(C(HttpRecordState.Completed, 200), C(HttpRecordState.Completed, 404));
            Assert.AreNotEqual(C(HttpRecordState.Completed, 404), C(HttpRecordState.Completed, 500));
            Assert.AreEqual(C(HttpRecordState.Completed, 500), C(HttpRecordState.Failed, 0), "5xx and transport failure share red");
            Assert.AreEqual(C(HttpRecordState.Aborted, 0), C(HttpRecordState.Incomplete, 0));
            Assert.Less(C(HttpRecordState.Pending, 0).a, 1f, "pending is translucent");
        }

        [Test]
        public void SetRecords_AcceptsEmptyAndPending_WithoutLayoutPass()
        {
            var view = new TimelineView();

            view.SetRecords(new EditorRecord[0]);
            view.SetRecords(new[]
            {
                new EditorRecord { Id = 1, State = HttpRecordState.Pending, StartedAtUtcTicks = System.DateTime.UtcNow.Ticks },
                R(2, 0, 50),
            });
            view.SetSelected(null);

            Assert.Pass("no exception before the element has a size");
        }

        [Test]
        public void LanePacking_DoesNotDependOnInputOrder()
        {
            // Two overlapping requests and one after both: 2 lanes either way, same lane per record.
            var a = R(1, 0, 100);
            var b = R(2, 50, 100);
            var c = R(3, 200, 10);

            var natural = new TimelineView();
            natural.SetRecords(new[] { a, b, c });
            var sorted = new TimelineView();
            sorted.SetRecords(new[] { c, b, a }); // as if the table were sorted descending by time

            Assert.AreEqual(natural.Lanes(), sorted.Lanes());
            Assert.AreEqual(new[] { 1L, 2L, 3L }, sorted.OrderedIds(), "packing walks records by start time");
            Assert.AreEqual(2, natural.LaneCount);
            Assert.AreEqual(0, natural.LaneOf(a));
            Assert.AreEqual(1, natural.LaneOf(b));
            Assert.AreEqual(0, natural.LaneOf(c), "c starts after a ended, so it reuses lane 0");
        }

        [Test]
        public void Query_Filter_KeepsArrivalOrder_WhileApply_Sorts()
        {
            var records = new[] { R(1, 0, 30), R(2, 10, 10), R(3, 20, 20) };
            var query = new RecordQuery { SortBy = SortColumn.Time, SortDescending = true };

            Assert.AreEqual(new long[] { 1, 3, 2 }, query.Apply(records).Select(r => r.Id).ToArray());
            Assert.AreEqual(new long[] { 1, 2, 3 }, query.Filter(records).Select(r => r.Id).ToArray());
        }
    }
}
