# HTTP Monitor for Unity

Chrome DevTools' Network tab inside the Unity Editor. Every HTTP request and response your game
makes, live, filterable, exportable, replayable. No code changes: call sites are rewritten at
compile time, so `UnityWebRequest` and `HttpClient` traffic shows up on its own.

## Install

Add the package through the Package Manager (git URL, or a local path while developing). It
depends on `com.unity.nuget.mono-cecil`, which the Package Manager resolves.

Open **Window ▸ Analysis ▸ HTTP Monitor** (Ctrl+Shift+H), press Play. Requests appear.

## What you get

- **Automatic capture** of `UnityWebRequest` and `System.Net.Http.HttpClient` in the Editor and in
  development builds. Release builds are not woven unless you opt in under Project Settings.
- **The window**: a request table with status colours, filter and search, sortable columns, a
  timeline strip, a detail pane with request and response blocks side by side, pretty-printed
  JSON and HTML, hex and image views, copy as cURL, HAR export and import, pop-out and pinned
  detail windows.
- **Replay**: resend any captured request from the Editor with one click, or edit it first in the
  composer. See the limits below.
- **Privacy by default**: `Authorization`, `Proxy-Authorization`, `Cookie` and `Set-Cookie` values
  are never stored. Bodies are capped (1 MB each, 64 MB total). All of it is configurable.
- **Manual capture** for code the weaver cannot reach: `HttpMonitorCapture.Track(request)`,
  `HttpMonitorCapture.CreateClient()`, or `HttpMonitorCapture.Begin(method, url)` for any client.
- **Your own logger**: assign `HttpMonitorLog.Logger` to route the SDK's own messages into your
  logging system.

Import the **Playground** sample from the Package Manager to try every path offline.

## Replay

Select a request and click **Replay** in the detail pane, or right-click a row. The request is
sent again from the Editor and appears as a new row with an **R** badge, linked to the original.
**Edit & resend** opens the composer, where you can change the method, URL, headers and body
before sending (Ctrl+Enter). The composer is also available empty under
**Window ▸ Analysis ▸ HTTP Request Composer**.

Three things to know:

- **It is the Editor sending, not your game.** The User-Agent, cookies and certificate handling are
  the Editor's. Good for "did the server change" and "what does this field do"; not a way to
  reproduce something that only happens on a device.
- **Redacted headers must be re-entered.** The SDK never stored your token, so the first replay of
  an authenticated request opens the composer and asks for it. The value is kept in memory for
  the Editor session only, never written to disk, a record, or a HAR file.
- **A replay repeats the request's effect.** Replaying a POST that submits a score submits it
  again. The window asks before resending anything that is not a GET, HEAD or OPTIONS; you can
  turn the question off in that dialog.

## Settings

**Project Settings ▸ HTTP Monitor** creates `Assets/HttpMonitor/HttpMonitorSettings.asset`, meant
to be committed and shared. Weaving options (on/off, release builds, excluded assemblies) take
effect on the next script compilation; the page offers a button. Capture options (body caps,
redacted headers) apply immediately.

The weaver reads a mirror of the weaving options from `ProjectSettings/HttpMonitorWeaver.cfg`,
written by the Editor. Commit it too. The `HTTP_MONITOR_DISABLE` scripting define turns weaving
off for a build target regardless of settings, and `[assembly: HttpMonitor.DoNotWeave]` opts a
single assembly out from code.

## Limits, stated up front

- Only C# traffic is seen. Native SDKs (ads, analytics written in Java or Objective-C) make their
  own connections and do not appear.
- `UnityWebRequest` headers Unity adds on its own (User-Agent, Accept-Encoding, Content-Length)
  are not visible to the SDK and are not recorded. Headers your code sets are.
- Bodies are captured from `UploadHandlerRaw` and `DownloadHandlerBuffer` only. File, texture,
  audio and asset-bundle handlers are recorded by size.
- `HttpClient` responses without a Content-Length are buffered to be captured, which defeats
  streaming consumers; turn **Buffer unknown-length responses** off for streaming APIs.
- WebGL: capture works (the calls are still C#); replay and the manual `HttpClient` helpers do not,
  since there is no `HttpClient` there.
