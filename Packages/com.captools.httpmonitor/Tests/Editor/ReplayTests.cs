using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using HttpMonitor.Editor;
using NUnit.Framework;

namespace HttpMonitor.Tests.Editor
{
    public class ReplayHeaderTests
    {
        [Test]
        public void Classify_DropsWhatTheClientOwns_AndHopByHop()
        {
            foreach (var name in new[] { "Host", "host", "Content-Length", "Connection", "Keep-Alive", "Transfer-Encoding", "TE", "Trailer", "Upgrade", "Expect", "Proxy-Connection", "", " ", null })
                Assert.AreEqual(HeaderPlacement.Drop, ReplayHeaders.Classify(name), name ?? "null");
        }

        [Test]
        public void Classify_PutsContentHeadersOnTheContent_AndTheRestOnTheRequest()
        {
            foreach (var name in new[] { "Content-Type", "content-encoding", "Content-Disposition", "Allow", "Expires", "Last-Modified" })
                Assert.AreEqual(HeaderPlacement.Content, ReplayHeaders.Classify(name), name);

            foreach (var name in new[] { "Authorization", "Accept", "X-Trace", "User-Agent", "Cookie", "If-None-Match" })
                Assert.AreEqual(HeaderPlacement.Request, ReplayHeaders.Classify(name), name);
        }

        [Test]
        public void Build_PlacesHeaders_AndOnlyAttachesContentWhenItMakesSense()
        {
            var request = new ReplayRequest
            {
                Method = "post",
                Url = "https://api.game.com/v1/score",
                Body = Encoding.UTF8.GetBytes("{\"a\":1}"),
                Headers =
                {
                    new EditorHeader("Content-Type", "application/json"),
                    new EditorHeader("Authorization", "Bearer t"),
                    new EditorHeader("Host", "wrong.example"),
                    new EditorHeader("Content-Length", "999"),
                },
            };

            using (var message = ReplayService.Build(request))
            {
                Assert.AreEqual("POST", message.Method.Method);
                Assert.AreEqual("https://api.game.com/v1/score", message.RequestUri.ToString());
                Assert.IsTrue(message.Headers.Contains("Authorization"));
                Assert.IsFalse(message.Headers.Contains("Host"), "Host is the client's to set");
                Assert.NotNull(message.Content);
                Assert.AreEqual("application/json", message.Content.Headers.ContentType.MediaType);
                Assert.AreEqual(7, message.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult().Length, "Content-Length comes from the body, not the captured header");
            }

            using (var get = ReplayService.Build(new ReplayRequest { Method = "GET", Url = "https://a/" }))
                Assert.IsNull(get.Content, "a GET without a body sends no content");

            using (var emptyPost = ReplayService.Build(new ReplayRequest { Method = "POST", Url = "https://a/" }))
                Assert.NotNull(emptyPost.Content, "a POST always has a content object, so Content-Length: 0 goes out");
        }
    }

    public class ReplayRequestTests
    {
        private static EditorRecord Record()
        {
            return new EditorRecord
            {
                Id = 12,
                Method = "PUT",
                Url = "https://api.game.com/v1/x",
                RequestHeaders = new[] { new EditorHeader("Authorization", "<redacted>"), new EditorHeader("X-Trace", "abc"), new EditorHeader("Cookie", "<redacted>") },
                RequestBody = Encoding.UTF8.GetBytes("body"),
            };
        }

        [Test]
        public void From_CopiesTheRequest_AndBlanksRedactedValues()
        {
            var request = ReplayRequest.From(Record(), "<redacted>");

            Assert.AreEqual("PUT", request.Method);
            Assert.AreEqual("https://api.game.com/v1/x", request.Url);
            Assert.AreEqual(12, request.OriginalId);
            Assert.AreEqual("body", Encoding.UTF8.GetString(request.Body));
            Assert.IsTrue(request.HasRedactedHeaders);
            Assert.AreEqual(new[] { "Authorization", "Cookie" }, request.RedactedHeaderNames.ToArray());
            Assert.AreEqual(string.Empty, request.Headers[0].Value);
            Assert.AreEqual("abc", request.Headers[1].Value);
            Assert.IsFalse(request.IsSafeMethod);
            Assert.IsTrue(new ReplayRequest { Method = "get" }.IsSafeMethod);
        }

        [Test]
        public void From_Body_IsACopy()
        {
            var record = Record();
            var request = ReplayRequest.From(record, "<redacted>");
            request.Body[0] = (byte)'X';

            Assert.AreEqual("body", Encoding.UTF8.GetString(record.RequestBody));
        }

        [Test]
        public void SupplyRedactedValue_FillsTheHeader_AndClearsThePendingList()
        {
            var request = ReplayRequest.From(Record(), "<redacted>");

            Assert.IsTrue(request.SupplyRedactedValue("authorization", "Bearer real"));
            Assert.AreEqual("Bearer real", request.Headers[0].Value);
            Assert.AreEqual(new[] { "Cookie" }, request.RedactedHeaderNames.ToArray());
            Assert.IsFalse(request.SupplyRedactedValue("X-Trace", "x"), "not a redacted header");
            Assert.IsFalse(request.SupplyRedactedValue("authorization", "again"), "already supplied");
        }
    }

    public class ReplayServiceTests
    {
        private HttpMonitorSession _session;
        private EditorRecordBuffer _buffer;
        private SessionBridge _bridge;
        private ReplayService _service;

        [SetUp]
        public void SetUp()
        {
            _session = new HttpMonitorSession(32);
            _buffer = new EditorRecordBuffer();
            _bridge = new SessionBridge(_session, _buffer);
            _service = new ReplayService(_session, _buffer);
        }

        [TearDown]
        public void TearDown()
        {
            _bridge.Dispose();
        }

        private HttpRecord Send(ReplayRequest request)
        {
            var record = _service.SendAsync(request).GetAwaiter().GetResult();
            _bridge.Drain();

            return record;
        }

        [Test]
        public void EchoPost_RoundTrips_AndIsLinkedToItsOriginal()
        {
            var body = Encoding.UTF8.GetBytes("{\"replayed\":true}");
            var request = new ReplayRequest
            {
                Method = "POST",
                Url = TestServer.Shared.Url("echo?from=replay"),
                Body = body,
                OriginalId = 7,
                Headers = { new EditorHeader("Content-Type", "application/json"), new EditorHeader("X-Custom", "yes"), new EditorHeader("Host", "ignored.example") },
            };

            var record = Send(request);

            Assert.AreEqual(HttpRecordState.Completed, record.State);
            Assert.AreEqual(200, record.StatusCode);
            Assert.AreEqual(HttpClientKind.HttpClient, record.Client);
            Assert.AreEqual(HttpCaptureSource.Manual, record.Source);
            Assert.AreEqual("POST", record.Method);
            Assert.AreEqual(body, record.RequestBody);
            Assert.AreEqual(body, record.ResponseBody, "the echo route returns the request body");
            Assert.AreEqual(body.Length, record.DownloadedBytes);
            Assert.AreEqual(body.Length, record.UploadedBytes);
            Assert.Greater(record.DurationMs, 0);
            Assert.That(record.ResponseHeaders, Has.Some.Matches<HttpHeader>(h => h.Name == "X-Echo-X-Custom" && h.Value == "yes"), "custom headers were sent");
            Assert.That(record.ResponseHeaders, Has.Some.Matches<HttpHeader>(h => h.Name == "X-Echo-Content-Type" && h.Value == "application/json"));

            var copy = _buffer.Records.Single();
            Assert.AreSame(record, copy.Runtime);
            Assert.AreEqual(7, copy.ReplayOfId);
            Assert.IsTrue(copy.IsReplay);
        }

        [Test]
        public void RedactionAndCaps_ApplyToReplays_LikeLiveTraffic()
        {
            _session.Options.MaxBodyBytes = 10;
            var request = new ReplayRequest
            {
                Method = "GET",
                Url = TestServer.Shared.Url("bytes/500?from=replay-cap"),
                Headers = { new EditorHeader("Authorization", "Bearer secret") },
            };

            var record = Send(request);

            Assert.AreEqual(HttpRecordState.Completed, record.State);
            Assert.AreEqual("<redacted>", record.RequestHeaders[0].Value);
            Assert.AreEqual(10, record.ResponseBody.Length);
            Assert.IsTrue(record.ResponseBodyTruncated);
            Assert.AreEqual(500, record.DownloadedBytes, "the whole response is read so the size is right");
        }

        [Test]
        public void ReplayIsRecorded_EvenWhileCaptureIsPaused()
        {
            _session.IsRecording = false;

            var record = Send(new ReplayRequest { Method = "GET", Url = TestServer.Shared.Url("echo?from=paused") });

            Assert.AreEqual(HttpRecordState.Completed, record.State);
            Assert.AreEqual(1, _buffer.Count);
        }

        [Test]
        public void ConnectionRefused_RecordsFailed_WithoutThrowing()
        {
            var record = Send(new ReplayRequest { Method = "GET", Url = TestServer.UnreachableUrl });

            Assert.AreEqual(HttpRecordState.Failed, record.State);
            Assert.IsNotEmpty(record.Error);
            Assert.AreEqual(0, record.StatusCode);
        }

        [Test]
        public void Timeout_RecordsAborted()
        {
            var record = Send(new ReplayRequest { Method = "GET", Url = TestServer.Shared.Url("delay/3000?from=replay-timeout"), TimeoutSeconds = 1 });

            Assert.AreEqual(HttpRecordState.Aborted, record.State);
            Assert.That(record.Error, Does.Contain("timed out"));
        }

        [Test]
        public void Cancellation_RecordsAborted()
        {
            using (var cts = new CancellationTokenSource(200))
            {
                var record = _service.SendAsync(new ReplayRequest { Method = "GET", Url = TestServer.Shared.Url("delay/3000?from=replay-cancel") }, cts.Token).GetAwaiter().GetResult();

                Assert.AreEqual(HttpRecordState.Aborted, record.State);
                Assert.AreEqual("cancelled", record.Error);
            }
        }

        [Test]
        public void HttpErrorStatus_IsCompleted()
        {
            var record = Send(new ReplayRequest { Method = "GET", Url = TestServer.Shared.Url("status/503?from=replay-503") });

            Assert.AreEqual(HttpRecordState.Completed, record.State);
            Assert.AreEqual(503, record.StatusCode);
        }
    }

    public class ReplayLinkTests
    {
        private static HttpRecord Begin(HttpMonitorSession session, string url)
        {
            return session.Begin(HttpClientKind.HttpClient, HttpCaptureSource.Manual, "GET", url, null);
        }

        [Test]
        public void MarkReplayOf_BeforeArrival_IsAppliedWhenTheCopyArrives()
        {
            var session = new HttpMonitorSession(8);
            var buffer = new EditorRecordBuffer();
            var runtime = Begin(session, "http://a/");

            buffer.MarkReplayOf(runtime, 3);
            var copy = buffer.Add(runtime);

            Assert.AreEqual(3, copy.ReplayOfId);
        }

        [Test]
        public void MarkReplayOf_AfterArrival_UpdatesInPlace_AndFiresUpdated()
        {
            var session = new HttpMonitorSession(8);
            var buffer = new EditorRecordBuffer();
            var runtime = Begin(session, "http://a/");
            var copy = buffer.Add(runtime);
            var updated = 0;
            buffer.RecordUpdated += _ => updated++;
            var revision = copy.Revision;

            buffer.MarkReplayOf(runtime, 5);

            Assert.AreEqual(5, copy.ReplayOfId);
            Assert.AreEqual(1, updated);
            Assert.Greater(copy.Revision, revision);
        }

        [Test]
        public void MarkReplayOf_IgnoresNonsense()
        {
            var buffer = new EditorRecordBuffer();
            buffer.MarkReplayOf(null, 1);
            buffer.MarkReplayOf(Begin(new HttpMonitorSession(2), "http://a/"), 0);

            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void Har_RoundTrips_ReplayLinks_ThroughRenumbering()
        {
            var original = new EditorRecord { Id = 40, Method = "GET", Url = "http://a/", State = HttpRecordState.Completed, StatusCode = 200 };
            var replay = new EditorRecord { Id = 41, Method = "GET", Url = "http://a/", State = HttpRecordState.Completed, StatusCode = 200, ReplayOfId = 40 };
            var orphan = new EditorRecord { Id = 42, Method = "GET", Url = "http://b/", State = HttpRecordState.Completed, StatusCode = 200, ReplayOfId = 99 };

            var imported = HarReader.Read(HarWriter.Write(new[] { original, replay, orphan }, "1.0"));
            Assert.AreEqual(40, imported[1].ReplayOfId, "the reader keeps the exported id");

            var buffer = new EditorRecordBuffer();
            buffer.AddImported(imported);
            var records = buffer.Records.ToList();

            Assert.AreEqual(records[0].Id, records[1].ReplayOfId, "renumbered along with its original");
            Assert.AreEqual(99, records[2].ReplayOfId, "an original that is not in the file keeps its old id");
        }
    }

    public class ManualCaptureClientKindTests
    {
        [Test]
        public void Begin_DefaultsToCustom_AndAcceptsAKind()
        {
            HttpMonitorSession.Current.IsRecording = true;

            var custom = HttpMonitorCapture.Begin("GET", "custom://a");
            var asHttpClient = HttpMonitorCapture.Begin("GET", "https://a/", client: HttpClientKind.HttpClient);

            try
            {
                Assert.AreEqual(HttpClientKind.Custom, custom.Record.Client);
                Assert.AreEqual(HttpClientKind.HttpClient, asHttpClient.Record.Client);
                Assert.AreEqual(HttpCaptureSource.Manual, asHttpClient.Record.Source);
            }
            finally
            {
                custom.Abort("test");
                asHttpClient.Abort("test");
            }
        }
    }
}
