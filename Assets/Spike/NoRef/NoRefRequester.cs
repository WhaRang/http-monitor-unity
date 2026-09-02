using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Spike.NoRef
{
    /// <summary>
    /// M0 spike, lives in an asmdef that does NOT reference HttpMonitor.Runtime. Verifies that the
    /// assembly reference the weaver injects binds at runtime without the user declaring it.
    /// </summary>
    public class NoRefRequester : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            new GameObject("Spike.NoRef").AddComponent<NoRefRequester>();
        }

        private IEnumerator Start()
        {
            using (var request = UnityWebRequest.Get("https://example.com/?from=noref"))
            {
                yield return request.SendWebRequest();
                Debug.Log($"[Spike.NoRef] done: {request.responseCode} {request.result}");
            }
        }
    }
}
