using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// The traffic window: toolbar, filter bar, request table and detail pane in a split view,
    /// optional timeline, status bar. Reads the Editor store only; all capture happens in the
    /// runtime and reaches the store through the session bridge.
    ///
    /// Room to read: the split ratio is remembered per orientation, the timeline is resizable and
    /// collapsible, and the detail pane can be maximized (the list shrinks to a one-row strip that
    /// still answers Up/Down; Esc restores).
    /// </summary>
    public sealed class HttpMonitorWindow : EditorWindow
    {
        private const string MenuPath = "Window/Analysis/HTTP Monitor %#h";
        private const string StyleSheetPath = "Packages/com.captools.httpmonitor/Editor/UI/HttpMonitorWindow.uss";
        private const string LayoutPrefKey = "HttpMonitor.Window.ListOnLeft";
        private const string AutoScrollPrefKey = "HttpMonitor.Window.AutoScroll";
        private const string TimelinePrefKey = "HttpMonitor.Window.Timeline";
        private const string SplitVerticalPrefKey = "HttpMonitor.Window.Split.Vertical";
        private const string SplitHorizontalPrefKey = "HttpMonitor.Window.Split.Horizontal";
        private const float DefaultSplit = 280f;

        private readonly RecordQuery _query = new RecordQuery();

        private TwoPaneSplitView _split;
        private VisualElement _listPane;
        private FilterBar _filterBar;
        private TimelineView _timeline;
        private RecordListView _list;
        private VisualElement _empty;
        private Label _emptyTitle;
        private Label _emptyText;
        private DetailPane _detail;
        private VisualElement _maximized;
        private VisualElement _compactStrip;
        private VisualElement _compactDot;
        private Label _compactMethod;
        private Label _compactStatus;
        private Label _compactName;
        private DetailPane _maximizedDetail;
        private Label _statusText;
        private VisualElement _recordDot;
        private ToolbarToggle _recordToggle;
        private ToolbarToggle _preserveToggle;
        private ToolbarToggle _timelineToggle;
        private ToolbarButton _layoutButton;
        private bool _refreshScheduled;
        private bool _isMaximized;
        private List<EditorRecord> _visible = new List<EditorRecord>();

        /// <summary>The open main window, for popped-out detail windows that forward keyboard navigation.</summary>
        internal static HttpMonitorWindow Instance { get; private set; }

        /// <summary>The main window's selected record; popped-out follower windows track it.</summary>
        internal static EditorRecord CurrentSelection { get; private set; }

        internal static event Action<EditorRecord> SelectionChanged;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            GetWindow<HttpMonitorWindow>();
        }

        internal void SelectRelativeFromOutside(int delta)
        {
            _list?.SelectRelative(delta);
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

        private static float SavedSplit(bool listOnLeft)
        {
            return EditorPrefs.GetFloat(listOnLeft ? SplitHorizontalPrefKey : SplitVerticalPrefKey, DefaultSplit);
        }

        private static void SaveSplit(bool listOnLeft, float value)
        {
            EditorPrefs.SetFloat(listOnLeft ? SplitHorizontalPrefKey : SplitVerticalPrefKey, value);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("HTTP Monitor", LoadIcon());
            minSize = new Vector2(520, 260);
            Instance = this;
            DetailWindow.FollowerClosed += OnFollowerClosed;
        }

        private void OnDisable()
        {
            Unsubscribe();
            DetailWindow.FollowerClosed -= OnFollowerClosed;

            if (Instance == this)
                Instance = null;
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
            root.Add(BuildMaximized());
            root.Add(BuildTimeline());
            root.Add(BuildStatusBar());

            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            if (!EditorPrefs.HasKey(LayoutPrefKey))
                root.RegisterCallback<GeometryChangedEvent>(DecideDefaultLayout);

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

            var export = new ToolbarMenu { text = "HAR", tooltip = "Export the captured requests as a HAR 1.2 file (opens in Chrome, Firefox, Charles, Proxyman), or import one" };
            export.menu.AppendAction("Export all…", _ => ExportHar(Store.Buffer.Records, "all"), _ => Store.Buffer.Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            export.menu.AppendAction("Export shown…", _ => ExportHar(_visible, "shown"), _ => _query.IsFiltering && _visible.Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            export.menu.AppendSeparator();
            export.menu.AppendAction("Import…", _ => ImportHar());
            toolbar.Add(export);

            _layoutButton = new ToolbarButton(ToggleLayout) { tooltip = "Switch between list-above and list-left layouts" };
            UpdateLayoutButton();
            toolbar.Add(_layoutButton);

            toolbar.Add(new ToolbarButton(HttpMonitorSettingsProvider.Open) { text = "⚙", tooltip = "Project Settings ▸ HTTP Monitor: weaving, body caps, redacted headers, records kept" });
            toolbar.Add(new ToolbarButton(ShowShortcuts) { text = "?", tooltip = "Keyboard shortcuts" });

            return toolbar;
        }

        private static void ShowShortcuts()
        {
            EditorUtility.DisplayDialog("HTTP Monitor shortcuts",
                "Ctrl+Shift+H\topen this window\n" +
                "Ctrl+F\t\tfocus the filter, Esc clears it\n" +
                "Up / Down\tmove the selection (also while maximized or from a follower window)\n" +
                "Home / End\tfirst / newest request; End resumes auto-scroll\n" +
                "Enter\t\topen the detail (maximize, or focus the popped-out window)\n" +
                "Esc\t\trestore from maximized\n" +
                "Delete\t\tclear the selection\n" +
                "Ctrl+C\t\tcopy the URL\n" +
                "Ctrl+Shift+C\tcopy as cURL\n" +
                "Click a header\tsort; again for descending; again for arrival order\n" +
                "Right-click a row\tcopy, filter by host, pin, open in browser",
                "Close");
        }

        private VisualElement BuildSplit()
        {
            var listOnLeft = ListOnLeft;
            _split = new TwoPaneSplitView(0, SavedSplit(listOnLeft), listOnLeft ? TwoPaneSplitViewOrientation.Horizontal : TwoPaneSplitViewOrientation.Vertical);
            _split.AddToClassList("hm-split");

            _listPane = new VisualElement { name = "hm-list-pane" };
            _listPane.AddToClassList("hm-list-pane");
            _listPane.RegisterCallback<GeometryChangedEvent>(OnListPaneResized);

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
            _list.PinRequested += record => DetailWindow.OpenPinned(record, position);
            _list.DetailRequested += _ =>
            {
                // Enter: give the detail focus where it lives (popped out, maximized, or in the split).
                var follower = DetailWindow.Follower;

                if (follower != null)
                    follower.Focus();
                else if (!_isMaximized)
                    SetMaximized(true);
            };
            _listPane.Add(_list);

            _empty = new VisualElement { name = "hm-empty", pickingMode = PickingMode.Ignore };
            _empty.AddToClassList("hm-empty");
            _emptyTitle = new Label("No requests yet");
            _emptyTitle.AddToClassList("hm-empty-title");
            _emptyText = new Label();
            _emptyText.AddToClassList("hm-empty-text");
            _empty.Add(_emptyTitle);
            _empty.Add(_emptyText);
            _listPane.Add(_empty);

            _detail = new DetailPane();
            _detail.MaximizeToggled += () => SetMaximized(!_isMaximized);
            _detail.PopOutRequested += PopOut;
            _detail.DockBackRequested += DockBack;
            _detail.SetPoppedOut(DetailWindow.Follower != null); // a follower may have survived the domain reload

            _split.Add(_listPane);
            _split.Add(_detail);

            return _split;
        }

        // ---------------------------------------------------------------- pop-out

        private void PopOut()
        {
            if (_isMaximized)
                SetMaximized(false);

            DetailWindow.OpenFollower(_list.SelectedRecord, position);
            _detail.SetPoppedOut(true);
        }

        private void DockBack()
        {
            DetailWindow.CloseFollower();
            _detail.SetPoppedOut(false);
            Focus();
        }

        private void OnFollowerClosed()
        {
            // The user closed the popped-out window with its own X, or clicked Dock back there.
            _detail?.SetPoppedOut(false);
        }

        private void PinCurrent()
        {
            var record = _list.SelectedRecord;

            if (record != null)
                DetailWindow.OpenPinned(record, position);
        }

        /// <summary>The maximized layout: a one-row strip for the selection above a second detail pane. Hidden until used.</summary>
        private VisualElement BuildMaximized()
        {
            _maximized = new VisualElement { name = "hm-maximized" };
            _maximized.AddToClassList("hm-maximized");
            _maximized.style.display = DisplayStyle.None;

            _compactStrip = new VisualElement { name = "hm-compact-strip", tooltip = "Click to restore the list (Esc)" };
            _compactStrip.AddToClassList("hm-compact-strip");
            _compactStrip.RegisterCallback<ClickEvent>(_ => SetMaximized(false));

            _compactDot = new VisualElement();
            _compactDot.AddToClassList("hm-status-dot");
            _compactStrip.Add(_compactDot);

            _compactMethod = new Label();
            _compactMethod.AddToClassList("hm-compact-method");
            _compactStrip.Add(_compactMethod);

            _compactStatus = new Label();
            _compactStatus.AddToClassList("hm-compact-status");
            _compactStrip.Add(_compactStatus);

            _compactName = new Label();
            _compactName.AddToClassList("hm-compact-name");
            _compactStrip.Add(_compactName);

            var hint = new Label("↑ ↓ move selection   ·   Esc restore");
            hint.AddToClassList("hm-compact-hint");
            _compactStrip.Add(hint);

            _maximized.Add(_compactStrip);

            _maximizedDetail = new DetailPane();
            _maximizedDetail.SetMaximized(true);
            _maximizedDetail.MaximizeToggled += () => SetMaximized(false);
            _maximized.Add(_maximizedDetail);

            return _maximized;
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

        // ---------------------------------------------------------------- layout

        /// <summary>First open only: wide windows start side by side, tall ones stacked.</summary>
        private void DecideDefaultLayout(GeometryChangedEvent e)
        {
            var size = e.newRect.size;

            if (size.x <= 0 || size.y <= 0)
                return;

            rootVisualElement.UnregisterCallback<GeometryChangedEvent>(DecideDefaultLayout);

            var wide = size.x > size.y;

            if (wide != ListOnLeft)
                ToggleLayout();
            else
                ListOnLeft = wide; // write the key so the decision is made once
        }

        private void OnListPaneResized(GeometryChangedEvent e)
        {
            if (_isMaximized)
                return;

            var listOnLeft = ListOnLeft;
            var value = listOnLeft ? e.newRect.width : e.newRect.height;

            if (value > 0 && Mathf.Abs(SavedSplit(listOnLeft) - value) > 1f)
                SaveSplit(listOnLeft, value);
        }

        private void ToggleLayout()
        {
            var listOnLeft = !ListOnLeft;
            ListOnLeft = listOnLeft;
            _split.fixedPaneInitialDimension = SavedSplit(listOnLeft);
            _split.orientation = listOnLeft ? TwoPaneSplitViewOrientation.Horizontal : TwoPaneSplitViewOrientation.Vertical;
            UpdateLayoutButton();
        }

        private void UpdateLayoutButton()
        {
            _layoutButton.text = ListOnLeft ? "Layout: side by side" : "Layout: stacked";
        }

        private void SetMaximized(bool maximized)
        {
            if (_isMaximized == maximized)
                return;

            _isMaximized = maximized;
            _split.style.display = maximized ? DisplayStyle.None : DisplayStyle.Flex;
            _filterBar.style.display = maximized ? DisplayStyle.None : DisplayStyle.Flex;
            _maximized.style.display = maximized ? DisplayStyle.Flex : DisplayStyle.None;
            _detail.SetMaximized(maximized);
            UpdateTimelineVisibility();
            ShowSelection(_list.SelectedRecord);

            if (!maximized)
                _list.FocusList();
        }

        private void UpdateCompactStrip(EditorRecord record)
        {
            _compactDot.ClearClassList();
            _compactDot.AddToClassList("hm-status-dot");

            if (record == null)
            {
                _compactMethod.text = string.Empty;
                _compactStatus.text = string.Empty;
                _compactName.text = "No request selected";

                return;
            }

            _compactDot.AddToClassList(RecordFormat.StatusClass(record));
            _compactMethod.text = record.Method;
            _compactStatus.text = RecordFormat.StatusText(record);
            _compactStatus.EnableInClassList("hm-cell-status--error", record.IsFinished && record.IsError);
            _compactName.text = RecordFormat.Name(record.Url);
            _compactName.tooltip = record.Url;
        }

        private void UpdateTimelineVisibility()
        {
            _timeline.style.display = ShowTimeline && !_isMaximized ? DisplayStyle.Flex : DisplayStyle.None;
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

        private static void PublishSelection(EditorRecord record)
        {
            if (ReferenceEquals(CurrentSelection, record))
                return;

            CurrentSelection = record;
            SelectionChanged?.Invoke(record);
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
            PublishSelection(record);

            if (_isMaximized)
            {
                _maximizedDetail.Show(record);
                UpdateCompactStrip(record);
            }
            else
            {
                _detail.Show(record);
            }
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.F && e.actionKey)
            {
                if (_isMaximized)
                    SetMaximized(false);

                _filterBar.FocusSearch();
                e.StopPropagation();

                return;
            }

            if (!_isMaximized)
                return;

            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    SetMaximized(false);
                    e.StopPropagation();

                    break;
                case KeyCode.UpArrow:
                    _list.SelectRelative(-1);
                    e.StopPropagation();

                    break;
                case KeyCode.DownArrow:
                    _list.SelectRelative(1);
                    e.StopPropagation();

                    break;
            }
        }

        // ---------------------------------------------------------------- HAR

        private void ExportHar(IReadOnlyList<EditorRecord> records, string what)
        {
            var name = $"http-monitor-{DateTime.Now:yyyyMMdd-HHmmss}.har";
            var path = EditorUtility.SaveFilePanel("Export HAR", string.Empty, name, "har");

            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                File.WriteAllText(path, HarWriter.Write(records, PackageVersion()), new UTF8Encoding(false));
                ShowNotification(new GUIContent($"Exported {records.Count} {what} request{(records.Count == 1 ? "" : "s")}"));
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Export failed", e.Message, "OK");
            }
        }

        private void ImportHar()
        {
            var path = EditorUtility.OpenFilePanel("Import HAR", string.Empty, "har");

            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                var records = HarReader.Read(File.ReadAllText(path));
                Store.Buffer.AddImported(records);
                ShowNotification(new GUIContent($"Imported {records.Count} request{(records.Count == 1 ? "" : "s")}"));
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Import failed", e.Message, "OK");
            }
        }

        private static string PackageVersion()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HttpMonitorWindow).Assembly);

            return info != null ? info.version : "0.0.0";
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
