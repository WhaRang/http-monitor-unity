using System.Linq;
using System.Text;
using HttpMonitor.Editor;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace HttpMonitor.Tests.Editor
{
    public class DetailPaneTests
    {
        private static EditorRecord Completed()
        {
            return new EditorRecord
            {
                Id = 7,
                Method = "POST",
                Url = "https://api.game.com/v1/score?run=3",
                Client = HttpClientKind.HttpClient,
                Source = HttpCaptureSource.Woven,
                State = HttpRecordState.Completed,
                StatusCode = 201,
                DurationMs = 42,
                UploadedBytes = 12,
                DownloadedBytes = 2,
                RequestHeaders = new[] { new EditorHeader("Content-Type", "application/json"), new EditorHeader("Authorization", "<redacted>") },
                ResponseHeaders = new[] { new EditorHeader("Location", "/v1/score/9") },
                RequestBody = Encoding.UTF8.GetBytes("{\"score\":42}"),
                ResponseBody = Encoding.UTF8.GetBytes("ok"),
            };
        }

        [Test]
        public void StatusLine_UsesReasonPhrase_AndStateWords()
        {
            Assert.AreEqual("201 Created", DetailPane.StatusLine(Completed()));
            Assert.AreEqual("299", DetailPane.StatusLine(new EditorRecord { State = HttpRecordState.Completed, StatusCode = 299 }));
            Assert.AreEqual("Pending", DetailPane.StatusLine(new EditorRecord { State = HttpRecordState.Pending }));
            Assert.AreEqual("Failed", DetailPane.StatusLine(new EditorRecord { State = HttpRecordState.Failed }));
        }

        [Test]
        public void MissingBodyReasons_ExplainEachCase()
        {
            var noBody = new EditorRecord { State = HttpRecordState.Completed, StatusCode = 204 };
            Assert.AreEqual("No request body", DetailPane.RequestBodyMissingReason(noBody));
            Assert.AreEqual("No response body", DetailPane.ResponseBodyMissingReason(noBody));

            var fileHandler = new EditorRecord { State = HttpRecordState.Completed, StatusCode = 200, DownloadedBytes = 5000, Client = HttpClientKind.UnityWebRequest };
            Assert.That(DetailPane.ResponseBodyMissingReason(fileHandler), Does.Contain("DownloadHandlerBuffer").And.Contain("4.9 KB"));

            var failed = new EditorRecord { State = HttpRecordState.Failed, Error = "refused" };
            Assert.AreEqual("No response: refused", DetailPane.ResponseBodyMissingReason(failed));

            var pending = new EditorRecord { State = HttpRecordState.Pending };
            Assert.AreEqual("Waiting for the response", DetailPane.ResponseBodyMissingReason(pending));
        }

        [Test]
        public void Show_PopulatesSummaryHeadersAndBodies()
        {
            var pane = new DetailPane();
            pane.Show(Completed());

            var labels = pane.Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.Contains("POST", labels);
            Assert.Contains("https://api.game.com/v1/score?run=3", labels);
            Assert.Contains("201 Created", labels);
            Assert.Contains("Content-Type", labels);
            Assert.Contains("application/json", labels);
            Assert.Contains("Location", labels);
            Assert.Contains("{\n    \"score\": 42\n}", labels, "the JSON request body is pretty-printed by default, shown four spaces per level");
            Assert.Contains("ok", labels, "response body text");
            Assert.That(labels, Has.Some.EqualTo("redacted"), "redacted values become a badge");
            Assert.That(labels, Has.None.EqualTo("<redacted>"), "the placeholder text itself is never shown");

            var tabs = pane.Query<Button>(className: "hm-tab").ToList().Select(b => b.text).ToList();
            Assert.Contains("Headers (2)", tabs);
            Assert.Contains("Headers (1)", tabs);
        }

        [Test]
        public void Show_Null_ShowsThePlaceholder()
        {
            var pane = new DetailPane();
            pane.Show(Completed());
            pane.Show(null);

            Assert.AreEqual(DisplayStyle.Flex, pane.Q(className: "hm-detail-placeholder").style.display.value);
            Assert.AreEqual(DisplayStyle.None, pane.Q(className: "hm-detail-content").style.display.value);
        }

        [Test]
        public void Show_FailedRecord_ShowsTheErrorAndNoResponseBody()
        {
            var pane = new DetailPane();
            pane.Show(new EditorRecord
            {
                Method = "GET",
                Url = "http://127.0.0.1:1/",
                State = HttpRecordState.Failed,
                Error = "Cannot connect to destination host",
                RequestHeaders = new EditorHeader[0],
                ResponseHeaders = new EditorHeader[0],
            });

            var labels = pane.Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.Contains("Cannot connect to destination host", labels);
            Assert.Contains("No response: Cannot connect to destination host", labels);
            Assert.Contains("Failed", labels);
        }
    }
}
