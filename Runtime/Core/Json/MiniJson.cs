using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NineDTech.AdsKit.Json
{
    /// <summary>
    /// Small dependency-free JSON reader/writer. Objects become <c>Dictionary&lt;string, object&gt;</c>,
    /// arrays <c>List&lt;object&gt;</c>, numbers <c>double</c>, plus string / bool / null.
    /// Used for Remote Config payloads whose keys are data (placement ids), which JsonUtility cannot read.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var parser = new Parser(json);
            var value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw new FormatException("Unexpected trailing characters at " + parser.Position);
            return value;
        }

        /// <summary>Parse, returning null (and the error) instead of throwing.</summary>
        public static Dictionary<string, object> TryParseObject(string json, out string error)
        {
            error = null;
            try
            {
                if (Parse(json) is Dictionary<string, object> obj) return obj;
                error = "Root is not a JSON object";
            }
            catch (Exception e) { error = e.Message; }
            return null;
        }

        public static string Serialize(object value, bool pretty = true)
        {
            var sb = new StringBuilder();
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        // ----- typed readers -----

        public static Dictionary<string, object> GetObject(this Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        public static List<object> GetArray(this Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out var v) ? v as List<object> : null;

        public static void Read(this Dictionary<string, object> obj, string key, ref bool target)
        {
            if (obj != null && obj.TryGetValue(key, out var v) && v is bool b) target = b;
        }

        public static void Read(this Dictionary<string, object> obj, string key, ref int target)
        {
            if (obj != null && obj.TryGetValue(key, out var v) && v is double d) target = (int)Math.Round(d);
        }

        public static void Read(this Dictionary<string, object> obj, string key, ref float target)
        {
            if (obj != null && obj.TryGetValue(key, out var v) && v is double d) target = (float)d;
        }

        public static void Read(this Dictionary<string, object> obj, string key, ref string target)
        {
            if (obj != null && obj.TryGetValue(key, out var v) && v is string s) target = s;
        }

        public static List<string> ReadStringList(this Dictionary<string, object> obj, string key)
        {
            var arr = obj.GetArray(key);
            if (arr == null) return null;
            var list = new List<string>(arr.Count);
            foreach (var item in arr)
                if (item is string s) list.Add(s);
            return list;
        }

        public static List<double> ReadNumberList(this Dictionary<string, object> obj, string key)
        {
            var arr = obj.GetArray(key);
            if (arr == null) return null;
            var list = new List<double>(arr.Count);
            foreach (var item in arr)
                if (item is double d) list.Add(d);
            return list;
        }

        // ----- writer -----

        private static void Write(StringBuilder sb, object value, bool pretty, int indent)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case IDictionary<string, object> dict:
                {
                    if (dict.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    var first = true;
                    foreach (var pair in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, pretty, indent + 1);
                        WriteString(sb, pair.Key);
                        sb.Append(pretty ? ": " : ":");
                        Write(sb, pair.Value, pretty, indent + 1);
                    }
                    NewLine(sb, pretty, indent);
                    sb.Append('}');
                    break;
                }
                case System.Collections.IList list:
                {
                    if (list.Count == 0) { sb.Append("[]"); break; }
                    var simple = true;
                    foreach (var item in list)
                        if (item is IDictionary<string, object> || item is System.Collections.IList) { simple = false; break; }
                    sb.Append('[');
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (i > 0) sb.Append(simple && pretty ? ", " : ",");
                        if (!simple) NewLine(sb, pretty, indent + 1);
                        Write(sb, list[i], pretty, indent + 1);
                    }
                    if (!simple) NewLine(sb, pretty, indent);
                    sb.Append(']');
                    break;
                }
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case IFormattable n: sb.Append(n.ToString(null, CultureInfo.InvariantCulture)); break;
                default: WriteString(sb, value.ToString()); break;
            }
        }

        private static void NewLine(StringBuilder sb, bool pretty, int indent)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', indent * 2);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ----- parser -----

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s; }

            public int Position => _i;
            public bool AtEnd => _i >= _s.Length;

            public void SkipWhitespace()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new FormatException("Unexpected end of JSON");
                var c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw new FormatException("Unexpected character '" + c + "' at " + _i);
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var obj = new Dictionary<string, object>();
                _i++; // {
                SkipWhitespace();
                if (Peek('}')) { _i++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    if (!Peek('"')) throw new FormatException("Expected property name at " + _i);
                    var key = ParseString();
                    SkipWhitespace();
                    if (!Peek(':')) throw new FormatException("Expected ':' at " + _i);
                    _i++;
                    obj[key] = ParseValue();
                    SkipWhitespace();
                    if (Peek(',')) { _i++; continue; }
                    if (Peek('}')) { _i++; return obj; }
                    throw new FormatException("Expected ',' or '}' at " + _i);
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                _i++; // [
                SkipWhitespace();
                if (Peek(']')) { _i++; return list; }
                while (true)
                {
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (Peek(',')) { _i++; continue; }
                    if (Peek(']')) { _i++; return list; }
                    throw new FormatException("Expected ',' or ']' at " + _i);
                }
            }

            private string ParseString()
            {
                var sb = new StringBuilder();
                _i++; // opening quote
                while (_i < _s.Length)
                {
                    var c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (_i >= _s.Length) break;
                    var e = _s[_i++];
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
                            if (_i + 4 > _s.Length) throw new FormatException("Bad \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw new FormatException("Bad escape \\" + e);
                    }
                }
                throw new FormatException("Unterminated string");
            }

            private double ParseNumber()
            {
                var start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                return double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private void Expect(string literal)
            {
                if (string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0)
                    throw new FormatException("Expected '" + literal + "' at " + _i);
                _i += literal.Length;
            }

            private bool Peek(char c) => _i < _s.Length && _s[_i] == c;
        }
    }
}
