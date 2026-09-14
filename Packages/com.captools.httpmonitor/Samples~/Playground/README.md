# Playground

A scene-free sample: add the `Playground` component to any GameObject in any scene, press Play,
and open **Window ▸ Analysis ▸ HTTP Monitor** (Ctrl+Shift+H).

The component starts a local HTTP server on a free loopback port, so everything works offline.
Buttons cover every capture path:

- **UnityWebRequest**, captured automatically by weaving: JSON, HTML, image, 404, 500, slow,
  connection refused, a 3 MB body (truncated in the window), redacted auth headers, an abort
  mid-flight, and a burst of 20 concurrent requests for the timeline.
- **HttpClient**, also automatic: GET, POST, cancellation, chunked transfer.
- **Manual capture**: `HttpMonitorCapture.Track` on a request that was also woven (shows as A+M),
  and `HttpMonitorCapture.Begin` for a client the SDK cannot see.

Nothing in the UnityWebRequest and HttpClient sections references the SDK. That is the point.
