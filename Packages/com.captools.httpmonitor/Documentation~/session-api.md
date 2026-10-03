# Reading records from code

The window is one consumer of the captured data. Your code can be another: an in-game debug
overlay, an automated test that asserts on traffic, a custom report.

Everything lives on `HttpMonitorSession.Current`, in the `HttpMonitor` namespace.

## The session

```csharp
var session = HttpMonitorSession.Current;

session.IsRecording = false;            // pause: requests pass through untouched
HttpRecord[] all = session.Snapshot();  // a copy, oldest first
session.Clear();
```

| Member | Meaning |
|---|---|
| `IsRecording` | Capture on or off. |
| `Snapshot()` | A copy of the current records, oldest first. |
| `Clear()` | Remove all records. |
| `Count`, `Capacity` | The session keeps the newest 1000 records. |
| `StoredBodyBytes` | Bytes of bodies currently held. |
| `Options` | Body caps and the redaction list. See [Settings](settings.md). |
| `RecordAdded` | Raised when a request is sent. The record is pending. |
| `RecordUpdated` | Raised once per request, when it has finished. |
| `Cleared` | Raised by `Clear()`. |

## Events

```csharp
private void OnEnable()
{
    HttpMonitorSession.Current.RecordUpdated += OnFinished;
}

private void OnDisable()
{
    HttpMonitorSession.Current.RecordUpdated -= OnFinished;
}

private void OnFinished(HttpRecord record)
{
    if (record.State == HttpRecordState.Failed)
        Debug.LogWarning($"{record.Method} {record.Url} failed: {record.Error}");
}
```

**Events arrive on the thread that made the request.** For `UnityWebRequest` that is the main
thread. For `HttpClient` it is a thread-pool thread, so do not touch Unity objects from the
handler; queue the work to the main thread instead.

A handler that throws is caught and reported through the [logger](logger.md). It can never break
the game's request.

## A record

`HttpRecord` is read-only. The request part is fixed when the record is created; the response
part is written once, before `RecordUpdated` is raised.

| Property | Meaning |
|---|---|
| `Id` | Increasing number within the session. |
| `Client` | `UnityWebRequest`, `HttpClient` or `Custom`. |
| `Source` | `Woven`, `Manual`, or both. A flags value. |
| `Method`, `Url` | As sent. |
| `StartedAtUtc` | When it was sent. |
| `RequestHeaders`, `ResponseHeaders` | Lists of `HttpHeader` (`Name`, `Value`). Redacted values hold the placeholder. |
| `RequestBody`, `ResponseBody` | Bytes up to the cap, or null when not captured. |
| `RequestBodyTruncated`, `ResponseBodyTruncated` | The body was longer than the cap. |
| `State` | See below. |
| `StatusCode` | HTTP status, or 0 when there was no response. |
| `Error` | Transport error text, when failed, aborted or incomplete. |
| `DurationMs` | Send to finish. |
| `UploadedBytes`, `DownloadedBytes` | Full transfer sizes, independent of what was stored. |
| `IsFinished` | True in every state except pending. |

## States

| State | Meaning |
|---|---|
| `Pending` | Sent, no outcome yet. |
| `Completed` | A response arrived. **Any status code**, 404 and 500 included. |
| `Failed` | No response: DNS, connection, TLS, or a data-processing error. `Error` says which. |
| `Aborted` | Your code disposed or cancelled the request before it finished. |
| `Incomplete` | It finished, but the response could not be read. See [Troubleshooting](troubleshooting.md). |

`Completed` means "the server answered", not "it went well". To find requests that went wrong,
check for `Failed`, `Aborted`, `Incomplete`, or a status of 400 and above.

## Example: assert on traffic in a test

```csharp
[UnityTest]
public IEnumerator Login_SendsExactlyOneRequest()
{
    HttpMonitorSession.Current.Clear();

    yield return loginScreen.Submit("user", "pass");

    var requests = HttpMonitorSession.Current.Snapshot();

    Assert.AreEqual(1, requests.Length);
    Assert.AreEqual("POST", requests[0].Method);
    Assert.That(requests[0].Url, Does.EndWith("/v1/login"));
    Assert.AreEqual(200, requests[0].StatusCode);
}
```
