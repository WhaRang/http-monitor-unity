using System;
using System.Collections.Generic;
using System.Text;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Indents HTML and XML: one tag per line, children indented, text on its own line, tags copied
    /// verbatim (attribute quoting and self-closing slashes are never rewritten). The contents of
    /// <c>script</c> and <c>style</c> keep their own line structure under a uniform indent; the
    /// contents of <c>pre</c> and <c>textarea</c> are emitted exactly as found. It is a
    /// tokenizer with a tag stack, not a DOM: HTML void elements and self-closing tags do not push,
    /// and a closing tag that matches nothing (or unbalanced nesting) makes the whole format fail so
    /// the viewer falls back to raw rather than mis-indent.
    /// </summary>
    internal static class MarkupFormatter
    {
        private const string Indent = "  ";

        private static readonly HashSet<string> VoidElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
        };

        private static readonly HashSet<string> RawTextElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "script", "style", "pre", "textarea",
        };

        /// <summary>Whitespace is content here, so not even a uniform indent is added.</summary>
        private static readonly HashSet<string> WhitespaceSignificantElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pre", "textarea",
        };

        public static bool LooksLikeMarkup(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            var i = 0;

            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;

            return i < text.Length && text[i] == '<';
        }

        /// <returns>false when the markup is not well-formed enough to indent safely; <paramref name="formatted"/> is then null.</returns>
        public static bool TryFormat(string markup, out string formatted)
        {
            formatted = null;

            if (!LooksLikeMarkup(markup))
                return false;

            var sb = new StringBuilder(markup.Length + markup.Length / 4);
            var stack = new List<string>();
            var i = 0;
            var sawTag = false;

            while (i < markup.Length)
            {
                if (markup[i] != '<')
                {
                    var textEnd = markup.IndexOf('<', i);

                    if (textEnd < 0)
                        textEnd = markup.Length;

                    var text = markup.Substring(i, textEnd - i).Trim();

                    if (text.Length > 0)
                        AppendLine(sb, stack.Count, text);

                    i = textEnd;

                    continue;
                }

                // Comments, doctype, processing instructions, CDATA: copy through as one line.
                if (Starts(markup, i, "<!--"))
                {
                    if (!CopySpecial(markup, ref i, "-->", sb, stack.Count))
                        return false;

                    continue;
                }

                if (Starts(markup, i, "<![CDATA["))
                {
                    if (!CopySpecial(markup, ref i, "]]>", sb, stack.Count))
                        return false;

                    continue;
                }

                if (Starts(markup, i, "<!") || Starts(markup, i, "<?"))
                {
                    if (!CopySpecial(markup, ref i, ">", sb, stack.Count))
                        return false;

                    continue;
                }

                var close = FindTagEnd(markup, i);

                if (close < 0)
                    return false;

                var tag = markup.Substring(i, close - i + 1);
                var name = TagName(tag, out var isClosing, out var isSelfClosing);

                if (name.Length == 0)
                    return false;

                sawTag = true;
                i = close + 1;

                if (isClosing)
                {
                    if (stack.Count == 0 || !string.Equals(stack[stack.Count - 1], name, StringComparison.OrdinalIgnoreCase))
                        return false;

                    stack.RemoveAt(stack.Count - 1);
                    AppendLine(sb, stack.Count, tag);

                    continue;
                }

                AppendLine(sb, stack.Count, tag);

                if (isSelfClosing || VoidElements.Contains(name))
                    continue;

                // Pushed before the raw-text handling: the closing tag of a <script> or <style> is
                // matched by the next iteration like any other, so it must find the name on the stack.
                stack.Add(name);

                if (RawTextElements.Contains(name))
                {
                    var closingTag = "</" + name;
                    var end = markup.IndexOf(closingTag, i, StringComparison.OrdinalIgnoreCase);

                    if (end < 0)
                        return false;

                    var raw = markup.Substring(i, end - i).Trim('\r', '\n');

                    if (raw.Trim().Length > 0)
                        AppendRaw(sb, WhitespaceSignificantElements.Contains(name) ? 0 : stack.Count, raw);

                    i = end;
                }
            }

            if (!sawTag || stack.Count != 0)
                return false;

            formatted = sb.ToString().TrimEnd('\n');

            return true;
        }

        private static bool Starts(string s, int i, string prefix)
        {
            return string.CompareOrdinal(s, i, prefix, 0, prefix.Length) == 0;
        }

        private static bool CopySpecial(string s, ref int i, string terminator, StringBuilder sb, int depth)
        {
            var end = s.IndexOf(terminator, i, StringComparison.Ordinal);

            if (end < 0)
                return false;

            end += terminator.Length;
            AppendLine(sb, depth, s.Substring(i, end - i).Trim());
            i = end;

            return true;
        }

        /// <summary>Index of the '>' that closes the tag starting at <paramref name="start"/>, honouring quoted attribute values.</summary>
        private static int FindTagEnd(string s, int start)
        {
            var quote = '\0';

            for (var i = start + 1; i < s.Length; i++)
            {
                var c = s[i];

                if (quote != '\0')
                {
                    if (c == quote)
                        quote = '\0';

                    continue;
                }

                if (c == '"' || c == '\'')
                    quote = c;
                else if (c == '>')
                    return i;
                else if (c == '<')
                    return -1; // a bare '<' inside a tag: not markup we can trust
            }

            return -1;
        }

        private static string TagName(string tag, out bool isClosing, out bool isSelfClosing)
        {
            var i = 1;
            isClosing = tag.Length > 1 && tag[1] == '/';

            if (isClosing)
                i++;

            var start = i;

            while (i < tag.Length && (char.IsLetterOrDigit(tag[i]) || tag[i] == '-' || tag[i] == ':' || tag[i] == '_' || tag[i] == '.'))
                i++;

            isSelfClosing = tag.Length >= 2 && tag[tag.Length - 2] == '/';

            return tag.Substring(start, i - start);
        }

        private static void AppendLine(StringBuilder sb, int depth, string text)
        {
            for (var d = 0; d < depth; d++)
                sb.Append(Indent);

            sb.Append(text).Append('\n');
        }

        /// <summary>Raw blocks keep their own line structure; only a uniform indent is added so they sit inside their parent.</summary>
        private static void AppendRaw(StringBuilder sb, int depth, string raw)
        {
            foreach (var line in raw.Split('\n'))
                AppendLine(sb, depth, line.TrimEnd('\r'));
        }
    }
}
