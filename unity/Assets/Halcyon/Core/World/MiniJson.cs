// A small JSON reader for the island's meta.json: objects become
// Dictionary<string, object>, arrays List<object>, numbers double, plus
// string, bool and null. Enough for data files; not a general serializer.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Halcyon.Core
{
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            var p = new Parser(json);
            p.SkipWs();
            object v = p.Value();
            p.SkipWs();
            if (!p.AtEnd) throw p.Error("trailing characters");
            return v;
        }

        sealed class Parser
        {
            readonly string s;
            int i;

            public Parser(string s) { this.s = s; }
            public bool AtEnd => i >= s.Length;

            public FormatException Error(string what) => new FormatException($"JSON: {what} at {i}");

            public void SkipWs()
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++;
            }

            public object Value()
            {
                if (i >= s.Length) throw Error("unexpected end");
                char c = s[i];
                switch (c)
                {
                    case '{': return Obj();
                    case '[': return Arr();
                    case '"': return Str();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return Num();
                        throw Error($"unexpected '{c}'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error($"expected {word}");
                i += word.Length;
            }

            Dictionary<string, object> Obj()
            {
                var d = new Dictionary<string, object>();
                i++;
                SkipWs();
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (i >= s.Length || s[i] != '"') throw Error("expected key");
                    string k = Str();
                    SkipWs();
                    if (i >= s.Length || s[i] != ':') throw Error("expected ':'");
                    i++;
                    SkipWs();
                    d[k] = Value();
                    SkipWs();
                    if (i >= s.Length) throw Error("unexpected end in object");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw Error("expected ',' or '}'");
                }
            }

            List<object> Arr()
            {
                var a = new List<object>();
                i++;
                SkipWs();
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    SkipWs();
                    a.Add(Value());
                    SkipWs();
                    if (i >= s.Length) throw Error("unexpected end in array");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return a; }
                    throw Error("expected ',' or ']'");
                }
            }

            string Str()
            {
                i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw Error("unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) throw Error("bad escape");
                    char e = s[i++];
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
                            if (i + 4 > s.Length) throw Error("bad unicode escape");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw Error("bad escape");
                    }
                }
            }

            double Num()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length)
                {
                    char c = s[i];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') i++;
                    else break;
                }
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Typed accessors over MiniJson values.</summary>
    public static class Json
    {
        public static Dictionary<string, object> Obj(object o) => (Dictionary<string, object>)o;
        public static List<object> Arr(object o) => (List<object>)o;
        public static Dictionary<string, object> Obj(this Dictionary<string, object> o, string key) => (Dictionary<string, object>)o[key];
        public static List<object> Arr(this Dictionary<string, object> o, string key) => (List<object>)o[key];
        public static double Num(this Dictionary<string, object> o, string key) => Convert.ToDouble(o[key], CultureInfo.InvariantCulture);
        public static double Num(this Dictionary<string, object> o, string key, double fallback) =>
            o.TryGetValue(key, out var v) && v != null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : fallback;
        public static int Int(this Dictionary<string, object> o, string key) => (int)Num(o, key);
        public static string Str(this Dictionary<string, object> o, string key) => (string)o[key];
        public static bool Bool(this Dictionary<string, object> o, string key) => o.TryGetValue(key, out var v) && v is bool b && b;
        public static double Num(object o) => Convert.ToDouble(o, CultureInfo.InvariantCulture);
    }
}
