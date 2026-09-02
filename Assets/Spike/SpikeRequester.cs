using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Spike
{
    /// <summary>
    /// M0 spike, lives in Assembly-CSharp. Sends one request from a coroutine and one from an async
    /// method so both a plain call site and one inside a compiler-generated state machine get woven.
    /// Expected console output per request: "[HttpMonitor] -> GET ..." then "[HttpMonitor] <- 200 ...".
    /// Self-registers at startup so the scene needs no changes.
    /// </summary>
    public class SpikeRequester : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            new GameObject("Spike.AssemblyCSharp").AddComponent<SpikeRequester>();
        }

        private IEnumerator Start()
        {
            using (var request = UnityWebRequest.Get("https://httpbin.org/get?from=coroutine"))
            {
                yield return request.SendWebRequest();
                Debug.Log($"[Spike] coroutine done: {request.responseCode} {request.result}");
            }

            var task = SendAsync();
            while (!task.IsCompleted) yield return null;

#if !UNITY_EDITOR
            // Headless player runs (M0 build verification): give the other spike a moment, then exit.
            if (Application.isBatchMode)
            {
                yield return new WaitForSeconds(5f);
                Debug.Log("[Spike] quitting");
                Application.Quit();
            }
#endif
        }

        private static async Task SendAsync()
        {
            using (var request = UnityWebRequest.Get("https://httpbin.org/get?from=async"))
            {
                var operation = request.SendWebRequest();
                var completion = new TaskCompletionSource<bool>();
                operation.completed += _ => completion.TrySetResult(true);
                await completion.Task;
                Debug.Log($"[Spike] async done: {request.responseCode} {request.result}");
            }
        }
    }
}
