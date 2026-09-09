using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// The request table. Owns the item list the <see cref="MultiColumnListView"/> is bound to and
    /// everything about rows: columns, cell binding, selection that survives inserts and evictions,
    /// DevTools-style auto-scroll, header-click sorting, and the row context menu. The window feeds
    /// it records; it never reads the store itself, so it works the same over a filtered view.
    ///
    /// Sorting uses the header's built-in machinery (<see cref="ColumnSortingMode.Custom"/>): Unity
    /// owns the click handling and the arrow indicator and cycles ascending → descending → off; we
    /// translate its sort description into a <see cref="SortColumn"/> and let the query do the work.
    /// </summary>
    internal sealed class RecordListView : VisualElement
    {
        private const string ViewDataKey = "HttpMonitor.RecordList";
        private const float RowHeight = 20f;

        private readonly List<EditorRecord> _items = new List<EditorRecord>();
        private readonly MultiColumnListView _list;
        private readonly Button _jumpToLatest;
        private readonly Dictionary<string, SortColumn> _sortByColumnName = new Dictionary<string, SortColumn>();

        private long _selectedId = -1;
        private bool _followLatest = true;
        private bool _userScrolled;
        private bool _autoScrollEnabled = true;
        private bool _sorted;

        /// <summary>Fired when the selected record changes, with null when nothing is selected.</summary>
        public event Action<EditorRecord> SelectionChanged;

        /// <summary>Fired with a host name when the user picks "Filter by host" from the context menu.</summary>
        public event Action<string> FilterByHostRequested;

        /// <summary>Fired when the user changes the header sort. <see cref="SortColumn.Arrival"/> means unsorted.</summary>
        public event Action<SortColumn, bool> SortChanged;

        public RecordListView()
        {
            AddToClassList("hm-record-list");
            style.flexGrow = 1;

            _list = new MultiColumnListView
            {
                name = "hm-list",
                viewDataKey = ViewDataKey,
                fixedItemHeight = RowHeight,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                sortingMode = ColumnSortingMode.Custom,
                reorderable = false,
                itemsSource = _items,
            };
            _list.AddToClassList("hm-list");
            _list.selectionChanged += OnSelectionChanged;
            _list.columnSortingChanged += OnColumnSortingChanged;
            _list.RegisterCallback<WheelEvent>(_ => OnUserScrolled(), TrickleDown.TrickleDown);
            _list.RegisterCallback<KeyDownEvent>(OnKeyDown);
            _list.RegisterCallback<ContextualMenuPopulateEvent>(PopulateContextMenu);

            AddColumns();
            Add(_list);

            _jumpToLatest = new Button(() => { Follow(true); ScrollToLatest(); }) { text = "↓ Jump to latest" };
            _jumpToLatest.AddToClassList("hm-jump-latest");
            _jumpToLatest.style.display = DisplayStyle.None;
            Add(_jumpToLatest);

            // The scroller and the header cells only exist after the first layout pass.
            _list.RegisterCallback<GeometryChangedEvent>(HookAfterLayout);
        }

        public EditorRecord SelectedRecord => _list.selectedIndex >= 0 && _list.selectedIndex < _items.Count ? _items[_list.selectedIndex] : null;

        public int Count => _items.Count;

        /// <summary>Master switch from the toolbar. Off means never follow; on means follow unless the user scrolled up.</summary>
        public bool AutoScrollEnabled
        {
            get => _autoScrollEnabled;
            set
            {
                _autoScrollEnabled = value;

                if (value)
                {
                    Follow(true);
                    ScrollToLatest();
                }
                else
                {
                    _jumpToLatest.style.display = DisplayStyle.None;
                }
            }
        }

        /// <summary>Replaces the rows. Selection is kept by record id; the view scrolls to the end when following an unsorted list.</summary>
        public void SetRecords(IReadOnlyList<EditorRecord> records, bool sorted = false)
        {
            _sorted = sorted;
            _items.Clear();
            _items.AddRange(records);
            _list.RefreshItems();

            RestoreSelection();

            if (_followLatest && _autoScrollEnabled && !sorted)
                ScrollToLatest();
        }

        /// <summary>Cheap refresh for in-place changes (a pending row finished).</summary>
        public void RefreshRow(EditorRecord record)
        {
            var index = _items.IndexOf(record);

            if (index >= 0)
                _list.RefreshItem(index);
        }

        public void Select(EditorRecord record)
        {
            var index = _items.IndexOf(record);

            if (index < 0)
                return;

            _list.SetSelection(index);
            _list.ScrollToItem(index);
        }

        public void ClearSelection()
        {
            _selectedId = -1;
            _list.ClearSelection();
        }

        /// <summary>Keyboard focus to the table itself (Focus() on this element is sealed by UI Toolkit).</summary>
        public void FocusList()
        {
            _list.Focus();
        }

        /// <summary>
        /// The sort the header currently shows. Unity persists it with the view data, so after a
        /// domain reload the header may already be sorted before the query knows.
        /// </summary>
        public void GetSort(out SortColumn column, out bool descending)
        {
            column = SortColumn.Arrival;
            descending = false;

            var descriptions = _list.sortColumnDescriptions;

            if (descriptions == null || descriptions.Count == 0)
                return;

            // Only the primary sort is honoured; arrival order is always the tiebreaker.
            var primary = descriptions[0];

            if (_sortByColumnName.TryGetValue(primary.columnName ?? string.Empty, out var mapped))
            {
                column = mapped;
                descending = primary.direction == SortDirection.Descending;
            }
        }

        private void OnColumnSortingChanged()
        {
            GetSort(out var column, out var descending);
            SortChanged?.Invoke(column, descending);
        }

        // ---------------------------------------------------------------- columns

        private void AddColumns()
        {
            var columns = _list.columns;

            columns.Add(Column("dot", string.Empty, SortColumn.Status, 22, 22, 22, MakeDot, BindDot, stretch: false, resizable: false));
            columns.Add(Column("method", "Method", SortColumn.Method, 58, 44, 90, MakeLabel("hm-cell-method"), (e, r) => Set(e, r.Method), stretch: false));
            columns.Add(Column("status", "Status", SortColumn.Status, 56, 44, 90, MakeLabel("hm-cell-status"), BindStatus, stretch: false));
            columns.Add(Column("source", "Src", SortColumn.Source, 44, 36, 60, MakeSource, BindSource, stretch: false));
            columns.Add(Column("name", "Name", SortColumn.Name, 260, 80, 2000, MakeLabel("hm-cell"), BindName, stretch: true));
            columns.Add(Column("host", "Host", SortColumn.Host, 150, 60, 600, MakeLabel("hm-cell"), (e, r) => Set(e, RecordFormat.Host(r.Url), r.Url), stretch: false));
            columns.Add(Column("type", "Type", SortColumn.Type, 70, 40, 160, MakeLabel("hm-cell"), BindType, stretch: false));
            columns.Add(Column("size", "Size", SortColumn.Size, 70, 44, 120, MakeLabel("hm-cell-right"), BindSize, stretch: false));
            columns.Add(Column("time", "Time", SortColumn.Time, 70, 44, 120, MakeLabel("hm-cell-right"), (e, r) => Set(e, RecordFormat.FormatDuration(r)), stretch: false));
            columns.Add(Column("started", "Started", SortColumn.Started, 90, 60, 140, MakeLabel("hm-cell"), (e, r) => Set(e, RecordFormat.FormatStarted(r), r.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff")), stretch: false));
        }

        private Column Column(string name, string title, SortColumn sort, float width, float min, float max,
            Func<VisualElement> make, Action<VisualElement, EditorRecord> bind, bool stretch, bool resizable = true)
        {
            _sortByColumnName[name] = sort;

            return new Column
            {
                name = name,
                title = title,
                width = width,
                minWidth = min,
                maxWidth = max,
                stretchable = stretch,
                resizable = resizable,
                sortable = true,
                makeCell = make,
                bindCell = (element, index) =>
                {
                    if (index >= 0 && index < _items.Count)
                        bind(element, _items[index]);
                },
            };
        }

        private static Func<VisualElement> MakeLabel(string className)
        {
            return () =>
            {
                var label = new Label();
                label.AddToClassList("hm-cell");
                label.AddToClassList(className);

                return label;
            };
        }

        private static void Set(VisualElement element, string text, string tooltip = null)
        {
            var label = (Label)element;
            label.text = text;
            label.tooltip = tooltip ?? text;
        }

        private static VisualElement MakeDot()
        {
            var container = new VisualElement();
            container.AddToClassList("hm-cell-dot");
            var dot = new VisualElement { name = "dot" };
            dot.AddToClassList("hm-status-dot");
            container.Add(dot);

            return container;
        }

        private static void BindDot(VisualElement element, EditorRecord record)
        {
            var dot = element.Q("dot");
            dot.ClearClassList();
            dot.AddToClassList("hm-status-dot");
            dot.AddToClassList(RecordFormat.StatusClass(record));
            element.tooltip = RecordFormat.StatusTooltip(record);
        }

        private static void BindStatus(VisualElement element, EditorRecord record)
        {
            var label = (Label)element;
            label.text = RecordFormat.StatusText(record);
            label.tooltip = RecordFormat.StatusTooltip(record);
            label.EnableInClassList("hm-cell-status--error", record.IsFinished && record.IsError);
        }

        private static VisualElement MakeSource()
        {
            var container = new VisualElement();
            container.AddToClassList("hm-cell-source");
            var badge = new Label { name = "badge" };
            badge.AddToClassList("hm-source-badge");
            container.Add(badge);

            return container;
        }

        private static void BindSource(VisualElement element, EditorRecord record)
        {
            var badge = element.Q<Label>("badge");
            badge.text = RecordFormat.SourceBadge(record);
            badge.ClearClassList();
            badge.AddToClassList("hm-source-badge");
            badge.AddToClassList(RecordFormat.SourceClass(record));
            element.tooltip = "Captured: " + RecordFormat.SourceText(record);
        }

        private static void BindName(VisualElement element, EditorRecord record)
        {
            Set(element, RecordFormat.Name(record.Url), record.Url);
        }

        private static void BindType(VisualElement element, EditorRecord record)
        {
            var contentType = RecordFormat.HeaderValue(record.ResponseHeaders, "Content-Type");
            Set(element, RecordFormat.ShortType(record), string.IsNullOrEmpty(contentType) ? "No Content-Type in the response" : contentType);
        }

        private static void BindSize(VisualElement element, EditorRecord record)
        {
            var text = record.IsFinished ? RecordFormat.FormatBytes(record.DownloadedBytes) : "…";
            var tooltip = record.IsFinished
                ? $"Downloaded {record.DownloadedBytes:N0} B, uploaded {record.UploadedBytes:N0} B"
                : "Waiting for a response";

            Set(element, text, tooltip);
        }

        // ---------------------------------------------------------------- selection and scrolling

        private void OnSelectionChanged(IEnumerable<object> _)
        {
            var selected = SelectedRecord;
            _selectedId = selected?.Id ?? -1;

            // Selecting a row is a "stop moving" gesture, like in dev tools; selecting the last row keeps following.
            if (selected != null && _list.selectedIndex != _items.Count - 1)
                Follow(false);

            SelectionChanged?.Invoke(selected);
        }

        private void RestoreSelection()
        {
            if (_selectedId < 0)
                return;

            var index = _items.FindIndex(r => r.Id == _selectedId);

            if (index >= 0)
            {
                _list.SetSelectionWithoutNotify(new[] { index });
            }
            else
            {
                _selectedId = -1;
                _list.ClearSelection();
                SelectionChanged?.Invoke(null);
            }
        }

        private void HookAfterLayout(GeometryChangedEvent _)
        {
            var scroller = _list.Q<Scroller>();

            if (scroller == null)
                return;

            _list.UnregisterCallback<GeometryChangedEvent>(HookAfterLayout);
            scroller.valueChanged += value =>
            {
                if (_userScrolled)
                    Follow(value >= scroller.highValue - RowHeight);

                _userScrolled = false;
            };
        }

        private void OnUserScrolled()
        {
            _userScrolled = true;
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.End)
            {
                Follow(true);
                ScrollToLatest();
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Home || e.keyCode == KeyCode.PageUp || e.keyCode == KeyCode.UpArrow)
            {
                _userScrolled = true;
            }
        }

        private void Follow(bool follow)
        {
            _followLatest = follow;
            _jumpToLatest.style.display = !follow && _autoScrollEnabled && !_sorted ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ScrollToLatest()
        {
            if (_items.Count > 0)
                _list.ScrollToItem(_items.Count - 1);
        }

        // ---------------------------------------------------------------- context menu

        private void PopulateContextMenu(ContextualMenuPopulateEvent e)
        {
            var record = SelectedRecord;

            if (record == null)
                return;

            e.menu.AppendAction("Copy URL", _ => Copy(record.Url));
            e.menu.AppendAction("Copy as cURL", _ => Copy(RecordFormat.ToCurl(record, HttpMonitorSession.Current.Options.RedactedValue)));
            e.menu.AppendAction("Copy response body", _ => Copy(BodyAsText(record.ResponseBody)),
                record.ResponseBody != null && record.ResponseBody.Length > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            e.menu.AppendAction("Copy request body", _ => Copy(BodyAsText(record.RequestBody)),
                record.RequestBody != null && record.RequestBody.Length > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            e.menu.AppendSeparator();

            var host = RecordFormat.Host(record.Url);
            e.menu.AppendAction("Filter by host", _ => FilterByHostRequested?.Invoke(host),
                string.IsNullOrEmpty(host) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            e.menu.AppendAction("Open in browser", _ => Application.OpenURL(record.Url),
                string.Equals(record.Method, "GET", StringComparison.OrdinalIgnoreCase) && record.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? DropdownMenuAction.Status.Normal
                    : DropdownMenuAction.Status.Disabled);
        }

        private static void Copy(string text)
        {
            EditorGUIUtility.systemCopyBuffer = text ?? string.Empty;
        }

        private static string BodyAsText(byte[] body)
        {
            if (body == null)
                return string.Empty;

            return RecordFormat.LooksLikeText(body) ? System.Text.Encoding.UTF8.GetString(body) : Convert.ToBase64String(body);
        }
    }
}
