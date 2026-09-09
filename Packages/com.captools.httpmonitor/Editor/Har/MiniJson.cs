using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// The little JSON we need for HAR, without a package dependency. Parses into plain objects
    /// (<c>Dictionary&lt;string, object&gt;</c>, <c>List&lt;object&gt;</c>, string, double, bool, null)
    /// and writes escaped strings. Not a general-purpose library: no comments, no trailing commas.
    /// </summary>
    internal static class MiniJson
    {
        // ---------------------------------------------------------------- writing

        public static void WriteString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("null");

                return;
            }

            sb.Append('"');

            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);

                        break;
                }
            }

            sb.Append('"');
        }

        public static void WriteNumber(StringBuilder sb, double value)
        {
            sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        // ---------------------------------------------------------------- reading

        /// <exception cref="FormatException">Malformed JSON, with the offset in the message.</exception>
        public static object Parse(string json)
        {
            if (json == null)
                throw new FormatException("JSON text is null");

            var i = 0;
            var value = ParseValue(json, ref i);
            SkipWhitespace(json, ref i);

            if (i != json.Length)
                throw Error(json, i, "unexpected trailing content");

            return value;
        }

        /// <summary>Typed lookups over the parsed shape; every one returns the default instead of throwing.</summary>
        public static Dictionary<string, object> AsObject(object value) => value as Dictionary<string, object>;

        public static List<object> AsArray(object value) => value as List<object>;

        public static Dictionary<string, object> GetObject(Dictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;
        }

        public static List<object> GetArray(Dictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var value) ? value as List<object> : null;
        }

        public static string GetString(Dictionary<string, object> obj, string key, string fallback = null)
        {
            return obj != null && obj.TryGetValue(key, out var value) && value is string s ? s : fallback;
        }

        public static double GetNumber(Dictionary<string, object> obj, string key, double fallback = 0)
        {
            return obj != null && obj.TryGetValue(key, out var value) && value is double d ? d : fallback;
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback = false)
        {
            return obj != null && obj.TryGetValue(key, out var value) && value is bool b ? b : fallback;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);

            if (i >= s.Length)
                throw Error(s, i, "unexpected end");

            switch (s[i])
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': return ParseLiteral(s, ref i, "true", true);
                case 'f': return ParseLiteral(s, ref i, "false", false);
                case 'n': return ParseLiteral(s, ref i, "null", null);
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var result = new Dictionary<string, object>();
            i++; // {
            SkipWhitespace(s, ref i);

            if (i < s.Length && s[i] == '}')
            {
                i++;

                return result;
            }

            while (true)
            {
                SkipWhitespace(s, ref i);

                if (i >= s.Length || s[i] != '"')
                    throw Error(s, i, "expected a property name");

                var key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);

                if (i >= s.Length || s[i] != ':')
                    throw Error(s, i, "expected ':'");

                i++;
                result[key] = ParseValue(s, ref i);
                SkipWhitespace(s, ref i);

                if (i >= s.Length)
                    throw Error(s, i, "unterminated object");

                if (s[i] == ',')
                {
                    i++;

                    continue;
                }

                if (s[i] == '}')
                {
                    i++;

                    return result;
                }

                throw Error(s, i, "expected ',' or '}'");
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var result = new List<object>();
            i++; // [
            SkipWhitespace(s, ref i);

            if (i < s.Length && s[i] == ']')
            {
                i++;

                return result;
            }

            while (true)
            {
                result.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);

                if (i >= s.Length)
                    throw Error(s, i, "unterminated array");

                if (s[i] == ',')
                {
                    i++;

                    continue;
                }

                if (s[i] == ']')
                {
                    i++;

                    return result;
                }

                throw Error(s, i, "expected ',' or ']'");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote

            while (i < s.Length)
            {
                var c = s[i++];

                if (c == '"')
                    return sb.ToString();

                if (c != '\\')
                {
                    sb.Append(c);

                    continue;
                }

                if (i >= s.Length)
                    break;

                var e = s[i++];

                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            throw Error(s, i, "bad \\u escape");

                        sb.Append((char)code);
                        i += 4;

                        break;
                    default:
                        throw Error(s, i - 1, "bad escape");
                }
            }

            throw Error(s, i, "unterminated string");
        }

        private static object ParseLiteral(string s, ref int i, string literal, object value)
        {
            if (string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                throw Error(s, i, "unexpected token");

            i += literal.Length;

            return value;
        }

        private static object ParseNumber(string s, ref int i)
        {
            var start = i;

            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E'))
                i++;

            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw Error(s, start, "bad number");

            return value;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r'))
                i++;
        }

        private static FormatException Error(string s, int i, string message)
        {
            return new FormatException($"Invalid JSON at offset {Math.Min(i, s.Length)}: {message}");
        }
    }
}
