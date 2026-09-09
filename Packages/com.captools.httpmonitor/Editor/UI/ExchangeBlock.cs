using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// One half of the exchange, Request or Response: a title row with the block name and a status
    /// or size caption, and Body / Headers tabs beneath. The last chosen tab is remembered per block
    /// in EditorPrefs, so a user who lives in the Response body keeps seeing it.
    /// </summary>
    internal sealed class ExchangeBlock : VisualElement
    {
        private readonly string _prefKey;
        private readonly Label _title;
        private readonly Label _caption;
        private readonly Button _bodyTab;
        private readonly Button _headersTab;
        private readonly BodyView _body;
        private readonly HeaderTable _headers;
        private bool _showBody;

        public ExchangeBlock(string title, string prefKey)
        {
            _prefKey = prefKey;
            AddToClassList("hm-block");

            var head = new VisualElement();
            head.AddToClassList("hm-block-head");

            _title = new Label(title);
            _title.AddToClassList("hm-block-title");
            head.Add(_title);

            _bodyTab = new Button(() => ShowTab(true)) { text = "Body" };
            _bodyTab.AddToClassList("hm-tab");
            head.Add(_bodyTab);

            _headersTab = new Button(() => ShowTab(false)) { text = "Headers" };
            _headersTab.AddToClassList("hm-tab");
            head.Add(_headersTab);

            var spacer = new VisualElement();
            spacer.AddToClassList("hm-toolbar-spacer");
            head.Add(spacer);

            _caption = new Label();
            _caption.AddToClassList("hm-block-caption");
            head.Add(_caption);
            Add(head);

            _body = new BodyView();
            Add(_body);

            _headers = new HeaderTable();
            Add(_headers);

            ShowTab(EditorPrefs.GetBool(_prefKey, true), persist: false);
        }

        public void SetCaption(string text, bool isError)
        {
            _caption.text = text ?? string.Empty;
            _caption.EnableInClassList("hm-block-caption--error", isError);
        }

        public void SetHeaders(EditorHeader[] headers, string redactedValue)
        {
            _headers.SetHeaders(headers, redactedValue);
            _headersTab.text = headers == null || headers.Length == 0 ? "Headers" : $"Headers ({headers.Length})";
        }

        public void SetBody(byte[] body, bool truncated, long fullSize, string contentType, string whyMissing, string suggestedFileName)
        {
            _body.SetBody(body, truncated, fullSize, contentType, whyMissing, suggestedFileName);
            _bodyTab.text = body != null && body.Length > 0 ? $"Body ({RecordFormat.FormatBytes(body.Length)})" : "Body";
        }

        private void ShowTab(bool body, bool persist = true)
        {
            _showBody = body;
            _body.style.display = body ? DisplayStyle.Flex : DisplayStyle.None;
            _headers.style.display = body ? DisplayStyle.None : DisplayStyle.Flex;
            _bodyTab.EnableInClassList("hm-tab--active", body);
            _headersTab.EnableInClassList("hm-tab--active", !body);

            if (persist)
                EditorPrefs.SetBool(_prefKey, body);
        }
    }
}
