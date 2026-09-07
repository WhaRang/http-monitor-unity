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

                var columns = root.Q<MultiColumnListView>("hm-list").columns;
                Assert.AreEqual(9, columns.Count);
                Assert.AreEqual("name", columns[3].name);
                Assert.IsTrue(columns[3].stretchable, "the name column absorbs spare width");
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
    }

    public class RecordFormatTests
    {
        [Test]
        public void StatusText_And_StatusClass_CoverEveryState()
        {
            var pending = new EditorRecord { State = HttpRecordState.Pending };
            var ok = new EditorRecord { State = HttpRecordState.Completed, StatusCode = 204 };
            var redirect = new EditorRecord { State = HttpRecordState.Completed, StatusCode = 302 };
            var clientError = new EditorRecord { State = HttpRecordState.Completed, StatusCode = 404 };
            var serverError = new EditorRecord { State = HttpRecordState.Completed, StatusCode = 503 };
            var failed = new EditorRecord { State = HttpRecordState.Failed, Error = "refused" };
            var aborted = new EditorRecord { State = HttpRecordState.Aborted, Error = "disposed" };

            Assert.AreEqual("…", RecordFormat.StatusText(pending));
            Assert.AreEqual("204", RecordFormat.StatusText(ok));
            Assert.AreEqual("failed", RecordFormat.StatusText(failed));
            Assert.AreEqual("aborted", RecordFormat.StatusText(aborted));

            Assert.AreEqual("hm-status-dot--pending", RecordFormat.StatusClass(pending));
            Assert.AreEqual("hm-status-dot--2xx", RecordFormat.StatusClass(ok));
            Assert.AreEqual("hm-status-dot--3xx", RecordFormat.StatusClass(redirect));
            Assert.AreEqual("hm-status-dot--4xx", RecordFormat.StatusClass(clientError));
            Assert.AreEqual("hm-status-dot--5xx", RecordFormat.StatusClass(serverError));
            Assert.AreEqual("hm-status-dot--failed", RecordFormat.StatusClass(failed));
            Assert.AreEqual("hm-status-dot--aborted", RecordFormat.StatusClass(aborted));

            Assert.AreEqual("404 Not Found", RecordFormat.StatusTooltip(clientError));
            Assert.AreEqual("No response: refused", RecordFormat.StatusTooltip(failed));
            Assert.AreEqual("Aborted: disposed", RecordFormat.StatusTooltip(aborted));

            Assert.IsTrue(clientError.IsError);
            Assert.IsFalse(ok.IsError);
        }

        [Test]
        public void FormatBytes_PicksSensibleUnits()
        {
            Assert.AreEqual("512 B", RecordFormat.FormatBytes(512));
            Assert.AreEqual("1.5 KB", RecordFormat.FormatBytes(1536));
            Assert.AreEqual("2.0 MB", RecordFormat.FormatBytes(2 * 1024 * 1024));
            Assert.AreEqual("—", RecordFormat.FormatBytes(-1));
        }

        [Test]
        public void FormatDuration_SwitchesToSecondsPastOneSecond()
        {
            Assert.AreEqual("…", RecordFormat.FormatDuration(new EditorRecord { State = HttpRecordState.Pending }));
            Assert.AreEqual("84 ms", RecordFormat.FormatDuration(new EditorRecord { State = HttpRecordState.Completed, DurationMs = 84.4 }));
            Assert.AreEqual("1.25 s", RecordFormat.FormatDuration(new EditorRecord { State = HttpRecordState.Completed, DurationMs = 1250 }));
        }

        [Test]
        public void SourceText_And_Badge_NameBothBits()
        {
            Assert.AreEqual("automatic", RecordFormat.SourceText(HttpCaptureSource.Woven));
            Assert.AreEqual("manual", RecordFormat.SourceText(HttpCaptureSource.Manual));
            Assert.AreEqual("automatic + manual", RecordFormat.SourceText(HttpCaptureSource.Woven | HttpCaptureSource.Manual));
            Assert.AreEqual("A", RecordFormat.SourceBadge(HttpCaptureSource.Woven));
            Assert.AreEqual("M", RecordFormat.SourceBadge(HttpCaptureSource.Manual));
            Assert.AreEqual("A+M", RecordFormat.SourceBadge(HttpCaptureSource.Woven | HttpCaptureSource.Manual));
        }

        [Test]
        public void Name_And_Host_SplitTheUrl()
        {
            Assert.AreEqual("/v1/profile?x=1", RecordFormat.Name("https://api.game.com/v1/profile?x=1"));
            Assert.AreEqual("api.game.com", RecordFormat.Host("https://api.game.com/v1/profile?x=1"));
            Assert.AreEqual("/", RecordFormat.Name("https://api.game.com"));
            Assert.AreEqual("custom://service/items/1", RecordFormat.Name("custom://service/items/1"), "no host: whole url");
            Assert.AreEqual("not a url", RecordFormat.Name("not a url"));
            Assert.AreEqual(string.Empty, RecordFormat.Host("not a url"));
        }

        [Test]
        public void ShortType_ReducesContentType()
        {
            EditorRecord With(string contentType) => new EditorRecord
            {
                State = HttpRecordState.Completed,
                ResponseHeaders = contentType == null ? new EditorHeader[0] : new[] { new EditorHeader("content-type", contentType) },
            };

            Assert.AreEqual("json", RecordFormat.ShortType(With("application/json; charset=utf-8")));
            Assert.AreEqual("json", RecordFormat.ShortType(With("application/problem+json")));
            Assert.AreEqual("html", RecordFormat.ShortType(With("text/html")));
            Assert.AreEqual("png", RecordFormat.ShortType(With("image/png")));
            Assert.AreEqual("www-form-urlencoded", RecordFormat.ShortType(With("application/x-www-form-urlencoded")));
            Assert.AreEqual("—", RecordFormat.ShortType(With(null)));
            Assert.AreEqual(string.Empty, RecordFormat.ShortType(new EditorRecord { State = HttpRecordState.Pending, ResponseHeaders = new EditorHeader[0] }));
        }

        [Test]
        public void ToCurl_EmitsMethodHeadersAndBody_WithRedactedValuesAsVariables()
        {
            var record = new EditorRecord
            {
                Method = "POST",
                Url = "https://api.game.com/v1/score?it's=1",
                RequestHeaders = new[]
                {
                    new EditorHeader("Content-Type", "application/json"),
                    new EditorHeader("Authorization", "<redacted>"),
                },
                RequestBody = System.Text.Encoding.UTF8.GetBytes("{\"score\":42}"),
            };

            var curl = RecordFormat.ToCurl(record, "<redacted>");

            Assert.AreEqual(
                "curl -X POST 'https://api.game.com/v1/score?it'\\''s=1' \\\n" +
                "  -H 'Content-Type: application/json' \\\n" +
                "  -H 'Authorization: $AUTHORIZATION' \\\n" +
                "  --data-raw '{\"score\":42}'",
                curl);
        }

        [Test]
        public void ToCurl_ForAPlainGet_IsMinimal()
        {
            var record = new EditorRecord { Method = "GET", Url = "https://example.com/", RequestHeaders = new EditorHeader[0] };

            Assert.AreEqual("curl 'https://example.com/'", RecordFormat.ToCurl(record, "<redacted>"));
        }

        [Test]
        public void ToCurl_BinaryBody_IsNotInlined()
        {
            var record = new EditorRecord { Method = "PUT", Url = "https://example.com/", RequestHeaders = new EditorHeader[0], RequestBody = new byte[] { 0, 1, 2, 255 } };

            Assert.That(RecordFormat.ToCurl(record, "<redacted>"), Does.Contain("--data-binary @request-body.bin"));
        }

        [Test]
        public void LooksLikeText_RejectsControlBytes()
        {
            Assert.IsTrue(RecordFormat.LooksLikeText(System.Text.Encoding.UTF8.GetBytes("{\"a\":1}\r\n\tx")));
            Assert.IsFalse(RecordFormat.LooksLikeText(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
            Assert.IsFalse(RecordFormat.LooksLikeText(null));
        }
    }
}
