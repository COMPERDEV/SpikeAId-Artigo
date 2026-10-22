using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PerformanceAdvisor.Artigo
{
    /// <summary>
    /// Strict JSON reader: objects become Dictionary, arrays List, numbers double.
    /// Throws FormatException on anything that is not exactly one valid JSON value.
    /// </summary>
    /// <remarks>
    /// MOTIVO: a métrica "validade de formato" precisa de um leitor que RECUSE o que não é
    /// JSON. O JsonUtility da Unity é tolerante — aceita lixo depois do objeto, não diferencia
    /// null de campo ausente e não diz o tipo de um valor. Para medir violação de formato, o
    /// leitor tem que ser estrito, senão a métrica mede a tolerância do leitor.
    /// </remarks>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("texto nulo");
            int i = 0;
            object v = Value(text, ref i);
            Skip(text, ref i);
            if (i != text.Length) throw new FormatException($"conteúdo depois do JSON na posição {i}");
            return v;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        private static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("fim inesperado");
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (c == 't') { Lit(s, ref i, "true");  return true; }
            if (c == 'f') { Lit(s, ref i, "false"); return false; }
            if (c == 'n') { Lit(s, ref i, "null");  return null; }
            if (c == '-' || (c >= '0' && c <= '9')) return Num(s, ref i);
            throw new FormatException($"caractere inesperado '{c}' na posição {i}");
        }

        private static void Lit(string s, ref int i, string lit)
        {
            if (string.CompareOrdinal(s, i, lit, 0, lit.Length) != 0) throw new FormatException($"literal inválido na posição {i}");
            i += lit.Length;
        }

        private static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; Skip(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                Skip(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException($"chave esperada na posição {i}");
                string k = Str(s, ref i);
                Skip(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException($"':' esperado na posição {i}");
                i++;
                d[k] = Value(s, ref i);
                Skip(s, ref i);
                if (i >= s.Length) throw new FormatException("objeto não fechado");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException($"',' ou '}}' esperado na posição {i}");
            }
        }

        private static List<object> Arr(string s, ref int i)
        {
            var l = new List<object>();
            i++; Skip(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Value(s, ref i));
                Skip(s, ref i);
                if (i >= s.Length) throw new FormatException("lista não fechada");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
                throw new FormatException($"',' ou ']' esperado na posição {i}");
            }
        }

        private static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (i >= s.Length) break;
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
                            if (i + 4 > s.Length) throw new FormatException("escape \\u incompleto");
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: throw new FormatException($"escape inválido \\{e}");
                    }
                }
                else if (c < 0x20) throw new FormatException("caractere de controle dentro de string");
                else sb.Append(c);
            }
            throw new FormatException("string não fechada");
        }

        private static double Num(string s, ref int i)
        {
            int start = i;
            if (s[i] == '-') i++;
            while (i < s.Length && "0123456789.eE+-".IndexOf(s[i]) >= 0) i++;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                throw new FormatException($"número inválido na posição {start}");
            return d;
        }

        /// <summary>Escapes a string for embedding inside a JSON string literal.</summary>
        public static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 16);
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
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
