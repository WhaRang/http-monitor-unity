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
                Assert.NotNull(root.Q("hm-list"), "list");
                Assert.NotNull(root.Q("hm-detail-pane"), "detail pane");
                Assert.NotNull(root.Q("hm-statusbar"), "status bar");
                Assert.NotNull(root.Q("hm-empty"), "empty state");
                Assert.AreEqual("HTTP Monitor", window.titleContent.text);
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void StatusText_And_StatusClass_CoverEveryState()
        {
            var pending = new EditorRecord { State = HttpRecordState.Pending };
            var ok = new EditorRecord { State = HttpRecordState.Completed, StatusCode = 204 };
            var redirect = new EditorRecord { State = HttpRecordState.Completed, StatusCode = 302 };
            var clientError = new EditorRecord { State = HttpRecordState.Completed, StatusCode = 404 };
            var serverError = new EditorRecord { State = HttpRecordState.Completed, StatusCode = 503 };
            var failed = new EditorRecord { State = HttpRecordState.Failed };
            var aborted = new EditorRecord { State = HttpRecordState.Aborted };

            Assert.AreEqual("…", HttpMonitorWindow.StatusText(pending));
            Assert.AreEqual("204", HttpMonitorWindow.StatusText(ok));
            Assert.AreEqual("failed", HttpMonitorWindow.StatusText(failed));
            Assert.AreEqual("aborted", HttpMonitorWindow.StatusText(aborted));

            Assert.AreEqual("hm-status-dot--pending", HttpMonitorWindow.StatusClass(pending));
            Assert.AreEqual("hm-status-dot--2xx", HttpMonitorWindow.StatusClass(ok));
            Assert.AreEqual("hm-status-dot--3xx", HttpMonitorWindow.StatusClass(redirect));
            Assert.AreEqual("hm-status-dot--4xx", HttpMonitorWindow.StatusClass(clientError));
            Assert.AreEqual("hm-status-dot--5xx", HttpMonitorWindow.StatusClass(serverError));
            Assert.AreEqual("hm-status-dot--failed", HttpMonitorWindow.StatusClass(failed));
            Assert.AreEqual("hm-status-dot--aborted", HttpMonitorWindow.StatusClass(aborted));

            Assert.IsTrue(clientError.IsError);
            Assert.IsFalse(ok.IsError);
        }

        [Test]
        public void FormatBytes_PicksSensibleUnits()
        {
            Assert.AreEqual("512 B", HttpMonitorWindow.FormatBytes(512));
            Assert.AreEqual("1.5 KB", HttpMonitorWindow.FormatBytes(1536));
            Assert.AreEqual("2.0 MB", HttpMonitorWindow.FormatBytes(2 * 1024 * 1024));
        }

        [Test]
        public void SourceText_NamesBothBits()
        {
            Assert.AreEqual("automatic", HttpMonitorWindow.SourceText(HttpCaptureSource.Woven));
            Assert.AreEqual("manual", HttpMonitorWindow.SourceText(HttpCaptureSource.Manual));
            Assert.AreEqual("automatic + manual", HttpMonitorWindow.SourceText(HttpCaptureSource.Woven | HttpCaptureSource.Manual));
        }
    }
}
