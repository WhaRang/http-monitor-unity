using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace Spike.Tests
{
    /// <summary>
    /// M0 exit criterion for Play Mode. This assembly does not reference HttpMonitor.Runtime, so a
    /// passing test proves both that the call site was woven and that the injected assembly
    /// reference binds at runtime.
    /// </summary>
    public class WeavingSpikeTests
    {
        private static Regex RequestLine(string url) => new Regex(@"^\[HttpMonitor\] -> GET " + Regex.Escape(url));
        private static Regex ResponseLine(string url) => new Regex(@"^\[HttpMonitor\] <- \d+ \w+ " + Regex.Escape(url));

        [UnityTest]
        public IEnumerator Coroutine_WithUsing_CapturesRequestAndResponse()
        {
            const string url = "https://example.com/?from=using";
            LogAssert.Expect(LogType.Log, RequestLine(url));
            LogAssert.Expect(LogType.Log, ResponseLine(url));

            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                Assert.AreEqual(UnityWebRequest.Result.Success, request.result, request.error);
            }
        }

        [UnityTest]
        public IEnumerator Coroutine_WithExplicitDispose_CapturesRequestAndResponse()
        {
            const string url = "https://example.com/?from=explicit-dispose";
            LogAssert.Expect(LogType.Log, RequestLine(url));
            LogAssert.Expect(LogType.Log, ResponseLine(url));

            var request = UnityWebRequest.Get(url);
            request.SendWebRequest();
            
            while (!request.isDone) 
                yield return null;
            
            request.Dispose();
        }

        [UnityTest]
        public IEnumerator Coroutine_NeverDisposed_CapturesViaCompletedEvent()
        {
            const string url = "https://example.com/?from=leaked";
            LogAssert.Expect(LogType.Log, RequestLine(url));
            LogAssert.Expect(LogType.Log, ResponseLine(url));

            var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();
            yield return null; // completed fires after the coroutine resumed; give it a frame
        }

        [UnityTest]
        public IEnumerator Async_WithUsing_CapturesRequestAndResponse()
        {
            const string url = "https://example.com/?from=async-test";
            LogAssert.Expect(LogType.Log, RequestLine(url));
            LogAssert.Expect(LogType.Log, ResponseLine(url));
            
            var task = SendAsync(url);
            
            while (!task.IsCompleted) 
                yield return null;
            
            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());
        }

        private static async System.Threading.Tasks.Task SendAsync(string url)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                var operation = request.SendWebRequest();
                var completion = new System.Threading.Tasks.TaskCompletionSource<bool>();
                operation.completed += _ => completion.TrySetResult(true);
                await completion.Task;
            }
        }
    }
}
