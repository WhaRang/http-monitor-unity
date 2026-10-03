# HTTP Monitor for Unity

Chrome DevTools' Network tab, inside the Unity Editor. Every HTTP request and response your game
makes, live, filterable, exportable and replayable, with no code changes.

![The HTTP Monitor window during Play Mode](images/window-overview.png)

## What it does

- **Captures automatically.** `UnityWebRequest` and `System.Net.Http.HttpClient` traffic shows up
  on its own. Call sites are rewritten when your scripts compile, so there is nothing to wrap
  and nothing to remember.
- **Shows what happened.** A request table with status colours, search and filters, sortable
  columns, a timeline, and a detail pane with the request and response side by side. JSON and
  HTML are pretty-printed, binary bodies get a hex view, images get a preview.
- **Lets you ask again.** Replay any captured request from the Editor with one click, or edit it
  first in the composer.
- **Leaves with you.** Copy as cURL, export a session as HAR and open it in Chrome, Firefox,
  Charles or Proxyman.
- **Keeps secrets out.** Authorization and cookie headers are redacted before anything is stored.
  Release builds are not instrumented unless you ask.

## Where to go

| If you want to | Read |
|---|---|
| See your first request in two minutes | [Getting started](getting-started.md) |
| Learn the window | [The window](window.md), [Request detail](detail.md) |
| Resend a request | [Replay and the composer](replay.md) |
| Change what is captured | [Settings](settings.md) |
| Capture a client the SDK does not know | [Manual capture](manual-capture.md) |
| Understand what the package does to your code | [How it works](how-it-works.md) |
| Find out why something is missing | [Limits and troubleshooting](troubleshooting.md) |

## Requirements

- Developed and tested on Unity 6000.3. Other versions have not been verified yet.
- The `com.unity.nuget.mono-cecil` package, resolved automatically as a dependency.
- Any platform for capture. Replay and the `HttpClient` helpers need a platform with
  `HttpClient`, which excludes WebGL.
