using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// The selected request in full: a summary strip, then the Request and Response blocks stacked,
    /// each with Body / Headers tabs. Layout follows the reference (ErnSur's Request Console): what
    /// was sent and what came back are both visible without switching tabs.
    /// </summary>
    internal sealed class DetailPane : VisualElement
    {
        private readonly VisualElement _placeholder;
        private readonly ScrollView _content;
        private readonly Label _method;
        private readonly Label _url;
        private readonly Label _status;
        private readonly Label _meta;
        private readonly Label _error;
        private readonly Button _copyUrl;
        private readonly Button _copyCurl;
        private readonly Button _maximize;
        private readonly Button _popOut;
        private readonly VisualElement _poppedOutNotice;
        private readonly Label _poppedOutSummary;
        private readonly ExchangeBlock _request;
        private readonly ExchangeBlock _response;
        private EditorRecord _record;
        private bool _poppedOut;

        /// <summary>The user clicked the maximize / restore button.</summary>
        public event Action MaximizeToggled;

        /// <summary>The user clicked "Pop out": the detail should move to its own window.</summary>
        public event Action PopOutRequested;

        /// <summary>The user clicked "Dock back" on the popped-out notice.</summary>
        public event Action DockBackRequested;

        public DetailPane()
        {
            name = "hm-detail-pane";
            AddToClassList("hm-detail-pane");

            _placeholder = new VisualElement();
            _placeholder.AddToClassList("hm-detail-placeholder");
            var placeholderText = new Label("Select a request to see what was sent and what came back.");
            placeholderText.AddToClassList("hm-muted");
            _placeholder.Add(placeholderText);
            Add(_placeholder);

            _content = new ScrollView(ScrollViewMode.Vertical);
            _content.AddToClassList("hm-detail-content");
            _content.style.display = DisplayStyle.None;
            Add(_content);

            // ---- summary strip
            var summary = new VisualElement();
            summary.AddToClassList("hm-summary");

            var line1 = new VisualElement();
            line1.AddToClassList("hm-summary-line");
            _method = new Label();
            _method.AddToClassList("hm-summary-method");
            line1.Add(_method);
            _url = new Label();
            _url.AddToClassList("hm-summary-url");
            _url.selection.isSelectable = true;
            line1.Add(_url);
            _copyUrl = new Button(() => Copy(_record?.Url)) { text = "Copy URL" };
            _copyUrl.AddToClassList("hm-small-button");
            line1.Add(_copyUrl);
            _copyCurl = new Button(() => Copy(RecordFormat.ToCurl(_record, RedactedValue))) { text = "Copy as cURL", tooltip = "A curl command that reproduces this request. Redacted headers become shell variables." };
            _copyCurl.AddToClassList("hm-small-button");
            line1.Add(_copyCurl);
            _maximize = new Button(() => MaximizeToggled?.Invoke());
            _maximize.AddToClassList("hm-small-button");
            _maximize.AddToClassList("hm-maximize");
            line1.Add(_maximize);
            _popOut = new Button(() => PopOutRequested?.Invoke()) { text = "⧉ Pop out", tooltip = "Move the detail into its own window. Pin it there to keep a request while you select others." };
            _popOut.AddToClassList("hm-small-button");
            line1.Add(_popOut);
            SetMaximized(false);
            summary.Add(line1);

            var line2 = new VisualElement();
            line2.AddToClassList("hm-summary-line");
            _status = new Label();
            _status.AddToClassList("hm-summary-status");
            line2.Add(_status);
            _meta = new Label();
            _meta.AddToClassList("hm-summary-meta");
            line2.Add(_meta);
            summary.Add(line2);

            _error = new Label();
            _error.AddToClassList("hm-summary-error");
            _error.selection.isSelectable = true;
            summary.Add(_error);

            _content.Add(summary);

            // ---- blocks
            _request = new ExchangeBlock("Request", "HttpMonitor.Detail.RequestBodyTab");
            _content.Add(_request);
            _response = new ExchangeBlock("Response", "HttpMonitor.Detail.ResponseBodyTab");
            _content.Add(_response);

            // ---- popped-out notice (replaces everything while the detail lives in another window)
            _poppedOutNotice = new VisualElement { name = "hm-popped-out" };
            _poppedOutNotice.AddToClassList("hm-popped-out");
            _poppedOutNotice.style.display = DisplayStyle.None;
            var noticeTitle = new Label("Detail is open in a separate window");
            noticeTitle.AddToClassList("hm-popped-out-title");
            _poppedOutNotice.Add(noticeTitle);
            _poppedOutSummary = new Label();
            _poppedOutSummary.AddToClassList("hm-muted");
            _poppedOutNotice.Add(_poppedOutSummary);
            var dockBack = new Button(() => DockBackRequested?.Invoke()) { text = "Dock back", tooltip = "Close the separate window and show the detail here again" };
            dockBack.AddToClassList("hm-popped-out-button");
            _poppedOutNotice.Add(dockBack);
            Add(_poppedOutNotice);
        }

        /// <summary>Hides the maximize and pop-out buttons; used by the pane inside a popped-out window.</summary>
        public void SetLayoutButtonsVisible(bool visible)
        {
            _maximize.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _popOut.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>While popped out, this pane shows only a notice and a summary of the selection.</summary>
        public void SetPoppedOut(bool poppedOut)
        {
            _poppedOut = poppedOut;
            Show(_record);
        }

        public bool IsPoppedOut => _poppedOut;

        private static string RedactedValue => HttpMonitorSession.Current.Options.RedactedValue;

        public EditorRecord Record => _record;

        public void SetMaximized(bool maximized)
        {
            _maximize.text = maximized ? "⤡ Restore" : "⤢ Maximize";
            _maximize.tooltip = maximized ? "Back to the list and detail layout (Esc)" : "Give the detail the whole window; Up/Down still move the selection, Esc restores";
        }

        public void Show(EditorRecord record)
        {
            _record = record;

            if (_poppedOut)
            {
                _placeholder.style.display = DisplayStyle.None;
                _content.style.display = DisplayStyle.None;
                _poppedOutNotice.style.display = DisplayStyle.Flex;
                _poppedOutSummary.text = record == null
                    ? "No request selected."
                    : $"Following the selection: {record.Method} {RecordFormat.StatusText(record)} {RecordFormat.Name(record.Url)}";

                return;
            }

            _poppedOutNotice.style.display = DisplayStyle.None;
            _placeholder.style.display = record == null ? DisplayStyle.Flex : DisplayStyle.None;
            _content.style.display = record == null ? DisplayStyle.None : DisplayStyle.Flex;

            if (record == null)
                return;

            ShowSummary(record);
            ShowRequest(record);
            ShowResponse(record);
        }

        private void ShowSummary(EditorRecord record)
        {
            _method.text = record.Method;
            _url.text = record.Url;
            _url.tooltip = record.Url;

            _status.text = StatusLine(record);
            _status.ClearClassList();
            _status.AddToClassList("hm-summary-status");
            _status.AddToClassList(RecordFormat.StatusClass(record).Replace("hm-status-dot--", "hm-status-text--"));

            _meta.text = string.Join("  ·  ", new[]
            {
                RecordFormat.ClientText(record.Client),
                RecordFormat.SourceText(record),
                "started " + RecordFormat.FormatStarted(record),
                RecordFormat.FormatDuration(record),
                "↑ " + RecordFormat.FormatBytes(record.UploadedBytes) + "  ↓ " + RecordFormat.FormatBytes(record.DownloadedBytes),
            });

            var hasError = record.IsFinished && !string.IsNullOrEmpty(record.Error);
            _error.style.display = hasError ? DisplayStyle.Flex : DisplayStyle.None;
            _error.text = hasError ? record.Error : string.Empty;
        }

        private void ShowRequest(EditorRecord record)
        {
            _request.SetCaption(record.RequestHeaders.Length > 0 || record.RequestBody != null
                ? RecordFormat.FormatBytes(record.UploadedBytes) + " sent"
                : string.Empty, false);
            _request.SetHeaders(record.RequestHeaders, RedactedValue);
            _request.SetBody(record.RequestBody, record.RequestBodyTruncated, record.UploadedBytes,
                RecordFormat.HeaderValue(record.RequestHeaders, "Content-Type"), RequestBodyMissingReason(record), FileName(record, "request"));
        }

        private void ShowResponse(EditorRecord record)
        {
            _response.SetCaption(StatusLine(record), record.IsFinished && record.IsError);
            _response.SetHeaders(record.ResponseHeaders, RedactedValue);
            _response.SetBody(record.ResponseBody, record.ResponseBodyTruncated, record.DownloadedBytes,
                RecordFormat.HeaderValue(record.ResponseHeaders, "Content-Type"), ResponseBodyMissingReason(record), FileName(record, "response"));
        }

        internal static string StatusLine(EditorRecord record)
        {
            switch (record.State)
            {
                case HttpRecordState.Pending: return "Pending";
                case HttpRecordState.Failed: return "Failed";
                case HttpRecordState.Aborted: return "Aborted";
                case HttpRecordState.Incomplete: return "Incomplete";
            }

            var reason = RecordFormat.ReasonPhrase(record.StatusCode);

            return string.IsNullOrEmpty(reason) ? record.StatusCode.ToString() : record.StatusCode + " " + reason;
        }

        internal static string RequestBodyMissingReason(EditorRecord record)
        {
            if (record.UploadedBytes == 0 && record.IsFinished)
                return "No request body";

            switch (record.Client)
            {
                case HttpClientKind.UnityWebRequest:
                    return "Not captured: only UploadHandlerRaw exposes its bytes (UploadHandlerFile and custom handlers are recorded by size)";
                case HttpClientKind.HttpClient:
                    return "Not captured: streamed content without a known length, or larger than HttpMonitorOptions.MaxBodyBytes";
                default:
                    return "Not captured: no request body was passed to HttpMonitorCapture.Begin";
            }
        }

        internal static string ResponseBodyMissingReason(EditorRecord record)
        {
            switch (record.State)
            {
                case HttpRecordState.Pending: return "Waiting for the response";
                case HttpRecordState.Failed: return "No response: " + record.Error;
                case HttpRecordState.Aborted: return "No response: " + record.Error;
                case HttpRecordState.Incomplete: return "Response could not be read: " + record.Error;
            }

            if (record.DownloadedBytes == 0)
                return "No response body";

            switch (record.Client)
            {
                case HttpClientKind.UnityWebRequest:
                    return $"Not captured: {RecordFormat.FormatBytes(record.DownloadedBytes)} went to a download handler that does not expose its bytes (file, texture, audio, asset bundle, script). Only DownloadHandlerBuffer is captured.";
                case HttpClientKind.HttpClient:
                    return $"Not captured: {RecordFormat.FormatBytes(record.DownloadedBytes)}, larger than HttpMonitorOptions.MaxBodyBytes or streamed with BufferUnknownLengthResponses off";
                default:
                    return "Not captured: no response body was passed to the capture handle";
            }
        }

        private static string FileName(EditorRecord record, string side)
        {
            var name = RecordFormat.Name(record.Url).Trim('/');
            var query = name.IndexOf('?');

            if (query >= 0)
                name = name.Substring(0, query);

            name = name.Replace('/', '_');

            return string.IsNullOrEmpty(name) ? $"{record.Id}-{side}" : $"{record.Id}-{side}-{name}";
        }

        private static void Copy(string text)
        {
            EditorGUIUtility.systemCopyBuffer = text ?? string.Empty;
        }
    }
}
