using System.Text;
using UnityEditor;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Name / value rows for one side's headers, with a copy-all button and per-row copy on the
    /// context menu. Redacted values are shown as a badge that explains itself on hover.
    /// </summary>
    internal sealed class HeaderTable : VisualElement
    {
        private readonly VisualElement _rows;
        private readonly Label _empty;
        private readonly Button _copyAll;
        private EditorHeader[] _headers = new EditorHeader[0];
        private string _redactedValue = HttpMonitorOptions.DefaultRedactedValue;

        public HeaderTable()
        {
            AddToClassList("hm-headers");

            var bar = new VisualElement();
            bar.AddToClassList("hm-headers-bar");
            _copyAll = new Button(CopyAll) { text = "Copy all", tooltip = "Copy every header as name: value lines" };
            _copyAll.AddToClassList("hm-small-button");
            bar.Add(_copyAll);
            Add(bar);

            _rows = new VisualElement();
            _rows.AddToClassList("hm-headers-rows");
            Add(_rows);

            _empty = new Label("No headers");
            _empty.AddToClassList("hm-muted");
            Add(_empty);
        }

        public void SetHeaders(EditorHeader[] headers, string redactedValue)
        {
            _headers = headers ?? new EditorHeader[0];
            _redactedValue = redactedValue ?? HttpMonitorOptions.DefaultRedactedValue;
            _rows.Clear();

            foreach (var header in _headers)
                _rows.Add(MakeRow(header));

            var any = _headers.Length > 0;
            _empty.style.display = any ? DisplayStyle.None : DisplayStyle.Flex;
            _copyAll.SetEnabled(any);
        }

        private VisualElement MakeRow(EditorHeader header)
        {
            var row = new VisualElement();
            row.AddToClassList("hm-header-row");

            var name = new Label(header.Name) { tooltip = header.Name };
            name.AddToClassList("hm-header-name");
            row.Add(name);

            var redacted = header.Value == _redactedValue;

            if (redacted)
            {
                var badge = new Label("redacted") { tooltip = "Hidden by HttpMonitorOptions.RedactedHeaders so secrets are never stored. The real value was sent on the wire." };
                badge.AddToClassList("hm-redacted-badge");
                row.Add(badge);
            }
            else
            {
                var value = new Label(header.Value) { tooltip = header.Value };
                value.AddToClassList("hm-header-value");
                value.selection.isSelectable = true;
                row.Add(value);
            }

            row.RegisterCallback<ContextualMenuPopulateEvent>(e =>
            {
                e.menu.AppendAction("Copy name", _ => Copy(header.Name));
                e.menu.AppendAction("Copy value", _ => Copy(header.Value), redacted ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                e.menu.AppendAction("Copy line", _ => Copy(header.ToString()));
            });

            return row;
        }

        private void CopyAll()
        {
            var sb = new StringBuilder();

            foreach (var header in _headers)
                sb.Append(header.Name).Append(": ").Append(header.Value).Append('\n');

            Copy(sb.ToString().TrimEnd('\n'));
        }

        private static void Copy(string text)
        {
            EditorGUIUtility.systemCopyBuffer = text ?? string.Empty;
        }
    }
}
