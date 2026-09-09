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
        public void LaneCount_FollowsTheHeight_AndOverflowSharesTheLastLane()
        {
            // Four requests all overlapping: four lanes when there is room, else the extras pile into the last lane.
            var records = new[] { R(1, 0, 100), R(2, 10, 100), R(3, 20, 100), R(4, 30, 100) };

            var tall = new TimelineView();
            tall.SetExpandedHeightForTests(400);
            tall.SetRecords(records);
            Assert.GreaterOrEqual(tall.AvailableLanes, 4);
            Assert.AreEqual(new[] { 0, 1, 2, 3 }, tall.Lanes());

            var short_ = new TimelineView();
            short_.SetExpandedHeightForTests(70);
            short_.SetRecords(records);
            var lanes = short_.AvailableLanes;
            Assert.Less(lanes, 4);
            Assert.AreEqual(lanes, short_.LaneCount);
            Assert.AreEqual(lanes - 1, short_.LaneOf(records[3]), "the overflow lands in the last lane");
        }

        [Test]
        public void Collapsed_KeepsTheRecords_AndRestoresOnExpand()
        {
            var view = new TimelineView();
            view.SetRecords(new[] { R(1, 0, 10), R(2, 5, 10) });
            var before = view.Lanes();

            view.Collapsed = true;
            Assert.IsTrue(view.Collapsed);
            Assert.AreEqual(before, view.Lanes(), "geometry is still computed while collapsed");

            view.Collapsed = false;
            Assert.AreEqual(before, view.Lanes());
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
