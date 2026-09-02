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

        private static string EchoedHeader(HttpRecord record, string name)
        {
            return record.ResponseHeaders.FirstOrDefault(h => h.Name.Equals("X-Echo-" + name, StringComparison.OrdinalIgnoreCase)).Value;
        }

        [UnityTest]
        public IEnumerator HeadersSetBeforeSend_AreRecorded_InOrder_AndActuallySent()
        {
            var url = TestServer.Shared.Url("echo?from=headers");

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
            Assert.AreEqual("1", HeaderValue(record, "x-first"));
            Assert.AreEqual(HttpMonitorOptions.DefaultRedactedValue, HeaderValue(record, "authorization"), "Authorization must be redacted at record time");
            Assert.AreEqual(HttpCaptureSource.Woven, record.Source);

            Assert.AreEqual("1", EchoedHeader(record, "X-First"), "the woven call must still set the real header");
            Assert.AreEqual("Bearer secret", EchoedHeader(record, "Authorization"), "redaction is on our copy only, the wire is untouched");
        }

        [UnityTest]
        public IEnumerator SettingTheSameHeaderTwice_KeepsOneEntry_WithTheLastValue()
        {
            var url = TestServer.Shared.Url("echo?from=header-override");

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
            var url = TestServer.Shared.Url("echo?from=implicit-content-type");

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
            Assert.AreEqual("application/json", EchoedHeader(record, "Content-Type"));
        }

        [UnityTest]
        public IEnumerator ExplicitContentType_WinsOverUploadHandler()
        {
            var url = TestServer.Shared.Url("echo?from=explicit-content-type");

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
            var url = TestServer.Shared.Url("echo?from=no-headers");

            using (var request = UnityWebRequest.Get(url))
                yield return request.SendWebRequest();

            var record = Find(url);

            Assert.NotNull(record);
            Assert.IsEmpty(record.RequestHeaders);
        }

        [UnityTest]
        public IEnumerator InvalidHeader_StillThrows_AndIsNotRecorded()
        {
            var url = TestServer.Shared.Url("echo?from=invalid-header");

            using (var request = UnityWebRequest.Get(url))
            {
                // Unity 6 accepts most names it used to reject (Content-Length included); an empty name still throws.
                Assert.Throws<ArgumentException>(() => request.SetRequestHeader(string.Empty, "5"));
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
