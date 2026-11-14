// Minimal JSON reader for the exporter's JSON files (objects -> Dictionary<string, object>, arrays -> List<object>,
// numbers -> double, strings, bools, null). Editor only.
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Severny.EditorTools
{
    public static class SevernyJson
    {
        public static object Parse(string s) { int i = 0; var v = Value(s, ref i); return v; }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++;
                while (true)
                {
                    Ws(s, ref i); if (i < s.Length && s[i] == '}') { i++; return d; }
                    string k = Str(s, ref i); Ws(s, ref i); i++; // ':'
                    d[k] = Value(s, ref i); Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                while (true)
                {
                    Ws(s, ref i); if (i < s.Length && s[i] == ']') { i++; return l; }
                    l.Add(Value(s, ref i)); Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    return l;
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && s.Substring(i, 4) == "true") { i += 4; return true; }
            if (s.Length - i >= 5 && s.Substring(i, 5) == "false") { i += 5; return false; }
            if (s.Length - i >= 4 && s.Substring(i, 4) == "null") { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out var num);
            return num;
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder(); i++; // opening quote
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    switch (s[i])
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'u': sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber)); i += 4; break;
                        default: sb.Append(s[i]); break;
                    }
                }
                else sb.Append(s[i]);
                i++;
            }
            i++; // closing quote
            return sb.ToString();
        }

        // helpers
        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
        public static List<object> Arr(object o) => o as List<object>;
        public static string S(Dictionary<string, object> d, string k, string def = null) => d != null && d.TryGetValue(k, out var v) && v is string s ? s : def;
        public static bool B(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v is bool b && b;
        public static double N(Dictionary<string, object> d, string k, double def = 0) => d != null && d.TryGetValue(k, out var v) && v is double x ? x : def;
        public static float[] F3(Dictionary<string, object> d, string k)
        {
            if (d == null || !d.TryGetValue(k, out var v) || !(v is List<object> l) || l.Count < 3) return null;
            return new[] { (float)(double)l[0], (float)(double)l[1], (float)(double)l[2] };
        }
    }
}
