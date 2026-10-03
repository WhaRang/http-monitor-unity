# Builds and devices

## What is instrumented where

| Where | Captured automatically | Change it with |
|---|---|---|
| Editor, Play Mode and Edit Mode | Yes | **Weaving Enabled** in [Settings](settings.md) |
| Development build | Yes | The same |
| Release build | **No** | **Weave Release Builds** in [Settings](settings.md) |

A development build is one with **Development Build** ticked in Build Settings. The decision is
made per build, when scripts compile for the player.

In a release build with default settings, your request code is compiled exactly as written. The
package's runtime assembly is still included, since the manual API may be in use, but nothing
calls into it.

## Where the records are

The window shows requests made **in the Editor process**: Play Mode, and Editor tools that make
requests in Edit Mode.

A player build captures too, but the records live in memory on the device, in
`HttpMonitorSession.Current`. **There is no built-in way yet to view a device's traffic in the
Editor window.** That is planned. Until then, on a device you can:

- Read records from code and show them in your own debug screen. See
  [Reading records from code](session-api.md).
- Log failures as they happen:

```csharp
HttpMonitorSession.Current.RecordUpdated += record =>
{
    if (record.State != HttpRecordState.Completed || record.StatusCode >= 400)
        Debug.LogWarning($"[net] {record.Method} {record.Url} → {record.State} {record.StatusCode} {record.Error}");
};
```

Remember that this event arrives on a thread-pool thread for `HttpClient` traffic.

## Settings in a build

The settings asset is a preloaded asset, so it is inside the build and applies before the first
scene. Body caps and the redaction list on the device are the ones in the asset.

A build machine needs `ProjectSettings/HttpMonitorWeaver.cfg` from version control, because the
weaver reads it during the very first compile, before any Editor code has run.

## Platforms

| Platform | Capture | Replay, `HttpClient` helpers |
|---|---|---|
| Windows, macOS, Linux | Yes | Yes |
| Android, iOS (Mono and IL2CPP) | Yes | Yes |
| WebGL | `UnityWebRequest` only | No. There is no `HttpClient` in the browser. |

IL2CPP is fully supported. The rewriting happens on IL, before IL2CPP converts it to C++.

## Managed code stripping

The package ships a `link.xml` that keeps its runtime assembly intact, so no stripping level
needs special handling.

## Memory on device

Bodies are the cost. With the defaults a session can hold up to 64 MB of them. For a
memory-constrained device, lower the caps in the asset, or turn body capture off at startup:

```csharp
#if DEVELOPMENT_BUILD && (UNITY_ANDROID || UNITY_IOS)
HttpMonitorSession.Current.Options.CaptureBodies = false;
#endif
```
