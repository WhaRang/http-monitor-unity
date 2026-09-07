using HttpMonitor.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace HttpMonitor.Tests.Editor
{
    public class WindowTests
    {
        [Test]
        public void MenuItem_Exists()
        {
            Assert.IsTrue(Menu.GetEnabled("Window/Analysis/HTTP Monitor"));
        }

        [Test]
        public void Window_Opens_WithToolbarListAndStatusBar()
        {
            var window = EditorWindow.GetWindow<HttpMonitorWindow>();

            try
            {
                var root = window.rootVisualElement;

                Assert.NotNull(root.Q("hm-toolbar"), "toolbar");
                Assert.NotNull(root.Q<MultiColumnListView>("hm-list"), "list");
                Assert.NotNull(root.Q("hm-detail-pane"), "detail pane");
                Assert.NotNull(root.Q("hm-statusbar"), "status bar");
                Assert.NotNull(root.Q("hm-empty"), "empty state");
                Assert.AreEqual("HTTP Monitor", window.titleContent.text);

                Assert.NotNull(root.Q("hm-filterbar"), "filter bar");
                Assert.NotNull(root.Q("hm-search"), "search field");
                Assert.NotNull(root.Q("hm-timeline"), "timeline");

                var list = root.Q<MultiColumnListView>("hm-list");
                var columns = list.columns;
                Assert.AreEqual(10, columns.Count);
                Assert.AreEqual("source", columns[3].name);
                Assert.AreEqual("name", columns[4].name);
                Assert.IsTrue(columns[4].stretchable, "the name column absorbs spare width");
                Assert.AreEqual(ColumnSortingMode.Custom, list.sortingMode, "header clicks must reach the sort machinery");

                for (var i = 0; i < columns.Count; i++)
                    Assert.IsTrue(columns[i].sortable, columns[i].name + " must be sortable");
            }
            finally
            {
                window.Close();
            }
        }
    }

    public class RecordListViewTests
    {
        private static EditorRecord Record(long id, string url = "http://h/p")
        {
            return new EditorRecord { Id = id, Method = "GET", Url = url, State = HttpRecordState.Completed, StatusCode = 200 };
        }

        [Test]
        public void SetRecords_KeepsSelectionByRecordId_AcrossInsertsAndEvictions()
        {
            var view = new RecordListView();
            EditorRecord selected = null;
            view.SelectionChanged += r => selected = r;

            var a = Record(1);
            var b = Record(2);
            view.SetRecords(new[] { a, b });
            view.Q<MultiColumnListView>().SetSelection(1);
            Assert.AreSame(b, selected);

            view.SetRecords(new[] { a, b, Record(3) });
            Assert.AreSame(b, view.SelectedRecord, "an insert after the selection keeps it");

            view.SetRecords(new[] { b, Record(3), Record(4) });
            Assert.AreSame(b, view.SelectedRecord, "an eviction before the selection keeps it");

            view.SetRecords(new[] { Record(3), Record(4) });
            Assert.IsNull(view.SelectedRecord, "an evicted selection is cleared");
            Assert.IsNull(selected, "and the listener hears about it");
        }

        [Test]
        public void SetRecords_ReportsCount()
        {
            var view = new RecordListView();
            view.SetRecords(new[] { Record(1), Record(2), Record(3) });

            Assert.AreEqual(3, view.Count);

            view.SetRecords(new EditorRecord[0]);
            Assert.AreEqual(0, view.Count);
        }

        [Test]
        public void HeaderSort_IsTranslatedToSortColumn_AndRaised()
        {
            var view = new RecordListView();
            var list = view.Q<MultiColumnListView>();
            SortColumn seenColumn = SortColumn.Arrival;
            var seenDescending = false;
            var raised = 0;
            view.SortChanged += (c, d) => { seenColumn = c; seenDescending = d; raised++; };

            view.GetSort(out var initial, out _);
            Assert.AreEqual(SortColumn.Arrival, initial);

            list.sortColumnDescriptions.Add(new SortColumnDescription("size", SortDirection.Descending));
            view.GetSort(out var column, out var descending);
            Assert.AreEqual(SortColumn.Size, column);
            Assert.IsTrue(descending);

            list.sortColumnDescriptions.Clear();
            list.sortColumnDescriptions.Add(new SortColumnDescription("dot", SortDirection.Ascending));
            view.GetSort(out column, out descending);
            Assert.AreEqual(SortColumn.Status, column, "the dot column sorts by status");
            Assert.IsFalse(descending);

            list.sortColumnDescriptions.Clear();
            view.GetSort(out column, out _);
            Assert.AreEqual(SortColumn.Arrival, column, "no description means arrival order");
        }

        [Test]
        public void Select_PicksTheRecord_AndReportsIt()
        {
            var view = new RecordListView();
            EditorRecord selected = null;
            view.SelectionChanged += r => selected = r;
            var b = Record(2);
            view.SetRecords(new[] { Record(1), b, Record(3) });

            view.Select(b);

            Assert.AreSame(b, view.SelectedRecord);
            Assert.AreSame(b, selected);
        }
    }
}
