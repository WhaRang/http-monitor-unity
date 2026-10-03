# Settings

**Edit ▸ Project Settings ▸ HTTP Monitor**, or the ⚙ button in the window.

![The settings page](images/settings.png)

## The settings asset

Out of the box there is no asset and the defaults apply. Click **Create settings asset** to make
`Assets/HttpMonitor/HttpMonitorSettings.asset`.

- It is a normal asset. **Commit it**, and the whole team shares one configuration.
- It is registered as a preloaded asset, so it ships inside builds and applies before the first
  scene loads.
- You can move it anywhere under `Assets`. Keep only one.

## Automatic capture (weaving)

These are read when scripts compile. After changing one, the page shows **Recompile now**.

| Setting | Default | Meaning |
|---|---|---|
| **Weaving Enabled** | on | Rewrite `UnityWebRequest` and `HttpClient` call sites so requests are captured with no code changes. Off: only the [manual API](manual-capture.md) captures. |
| **Weave Release Builds** | off | Also instrument non-development player builds. Leave off unless you need capture in a shipped build. |
| **Excluded Assemblies** | none | Assembly names the weaver leaves alone, for example a third-party networking library. |

Two more switches live outside the asset:

- The scripting define **`HTTP_MONITOR_DISABLE`** turns weaving off for a build target,
  whatever the asset says.
- **`[assembly: HttpMonitor.DoNotWeave]`** in any source file opts that assembly out from code.

The weaver runs in a separate process and cannot read assets, so the Editor mirrors these three
settings into **`ProjectSettings/HttpMonitorWeaver.cfg`**. Commit that file too. Edit the asset,
not the file. See [How it works](how-it-works.md).

## Bodies

Applied immediately to new requests.

| Setting | Default | Meaning |
|---|---|---|
| **Capture Bodies** | on | Off: no request or response body is read or stored. Headers, status and sizes still are. |
| **Max Body Kilobytes** | 1024 | Per-body cap. A longer body keeps its first bytes and is marked truncated. |
| **Max Total Body Megabytes** | 64 | Cap on all bodies in the session. The oldest requests are dropped to stay under it. |
| **Buffer Unknown Length Responses** | on | `HttpClient` only. A response without a Content-Length can only be captured by buffering all of it, which defeats code that streams. Turn off for streaming APIs. |

## Privacy

| Setting | Default | Meaning |
|---|---|---|
| **Redacted Headers** | `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie` | Values of these headers are never stored, on either side. Names are case-insensitive. Add your own, such as `X-Api-Key`. |
| **Redacted Value** | `<redacted>` | What is stored instead. |

Redaction happens at the moment a request is recorded. The secret does not exist anywhere in the
tool afterwards: not in the window, not in a HAR export, not in a copied cURL command.

Redaction covers headers. A secret inside a body or a URL query string is captured as-is. If
your API puts tokens there, lower the body cap, turn body capture off, or keep the exports to
yourself.

## Editor window (this machine)

Stored per machine, not in the asset.

| Setting | Default | Meaning |
|---|---|---|
| **Records kept** | 1000 | Oldest rows leave the window past this count. |
| **Body budget (MB)** | 16 | Bodies the window keeps across script recompiles. They are written to disk before each recompile, so a large value slows recompiles down. |

## Reset

**Reset to defaults** restores every value above. It supports Undo.

## From code

The same capture settings are available at runtime, for example to turn bodies off on a
low-memory device:

```csharp
var options = HttpMonitorSession.Current.Options;
options.CaptureBodies = false;
options.MaxBodyBytes = 64 * 1024;
options.RedactedHeaders.Add("X-Api-Key");
```

Values set in code last until the settings asset is applied again, which happens when it loads.
