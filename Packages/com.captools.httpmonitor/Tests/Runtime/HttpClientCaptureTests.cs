using System;
using System.Collections;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace HttpMonitor.Tests
{
    /// <summary>
    /// The <c>new HttpClient(...)</c> expressions below are woven into Interceptor.CreateHttpClient,
    /// which installs the monitoring handler. Nothing here references the handler directly.
    /// </summary>
    public class HttpClientCaptureTests
    {
        private static HttpRecord Find(string url)
        {
            return Array.Find(HttpMonitorSession.Current.Snapshot(), r => r.Url == url);
        }

        private static IEnumerator Await(Task task, float timeoutSeconds = 20f)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (!task.IsCompleted && DateTime.UtcNow < deadline)
                yield return null;

            Assert.IsTrue(task.IsCompleted, "timed out");
        }

        private static string ResponseHeader(HttpRecord record, string name)
        {
            return record.ResponseHeaders.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        }

        [UnityTest]
        public IEnumerator DefaultConstructor_Get_IsRecordedWithBody()
        {
            var url = TestServer.Shared.Url("echo?from=httpclient-get");
            string body = null;

            var task = Task.Run(async () =>
            {
                using (var client = new HttpClient())
                    body = await client.GetStringAsync(url);
            });

            yield return Await(task);
            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());

            var record = Find(url);

            Assert.NotNull(record, "no record");
            Assert.AreEqual(HttpClientKind.HttpClient, record.Client);
            Assert.AreEqual(HttpCaptureSource.Woven, record.Source);
            Assert.AreEqual("GET", record.Method);
            Assert.AreEqual(HttpRecordState.Completed, record.State);
            Assert.AreEqual(200, record.StatusCode);
            Assert.IsNull(record.Error);
            Assert.Greater(record.DurationMs, 0);
            Assert.AreEqual(body, Encoding.UTF8.GetString(record.ResponseBody));
            Assert.AreEqual(body.Length, record.DownloadedBytes);
            Assert.AreEqual("application/json", ResponseHeader(record, "Content-Type"));
        }

        [UnityTest]
        public IEnumerator HandlerConstructor_Post_RecordsRequestBodyAndHeaders()
        {
            var url = TestServer.Shared.Url("echo?from=httpclient-post");
            const string payload = "{\"k\":\"v\"}";

            var task = Task.Run(async () =>
            {
                using (var client = new HttpClient(new HttpClientHandler(), true))
                {
                    client.DefaultRequestHeaders.Add("X-Custom", "yes");
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer secret");

                    using (var response = await client.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json")))
                        response.EnsureSuccessStatusCode();
                }
            });

            yield return Await(task);
            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual("POST", record.Method);
            Assert.AreEqual(payload, Encoding.UTF8.GetString(record.RequestBody));
            Assert.AreEqual(payload.Length, record.UploadedBytes);

            var names = record.RequestHeaders.Select(h => h.Name).ToList();
            Assert.Contains("X-Custom", names);
            Assert.Contains("Authorization", names);
            Assert.Contains("Content-Type", names, "content headers are merged in");
            Assert.AreEqual("<redacted>", record.RequestHeaders.First(h => h.Name == "Authorization").Value);
            Assert.That(record.RequestHeaders.First(h => h.Name == "Content-Type").Value, Does.StartWith("application/json"));
            Assert.AreEqual(payload, Encoding.UTF8.GetString(record.ResponseBody));
            Assert.AreEqual("Bearer secret", ResponseHeader(record, "X-Echo-Authorization"), "the wire is untouched");
        }

        [UnityTest]
        public IEnumerator ConnectionRefused_RecordsFailed_AndStillThrowsToTheCaller()
        {
            var url = TestServer.UnreachableUrl + "?from=httpclient-failed";

            var task = Task.Run(async () =>
            {
                using (var client = new HttpClient())
                    await client.GetAsync(url);
            });

            yield return Await(task);
            Assert.IsTrue(task.IsFaulted, "the caller must still see the exception");

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual(HttpRecordState.Failed, record.State);
            Assert.AreEqual(0, record.StatusCode);
            Assert.IsNotEmpty(record.Error);
        }

        [UnityTest]
        public IEnumerator Cancelled_RecordsAborted()
        {
            var url = TestServer.Shared.Url("delay/5000?from=httpclient-cancelled");

            var task = Task.Run(async () =>
            {
                using (var client = new HttpClient())
                using (var cts = new CancellationTokenSource(300))
                    await client.GetAsync(url, cts.Token);
            });

            yield return Await(task);
            Assert.IsTrue(task.IsCanceled || task.IsFaulted, "the caller must still see the cancellation");

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual(HttpRecordState.Aborted, record.State);
            Assert.AreEqual("cancelled", record.Error);
        }

        [UnityTest]
        public IEnumerator HttpErrorStatus_IsCompleted()
        {
            var url = TestServer.Shared.Url("status/500?from=httpclient-500");

            var task = Task.Run(async () =>
            {
                using (var client = new HttpClient())
                using (var response = await client.GetAsync(url))
                    Assert.AreEqual(500, (int)response.StatusCode);
            });

            yield return Await(task);
            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual(HttpRecordState.Completed, record.State);
            Assert.AreEqual(500, record.StatusCode);
            Assert.IsNull(record.Error);
        }

        [UnityTest]
        public IEnumerator RepeatedResponseHeaders_AreKeptSeparately_AndRedacted()
        {
            var url = TestServer.Shared.Url("cookies?from=httpclient-cookies");

            var task = Task.Run(async () =>
            {
                using (var client = new HttpClient())
                    await client.GetStringAsync(url);
            });

            yield return Await(task);
            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());

            var record = Find(url);

            Assert.NotNull(record);
            var cookies = record.ResponseHeaders.Where(h => h.Name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.AreEqual(2, cookies.Count);
            Assert.IsTrue(cookies.All(h => h.Value == "<redacted>"));
        }

        [UnityTest]
        public IEnumerator ChunkedResponse_IsBufferedByDefault_AndSkippedWhenDisabled()
        {
            var options = HttpMonitorSession.Current.Options;
            var buffered = TestServer.Shared.Url("chunked?from=buffered");
            var skipped = TestServer.Shared.Url("chunked?from=skipped");
            string bufferedBody = null;
            string skippedBody = null;

            var first = Task.Run(async () =>
            {
                using (var client = new HttpClient())
                    bufferedBody = await client.GetStringAsync(buffered);
            });

            yield return Await(first);
            Assert.IsFalse(first.IsFaulted, first.Exception?.ToString());

            options.BufferUnknownLengthResponses = false;

            var second = Task.Run(async () =>
            {
                using (var client = new HttpClient())
                    skippedBody = await client.GetStringAsync(skipped);
            });

            yield return Await(second);
            options.BufferUnknownLengthResponses = true;
            Assert.IsFalse(second.IsFaulted, second.Exception?.ToString());

            Assert.AreEqual("chunk-one;chunk-two", bufferedBody);
            Assert.AreEqual("chunk-one;chunk-two", skippedBody, "the caller always gets the full body");

            var bufferedRecord = Find(buffered);
            Assert.NotNull(bufferedRecord);
            Assert.AreEqual("chunk-one;chunk-two", Encoding.UTF8.GetString(bufferedRecord.ResponseBody));

            var skippedRecord = Find(skipped);
            Assert.NotNull(skippedRecord);
            Assert.AreEqual(HttpRecordState.Completed, skippedRecord.State);
            Assert.IsNull(skippedRecord.ResponseBody);
        }
    }
}
