// Reading the reference JSON written by tools/dev/vehicle-ref.ts.
//
// Numbers are JavaScript's shortest round-trip repr, which .NET parses back to
// the identical double; NaN, the infinities and -0 arrive as strings. The Core
// types keep the TypeScript field names, so JSON objects map onto them by name
// through reflection, both to compare (Tree) and to construct (Build).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Halcyon.Core.Tests
{
    static class RefJson
    {
        public static JsonElement Load(string path)
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllBytes(path), new JsonDocumentOptions { MaxDepth = 256 });
            return doc.RootElement.Clone();
        }

        public static double D(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Number:
                    return double.Parse(e.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
                case JsonValueKind.String:
                    switch (e.GetString())
                    {
                        case "NaN": return double.NaN;
                        case "Infinity": return double.PositiveInfinity;
                        case "-Infinity": return double.NegativeInfinity;
                        case "-0": return -0.0;
                    }
                    break;
            }
            throw new FormatException($"not a number: {e.GetRawText()}");
        }

        public static bool IsNumber(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Number) return true;
            if (e.ValueKind != JsonValueKind.String) return false;
            string s = e.GetString();
            return s == "NaN" || s == "Infinity" || s == "-Infinity" || s == "-0";
        }

        public static double D(JsonElement obj, string name) => D(obj.GetProperty(name));
        public static string S(JsonElement obj, string name) => obj.GetProperty(name).GetString();

        public static bool TryGet(JsonElement obj, string name, out JsonElement value)
        {
            value = default;
            return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null;
        }

        /// <summary>The C# member for a TypeScript field: same name, or the same ignoring case (params -> Params).</summary>
        public static MemberInfo FindMember(Type t, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            MemberInfo m = t.GetField(name, flags) ?? (MemberInfo)t.GetProperty(name, flags);
            if (m != null) return m;
            foreach (FieldInfo f in t.GetFields(flags)) if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
            foreach (PropertyInfo p in t.GetProperties(flags)) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.GetIndexParameters().Length == 0) return p;
            return null;
        }

        static object GetValue(MemberInfo m, object o) => m is FieldInfo f ? f.GetValue(o) : ((PropertyInfo)m).GetValue(o);
        static Type MemberType(MemberInfo m) => m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType;

        static string EnumName(object e) => e.ToString().ToLowerInvariant();

        /// <summary>
        /// Compare a JSON value with a C# object graph, member by member. With
        /// strict, C# fields absent from a JSON object must be null (TypeScript
        /// optional fields that are not set).
        /// </summary>
        public static void Tree(Check c, string path, JsonElement j, object o, bool strict = false)
        {
            switch (j.ValueKind)
            {
                case JsonValueKind.Null:
                    if (o != null) c.Fail($"{path}: expected null, got {o}");
                    else c.Str("null", "null", path);
                    return;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    if (o is bool b) c.Bool(j.GetBoolean(), b, path);
                    else c.Fail($"{path}: expected a bool, got {o?.GetType().Name ?? "null"}");
                    return;
                case JsonValueKind.Number:
                case JsonValueKind.String:
                    if (o is double || o is int || o is float || o is long || o is uint)
                    {
                        if (IsNumber(j)) c.Num(D(j), Convert.ToDouble(o, CultureInfo.InvariantCulture), path);
                        else c.Fail($"{path}: expected {j.GetRawText()}, got the number {o}");
                    }
                    else if (o is string s) c.Str(j.GetString(), s, path);
                    else if (o is Enum) c.Str(j.GetString().ToLowerInvariant(), EnumName(o), path);
                    else c.Fail($"{path}: expected {j.GetRawText()}, got {o?.GetType().Name ?? "null"}");
                    return;
                case JsonValueKind.Array:
                {
                    int n = j.GetArrayLength();
                    if (o is Array arr)
                    {
                        if (arr.Length != n)
                        {
                            c.Fail($"{path}: expected {n} elements, got {arr.Length}");
                            return;
                        }
                        int i = 0;
                        foreach (JsonElement e in j.EnumerateArray()) Tree(c, $"{path}[{i}]", e, arr.GetValue(i++), strict);
                    }
                    else if (o is ITuple tup)
                    {
                        if (tup.Length != n)
                        {
                            c.Fail($"{path}: expected {n} tuple items, got {tup.Length}");
                            return;
                        }
                        int i = 0;
                        foreach (JsonElement e in j.EnumerateArray()) Tree(c, $"{path}[{i}]", e, tup[i++], strict);
                    }
                    else c.Fail($"{path}: expected an array, got {o?.GetType().Name ?? "null"}");
                    return;
                }
                case JsonValueKind.Object:
                {
                    if (o == null)
                    {
                        c.Fail($"{path}: expected an object, got null");
                        return;
                    }
                    Type t = o.GetType();
                    if (o is IDictionary<string, double> dict)
                    {
                        foreach (JsonProperty p in j.EnumerateObject())
                        {
                            if (dict.TryGetValue(p.Name, out double v)) c.Num(D(p.Value), v, $"{path}.{p.Name}");
                            else c.Fail($"{path}.{p.Name}: missing");
                        }
                        return;
                    }
                    var seen = new HashSet<string>();
                    foreach (JsonProperty p in j.EnumerateObject())
                    {
                        MemberInfo m = FindMember(t, p.Name);
                        if (m == null)
                        {
                            c.Fail($"{path}.{p.Name}: no such field on {t.Name}");
                            continue;
                        }
                        seen.Add(m.Name);
                        Tree(c, $"{path}.{p.Name}", p.Value, GetValue(m, o), strict);
                    }
                    if (strict)
                    {
                        foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (seen.Contains(f.Name)) continue;
                            object v = f.GetValue(o);
                            if (v != null) c.Fail($"{path}.{f.Name}: not set in the TypeScript, but {v} in C#");
                        }
                    }
                    return;
                }
            }
            c.Fail($"{path}: unexpected JSON {j.ValueKind}");
        }

        /// <summary>Construct a C# value of type t from JSON (CarSpec, Tune, Upgrades, Aids, PadSettings...).</summary>
        public static object Build(Type t, JsonElement j)
        {
            if (j.ValueKind == JsonValueKind.Null) return null;
            Type under = Nullable.GetUnderlyingType(t);
            if (under != null) return Build(under, j);
            if (t == typeof(double)) return D(j);
            if (t == typeof(int)) return checked((int)D(j));
            if (t == typeof(bool)) return j.GetBoolean();
            if (t == typeof(string)) return j.GetString();
            if (t.IsEnum) return Enum.Parse(t, j.GetString(), true);
            if (t.IsArray)
            {
                Type et = t.GetElementType();
                var arr = Array.CreateInstance(et, j.GetArrayLength());
                int i = 0;
                foreach (JsonElement e in j.EnumerateArray()) arr.SetValue(Build(et, e), i++);
                return arr;
            }
            if (t == typeof(ValueTuple<double, double>)) return (D(j[0]), D(j[1]));
            if (t == typeof(Dictionary<string, double>))
            {
                var d = new Dictionary<string, double>();
                foreach (JsonProperty p in j.EnumerateObject()) d[p.Name] = D(p.Value);
                return d;
            }
            object o = Activator.CreateInstance(t);
            foreach (JsonProperty p in j.EnumerateObject())
            {
                MemberInfo m = FindMember(t, p.Name);
                if (m == null) throw new FormatException($"{t.Name} has no field {p.Name}");
                object v = Build(MemberType(m), p.Value);
                if (m is FieldInfo f) f.SetValue(o, v);
                else ((PropertyInfo)m).SetValue(o, v);
            }
            return o;
        }

        public static T Build<T>(JsonElement j) => (T)Build(typeof(T), j);
    }
}
