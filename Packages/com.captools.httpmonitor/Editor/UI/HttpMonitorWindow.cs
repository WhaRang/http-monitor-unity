using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// The traffic window. M2 step 2 skeleton: toolbar, split view with a simple request list and a
    /// detail placeholder, status bar, empty state, theme-aware styling, remembered layout.
    /// </summary>
    public sealed class HttpMonitorWindow : EditorWindow
    {
        private const string MenuPath = "Window/Analysis/HTTP Monitor %#h";
        private const string StyleSheetPath = "Packages/com.captools.httpmonitor/Editor/UI/HttpMonitorWindow.uss";
        private const string LayoutPrefKey = "HttpMonitor.Window.ListOnLeft";
        private const string AutoScrollPrefKey = "HttpMonitor.Window.AutoScroll";

        private readonly List<EditorRecord> _view = new List<EditorRecord>();

        private TwoPaneSplitView _split;
        private ListView _list;
        private VisualElement _empty;
        private Label _emptyText;
        private Label _detailPlaceholder;
        private Label _detailSummary;
        private Label _statusText;
        private VisualElement _recordDot;
        private ToolbarToggle _recordToggle;
        private ToolbarToggle _preserveToggle;
        private ToolbarToggle _autoScrollToggle;
        private ToolbarButton _layoutButton;
        private bool _refreshScheduled;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            GetWindow<HttpMonitorWindow>();
        }

        private static EditorRecordStore Store => EditorRecordStore.instance;

        private static bool ListOnLeft
        {
            get => EditorPrefs.GetBool(LayoutPrefKey, false);
            set => EditorPrefs.SetBool(LayoutPrefKey, value);
        }

        private static bool AutoScroll
        {
            get => EditorPrefs.GetBool(AutoScrollPrefKey, true);
            set => EditorPrefs.SetBool(AutoScrollPrefKey, value);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("HTTP Monitor", LoadIcon());
            minSize = new Vector2(420, 240);
        }

        private void OnDisable()
        {
            Store.Buffer.Changed -= ScheduleRefresh;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("hm-root");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "hm-dark" : "hm-light");

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);

            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            root.Add(BuildToolbar());
            root.Add(BuildSplit());
            root.Add(BuildStatusBar());

            Store.Buffer.Changed -= ScheduleRefresh;
            Store.Buffer.Changed += ScheduleRefresh;

            Refresh();
        }

        // ---------------------------------------------------------------- building

        private VisualElement BuildToolbar()
        {
            var toolbar = new Toolbar { name = "hm-toolbar" };
            toolbar.AddToClassList("hm-toolbar");

            _recordToggle = new ToolbarToggle { text = "Record", tooltip = "Capture requests (Ctrl+Shift+H opens this window)" };
            _recordDot = new VisualElement();
            _recordDot.AddToClassList("hm-record-dot");
            _recordToggle.Insert(0, _recordDot);
            _recordToggle.SetValueWithoutNotify(Store.IsRecording);
            _recordToggle.RegisterValueChangedCallback(e =>
            {
                Store.IsRecording = e.newValue;
                Refresh();
            });
            toolbar.Add(_recordToggle);

            var clear = new ToolbarButton(() => Store.Clear()) { text = "Clear", tooltip = "Remove all captured requests" };
            toolbar.Add(clear);

            _preserveToggle = new ToolbarToggle { text = "Preserve log", tooltip = "Keep requests from previous Play sessions instead of clearing when Play starts" };
            _preserveToggle.SetValueWithoutNotify(Store.PreserveLog);
            _preserveToggle.RegisterValueChangedCallback(e => Store.PreserveLog = e.newValue);
            toolbar.Add(_preserveToggle);

            _autoScrollToggle = new ToolbarToggle { text = "Auto-scroll", tooltip = "Follow the newest request" };
            _autoScrollToggle.SetValueWithoutNotify(AutoScroll);
            _autoScrollToggle.RegisterValueChangedCallback(e =>
            {
                AutoScroll = e.newValue;

                if (e.newValue)
                    ScrollToLatest();
            });
            toolbar.Add(_autoScrollToggle);

            var spacer = new VisualElement();
            spacer.AddToClassList("hm-toolbar-spacer");
            toolbar.Add(spacer);

            _layoutButton = new ToolbarButton(ToggleLayout) { tooltip = "Switch between list-above and list-left layouts" };
            UpdateLayoutButton();
            toolbar.Add(_layoutButton);

            return toolbar;
        }

        private VisualElement BuildSplit()
        {
            _split = new TwoPaneSplitView(0, 260, ListOnLeft ? TwoPaneSplitViewOrientation.Horizontal : TwoPaneSplitViewOrientation.Vertical);
            _split.AddToClassList("hm-split");

            var listPane = new VisualElement { name = "hm-list-pane" };
            listPane.AddToClassList("hm-list-pane");

            _list = new ListView(_view, 20, MakeRow, BindRow)
            {
                name = "hm-list",
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
            };
            _list.AddToClassList("hm-list");
            _list.selectionChanged += _ => ShowSelection();
            listPane.Add(_list);

            _empty = new VisualElement { name = "hm-empty", pickingMode = PickingMode.Ignore };
            _empty.AddToClassList("hm-empty");
            var emptyTitle = new Label("No requests yet");
            emptyTitle.AddToClassList("hm-empty-title");
            _emptyText = new Label();
            _emptyText.AddToClassList("hm-empty-text");
            _empty.Add(emptyTitle);
            _empty.Add(_emptyText);
            listPane.Add(_empty);

            var detailPane = new VisualElement { name = "hm-detail-pane" };
            detailPane.AddToClassList("hm-detail-pane");
            _detailPlaceholder = new Label("Select a request to see its headers and body.");
            _detailPlaceholder.AddToClassList("hm-detail-placeholder");
            _detailSummary = new Label { style = { display = DisplayStyle.None } };
            _detailSummary.AddToClassList("hm-detail-summary");
            detailPane.Add(_detailPlaceholder);
            detailPane.Add(_detailSummary);

            _split.Add(listPane);
            _split.Add(detailPane);

            return _split;
        }

        private VisualElement BuildStatusBar()
        {
            var bar = new VisualElement { name = "hm-statusbar" };
            bar.AddToClassList("hm-statusbar");
            _statusText = new Label();
            _statusText.AddToClassList("hm-statusbar-text");
            bar.Add(_statusText);

            return bar;
        }

        private static VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("hm-row");

            var dot = new VisualElement { name = "dot" };
            dot.AddToClassList("hm-status-dot");
            row.Add(dot);

            var method = new Label { name = "method" };
            method.AddToClassList("hm-row-method");
            row.Add(method);

            var status = new Label { name = "status" };
            status.AddToClassList("hm-row-status");
            row.Add(status);

            var url = new Label { name = "url" };
            url.AddToClassList("hm-row-url");
            row.Add(url);

            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _view.Count)
                return;

            var record = _view[index];

            row.Q<Label>("method").text = record.Method;
            row.Q<Label>("status").text = StatusText(record);
            row.Q<Label>("url").text = record.Url;
            row.tooltip = record.IsFinished && !string.IsNullOrEmpty(record.Error) ? record.Error : record.Url;

            var dot = row.Q("dot");
            dot.ClearClassList();
            dot.AddToClassList("hm-status-dot");
            dot.AddToClassList(StatusClass(record));
        }

        // ---------------------------------------------------------------- state

        private void ScheduleRefresh()
        {
            if (_refreshScheduled)
                return;

            _refreshScheduled = true;
            rootVisualElement.schedule.Execute(() =>
            {
                _refreshScheduled = false;
                Refresh();
            });
        }

        private void Refresh()
        {
            if (_list == null)
                return;

            var selectedId = SelectedRecord()?.Id ?? -1;
            var wasAtEnd = _view.Count == 0 || _list.selectedIndex == _view.Count - 1;

            _view.Clear();
            _view.AddRange(Store.Buffer.Records);
            _list.RefreshItems();

            if (selectedId >= 0)
            {
                var index = _view.FindIndex(r => r.Id == selectedId);
                _list.SetSelectionWithoutNotify(index >= 0 ? new[] { index } : new int[0]);
            }

            _empty.style.display = _view.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _emptyText.text = Store.IsRecording
                ? "Press Play. Requests made with UnityWebRequest or HttpClient appear here automatically, no setup needed."
                : "Recording is paused. Turn on Record in the toolbar to capture requests.";

            _recordDot.EnableInClassList("hm-record-dot--paused", !Store.IsRecording);
            _recordToggle.SetValueWithoutNotify(Store.IsRecording);
            _preserveToggle.SetValueWithoutNotify(Store.PreserveLog);

            UpdateStatusBar();
            ShowSelection();

            if (AutoScroll && (wasAtEnd || selectedId < 0))
                ScrollToLatest();
        }

        private void UpdateStatusBar()
        {
            var records = Store.Buffer.Records;
            var errors = 0;
            var pending = 0;

            foreach (var record in records)
            {
                if (!record.IsFinished)
                    pending++;
                else if (record.IsError)
                    errors++;
            }

            var parts = new List<string> { $"{records.Count} request{(records.Count == 1 ? "" : "s")}" };

            if (pending > 0)
                parts.Add($"{pending} pending");

            if (errors > 0)
                parts.Add($"{errors} failed");

            parts.Add(FormatBytes(Store.Buffer.StoredBodyBytes) + " of bodies");
            parts.Add(Store.IsRecording ? "Recording" : "Paused");

            _statusText.text = string.Join("  ·  ", parts);
        }

        private void ShowSelection()
        {
            var record = SelectedRecord();

            _detailPlaceholder.style.display = record == null ? DisplayStyle.Flex : DisplayStyle.None;
            _detailSummary.style.display = record == null ? DisplayStyle.None : DisplayStyle.Flex;

            if (record == null)
                return;

            var lines = new List<string>
            {
                $"{record.Method} {record.Url}",
                $"{StatusText(record)}  ·  {record.State}  ·  {record.Client}  ·  {SourceText(record.Source)}",
                $"Started {record.StartedAtUtc.ToLocalTime():HH:mm:ss.fff}  ·  {record.DurationMs:F0} ms  ·  ↑ {FormatBytes(record.UploadedBytes)}  ↓ {FormatBytes(record.DownloadedBytes)}",
            };

            if (!string.IsNullOrEmpty(record.Error))
                lines.Add("Error: " + record.Error);

            lines.Add($"{record.RequestHeaders.Length} request header(s), {record.ResponseHeaders.Length} response header(s). Full detail view arrives in M2 step 5.");

            _detailSummary.text = string.Join("\n", lines);
        }

        private EditorRecord SelectedRecord()
        {
            if (_list == null)
                return null;

            var index = _list.selectedIndex;

            return index >= 0 && index < _view.Count ? _view[index] : null;
        }

        private void ScrollToLatest()
        {
            if (_view.Count > 0)
                _list.ScrollToItem(_view.Count - 1);
        }

        private void ToggleLayout()
        {
            ListOnLeft = !ListOnLeft;
            _split.orientation = ListOnLeft ? TwoPaneSplitViewOrientation.Horizontal : TwoPaneSplitViewOrientation.Vertical;
            UpdateLayoutButton();
        }

        private void UpdateLayoutButton()
        {
            _layoutButton.text = ListOnLeft ? "Layout: side by side" : "Layout: stacked";
        }

        // ---------------------------------------------------------------- formatting

        internal static string StatusText(EditorRecord record)
        {
            switch (record.State)
            {
                case HttpRecordState.Pending: return "…";
                case HttpRecordState.Failed: return "failed";
                case HttpRecordState.Aborted: return "aborted";
                case HttpRecordState.Incomplete: return "?";
                default: return record.StatusCode.ToString();
            }
        }

        internal static string StatusClass(EditorRecord record)
        {
            switch (record.State)
            {
                case HttpRecordState.Pending: return "hm-status-dot--pending";
                case HttpRecordState.Failed: return "hm-status-dot--failed";
                case HttpRecordState.Aborted:
                case HttpRecordState.Incomplete: return "hm-status-dot--aborted";
            }

            if (record.StatusCode >= 500) return "hm-status-dot--5xx";
            if (record.StatusCode >= 400) return "hm-status-dot--4xx";
            if (record.StatusCode >= 300) return "hm-status-dot--3xx";
            if (record.StatusCode >= 200) return "hm-status-dot--2xx";

            return string.Empty;
        }

        internal static string SourceText(HttpCaptureSource source)
        {
            switch (source)
            {
                case HttpCaptureSource.Woven: return "automatic";
                case HttpCaptureSource.Manual: return "manual";
                case HttpCaptureSource.Woven | HttpCaptureSource.Manual: return "automatic + manual";
                default: return "unknown source";
            }
        }

        internal static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";

            return (bytes / (1024.0 * 1024.0)).ToString("F1") + " MB";
        }

        private static Texture2D LoadIcon()
        {
            try
            {
                return EditorGUIUtility.IconContent("d_Profiler.NetworkMessages").image as Texture2D;
            }
            catch
            {
                return null;
            }
        }
    }
}
