using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HttpMonitor.Editor;
using NUnit.Framework;

namespace HttpMonitor.Tests.Editor
{
    public class MiniJsonTests
    {
        [Test]
        public void Parse_NestedShapes_AndEscapes()
        {
            var root = MiniJson.AsObject(MiniJson.Parse("{\"a\":[1,2.5,-3e2,true,false,null,\"s\\n\\u00e9\\\"q\\\"\"],\"o\":{\"k\":\"v\"},\"e\":{},\"x\":[]}"));

            var a = MiniJson.GetArray(root, "a");
            Assert.AreEqual(new object[] { 1.0, 2.5, -300.0, true, false, null, "s\né\"q\"" }, a.ToArray());
            Assert.AreEqual("v", MiniJson.GetString(MiniJson.GetObject(root, "o"), "k"));
            Assert.IsEmpty(MiniJson.GetObject(root, "e"));
            Assert.IsEmpty(MiniJson.GetArray(root, "x"));
        }

        [Test]
        public void Parse_Invalid_Throws_WithOffset()
        {
            foreach (var bad in new[] { "", "{", "[1,]", "{\"a\":1}x", "{a:1}", "\"unterminated", "{\"a\":tru}", "[\"\\q\"]" })
            {
                var e = Assert.Throws<FormatException>(() => MiniJson.Parse(bad), "should reject: " + bad);
                Assert.That(e.Message, Does.Contain("offset"));
            }
        }

        [Test]
        public void WriteString_EscapesEverythingThatNeedsIt()
        {
            var sb = new StringBuilder();
            MiniJson.WriteString(sb, "a\"b\\c\nd\te\u0001é");

            Assert.AreEqual("\"a\\\"b\\\\c\\nd\\te\\u0001é\"", sb.ToString());
            Assert.AreEqual("a\"b\\c\nd\te\u0001é", MiniJson.Parse(sb.ToString()), "round trip");

            sb.Clear();
            MiniJson.WriteString(sb, null);
            Assert.AreEqual("null", sb.ToString());
        }

        [Test]
        public void Lookups_ReturnDefaults_InsteadOfThrowing()
        {
            var obj = MiniJson.AsObject(MiniJson.Parse("{\"n\":\"not a number\"}"));

            Assert.AreEqual(7, MiniJson.GetNumber(obj, "n", 7));
            Assert.IsNull(MiniJson.GetString(obj, "missing"));
            Assert.IsNull(MiniJson.GetObject(null, "x"));
            Assert.IsTrue(MiniJson.GetBool(null, "x", true));
        }
    }

    public class HarTests
    {
        private static EditorRecord Completed()
        {
            return new EditorRecord
            {
                Id = 12,
                Client = HttpClientKind.HttpClient,
                Source = HttpCaptureSource.Woven | HttpCaptureSource.Manual,
                Method = "POST",
                Url = "https://api.game.com/v1/score?run=3&name=a%20b",
                StartedAtUtcTicks = new DateTime(2026, 9, 9, 10, 30, 15, 250, DateTimeKind.Utc).Ticks,
                RequestHeaders = new[] { new EditorHeader("Content-Type", "application/json"), new EditorHeader("Authorization", "<redacted>") },
                RequestBody = Encoding.UTF8.GetBytes("{\"score\":42}"),
                UploadedBytes = 12,
                State = HttpRecordState.Completed,
                StatusCode = 201,
                DurationMs = 84.5,
                ResponseHeaders = new[] { new EditorHeader("Content-Type", "text/plain"), new EditorHeader("Location", "/v1/score/9") },
                ResponseBody = Encoding.UTF8.GetBytes("ok"),
                ResponseBodyTruncated = true,
                DownloadedBytes = 2000,
            };
        }

        private static EditorRecord Failed()
        {
            return new EditorRecord
            {
                Id = 13,
                Client = HttpClientKind.UnityWebRequest,
                Source = HttpCaptureSource.Woven,
                Method = "GET",
                Url = "http://127.0.0.1:1/",
                StartedAtUtcTicks = DateTime.UtcNow.Ticks,
                RequestHeaders = new EditorHeader[0],
                ResponseHeaders = new EditorHeader[0],
                State = HttpRecordState.Failed,
                Error = "Cannot connect to destination host",
                DurationMs = 3,
            };
        }

        private static Dictionary<string, object> FirstEntry(string har)
        {
            var log = MiniJson.GetObject(MiniJson.AsObject(MiniJson.Parse(har)), "log");
            Assert.AreEqual("1.2", MiniJson.GetString(log, "version"));
            Assert.AreEqual(HarWriter.CreatorName, MiniJson.GetString(MiniJson.GetObject(log, "creator"), "name"));

            return MiniJson.AsObject(MiniJson.GetArray(log, "entries")[0]);
        }

        [Test]
        public void Write_ProducesHar12_WithStandardFields()
        {
            var entry = FirstEntry(HarWriter.Write(new[] { Completed() }, "1.2.3"));

            Assert.AreEqual("2026-09-09T10:30:15.250Z", MiniJson.GetString(entry, "startedDateTime"));
            Assert.AreEqual(84.5, MiniJson.GetNumber(entry, "time"));

            var request = MiniJson.GetObject(entry, "request");
            Assert.AreEqual("POST", MiniJson.GetString(request, "method"));
            Assert.AreEqual("https://api.game.com/v1/score?run=3&name=a%20b", MiniJson.GetString(request, "url"));
            Assert.AreEqual(2, MiniJson.GetArray(request, "headers").Count);
            Assert.AreEqual(12, MiniJson.GetNumber(request, "bodySize"));

            var query = MiniJson.GetArray(request, "queryString").Select(MiniJson.AsObject).ToList();
            Assert.AreEqual("run", MiniJson.GetString(query[0], "name"));
            Assert.AreEqual("3", MiniJson.GetString(query[0], "value"));
            Assert.AreEqual("a b", MiniJson.GetString(query[1], "value"), "query values are unescaped");

            var postData = MiniJson.GetObject(request, "postData");
            Assert.AreEqual("application/json", MiniJson.GetString(postData, "mimeType"));
            Assert.AreEqual("{\"score\":42}", MiniJson.GetString(postData, "text"));

            var response = MiniJson.GetObject(entry, "response");
            Assert.AreEqual(201, MiniJson.GetNumber(response, "status"));
            Assert.AreEqual("Created", MiniJson.GetString(response, "statusText"));
            Assert.AreEqual("/v1/score/9", MiniJson.GetString(response, "redirectURL"));

            var content = MiniJson.GetObject(response, "content");
            Assert.AreEqual(2000, MiniJson.GetNumber(content, "size"), "size is the wire size, not the stored prefix");
            Assert.AreEqual("text/plain", MiniJson.GetString(content, "mimeType"));
            Assert.AreEqual("ok", MiniJson.GetString(content, "text"));
            Assert.IsNull(MiniJson.GetString(content, "encoding"), "text bodies carry no encoding flag");

            var timings = MiniJson.GetObject(entry, "timings");
            Assert.AreEqual(84.5, MiniJson.GetNumber(timings, "wait"));
            Assert.AreEqual(0, MiniJson.GetNumber(timings, "send"));

            var extension = MiniJson.GetObject(entry, "_httpMonitor");
            Assert.AreEqual("HttpClient", MiniJson.GetString(extension, "client"));
            Assert.AreEqual("Woven, Manual", MiniJson.GetString(extension, "source"));
            Assert.IsTrue(MiniJson.GetBool(extension, "responseBodyTruncated"));
        }

        [Test]
        public void Write_FailedRecord_HasStatusZero_AndTheErrorAsStatusText()
        {
            var entry = FirstEntry(HarWriter.Write(new[] { Failed() }, null));
            var response = MiniJson.GetObject(entry, "response");

            Assert.AreEqual(0, MiniJson.GetNumber(response, "status"));
            Assert.AreEqual("Cannot connect to destination host", MiniJson.GetString(response, "statusText"));
            Assert.IsNull(MiniJson.GetString(MiniJson.GetObject(response, "content"), "text"));
            Assert.AreEqual("Failed", MiniJson.GetString(MiniJson.GetObject(entry, "_httpMonitor"), "state"));
        }

        [Test]
        public void Write_BinaryBodies_AreBase64()
        {
            var record = Completed();
            record.RequestBody = new byte[] { 0, 1, 2, 255 };
            record.ResponseBody = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

            var entry = FirstEntry(HarWriter.Write(new[] { record }, null));
            var postData = MiniJson.GetObject(MiniJson.GetObject(entry, "request"), "postData");
            var content = MiniJson.GetObject(MiniJson.GetObject(entry, "response"), "content");

            Assert.AreEqual(Convert.ToBase64String(record.RequestBody), MiniJson.GetString(postData, "text"));
            Assert.AreEqual("base64", MiniJson.GetString(postData, "_encoding"));
            Assert.AreEqual(Convert.ToBase64String(record.ResponseBody), MiniJson.GetString(content, "text"));
            Assert.AreEqual("base64", MiniJson.GetString(content, "encoding"));
        }

        [Test]
        public void Write_PendingRecord_UsesMinusOneForTime()
        {
            var record = Completed();
            record.State = HttpRecordState.Pending;
            record.StatusCode = 0;

            var entry = FirstEntry(HarWriter.Write(new[] { record }, null));

            Assert.AreEqual(-1, MiniJson.GetNumber(entry, "time"));
            Assert.AreEqual(0, MiniJson.GetNumber(MiniJson.GetObject(entry, "timings"), "wait"), "timings never go negative for a real phase");
        }

        [Test]
        public void RoundTrip_ThroughTheReader_PreservesEverything()
        {
            var original = new[] { Completed(), Failed() };
            var restored = HarReader.Read(HarWriter.Write(original, "1.0.0"));

            Assert.AreEqual(2, restored.Count);

            for (var i = 0; i < 2; i++)
            {
                var a = original[i];
                var b = restored[i];

                Assert.IsTrue(b.Imported);
                Assert.AreEqual(a.Id, b.RuntimeId, "the exported id comes back as the runtime id");
                Assert.AreEqual(a.Client, b.Client);
                Assert.AreEqual(a.Source, b.Source);
                Assert.AreEqual(a.Method, b.Method);
                Assert.AreEqual(a.Url, b.Url);
                Assert.AreEqual(a.StartedAtUtcTicks / TimeSpan.TicksPerMillisecond, b.StartedAtUtcTicks / TimeSpan.TicksPerMillisecond, "millisecond precision");
                Assert.AreEqual(a.RequestHeaders.Select(h => h.ToString()), b.RequestHeaders.Select(h => h.ToString()));
                Assert.AreEqual(a.RequestBody, b.RequestBody);
                Assert.AreEqual(a.UploadedBytes, b.UploadedBytes);
                Assert.AreEqual(a.State, b.State);
                Assert.AreEqual(a.StatusCode, b.StatusCode);
                Assert.AreEqual(a.Error, b.Error);
                Assert.AreEqual(a.DurationMs, b.DurationMs);
                Assert.AreEqual(a.ResponseHeaders.Select(h => h.ToString()), b.ResponseHeaders.Select(h => h.ToString()));
                Assert.AreEqual(a.ResponseBody, b.ResponseBody);
                Assert.AreEqual(a.ResponseBodyTruncated, b.ResponseBodyTruncated);
                Assert.AreEqual(a.DownloadedBytes, b.DownloadedBytes);
            }
        }

        [Test]
        public void Read_HarFromAnotherTool_MapsOntoTheClosestState()
        {
            const string chromeStyle = @"{""log"":{""version"":""1.2"",""creator"":{""name"":""WebInspector"",""version"":""537.36""},""entries"":[
                {""startedDateTime"":""2026-09-09T12:00:00.000+02:00"",""time"":12.5,
                 ""request"":{""method"":""GET"",""url"":""https://example.com/a"",""headers"":[{""name"":""Accept"",""value"":""*/*""}],""bodySize"":0},
                 ""response"":{""status"":200,""statusText"":""OK"",""headers"":[{""name"":""Content-Type"",""value"":""text/html""}],
                               ""content"":{""size"":5,""mimeType"":""text/html"",""text"":""<a/>""},""bodySize"":5}},
                {""startedDateTime"":""2026-09-09T12:00:01.000Z"",""time"":-1,
                 ""request"":{""method"":""GET"",""url"":""https://down.example.com/"",""headers"":[],""bodySize"":-1},
                 ""response"":{""status"":0,""statusText"":"""",""_error"":""net::ERR_CONNECTION_REFUSED"",""headers"":[],""content"":{""size"":0},""bodySize"":-1}}
            ]}}";

            var records = HarReader.Read(chromeStyle);

            Assert.AreEqual(2, records.Count);

            var ok = records[0];
            Assert.AreEqual(HttpClientKind.Custom, ok.Client);
            Assert.AreEqual(HttpCaptureSource.None, ok.Source);
            Assert.AreEqual(HttpRecordState.Completed, ok.State);
            Assert.AreEqual(200, ok.StatusCode);
            Assert.AreEqual(new DateTime(2026, 9, 9, 10, 0, 0, DateTimeKind.Utc).Ticks, ok.StartedAtUtcTicks, "offsets are normalised to UTC");
            Assert.AreEqual("<a/>", Encoding.UTF8.GetString(ok.ResponseBody));
            Assert.AreEqual(5, ok.DownloadedBytes);
            Assert.AreEqual(12.5, ok.DurationMs);
            Assert.IsTrue(ok.Imported);

            var failed = records[1];
            Assert.AreEqual(HttpRecordState.Failed, failed.State);
            Assert.AreEqual("net::ERR_CONNECTION_REFUSED", failed.Error);
            Assert.AreEqual(0, failed.DurationMs);
            Assert.AreEqual(0, failed.UploadedBytes, "-1 sizes become 0");
            Assert.IsNull(failed.ResponseBody);
        }

        [Test]
        public void Read_NotAHar_Throws()
        {
            Assert.Throws<FormatException>(() => HarReader.Read("not json"));
            Assert.Throws<FormatException>(() => HarReader.Read("{\"log\":{}}"));
            Assert.Throws<FormatException>(() => HarReader.Read("[]"));
        }

        [Test]
        public void QueryPairs_HandlesEdgeCases()
        {
            Assert.IsEmpty(HarWriter.QueryPairs("https://a.com/x"));
            Assert.IsEmpty(HarWriter.QueryPairs("https://a.com/x?"));
            Assert.AreEqual(new[] { "flag=", "k=v w", "x=1" }, HarWriter.QueryPairs("https://a.com/x?flag&k=v+w&&x=1#frag").Select(p => p.Key + "=" + p.Value).ToArray());
        }

        [Test]
        public void Buffer_AddImported_AssignsIds_AndFlagsThem()
        {
            var buffer = new EditorRecordBuffer();
            var seen = new List<long>();
            buffer.RecordAdded += r => seen.Add(r.Id);
            var changed = 0;
            buffer.Changed += () => changed++;

            buffer.AddImported(new[] { Completed(), Failed() });

            Assert.AreEqual(new long[] { 1, 2 }, seen.ToArray());
            Assert.AreEqual(1, changed, "one Changed for the whole import");
            Assert.IsTrue(buffer.Records.All(r => r.Imported && r.Runtime == null));
        }

        [Test]
        public void Query_ImportedRecordsWithoutASource_IgnoreTheSourceChips()
        {
            var imported = new EditorRecord { Id = 1, Imported = true, Source = HttpCaptureSource.None, Method = "GET", Url = "http://a/", State = HttpRecordState.Completed, StatusCode = 200 };
            var query = new RecordQuery { ShowAutomatic = false, ShowManual = false };

            Assert.IsTrue(query.Matches(imported));
            Assert.AreEqual("HAR", RecordFormat.SourceBadge(imported));
            Assert.AreEqual("hm-source-badge--imported", RecordFormat.SourceClass(imported));
            Assert.AreEqual("no capture source, imported from HAR", RecordFormat.SourceText(imported));
        }
    }
}
