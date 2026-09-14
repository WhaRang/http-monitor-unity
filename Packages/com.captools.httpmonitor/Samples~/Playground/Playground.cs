using System;
using System.Collections;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace HttpMonitor.Samples
{
    /// <summary>
    /// Drop this on any GameObject, press Play, open Window ▸ Analysis ▸ HTTP Monitor. The buttons
    /// exercise every capture path against the local echo server the sample starts itself, so the
    /// window can be explored offline. Nothing here references HttpMonitor except the manual
    /// capture examples, which is the point: the other requests are captured by weaving alone.
    /// </summary>
    public sealed class Playground : MonoBehaviour
    {
        private PlaygroundServer _server;
        private string _baseUrl;
        private string _log = "Press a button. Requests appear in Window ▸ Analysis ▸ HTTP Monitor.";
        private Vector2 _scroll;

        private void OnEnable()
        {
            _server = new PlaygroundServer();
            _baseUrl = _server.Start();
        }

        private void OnDisable()
        {
            _server?.Dispose();
            _server = null;
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, Mathf.Min(Screen.width - 32, 520), Screen.height - 32));
            GUILayout.Label("<b>HTTP Monitor playground</b>  ·  local server at " + _baseUrl, new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Space(6);

            GUILayout.Label("UnityWebRequest (captured automatically)");
            Row(
                ("GET JSON", () => StartCoroutine(Get("echo?from=json", "application/json"))),
                ("POST JSON", () => StartCoroutine(PostJson())),
                ("GET HTML", () => StartCoroutine(Get("html", null))),
                ("GET image", () => StartCoroutine(Get("image", null))));
            Row(
                ("404", () => StartCoroutine(Get("status/404", null))),
                ("500", () => StartCoroutine(Get("status/500", null))),
                ("Slow (2 s)", () => StartCoroutine(Get("delay/2000", null))),
                ("Connection refused", () => StartCoroutine(GetUrl("http://127.0.0.1:1/?from=refused"))));
            Row(
                ("Large body (3 MB)", () => StartCoroutine(Get("bytes/3000000", null))),
                ("With auth header", () => StartCoroutine(GetWithHeaders())),
                ("Abort mid-flight", () => StartCoroutine(AbortMidFlight())),
                ("Burst of 20", () => StartCoroutine(Burst(20))));

            GUILayout.Space(8);
            GUILayout.Label("HttpClient (captured automatically)");
            Row(
                ("GET", () => Fire(HttpClientGet("echo?from=httpclient"))),
                ("POST JSON", () => Fire(HttpClientPost())),
                ("Cancel after 300 ms", () => Fire(HttpClientCancel())),
                ("Chunked", () => Fire(HttpClientGet("chunked"))));

            GUILayout.Space(8);
            GUILayout.Label("Manual capture API");
            Row(
                ("Track after yield", () => StartCoroutine(ManualTrack())),
                ("Custom client via Begin", ManualBegin),
                ("Pause recording", () => HttpMonitorSession.Current.IsRecording = false),
                ("Resume recording", () => HttpMonitorSession.Current.IsRecording = true));

            GUILayout.Space(10);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(140));
            GUILayout.Label(_log);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void Row(params (string label, Action action)[] buttons)
        {
            GUILayout.BeginHorizontal();

            foreach (var (label, action) in buttons)
            {
                if (GUILayout.Button(label, GUILayout.Height(28)))
                    action();
            }

            GUILayout.EndHorizontal();
        }

        private void Log(string line)
        {
            _log = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + "\n" + _log;
        }

        private static void Fire(Task task)
        {
            task.ContinueWith(t => Debug.LogWarning("[Playground] " + t.Exception?.InnerException?.Message), TaskContinuationOptions.OnlyOnFaulted);
        }

        // ---------------------------------------------------------------- UnityWebRequest

        private IEnumerator Get(string path, string accept)
        {
            yield return GetUrl(_baseUrl + path, accept);
        }

        private IEnumerator GetUrl(string url, string accept = null)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                if (accept != null)
                    request.SetRequestHeader("Accept", accept);

                yield return request.SendWebRequest();
                Log($"UWR GET {url}: {request.responseCode} {request.result}");
            }
        }

        private IEnumerator PostJson()
        {
            var body = "{\"player\":\"p1\",\"score\":" + UnityEngine.Random.Range(0, 1000) + ",\"tags\":[\"a\",\"b\"],\"nested\":{\"ok\":true}}";

            using (var request = UnityWebRequest.Post(_baseUrl + "echo?from=post", body, "application/json"))
            {
                request.SetRequestHeader("X-Playground", "1");
                yield return request.SendWebRequest();
                Log($"UWR POST: {request.responseCode}");
            }
        }

        private IEnumerator GetWithHeaders()
        {
            using (var request = UnityWebRequest.Get(_baseUrl + "echo?from=headers"))
            {
                request.SetRequestHeader("Authorization", "Bearer this-is-secret");
                request.SetRequestHeader("Cookie", "session=abc");
                request.SetRequestHeader("X-Trace", "trace-123");
                yield return request.SendWebRequest();
                Log("UWR with auth header: check the detail, the secret is redacted");
            }
        }

        private IEnumerator AbortMidFlight()
        {
            var request = UnityWebRequest.Get(_baseUrl + "delay/5000");
            request.SendWebRequest();
            yield return new WaitForSeconds(0.3f);
            request.Dispose();
            Log("UWR aborted after 300 ms");
        }

        private IEnumerator Burst(int count)
        {
            for (var i = 0; i < count; i++)
                StartCoroutine(Get($"delay/{UnityEngine.Random.Range(50, 800)}?n={i}", null));

            Log($"UWR burst of {count} started; open the timeline");
            yield break;
        }

        // ---------------------------------------------------------------- HttpClient

        private async Task HttpClientGet(string path)
        {
            using (var client = new HttpClient())
            {
                var text = await client.GetStringAsync(_baseUrl + path);
                Log($"HttpClient GET {path}: {text.Length} chars");
            }
        }

        private async Task HttpClientPost()
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer also-secret");

                using (var response = await client.PostAsync(_baseUrl + "echo?from=httpclient-post", new StringContent("{\"hello\":\"httpclient\"}", Encoding.UTF8, "application/json")))
                    Log($"HttpClient POST: {(int)response.StatusCode}");
            }
        }

        private async Task HttpClientCancel()
        {
            using (var client = new HttpClient())
            using (var cts = new CancellationTokenSource(300))
            {
                try
                {
                    await client.GetAsync(_baseUrl + "delay/5000", cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Log("HttpClient cancelled after 300 ms");
                }
            }
        }

        // ---------------------------------------------------------------- manual

        private IEnumerator ManualTrack()
        {
            using (var request = UnityWebRequest.Get(_baseUrl + "echo?from=manual"))
            {
                yield return request.SendWebRequest();
                var record = HttpMonitorCapture.Track(request);
                Log($"manual Track → record #{record?.Id}, source {record?.Source} (A+M: woven and tracked)");
            }
        }

        private void ManualBegin()
        {
            var handle = HttpMonitorCapture.Begin("PUT", "custom://inventory/items/42", null, Encoding.UTF8.GetBytes("{\"qty\":3}"));
            handle?.Complete(200, null, Encoding.UTF8.GetBytes("{\"ok\":true}"));
            Log("manual Begin/Complete for a client the SDK cannot see");
        }
    }
}
