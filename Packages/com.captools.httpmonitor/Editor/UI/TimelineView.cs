using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// One bar per visible request on a shared time axis, drawn with the mesh API so a few thousand
    /// bars cost one draw. Rows are packed greedily: a request goes on the first lane whose previous
    /// bar ended before it started, so concurrent requests stack and sequential ones share a lane.
    /// Packing walks the records by start time, so the picture does not depend on the order the
    /// records arrive in (the table may be sorted by any column). Hover shows a tooltip, click
    /// selects, selection highlights.
    ///
    /// Sizing: the user drags the top edge to choose the height (remembered), and the lane count
    /// follows the height; extra concurrency lands in the last lane. A collapse button shrinks it to
    /// a one-line summary so the list gets the room back without losing the axis.
    /// </summary>
    internal sealed class TimelineView : VisualElement
    {
        private const float LaneHeight = 10f;
        private const float LaneGap = 3f;
        private const float MinBarWidth = 2f;
        private const float PaddingX = 8f;
        private const float PaddingY = 6f;
        private const float AxisHeight = 16f;
        private const float HandleHeight = 5f;
        private const float CollapsedHeight = 22f;
        private const float MinExpandedHeight = 70f;
        private const float MaxExpandedHeight = 420f;
        private const float DefaultExpandedHeight = 110f;
        private const double MinWindowMs = 250;
        private const double PendingMinWidthMs = 20;
        private const string HeightPrefKey = "HttpMonitor.Timeline.Height";
        private const string CollapsedPrefKey = "HttpMonitor.Timeline.Collapsed";

        private struct Bar
        {
            public EditorRecord Record;
            public int Lane;
            public double StartMs;
            public double EndMs;
            public Rect Rect;
        }

        private readonly List<Bar> _bars = new List<Bar>();
        private readonly List<EditorRecord> _records = new List<EditorRecord>();
        private readonly VisualElement _handle;
        private readonly Label _tooltipLabel;
        private readonly Label _axisLabel;
        private readonly Button _collapseButton;
        private EditorRecord _selected;
        private EditorRecord _hovered;
        private long _originTicks;
        private double _windowMs = MinWindowMs;
        private int _laneCount = 1;
        private float _expandedHeight;
        private bool _collapsed;
        private int _dragPointer = -1;
        private float _dragStartY;
        private float _dragStartHeight;

        public event Action<EditorRecord> BarClicked;

        public TimelineView()
        {
            name = "hm-timeline";
            AddToClassList("hm-timeline");

            _expandedHeight = Mathf.Clamp(EditorPrefs.GetFloat(HeightPrefKey, DefaultExpandedHeight), MinExpandedHeight, MaxExpandedHeight);
            _collapsed = EditorPrefs.GetBool(CollapsedPrefKey, false);

            _handle = new VisualElement { name = "hm-timeline-handle", tooltip = "Drag to resize the timeline" };
            _handle.AddToClassList("hm-timeline-handle");
            _handle.RegisterCallback<PointerDownEvent>(OnHandleDown);
            _handle.RegisterCallback<PointerMoveEvent>(OnHandleMove);
            _handle.RegisterCallback<PointerUpEvent>(OnHandleUp);
            Add(_handle);

            _axisLabel = new Label { pickingMode = PickingMode.Ignore };
            _axisLabel.AddToClassList("hm-timeline-axis");
            Add(_axisLabel);

            _collapseButton = new Button(() => Collapsed = !Collapsed);
            _collapseButton.AddToClassList("hm-timeline-collapse");
            Add(_collapseButton);

            _tooltipLabel = new Label { pickingMode = PickingMode.Ignore };
            _tooltipLabel.AddToClassList("hm-timeline-tooltip");
            _tooltipLabel.style.display = DisplayStyle.None;
            Add(_tooltipLabel);

            generateVisualContent += Draw;
            RegisterCallback<MouseMoveEvent>(OnMouseMove);
            RegisterCallback<MouseLeaveEvent>(_ => SetHovered(null));
            RegisterCallback<ClickEvent>(OnClick);
            RegisterCallback<GeometryChangedEvent>(_ => Layout());

            ApplyHeight();
        }

        /// <summary>Collapsed: a one-line strip with the axis summary; the bars come back on expand.</summary>
        public bool Collapsed
        {
            get => _collapsed;
            set
            {
                if (_collapsed == value)
                    return;

                _collapsed = value;
                EditorPrefs.SetBool(CollapsedPrefKey, value);
                ApplyHeight();
                Layout();
            }
        }

        public float ExpandedHeight => _expandedHeight;

        /// <summary>How many lanes fit the current expanded height; concurrency beyond that shares the last lane.</summary>
        internal int AvailableLanes => Math.Max(1, (int)((_expandedHeight - PaddingY * 2 - AxisHeight) / (LaneHeight + LaneGap)));

        public void SetRecords(IReadOnlyList<EditorRecord> records)
        {
            _records.Clear();

            if (records != null)
                _records.AddRange(records);

            // Time order, id as the tiebreaker: lane packing must see requests in the order they started.
            _records.Sort((a, b) =>
            {
                var c = a.StartedAtUtcTicks.CompareTo(b.StartedAtUtcTicks);

                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });

            Layout();
        }

        public void SetSelected(EditorRecord record)
        {
            if (ReferenceEquals(_selected, record))
                return;

            _selected = record;
            MarkDirtyRepaint();
        }

        /// <summary>Repaint only; geometry is recomputed lazily on the next layout pass when the record set changed.</summary>
        public void RefreshRecord(EditorRecord record)
        {
            if (!record.IsFinished)
                return;

            Layout();
        }

        // ---------------------------------------------------------------- test hooks

        internal int LaneCount => _laneCount;

        internal int LaneOf(EditorRecord record)
        {
            foreach (var bar in _bars)
            {
                if (ReferenceEquals(bar.Record, record))
                    return bar.Lane;
            }

            return -1;
        }

        internal int[] Lanes()
        {
            var lanes = new int[_bars.Count];

            for (var i = 0; i < _bars.Count; i++)
                lanes[i] = _bars[i].Lane;

            return lanes;
        }

        internal long[] OrderedIds()
        {
            var ids = new long[_bars.Count];

            for (var i = 0; i < _bars.Count; i++)
                ids[i] = _bars[i].Record.Id;

            return ids;
        }

        internal void SetExpandedHeightForTests(float height)
        {
            _expandedHeight = Mathf.Clamp(height, MinExpandedHeight, MaxExpandedHeight);
            ApplyHeight();
            Layout();
        }

        // ---------------------------------------------------------------- sizing

        private void ApplyHeight()
        {
            style.height = _collapsed ? CollapsedHeight : _expandedHeight;
            _collapseButton.text = _collapsed ? "▴ Expand" : "▾ Collapse";
            _collapseButton.tooltip = _collapsed ? "Show the bars again" : "Shrink the timeline to one line";
            EnableInClassList("hm-timeline--collapsed", _collapsed);
        }

        private void OnHandleDown(PointerDownEvent e)
        {
            if (e.button != 0)
                return;

            _dragPointer = e.pointerId;
            _dragStartY = e.position.y;
            _dragStartHeight = _collapsed ? MinExpandedHeight : _expandedHeight;
            _handle.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnHandleMove(PointerMoveEvent e)
        {
            if (e.pointerId != _dragPointer)
                return;

            // The timeline sits at the bottom: dragging the top edge up makes it taller.
            var height = Mathf.Clamp(_dragStartHeight - (e.position.y - _dragStartY), MinExpandedHeight, MaxExpandedHeight);

            if (_collapsed)
            {
                _collapsed = false;
                EditorPrefs.SetBool(CollapsedPrefKey, false);
            }

            _expandedHeight = height;
            ApplyHeight();
            Layout();
            e.StopPropagation();
        }

        private void OnHandleUp(PointerUpEvent e)
        {
            if (e.pointerId != _dragPointer)
                return;

            _handle.ReleasePointer(e.pointerId);
            _dragPointer = -1;
            EditorPrefs.SetFloat(HeightPrefKey, _expandedHeight);
            e.StopPropagation();
        }

        // ---------------------------------------------------------------- geometry

        private void Layout()
        {
            _bars.Clear();

            if (_records.Count == 0)
            {
                _laneCount = 1;
                _axisLabel.text = _collapsed ? "Timeline: no requests" : string.Empty;
                MarkDirtyRepaint();

                return;
            }

            _originTicks = long.MaxValue;
            var nowTicks = DateTime.UtcNow.Ticks;
            double latestMs = 0;

            foreach (var record in _records)
                _originTicks = Math.Min(_originTicks, record.StartedAtUtcTicks);

            var maxLanes = AvailableLanes;
            var laneEnds = new List<double>();
            var overflow = false;

            foreach (var record in _records)
            {
                var startMs = (record.StartedAtUtcTicks - _originTicks) / (double)TimeSpan.TicksPerMillisecond;
                var endMs = record.IsFinished
                    ? startMs + record.DurationMs
                    : Math.Max(startMs + PendingMinWidthMs, (nowTicks - _originTicks) / (double)TimeSpan.TicksPerMillisecond);

                var lane = -1;

                for (var i = 0; i < laneEnds.Count; i++)
                {
                    if (laneEnds[i] <= startMs)
                    {
                        lane = i;

                        break;
                    }
                }

                if (lane < 0)
                {
                    if (laneEnds.Count < maxLanes)
                    {
                        lane = laneEnds.Count;
                        laneEnds.Add(0);
                    }
                    else
                    {
                        lane = maxLanes - 1; // overflow lane: bars overlap, still hoverable
                        overflow = true;
                    }
                }

                laneEnds[lane] = Math.Max(laneEnds[lane], endMs);
                latestMs = Math.Max(latestMs, endMs);
                _bars.Add(new Bar { Record = record, Lane = lane, StartMs = startMs, EndMs = endMs });
            }

            _laneCount = Math.Max(1, laneEnds.Count);
            _windowMs = Math.Max(MinWindowMs, latestMs);

            var width = Math.Max(0, resolvedStyle.width - PaddingX * 2);
            var scale = width > 0 ? width / _windowMs : 0;

            for (var i = 0; i < _bars.Count; i++)
            {
                var bar = _bars[i];
                var x = PaddingX + (float)(bar.StartMs * scale);
                var w = Math.Max(MinBarWidth, (float)((bar.EndMs - bar.StartMs) * scale));
                var y = PaddingY + HandleHeight + bar.Lane * (LaneHeight + LaneGap);
                bar.Rect = new Rect(x, y, w, LaneHeight);
                _bars[i] = bar;
            }

            var summary = $"{_records.Count} request{(_records.Count == 1 ? "" : "s")} over {FormatAxis(_windowMs)}";
            _axisLabel.text = _collapsed
                ? "Timeline: " + summary
                : $"0 ms  →  {FormatAxis(_windowMs)}   ·   {summary}, {_laneCount} lane{(_laneCount == 1 ? "" : "s")}{(overflow ? " (more concurrency than fits; drag the top edge to see it)" : "")}";
            MarkDirtyRepaint();
        }

        private static string FormatAxis(double ms)
        {
            return ms < 1000 ? ms.ToString("F0") + " ms" : (ms / 1000).ToString("F1") + " s";
        }

        // ---------------------------------------------------------------- drawing

        private void Draw(MeshGenerationContext context)
        {
            if (_collapsed || _bars.Count == 0)
                return;

            var painter = context.painter2D;
            var axisY = resolvedStyle.height - AxisHeight - 2;

            painter.strokeColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            painter.lineWidth = 1;
            painter.BeginPath();
            painter.MoveTo(new Vector2(PaddingX, axisY));
            painter.LineTo(new Vector2(resolvedStyle.width - PaddingX, axisY));
            painter.Stroke();

            foreach (var bar in _bars)
            {
                var isSelected = ReferenceEquals(bar.Record, _selected);
                var isHovered = ReferenceEquals(bar.Record, _hovered);
                var color = BarColor(bar.Record);

                if (!isSelected && !isHovered && _selected != null)
                    color.a = 0.45f;

                painter.fillColor = color;
                painter.BeginPath();
                var rect = bar.Rect;

                if (isSelected || isHovered)
                    rect = new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2);

                painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
                painter.LineTo(new Vector2(rect.xMax, rect.yMin));
                painter.LineTo(new Vector2(rect.xMax, rect.yMax));
                painter.LineTo(new Vector2(rect.xMin, rect.yMax));
                painter.ClosePath();
                painter.Fill();

                if (isSelected)
                {
                    painter.strokeColor = Color.white;
                    painter.lineWidth = 1;
                    painter.Stroke();
                }
            }
        }

        internal static Color BarColor(EditorRecord record)
        {
            switch (record.State)
            {
                case HttpRecordState.Pending: return new Color(0.55f, 0.55f, 0.55f, 0.8f);
                case HttpRecordState.Failed: return Hex(0xf85149);
                case HttpRecordState.Aborted:
                case HttpRecordState.Incomplete: return Hex(0x8a8a8a);
            }

            if (record.StatusCode >= 500) return Hex(0xf85149);
            if (record.StatusCode >= 400) return Hex(0xe3a008);
            if (record.StatusCode >= 300) return Hex(0x58a6ff);

            return Hex(0x3fb950);
        }

        private static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
        }

        // ---------------------------------------------------------------- interaction

        private void OnMouseMove(MouseMoveEvent e)
        {
            if (_collapsed)
                return;

            var hit = HitTest(e.localMousePosition);
            SetHovered(hit);

            if (hit == null)
                return;

            _tooltipLabel.text = $"{hit.Method} {RecordFormat.Name(hit.Url)}  ·  {RecordFormat.StatusText(hit)}  ·  {RecordFormat.FormatDuration(hit)}";
            var x = Mathf.Clamp(e.localMousePosition.x + 12, 0, Math.Max(0, resolvedStyle.width - _tooltipLabel.resolvedStyle.width - 4));
            _tooltipLabel.style.left = x;
            _tooltipLabel.style.top = Mathf.Clamp(e.localMousePosition.y - 22, 0, resolvedStyle.height - 18);
        }

        private void OnClick(ClickEvent e)
        {
            if (_collapsed)
                return;

            var hit = HitTest(e.localPosition);

            if (hit != null)
                BarClicked?.Invoke(hit);
        }

        private void SetHovered(EditorRecord record)
        {
            if (ReferenceEquals(_hovered, record))
                return;

            _hovered = record;
            _tooltipLabel.style.display = record == null ? DisplayStyle.None : DisplayStyle.Flex;
            MarkDirtyRepaint();
        }

        private EditorRecord HitTest(Vector2 point)
        {
            // Last drawn wins, so overlapping bars in the overflow lane resolve to the newest.
            for (var i = _bars.Count - 1; i >= 0; i--)
            {
                var rect = _bars[i].Rect;
                rect.yMin -= 1;
                rect.yMax += 1;
                rect.xMin -= 1;
                rect.xMax += 1;

                if (rect.Contains(point))
                    return _bars[i].Record;
            }

            return null;
        }
    }
}
