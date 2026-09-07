using System.Linq;
using HttpMonitor.Editor;
using NUnit.Framework;

namespace HttpMonitor.Tests.Editor
{
    public class RecordQueryTests
    {
        private static EditorRecord R(long id, string method = "GET", string url = "https://api.game.com/v1/x",
            HttpRecordState state = HttpRecordState.Completed, long status = 200,
            HttpCaptureSource source = HttpCaptureSource.Woven, HttpClientKind client = HttpClientKind.UnityWebRequest,
            double duration = 10, long size = 100, params EditorHeader[] responseHeaders)
        {
            return new EditorRecord
            {
                Id = id,
                Method = method,
                Url = url,
                State = state,
                StatusCode = status,
                Source = source,
                Client = client,
                DurationMs = duration,
                DownloadedBytes = size,
                StartedAtUtcTicks = id * 1000,
                RequestHeaders = new EditorHeader[0],
                ResponseHeaders = responseHeaders,
                Error = state == HttpRecordState.Failed ? "refused" : null,
            };
        }

        private static long[] Ids(RecordQuery query, params EditorRecord[] records)
        {
            return query.Apply(records).Select(r => r.Id).ToArray();
        }

        [Test]
        public void NoFilters_ReturnsEverything_InArrivalOrder()
        {
            var query = new RecordQuery();

            Assert.IsFalse(query.IsFiltering);
            Assert.AreEqual(new long[] { 1, 2, 3 }, Ids(query, R(1), R(2), R(3)));
        }

        [Test]
        public void Text_MatchesUrlMethodStatusAndHeaders_CaseInsensitively()
        {
            var records = new[]
            {
                R(1, url: "https://api.game.com/Profile"),
                R(2, method: "POST", url: "https://cdn.game.com/asset"),
                R(3, status: 404, url: "https://other.com/"),
                R(4, url: "https://other.com/", responseHeaders: new EditorHeader("X-Cache", "HIT")),
                R(5, state: HttpRecordState.Failed, status: 0, url: "https://down.com/"),
            };

            Assert.AreEqual(new long[] { 1 }, Ids(new RecordQuery { Text = "profile" }, records));
            Assert.AreEqual(new long[] { 2 }, Ids(new RecordQuery { Text = "post" }, records));
            Assert.AreEqual(new long[] { 3 }, Ids(new RecordQuery { Text = "40" }, records), "status code prefix");
            Assert.AreEqual(new long[] { 4 }, Ids(new RecordQuery { Text = "x-cache" }, records), "header name");
            Assert.AreEqual(new long[] { 4 }, Ids(new RecordQuery { Text = "hit" }, records), "header value");
            Assert.AreEqual(new long[] { 5 }, Ids(new RecordQuery { Text = "failed" }, records), "state word");
            Assert.AreEqual(new long[] { 1, 2 }, Ids(new RecordQuery { Text = "game.com" }, records));
        }

        [Test]
        public void ErrorsOnly_KeepsFailuresAnd4xx5xx_DropsPending()
        {
            var records = new[]
            {
                R(1),
                R(2, status: 404),
                R(3, status: 500),
                R(4, state: HttpRecordState.Failed, status: 0),
                R(5, state: HttpRecordState.Aborted, status: 0),
                R(6, state: HttpRecordState.Pending, status: 0),
            };

            Assert.AreEqual(new long[] { 2, 3, 4, 5 }, Ids(new RecordQuery { ErrorsOnly = true }, records));
        }

        [Test]
        public void SourceChips_AreInclusive_AndBothBitsMatchEither()
        {
            var records = new[]
            {
                R(1, source: HttpCaptureSource.Woven),
                R(2, source: HttpCaptureSource.Manual),
                R(3, source: HttpCaptureSource.Woven | HttpCaptureSource.Manual),
            };

            Assert.AreEqual(new long[] { 1, 3 }, Ids(new RecordQuery { ShowManual = false }, records));
            Assert.AreEqual(new long[] { 2, 3 }, Ids(new RecordQuery { ShowAutomatic = false }, records));
            Assert.IsEmpty(Ids(new RecordQuery { ShowAutomatic = false, ShowManual = false }, records));
        }

        [Test]
        public void ClientChips_MethodAndHost_Filter()
        {
            var records = new[]
            {
                R(1, client: HttpClientKind.UnityWebRequest, url: "https://a.com/1"),
                R(2, client: HttpClientKind.HttpClient, method: "PUT", url: "https://b.com/2"),
                R(3, client: HttpClientKind.Custom, url: "custom://c/3"),
            };

            Assert.AreEqual(new long[] { 2, 3 }, Ids(new RecordQuery { ShowUnityWebRequest = false }, records));
            Assert.AreEqual(new long[] { 1, 3 }, Ids(new RecordQuery { ShowHttpClient = false }, records));
            Assert.AreEqual(new long[] { 1, 2 }, Ids(new RecordQuery { ShowCustom = false }, records));
            Assert.AreEqual(new long[] { 2 }, Ids(new RecordQuery { Method = "put" }, records));
            Assert.AreEqual(new long[] { 2 }, Ids(new RecordQuery { Host = "B.com" }, records));
        }

        [Test]
        public void ClearFilters_ResetsEverything_ButNotTheSort()
        {
            var query = new RecordQuery { Text = "x", ErrorsOnly = true, ShowManual = false, Method = "GET", Host = "h", SortBy = SortColumn.Time };

            query.ClearFilters();

            Assert.IsFalse(query.IsFiltering);
            Assert.AreEqual(SortColumn.Time, query.SortBy);
        }

        [Test]
        public void Sort_ByEachColumn_WithArrivalAsTiebreaker()
        {
            var records = new[]
            {
                R(1, method: "POST", url: "https://b.com/z", status: 500, duration: 30, size: 3, responseHeaders: new EditorHeader("Content-Type", "text/html")),
                R(2, method: "GET", url: "https://a.com/y", status: 200, duration: 10, size: 1, responseHeaders: new EditorHeader("Content-Type", "application/json")),
                R(3, method: "GET", url: "https://c.com/x", status: 404, duration: 20, size: 2, responseHeaders: new EditorHeader("Content-Type", "image/png")),
            };

            Assert.AreEqual(new long[] { 2, 3, 1 }, Ids(new RecordQuery { SortBy = SortColumn.Method }, records), "GET, GET (arrival), POST");
            Assert.AreEqual(new long[] { 1, 2, 3 }, Ids(new RecordQuery { SortBy = SortColumn.Method, SortDescending = true }, records), "POST, then GETs still in arrival order");
            Assert.AreEqual(new long[] { 2, 3, 1 }, Ids(new RecordQuery { SortBy = SortColumn.Status }, records));
            Assert.AreEqual(new long[] { 3, 2, 1 }, Ids(new RecordQuery { SortBy = SortColumn.Name }, records), "/x, /y, /z");
            Assert.AreEqual(new long[] { 2, 1, 3 }, Ids(new RecordQuery { SortBy = SortColumn.Host }, records));
            Assert.AreEqual(new long[] { 1, 2, 3 }, Ids(new RecordQuery { SortBy = SortColumn.Type }, records), "html, json, png");
            Assert.AreEqual(new long[] { 2, 3, 1 }, Ids(new RecordQuery { SortBy = SortColumn.Size }, records));
            Assert.AreEqual(new long[] { 1, 3, 2 }, Ids(new RecordQuery { SortBy = SortColumn.Time, SortDescending = true }, records));
            Assert.AreEqual(new long[] { 1, 2, 3 }, Ids(new RecordQuery { SortBy = SortColumn.Started }, records));
        }

        [Test]
        public void Sort_ByStatus_GroupsPendingFirst_ThenCodes_ThenFailures()
        {
            var records = new[]
            {
                R(1, state: HttpRecordState.Failed, status: 0),
                R(2, status: 500),
                R(3, state: HttpRecordState.Pending, status: 0),
                R(4, status: 200),
                R(5, state: HttpRecordState.Aborted, status: 0),
            };

            Assert.AreEqual(new long[] { 3, 4, 2, 5, 1 }, Ids(new RecordQuery { SortBy = SortColumn.Status }, records));
        }

        [Test]
        public void Sort_BySource_OrdersAutomaticManualBoth()
        {
            var records = new[]
            {
                R(1, source: HttpCaptureSource.Woven | HttpCaptureSource.Manual),
                R(2, source: HttpCaptureSource.Manual),
                R(3, source: HttpCaptureSource.Woven),
            };

            Assert.AreEqual(new long[] { 3, 2, 1 }, Ids(new RecordQuery { SortBy = SortColumn.Source }, records));
        }

        [Test]
        public void Describe_NamesTheActiveFilters()
        {
            Assert.AreEqual(string.Empty, new RecordQuery().Describe());
            Assert.AreEqual("\"pro\", errors, manual, POST, host a.com, UWR+custom",
                new RecordQuery { Text = "pro", ErrorsOnly = true, ShowAutomatic = false, Method = "POST", Host = "a.com", ShowHttpClient = false }.Describe());
        }

        [Test]
        public void SourceClass_MapsEachCombination()
        {
            Assert.AreEqual("hm-source-badge--automatic", RecordFormat.SourceClass(HttpCaptureSource.Woven));
            Assert.AreEqual("hm-source-badge--manual", RecordFormat.SourceClass(HttpCaptureSource.Manual));
            Assert.AreEqual("hm-source-badge--both", RecordFormat.SourceClass(HttpCaptureSource.Woven | HttpCaptureSource.Manual));
            Assert.AreEqual("hm-source-badge--unknown", RecordFormat.SourceClass(HttpCaptureSource.None));
        }
    }
}
