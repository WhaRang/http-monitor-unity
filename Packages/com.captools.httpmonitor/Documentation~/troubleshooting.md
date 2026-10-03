# Limits and troubleshooting

## Limits, stated plainly

- **Only C# traffic is seen.** Native SDKs make their own connections and do not appear.
- **Precompiled DLLs are not instrumented.** Capture them with the
  [manual API](manual-capture.md).
- **`UnityWebRequest` request headers are the ones your code set.** Unity adds `User-Agent`,
  `Accept-Encoding`, `Content-Length` and others natively, where the SDK cannot see them.
- **`UnityWebRequest` merges repeated response headers.** Two `Set-Cookie` lines arrive as one
  value. `HttpClient` keeps them separate.
- **Bodies come from two handlers only.** `UploadHandlerRaw` for uploads and
  `DownloadHandlerBuffer` for downloads. File, texture, audio and asset-bundle handlers are
  recorded by size, because their bytes are not available as a wire body.
- **Only the total duration is measured.** There are no DNS, connect or TLS phases.
- **Redaction covers headers**, not bodies and not query strings.
- **Device traffic is not shown in the Editor window yet.** See [Builds and devices](builds.md).
- **WebSockets, gRPC streams and raw sockets** are out of scope.

## Nothing appears in the window

Work down this list.

1. **Is Record on?** The dot in the toolbar is red while recording. The empty list says
   "Recording is paused" when it is off.
2. **Is a filter hiding everything?** The list says "No requests match" and names the filters.
   Click **Clear filters**.
3. **Is weaving enabled?** **Project Settings ▸ HTTP Monitor ▸ Weaving Enabled**. After turning
   it on, click **Recompile now**.
4. **Is the `HTTP_MONITOR_DISABLE` define set** for the current build target? It overrides the
   setting.
5. **Is the request made by a precompiled DLL**, an excluded assembly, or one marked
   `DoNotWeave`? Those need the [manual API](manual-capture.md).
6. **Does the code really use `UnityWebRequest` or `HttpClient`?** A third-party HTTP library
   with its own socket code is invisible to automatic capture.

## A request is listed as Incomplete

It finished, but the response could not be read. This happens when a `UnityWebRequest` is
disposed through a path the weaver cannot see, for example by a precompiled library that owns
the request, and the completion event arrives after the disposal. The request itself was
unaffected.

Calling `HttpMonitorCapture.Track(request)` before disposing fixes it.

## A request shows as Aborted, but it worked

`Aborted` means the request was disposed or cancelled before it finished. If your code disposes
a request while it is still in flight and does not care about the response, that is what you
will see, and it is accurate.

## The response body is missing

The body area says why. The common cases:

- The response went to a download handler other than `DownloadHandlerBuffer`.
- The body is larger than the cap. It is shown truncated, with the full size.
- **Capture Bodies** is off.
- An `HttpClient` response had no Content-Length while **Buffer Unknown Length Responses** is off.

## JSON does not pretty-print

- The server sent it as `text/plain`. The declared type wins, so it opens raw; click **Pretty**.
- It is not valid JSON. The viewer says so and shows it raw.
- It is larger than 1 MB. Formatting is skipped to keep the Editor responsive.

## Records disappeared

- **Preserve log** is off, and you pressed Play. That clears the list.
- The window keeps the newest 1000 requests and 16 MB of bodies, and drops the oldest past that.
  Both limits are in **Project Settings ▸ HTTP Monitor ▸ Editor window**.

## A weaving setting had no effect

Weaving settings apply when scripts compile. Click **Recompile now** on the settings page, or
change any script.

On a build machine, check that `ProjectSettings/HttpMonitorWeaver.cfg` is in version control.

## The build fails or behaves differently with the package installed

The weaver is written so that a failure inside it becomes a warning and the assembly is passed
through unchanged. If you suspect it anyway:

1. Add the `HTTP_MONITOR_DISABLE` scripting define and rebuild. That removes all rewriting.
2. If the problem is gone, re-enable and add the suspect assembly to **Excluded Assemblies** to
   narrow it down.
3. Report it with the assembly name and the console warning, if any.

## Streaming with HttpClient stopped streaming

To capture a response body without a Content-Length, the SDK has to read all of it first. Turn
off **Buffer Unknown Length Responses**. The exchange is still recorded, without the body.

## Messages in the console

HTTP Monitor is silent in normal operation. A message prefixed `[HttpMonitor]` means a capture
step failed and was skipped. The game's request was not affected. To route these messages
elsewhere, see [Custom logger](logger.md).
