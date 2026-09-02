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

        private static string HeaderValue(HttpRecord record, string name)
        {
            return record.ResponseHeaders.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        }

        [UnityTest]
        public IEnumerator DefaultConstructor_Get_IsRecordedWithBody()
        {
            const string url = "https://example.com/?from=httpclient-get";
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
            Assert.IsNotEmpty(HeaderValue(record, "Content-Type"));
        }

        [UnityTest]
        public IEnumerator HandlerConstructor_Post_RecordsRequestBodyAndHeaders()
        {
            const string url = "https://httpbin.org/post?from=httpclient-post";
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
            Assert.That(Encoding.UTF8.GetString(record.ResponseBody), Does.Contain("\"k\": \"v\""));
        }

        [UnityTest]
        public IEnumerator UnreachableHost_RecordsFailed_AndStillThrowsToTheCaller()
        {
            const string url = "https://nonexistent.invalid/?from=httpclient-failed";

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
            const string url = "https://httpbin.org/delay/10?from=httpclient-cancelled";

            var task = Task.Run(async () =>
            {
                using (var client = new HttpClient())
                using (var cts = new CancellationTokenSource(500))
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
            const string url = "https://httpbin.org/status/500?from=httpclient-500";

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
    }
}
