# Manual capture

Automatic capture covers `UnityWebRequest` and `HttpClient` calls in your own compiled scripts.
Use the manual API for everything else:

- a precompiled DLL the weaver never sees,
- an assembly you [excluded](settings.md) or marked `[assembly: HttpMonitor.DoNotWeave]`,
- a project with weaving turned off,
- a client the SDK does not know: Best HTTP, a socket library, a native plugin you wrap.

Everything is on the static class `HttpMonitorCapture`, in the `HttpMonitor` namespace. Nothing
in it throws: a failure is reported through the [logger](logger.md) and the call returns null.

Requests captured this way carry the blue **M** badge. A request that was also captured
automatically is never recorded twice; it gains the manual mark and shows as **A+M**.

## UnityWebRequest

Call `Track` after the request finished and before you dispose it:

```csharp
using (var request = UnityWebRequest.Get(url))
{
    yield return request.SendWebRequest();

    HttpMonitorCapture.Track(request);   // records the request and its response

    // ... use request.downloadHandler.text ...
}
```

`Track` works at any point in the request's life:

| When you call it | What happens |
|---|---|
| Before `SendWebRequest` | Remembered. The request is recorded when it is sent, with an accurate duration. Returns null. |
| While in flight | Recorded as pending. Call `Track` again once it is done to finish the record. |
| After it finished | Recorded complete in one call. The duration is unknown and shows as 0. |

For an accurate duration, call it before sending and again after:

```csharp
HttpMonitorCapture.Track(request);
yield return request.SendWebRequest();
HttpMonitorCapture.Track(request);
```

There is also a one-liner for the send site:

```csharp
yield return HttpMonitorCapture.Track(request.SendWebRequest());
```

Be careful with that form in a coroutine with a `using` block. Unity resumes the coroutine before
it raises the completion event, so the request is already disposed when the event arrives and the
response is lost. Calling `Track(request)` after the yield does not have that problem.

## HttpClient

Put the monitoring handler in front of the real one:

```csharp
var client = new HttpClient(HttpMonitorCapture.CreateHandler());
```

or with your own inner handler:

```csharp
var inner = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip };
var client = new HttpClient(HttpMonitorCapture.CreateHandler(inner));
```

or in one call:

```csharp
var client = HttpMonitorCapture.CreateClient();
```

Disposing the returned handler disposes the inner one, as `HttpClient` expects.

## Any other client

`Begin` opens a record and returns a handle. Finish it with exactly one of `Complete`, `Fail` or
`Abort`; later calls are ignored.

```csharp
var handle = HttpMonitorCapture.Begin("POST", url, requestHeaders, requestBodyBytes);

try
{
    var response = await myClient.SendAsync(...);

    handle?.Complete(response.StatusCode, response.Headers, response.BodyBytes);
}
catch (OperationCanceledException)
{
    handle?.Abort("cancelled");
    throw;
}
catch (Exception e)
{
    handle?.Fail(e.Message);
    throw;
}
```

`Begin` returns null while recording is paused, hence the `?.`.

Headers are a list of `HttpHeader`:

```csharp
var requestHeaders = new List<HttpHeader>
{
    new HttpHeader("Content-Type", "application/json"),
    new HttpHeader("Authorization", token),      // stored redacted, like any other
};
```

| Handle method | Use when |
|---|---|
| `Complete(statusCode, responseHeaders, responseBody, downloadedBytes, uploadedBytes)` | A response arrived, with any status. Everything after the status is optional. |
| `Fail(error)` | No response: DNS, connection, TLS. |
| `Abort(reason)` | Your code cancelled it. |

By default the record is labelled as a custom client. If the request really went through one of
the known clients, say so with the last argument:

```csharp
HttpMonitorCapture.Begin("GET", url, client: HttpClientKind.HttpClient);
```

Redaction and body caps apply to manually captured requests exactly as to automatic ones.
The handle is safe to finish from any thread.
