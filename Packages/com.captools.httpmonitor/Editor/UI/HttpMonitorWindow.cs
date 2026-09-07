using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// The traffic window: toolbar, filter bar, optional timeline, request table, detail pane,
    /// status bar. Reads the Editor store only; all capture happens in the runtime and reaches the
    /// store through the session bridge.
    /// </summary>
    public sealed class HttpMonitorWindow : EditorWindow
    {
        private const string MenuPath = "Window/Analysis/HTTP Monitor %#h";
        private const string StyleSheetPath = "Packages/com.captools.httpmonitor/Editor/UI/HttpMonitorWindow.uss";
        private const string LayoutPrefKey = "HttpMonitor.Window.ListOnLeft";
        private const string AutoScrollPrefKey = "HttpMonitor.Window.AutoScroll";
        private const string TimelinePrefKey = "HttpMonitor.Window.Timeline";

        private readonly RecordQuery _query = new RecordQuery();

        private TwoPaneSplitView _split;
        private FilterBar _filterBar;
        private TimelineView _timeline;
        private RecordListView _list;
        private VisualElement _empty;
        private Label _emptyTitle;
        private Label _emptyText;
        private Label _detailPlaceholder;
        private Label _detailSummary;
        private Label _statusText;
        private VisualElement _recordDot;
        private ToolbarToggle _recordToggle;
        private ToolbarToggle _preserveToggle;
        private ToolbarToggle _timelineToggle;
        private ToolbarButton _layoutButton;
        private bool _refreshScheduled;
        private List<EditorRecord> _visible = new List<EditorRecord>();

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

        private static bool ShowTimeline
        {
            get => EditorPrefs.GetBool(TimelinePrefKey, false);
            set => EditorPrefs.SetBool(TimelinePrefKey, value);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("HTTP Monitor", LoadIcon());
            minSize = new Vector2(520, 260);
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

            _filterBar = new FilterBar(_query);
            _filterBar.Changed += OnQueryChanged;
            root.Add(_filterBar);

            root.Add(BuildSplit());
            root.Add(BuildTimeline());
            root.Add(BuildStatusBar());

            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            Unsubscribe();
            Store.Buffer.Changed += ScheduleRefresh;
            Store.Buffer.RecordUpdated += OnRecordUpdated;

            // The header may come back already sorted from persisted view data.
            _list.GetSort(out var sortBy, out var descending);
            _query.SortBy = sortBy;
            _query.SortDescending = descending;

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

            _timelineToggle = new ToolbarToggle { text = "Timeline", tooltip = "Show the requests as bars on a time axis at the bottom of the window" };
            _timelineToggle.SetValueWithoutNotify(ShowTimeline);
            _timelineToggle.RegisterValueChangedCallback(e =>
            {
                ShowTimeline = e.newValue;
                UpdateTimelineVisibility();
            });
            toolbar.Add(_timelineToggle);

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
            _list.SelectionChanged += OnSelectionChanged;
            _list.SortChanged += (column, descending) =>
            {
                _query.SortBy = column;
                _query.SortDescending = descending;
                OnQueryChanged();
            };
            _list.FilterByHostRequested += host =>
            {
                _query.Host = host;
                _filterBar.SyncFromQuery();
                OnQueryChanged();
            };
            listPane.Add(_list);

            _empty = new VisualElement { name = "hm-empty", pickingMode = PickingMode.Ignore };
            _empty.AddToClassList("hm-empty");
            _emptyTitle = new Label("No requests yet");
            _emptyTitle.AddToClassList("hm-empty-title");
            _emptyText = new Label();
            _emptyText.AddToClassList("hm-empty-text");
            _empty.Add(_emptyTitle);
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

        /// <summary>The timeline sits under the split, full width, whatever the layout orientation.</summary>
        private VisualElement BuildTimeline()
        {
            _timeline = new TimelineView();
            _timeline.BarClicked += record => _list.Select(record);
            UpdateTimelineVisibility();

            return _timeline;
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

        private void OnQueryChanged()
        {
            _filterBar.SyncFromQuery();
            Refresh();
        }

        private void OnRecordUpdated(EditorRecord record)
        {
            // A pending row finishing is the hot path during Play: repaint that row only, unless the
            // change moves it in or out of the current filter or sort, in which case rebuild.
            var wasVisible = _visible.Contains(record);
            var isVisible = _query.Matches(record);

            if (wasVisible != isVisible || _query.SortBy != SortColumn.Arrival)
            {
                ScheduleRefresh();

                return;
            }

            _list?.RefreshRow(record);
            _timeline?.RefreshRecord(record);

            if (_list?.SelectedRecord == record)
                ShowSelection(record);

            UpdateStatusBar();
        }

        private void OnSelectionChanged(EditorRecord record)
        {
            _timeline.SetSelected(record);
            ShowSelection(record);
        }

        private void Refresh()
        {
            if (_list == null)
                return;

            var sorted = _query.SortBy != SortColumn.Arrival;
            var filtered = _query.Filter(Store.Buffer.Records);
            _visible = sorted ? _query.Apply(Store.Buffer.Records) : filtered;

            _list.SetRecords(_visible, sorted);
            _timeline.SetRecords(filtered); // time order, whatever the table is sorted by
            _filterBar.SetAvailableMethods(MethodsSeen());

            var total = Store.Buffer.Count;
            var showEmpty = _visible.Count == 0;
            _empty.style.display = showEmpty ? DisplayStyle.Flex : DisplayStyle.None;

            if (showEmpty)
            {
                if (total > 0)
                {
                    _emptyTitle.text = "No requests match";
                    _emptyText.text = $"{total} request{(total == 1 ? "" : "s")} hidden by the current filters ({_query.Describe()}).";
                }
                else
                {
                    _emptyTitle.text = "No requests yet";
                    _emptyText.text = Store.IsRecording
                        ? "Press Play. Requests made with UnityWebRequest or HttpClient appear here automatically, no setup needed."
                        : "Recording is paused. Turn on Record in the toolbar to capture requests.";
                }
            }

            _recordDot.EnableInClassList("hm-record-dot--paused", !Store.IsRecording);
            _recordToggle.SetValueWithoutNotify(Store.IsRecording);
            _preserveToggle.SetValueWithoutNotify(Store.PreserveLog);

            UpdateStatusBar();
            _timeline.SetSelected(_list.SelectedRecord);
            ShowSelection(_list.SelectedRecord);
        }

        private IEnumerable<string> MethodsSeen()
        {
            var seen = new SortedSet<string>();

            foreach (var record in Store.Buffer.Records)
            {
                if (!string.IsNullOrEmpty(record.Method))
                    seen.Add(record.Method.ToUpperInvariant());
            }

            return seen;
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

            if (_query.IsFiltering)
                parts.Add($"{_visible.Count} of {records.Count} shown ({_query.Describe()})");
            else
                parts.Add($"{records.Count} request{(records.Count == 1 ? "" : "s")}");

            if (_query.SortBy != SortColumn.Arrival)
                parts.Add($"sorted by {_query.SortBy.ToString().ToLowerInvariant()}{(_query.SortDescending ? " ↓" : " ↑")}");

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

        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.F && e.actionKey)
            {
                _filterBar.FocusSearch();
                e.StopPropagation();
            }
        }

        private void UpdateTimelineVisibility()
        {
            _timeline.style.display = ShowTimeline ? DisplayStyle.Flex : DisplayStyle.None;
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
