using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// The traffic window: toolbar, request table, detail pane, status bar. Reads the Editor store
    /// only; all capture happens in the runtime and reaches the store through the session bridge.
    /// </summary>
    public sealed class HttpMonitorWindow : EditorWindow
    {
        private const string MenuPath = "Window/Analysis/HTTP Monitor %#h";
        private const string StyleSheetPath = "Packages/com.captools.httpmonitor/Editor/UI/HttpMonitorWindow.uss";
        private const string LayoutPrefKey = "HttpMonitor.Window.ListOnLeft";
        private const string AutoScrollPrefKey = "HttpMonitor.Window.AutoScroll";

        private TwoPaneSplitView _split;
        private RecordListView _list;
        private VisualElement _empty;
        private Label _emptyText;
        private Label _detailPlaceholder;
        private Label _detailSummary;
        private Label _statusText;
        private VisualElement _recordDot;
        private ToolbarToggle _recordToggle;
        private ToolbarToggle _preserveToggle;
        private ToolbarButton _layoutButton;
        private bool _refreshScheduled;
        private string _hostFilter;

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
            minSize = new Vector2(480, 240);
        }

        private void OnDisable()
        {
            Unsubscribe();
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

            Unsubscribe();
            Store.Buffer.Changed += ScheduleRefresh;
            Store.Buffer.RecordUpdated += OnRecordUpdated;

            Refresh();
        }

        private void Unsubscribe()
        {
            Store.Buffer.Changed -= ScheduleRefresh;
            Store.Buffer.RecordUpdated -= OnRecordUpdated;
        }

        // ---------------------------------------------------------------- building

        private VisualElement BuildToolbar()
        {
            var toolbar = new Toolbar { name = "hm-toolbar" };
            toolbar.AddToClassList("hm-toolbar");

            _recordToggle = new ToolbarToggle { text = "Record", tooltip = "Capture requests. Off: requests pass through untouched and nothing is stored." };
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

            toolbar.Add(new ToolbarButton(() => Store.Clear()) { text = "Clear", tooltip = "Remove all captured requests" });

            _preserveToggle = new ToolbarToggle { text = "Preserve log", tooltip = "Keep requests from previous Play sessions instead of clearing when Play starts" };
            _preserveToggle.SetValueWithoutNotify(Store.PreserveLog);
            _preserveToggle.RegisterValueChangedCallback(e => Store.PreserveLog = e.newValue);
            toolbar.Add(_preserveToggle);

            var autoScroll = new ToolbarToggle { text = "Auto-scroll", tooltip = "Follow the newest request. Pauses while you scroll up or select an older row." };
            autoScroll.SetValueWithoutNotify(AutoScroll);
            autoScroll.RegisterValueChangedCallback(e =>
            {
                AutoScroll = e.newValue;
                _list.AutoScrollEnabled = e.newValue;
            });
            toolbar.Add(autoScroll);

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
            _split = new TwoPaneSplitView(0, 280, ListOnLeft ? TwoPaneSplitViewOrientation.Horizontal : TwoPaneSplitViewOrientation.Vertical);
            _split.AddToClassList("hm-split");

            var listPane = new VisualElement { name = "hm-list-pane" };
            listPane.AddToClassList("hm-list-pane");

            _list = new RecordListView { AutoScrollEnabled = AutoScroll };
            _list.SelectionChanged += ShowSelection;
            _list.FilterByHostRequested += host =>
            {
                _hostFilter = host;
                Refresh();
            };
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

        private void OnRecordUpdated(EditorRecord record)
        {
            // A pending row finishing is the hot path during Play: repaint that row only.
            _list?.RefreshRow(record);

            if (_list?.SelectedRecord == record)
                ShowSelection(record);

            UpdateStatusBar();
        }

        private void Refresh()
        {
            if (_list == null)
                return;

            _list.SetRecords(VisibleRecords());

            var total = Store.Buffer.Count;
            _empty.style.display = total == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _emptyText.text = Store.IsRecording
                ? "Press Play. Requests made with UnityWebRequest or HttpClient appear here automatically, no setup needed."
                : "Recording is paused. Turn on Record in the toolbar to capture requests.";

            _recordDot.EnableInClassList("hm-record-dot--paused", !Store.IsRecording);
            _recordToggle.SetValueWithoutNotify(Store.IsRecording);
            _preserveToggle.SetValueWithoutNotify(Store.PreserveLog);

            UpdateStatusBar();
            ShowSelection(_list.SelectedRecord);
        }

        /// <summary>The records the list shows. Step 4 replaces the host-only filter with the real filter bar.</summary>
        private IReadOnlyList<EditorRecord> VisibleRecords()
        {
            var all = Store.Buffer.Records;

            if (string.IsNullOrEmpty(_hostFilter))
                return all;

            var filtered = new List<EditorRecord>(all.Count);

            foreach (var record in all)
            {
                if (RecordFormat.Host(record.Url) == _hostFilter)
                    filtered.Add(record);
            }

            return filtered;
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

            var parts = new List<string>();
            var shown = _list?.Count ?? records.Count;

            parts.Add(shown == records.Count
                ? $"{records.Count} request{(records.Count == 1 ? "" : "s")}"
                : $"{shown} of {records.Count} requests shown (host: {_hostFilter})");

            if (pending > 0)
                parts.Add($"{pending} pending");

            if (errors > 0)
                parts.Add($"{errors} failed");

            parts.Add(RecordFormat.FormatBytes(Store.Buffer.StoredBodyBytes) + " of bodies");
            parts.Add(Store.IsRecording ? "Recording" : "Paused");

            _statusText.text = string.Join("  ·  ", parts);
        }

        private void ShowSelection(EditorRecord record)
        {
            _detailPlaceholder.style.display = record == null ? DisplayStyle.Flex : DisplayStyle.None;
            _detailSummary.style.display = record == null ? DisplayStyle.None : DisplayStyle.Flex;

            if (record == null)
                return;

            var status = RecordFormat.StatusText(record);
            var reason = RecordFormat.ReasonPhrase(record.StatusCode);

            var lines = new List<string>
            {
                $"{record.Method} {record.Url}",
                $"{status}{(string.IsNullOrEmpty(reason) ? "" : " " + reason)}  ·  {record.State}  ·  {RecordFormat.ClientText(record.Client)}  ·  {RecordFormat.SourceText(record.Source)}",
                $"Started {RecordFormat.FormatStarted(record)}  ·  {RecordFormat.FormatDuration(record)}  ·  ↑ {RecordFormat.FormatBytes(record.UploadedBytes)}  ↓ {RecordFormat.FormatBytes(record.DownloadedBytes)}",
            };

            if (!string.IsNullOrEmpty(record.Error))
                lines.Add("Error: " + record.Error);

            lines.Add($"{record.RequestHeaders.Length} request header(s), {record.ResponseHeaders.Length} response header(s). Full detail view arrives in M2 step 5.");

            _detailSummary.text = string.Join("\n", lines);
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
