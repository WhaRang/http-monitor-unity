using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Shows one body (request or response). A banner explains anything unusual (not captured,
    /// truncated, binary, empty); below it a mode strip and the content. Modes: Pretty (JSON and
    /// HTML/XML through the formatters, with line numbers), Raw text, Hex, Image. The default mode
    /// follows the detected kind; the user's choice sticks until the kind changes.
    /// </summary>
    internal sealed class BodyView : VisualElement
    {
        private const int MaxDisplayedChars = 512 * 1024;
        private const int MaxPrettyBytes = 1024 * 1024;
        private const int MaxHexBytes = 64 * 1024;

        private enum Mode
        {
            Pretty,
            Raw,
            Hex,
            Image,
        }

        private readonly Label _banner;
        private readonly VisualElement _modes;
        private readonly Button _prettyButton;
        private readonly Button _rawButton;
        private readonly Button _hexButton;
        private readonly Button _imageButton;
        private readonly Toggle _wrap;
        private readonly Button _copy;
        private readonly Button _save;
        private readonly ScrollView _scroll;
        private readonly VisualElement _textRow;
        private readonly Label _lineNumbers;
        private readonly Label _text;
        private readonly Image _image;
        private readonly Label _note;

        private byte[] _body;
        private BodyKind _kind = BodyKind.Empty;
        private Mode _mode = Mode.Raw;
        private bool _modeChosenByUser;
        private string _pretty;
        private string _suggestedFileName = "body";
        private Texture2D _texture;

        public BodyView()
        {
            AddToClassList("hm-body");

            var bar = new VisualElement();
            bar.AddToClassList("hm-body-bar");

            _banner = new Label();
            _banner.AddToClassList("hm-body-banner");
            bar.Add(_banner);

            var spacer = new VisualElement();
            spacer.AddToClassList("hm-toolbar-spacer");
            bar.Add(spacer);

            _modes = new VisualElement();
            _modes.AddToClassList("hm-body-modes");
            _prettyButton = ModeButton("Pretty", Mode.Pretty, "Formatted with indentation and line numbers");
            _rawButton = ModeButton("Raw", Mode.Raw, "The text exactly as captured");
            _hexButton = ModeButton("Hex", Mode.Hex, "Offset, bytes and ASCII");
            _imageButton = ModeButton("Image", Mode.Image, "Decoded image preview");
            bar.Add(_modes);

            _wrap = new Toggle("Wrap") { tooltip = "Wrap long lines" };
            _wrap.AddToClassList("hm-body-wrap");
            _wrap.SetValueWithoutNotify(EditorPrefs.GetBool("HttpMonitor.Body.Wrap", false));
            _wrap.RegisterValueChangedCallback(e =>
            {
                EditorPrefs.SetBool("HttpMonitor.Body.Wrap", e.newValue);
                ApplyWrap();
            });
            bar.Add(_wrap);

            _copy = new Button(CopyBody) { text = "Copy", tooltip = "Copy what is shown (pretty or raw); base64 for binary" };
            _copy.AddToClassList("hm-small-button");
            bar.Add(_copy);

            _save = new Button(SaveBody) { text = "Save…", tooltip = "Save the captured bytes to a file" };
            _save.AddToClassList("hm-small-button");
            bar.Add(_save);
            Add(bar);

            _scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _scroll.AddToClassList("hm-body-scroll");

            _textRow = new VisualElement();
            _textRow.AddToClassList("hm-body-text-row");
            _lineNumbers = new Label { pickingMode = PickingMode.Ignore };
            _lineNumbers.AddToClassList("hm-body-lines");
            _textRow.Add(_lineNumbers);
            _text = new Label();
            _text.AddToClassList("hm-body-text");
            _text.selection.isSelectable = true;
            _textRow.Add(_text);
            _scroll.Add(_textRow);

            _image = new Image { scaleMode = ScaleMode.ScaleToFit };
            _image.AddToClassList("hm-body-image");
            _scroll.Add(_image);

            _note = new Label();
            _note.AddToClassList("hm-body-note");
            _scroll.Add(_note);

            Add(_scroll);
            ApplyWrap();
        }

        /// <param name="body">Stored bytes, or null when nothing was captured.</param>
        /// <param name="truncated">True when only a prefix of a larger body was stored.</param>
        /// <param name="fullSize">Wire size, for the banner; -1 when unknown.</param>
        /// <param name="contentType">The side's Content-Type header, if any; drives detection.</param>
        /// <param name="whyMissing">Shown when <paramref name="body"/> is null; explains what was not captured and why.</param>
        public void SetBody(byte[] body, bool truncated, long fullSize, string contentType, string whyMissing, string suggestedFileName)
        {
            _body = body;
            _pretty = null;
            _suggestedFileName = string.IsNullOrEmpty(suggestedFileName) ? "body" : suggestedFileName;
            ReleaseTexture();

            var previousKind = _kind;
            _kind = BodyKindDetector.Detect(body, contentType);

            if (_kind != previousKind || !_modeChosenByUser)
            {
                _mode = DefaultMode(_kind);
                _modeChosenByUser = false;
            }

            var hasBytes = body != null && body.Length > 0;
            _copy.SetEnabled(hasBytes);
            _save.SetEnabled(hasBytes);
            _modes.style.display = hasBytes ? DisplayStyle.Flex : DisplayStyle.None;
            _wrap.style.display = hasBytes && _kind != BodyKind.Image ? DisplayStyle.Flex : DisplayStyle.None;

            if (body == null)
            {
                SetBanner(whyMissing ?? "Not captured", true);
                _scroll.style.display = DisplayStyle.None;

                return;
            }

            _scroll.style.display = DisplayStyle.Flex;

            if (body.Length == 0)
            {
                SetBanner("Empty body", false);
                ShowText(string.Empty, false);

                return;
            }

            var sizeText = RecordFormat.FormatBytes(body.Length);
            var kindText = KindText(_kind);

            if (truncated)
                SetBanner($"Showing the first {sizeText}{(fullSize > body.Length ? " of " + RecordFormat.FormatBytes(fullSize) : string.Empty)} (body cap in HttpMonitorOptions.MaxBodyBytes)", true);
            else
                SetBanner(kindText + ", " + sizeText, false);

            _prettyButton.style.display = _kind == BodyKind.Json || _kind == BodyKind.Markup ? DisplayStyle.Flex : DisplayStyle.None;
            _imageButton.style.display = _kind == BodyKind.Image ? DisplayStyle.Flex : DisplayStyle.None;
            _rawButton.style.display = _kind == BodyKind.Image || _kind == BodyKind.Binary ? DisplayStyle.None : DisplayStyle.Flex;

            Render();
        }

        // ---------------------------------------------------------------- modes

        private Button ModeButton(string text, Mode mode, string tooltip)
        {
            var button = new Button(() =>
            {
                _mode = mode;
                _modeChosenByUser = true;
                Render();
            }) { text = text, tooltip = tooltip };
            button.AddToClassList("hm-mode");
            _modes.Add(button);

            return button;
        }

        private static Mode DefaultMode(BodyKind kind)
        {
            switch (kind)
            {
                case BodyKind.Json:
                case BodyKind.Markup: return Mode.Pretty;
                case BodyKind.Image: return Mode.Image;
                case BodyKind.Binary: return Mode.Hex;
                default: return Mode.Raw;
            }
        }

        private static string KindText(BodyKind kind)
        {
            switch (kind)
            {
                case BodyKind.Json: return "JSON";
                case BodyKind.Markup: return "HTML/XML";
                case BodyKind.Image: return "Image";
                case BodyKind.Binary: return "Binary";
                default: return "Text";
            }
        }

        private void Render()
        {
            _prettyButton.EnableInClassList("hm-mode--active", _mode == Mode.Pretty);
            _rawButton.EnableInClassList("hm-mode--active", _mode == Mode.Raw);
            _hexButton.EnableInClassList("hm-mode--active", _mode == Mode.Hex);
            _imageButton.EnableInClassList("hm-mode--active", _mode == Mode.Image);

            _note.style.display = DisplayStyle.None;
            _image.style.display = DisplayStyle.None;
            _textRow.style.display = DisplayStyle.Flex;

            switch (_mode)
            {
                case Mode.Pretty:
                    RenderPretty();

                    break;
                case Mode.Hex:
                    ShowText(HexPreview(_body, MaxHexBytes), false);

                    break;
                case Mode.Image:
                    RenderImage();

                    break;
                default:
                    ShowText(Encoding.UTF8.GetString(_body), true);

                    break;
            }

            _scroll.scrollOffset = Vector2.zero;
        }

        private void RenderPretty()
        {
            if (_pretty == null)
            {
                if (_body.Length > MaxPrettyBytes)
                {
                    Note($"Too large to format ({RecordFormat.FormatBytes(_body.Length)}); showing raw.");
                    ShowText(Encoding.UTF8.GetString(_body), true);

                    return;
                }

                var text = Encoding.UTF8.GetString(_body);
                var ok = _kind == BodyKind.Json ? JsonFormatter.TryFormat(text, out _pretty) : MarkupFormatter.TryFormat(text, out _pretty);

                if (!ok)
                {
                    _pretty = null;
                    Note(_kind == BodyKind.Json ? "Not valid JSON; showing raw." : "Markup could not be parsed; showing raw.");
                    ShowText(text, true);

                    return;
                }
            }

            ShowText(_pretty, true);
        }

        private void RenderImage()
        {
            _textRow.style.display = DisplayStyle.None;

            if (_texture == null)
            {
                _texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };

                if (!_texture.LoadImage(_body))
                {
                    ReleaseTexture();
                    Note("Image could not be decoded; showing hex.");
                    _textRow.style.display = DisplayStyle.Flex;
                    ShowText(HexPreview(_body, MaxHexBytes), false);

                    return;
                }
            }

            _image.image = _texture;
            _image.style.width = Math.Min(_texture.width, 800);
            _image.style.height = Math.Min(_texture.height, 600) * (Math.Min(_texture.width, 800) / (float)_texture.width);
            _image.style.display = DisplayStyle.Flex;
            SetBanner($"Image, {_texture.width}×{_texture.height}, {RecordFormat.FormatBytes(_body.Length)}", false);
        }

        private void ShowText(string text, bool withLineNumbers)
        {
            if (text.Length > MaxDisplayedChars)
            {
                text = text.Substring(0, MaxDisplayedChars);
                Note($"Showing the first {MaxDisplayedChars:N0} characters. Use Copy or Save for the whole body.");
            }

            _text.text = text;

            if (withLineNumbers && text.Length > 0)
            {
                _lineNumbers.text = LineNumbers(CountLines(text));
                _lineNumbers.style.display = DisplayStyle.Flex;
            }
            else
            {
                _lineNumbers.style.display = DisplayStyle.None;
            }
        }

        private void Note(string text)
        {
            _note.text = text;
            _note.style.display = DisplayStyle.Flex;
        }

        private void SetBanner(string text, bool warn)
        {
            _banner.text = text;
            _banner.EnableInClassList("hm-body-banner--warn", warn);
        }

        private void ApplyWrap()
        {
            var wrap = _wrap.value;
            _text.style.whiteSpace = wrap ? WhiteSpace.Normal : WhiteSpace.NoWrap;
            _scroll.mode = wrap ? ScrollViewMode.Vertical : ScrollViewMode.VerticalAndHorizontal;
            // Line numbers only line up without wrapping.
            _lineNumbers.style.display = wrap || string.IsNullOrEmpty(_lineNumbers.text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void ReleaseTexture()
        {
            if (_texture == null)
                return;

            UnityEngine.Object.DestroyImmediate(_texture);
            _texture = null;
            _image.image = null;
        }

        // ---------------------------------------------------------------- pure helpers

        internal static int CountLines(string text)
        {
            var lines = 1;

            foreach (var c in text)
            {
                if (c == '\n')
                    lines++;
            }

            return lines;
        }

        internal static string LineNumbers(int count)
        {
            var width = count.ToString().Length;
            var sb = new StringBuilder(count * (width + 1));

            for (var i = 1; i <= count; i++)
                sb.Append(i.ToString().PadLeft(width)).Append('\n');

            return sb.ToString(0, sb.Length - 1);
        }

        /// <summary>Offset, 16 hex bytes, ASCII.</summary>
        internal static string HexPreview(byte[] bytes, int maxBytes = 4096)
        {
            var sb = new StringBuilder();
            var limit = Math.Min(bytes.Length, maxBytes);

            for (var offset = 0; offset < limit; offset += 16)
            {
                sb.Append(offset.ToString("x8")).Append("  ");

                for (var i = 0; i < 16; i++)
                {
                    if (offset + i < limit)
                        sb.Append(bytes[offset + i].ToString("x2")).Append(' ');
                    else
                        sb.Append("   ");

                    if (i == 7)
                        sb.Append(' ');
                }

                sb.Append(' ');

                for (var i = 0; i < 16 && offset + i < limit; i++)
                {
                    var b = bytes[offset + i];
                    sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                }

                sb.Append('\n');
            }

            if (bytes.Length > limit)
                sb.Append($"… {bytes.Length - limit:N0} more bytes");

            return sb.ToString();
        }

        private void CopyBody()
        {
            if (_body == null)
                return;

            string text;

            if (_mode == Mode.Pretty && _pretty != null)
                text = _pretty;
            else if (_kind == BodyKind.Binary || _kind == BodyKind.Image)
                text = Convert.ToBase64String(_body);
            else
                text = Encoding.UTF8.GetString(_body);

            EditorGUIUtility.systemCopyBuffer = text;
        }

        private void SaveBody()
        {
            if (_body == null)
                return;

            var path = EditorUtility.SaveFilePanel("Save body", string.Empty, _suggestedFileName, Extension(_kind));

            if (!string.IsNullOrEmpty(path))
                System.IO.File.WriteAllBytes(path, _body);
        }

        private string Extension(BodyKind kind)
        {
            switch (kind)
            {
                case BodyKind.Json: return "json";
                case BodyKind.Markup: return "html";
                case BodyKind.Text: return "txt";
                case BodyKind.Image: return _body.Length > 3 && _body[0] == 0xFF ? "jpg" : "png";
                default: return "bin";
            }
        }
    }
}
