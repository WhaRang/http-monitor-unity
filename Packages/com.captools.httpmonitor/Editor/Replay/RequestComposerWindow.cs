using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Edit a request and send it from the Editor. Opened pre-filled from a captured record
    /// ("Edit and resend") or empty from the menu. The request is serialized with the window so it
    /// survives a domain reload; values typed for redacted headers go to <see cref="ReplaySecrets"/>
    /// and are never serialized. Ctrl+Enter sends.
    /// </summary>
    public sealed class RequestComposerWindow : EditorWindow
    {
        private const string StyleSheetPath = "Packages/com.captools.httpmonitor/Editor/UI/HttpMonitorWindow.uss";
        private const string CustomMethod = "Custom…";
        private static readonly string[] Methods = { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", CustomMethod };

        [SerializeField] private ReplayRequest _request = new ReplayRequest { Url = "https://" };
        [SerializeField] private string _bodyText = string.Empty;
        [SerializeField] private bool _bodyIsBinary;

        private CancellationTokenSource _inFlight;
        private DropdownField _method;
        private TextField _customMethod;
        private TextField _url;
        private Toggle _followRedirects;
        private IntegerField _timeout;
        private VisualElement _headerRows;
        private Label _bodyHint;
        private TextField _body;
        private Label _bodyNote;
        private VisualElement _binaryNote;
        private Button _send;
        private Button _cancel;
        private Label _result;
        private Button _showInList;
        private HttpRecord _lastRecord;

        [MenuItem("Window/Analysis/HTTP Request Composer")]
        public static void OpenEmpty()
        {
            Open(new ReplayRequest { Url = "https://" });
        }

        /// <summary>Opens (or focuses) the composer with the given request loaded.</summary>
        public static RequestComposerWindow Open(ReplayRequest request)
        {
            var window = GetWindow<RequestComposerWindow>();
            window.Load(request ?? new ReplayRequest { Url = "https://" });
            window.Show();
            window.Focus();

            return window;
        }

        /// <summary>The request as it would be sent now: edits applied, remembered secrets filled in.</summary>
        internal ReplayRequest BuildRequest()
        {
            var request = _request.Clone();
            request.Method = ResolveMethod();
            request.Url = (_url?.value ?? _request.Url ?? string.Empty).Trim();
            request.Body = _bodyIsBinary ? _request.Body : (string.IsNullOrEmpty(_bodyText) ? null : Encoding.UTF8.GetBytes(_bodyText));
            ReplaySecrets.Fill(request);

            return request;
        }

        private void OnEnable()
        {
            minSize = new Vector2(520, 420);
            UpdateTitle();
        }

        private void OnDisable()
        {
            _inFlight?.Cancel();
        }

        private void Load(ReplayRequest request)
        {
            _request = request;
            _bodyIsBinary = request.HasBody && !RecordFormat.LooksLikeText(request.Body);
            _bodyText = _bodyIsBinary || !request.HasBody ? string.Empty : Encoding.UTF8.GetString(request.Body);
            _lastRecord = null;
            UpdateTitle();

            if (rootVisualElement.childCount > 0)
                Rebuild();
        }

        private void UpdateTitle()
        {
            titleContent = new GUIContent(_request != null && _request.OriginalId > 0 ? $"Replay #{_request.OriginalId}" : "Compose request");
        }

        public void CreateGUI()
        {
            Rebuild();
        }

        // ---------------------------------------------------------------- building

        private void Rebuild()
        {
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("hm-root");
            root.AddToClassList("hm-composer");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "hm-dark" : "hm-light");

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);

            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            root.Add(BuildRequestLine());
            root.Add(BuildOptions());

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("hm-composer-scroll");
            scroll.Add(BuildHeaders());
            scroll.Add(BuildBody());
            root.Add(scroll);

            root.Add(BuildResultBar());
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && e.actionKey)
                {
                    Send();
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            if (_request.HasRedactedHeaders)
                _result.text = "This request had redacted headers. Paste the real values below, they are kept in memory for this Editor session only.";
        }

        private VisualElement BuildRequestLine()
        {
            var line = new VisualElement();
            line.AddToClassList("hm-composer-line");

            var known = Array.IndexOf(Methods, (_request.Method ?? "GET").ToUpperInvariant()) >= 0;
            _method = new DropdownField(new List<string>(Methods), known ? (_request.Method ?? "GET").ToUpperInvariant() : CustomMethod) { tooltip = "HTTP method" };
            _method.AddToClassList("hm-composer-method");
            _method.RegisterValueChangedCallback(_ => { _customMethod.style.display = _method.value == CustomMethod ? DisplayStyle.Flex : DisplayStyle.None; UpdateBodyHint(); });
            line.Add(_method);

            _customMethod = new TextField { value = known ? string.Empty : _request.Method, tooltip = "Any method token, e.g. PROPFIND" };
            _customMethod.AddToClassList("hm-composer-custom-method");
            _customMethod.style.display = known ? DisplayStyle.None : DisplayStyle.Flex;
            line.Add(_customMethod);

            _url = new TextField { value = _request.Url, tooltip = "Absolute URL" };
            _url.AddToClassList("hm-composer-url");
            _url.RegisterValueChangedCallback(e => _request.Url = e.newValue);
            line.Add(_url);

            _send = new Button(Send) { text = "Send", tooltip = "Send from the Editor (Ctrl+Enter). The result appears in the HTTP Monitor window." };
            _send.AddToClassList("hm-composer-send");
            line.Add(_send);

            _cancel = new Button(() => _inFlight?.Cancel()) { text = "Cancel", tooltip = "Abort the request in flight" };
            _cancel.AddToClassList("hm-composer-send");
            _cancel.style.display = DisplayStyle.None;
            line.Add(_cancel);

            return line;
        }

        private VisualElement BuildOptions()
        {
            var line = new VisualElement();
            line.AddToClassList("hm-composer-options");

            _followRedirects = new Toggle("Follow redirects") { value = _request.FollowRedirects, tooltip = "Off: a 3xx is recorded as the response instead of being followed" };
            _followRedirects.RegisterValueChangedCallback(e => _request.FollowRedirects = e.newValue);
            line.Add(_followRedirects);

            _timeout = new IntegerField("Timeout (s)") { value = _request.TimeoutSeconds, tooltip = "The replay is recorded as Aborted when this elapses" };
            _timeout.AddToClassList("hm-composer-timeout");
            _timeout.RegisterValueChangedCallback(e => _request.TimeoutSeconds = Mathf.Max(1, e.newValue));
            line.Add(_timeout);

            if (_request.OriginalId > 0)
            {
                var origin = new Label($"Based on request #{_request.OriginalId}") { tooltip = "Click to select the original in the HTTP Monitor window" };
                origin.AddToClassList("hm-composer-origin");
                origin.RegisterCallback<ClickEvent>(_ => HttpMonitorWindow.Instance?.SelectById(_request.OriginalId));
                line.Add(origin);
            }

            return line;
        }

        private VisualElement BuildHeaders()
        {
            var section = new VisualElement();
            section.AddToClassList("hm-composer-section");

            var head = new VisualElement();
            head.AddToClassList("hm-composer-section-head");
            var title = new Label("Headers");
            title.AddToClassList("hm-composer-section-title");
            head.Add(title);
            var spacer = new VisualElement();
            spacer.AddToClassList("hm-toolbar-spacer");
            head.Add(spacer);
            var add = new Button(() =>
            {
                _request.Headers.Add(new EditorHeader(string.Empty, string.Empty));
                RebuildHeaderRows();
            }) { text = "+ Add header" };
            add.AddToClassList("hm-small-button");
            head.Add(add);
            section.Add(head);

            _headerRows = new VisualElement();
            section.Add(_headerRows);
            RebuildHeaderRows();

            return section;
        }

        private void RebuildHeaderRows()
        {
            _headerRows.Clear();

            if (_request.Headers.Count == 0)
            {
                var none = new Label("No headers. HttpClient adds Host and Content-Length itself.");
                none.AddToClassList("hm-muted");
                none.AddToClassList("hm-composer-empty");
                _headerRows.Add(none);
            }

            for (var i = 0; i < _request.Headers.Count; i++)
                _headerRows.Add(BuildHeaderRow(i));
        }

        private VisualElement BuildHeaderRow(int index)
        {
            var header = _request.Headers[index];
            var redacted = _request.RedactedHeaderNames.Exists(n => string.Equals(n, header.Name, StringComparison.OrdinalIgnoreCase));
            var row = new VisualElement();
            row.AddToClassList("hm-composer-header-row");

            var name = new TextField { value = header.Name, tooltip = "Header name" };
            name.AddToClassList("hm-composer-header-name");
            name.RegisterValueChangedCallback(e => _request.Headers[index] = new EditorHeader(e.newValue, _request.Headers[index].Value));
            row.Add(name);

            string secret = null;
            var hasSecret = redacted && ReplaySecrets.TryGet(_request.Host, header.Name, out secret);
            var value = new TextField { value = redacted ? (hasSecret ? secret : string.Empty) : header.Value, isPasswordField = redacted };
            value.AddToClassList("hm-composer-header-value");
            value.tooltip = redacted
                ? "Redacted in the capture, so the SDK never had it. Paste the real value; it is remembered for this Editor session only and never written anywhere."
                : "Header value";

            if (redacted)
            {
                value.RegisterValueChangedCallback(e => ReplaySecrets.Remember(_request.Host, _request.Headers[index].Name, e.newValue));
                var badge = new Label("redacted") { tooltip = value.tooltip };
                badge.AddToClassList("hm-redacted-badge");
                row.Add(badge);
            }
            else
            {
                value.RegisterValueChangedCallback(e => _request.Headers[index] = new EditorHeader(_request.Headers[index].Name, e.newValue));
            }

            row.Add(value);

            var remove = new Button(() =>
            {
                var removed = _request.Headers[index];
                _request.Headers.RemoveAt(index);
                _request.RedactedHeaderNames.RemoveAll(n => string.Equals(n, removed.Name, StringComparison.OrdinalIgnoreCase));
                RebuildHeaderRows();
                UpdateBodyHint();
            }) { text = "✕", tooltip = "Remove this header" };
            remove.AddToClassList("hm-composer-remove");
            row.Add(remove);

            return row;
        }

        private VisualElement BuildBody()
        {
            var section = new VisualElement();
            section.AddToClassList("hm-composer-section");

            var head = new VisualElement();
            head.AddToClassList("hm-composer-section-head");
            var title = new Label("Body");
            title.AddToClassList("hm-composer-section-title");
            head.Add(title);
            _bodyHint = new Label();
            _bodyHint.AddToClassList("hm-muted");
            _bodyHint.AddToClassList("hm-composer-body-hint");
            head.Add(_bodyHint);
            var spacer = new VisualElement();
            spacer.AddToClassList("hm-toolbar-spacer");
            head.Add(spacer);

            var format = new Button(FormatJson) { text = "Format JSON", tooltip = "Pretty-print the body; says so if it is not valid JSON" };
            format.AddToClassList("hm-small-button");
            head.Add(format);

            var clear = new Button(() =>
            {
                _bodyIsBinary = false;
                _bodyText = string.Empty;
                _request.Body = null;
                _body.SetValueWithoutNotify(string.Empty);
                UpdateBodyMode();
            }) { text = "Clear", tooltip = "Send no body" };
            clear.AddToClassList("hm-small-button");
            head.Add(clear);
            section.Add(head);

            _binaryNote = new VisualElement();
            _binaryNote.AddToClassList("hm-composer-binary");
            var binaryText = new Label();
            binaryText.AddToClassList("hm-muted");
            _binaryNote.Add(binaryText);
            section.Add(_binaryNote);

            _body = new TextField { multiline = true, value = _bodyText };
            _body.AddToClassList("hm-composer-body");
            _body.RegisterValueChangedCallback(e =>
            {
                _bodyText = e.newValue;
                _bodyNote.style.display = DisplayStyle.None;
            });
            section.Add(_body);

            _bodyNote = new Label();
            _bodyNote.AddToClassList("hm-body-note");
            _bodyNote.style.display = DisplayStyle.None;
            section.Add(_bodyNote);

            UpdateBodyMode();
            UpdateBodyHint();

            return section;
        }

        private void UpdateBodyMode()
        {
            _binaryNote.style.display = _bodyIsBinary ? DisplayStyle.Flex : DisplayStyle.None;
            _body.style.display = _bodyIsBinary ? DisplayStyle.None : DisplayStyle.Flex;

            if (_bodyIsBinary)
                _binaryNote.Q<Label>().text = $"Binary body, {RecordFormat.FormatBytes(_request.Body.Length)}, sent exactly as captured. Clear it to type a new one.";
        }

        private void UpdateBodyHint()
        {
            var contentType = ReplayHeaders.ContentType(_request.Headers);
            var method = ResolveMethod();
            var safe = method == "GET" || method == "HEAD" || method == "OPTIONS";

            _bodyHint.text = !string.IsNullOrEmpty(contentType) ? contentType
                : safe ? "no Content-Type; a body on a " + method + " is unusual"
                : "no Content-Type header: add one if the server needs it";
        }

        private VisualElement BuildResultBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("hm-composer-result");

            _result = new Label();
            _result.AddToClassList("hm-composer-result-text");
            bar.Add(_result);

            _showInList = new Button(() => { if (_lastRecord != null) HttpMonitorWindow.Instance?.SelectRuntime(_lastRecord); }) { text = "Show in list" };
            _showInList.AddToClassList("hm-small-button");
            _showInList.style.display = DisplayStyle.None;
            bar.Add(_showInList);

            return bar;
        }

        // ---------------------------------------------------------------- actions

        private string ResolveMethod()
        {
            if (_method == null)
                return (_request.Method ?? "GET").ToUpperInvariant();

            var chosen = _method.value == CustomMethod ? _customMethod.value : _method.value;

            return string.IsNullOrWhiteSpace(chosen) ? "GET" : chosen.Trim().ToUpperInvariant();
        }

        private void FormatJson()
        {
            if (_bodyIsBinary)
                return;

            if (JsonFormatter.TryFormat(_bodyText, out var pretty))
            {
                _bodyText = pretty;
                _body.SetValueWithoutNotify(pretty);
                _bodyNote.style.display = DisplayStyle.None;
            }
            else
            {
                _bodyNote.text = "Not valid JSON.";
                _bodyNote.style.display = DisplayStyle.Flex;
            }
        }

        private async void Send()
        {
            if (_inFlight != null)
                return;

            var request = BuildRequest();

            if (string.IsNullOrEmpty(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out _))
            {
                ShowResult("Enter an absolute URL first.", error: true);

                return;
            }

            if (request.HasRedactedHeaders)
            {
                ShowResult("Paste the real value for: " + string.Join(", ", request.RedactedHeaderNames), error: true);

                return;
            }

            if (!request.IsSafeMethod && ReplayController.ConfirmUnsafe && !ReplayController.Confirm(request))
                return;

            _inFlight = new CancellationTokenSource();
            SetSending(true);
            ShowResult("Sending…", error: false);

            var handle = ReplayController.Send(request);
            _lastRecord = handle.Record;

            try
            {
                var record = await handle.Completion;

                if (_result == null)
                    return; // window closed meanwhile

                var status = record.State == HttpRecordState.Completed
                    ? record.StatusCode + " " + RecordFormat.ReasonPhrase(record.StatusCode)
                    : record.State + (string.IsNullOrEmpty(record.Error) ? string.Empty : ": " + record.Error);

                ShowResult($"{status.Trim()}  ·  {record.DurationMs:F0} ms  ·  {RecordFormat.FormatBytes(record.DownloadedBytes)}", error: record.State != HttpRecordState.Completed);
                _showInList.style.display = DisplayStyle.Flex;
            }
            finally
            {
                _inFlight = null;

                if (_send != null)
                    SetSending(false);
            }
        }

        private void SetSending(bool sending)
        {
            _send.SetEnabled(!sending);
            _cancel.style.display = sending ? DisplayStyle.Flex : DisplayStyle.None;
            _showInList.style.display = DisplayStyle.None;
        }

        private void ShowResult(string text, bool error)
        {
            _result.text = text;
            _result.EnableInClassList("hm-composer-result-text--error", error);
        }
    }
}
