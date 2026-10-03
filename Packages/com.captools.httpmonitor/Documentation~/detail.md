# Request detail

Select a row to see the exchange in full: a summary strip, then a **Request** block and a
**Response** block. Both blocks are visible at once, so you can compare what went out with what
came back without switching views.

![The detail pane for a JSON POST](images/detail-overview.png)

## Summary strip

The method and full URL, with buttons for **Copy URL**, **Copy as cURL**,
[**Replay** and **Edit & resend**](replay.md), **Maximize** and **Pop out**.

Below it: the status with its reason phrase in the status colour (`201 Created`), the client,
how the request was captured, when it started, how long it took, and bytes up and down. A failed
request shows its error text in red.

**Copy as cURL** produces a command that reproduces the request. Redacted header values become
shell variables such as `$AUTHORIZATION`, so the command is honest about what it does not know
and still runs once you set the variable. A binary body is referenced as a file rather than
pasted inline.

## Request and Response blocks

Each block has two tabs, and the tab shows what is inside: `Headers (7)`, `Body (1.2 KB)`. Each
block remembers the tab you last chose.

### Headers

Name and value rows. Values are selectable.

- **Copy all** copies the block as `name: value` lines.
- **Right-click** a row to copy the name, the value, or the line.
- A **redacted** badge replaces any value on the redaction list. The real value was sent on the
  wire; it was never stored. See [Settings](settings.md).

For `UnityWebRequest`, request headers are the ones your code set, plus the Content-Type that
comes from the upload handler. Headers Unity adds by itself are not visible to the SDK; see
[Limits](troubleshooting.md).

### Body

A line above the body says what it is and how large, and explains anything unusual.

![A pretty-printed JSON response with line numbers](images/body-pretty.png)

| Mode | For | Notes |
|---|---|---|
| **Pretty** | JSON, HTML, XML | Indented by nesting, with line numbers. The default for these types. |
| **Raw** | Any text | Exactly as captured. |
| **Hex** | Anything | Offset, bytes, ASCII. The default for binary. |
| **Image** | PNG, JPEG | A decoded preview with its dimensions. |

- **Wrap** wraps long lines. Line numbers hide while wrapping, since they could not line up.
- **Copy** copies what is shown: pretty text in Pretty mode, base64 for binary.
- **Save…** writes the captured bytes to a file.

How the type is decided: the `Content-Type` header wins. If the server says `text/plain`, the
body opens as raw text even when it is really JSON, and **Pretty** is still offered, one click
away. With no usable Content-Type, the first bytes are sniffed.

If the body cannot be formatted (invalid JSON, unbalanced markup) the viewer says so and shows
it raw. It never shows a half-formatted body.

### When a body is missing

The body area always says why:

| Message | Cause |
|---|---|
| *No response body* | The server sent none (a 204, a HEAD). |
| *Not captured: … only DownloadHandlerBuffer is captured* | The response went to a file, texture, audio or asset-bundle handler. It is recorded by size. |
| *Showing the first 1 MB of 4.2 MB* | The body is larger than the per-body cap. |
| *No response: connection refused* | The request failed before a response. |
| *Waiting for the response* | Still in flight. |

Caps are in [Settings](settings.md).

## Room to read

![The detail maximized, with the one-row strip on top](images/detail-maximized.png)

- **Maximize** gives the detail the whole window. A one-row strip at the top shows the selected
  request; **Up** and **Down** still move the selection, **Esc** restores.
- **Pop out** moves the detail into its own dockable window. The main window shows a short
  notice in its place, with **Dock back**.
- In the popped-out window, **Pin** freezes it on its request. Pin one response, select another
  request, and read both side by side or on a second monitor. Any number of pinned windows can
  be open. You can also pin straight from the list: right-click ▸ **Pin in a new window**.

![Two pinned detail windows next to the main window](images/detail-pinned.png)

Pinned windows survive a script recompile. If their request has left the log, they say so.
