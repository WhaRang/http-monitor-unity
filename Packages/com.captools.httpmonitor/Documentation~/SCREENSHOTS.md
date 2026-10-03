# Screenshot checklist

This file is a to-do list for the maintainer. It is not part of the published book (it is not
listed in SUMMARY.md).

Save every image as PNG in `Documentation~/images/` with the exact file name below. The pages
already reference these names, so an image appears as soon as its file exists.

## Before you start

- **Theme**: Unity's dark theme for all of them, for a consistent look.
- **Data**: import the Playground sample, add the `Playground` component to a GameObject, press
  Play. Its buttons produce every kind of row offline. Turn **Preserve log** on so rows survive.
- **Window size**: undock the HTTP Monitor window and size it to about 1400 × 800 for full-window
  shots. Crop tighter shots to the part being shown.
- **Capture**: Win+Shift+S (Windows) or Cmd+Shift+4 (macOS). Capture the window only, without
  the desktop.
- **Width**: 1600 px at most. Larger files slow the site down.
- **Secrets**: the Playground uses fake tokens. If you screenshot a real project, check that no
  real host name or body content is visible.

## The list

| # | File | Used on | What to show | How to get there |
|---|---|---|---|---|
| 1 | `window-overview.png` | Introduction | The whole window, side-by-side layout: a dozen rows with mixed statuses, one JSON request selected, detail showing a pretty-printed response. The hero image. | Playground: GET JSON, POST JSON, GET HTML, 404, 500, Slow, a few more. Select the POST JSON row. Response block on Body. |
| 2 | `window-empty.png` | Getting started | The window before Play: empty list with the "Press Play…" message, Record on. | Clear the log, exit Play Mode. |
| 3 | `playground.png` | Getting started | The Game view with the Playground's buttons. | Game view while playing. Crop to the buttons. |
| 4 | `detail-overview.png` | Getting started, Request detail | The detail pane only: summary strip, Request block on Headers, Response block on Body with pretty JSON. | Select POST JSON. Crop to the detail pane. |
| 5 | `window-annotated.png` | The window | The whole window with numbered callouts: 1 toolbar, 2 filter bar, 3 request list, 4 detail, 5 timeline, 6 status bar. | Screenshot 1 with the timeline on, then add numbers in any image editor. A plain screenshot is acceptable if you skip the callouts. |
| 6 | `list-badges.png` | The window | A crop of the list showing every status colour and badge: 200, 404, 500, failed, aborted, pending, and Src badges A, M, A+M, R. | Playground: GET JSON, 404, 500, Connection refused, Abort mid-flight, Slow (capture while pending), Track after yield (A+M), Custom client via Begin (M), then Replay one row (R). |
| 7 | `filter-bar.png` | The window | The filter bar with text in the search box, Errors on, and a host chip visible; status bar reading "N of M shown". | Type `status` in search, click Errors, right-click a row ▸ Filter by host. Crop to the filter bar, a few rows, and the status bar. |
| 8 | `timeline.png` | The window | The timeline strip with several lanes of overlapping bars, one bar hovered with its tooltip. | Turn Timeline on, click Playground "Burst of 20", drag the strip's top edge taller, hover a bar. Crop to the strip plus a few rows above. |
| 9 | `body-pretty.png` | Request detail | A Response block on Body: nested JSON, indented, with line numbers and the Pretty / Raw / Hex mode strip. | Select POST JSON. Crop to the Response block. |
| 10 | `detail-maximized.png` | Request detail | The window with the detail maximized: the one-row strip on top and the detail filling the rest. | Select a row, click Maximize. |
| 11 | `detail-pinned.png` | Request detail | The main window with two pinned detail windows beside it, showing two different requests. | Right-click two rows ▸ Pin in a new window. Arrange the three windows side by side. A wider capture is fine here. |
| 12 | `replay-row.png` | Replay | The list with an original row and its replay below it (R badge), the replay selected, and the detail strip showing "replay of #N". | Select GET JSON, click Replay. |
| 13 | `composer.png` | Replay | The composer window filled from a POST: method, URL, two or three headers, a formatted JSON body, and a result line after sending. | Select POST JSON ▸ Edit & resend, click Format JSON, Send. |
| 14 | `composer-redacted.png` | Replay | The composer with a redacted Authorization row: the "redacted" badge and the empty password field. | Playground "With auth header", select it ▸ Edit & resend. Crop to the Headers section. |
| 15 | `har-menu.png` | Export and import | The toolbar with the HAR menu open. | Click HAR. Crop to the toolbar and the open menu. |
| 16 | `settings.png` | Settings | Project Settings ▸ HTTP Monitor with the asset created, all sections visible. | Create the asset first. Capture the right-hand panel of the Project Settings window. |

## After adding images

Push to `main`. The Docs workflow rebuilds the site. To preview locally, install mdBook and run
`mdbook serve` in the repository root.
