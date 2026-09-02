using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace HttpMonitor.Tests
{
    /// <summary>Request headers reach the record through the woven SetRequestHeader call site.</summary>
    public class RequestHeaderCaptureTests
    {
        private static HttpRecord Find(string url)
        {
            return Array.Find(HttpMonitorSession.Current.Snapshot(), r => r.Url == url);
        }

        private static string HeaderValue(HttpRecord record, string name)
        {
            return record.RequestHeaders.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        }

        [UnityTest]
        public IEnumerator HeadersSetBeforeSend_AreRecorded_InOrder()
        {
            const string url = "https://example.com/?from=headers";

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("X-First", "1");
                request.SetRequestHeader("Authorization", "Bearer secret");
                request.SetRequestHeader("X-Last", "3");

                yield return request.SendWebRequest();
            }

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual(new[] { "X-First", "Authorization", "X-Last" }, record.RequestHeaders.Select(h => h.Name).ToArray());
            Assert.AreEqual("Bearer secret", HeaderValue(record, "authorization")); // redaction arrives in step 4
            Assert.AreEqual(HttpCaptureSource.Woven, record.Source);
        }

        [UnityTest]
        public IEnumerator SettingTheSameHeaderTwice_KeepsOneEntry_WithTheLastValue()
        {
            const string url = "https://example.com/?from=header-override";

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("X-Token", "old");
                request.SetRequestHeader("x-token", "new");

                yield return request.SendWebRequest();
            }

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual(1, record.RequestHeaders.Count);
            Assert.AreEqual("new", HeaderValue(record, "X-Token"));
        }

        [UnityTest]
        public IEnumerator ContentTypeFromUploadHandler_IsRecorded_WhenNotSetExplicitly()
        {
            const string url = "https://httpbin.org/post?from=implicit-content-type";

            using (var request = UnityWebRequest.Post(url, "{\"a\":1}", "application/json"))
            {
                request.SetRequestHeader("X-Custom", "yes");

                yield return request.SendWebRequest();
            }

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual("POST", record.Method);
            Assert.AreEqual("application/json", HeaderValue(record, "Content-Type"));
            Assert.AreEqual("yes", HeaderValue(record, "X-Custom"));
            Assert.AreEqual(2, record.RequestHeaders.Count);
        }

        [UnityTest]
        public IEnumerator ExplicitContentType_WinsOverUploadHandler()
        {
            const string url = "https://httpbin.org/post?from=explicit-content-type";

            using (var request = UnityWebRequest.Post(url, "<a/>", "application/json"))
            {
                request.SetRequestHeader("Content-Type", "application/xml");

                yield return request.SendWebRequest();
            }

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual(1, record.RequestHeaders.Count);
            Assert.AreEqual("application/xml", HeaderValue(record, "Content-Type"));
        }

        [UnityTest]
        public IEnumerator RequestWithoutHeaders_HasEmptyList()
        {
            const string url = "https://example.com/?from=no-headers";

            using (var request = UnityWebRequest.Get(url))
                yield return request.SendWebRequest();

            var record = Find(url);

            Assert.NotNull(record);
            Assert.IsEmpty(record.RequestHeaders);
        }

        [UnityTest]
        public IEnumerator InvalidHeader_StillThrows_AndIsNotRecorded()
        {
            const string url = "https://example.com/?from=invalid-header";

            using (var request = UnityWebRequest.Get(url))
            {
                Assert.Throws<InvalidOperationException>(() => request.SetRequestHeader("Content-Length", "5"));
                request.SetRequestHeader("X-Ok", "1");

                yield return request.SendWebRequest();
            }

            var record = Find(url);

            Assert.NotNull(record);
            Assert.AreEqual(1, record.RequestHeaders.Count);
            Assert.AreEqual("X-Ok", record.RequestHeaders[0].Name);
        }
    }
}
