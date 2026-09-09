using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Capture options, edited live and persisted through the Editor store. The gear button in the
    /// traffic window opens it. Project Settings (M3) will host the same fields as a settings page.
    /// </summary>
    public sealed class OptionsWindow : EditorWindow
    {
        private const int Kilobyte = 1024;
        private const int Megabyte = 1024 * 1024;

        public static void Open()
        {
            var window = GetWindow<OptionsWindow>(true, "HTTP Monitor Options", true);
            window.minSize = new Vector2(420, 360);
            window.maxSize = new Vector2(640, 520);
        }

        public void CreateGUI()
        {
            var options = HttpMonitorSession.Current.Options;
            var store = EditorRecordStore.instance;
            var root = rootVisualElement;
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = 10;

            root.Add(Heading("Capture"));

            var captureBodies = new Toggle("Capture bodies") { value = options.CaptureBodies, tooltip = "Off: no request or response body is read or stored" };
            captureBodies.RegisterValueChangedCallback(e => Apply(o => o.CaptureBodies = e.newValue));
            root.Add(captureBodies);

            var maxBody = new IntegerField("Per-body cap (KB)") { value = options.MaxBodyBytes / Kilobyte, tooltip = "Longer bodies keep their first bytes and are flagged truncated" };
            maxBody.RegisterValueChangedCallback(e => Apply(o => o.MaxBodyBytes = Math.Max(0, e.newValue) * Kilobyte));
            root.Add(maxBody);

            var maxTotal = new IntegerField("Runtime body budget (MB)") { value = (int)(options.MaxTotalBodyBytes / Megabyte), tooltip = "Oldest records are evicted from the runtime session to stay under it" };
            maxTotal.RegisterValueChangedCallback(e => Apply(o => o.MaxTotalBodyBytes = Math.Max(0, e.newValue) * (long)Megabyte));
            root.Add(maxTotal);

            var bufferUnknown = new Toggle("Buffer unknown-length responses") { value = options.BufferUnknownLengthResponses, tooltip = "HttpClient only. Chunked responses can only be captured by buffering them; turn off for streaming APIs" };
            bufferUnknown.RegisterValueChangedCallback(e => Apply(o => o.BufferUnknownLengthResponses = e.newValue));
            root.Add(bufferUnknown);

            root.Add(Heading("Privacy"));

            var redactedHeaders = new TextField("Redacted headers") { value = string.Join(", ", options.RedactedHeaders), tooltip = "Comma-separated, case-insensitive. Values of these headers are never stored." };
            redactedHeaders.RegisterValueChangedCallback(e => Apply(o =>
            {
                o.RedactedHeaders.Clear();

                foreach (var name in e.newValue.Split(','))
                {
                    var trimmed = name.Trim();

                    if (trimmed.Length > 0)
                        o.RedactedHeaders.Add(trimmed);
                }
            }));
            root.Add(redactedHeaders);

            var redactedValue = new TextField("Redacted value") { value = options.RedactedValue, tooltip = "What is stored in place of a redacted header value" };
            redactedValue.RegisterValueChangedCallback(e => Apply(o => o.RedactedValue = string.IsNullOrEmpty(e.newValue) ? HttpMonitorOptions.DefaultRedactedValue : e.newValue));
            root.Add(redactedValue);

            root.Add(Heading("Editor window"));

            var capacity = new IntegerField("Records kept") { value = store.Buffer.Capacity, tooltip = "Oldest records leave the window past this count" };
            capacity.RegisterValueChangedCallback(e => store.Buffer.Capacity = Math.Max(1, e.newValue));
            root.Add(capacity);

            var editorBudget = new IntegerField("Editor body budget (MB)") { value = (int)(store.Buffer.MaxTotalBodyBytes / Megabyte), tooltip = "Bodies kept in the window across domain reloads; saved to disk before every reload, so keep it modest" };
            editorBudget.RegisterValueChangedCallback(e => store.Buffer.MaxTotalBodyBytes = Math.Max(0, e.newValue) * (long)Megabyte);
            root.Add(editorBudget);

            var note = new Label("Changes apply immediately to new requests and are remembered across domain reloads and Editor restarts on this machine. A Project Settings page for sharing them through version control is planned.")
            {
                style = { whiteSpace = WhiteSpace.Normal, marginTop = 12, opacity = 0.7f },
            };
            root.Add(note);

            var reset = new Button(ResetToDefaults) { text = "Reset to defaults", style = { marginTop = 8, alignSelf = Align.FlexStart } };
            root.Add(reset);
        }

        private static Label Heading(string text)
        {
            return new Label(text) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8, marginBottom = 4 } };
        }

        private static void Apply(Action<HttpMonitorOptions> change)
        {
            change(HttpMonitorSession.Current.Options);
            EditorRecordStore.instance.SaveOptionsFromSession();
        }

        private void ResetToDefaults()
        {
            Apply(o =>
            {
                o.CaptureBodies = true;
                o.MaxBodyBytes = HttpMonitorOptions.DefaultMaxBodyBytes;
                o.MaxTotalBodyBytes = HttpMonitorOptions.DefaultMaxTotalBodyBytes;
                o.BufferUnknownLengthResponses = true;
                o.RedactedValue = HttpMonitorOptions.DefaultRedactedValue;
                o.RedactedHeaders.Clear();

                foreach (var name in new List<string> { "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie" })
                    o.RedactedHeaders.Add(name);
            });

            var store = EditorRecordStore.instance;
            store.Buffer.Capacity = EditorRecordBuffer.DefaultCapacity;
            store.Buffer.MaxTotalBodyBytes = EditorRecordBuffer.DefaultMaxTotalBodyBytes;

            rootVisualElement.Clear();
            CreateGUI();
        }
    }
}
