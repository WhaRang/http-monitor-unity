using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace Spike.Tests
{
    /// <summary>
    /// This assembly deliberately does not reference HttpMonitor.Runtime. Its call sites are still
    /// woven, so simply running them proves the injected assembly reference binds at runtime: a
    /// binding failure would surface as a TypeLoadException here. What gets recorded is asserted
    /// in the package's own tests (HttpMonitor.Tests), which do reference the runtime.
    /// </summary>
    public class WeavingSpikeTests
    {
        [UnityTest]
        public IEnumerator WovenCallSites_BindWithoutAReferenceToTheRuntime()
        {
            using (var request = UnityWebRequest.Get("https://example.com/?from=noref-using"))
            {
                yield return request.SendWebRequest();
                Assert.AreEqual(UnityWebRequest.Result.Success, request.result, request.error);
            }

            var explicitDispose = UnityWebRequest.Get("https://example.com/?from=noref-dispose");
            yield return explicitDispose.SendWebRequest();
            explicitDispose.Dispose();

            var task = SendAsync("https://example.com/?from=noref-async");

            while (!task.IsCompleted)
                yield return null;

            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());
        }

        private static async Task SendAsync(string url)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                var operation = request.SendWebRequest();
                var completion = new TaskCompletionSource<bool>();
                operation.completed += _ => completion.TrySetResult(true);
                await completion.Task;
            }
        }
    }
}
