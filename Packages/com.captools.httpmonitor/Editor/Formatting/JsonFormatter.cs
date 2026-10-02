using System;
using System.Globalization;
using System.Text;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Pretty-prints JSON without building a tree: a single pass that re-emits tokens with
    /// indentation. Validates as it goes, so malformed input returns false and the viewer falls
    /// back to raw text instead of showing a half-formatted body.
    /// </summary>
    internal static class JsonFormatter
    {
        private const string Indent = "  ";

        /// <returns>false when the input is not valid JSON; <paramref name="formatted"/> is then null.</returns>
        public static bool TryFormat(string json, out string formatted)
        {
            formatted = null;

            if (string.IsNullOrWhiteSpace(json))
                return false;

            var sb = new StringBuilder(json.Length + json.Length / 4);
            var i = 0;

            if (!SkipWhitespace(json, ref i) || !TryWriteValue(json, ref i, 0, sb))
                return false;

            SkipWhitespace(json, ref i);

            if (i != json.Length)
                return false;

            formatted = sb.ToString();

            return true;
        }

        /// <summary>Cheap check for "does this look like JSON" before paying for a full format.</summary>
        public static bool LooksLikeJson(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            var i = 0;
            SkipWhitespace(text, ref i);

            if (i >= text.Length)
                return false;

            var c = text[i];

            return c == '{' || c == '[';
        }

        private static bool TryWriteValue(string s, ref int i, int depth, StringBuilder sb)
        {
            if (i >= s.Length)
                return false;

            switch (s[i])
            {
                case '{': return TryWriteObject(s, ref i, depth, sb);
                case '[': return TryWriteArray(s, ref i, depth, sb);
                case '"': return TryWriteString(s, ref i, sb);
                case 't': return TryWriteLiteral(s, ref i, "true", sb);
                case 'f': return TryWriteLiteral(s, ref i, "false", sb);
                case 'n': return TryWriteLiteral(s, ref i, "null", sb);
                default: return TryWriteNumber(s, ref i, sb);
            }
        }

        private static bool TryWriteObject(string s, ref int i, int depth, StringBuilder sb)
        {
            i++; // {
            SkipWhitespace(s, ref i);

            if (i < s.Length && s[i] == '}')
            {
                i++;
                sb.Append("{}");

                return true;
            }

            sb.Append('{');
            var first = true;

            while (true)
            {
                if (!first)
                {
                    SkipWhitespace(s, ref i);

                    if (i >= s.Length || s[i] != ',')
                        break;

                    i++;
                    sb.Append(',');
                }

                first = false;
                SkipWhitespace(s, ref i);
                sb.Append('\n');
                AppendIndent(sb, depth + 1);

                if (i >= s.Length || s[i] != '"' || !TryWriteString(s, ref i, sb))
                    return false;

                SkipWhitespace(s, ref i);

                if (i >= s.Length || s[i] != ':')
                    return false;

                i++;
                sb.Append(": ");
                SkipWhitespace(s, ref i);

                if (!TryWriteValue(s, ref i, depth + 1, sb))
                    return false;
            }

            if (i >= s.Length || s[i] != '}')
                return false;

            i++;
            sb.Append('\n');
            AppendIndent(sb, depth);
            sb.Append('}');

            return true;
        }

        private static bool TryWriteArray(string s, ref int i, int depth, StringBuilder sb)
        {
            i++; // [
            SkipWhitespace(s, ref i);

            if (i < s.Length && s[i] == ']')
            {
                i++;
                sb.Append("[]");

                return true;
            }

            sb.Append('[');
            var first = true;

            while (true)
            {
                if (!first)
                {
                    SkipWhitespace(s, ref i);

                    if (i >= s.Length || s[i] != ',')
                        break;

                    i++;
                    sb.Append(',');
                }

                first = false;
                SkipWhitespace(s, ref i);
                sb.Append('\n');
                AppendIndent(sb, depth + 1);

                if (!TryWriteValue(s, ref i, depth + 1, sb))
                    return false;
            }

            if (i >= s.Length || s[i] != ']')
                return false;

            i++;
            sb.Append('\n');
            AppendIndent(sb, depth);
            sb.Append(']');

            return true;
        }

        private static bool TryWriteString(string s, ref int i, StringBuilder sb)
        {
            var start = i;
            i++; // opening quote

            while (i < s.Length)
            {
                var c = s[i];

                if (c == '\\')
                {
                    i += 2;

                    continue;
                }

                if (c == '"')
                {
                    i++;
                    sb.Append(s, start, i - start);

                    return true;
                }

                if (c < 0x20)
                    return false;

                i++;
            }

            return false;
        }

        private static bool TryWriteLiteral(string s, ref int i, string literal, StringBuilder sb)
        {
            if (string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                return false;

            i += literal.Length;
            sb.Append(literal);

            return true;
        }

        private static bool TryWriteNumber(string s, ref int i, StringBuilder sb)
        {
            var start = i;

            if (i < s.Length && s[i] == '-')
                i++;

            var digits = 0;

            while (i < s.Length && char.IsDigit(s[i]))
            {
                i++;
                digits++;
            }

            if (i < s.Length && s[i] == '.')
            {
                i++;
                var fractionDigits = 0;

                while (i < s.Length && char.IsDigit(s[i]))
                {
                    i++;
                    fractionDigits++;
                }

                // "1." is not JSON: a decimal point must be followed by at least one digit.
                if (fractionDigits == 0)
                    return false;
            }

            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;

                if (i < s.Length && (s[i] == '+' || s[i] == '-'))
                    i++;

                var exponentDigits = 0;

                while (i < s.Length && char.IsDigit(s[i]))
                {
                    i++;
                    exponentDigits++;
                }

                if (exponentDigits == 0)
                    return false;
            }

            if (digits == 0)
                return false;

            sb.Append(s, start, i - start);

            return true;
        }

        private static bool SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r'))
                i++;

            return true;
        }

        private static void AppendIndent(StringBuilder sb, int depth)
        {
            for (var d = 0; d < depth; d++)
                sb.Append(Indent);
        }
    }
}
