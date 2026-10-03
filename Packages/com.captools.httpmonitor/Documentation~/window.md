# The window

**Window ▸ Analysis ▸ HTTP Monitor** (Ctrl+Shift+H). The `?` button in the toolbar lists every
shortcut.

![The window with its parts labelled](images/window-annotated.png)

## Toolbar

| Control | What it does |
|---|---|
| **Record** | Capture on or off. Off: requests pass through untouched and nothing is stored. The dot is red while recording. |
| **Clear** | Remove every captured request. |
| **Preserve log** | Keep requests across Play sessions. Off by default, so each Play starts with an empty list. |
| **Auto-scroll** | Follow the newest request. It pauses on its own when you scroll up or select an older row. |
| **Timeline** | Show or hide the timeline strip at the bottom. |
| **HAR** | Export all, export only the rows shown by the current filter, or import a file. See [Export and import](har.md). |
| **Layout** | Switch between the list above the detail and the list beside it. The divider position is remembered for each. |
| **⚙** | Open [Settings](settings.md). |
| **?** | Keyboard shortcuts. |

## The request list

One row per request, newest at the bottom.

| Column | Shows |
|---|---|
| ● | Status at a glance: green 2xx, blue 3xx, orange 4xx, red 5xx or a transport failure, grey aborted, hollow while pending. |
| **Method** | GET, POST, … |
| **Status** | The status code, or `failed`, `aborted`, `…` while pending. Hover for the reason. |
| **Src** | How the request was captured. See the badges below. |
| **Name** | Path and query. Hover for the full URL. |
| **Host** | The host name. |
| **Type** | Short content type of the response: `json`, `html`, `png`. |
| **Size** | Downloaded bytes. Hover for both directions. |
| **Time** | Duration from send to finish. |
| **Started** | Local start time. |

Column widths are remembered. A pending row updates in place when the response arrives.

### Source badges

| Badge | Meaning |
|---|---|
| <span style="color:#3fb950">**A**</span> | Automatic: captured by a rewritten call site, no code involved. |
| <span style="color:#58a6ff">**M**</span> | Manual: captured through the [manual API](manual-capture.md). |
| <span style="color:#bc8cff">**A+M**</span> | Both: captured automatically and also tracked manually. One row, never two. |
| <span style="color:#20b2aa">**R**</span> | A [replay](replay.md), sent from the Editor. Hover to see which request it replays. |
| <span style="color:#e3a008">**HAR**</span> | Imported from a HAR file made by another tool. |

![Rows with different statuses and source badges](images/list-badges.png)

### Sorting

Click a column header to sort ascending, again for descending, a third time to return to arrival
order. Arrival order always breaks ties, so two requests with the same status stay in the order
they happened. While a sort is active, auto-scroll steps aside, since "newest" has no position.

### Auto-scroll

The list follows new requests until you scroll up, press Home or Up, or select an older row.
A **Jump to latest** pill appears; click it or press **End** to follow again.

### Right-click a row

Replay, Edit and resend, Pin in a new window, Copy URL, Copy as cURL, Copy response body,
Copy request body, Filter by host, Open in browser (GET only).

## Filtering

![The filter bar with a search and chips active](images/filter-bar.png)

- **Search** (Ctrl+F to focus, Esc to clear) matches the URL, the method, the status code, the
  state word (`failed`, `aborted`), and any header name or value.
- **Errors** keeps failed, aborted and 4xx/5xx requests.
- **A** and **M** choose automatic and manual capture. A request captured both ways matches
  either.
- **UWR**, **HttpClient**, **Custom** choose the client.
- **Method** lists only the methods that actually occurred.
- **Filter by host** from the row menu adds a host chip; click the chip to remove it.
- **Clear filters** resets everything.

The status bar always says how many requests are shown out of how many captured, and names the
active filters. When a filter hides everything, the list says so instead of looking empty.

## Timeline

A strip at the bottom of the window with one bar per visible request on a shared time axis,
coloured like the status dot. Concurrent requests stack into lanes; sequential ones share a lane.

![The timeline with a burst of concurrent requests](images/timeline.png)

- **Hover** a bar for method, path, status and duration.
- **Click** a bar to select its row. Selecting a row highlights its bar.
- **Drag the top edge** to resize. A taller strip shows more lanes.
- **Collapse** shrinks it to a one-line summary without hiding it.

The timeline always runs in time order, whatever the table is sorted by. It follows the filter:
bars for hidden rows are hidden too.

## Status bar

Request count, how many are shown, pending and failed counts, replays, the size of stored bodies,
and whether recording is on.

## Keyboard

| Key | Action |
|---|---|
| Ctrl+Shift+H | Open the window |
| Ctrl+F | Focus the search; Esc clears it |
| Up / Down | Move the selection |
| Home / End | First / newest request; End resumes auto-scroll |
| Enter | Open the detail: maximize it, or focus the popped-out window |
| Esc | Restore from maximized |
| Delete | Clear the selection |
| Ctrl+C | Copy the URL |
| Ctrl+Shift+C | Copy as cURL |
