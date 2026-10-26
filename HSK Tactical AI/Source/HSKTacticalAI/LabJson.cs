#if RW16
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace HSKTacticalAI
{
    /// <summary>
    /// AI Lab only, RimWorld 1.6 build: .NET Framework 4.7.2 has no System.Text.Json. Reads the Lab config (a flat object
    /// with string, number and bool fields and two dictionaries) and writes the results (dictionaries, lists, numbers,
    /// strings) - all the Lab needs.
    /// </summary>
    public static class LabJson
    {
        // ---------------- write ----------------

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, int n) { sb.Append('\n'); sb.Append(' ', n * 2); }

        private static void WriteValue(StringBuilder sb, object v, int depth)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case char c: WriteString(sb, c.ToString()); return;
                case Enum e: sb.Append(System.Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); return;
                case float f: WriteNumber(sb, f); return;
                case double d: WriteNumber(sb, d); return;
                case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture)); return;
                case IDictionary dict:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Indent(sb, depth + 1);
                        WriteString(sb, System.Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                        sb.Append(": ");
                        WriteValue(sb, kv.Value, depth + 1);
                    }
                    if (!first) Indent(sb, depth);
                    sb.Append('}');
                    return;
                }
                case IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (object o in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Indent(sb, depth + 1);
                        WriteValue(sb, o, depth + 1);
                    }
                    if (!first) Indent(sb, depth);
                    sb.Append(']');
                    return;
                }
            }
            if (v is IConvertible conv && v.GetType().IsPrimitive) { sb.Append(conv.ToString(CultureInfo.InvariantCulture)); return; }
            WriteString(sb, v.ToString());
        }

        private static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == '<' || c == '>' || c == '&' || c == '+' || c == '\'') sb.Append("\\u").Append(((int)c).ToString("X4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------- read ----------------

        /// <summary>The Lab config: JSON object fields onto LabConfig fields, names case-insensitive.</summary>
        public static LabConfig ReadConfig(string json)
        {
            int i = 0;
            var obj = Parse(json, ref i) as Dictionary<string, object>;
            var cfg = new LabConfig();
            if (obj == null) return cfg;
            foreach (var kv in obj)
            {
                FieldInfo f = typeof(LabConfig).GetField(kv.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (f == null || kv.Value == null) continue;
                try { f.SetValue(cfg, ConvertTo(kv.Value, f.FieldType)); }
                catch { }
            }
            return cfg;
        }

        private static object ConvertTo(object v, Type t)
        {
            if (t == typeof(string)) return v as string ?? System.Convert.ToString(v, CultureInfo.InvariantCulture);
            if (t == typeof(int)) return System.Convert.ToInt32(v, CultureInfo.InvariantCulture);
            if (t == typeof(float)) return System.Convert.ToSingle(v, CultureInfo.InvariantCulture);
            if (t == typeof(double)) return System.Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (t == typeof(bool)) return v is bool b ? b : bool.Parse(System.Convert.ToString(v, CultureInfo.InvariantCulture));
            if (t == typeof(Dictionary<string, string>) && v is Dictionary<string, object> ds)
            {
                var d = new Dictionary<string, string>();
                foreach (var kv in ds) d[kv.Key] = System.Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                return d;
            }
            if (t == typeof(Dictionary<string, float>) && v is Dictionary<string, object> df)
            {
                var d = new Dictionary<string, float>();
                foreach (var kv in df) d[kv.Key] = System.Convert.ToSingle(kv.Value, CultureInfo.InvariantCulture);
                return d;
            }
            return null;
        }

        private static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static object Parse(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '﻿') { i++; return Parse(s, ref i); }
            if (c == '{')
            {
                var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                i++;
                Skip(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (i < s.Length)
                {
                    Skip(s, ref i);
                    string key = (string)Parse(s, ref i);
                    Skip(s, ref i);
                    i++;   // ':'
                    d[key] = Parse(s, ref i);
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    i++;   // '}'
                    break;
                }
                return d;
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++;
                Skip(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (i < s.Length)
                {
                    l.Add(Parse(s, ref i));
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    i++;   // ']'
                    break;
                }
                return l;
            }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        char e = s[++i];
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u': sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber)); i += 4; break;
                            default: sb.Append(e); break;
                        }
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                i++;
                return sb.ToString();
            }
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }
    }
}
#endif
