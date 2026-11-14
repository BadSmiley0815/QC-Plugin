// Kleiner JSON-Leser ohne Fremdbibliothek (das Plugin soll im QuadClient/Experten keine weiteren DLLs brauchen).
// Ergebnis: Dictionary<string, object>, List<object>, double, string, bool oder null.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace QcWetter
{
    public static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            object v = Value(text, ref i);
            Ws(text, ref i);
            if (i != text.Length) throw new FormatException("JSON: Zeichen nach dem Ende");
            return v;
        }

        /// <summary>Wert über einen Pfad holen, z. B. Get(root, "daily", "time"). null, wenn es ihn nicht gibt.</summary>
        public static object Get(object node, params string[] path)
        {
            foreach (string key in path)
            {
                Dictionary<string, object> d = node as Dictionary<string, object>;
                if (d == null || !d.TryGetValue(key, out node)) return null;
            }
            return node;
        }

        public static double? Number(object v)
        {
            if (v is double) return (double)v;
            return null;
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON: unerwartetes Ende");
            char c = s[i];
            if (c == '{')
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    string key = Str(s, ref i);
                    Ws(s, ref i);
                    if (s[i] != ':') throw new FormatException("JSON: ':' erwartet");
                    i++;
                    d[key] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("JSON: ',' oder '}' erwartet");
                }
            }
            if (c == '[')
            {
                List<object> l = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("JSON: ',' oder ']' erwartet");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == st) throw new FormatException("JSON: unerwartetes Zeichen '" + c + "'");
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("JSON: Zeichenkette erwartet");
            i++;
            StringBuilder b = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON: Zeichenkette nicht beendet");
                char c = s[i++];
                if (c == '"') return b.ToString();
                if (c != '\\') { b.Append(c); continue; }
                c = s[i++];
                switch (c)
                {
                    case 'n': b.Append('\n'); break;
                    case 't': b.Append('\t'); break;
                    case 'r': b.Append('\r'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'u':
                        b.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: b.Append(c); break;
                }
            }
        }
    }
}
