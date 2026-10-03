# Export and import (HAR)

HAR 1.2 is the standard format for a recorded HTTP session. Chrome and Firefox DevTools, Charles,
Proxyman and Fiddler all open it.

## Export

Toolbar ▸ **HAR**:

- **Export all…** writes every captured request.
- **Export shown…** writes only the rows the current filter shows. Available while a filter is
  active.

![The HAR menu](images/har-menu.png)

What is in the file:

- Method, URL, query string, headers and bodies, status, sizes and the total time.
- Text bodies as text. Binary bodies as base64.
- A failed request as status `0` with its error as the status text, which is how other tools
  display a network failure.
- **Redacted header values stay redacted.** The placeholder is exported, never the secret,
  because the secret was never stored.

To check a file, drag it onto the Network tab of Chrome DevTools.

## Import

Toolbar ▸ **HAR** ▸ **Import…** adds the file's requests to the window. Imported rows are
appended; nothing is replaced.

- A file exported by HTTP Monitor comes back complete: client, capture source, state, error
  text, truncation flags and replay links.
- A file from another tool is mapped onto what HAR can express: a status above 0 is a completed
  request, status 0 a failed one. These rows carry an amber **HAR** badge, and the automatic and
  manual filter chips do not apply to them.

## What HAR cannot say

HAR has no fields for how a request was captured or why it was aborted. HTTP Monitor writes those
into an `_httpMonitor` object on each entry. Fields starting with an underscore are the format's
own extension mechanism: other tools ignore them, and HTTP Monitor reads them back on import.

Timings are honest about what is known. Only the total duration is measured, so it is reported
as `wait`, and DNS, connect and TLS phases are marked unavailable rather than invented.
