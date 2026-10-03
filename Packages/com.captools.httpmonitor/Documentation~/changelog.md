# Changelog

## 1.0.0 (unreleased)

First release.

**Capture**

- Automatic capture of `UnityWebRequest` and `System.Net.Http.HttpClient` through compile-time
  rewriting, in the Editor and in development builds.
- Request headers, response headers, bodies, status, sizes and duration.
- Header redaction and body caps, applied before anything is stored.
- Manual capture API for precompiled code and custom clients.
- Opt-outs: a project setting, an assembly list, a scripting define, an assembly attribute.

**Window**

- Request table with status colours, source badges, sortable columns and remembered widths.
- Search and filters by text, errors, capture source, client, method and host.
- Timeline strip, resizable and collapsible.
- Detail pane with request and response blocks; pretty-printed JSON, HTML and XML; hex and image
  views; copy and save.
- Maximize, pop out and pin the detail.
- Copy as cURL. HAR 1.2 export and import.

**Replay**

- One-click replay from the Editor, and a composer to edit a request before sending.
- Session-only memory for redacted header values.

**Project**

- Settings asset and Project Settings page.
- Injectable logger.
- Playground sample with a local server.
