using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// A request's detail in its own dockable window. Unpinned, it follows the main window's
    /// selection and there is at most one such follower; pinned, it keeps its record whatever the
    /// selection does, and any number can be open, which is how two responses get compared side by
    /// side. Survives domain reloads by record id and says so when the record is gone.
    /// </summary>
    public sealed class DetailWindow : EditorWindow
    {
        [SerializeField] private long _recordId = -1;
        [SerializeField] private bool _pinned;

        private DetailPane _pane;
        private ToolbarToggle _pinToggle;
        private Label _summary;
        private ToolbarButton _dockBack;
        private VisualElement _missing;
        private Label _missingText;
        private bool _subscribed;

        /// <summary>Raised when a follower window closes for any reason, so the main window can dock the detail back.</summary>
        internal static event Action FollowerClosed;

        // ---------------------------------------------------------------- opening

        /// <summary>The single unpinned window, or null.</summary>
        internal static DetailWindow Follower
        {
            get
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<DetailWindow>())
                {
                    if (!window._pinned)
                        return window;
                }

                return null;
            }
        }

        internal static IEnumerable<DetailWindow> All => Resources.FindObjectsOfTypeAll<DetailWindow>();

        /// <summary>Opens (or focuses) the follower and points it at <paramref name="record"/>.</summary>
        internal static DetailWindow OpenFollower(EditorRecord record, Rect anchor)
        {
            var window = Follower;

            if (window == null)
            {
                window = CreateInstance<DetailWindow>();
                window._pinned = false;
                window.position = new Rect(anchor.x + 40, anchor.y + 40, Mathf.Max(560, anchor.width * 0.6f), Mathf.Max(420, anchor.height * 0.8f));
                window.Show();
            }

            window.SetRecord(record);
            window.Focus();

            return window;
        }

        /// <summary>Opens a new pinned window for <paramref name="record"/>.</summary>
        internal static DetailWindow OpenPinned(EditorRecord record, Rect anchor)
        {
            var window = CreateInstance<DetailWindow>();
            window._pinned = true;
            window.position = new Rect(anchor.x + 60, anchor.y + 60, Mathf.Max(560, anchor.width * 0.6f), Mathf.Max(420, anchor.height * 0.8f));
            window.Show();
            window.SetRecord(record);
            window.Focus();

            return window;
        }

        internal static void CloseFollower()
        {
            Follower?.Close();
        }

        // ---------------------------------------------------------------- state

        internal bool IsPinned => _pinned;

        internal long RecordId => _recordId;

        internal EditorRecord Record => _recordId >= 0 ? EditorRecordStore.instance.Buffer.FindById(_recordId) : null;

        internal void SetRecord(EditorRecord record)
        {
            _recordId = record?.Id ?? -1;
            Render();
        }

        internal void SetPinned(bool pinned)
        {
            if (_pinned == pinned)
                return;

            _pinned = pinned;

            // At most one follower: unpinning while another follower exists retires that one.
            if (!pinned)
            {
                foreach (var other in All)
                {
                    if (other != this && !other._pinned)
                        other.Close();
                }

                SetRecord(HttpMonitorWindow.CurrentSelection);
            }

            Render();
        }

        // ---------------------------------------------------------------- lifecycle

        private void OnEnable()
        {
            minSize = new Vector2(360, 240);
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            if (!_pinned)
                FollowerClosed?.Invoke();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("hm-root");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "hm-dark" : "hm-light");

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.captools.httpmonitor/Editor/UI/HttpMonitorWindow.uss");

            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            var toolbar = new Toolbar();
            toolbar.AddToClassList("hm-toolbar");

            _pinToggle = new ToolbarToggle { text = "Pin", tooltip = "Pinned: keep this request while you select others (open as many as you like). Unpinned: follow the selection." };
            _pinToggle.RegisterValueChangedCallback(e => SetPinned(e.newValue));
            toolbar.Add(_pinToggle);

            _summary = new Label();
            _summary.AddToClassList("hm-detail-window-summary");
            toolbar.Add(_summary);

            var spacer = new VisualElement();
            spacer.AddToClassList("hm-toolbar-spacer");
            toolbar.Add(spacer);

            _dockBack = new ToolbarButton(Close) { text = "Dock back", tooltip = "Close this window and show the detail in the main window again" };
            toolbar.Add(_dockBack);
            root.Add(toolbar);

            _pane = new DetailPane();
            _pane.SetLayoutButtonsVisible(false);
            _pane.ReplayRequested += ReplayController.Replay;
            _pane.EditAndResendRequested += ReplayController.EditAndResend;
            _pane.OriginalRequested += id => HttpMonitorWindow.Instance?.SelectById(id);
            root.Add(_pane);

            _missing = new VisualElement();
            _missing.AddToClassList("hm-detail-placeholder");
            _missing.style.display = DisplayStyle.None;
            _missingText = new Label();
            _missingText.AddToClassList("hm-muted");
            _missing.Add(_missingText);
            root.Add(_missing);

            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            Subscribe();
            Render();
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;

            _subscribed = true;
            HttpMonitorWindow.SelectionChanged += OnSelectionChanged;
            EditorRecordStore.instance.Buffer.RecordUpdated += OnRecordUpdated;
            EditorRecordStore.instance.Buffer.Changed += OnStoreChanged;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            _subscribed = false;
            HttpMonitorWindow.SelectionChanged -= OnSelectionChanged;
            EditorRecordStore.instance.Buffer.RecordUpdated -= OnRecordUpdated;
            EditorRecordStore.instance.Buffer.Changed -= OnStoreChanged;
        }

        private void OnSelectionChanged(EditorRecord record)
        {
            if (!_pinned)
                SetRecord(record);
        }

        private void OnRecordUpdated(EditorRecord record)
        {
            if (record.Id == _recordId)
                Render();
        }

        private void OnStoreChanged()
        {
            // Eviction or Clear may have taken our record away.
            Render();
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if (_pinned)
                return;

            if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow)
            {
                HttpMonitorWindow.Instance?.SelectRelativeFromOutside(e.keyCode == KeyCode.UpArrow ? -1 : 1);
                e.StopPropagation();
            }
        }

        // ---------------------------------------------------------------- rendering

        private void Render()
        {
            var record = Record;
            titleContent = new GUIContent(Title(record));

            if (_pane == null)
                return;

            _pinToggle.SetValueWithoutNotify(_pinned);
            _dockBack.style.display = _pinned ? DisplayStyle.None : DisplayStyle.Flex;
            _summary.text = record == null
                ? (_pinned ? "Pinned" : "Following the selection")
                : $"{(_pinned ? "Pinned" : "Following")}  ·  #{record.Id} {record.Method} {RecordFormat.StatusText(record)} {RecordFormat.Name(record.Url)}";

            var missing = _recordId >= 0 && record == null;
            _missing.style.display = missing ? DisplayStyle.Flex : DisplayStyle.None;
            _pane.style.display = missing ? DisplayStyle.None : DisplayStyle.Flex;

            if (missing)
                _missingText.text = $"Request #{_recordId} is no longer available: it was evicted or the log was cleared.";
            else
                _pane.Show(record);
        }

        internal static string Title(EditorRecord record)
        {
            if (record == null)
                return "HTTP Detail";

            var name = RecordFormat.Name(record.Url);

            if (name.Length > 40)
                name = name.Substring(0, 37) + "…";

            return $"{record.Method} {RecordFormat.StatusText(record)} {name}";
        }
    }
}
