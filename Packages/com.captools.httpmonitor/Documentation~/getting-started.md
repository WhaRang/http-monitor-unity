# Getting started

## Install

In Unity: **Window ▸ Package Manager ▸ + ▸ Install package from git URL**, and paste:

```
https://github.com/WhaRang/http-monitor-unity.git?path=Packages/com.captools.httpmonitor
```

The one dependency, Mono.Cecil, is resolved for you.

While developing the package itself, an embedded copy under `Packages/` or a `file:` path in
`manifest.json` works the same way.

## See your first request

1. Open **Window ▸ Analysis ▸ HTTP Monitor**, or press **Ctrl+Shift+H**.
2. Press **Play**.
3. Requests your game makes appear in the list as they are sent.

That is the whole setup. There is no component to add and no call to make.

![The empty window, waiting for Play](images/window-empty.png)

If your project makes no requests yet, import the **Playground** sample: **Package Manager ▸
HTTP Monitor ▸ Samples ▸ Playground ▸ Import**, add the `Playground` component to any GameObject,
and press Play. It starts a small local server and gives you a button per kind of request, so
everything works offline.

![The Playground sample's buttons](images/playground.png)

## Read a request

Click a row. The detail pane shows what was sent and what came back, each with a **Body** and a
**Headers** tab.

![A selected request with its detail](images/detail-overview.png)

## Three things worth knowing on day one

- **Secrets are redacted.** `Authorization`, `Proxy-Authorization`, `Cookie` and `Set-Cookie`
  values are replaced before anything is stored. You will see a "redacted" badge in their place.
  The list is editable in [Settings](settings.md).
- **The log clears when you press Play.** Turn on **Preserve log** in the toolbar to keep
  requests across Play sessions.
- **Release builds are untouched.** Capture is active in the Editor and in development builds.
  A release build contains no capture code on its request path unless you opt in. See
  [Builds and devices](builds.md).

## Next

- [The window](window.md): filters, sorting, the timeline.
- [Replay and the composer](replay.md): send a request again.
- [Settings](settings.md): what is captured and how much.
